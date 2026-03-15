using Microsoft.UI.Xaml;
using Microsoft.VisualBasic.FileIO;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Core;
using Windows.Media.Playback;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Graphics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using System.Text.Json;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace main
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public ObservableCollection<FileSystemItem> FilesAndFolders { get; } = new ObservableCollection<FileSystemItem>();
        private MediaPlayer _previewPlayer;
        private string? _currentlyPlayingPath;
        private Button? _currentlyPlayingButton;

        private MediaPlayerElement? _currentVideoPlayerElement;
        private string? _currentVideoPlayingPath;
        private Button? _currentVideoPlayingButton;
        private string? _currentModelDirectory;
        private string? _currentDirectoryPath;

        private AppWindow? _appWindow;
        private RectInt32 _lastNormalBounds;
        private string _settingsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "multimedia-explorer", "window_settings.json");

        // サムネイル生成用
        private Queue<string> _thumbnailQueue = new Queue<string>();
        private bool _isGeneratingThumbnail = false;
        private string? _currentThumbnailDirectory;

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

        public MainWindow()
        {
            InitializeComponent();
            
            // アプリケーションウィンドウのアイコンを設定
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            
            // 実行ファイル自体からアイコンを抽出してタイトルバーに適用する
            string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath != null)
            {
                IntPtr hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
                if (hIcon != IntPtr.Zero)
                {
                    IconId iconId = Win32Interop.GetIconIdFromIcon(hIcon);
                    _appWindow.SetIcon(iconId);
                }
            }

            _appWindow.Changed += AppWindow_Changed;
            _appWindow.Closing += AppWindow_Closing;

            FileSystemGridView.ItemsSource = FilesAndFolders;
            _previewPlayer = new MediaPlayer();
            _previewPlayer.MediaEnded += _previewPlayer_MediaEnded;

            InitializeFolderTree();
            InitializeWebViewAsync();

            // ウィンドウ状態の復元
            RestoreWindowState();
        }

        private async void InitializeWebViewAsync()
        {
            try
            {
                string userDataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "multimedia-explorer", "WebView2");

                var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, new CoreWebView2EnvironmentOptions());

                await ModelWebView.EnsureCoreWebView2Async(env);
                await ThumbnailWebView.EnsureCoreWebView2Async(env);
                
                string assetsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets");
                ModelWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "appassets.local",
                    assetsPath,
                    CoreWebView2HostResourceAccessKind.Allow
                );
                ThumbnailWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "appassets.local",
                    assetsPath,
                    CoreWebView2HostResourceAccessKind.Allow
                );

                ModelWebView.CoreWebView2.AddWebResourceRequestedFilter("http://models.local/*", CoreWebView2WebResourceContext.All);
                ModelWebView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

                ThumbnailWebView.CoreWebView2.AddWebResourceRequestedFilter("http://models.local/*", CoreWebView2WebResourceContext.All);
                ThumbnailWebView.CoreWebView2.WebResourceRequested += ThumbnailWebView_WebResourceRequested;
                ThumbnailWebView.CoreWebView2.WebMessageReceived += ThumbnailWebView_WebMessageReceived;

                ModelWebView.Source = new Uri("http://appassets.local/3d_viewer.html");
                ThumbnailWebView.Source = new Uri("http://appassets.local/3d_viewer.html");
            }
            catch (Exception ex)
            {
                SelectedDirectoryTextBlock.Text = $"WebView2 初期化エラー: {ex.Message}";
            }
        }

        private async void CoreWebView2_WebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            string uriPattern = "http://models.local/";
            if (args.Request.Uri.StartsWith(uriPattern, StringComparison.OrdinalIgnoreCase))
            {
                var deferral = args.GetDeferral();
                try
                {
                    string relativePath = args.Request.Uri.Substring(uriPattern.Length);
                    relativePath = Uri.UnescapeDataString(relativePath);
                    
                    int queryIndex = relativePath.IndexOf('?');
                    if (queryIndex > 0)
                    {
                        relativePath = relativePath.Substring(0, queryIndex);
                    }
                    
                    relativePath = relativePath.Replace('/', '\\');

                    if (!string.IsNullOrEmpty(_currentModelDirectory))
                    {
                        string fullPath = System.IO.Path.Combine(_currentModelDirectory, relativePath);
                        if (System.IO.File.Exists(fullPath))
                        {
                            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(fullPath);
                            var stream = await file.OpenReadAsync();
                            string extension = System.IO.Path.GetExtension(fullPath).ToLowerInvariant();
                            string contentType = "application/octet-stream";
                            if (extension == ".png") contentType = "image/png";
                            else if (extension == ".jpg" || extension == ".jpeg") contentType = "image/jpeg";
                            else if (extension == ".glb") contentType = "model/gltf-binary";
                            else if (extension == ".gltf") contentType = "model/gltf+json";
                            else if (extension == ".obj") contentType = "text/plain";
                            else if (extension == ".mtl") contentType = "text/plain";
                            else if (extension == ".fbx") contentType = "application/octet-stream";
                            
                            var response = sender.Environment.CreateWebResourceResponse(
                                stream,
                                200,
                                "OK",
                                $"Access-Control-Allow-Origin: *\nContent-Type: {contentType}"
                            );
                            args.Response = response;
                            return;
                        }
                    }
                    
                    args.Response = sender.Environment.CreateWebResourceResponse(null, 404, "Not Found", "Access-Control-Allow-Origin: *");
                }
                catch (Exception)
                {
                    args.Response = sender.Environment.CreateWebResourceResponse(null, 500, "Internal Server Error", "Access-Control-Allow-Origin: *");
                }
                finally
                {
                    deferral.Complete();
                }
            }
        }

        private async void ThumbnailWebView_WebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            string uriPattern = "http://models.local/";
            if (args.Request.Uri.StartsWith(uriPattern, StringComparison.OrdinalIgnoreCase))
            {
                var deferral = args.GetDeferral();
                try
                {
                    string relativePath = args.Request.Uri.Substring(uriPattern.Length);
                    relativePath = Uri.UnescapeDataString(relativePath);
                    
                    int queryIndex = relativePath.IndexOf('?');
                    if (queryIndex > 0)
                    {
                        relativePath = relativePath.Substring(0, queryIndex);
                    }
                    
                    relativePath = relativePath.Replace('/', '\\');

                    if (!string.IsNullOrEmpty(_currentThumbnailDirectory))
                    {
                        string fullPath = System.IO.Path.Combine(_currentThumbnailDirectory, relativePath);
                        if (System.IO.File.Exists(fullPath))
                        {
                            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(fullPath);
                            var stream = await file.OpenReadAsync();
                            string extension = System.IO.Path.GetExtension(fullPath).ToLowerInvariant();
                            string contentType = "application/octet-stream";
                            if (extension == ".png") contentType = "image/png";
                            else if (extension == ".jpg" || extension == ".jpeg") contentType = "image/jpeg";
                            else if (extension == ".glb") contentType = "model/gltf-binary";
                            else if (extension == ".gltf") contentType = "model/gltf+json";
                            else if (extension == ".obj") contentType = "text/plain";
                            else if (extension == ".mtl") contentType = "text/plain";
                            else if (extension == ".fbx") contentType = "application/octet-stream";
                            
                            var response = sender.Environment.CreateWebResourceResponse(
                                stream,
                                200,
                                "OK",
                                $"Access-Control-Allow-Origin: *\nContent-Type: {contentType}"
                            );
                            args.Response = response;
                            return;
                        }
                    }
                    
                    args.Response = sender.Environment.CreateWebResourceResponse(null, 404, "Not Found", "Access-Control-Allow-Origin: *");
                }
                catch (Exception)
                {
                    args.Response = sender.Environment.CreateWebResourceResponse(null, 500, "Internal Server Error", "Access-Control-Allow-Origin: *");
                }
                finally
                {
                    deferral.Complete();
                }
            }
        }

        private async void ThumbnailWebView_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                string json = args.WebMessageAsJson;
                var parsed = System.Text.Json.JsonDocument.Parse(json).RootElement;
                if (parsed.TryGetProperty("action", out var actionElement) && actionElement.GetString() == "thumbnail_result")
                {
                    if (parsed.TryGetProperty("data", out var dataElement) && parsed.TryGetProperty("url", out var urlElement))
                    {
                        string dataUrl = dataElement.GetString() ?? "";
                        string virtualUrl = urlElement.GetString() ?? "";

                        if (dataUrl.StartsWith("data:image/png;base64,"))
                        {
                            string base64 = dataUrl.Substring("data:image/png;base64,".Length);
                            byte[] bytes = Convert.FromBase64String(base64);

                            using (var stream = new InMemoryRandomAccessStream())
                            {
                                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                                {
                                    writer.WriteBytes(bytes);
                                    await writer.StoreAsync();
                                }
                                
                                var bitmapImage = new BitmapImage();
                                await bitmapImage.SetSourceAsync(stream);

                                // Find the corresponding FileSystemItem
                                string fileName = Uri.UnescapeDataString(virtualUrl.Substring("http://models.local/".Length));
                                if (!string.IsNullOrEmpty(_currentThumbnailDirectory))
                                {
                                    string fullPath = System.IO.Path.Combine(_currentThumbnailDirectory, fileName);
                                    var item = FilesAndFolders.FirstOrDefault(f => f.Path == fullPath);
                                    if (item != null)
                                    {
                                        item.ModelThumbnail = bitmapImage;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Thumbnail generation error: {ex.Message}");
            }
            finally
            {
                _isGeneratingThumbnail = false;
                ProcessThumbnailQueue(); // Process next in queue
            }
        }

        private void InitializeFolderTree()
        {
            try
            {
                // クイックアクセスノード
                var quickAccessNode = new Microsoft.UI.Xaml.Controls.TreeViewNode()
                {
                    Content = new ExplorerItem { Name = "クイックアクセス", Path = "", Icon = "\uE83F" }, // Star icon
                    IsExpanded = true
                };

                // PCノード
                var pcNode = new Microsoft.UI.Xaml.Controls.TreeViewNode()
                {
                    Content = new ExplorerItem { Name = "PC", Path = "", Icon = "\uE7F4" }, // Monitor icon
                    IsExpanded = true
                };

                // 特殊フォルダを追加
                AddSpecialFolder(quickAccessNode, "デスクトップ", Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "\uE8B7"); 
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                AddSpecialFolder(quickAccessNode, "ダウンロード", System.IO.Path.Combine(userProfile, "Downloads"), "\uE896"); // Download icon
                AddSpecialFolder(quickAccessNode, "ドキュメント", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "\uE8A5"); // Document icon
                AddSpecialFolder(quickAccessNode, "ピクチャ", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "\uE8B9"); // Picture icon
                AddSpecialFolder(quickAccessNode, "ビデオ", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "\uE8B2"); // Video icon
                AddSpecialFolder(quickAccessNode, "ミュージック", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "\uE8D6"); // Music icon

                // ドライブを追加
                foreach (var drive in System.IO.DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        var node = new Microsoft.UI.Xaml.Controls.TreeViewNode()
                        {
                            Content = new ExplorerItem { Name = drive.Name, Path = drive.RootDirectory.FullName, Icon = "\uEDA2" }, // HardDrive icon
                            HasUnrealizedChildren = true
                        };
                        pcNode.Children.Add(node);
                    }
                }

                FolderTreeView.RootNodes.Add(quickAccessNode);
                FolderTreeView.RootNodes.Add(pcNode);
            }
            catch (Exception ex)
            {
                SelectedDirectoryTextBlock.Text = $"ディレクトリの読み込みエラー: {ex.Message}";
            }
        }

        private void AddSpecialFolder(Microsoft.UI.Xaml.Controls.TreeViewNode parentNode, string name, string path, string icon)
        {
            if (System.IO.Directory.Exists(path))
            {
                var node = new Microsoft.UI.Xaml.Controls.TreeViewNode()
                {
                    Content = new ExplorerItem { Name = name, Path = path, Icon = icon },
                    HasUnrealizedChildren = true
                };
                parentNode.Children.Add(node);
            }
        }

        private void FolderTreeView_Expanding(Microsoft.UI.Xaml.Controls.TreeView sender, Microsoft.UI.Xaml.Controls.TreeViewExpandingEventArgs args)
        {
            if (args.Node.HasUnrealizedChildren)
            {
                FillTreeNode(args.Node);
            }
        }

        private void FillTreeNode(Microsoft.UI.Xaml.Controls.TreeViewNode node)
        {
            if (node.Content is ExplorerItem item)
            {
                node.Children.Clear();
                try
                {
                    var dirInfo = new System.IO.DirectoryInfo(item.Path);
                    foreach (var dir in dirInfo.GetDirectories())
                    {
                        if (!dir.Attributes.HasFlag(System.IO.FileAttributes.Hidden) && !dir.Attributes.HasFlag(System.IO.FileAttributes.System))
                        {
                            var childNode = new Microsoft.UI.Xaml.Controls.TreeViewNode()
                            {
                                Content = new ExplorerItem { Name = dir.Name, Path = dir.FullName },
                                HasUnrealizedChildren = true
                            };
                            node.Children.Add(childNode);
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // アクセス拒否時はスキップ
                }
                catch (Exception)
                {
                    // そのほかのエラーもスキップ
                }
                finally
                {
                    node.HasUnrealizedChildren = false;
                }
            }
        }

        private void FolderTreeView_ItemInvoked(Microsoft.UI.Xaml.Controls.TreeView sender, Microsoft.UI.Xaml.Controls.TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is Microsoft.UI.Xaml.Controls.TreeViewNode node && node.Content is ExplorerItem item)
            {
                if (string.IsNullOrEmpty(item.Path)) return;

                SelectedDirectoryTextBlock.Text = item.Path;
                LoadDirectoryContents(item.Path);
            }
        }

        private void LoadDirectoryContents(string path)
        {
            FilesAndFolders.Clear();
            _thumbnailQueue.Clear();
            _isGeneratingThumbnail = false;
            _currentThumbnailDirectory = path;
            _currentDirectoryPath = null;

            try
            {
                var dirInfo = new DirectoryInfo(path);

                // フォルダの追加
                foreach (var dir in dirInfo.GetDirectories())
                {
                    FilesAndFolders.Add(new FileSystemItem
                    {
                        Name = dir.Name,
                        Path = dir.FullName,
                        IsFolder = true
                    });
                }

                // ファイルの追加
                foreach (var file in dirInfo.GetFiles())
                {
                    var item = new FileSystemItem
                    {
                        Name = file.Name,
                        Path = file.FullName,
                        IsFolder = false
                    };
                    FilesAndFolders.Add(item);

                    if (item.Is3DModel)
                    {
                        _thumbnailQueue.Enqueue(item.Path);
                    }
                }

                ProcessThumbnailQueue();
                _currentDirectoryPath = path;
            }
            catch (Exception ex)
            {
                // アクセス権限がない場合などのエラー処理
                SelectedDirectoryTextBlock.Text = $"エラー: {ex.Message}";
            }
        }

        private void ProcessThumbnailQueue()
        {
            if (_isGeneratingThumbnail || _thumbnailQueue.Count == 0)
                return;

            _isGeneratingThumbnail = true;
            string path = _thumbnailQueue.Dequeue();

            try
            {
                string fileName = System.IO.Path.GetFileName(path);
                string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
                string virtualUrl = $"http://models.local/{Uri.EscapeDataString(fileName)}";

                string message = $"{{\"action\":\"thumbnail\", \"url\":\"{virtualUrl}\", \"extension\":\"{extension}\"}}";
                ThumbnailWebView.CoreWebView2.PostWebMessageAsJson(message);
            }
            catch (Exception)
            {
                _isGeneratingThumbnail = false;
                ProcessThumbnailQueue(); // Skip and try next
            }
        }

        private void FileSystemItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FileSystemItem item)
            {
                if (item.IsFolder)
                {
                    SelectedDirectoryTextBlock.Text = item.Path;
                    LoadDirectoryContents(item.Path);
                    SelectTreeNodeFromPath(item.Path);
                }
                else
                {
                    try
                    {
                        // 関連付けられたデフォルトのアプリケーションでファイルを開く
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = item.Path,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        SelectedDirectoryTextBlock.Text = $"ファイルの起動に失敗しました: {ex.Message}";
                    }
                }
            }
        }

        private void SelectTreeNodeFromPath(string targetPath)
        {
            // パスの正規化（末尾のセパレータ削除、小文字化での比較用）
            targetPath = targetPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            string targetPathLower = targetPath.ToLowerInvariant();

            foreach (var rootNode in FolderTreeView.RootNodes)
            {
                var foundNode = FindNodeRecursive(rootNode, targetPathLower);
                if (foundNode != null)
                {
                    // 見つかったノードを選択状態にする
                    FolderTreeView.SelectedNode = foundNode;
                    
                    // 親ノードをすべて展開する
                    var parent = foundNode.Parent;
                    while (parent != null)
                    {
                        parent.IsExpanded = true;
                        parent = parent.Parent;
                    }
                    return;
                }
            }
        }

        private Microsoft.UI.Xaml.Controls.TreeViewNode? FindNodeRecursive(Microsoft.UI.Xaml.Controls.TreeViewNode currentNode, string targetPathLower)
        {
            if (currentNode.Content is ExplorerItem item)
            {
                string nodePath = item.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                
                // パスが一致すればこのノード
                if (nodePath.ToLowerInvariant() == targetPathLower)
                {
                    return currentNode;
                }

                // 現在のノードのパスがターゲットパスの前方一致（親ディレクトリ）であるか確認
                // ただし、空文字列のノード（"PC" や "クイックアクセス" の直下など）は常に探索対象とする
                if (string.IsNullOrEmpty(nodePath) || targetPathLower.StartsWith(nodePath.ToLowerInvariant() + System.IO.Path.DirectorySeparatorChar))
                {
                    // 未展開のノードがあれば展開（子ノードを生成）してから探索
                    if (currentNode.HasUnrealizedChildren)
                    {
                        FillTreeNode(currentNode);
                    }

                    foreach (var childNode in currentNode.Children)
                    {
                        var result = FindNodeRecursive(childNode, targetPathLower);
                        if (result != null)
                        {
                            return result;
                        }
                    }
                }
            }
            return null;
        }

        private void FileSystemGridView_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            var isCtrlPressed = ctrlState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (isCtrlPressed)
            {
                var delta = e.GetCurrentPoint(FileSystemGridView).Properties.MouseWheelDelta;
                double step = delta > 0 ? 15 : -15;
                
                double newValue = ZoomSlider.Value + step;
                if (newValue > ZoomSlider.Maximum) newValue = ZoomSlider.Maximum;
                if (newValue < ZoomSlider.Minimum) newValue = ZoomSlider.Minimum;
                
                ZoomSlider.Value = newValue;

                // 選択中のアイテムが画面内に収まるようにスクロール
                ScrollToSelectedItem();

                e.Handled = true; // ズーム処理したのでスクロールをキャンセル
            }
        }

        /// <summary>
        /// 選択中のアイテムが画面内に収まるようにスクロール位置を調整する
        /// </summary>
        private void ScrollToSelectedItem()
        {
            if (FileSystemGridView == null || FileSystemGridView.SelectedItems.Count == 0) return;

            // レイアウト更新後にスクロールを実行
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                FileSystemGridView.UpdateLayout();

                if (FileSystemGridView.SelectedItems.Count > 0)
                {
                    var firstSelected = FileSystemGridView.SelectedItems[0];
                    FileSystemGridView.ScrollIntoView(firstSelected, ScrollIntoViewAlignment.Default);
                }
            });
        }

        private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            ScrollToSelectedItem();
        }

        private async void AudioPlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                // 動画が再生中の場合は停止
                if (_currentVideoPlayerElement != null)
                {
                    _currentVideoPlayerElement.MediaPlayer?.Pause();
                    _currentVideoPlayerElement.Source = null;
                    _currentVideoPlayerElement.Visibility = Visibility.Collapsed;
                    if (_currentVideoPlayingButton != null && _currentVideoPlayingButton.Content is FontIcon oldVideoIcon)
                    {
                        oldVideoIcon.Glyph = "\uE768"; // Play
                    }
                    _currentVideoPlayingPath = null;
                    _currentVideoPlayerElement = null;
                    _currentVideoPlayingButton = null;
                }

                if (_currentlyPlayingPath == path && _previewPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                {
                    // 既に同じファイルが再生中の場合は一時停止
                    _previewPlayer.Pause();
                    if (btn.Content is FontIcon icon) icon.Glyph = "\uE768"; // Play
                    return;
                }
                else if (_currentlyPlayingPath == path && _previewPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Paused)
                {
                    // 一時停止中の再開
                    _previewPlayer.Play();
                    if (btn.Content is FontIcon icon) icon.Glyph = "\uE769"; // Pause
                    return;
                }

                // 前に再生していたボタンのアイコンをPlayに戻す
                if (_currentlyPlayingButton != null && _currentlyPlayingButton != btn && _currentlyPlayingButton.Content is FontIcon oldIcon)
                {
                    oldIcon.Glyph = "\uE768"; // Play
                }

                try
                {
                    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                    _previewPlayer.Source = MediaSource.CreateFromStorageFile(file);
                    _previewPlayer.Play();
                    _currentlyPlayingPath = path;
                    _currentlyPlayingButton = btn;

                    if (btn.Content is FontIcon currentIcon)
                    {
                        currentIcon.Glyph = "\uE769"; // Pause
                    }
                }
                catch (Exception ex)
                {
                    SelectedDirectoryTextBlock.Text = $"再生エラー: {ex.Message}";
                }
            }
        }

        private void _previewPlayer_MediaEnded(MediaPlayer sender, object args)
        {
            // UIスレッドでボタンのアイコンを戻す
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_currentlyPlayingButton != null && _currentlyPlayingButton.Content is FontIcon icon)
                {
                    icon.Glyph = "\uE768"; // Play
                }
                _currentlyPlayingPath = null;
            });
        }

        private async void VideoPlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                // 既存の音声プレイバックがあれば停止
                if (_previewPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                {
                    _previewPlayer.Pause();
                    if (_currentlyPlayingButton != null && _currentlyPlayingButton.Content is FontIcon oldAudioIcon)
                    {
                        oldAudioIcon.Glyph = "\uE768"; // Play
                    }
                    _currentlyPlayingPath = null;
                }

                // 兄弟要素から MediaPlayerElement を探す
                var grid = (Grid)VisualTreeHelper.GetParent(btn);
                MediaPlayerElement? playerElement = null;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(grid); i++)
                {
                    if (VisualTreeHelper.GetChild(grid, i) is MediaPlayerElement me)
                    {
                        playerElement = me;
                        break;
                    }
                }

                if (playerElement == null) return;

                if (_currentVideoPlayingPath == path)
                {
                    // 同じものをクリックした場合は一時停止 / 再開
                    if (playerElement.MediaPlayer?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                    {
                        playerElement.MediaPlayer.Pause();
                        if (btn.Content is FontIcon icon) icon.Glyph = "\uE768"; // Play
                    }
                    else if (playerElement.MediaPlayer != null)
                    {
                        playerElement.MediaPlayer.Play();
                        if (btn.Content is FontIcon icon) icon.Glyph = "\uE769"; // Pause
                    }
                    return;
                }

                // 別の動画が再生中の場合は、前のを停止・非表示・Playボタンに戻す
                if (_currentVideoPlayerElement != null && _currentVideoPlayerElement != playerElement)
                {
                    _currentVideoPlayerElement.MediaPlayer?.Pause();
                    _currentVideoPlayerElement.Source = null;
                    _currentVideoPlayerElement.Visibility = Visibility.Collapsed;
                    if (_currentVideoPlayingButton != null && _currentVideoPlayingButton.Content is FontIcon oldIcon)
                    {
                        oldIcon.Glyph = "\uE768"; // Play
                    }
                }

                try
                {
                    // 新規再生
                    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                    if (playerElement.MediaPlayer == null)
                    {
                        var player = new MediaPlayer();
                        player.MediaEnded += VideoPlayer_MediaEnded;
                        player.IsLoopingEnabled = false;
                        playerElement.SetMediaPlayer(player);
                    }
                    playerElement.Source = MediaSource.CreateFromStorageFile(file);
                    playerElement.Visibility = Visibility.Visible;
                    playerElement.MediaPlayer?.Play();
                    
                    _currentVideoPlayerElement = playerElement;
                    _currentVideoPlayingPath = path;
                    _currentVideoPlayingButton = btn;

                    if (btn.Content is FontIcon currentIcon)
                    {
                        currentIcon.Glyph = "\uE769"; // Pause
                    }
                }
                catch (Exception ex)
                {
                    SelectedDirectoryTextBlock.Text = $"再生エラー: {ex.Message}";
                }
            }
        }

        private void VideoPlayer_MediaEnded(MediaPlayer sender, object args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_currentVideoPlayingButton != null && _currentVideoPlayingButton.Content is FontIcon icon)
                {
                    icon.Glyph = "\uE768"; // Play
                }
                if (_currentVideoPlayerElement != null)
                {
                    _currentVideoPlayerElement.Visibility = Visibility.Collapsed;
                    _currentVideoPlayerElement.Source = null;
                }
                _currentVideoPlayingPath = null;
                _currentVideoPlayerElement = null;
                _currentVideoPlayingButton = null;
            });
        }

        private void ModelPlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                try
                {
                    // Map the folder containing the model
                    string directoryPath = System.IO.Path.GetDirectoryName(path) ?? "";
                    if (!string.IsNullOrEmpty(directoryPath))
                    {
                        _currentModelDirectory = directoryPath;

                        string fileName = System.IO.Path.GetFileName(path);
                        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
                        string virtualUrl = $"http://models.local/{Uri.EscapeDataString(fileName)}";

                        string message = $"{{\"action\":\"load\", \"url\":\"{virtualUrl}\", \"extension\":\"{extension}\"}}";
                        ModelWebView.CoreWebView2.PostWebMessageAsJson(message);
                        
                        ModelViewerOverlay.Visibility = Visibility.Visible;
                    }
                }
                catch (Exception ex)
                {
                    SelectedDirectoryTextBlock.Text = $"3Dモデル読み込みエラー: {ex.Message}";
                }
            }
        }

        private void CloseModelViewer_Click(object sender, RoutedEventArgs e)
        {
            ModelViewerOverlay.Visibility = Visibility.Collapsed;
            string message = "{\"action\":\"clear\"}";
            ModelWebView.CoreWebView2.PostWebMessageAsJson(message);
        }

        /// <summary>
        /// GridViewのContextFlyoutが開く前に呼ばれるイベントハンドラー
        /// 選択状態やクリップボードに応じてメニュー項目の有効/無効を切り替える
        /// </summary>
        private void GridViewContextFlyout_Opening(object sender, object e)
        {
            bool hasSelection = FileSystemGridView.SelectedItems.Count > 0;
            bool hasClipboardData = Clipboard.GetContent().Contains(StandardDataFormats.StorageItems);

            GridViewCopyMenuItem.IsEnabled = hasSelection;
            GridViewPasteMenuItem.IsEnabled = hasClipboardData;
            GridViewDeleteMenuItem.IsEnabled = hasSelection;
            GridViewPermanentDeleteMenuItem.IsEnabled = hasSelection;
        }

        /// <summary>
        /// GridViewのContextFlyoutが閉じた後に呼ばれるイベントハンドラー
        /// キーボードアクセラレーションで正しく動作するよう、IsEnabledをすべてtrueにリセットする
        /// </summary>
        private void GridViewContextFlyout_Closed(object sender, object e)
        {
            GridViewCopyMenuItem.IsEnabled = true;
            GridViewPasteMenuItem.IsEnabled = true;
            GridViewDeleteMenuItem.IsEnabled = true;
            GridViewPermanentDeleteMenuItem.IsEnabled = true;
        }

        /// <summary>
        /// GridViewのコピーメニューがクリックされたときのハンドラー
        /// キーボードアクセラレーションからの呼び出し時も含め、選択状態を再チェックする
        /// </summary>
        private async void GridViewCopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedPaths = FileSystemGridView.SelectedItems
                .OfType<FileSystemItem>()
                .Select(item => item.Path)
                .ToList();

            // 選択がない場合は何もしない（アクセラレーション経由でも安全に処理）
            if (selectedPaths.Count == 0) return;

            System.Diagnostics.Debug.WriteLine("GridViewCopyMenuItem_Click");

            await CopyPathsToClipboard(selectedPaths);
        }

        /// <summary>
        /// GridViewの貼り付けメニューがクリックされたときのハンドラー
        /// キーボードアクセラレーションからの呼び出し時も含め、クリップボードと対象ディレクトリを再チェックする
        /// </summary>
        private async void GridViewPasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // クリップボードにファイルデータがなければ何もしない
            if (!Clipboard.GetContent().Contains(StandardDataFormats.StorageItems)) return;

            string? targetDir = GetCurrentDirectoryPath();
            if (!string.IsNullOrEmpty(targetDir))
            {
                System.Diagnostics.Debug.WriteLine("GridViewPasteMenuItem_Click");

                await PasteFilesFromClipboardAsync(targetDir);
            }
        }

        /// <summary>
        /// GridViewのゴミ箱に移動メニューがクリックされたときのハンドラー
        /// キーボードアクセラレーションからの呼び出し時も含め、選択状態を再チェックする
        /// </summary>
        private async void GridViewDeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = FileSystemGridView.SelectedItems
                .OfType<FileSystemItem>()
                .ToList();

            // 選択がない場合は何もしない（アクセラレーション経由でも安全に処理）
            if (selectedItems.Count == 0) return;

            System.Diagnostics.Debug.WriteLine("GridViewDeleteMenuItem_Click");

            await DeleteSelectedItemsAsync(selectedItems, permanentDelete: false);
        }

        /// <summary>
        /// GridViewの完全削除メニューがクリックされたときのハンドラー
        /// キーボードアクセラレーションからの呼び出し時も含め、選択状態を再チェックする
        /// </summary>
        private async void GridViewPermanentDeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = FileSystemGridView.SelectedItems
                .OfType<FileSystemItem>()
                .ToList();

            // 選択がない場合は何もしない（アクセラレーション経由でも安全に処理）
            if (selectedItems.Count == 0) return;

            System.Diagnostics.Debug.WriteLine("GridViewPermanentDeleteMenuItem_Click");

            await DeleteSelectedItemsAsync(selectedItems, permanentDelete: true);
        }

        /// <summary>
        /// TreeViewのContextFlyoutが開く前に呼ばれるイベントハンドラー
        /// 選択状態やクリップボードに応じてメニュー項目の有効/無効を切り替える
        /// </summary>
        private void FolderTreeViewContextFlyout_Opening(object sender, object e)
        {
            System.Diagnostics.Debug.WriteLine("FolderTreeViewContextFlyout_Opening");
            bool hasSelection = FolderTreeView.SelectedNode?.Content is ExplorerItem item
                && !string.IsNullOrEmpty(item.Path);
            bool hasClipboardData = Clipboard.GetContent().Contains(StandardDataFormats.StorageItems);

            TreeViewCopyMenuItem.IsEnabled = hasSelection;
            TreeViewPasteMenuItem.IsEnabled = hasSelection && hasClipboardData;
        }

        /// <summary>
        /// TreeViewのContextFlyoutが閉じた後に呼ばれるイベントハンドラー
        /// キーボードアクセラレーションで正しく動作するよう、IsEnabledをすべてtrueにリセットする
        /// </summary>
        private void FolderTreeViewContextFlyout_Closed(object sender, object e)
        {
            TreeViewCopyMenuItem.IsEnabled = true;
            TreeViewPasteMenuItem.IsEnabled = true;
        }

        /// <summary>
        /// TreeViewのコピーメニューがクリックされたときのハンドラー
        /// キーボードアクセラレーションからの呼び出し時も含め、選択状態を再チェックする
        /// </summary>
        private async void TreeViewCopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // 選択フォルダがなければ何もしない（アクセラレーション経由でも安全に処理）
            if (FolderTreeView.SelectedNode?.Content is not ExplorerItem item
                || string.IsNullOrEmpty(item.Path)) return;

            System.Diagnostics.Debug.WriteLine("TreeViewCopyMenuItem_Click");

            await CopyPathsToClipboard(new List<string> { item.Path });
        }

        /// <summary>
        /// TreeViewの貼り付けメニューがクリックされたときのハンドラー
        /// キーボードアクセラレーションからの呼び出し時も含め、選択状態とクリップボードを再チェックする
        /// </summary>
        private async void TreeViewPasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // 選択フォルダがなければ何もしない（アクセラレーション経由でも安全に処理）
            if (FolderTreeView.SelectedNode?.Content is not ExplorerItem item
                || string.IsNullOrEmpty(item.Path)) return;

            // クリップボードにファイルデータがなければ何もしない
            if (!Clipboard.GetContent().Contains(StandardDataFormats.StorageItems)) return;

            System.Diagnostics.Debug.WriteLine("TreeViewPasteMenuItem_Click");

            await PasteFilesFromClipboardAsync(item.Path);
        }

        /// <summary>
        /// 指定されたパスのファイル・フォルダをクリップボードにコピーする
        /// </summary>
        /// <param name="paths">コピー対象のパスリスト</param>
        private async System.Threading.Tasks.Task CopyPathsToClipboard(List<string> paths)
        {
            try
            {
                var storageItems = new List<IStorageItem>();

                foreach (var path in paths)
                {
                    try
                    {
                        if (System.IO.Directory.Exists(path))
                        {
                            var folder = await StorageFolder.GetFolderFromPathAsync(path);
                            storageItems.Add(folder);
                        }
                        else if (System.IO.File.Exists(path))
                        {
                            var file = await StorageFile.GetFileFromPathAsync(path);
                            storageItems.Add(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"クリップボードへのアイテム追加に失敗: {path} - {ex.Message}");
                    }
                }

                if (storageItems.Count > 0)
                {
                    foreach (var storageItem in storageItems)
                    {
                        System.Diagnostics.Debug.WriteLine($"Adding to clipboard: {storageItem.Path}");
                    }
                    var dataPackage = new DataPackage();
                    dataPackage.RequestedOperation = DataPackageOperation.Copy;
                    dataPackage.SetStorageItems(storageItems);
                    Clipboard.SetContent(dataPackage);
                    Clipboard.Flush();
                }
            }
            catch (Exception ex)
            {
                SelectedDirectoryTextBlock.Text = $"コピーに失敗しました: {ex.Message}";
            }
        }

        /// <summary>
        /// クリップボードのファイル・フォルダを指定ディレクトリにペーストする
        /// </summary>
        /// <param name="targetDirectoryPath">ペースト先のディレクトリパス</param>
        private async System.Threading.Tasks.Task PasteFilesFromClipboardAsync(string targetDirectoryPath)
        {
            try
            {
                var content = Clipboard.GetContent();
                if (!content.Contains(StandardDataFormats.StorageItems)) return;

                var items = await content.GetStorageItemsAsync();
                if (items == null || items.Count == 0) return;

                int successCount = 0;
                var failedItems = new List<string>();

                foreach (var item in items)
                {
                    try
                    {
                        if (item is StorageFile file)
                        {
                            // ファイルのコピー
                            string destPath = GetUniqueDestinationPath(
                                System.IO.Path.Combine(targetDirectoryPath, file.Name));
                            string destFileName = System.IO.Path.GetFileName(destPath);
                            var destFolder = await StorageFolder.GetFolderFromPathAsync(targetDirectoryPath);
                            System.Diagnostics.Debug.WriteLine($"Copying file: {file.Path} to {destFolder.Path}\\{destFileName}");
                            await file.CopyAsync(destFolder, destFileName, NameCollisionOption.GenerateUniqueName);
                            successCount++;
                        }
                        else if (item is StorageFolder folder)
                        {
                            // フォルダのコピー（再帰的）
                            string destPath = GetUniqueDestinationPath(
                                System.IO.Path.Combine(targetDirectoryPath, folder.Name));
                            string destFolderName = System.IO.Path.GetFileName(destPath);
                            var destParent = await StorageFolder.GetFolderFromPathAsync(targetDirectoryPath);
                            var newFolder = await destParent.CreateFolderAsync(destFolderName, CreationCollisionOption.GenerateUniqueName);
                            await CopyFolderContentsAsync(folder, newFolder);
                            successCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failedItems.Add($"{item.Name}: {ex.Message}");
                    }
                }

                // ペースト完了後、現在表示中のディレクトリを再読込
                string? currentDir = GetCurrentDirectoryPath();
                if (!string.IsNullOrEmpty(currentDir) && 
                    currentDir.Equals(targetDirectoryPath, StringComparison.OrdinalIgnoreCase))
                {
                    LoadDirectoryContents(currentDir);
                }

                // ペースト先フォルダに対応するTreeViewノードが展開済みなら再読み込みする
                string targetPathLower = targetDirectoryPath
                    .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                    .ToLowerInvariant();
                foreach (var rootNode in FolderTreeView.RootNodes)
                {
                    var targetNode = FindNodeRecursive(rootNode, targetPathLower);
                    if (targetNode != null && !targetNode.HasUnrealizedChildren)
                    {
                        FillTreeNode(targetNode);
                        break;
                    }
                }

                if (failedItems.Count > 0)
                {
                    SelectedDirectoryTextBlock.Text = $"ペーストに失敗したアイテム: {string.Join(", ", failedItems)}";
                }
            }
            catch (Exception ex)
            {
                SelectedDirectoryTextBlock.Text = $"ペーストに失敗しました: {ex.Message}";
            }
        }

        /// <summary>
        /// フォルダの内容を再帰的にコピーする
        /// </summary>
        private async System.Threading.Tasks.Task CopyFolderContentsAsync(StorageFolder source, StorageFolder destination)
        {
            System.Diagnostics.Debug.WriteLine($"Copying folder: {source.Path} to {destination.Path}");
            // ファイルをコピー
            foreach (var file in await source.GetFilesAsync())
            {
                await file.CopyAsync(destination, file.Name, NameCollisionOption.GenerateUniqueName);
            }

            // サブフォルダを再帰的にコピー
            foreach (var subFolder in await source.GetFoldersAsync())
            {
                var newSubFolder = await destination.CreateFolderAsync(subFolder.Name, CreationCollisionOption.GenerateUniqueName);
                await CopyFolderContentsAsync(subFolder, newSubFolder);
            }
        }

        /// <summary>
        /// 同名のファイル・フォルダが存在する場合、ユニークなパスを生成する
        /// </summary>
        private string GetUniqueDestinationPath(string path)
        {
            if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                return path;

            string directory = System.IO.Path.GetDirectoryName(path) ?? "";
            string nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(path);
            string extension = System.IO.Path.GetExtension(path);

            int counter = 2;
            string newPath;
            do
            {
                // "ファイル名 (2).ext" のような形式
                newPath = System.IO.Path.Combine(directory, $"{nameWithoutExt} ({counter}){extension}");
                counter++;
            } while (System.IO.File.Exists(newPath) || System.IO.Directory.Exists(newPath));

            return newPath;
        }

        /// <summary>
        /// 現在表示中のディレクトリパスを取得する
        /// </summary>
        private string? GetCurrentDirectoryPath()
        {
            string text = SelectedDirectoryTextBlock.Text;
            if (!string.IsNullOrEmpty(text) && System.IO.Directory.Exists(text))
            {
                return text;
            }
            return null;
        }

        /// <summary>
        /// 選択されたアイテムを削除する
        /// </summary>
        /// <param name="items">削除対象のアイテムリスト</param>
        /// <param name="permanentDelete">trueなら完全削除、falseならゴミ箱へ移動</param>
        private async System.Threading.Tasks.Task DeleteSelectedItemsAsync(List<FileSystemItem> items, bool permanentDelete)
        {
            // 完全削除の場合は確認ダイアログを表示
            if (permanentDelete)
            {
                string itemNames = string.Join("\n", items.Select(i => i.Name));
                string message = items.Count == 1
                    ? $"「{items[0].Name}」を完全に削除しますか？\nこの操作は元に戻せません。"
                    : $"{items.Count} 個のアイテムを完全に削除しますか？\nこの操作は元に戻せません。";

                var dialog = new ContentDialog
                {
                    Title = "完全削除の確認",
                    Content = message,
                    PrimaryButtonText = "削除",
                    CloseButtonText = "キャンセル",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary) return;
            }

            int successCount = 0;
            var failedItems = new List<string>();

            foreach (var item in items)
            {
                try
                {
                    if (item.IsFolder)
                    {
                        if (permanentDelete)
                        {
                            // 完全削除
                            System.IO.Directory.Delete(item.Path, recursive: true);
                        }
                        else
                        {
                            // ゴミ箱へ移動
                            FileSystem.DeleteDirectory(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        }
                    }
                    else
                    {
                        if (permanentDelete)
                        {
                            // 完全削除
                            System.IO.File.Delete(item.Path);
                        }
                        else
                        {
                            // ゴミ箱へ移動
                            FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        }
                    }

                    FilesAndFolders.Remove(item);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failedItems.Add($"{item.Name}: {ex.Message}");
                }
            }

            // エラーがあった場合はステータスバーに表示
            if (failedItems.Count > 0)
            {
                string errorMessage = $"削除に失敗したアイテム: {string.Join(", ", failedItems)}";
                SelectedDirectoryTextBlock.Text = errorMessage;
            }
        }

        private async void FileSystemGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            var storageItems = new List<IStorageItem>();
            foreach (var item in e.Items)
            {
                if (item is FileSystemItem fsItem)
                {
                    try
                    {
                        if (fsItem.IsFolder)
                        {
                            storageItems.Add(await StorageFolder.GetFolderFromPathAsync(fsItem.Path));
                        }
                        else
                        {
                            storageItems.Add(await StorageFile.GetFileFromPathAsync(fsItem.Path));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to get storage item: {ex.Message}");
                    }
                }
            }

            if (storageItems.Count > 0)
            {
                e.Data.SetStorageItems(storageItems);
                e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
            }
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidPositionChange || args.DidSizeChange)
            {
                if (sender.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Restored)
                {
                    _lastNormalBounds = new RectInt32(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
                }
            }
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            SaveWindowState();

            // WebView2の例外エラーを防ぐため、アプリ終了時にリソースを明示的に解放します
            try
            {
                if (ModelWebView != null)
                {
                    ModelWebView.Close();
                }
                if (ThumbnailWebView != null)
                {
                    ThumbnailWebView.Close();
                }
            }
            catch
            {
                // アプリ終了処理中のため例外は無視する
            }
        }

        private void RestoreWindowState()
        {
            if (_appWindow == null) return;

            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize(json, WindowSettingsContext.Default.WindowSettings);

                    if (settings != null)
                    {
                        // 前回の位置とサイズを適用
                        _appWindow.MoveAndResize(new RectInt32(settings.X, settings.Y, settings.Width, settings.Height));
                        _lastNormalBounds = new RectInt32(settings.X, settings.Y, settings.Width, settings.Height);

                        // 最大化状態の適用
                        if (settings.IsMaximized)
                        {
                            if (_appWindow.Presenter is OverlappedPresenter presenter)
                            {
                                presenter.Maximize();
                            }
                        }
                    }
                }
                else
                {
                    // デフォルトの通常サイズを現在の状態から取得
                    _lastNormalBounds = new RectInt32(_appWindow.Position.X, _appWindow.Position.Y, _appWindow.Size.Width, _appWindow.Size.Height);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to restore window state: {ex.Message}");
            }
        }

        private void SaveWindowState()
        {
            if (_appWindow == null) return;

            try
            {
                var settings = new WindowSettings
                {
                    X = _lastNormalBounds.X,
                    Y = _lastNormalBounds.Y,
                    Width = _lastNormalBounds.Width,
                    Height = _lastNormalBounds.Height,
                    IsMaximized = (_appWindow.Presenter is OverlappedPresenter presenter) && (presenter.State == OverlappedPresenterState.Maximized)
                };

                string directory = System.IO.Path.GetDirectoryName(_settingsPath)!;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(settings, WindowSettingsContext.Default.WindowSettings);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save window state: {ex.Message}");
            }
        }

        internal class WindowSettings
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public bool IsMaximized { get; set; }
        }

        [System.Text.Json.Serialization.JsonSerializable(typeof(WindowSettings))]
        internal partial class WindowSettingsContext : System.Text.Json.Serialization.JsonSerializerContext
        {
        }

        private void FileSystemGridView_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
            }
        }

        private async void FileSystemGridView_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0 && !string.IsNullOrEmpty(_currentDirectoryPath))
                {
                    foreach (var item in items)
                    {
                        try
                        {
                            if (item is StorageFile file)
                            {
                                await file.CopyAsync(await StorageFolder.GetFolderFromPathAsync(_currentDirectoryPath), file.Name, NameCollisionOption.GenerateUniqueName);
                            }
                            else if (item is StorageFolder folder)
                            {
                                // フォルダのコピーは再帰的に行う必要がある
                                await CopyFolderAsync(folder, await StorageFolder.GetFolderFromPathAsync(_currentDirectoryPath));
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Drop failed: {ex.Message}");
                        }
                    }
                    // リフレッシュ
                    LoadDirectoryContents(_currentDirectoryPath);
                }
            }
        }

        private async Task CopyFolderAsync(StorageFolder source, StorageFolder destinationParent)
        {
            var newFolder = await destinationParent.CreateFolderAsync(source.Name, CreationCollisionOption.GenerateUniqueName);
            foreach (var file in await source.GetFilesAsync())
            {
                await file.CopyAsync(newFolder, file.Name, NameCollisionOption.GenerateUniqueName);
            }
            foreach (var subFolder in await source.GetFoldersAsync())
            {
                await CopyFolderAsync(subFolder, newFolder);
            }
        }
    }
}
