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

        public MainWindow()
        {
            InitializeComponent();
            FileSystemGridView.ItemsSource = FilesAndFolders;
            _previewPlayer = new MediaPlayer();
            _previewPlayer.MediaEnded += _previewPlayer_MediaEnded;
        }

        private async void SelectDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");

            // WinUI 3 向けに Window ハンドルを取得して Picker を関連付ける
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                SelectedDirectoryTextBlock.Text = folder.Path;
                LoadDirectoryContents(folder.Path);
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
    }
}
