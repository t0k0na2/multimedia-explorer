using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
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

        public double TargetSize
        {
            get { return (double)GetValue(TargetSizeProperty); }
            set { SetValue(TargetSizeProperty, value); }
        }

        public static readonly DependencyProperty TargetSizeProperty =
            DependencyProperty.Register("TargetSize", typeof(double), typeof(AnimatedImageControl), new PropertyMetadata(100.0, OnTargetSizeChanged));

        private static void OnTargetSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (AnimatedImageControl)d;
            if (!string.IsNullOrEmpty(control.ImagePath) && Path.GetExtension(control.ImagePath).ToLowerInvariant() == ".ico")
            {
                control.DispatcherQueue.TryEnqueue(async () =>
                {
                    await control.LoadImageAsync(control.ImagePath);
                });
            }
        }

        public AnimatedImageControl()
        {
            this.InitializeComponent();
            _timer = new DispatcherTimer();
            _timer.Tick += Timer_Tick;
            this.Unloaded += AnimatedImageControl_Unloaded;
        }

        private static void OnImagePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (AnimatedImageControl)d;
            control.DispatcherQueue.TryEnqueue(async () =>
            {
                await control.LoadImageAsync((string)e.NewValue);
            });
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
            else if (ext == ".ico")
            {
                await LoadIcoAsync(path);
            }
            else if (ext == ".svg")
            {
                var svgSource = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri(path));
                DisplayImage.Source = svgSource;
            }
            else if (ext == ".gif" || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp")
            {
                // Fallback to standard BitmapImage which supports GIF natively
                var bmp = new BitmapImage(new Uri(path));
                DisplayImage.Source = bmp;
            }
            else if (ext == ".mp4" || ext == ".avi" || ext == ".mov" || ext == ".webm" || ext == ".wmv")
            {
                await LoadVideoThumbnailAsync(path);
            }
        }

        private async Task LoadVideoThumbnailAsync(string path)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.VideosView, (uint)(TargetSize > 0 ? TargetSize : 100));
                
                if (thumbnail != null && thumbnail.Type == Windows.Storage.FileProperties.ThumbnailType.Image)
                {
                    var bitmapImage = new BitmapImage();
                    await bitmapImage.SetSourceAsync(thumbnail);
                    DisplayImage.Source = bitmapImage;
                }
                else
                {
                    thumbnail?.Dispose();
                    // OSがサムネイル（画像）を返さなかった場合（WebMなどでアイコンが返る場合）のフォールバック
                    var clip = await Windows.Media.Editing.MediaClip.CreateFromFileAsync(file);
                    var composition = new Windows.Media.Editing.MediaComposition();
                    composition.Clips.Add(clip);
                    
                    // 先頭フレームを取得 (scaledHeightを0にすることでアスペクト比を維持)
                    int targetWidth = (int)(TargetSize > 0 ? TargetSize : 100);
                    var stream = await composition.GetThumbnailAsync(TimeSpan.Zero, targetWidth, 0, Windows.Media.Editing.VideoFramePrecision.NearestFrame);
                    if (stream != null)
                    {
                        var bitmapImage = new BitmapImage();
                        await bitmapImage.SetSourceAsync(stream);
                        DisplayImage.Source = bitmapImage;
                    }
                    else
                    {
                        DisplayImage.Source = null;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load video thumbnail: {ex.Message}");
                DisplayImage.Source = null;
            }
        }

        private async Task LoadIcoAsync(string path)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var stream = await file.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(stream);
                
                uint frameCount = decoder.FrameCount;
                if (frameCount == 0) return;

                uint bestFrameIndex = 0;
                double target = TargetSize > 0 ? TargetSize : 100;
                
                uint bestSize = 0;
                
                for (uint i = 0; i < frameCount; i++)
                {
                    var frame = await decoder.GetFrameAsync(i);
                    uint size = Math.Max(frame.PixelWidth, frame.PixelHeight);
                    
                    if (size >= target)
                    {
                        if (bestSize == 0 || bestSize < target || size < bestSize)
                        {
                            bestSize = size;
                            bestFrameIndex = i;
                        }
                    }
                    else 
                    {
                        if (bestSize == 0 || (bestSize < target && size > bestSize))
                        {
                            bestSize = size;
                            bestFrameIndex = i;
                        }
                    }
                }

                var bestFrame = await decoder.GetFrameAsync(bestFrameIndex);
                var softwareBitmap = await bestFrame.GetSoftwareBitmapAsync();
                
                if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                    softwareBitmap.BitmapAlphaMode == BitmapAlphaMode.Straight)
                {
                    softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                }
                
                var source = new Microsoft.UI.Xaml.Media.Imaging.SoftwareBitmapSource();
                await source.SetBitmapAsync(softwareBitmap);
                DisplayImage.Source = source;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load ICO: {ex.Message}");
                DisplayImage.Source = null;
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
