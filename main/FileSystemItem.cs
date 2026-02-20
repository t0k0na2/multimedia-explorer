using System;
using System.IO;

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
    }
}
