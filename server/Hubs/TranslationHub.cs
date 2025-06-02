using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;
using SpeechTranslator.Services;
using System.IO;

namespace SpeechTranslator.Hubs
{
    public class TranslationHub : Hub
    {
        private readonly ILogger<TranslationHub> _logger;
        private readonly VideoProcessingService _videoService;
        private readonly SpeechToTextService _speechService;
        private readonly TranslationService _translationService;
        
        public TranslationHub(
            ILogger<TranslationHub> logger, 
            VideoProcessingService videoService,
            SpeechToTextService speechService,
            TranslationService translationService)
        {
            _logger = logger;
            _videoService = videoService;
            _speechService = speechService;
            _translationService = translationService;
        }
        
        public async Task SendTranslation(string originalText, string translatedText, string sourceLanguage, string targetLanguage)
        {
            await Clients.All.SendAsync("ReceiveTranslation", originalText, translatedText, sourceLanguage, targetLanguage);
        }

        public async Task SendInterimTranslation(string originalText, string translatedText, string sourceLanguage, string targetLanguage)
        {
            await Clients.All.SendAsync("ReceiveInterimTranslation", originalText, translatedText, sourceLanguage, targetLanguage);
        }

        public async Task SendFullTranslation(string originalText, string translatedText)
        {
            await Clients.All.SendAsync("ReceiveFullTranslation", originalText, translatedText);
        }

        public async Task SendMessage(string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", message);
        }

        // Video translation methods
        public async Task SendVideoTranslationStatus(string status)
        {
            await Clients.All.SendAsync("VideoTranslationStatus", status);
        }

        public async Task SendFullVideoSpeechTranslation(string originalText, string translatedText, string sourceLanguage, string targetLanguage)
        {
            await Clients.All.SendAsync("ReceiveFullVideoSpeechTranslation", originalText, translatedText, sourceLanguage, targetLanguage);
        }

        public async Task SendVideoFrame(string frameBase64, int frameNumber, int totalFrames)
        {
            await Clients.All.SendAsync("ReceiveVideoFrame", frameBase64, frameNumber, totalFrames);
        }

        public async Task SendDetectedText(string originalText, string translatedText, object boundingBox)
        {
            await Clients.All.SendAsync("TextDetectedInVideo", originalText, translatedText, boundingBox);
        }

        public async Task SendVideoMetadata(object metadata)
        {
            await Clients.All.SendAsync("VideoMetadata", metadata);
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"Client connected: {Context.ConnectionId}");
            await Clients.Caller.SendAsync("Connected", "Connection established");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
            if (exception != null)
            {
                _logger.LogError(exception, "Error during client disconnection");
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task ProcessLiveVideoFrame(string frameBase64, string sourceLanguage, string targetLanguage)
        {
            try
            {
                _logger.LogDebug("Received live video frame for processing");
                
                // Convert base64 string to byte array
                byte[] frameBytes = Convert.FromBase64String(frameBase64);
                
                // Process the frame and get results
                var (processedFrameBase64, detectedTexts) = await _videoService.ProcessLiveFrameAsync(
                    frameBytes, 
                    sourceLanguage, 
                    targetLanguage
                );
                
                // Send processed frame back to caller only (not to all clients)
                await Clients.Caller.SendAsync("ReceiveProcessedFrame", processedFrameBase64, detectedTexts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing live video frame");
                await Clients.Caller.SendAsync("VideoProcessingError", ex.Message);
            }
        }

        public async Task ProcessLiveAudio(string audioBase64, string sourceLanguage, string targetLanguage, bool isFinal = false)
        {
            try
            {
                _logger.LogDebug($"Received live audio for processing (isFinal: {isFinal})");
                
                // Convert base64 string to byte array
                byte[] audioBytes = Convert.FromBase64String(audioBase64);
                
                // Skip tiny audio fragments that likely don't contain speech
                if (audioBytes.Length < 1000)
                {
                    _logger.LogDebug($"Audio data too small ({audioBytes.Length} bytes), skipping");
                    return;
                }
                
                // Create a temporary file for the audio
                string tempAudioFile = Path.Combine(
                    Path.GetTempPath(),
                    $"live_audio_{Guid.NewGuid()}.webm"
                );
                
                try
                {
                    // Save the audio to a temporary file
                    await File.WriteAllBytesAsync(tempAudioFile, audioBytes);
                    
                    // Convert webm to wav with optimized settings
                    string wavFile = Path.ChangeExtension(tempAudioFile, ".wav");
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        // Use optimized settings for speech recognition
                        FileName = "cmd.exe",
                        Arguments = $"/C ffmpeg -i \"{tempAudioFile}\" -ac 1 -ar 16000 -vn -acodec pcm_s16le \"{wavFile}\" -y",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    var process = System.Diagnostics.Process.Start(processInfo);
                    if (process == null)
                    {
                        _logger.LogError("Failed to start FFmpeg process");
                        return;
                    }
                    
                    // Don't wait too long for small audio chunks
                    var waitTask = process.WaitForExitAsync();
                    var completedTask = await Task.WhenAny(waitTask, Task.Delay(5000)); // 5 second timeout
                    
                    if (completedTask != waitTask)
                    {
                        _logger.LogWarning("FFmpeg process timed out, killing process");
                        try { process.Kill(); } catch { }
                        return;
                    }
                    
                    if (process.ExitCode == 0)
                    {
                        try
                        {
                            // Special handling for final chunks
                            if (isFinal)
                            {
                                _logger.LogDebug("Processing final audio chunk");
                            }
                            
                            // Process the audio with speech-to-text
                            string recognizedText = await _speechService.ConvertSpeechToTextAsync(wavFile);
                            
                            if (!string.IsNullOrWhiteSpace(recognizedText))
                            {
                                // Translate the recognized text
                                string translatedText = await _translationService.TranslateTextAsync(
                                    sourceLanguage,
                                    targetLanguage,
                                    recognizedText
                                );
                                
                                // Send back to the client
                                await Clients.Caller.SendAsync(
                                    isFinal ? "ReceiveFullVideoSpeechTranslation" : "ReceiveVideoSpeechTranslation",
                                    recognizedText,
                                    translatedText,
                                    sourceLanguage,
                                    targetLanguage
                                );
                                
                                _logger.LogInformation($"Processed live audio{(isFinal ? " (final)" : "")}: '{recognizedText}' -> '{translatedText}'");
                            }
                            else
                            {
                                _logger.LogDebug("No speech detected in live audio segment");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error recognizing speech from audio");
                        }
                    }
                    else
                    {
                        string error = await process.StandardError.ReadToEndAsync();
                        _logger.LogError($"FFmpeg error: {error}");
                    }
                }
                finally
                {
                    // Clean up temporary files
                    try { if (File.Exists(tempAudioFile)) File.Delete(tempAudioFile); } catch { }
                    try { if (File.Exists(Path.ChangeExtension(tempAudioFile, ".wav"))) 
                            File.Delete(Path.ChangeExtension(tempAudioFile, ".wav")); } catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing live audio");
            }
        }
    }
}
