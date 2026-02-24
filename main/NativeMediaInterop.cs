using System;
using System.Runtime.InteropServices;

namespace main
{
    public static class NativeMediaInterop
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct WebPFrame
        {
            public IntPtr bgraBuffer;
            public int durationMs;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WebPAnimation
        {
            public int width;
            public int height;
            public int frameCount;
            public int loopCount;
            public IntPtr frames; // Pointer to WebPFrame array
        }

        [DllImport("NativeMedia.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr DecodeWebPAnimation(string filePath);

        [DllImport("NativeMedia.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void FreeWebPAnimation(IntPtr animation);
    }
}
