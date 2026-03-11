using Microsoft.UI.Xaml;
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
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            
            // 実行ファイル自体からアイコンを抽出してタイトルバーに適用する
            string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath != null)
            {
                IntPtr hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
                if (hIcon != IntPtr.Zero)
                {
                    Microsoft.UI.IconId iconId = Microsoft.UI.Win32Interop.GetIconIdFromIcon(hIcon);
                    appWindow.SetIcon(iconId);
                }
            }

            FileSystemGridView.ItemsSource = FilesAndFolders;
            _previewPlayer = new MediaPlayer();
            _previewPlayer.MediaEnded += _previewPlayer_MediaEnded;

            InitializeFolderTree();
            InitializeWebViewAsync();
        }

        private async void InitializeWebViewAsync()
        {
            try
            {
                await ModelWebView.EnsureCoreWebView2Async();
                await ThumbnailWebView.EnsureCoreWebView2Async();
                
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
                e.Handled = true; // ズーム処理したのでスクロールをキャンセル
            }
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
    }
}
