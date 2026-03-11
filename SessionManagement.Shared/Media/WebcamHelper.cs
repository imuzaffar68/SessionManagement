//using System;
//using System.Drawing;
//using System.Drawing.Imaging;
//using System.IO;
//using System.Linq;
//using AForge.Video;
//using AForge.Video.DirectShow;

//namespace SessionManagement.Media
//{
//    /// <summary>
//    /// Webcam capture helper using AForge.NET
//    /// Install NuGet packages: AForge.Video, AForge.Video.DirectShow
//    /// </summary>
//    public class WebcamHelper : IDisposable
//    {
//        private FilterInfoCollection videoDevices;
//        private VideoCaptureDevice videoSource;
//        private Bitmap capturedImage;
//        private bool isCapturing = false;
//        private object lockObject = new object();

//        public event EventHandler<ImageCapturedEventArgs> ImageCaptured;
//        public event EventHandler<WebcamErrorEventArgs> CaptureError;

//        #region Properties

//        public bool IsDeviceAvailable
//        {
//            get
//            {
//                try
//                {
//                    videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
//                    return videoDevices.Count > 0;
//                }
//                catch
//                {
//                    return false;
//                }
//            }
//        }

//        public string[] GetAvailableDevices()
//        {
//            try
//            {
//                videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
//                return videoDevices.Cast<FilterInfo>().Select(d => d.Name).ToArray();
//            }
//            catch (Exception ex)
//            {
//                OnCaptureError(new WebcamErrorEventArgs($"Error listing devices: {ex.Message}"));
//                return new string[0];
//            }
//        }

//        #endregion

//        #region Capture Methods

//        /// <summary>
//        /// Captures a single image from the default webcam
//        /// </summary>
//        public Bitmap CaptureImage()
//        {
//            return CaptureImage(0); // Use first device
//        }

//        /// <summary>
//        /// Captures a single image from specified webcam device
//        /// </summary>
//        public Bitmap CaptureImage(int deviceIndex)
//        {
//            try
//            {
//                videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

//                if (videoDevices.Count == 0)
//                {
//                    OnCaptureError(new WebcamErrorEventArgs("No webcam devices found"));
//                    return null;
//                }

//                if (deviceIndex >= videoDevices.Count)
//                {
//                    deviceIndex = 0;
//                }

//                // Create video source
//                videoSource = new VideoCaptureDevice(videoDevices[deviceIndex].MonikerString);

//                // Set resolution (optional - adjust as needed)
//                if (videoSource.VideoCapabilities.Length > 0)
//                {
//                    // Select highest resolution available
//                    var capabilities = videoSource.VideoCapabilities
//                        .OrderByDescending(c => c.FrameSize.Width * c.FrameSize.Height)
//                        .FirstOrDefault();

//                    if (capabilities != null)
//                    {
//                        videoSource.VideoResolution = capabilities;
//                    }
//                }

//                // Subscribe to new frame event
//                videoSource.NewFrame += VideoSource_NewFrame;

//                // Start capturing
//                isCapturing = true;
//                capturedImage = null;
//                videoSource.Start();

//                // Wait for image capture (max 5 seconds)
//                DateTime startTime = DateTime.Now;
//                while (capturedImage == null && (DateTime.Now - startTime).TotalSeconds < 5)
//                {
//                    System.Threading.Thread.Sleep(100);
//                }

//                // Stop and cleanup
//                StopCapture();

//                if (capturedImage != null)
//                {
//                    OnImageCaptured(new ImageCapturedEventArgs(capturedImage));
//                    return capturedImage;
//                }
//                else
//                {
//                    OnCaptureError(new WebcamErrorEventArgs("Timeout waiting for image capture"));
//                    return null;
//                }
//            }
//            catch (Exception ex)
//            {
//                OnCaptureError(new WebcamErrorEventArgs($"Capture error: {ex.Message}"));
//                StopCapture();
//                return null;
//            }
//        }

//        /// <summary>
//        /// Starts continuous video capture for preview
//        /// </summary>
//        public bool StartPreview(int deviceIndex = 0)
//        {
//            try
//            {
//                if (isCapturing)
//                {
//                    StopCapture();
//                }

//                videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

//                if (videoDevices.Count == 0)
//                {
//                    OnCaptureError(new WebcamErrorEventArgs("No webcam devices found"));
//                    return false;
//                }

//                if (deviceIndex >= videoDevices.Count)
//                {
//                    deviceIndex = 0;
//                }

//                videoSource = new VideoCaptureDevice(videoDevices[deviceIndex].MonikerString);
//                videoSource.NewFrame += VideoSource_NewFrame_Preview;

//                isCapturing = true;
//                videoSource.Start();
//                return true;
//            }
//            catch (Exception ex)
//            {
//                OnCaptureError(new WebcamErrorEventArgs($"Preview start error: {ex.Message}"));
//                return false;
//            }
//        }

//        /// <summary>
//        /// Captures current frame from active preview
//        /// </summary>
//        public Bitmap CaptureCurrentFrame()
//        {
//            lock (lockObject)
//            {
//                if (capturedImage != null)
//                {
//                    return new Bitmap(capturedImage);
//                }
//                return null;
//            }
//        }

//        /// <summary>
//        /// Stops video capture
//        /// </summary>
//        public void StopCapture()
//        {
//            try
//            {
//                if (videoSource != null && videoSource.IsRunning)
//                {
//                    videoSource.SignalToStop();
//                    videoSource.WaitForStop();
//                }

//                if (videoSource != null)
//                {
//                    videoSource.NewFrame -= VideoSource_NewFrame;
//                    videoSource.NewFrame -= VideoSource_NewFrame_Preview;
//                    videoSource = null;
//                }

//                isCapturing = false;
//            }
//            catch (Exception ex)
//            {
//                OnCaptureError(new WebcamErrorEventArgs($"Stop capture error: {ex.Message}"));
//            }
//        }

//        #endregion

//        #region Event Handlers

//        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
//        {
//            try
//            {
//                if (capturedImage == null)
//                {
//                    lock (lockObject)
//                    {
//                        capturedImage = (Bitmap)eventArgs.Frame.Clone();
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                OnCaptureError(new WebcamErrorEventArgs($"Frame capture error: {ex.Message}"));
//            }
//        }

//        private void VideoSource_NewFrame_Preview(object sender, NewFrameEventArgs eventArgs)
//        {
//            try
//            {
//                lock (lockObject)
//                {
//                    if (capturedImage != null)
//                    {
//                        capturedImage.Dispose();
//                    }
//                    capturedImage = (Bitmap)eventArgs.Frame.Clone();
//                }

//                OnImageCaptured(new ImageCapturedEventArgs((Bitmap)eventArgs.Frame.Clone()));
//            }
//            catch (Exception ex)
//            {
//                OnCaptureError(new WebcamErrorEventArgs($"Preview frame error: {ex.Message}"));
//            }
//        }

//        #endregion

//        #region Image Conversion

//        /// <summary>
//        /// Converts Bitmap to Base64 string for network transfer
//        /// </summary>
//        public static string BitmapToBase64(Bitmap image, ImageFormat format = null)
//        {
//            if (image == null)
//                return null;

//            try
//            {
//                format = format ?? ImageFormat.Jpeg;

//                using (MemoryStream ms = new MemoryStream())
//                {
//                    image.Save(ms, format);
//                    byte[] imageBytes = ms.ToArray();
//                    return Convert.ToBase64String(imageBytes);
//                }
//            }
//            catch (Exception ex)
//            {
//                System.Diagnostics.Debug.WriteLine($"BitmapToBase64 error: {ex.Message}");
//                return null;
//            }
//        }

//        /// <summary>
//        /// Converts Base64 string to Bitmap
//        /// </summary>
//        public static Bitmap Base64ToBitmap(string base64String)
//        {
//            if (string.IsNullOrEmpty(base64String))
//                return null;

//            try
//            {
//                byte[] imageBytes = Convert.FromBase64String(base64String);
//                using (MemoryStream ms = new MemoryStream(imageBytes))
//                {
//                    return new Bitmap(ms);
//                }
//            }
//            catch (Exception ex)
//            {
//                System.Diagnostics.Debug.WriteLine($"Base64ToBitmap error: {ex.Message}");
//                return null;
//            }
//        }

//        /// <summary>
//        /// Saves bitmap to file
//        /// </summary>
//        public static bool SaveBitmapToFile(Bitmap image, string filePath, ImageFormat format = null)
//        {
//            if (image == null || string.IsNullOrEmpty(filePath))
//                return false;

//            try
//            {
//                format = format ?? ImageFormat.Jpeg;

//                string directory = Path.GetDirectoryName(filePath);
//                if (!Directory.Exists(directory))
//                {
//                    Directory.CreateDirectory(directory);
//                }

//                image.Save(filePath, format);
//                return true;
//            }
//            catch (Exception ex)
//            {
//                System.Diagnostics.Debug.WriteLine($"SaveBitmapToFile error: {ex.Message}");
//                return false;
//            }
//        }

//        /// <summary>
//        /// Loads bitmap from file
//        /// </summary>
//        public static Bitmap LoadBitmapFromFile(string filePath)
//        {
//            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
//                return null;

//            try
//            {
//                return new Bitmap(filePath);
//            }
//            catch (Exception ex)
//            {
//                System.Diagnostics.Debug.WriteLine($"LoadBitmapFromFile error: {ex.Message}");
//                return null;
//            }
//        }

//        /// <summary>
//        /// Resizes bitmap while maintaining aspect ratio
//        /// </summary>
//        public static Bitmap ResizeBitmap(Bitmap image, int maxWidth, int maxHeight)
//        {
//            if (image == null)
//                return null;

//            try
//            {
//                double ratioX = (double)maxWidth / image.Width;
//                double ratioY = (double)maxHeight / image.Height;
//                double ratio = Math.Min(ratioX, ratioY);

//                int newWidth = (int)(image.Width * ratio);
//                int newHeight = (int)(image.Height * ratio);

//                Bitmap resized = new Bitmap(newWidth, newHeight);
//                using (Graphics g = Graphics.FromImage(resized))
//                {
//                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
//                    g.DrawImage(image, 0, 0, newWidth, newHeight);
//                }

//                return resized;
//            }
//            catch (Exception ex)
//            {
//                System.Diagnostics.Debug.WriteLine($"ResizeBitmap error: {ex.Message}");
//                return null;
//            }
//        }

//        #endregion

//        #region Event Raising

//        protected virtual void OnImageCaptured(ImageCapturedEventArgs e)
//        {
//            ImageCaptured?.Invoke(this, e);
//        }

//        protected virtual void OnCaptureError(WebcamErrorEventArgs e)
//        {
//            CaptureError?.Invoke(this, e);
//        }

//        #endregion

//        #region IDisposable

//        public void Dispose()
//        {
//            StopCapture();

//            if (capturedImage != null)
//            {
//                capturedImage.Dispose();
//                capturedImage = null;
//            }
//        }

//        #endregion
//    }

//    #region Event Args

//    public class ImageCapturedEventArgs : EventArgs
//    {
//        public Bitmap Image { get; private set; }
//        public DateTime CaptureTime { get; private set; }

//        public ImageCapturedEventArgs(Bitmap image)
//        {
//            Image = image;
//            CaptureTime = DateTime.Now;
//        }
//    }

//    public class WebcamErrorEventArgs : EventArgs
//    {
//        public string ErrorMessage { get; private set; }
//        public DateTime ErrorTime { get; private set; }

//        public WebcamErrorEventArgs(string errorMessage)
//        {
//            ErrorMessage = errorMessage;
//            ErrorTime = DateTime.Now;
//        }
//    }

//    #endregion

//    #region Usage Example

//    /*
//     * USAGE EXAMPLE:
//     * 
//     * // Create webcam helper
//     * using (var webcam = new WebcamHelper())
//     * {
//     *     // Subscribe to events
//     *     webcam.ImageCaptured += (s, e) => 
//     *     {
//     *         Console.WriteLine("Image captured!");
//     *         // Display image or process it
//     *     };
//     *     
//     *     webcam.CaptureError += (s, e) => 
//     *     {
//     *         Console.WriteLine($"Error: {e.ErrorMessage}");
//     *     };
//     *     
//     *     // Check if webcam is available
//     *     if (webcam.IsDeviceAvailable)
//     *     {
//     *         // Capture single image
//     *         Bitmap image = webcam.CaptureImage();
//     *         
//     *         if (image != null)
//     *         {
//     *             // Convert to Base64 for network transfer
//     *             string base64 = WebcamHelper.BitmapToBase64(image, ImageFormat.Jpeg);
//     *             
//     *             // Send to server via WCF
//     *             serviceClient.UploadLoginImage(sessionId, userId, base64);
//     *             
//     *             // Or save to file
//     *             WebcamHelper.SaveBitmapToFile(image, "captured_image.jpg");
//     *         }
//     *     }
//     *     else
//     *     {
//     *         Console.WriteLine("No webcam available");
//     *     }
//     * }
//     * 
//     * REQUIRED NUGET PACKAGES:
//     * Install-Package AForge
//     * Install-Package AForge.Video
//     * Install-Package AForge.Video.DirectShow
//     */

//    #endregion
//}