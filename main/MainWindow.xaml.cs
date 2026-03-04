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

        public MainWindow()
        {
            InitializeComponent();
            FileSystemGridView.ItemsSource = FilesAndFolders;
            _previewPlayer = new MediaPlayer();
            _previewPlayer.MediaEnded += _previewPlayer_MediaEnded;

            InitializeFolderTree();
        }

        private void InitializeFolderTree()
        {
            try
            {
                foreach (var drive in System.IO.DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        var node = new Microsoft.UI.Xaml.Controls.TreeViewNode()
                        {
                            Content = new ExplorerItem { Name = drive.Name, Path = drive.RootDirectory.FullName },
                            HasUnrealizedChildren = true
                        };
                        FolderTreeView.RootNodes.Add(node);
                    }
                }
            }
            catch (Exception ex)
            {
                SelectedDirectoryTextBlock.Text = $"ドライブの読み込みエラー: {ex.Message}";
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
                SelectedDirectoryTextBlock.Text = item.Path;
                LoadDirectoryContents(item.Path);
            }
        }

        private void LoadDirectoryContents(string path)
        {
            FilesAndFolders.Clear();

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
                    FilesAndFolders.Add(new FileSystemItem
                    {
                        Name = file.Name,
                        Path = file.FullName,
                        IsFolder = false
                    });
                }
            }
            catch (Exception ex)
            {
                // アクセス権限がない場合などのエラー処理
                SelectedDirectoryTextBlock.Text = $"エラー: {ex.Message}";
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
    }
}
