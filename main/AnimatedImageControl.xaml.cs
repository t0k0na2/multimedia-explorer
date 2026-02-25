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
        private NativeMediaWinRT.WebPAnimation? _webpAnimation;
        private int _currentFrameIndex = 0;
        private WriteableBitmap? _bitmap;

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
                    _webpAnimation = NativeMediaWinRT.WebPDecoder.DecodeAnimation(path);
                });

                if (_webpAnimation != null && _webpAnimation.FrameCount > 0)
                {
                    _bitmap = new WriteableBitmap(_webpAnimation.Width, _webpAnimation.Height);
                    DisplayImage.Source = _bitmap;
                    _currentFrameIndex = 0;
                    
                    if (_webpAnimation.FrameCount > 1)
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
            if (_webpAnimation == null || _webpAnimation.FrameCount == 0 || _bitmap == null) return;

            var frame = _webpAnimation.Frames[_currentFrameIndex];
            
            var pixelBuffer = _bitmap.PixelBuffer;
            using (var stream = pixelBuffer.AsStream())
            {
                byte[] managedArray = frame.BgraBuffer;
                stream.Write(managedArray, 0, managedArray.Length);
            }
            _bitmap.Invalidate(); // Ensure UI updates

            // Set interval for next frame
            int duration = frame.DurationMs > 0 ? frame.DurationMs : 100;
            _timer.Interval = TimeSpan.FromMilliseconds(duration);
        }

        private void Timer_Tick(object? sender, object e)
        {
            _currentFrameIndex++;
            if (_webpAnimation != null && _currentFrameIndex >= _webpAnimation.FrameCount)
            {
                _currentFrameIndex = 0;
            }
            UpdateFrame();
        }

        private void StopAnimation()
        {
            _timer.Stop();
            _webpAnimation = null;
            _bitmap = null;
        }

        private void AnimatedImageControl_Unloaded(object sender, RoutedEventArgs e)
        {
            StopAnimation();
        }
    }
}
