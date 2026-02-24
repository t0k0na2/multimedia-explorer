using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace main
{
    public sealed partial class AnimatedImageControl : UserControl
    {
        private DispatcherTimer _timer;
        private IntPtr _webpAnimation = IntPtr.Zero;
        private int _currentFrameIndex = 0;
        private WriteableBitmap? _bitmap;
        private NativeMediaInterop.WebPAnimation _animData;

        public string? ImagePath
        {
            get { return (string)GetValue(ImagePathProperty); }
            set { SetValue(ImagePathProperty, value); }
        }

        public static readonly DependencyProperty ImagePathProperty =
            DependencyProperty.Register("ImagePath", typeof(string), typeof(AnimatedImageControl), new PropertyMetadata(null, OnImagePathChanged));

        public AnimatedImageControl()
        {
            this.InitializeComponent();
            _timer = new DispatcherTimer();
            _timer.Tick += Timer_Tick;
            this.Unloaded += AnimatedImageControl_Unloaded;
        }

        private static async void OnImagePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (AnimatedImageControl)d;
            await control.LoadImageAsync((string)e.NewValue);
        }

        private async Task LoadImageAsync(string path)
        {
            StopAnimation();
            
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                DisplayImage.Source = null;
                return;
            }

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".webp")
            {
                // Load via C++ decoder
                await Task.Run(() =>
                {
                    _webpAnimation = NativeMediaInterop.DecodeWebPAnimation(path);
                    if (_webpAnimation != IntPtr.Zero)
                    {
                        _animData = Marshal.PtrToStructure<NativeMediaInterop.WebPAnimation>(_webpAnimation);
                    }
                });

                if (_webpAnimation != IntPtr.Zero && _animData.frameCount > 0)
                {
                    _bitmap = new WriteableBitmap(_animData.width, _animData.height);
                    DisplayImage.Source = _bitmap;
                    _currentFrameIndex = 0;
                    
                    if (_animData.frameCount > 1)
                    {
                        UpdateFrame();
                        _timer.Start();
                    }
                    else
                    {
                        UpdateFrame();
                    }
                }
            }
            else if (ext == ".gif" || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp")
            {
                // Fallback to standard BitmapImage which supports GIF natively
                var bmp = new BitmapImage(new Uri(path));
                DisplayImage.Source = bmp;
            }
        }

        private void UpdateFrame()
        {
            if (_webpAnimation == IntPtr.Zero || _animData.frameCount == 0 || _bitmap == null) return;

            long frameOffset = _animData.frames.ToInt64() + _currentFrameIndex * Marshal.SizeOf<NativeMediaInterop.WebPFrame>();
            var frame = Marshal.PtrToStructure<NativeMediaInterop.WebPFrame>(new IntPtr(frameOffset));

            int bytesPerPixel = 4;
            int totalBytes = _animData.width * _animData.height * bytesPerPixel;
            
            var pixelBuffer = _bitmap.PixelBuffer;
            using (var stream = pixelBuffer.AsStream())
            {
                byte[] managedArray = new byte[totalBytes];
                Marshal.Copy(frame.bgraBuffer, managedArray, 0, totalBytes);
                stream.Write(managedArray, 0, totalBytes);
            }
            _bitmap.Invalidate(); // Ensure UI updates

            // Set interval for next frame
            int duration = frame.durationMs > 0 ? frame.durationMs : 100;
            _timer.Interval = TimeSpan.FromMilliseconds(duration);
        }

        private void Timer_Tick(object? sender, object e)
        {
            _currentFrameIndex++;
            if (_currentFrameIndex >= _animData.frameCount)
            {
                _currentFrameIndex = 0;
            }
            UpdateFrame();
        }

        private void StopAnimation()
        {
            _timer.Stop();
            if (_webpAnimation != IntPtr.Zero)
            {
                NativeMediaInterop.FreeWebPAnimation(_webpAnimation);
                _webpAnimation = IntPtr.Zero;
            }
            _bitmap = null;
        }

        private void AnimatedImageControl_Unloaded(object sender, RoutedEventArgs e)
        {
            StopAnimation();
        }
    }
}
