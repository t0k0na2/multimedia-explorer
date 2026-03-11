using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace main
{
    public class FileSystemItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        
        // フォルダかファイルかによって表示するアイコンを決定
        // \uE8B7 = フォルダ, \uE8A5 = ファイル
        public string Icon => IsFolder ? "\uE8B7" : "\uE8A5";

        // 画像ファイルかどうかを判定
        public bool IsImage
        {
            get
            {
                if (IsFolder || string.IsNullOrEmpty(Path)) return false;
                var ext = System.IO.Path.GetExtension(Path).ToLowerInvariant();
                return ext == ".bmp" || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".webp" || ext == ".ico" || ext == ".svg";
            }
        }

        // 音声ファイルかどうかを判定
        public bool IsAudio
        {
            get
            {
                if (IsFolder || string.IsNullOrEmpty(Path)) return false;
                var ext = System.IO.Path.GetExtension(Path).ToLowerInvariant();
                return ext == ".wav" || ext == ".ogg" || ext == ".mp3" || ext == ".m4a" || ext == ".flac" || ext == ".wma" || ext == ".aac";
            }
        }

        // 動画ファイルかどうかを判定
        public bool IsVideo
        {
            get
            {
                if (IsFolder || string.IsNullOrEmpty(Path)) return false;
                var ext = System.IO.Path.GetExtension(Path).ToLowerInvariant();
                return ext == ".avi" || ext == ".mov" || ext == ".mp4" || ext == ".webm" || ext == ".wmv";
            }
        }

        // 3Dモデルかどうかを判定
        public bool Is3DModel
        {
            get
            {
                if (IsFolder || string.IsNullOrEmpty(Path)) return false;
                var ext = System.IO.Path.GetExtension(Path).ToLowerInvariant();
                return ext == ".fbx" || ext == ".gltf" || ext == ".glb" || ext == ".obj";
            }
        }

        // FontIconの表示制御 (フォルダ、または画像・音声・動画・3Dモデル以外のファイル)
        public Visibility FontIconVisibility => (!IsImage && !IsAudio && !IsVideo && !Is3DModel) ? Visibility.Visible : Visibility.Collapsed;

        // Imageの表示制御
        public Visibility ImageIconVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;

        // Audioボタンの表示制御
        public Visibility AudioButtonVisibility => IsAudio ? Visibility.Visible : Visibility.Collapsed;

        // Videoボタンの表示制御
        public Visibility VideoButtonVisibility => IsVideo ? Visibility.Visible : Visibility.Collapsed;
        
        // 3Dモデルボタンの表示制御
        public Visibility ModelButtonVisibility => Is3DModel ? Visibility.Visible : Visibility.Collapsed;

        // 画像ソース
        public BitmapImage? ImageSource
        {
            get
            {
                if (IsImage)
                {
                    try
                    {
                        return new BitmapImage(new Uri(Path));
                    }
                    catch
                    {
                        return null; // 読み込み失敗時はnull
                    }
                }
                return null;
            }
        }
    }
}
