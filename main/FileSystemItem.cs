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
                return ext == ".bmp" || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".webp";
            }
        }

        // FontIconの表示制御
        public Visibility FontIconVisibility => IsImage ? Visibility.Collapsed : Visibility.Visible;

        // Imageの表示制御
        public Visibility ImageIconVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;

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
