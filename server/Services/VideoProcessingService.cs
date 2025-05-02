using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Microsoft.AspNetCore.SignalR;
using OpenCvSharp;
using SpeechTranslator.Hubs;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpeechTranslator.Services
{
    public class VideoProcessingService
    {
        private readonly string _visionApiKey;
        private readonly string _visionEndpoint;
        private readonly TranslationService _translationService;
        private readonly ILogger<VideoProcessingService> _logger;
        private bool _isProcessing;
        private VideoCapture? _videoCapture;
        
        // Track detected text regions and their translations
        private readonly Dictionary<Rectangle, (string Original, string Translated)> _textRegions = new();

        public VideoProcessingService(string visionApiKey, string visionEndpoint, TranslationService translationService, ILogger<VideoProcessingService> logger)
        {
            _visionApiKey = visionApiKey;
            _visionEndpoint = visionEndpoint;
            _translationService = translationService;
            _logger = logger;
            _isProcessing = false;
        }

        // Fixed the async without await warning by using Task.FromResult
        public Task<bool> StartVideoProcessingAsync(string videoPath, string sourceLanguage, string targetLanguage, IHubContext<TranslationHub> hubContext)
        {
            if (_isProcessing)
            {
                _logger.LogWarning("Video processing is already in progress");
                return Task.FromResult(false);
            }

            try
            {
                _videoCapture = new VideoCapture(videoPath);
                if (!_videoCapture.IsOpened())
                {
                    _logger.LogError($"Failed to open video file at {videoPath}");
                    return Task.FromResult(false);
                }

                _isProcessing = true;
                _textRegions.Clear();
                
                // Start processing in a background task
                _ = Task.Run(async () => 
                {
                    await ProcessVideoFramesAsync(sourceLanguage, targetLanguage, hubContext);
                });
                
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting video processing");
                return Task.FromResult(false);
            }
        }

        private async Task ProcessVideoFramesAsync(string sourceLanguage, string targetLanguage, IHubContext<TranslationHub> hubContext)
        {
            if (_videoCapture == null || !_videoCapture.IsOpened())
            {
                _logger.LogError("Video capture is not initialized");
                return;
            }

            try
            {
                var fps = _videoCapture.Get(VideoCaptureProperties.Fps);
                var frameDelay = 1000 / fps;
                var totalFrames = _videoCapture.Get(VideoCaptureProperties.FrameCount);
                var frameWidth = _videoCapture.Get(VideoCaptureProperties.FrameWidth);
                var frameHeight = _videoCapture.Get(VideoCaptureProperties.FrameHeight);
                
                // Report video metadata to clients
                await hubContext.Clients.All.SendAsync("VideoMetadata", new
                {
                    Fps = fps,
                    TotalFrames = totalFrames,
                    Width = frameWidth,
                    Height = frameHeight
                });

                // Process every 30th frame for OCR (adjust based on performance needs)
                int frameSkipRate = 30;
                int frameCount = 0;

                while (_isProcessing)
                {
                    using var frame = new Mat();
                    if (!_videoCapture.Read(frame) || frame.Empty())
                    {
                        _logger.LogInformation("End of video reached");
                        break;
                    }

                    frameCount++;
                    
                    // Process text detection every Nth frame
                    if (frameCount % frameSkipRate == 1)
                    {
                        await DetectAndTranslateTextInFrameAsync(frame, sourceLanguage, targetLanguage, hubContext);
                    }
                    
                    // Create frame with overlaid translations
                    using var processedFrame = OverlayTranslationsOnFrame(frame);
                    
                    // Convert to base64 for sending via SignalR
                    string frameBase64 = ConvertFrameToBase64(processedFrame);
                    
                    // Send the processed frame to clients
                    await hubContext.Clients.All.SendAsync("ReceiveVideoFrame", frameBase64, frameCount, totalFrames);
                    
                    // Respect original video frame rate
                    await Task.Delay((int)frameDelay);
                }
                
                // Notify clients that processing is complete
                await hubContext.Clients.All.SendAsync("VideoProcessingComplete");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing video frames");
                await hubContext.Clients.All.SendAsync("VideoProcessingError", ex.Message);
            }
            finally
            {
                _isProcessing = false;
                _videoCapture?.Dispose();
                _videoCapture = null;
            }
        }

        private async Task DetectAndTranslateTextInFrameAsync(Mat frame, string sourceLanguage, string targetLanguage, IHubContext<TranslationHub> hubContext)
        {
            try
            {
                // Convert OpenCV Mat to byte array
                byte[] imageBytes;
                using (var memoryStream = new MemoryStream())
                {
                    frame.WriteToStream(memoryStream);
                    imageBytes = memoryStream.ToArray();
                }
                
                // Create a client for Azure Computer Vision
                var credential = new AzureKeyCredential(_visionApiKey);
                var client = new ImageAnalysisClient(new Uri(_visionEndpoint), credential);
                
                // Define visual features for text detection (ReadAPI)
                var visualFeatures = VisualFeatures.Read;

                // Analyze image to detect text
                var imageContent = BinaryData.FromBytes(imageBytes);
                var options = new ImageAnalysisOptions { Language = sourceLanguage };
                var result = await client.AnalyzeAsync(imageContent, visualFeatures, options);
                
                if (result?.Value?.Read != null)
                {
                    // Clear previous text regions for this frame
                    _textRegions.Clear();
                    
                    // Process text blocks and lines correctly based on API structure
                    foreach (var block in result.Value.Read.Blocks)
                    {
                        foreach (var line in block.Lines)
                        {
                            string originalText = line.Text;
                            
                            // Get bounding polygon
                            if (line.BoundingPolygon.Count >= 4)
                            {
                                // Create a bounding box from the polygon points
                                int minX = (int)line.BoundingPolygon.Min(p => p.X);
                                int minY = (int)line.BoundingPolygon.Min(p => p.Y);
                                int maxX = (int)line.BoundingPolygon.Max(p => p.X);
                                int maxY = (int)line.BoundingPolygon.Max(p => p.Y);
                                
                                var boundingBox = new Rectangle(
                                    minX, 
                                    minY,
                                    maxX - minX,
                                    maxY - minY
                                );
                                
                                // Translate the detected text
                                string translatedText = await _translationService.TranslateTextAsync(
                                    sourceLanguage, 
                                    targetLanguage, 
                                    originalText
                                );
                                
                                // Store the text and its location
                                _textRegions[boundingBox] = (originalText, translatedText);
                                
                                // Notify clients about detected and translated text
                                await hubContext.Clients.All.SendAsync(
                                    "TextDetectedInVideo", 
                                    originalText, 
                                    translatedText,
                                    new { X = boundingBox.X, Y = boundingBox.Y, Width = boundingBox.Width, Height = boundingBox.Height }
                                );
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting text in frame: {Message}", ex.Message);
            }
        }

        private Mat OverlayTranslationsOnFrame(Mat frame)
        {
            // Create a copy of the frame to work with
            var processedFrame = frame.Clone();
            
            foreach (var region in _textRegions)
            {
                var box = region.Key;
                var (originalText, translatedText) = region.Value;
                
                // Create a background for the text (semi-transparent rectangle)
                Cv2.Rectangle(
                    processedFrame,
                    new OpenCvSharp.Point(box.X, box.Y),
                    new OpenCvSharp.Point(box.X + box.Width, box.Y + box.Height),
                    new Scalar(0, 0, 0, 150),
                    Cv2.FILLED
                );
                
                if (!string.IsNullOrEmpty(translatedText))
                {
                    try
                    {
                        // Use System.Drawing for Unicode text rendering
                        using (var bitmap = new Bitmap(box.Width, box.Height))
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            // Set up high quality text rendering
                            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                            
                            // Fill with transparent background
                            graphics.Clear(Color.Transparent);
                            
                            // Draw the translated text with appropriate font size based on box size
                            float fontSize = Math.Max(10, Math.Min(16, box.Height / 2));
                            using (var font = new Font("Arial", fontSize, FontStyle.Bold))
                            using (var brush = new SolidBrush(Color.White))
                            {
                                // Position text within the box
                                var padding = 5;
                                var textRect = new RectangleF(
                                    padding, 
                                    padding, 
                                    box.Width - (2 * padding),
                                    box.Height - (2 * padding)
                                );
                                
                                // Create format for center alignment
                                using (var format = new StringFormat())
                                {
                                    format.Alignment = StringAlignment.Center;
                                    format.LineAlignment = StringAlignment.Center;
                                    graphics.DrawString(translatedText, font, brush, textRect, format);
                                }
                            }
                            
                            // Convert bitmap to OpenCV Mat
                            using (var memoryStream = new MemoryStream())
                            {
                                bitmap.Save(memoryStream, ImageFormat.Png);
                                memoryStream.Position = 0;
                                
                                using (var textMat = Mat.FromImageData(memoryStream.ToArray()))
                                {
                                    if (!textMat.Empty())
                                    {
                                        // Create an ROI for the text overlay
                                        var roi = new OpenCvSharp.Rect(
                                            box.X, 
                                            box.Y,
                                            Math.Min(textMat.Width, box.Width),
                                            Math.Min(textMat.Height, box.Height)
                                        );
                                        
                                        // Ensure ROI is within frame boundaries
                                        if (roi.X >= 0 && roi.Y >= 0 && 
                                            roi.X + roi.Width <= processedFrame.Width && 
                                            roi.Y + roi.Height <= processedFrame.Height)
                                        {
                                            // Create a mask from alpha channel (if available)
                                            using (var mask = textMat.Channels() == 4 ? textMat.ExtractChannel(3) : null)
                                            {
                                                // Fixed error: Don't assign to a using variable
                                                Mat textBgr;
                                                if (textMat.Channels() == 4)
                                                {
                                                    textBgr = new Mat();
                                                    Cv2.CvtColor(textMat, textBgr, ColorConversionCodes.BGRA2BGR);
                                                }
                                                else
                                                {
                                                    textBgr = textMat.Clone();
                                                }
                                                
                                                using (textBgr) // Add using here after assignment
                                                {
                                                    // Create a Mat for the destination ROI
                                                    using (var roiMat = new Mat(processedFrame, roi))
                                                    {
                                                        // Resize text image to match ROI
                                                        using (var resizedText = new Mat())
                                                        {
                                                            Cv2.Resize(textBgr, resizedText, new OpenCvSharp.Size(roi.Width, roi.Height));
                                                            
                                                            if (mask != null)
                                                            {
                                                                using (var resizedMask = new Mat())
                                                                {
                                                                    Cv2.Resize(mask, resizedMask, new OpenCvSharp.Size(roi.Width, roi.Height));
                                                                    resizedText.CopyTo(roiMat, resizedMask);
                                                                }
                                                            }
                                                            else
                                                            {
                                                                // If no alpha channel, just copy directly
                                                                resizedText.CopyTo(roiMat);
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error rendering Unicode text with System.Drawing");
                        
                        // Fallback: Use ASCII-only text with OpenCV
                        string asciiText = ReplaceNonAsciiWithDots(translatedText);
                        Cv2.PutText(
                            processedFrame,
                            asciiText,
                            new OpenCvSharp.Point(box.X + 5, box.Y + box.Height - 10),
                            HersheyFonts.HersheySimplex,
                            0.7,
                            new Scalar(255, 255, 255),
                            2
                        );
                    }
                }
            }
            
            return processedFrame;
        }
        
        private string ReplaceNonAsciiWithDots(string text)
        {
            // Replace non-ASCII characters with dots as a fallback
            return string.Join("", text.Select(c => c < 128 ? c : '.'));
        }

        private string ConvertFrameToBase64(Mat frame)
        {
            using var memoryStream = new MemoryStream();
            frame.WriteToStream(memoryStream);
            byte[] imageBytes = memoryStream.ToArray();
            return Convert.ToBase64String(imageBytes);
        }

        public async Task StopVideoProcessingAsync()
        {
            _isProcessing = false;
            
            // Give time for the processing loop to exit gracefully
            await Task.Delay(100);
            
            _videoCapture?.Dispose();
            _videoCapture = null;
        }
    }
}