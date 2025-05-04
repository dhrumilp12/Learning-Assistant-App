using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SpeechTranslator.Hubs;
using SpeechTranslator.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SpeechTranslator.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SpeechController : ControllerBase
    {
        private readonly SpeechToTextService _speechService;
        private readonly TranslationService _translationService;
        private readonly VideoProcessingService _videoService;
        private readonly IHubContext<TranslationHub> _hubContext;
        private readonly ILogger<SpeechController> _logger;
        
        public SpeechController(
            SpeechToTextService speechService, 
            TranslationService translationService,
            VideoProcessingService videoService,
            IHubContext<TranslationHub> hubContext,
            ILogger<SpeechController> logger)
        {
            _speechService = speechService;
            _translationService = translationService;
            _videoService = videoService;
            _hubContext = hubContext;
            _logger = logger;
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartTranslation([FromBody] TranslationRequest request)
        {
            try
            {
                // Notify that translation is starting
                await _hubContext.Clients.All.SendAsync("TranslationStarted");
                
                // Start a background task to handle the real-time translation
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var speechStream = _speechService.GetSpeechStreamAsync(request.SourceLanguage, request.TargetLanguage);
                        
                        await foreach (var (originalText, translatedText, isInterim) in speechStream)
                        {
                            if (isInterim)
                            {
                                await _hubContext.Clients.All.SendAsync(
                                    "ReceiveInterimTranslation", 
                                    originalText, 
                                    translatedText, 
                                    request.SourceLanguage, 
                                    request.TargetLanguage
                                );
                            }
                            else
                            {
                                await _hubContext.Clients.All.SendAsync(
                                    "ReceiveTranslation", 
                                    originalText, 
                                    translatedText, 
                                    request.SourceLanguage, 
                                    request.TargetLanguage
                                );
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during translation process");
                        await _hubContext.Clients.All.SendAsync("TranslationError", ex.Message);
                    }
                    finally
                    {
                        await _hubContext.Clients.All.SendAsync("TranslationEnded");
                    }
                });
                
                return Ok(new { message = "Translation started" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting translation");
                return StatusCode(500, new { error = ex.Message });
            }
        }
        
        [HttpPost("stop")]
        public async Task<IActionResult> StopTranslation()
        {
            try 
            {
                await _speechService.StopListeningAsync();
                
                // Get the full accumulated text
                var (originalText, translatedText) = _speechService.GetFullText();
                
                // Send the full text via SignalR
                await _hubContext.Clients.All.SendAsync("ReceiveFullTranslation", originalText, translatedText);
                
                // Send translation ended event
                await _hubContext.Clients.All.SendAsync("TranslationEnded");
                
                return Ok(new { 
                    message = "Translation stopped",
                    originalText,
                    translatedText
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping translation");
                return StatusCode(500, new { error = ex.Message });
            }
        }
        
        // Add a simple status endpoint for testing the backend
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            return Ok(new { 
                status = "Running", 
                timestamp = DateTime.UtcNow,
                message = "Speech translator service is operational"
            });
        }
        
        [HttpPost("translate-video")]
        public async Task<IActionResult> TranslateVideo([FromForm] IFormFile videoFile, [FromForm] string sourceLanguage, [FromForm] string targetLanguage)
        {
            try
            {
                if (videoFile == null || videoFile.Length == 0)
                {
                    return BadRequest("No video file provided");
                }

                // Create a temporary file to store the uploaded video
                var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{Path.GetExtension(videoFile.FileName)}");
                
                // Save the uploaded file to the temporary location
                using (var stream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await videoFile.CopyToAsync(stream);
                }
                
                _logger.LogInformation($"Video saved to {tempFilePath}, starting processing");
                
                // Start video processing
                var result = await _videoService.StartVideoProcessingAsync(tempFilePath, sourceLanguage, targetLanguage, _hubContext);
                
                if (result)
                {
                    // Extract audio in segments for progressive translation
                    _ = Task.Run(async () => 
                    {
                        try 
                        {
                            _logger.LogInformation("Starting audio extraction and processing from video");
                            
                            // Get video duration using FFmpeg
                            var duration = await GetVideoDurationAsync(tempFilePath);
                            _logger.LogInformation($"Video duration: {duration} seconds");
                            
                            if (duration <= 0)
                            {
                                _logger.LogWarning("Could not determine video duration, processing entire audio");
                                await ProcessEntireVideoAudio(tempFilePath, sourceLanguage, targetLanguage);
                                return;
                            }

                            // Process audio in segments
                            // For shorter videos (under 30 seconds), use 3 segments
                            // For longer videos, use more segments
                            int segmentCount = duration < 30 ? 3 : Math.Min(10, (int)(duration / 10));
                            double segmentDuration = duration / segmentCount;
                            
                            _logger.LogInformation($"Processing audio in {segmentCount} segments of approximately {segmentDuration:0.0} seconds each");
                            
                            for (int i = 0; i < segmentCount; i++)
                            {
                                double startTime = i * segmentDuration;
                                double endTime = (i + 1) * segmentDuration;
                                
                                _logger.LogInformation($"Processing audio segment {i+1}/{segmentCount} ({startTime:0.0}s to {endTime:0.0}s)");
                                
                                // Extract and process this audio segment
                                string segmentText = await ExtractAndTranscribeAudioSegment(
                                    tempFilePath, startTime, segmentDuration, sourceLanguage);
                                
                                if (!string.IsNullOrWhiteSpace(segmentText))
                                {
                                    // Translate the segment text
                                    string translatedText = await _translationService.TranslateTextAsync(
                                        sourceLanguage, targetLanguage, segmentText);
                                    
                                    // Send to clients
                                    await _hubContext.Clients.All.SendAsync(
                                        "ReceiveVideoSpeechTranslation", 
                                        segmentText,
                                        translatedText,
                                        sourceLanguage,
                                        targetLanguage
                                    );
                                    
                                    _logger.LogInformation($"Sent translation for segment {i+1}: {segmentText.Substring(0, Math.Min(50, segmentText.Length))}...");
                                    
                                    // Add delay to simulate sequential processing
                                    // Shorter delay for short videos, longer delay for long videos
                                    await Task.Delay((int)(1500 * Math.Min(1, segmentDuration / 5)));
                                }
                                else
                                {
                                    _logger.LogInformation($"No speech detected in segment {i+1}");
                                }
                            }
                            
                            // Process the entire audio as a final step
                            await ProcessEntireVideoAudio(tempFilePath, sourceLanguage, targetLanguage, isFullText: true);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing video speech in segments");
                            await _hubContext.Clients.All.SendAsync(
                                "ReceiveVideoSpeechTranslation", 
                                "Error processing audio segments",
                                "Error al procesar segmentos de audio",
                                sourceLanguage,
                                targetLanguage
                            );
                        }
                    });
                
                    return Ok(new { message = "Video translation started", tempFilePath });
                }
                else
                {
                    System.IO.File.Delete(tempFilePath);
                    return StatusCode(500, new { error = "Failed to start video processing" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing video for translation");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private async Task<double> GetVideoDurationAsync(string videoPath)
        {
            try
            {
                // Use FFmpeg to get video duration
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = System.Diagnostics.Process.Start(processInfo);
                if (process == null) return 0;

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && double.TryParse(output.Trim(), out double duration))
                {
                    return duration;
                }
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting video duration");
                return 0;
            }
        }

        private async Task<string> ExtractAndTranscribeAudioSegment(string videoPath, double startTime, double duration, string language)
        {
            try
            {
                string segmentAudioPath = Path.Combine(
                    Path.GetTempPath(), 
                    $"segment_{Guid.NewGuid()}.wav"
                );

                // Extract audio segment using FFmpeg
                var ffmpegCmd = $"ffmpeg -i \"{videoPath}\" -ss {startTime} -t {duration} " +
                    $"-ac 1 -ar 16000 -vn -q:a 0 " +
                    $"-af \"loudnorm=I=-16:TP=-1.5:LRA=11\" \"{segmentAudioPath}\" -y";

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C {ffmpegCmd}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = System.Diagnostics.Process.Start(processInfo);
                if (process == null) return string.Empty;

                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    string error = await process.StandardError.ReadToEndAsync();
                    _logger.LogError($"FFmpeg error: {error}");
                    return string.Empty;
                }

                // Process the segment audio with speech recognition
                try
                {
                    string recognizedText = await _speechService.ConvertSpeechToTextAsync(segmentAudioPath);
                    
                    // Clean up the temporary file
                    try { System.IO.File.Delete(segmentAudioPath); } catch { }
                    
                    return recognizedText;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error recognizing speech from audio segment");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting audio segment");
                return string.Empty;
            }
        }

        private async Task ProcessEntireVideoAudio(string videoPath, string sourceLanguage, string targetLanguage, bool isFullText = false)
        {
            try
            {
                _logger.LogInformation("Processing entire video audio");
                
                // Extract audio from the video
                var audioText = await _speechService.ConvertSpeechToTextFromVideoAsync(videoPath);
                
                if (string.IsNullOrWhiteSpace(audioText))
                {
                    _logger.LogWarning("No speech detected in the full video");
                    
                    if (!isFullText) // Only send if we haven't already sent segments
                    {
                        await _hubContext.Clients.All.SendAsync(
                            "ReceiveVideoSpeechTranslation", 
                            "No speech detected in video.",
                            "No se detectó habla en el video.",
                            sourceLanguage,
                            targetLanguage
                        );
                    }
                    return;
                }

                _logger.LogInformation($"Full audio text: {audioText}");
                
                // Translate the full text
                var fullTranslation = await _translationService.TranslateTextAsync(
                    sourceLanguage, targetLanguage, audioText);
                    
                // Send to clients with a special flag for full text
                await _hubContext.Clients.All.SendAsync(
                    "ReceiveFullVideoSpeechTranslation", 
                    audioText,
                    fullTranslation,
                    sourceLanguage,
                    targetLanguage
                );
                
                _logger.LogInformation("Sent full video speech translation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing full video audio");
            }
        }

        private async Task ProcessAudioTextInChunks(string audioText, string sourceLanguage, string targetLanguage)
        {
            try
            {
                // Try different approaches to split the text into meaningful chunks
                
                // Approach 1: Split by sentence ending punctuation
                var sentenceDelimiters = new[] { ". ", "! ", "? ", ".\n", "!\n", "?\n" };
                var sentences = new List<string>();
                
                int startPos = 0;
                for (int i = 0; i < audioText.Length; i++)
                {
                    foreach (var delimiter in sentenceDelimiters)
                    {
                        if (i + delimiter.Length <= audioText.Length && 
                            audioText.Substring(i, delimiter.Length) == delimiter)
                        {
                            // Found a sentence delimiter
                            var sentence = audioText.Substring(startPos, i - startPos + 1).Trim();
                            if (!string.IsNullOrEmpty(sentence))
                            {
                                sentences.Add(sentence);
                            }
                            startPos = i + delimiter.Length;
                            break;
                        }
                    }
                }
                
                // Add the last part if any remains
                if (startPos < audioText.Length)
                {
                    var lastSentence = audioText.Substring(startPos).Trim();
                    if (!string.IsNullOrEmpty(lastSentence))
                    {
                        sentences.Add(lastSentence);
                    }
                }
                
                // Alternative approach if no sentences were found
                if (sentences.Count <= 1)
                {
                    // Approach 2: Split by commas and other punctuation
                    var secondaryDelimiters = new[] { ", ", "; " };
                    sentences.Clear();
                    startPos = 0;
                    
                    for (int i = 0; i < audioText.Length; i++)
                    {
                        foreach (var delimiter in secondaryDelimiters)
                        {
                            if (i + delimiter.Length <= audioText.Length && 
                                audioText.Substring(i, delimiter.Length) == delimiter)
                            {
                                // Found a delimiter
                                var chunk = audioText.Substring(startPos, i - startPos + 1).Trim();
                                if (!string.IsNullOrEmpty(chunk))
                                {
                                    sentences.Add(chunk);
                                }
                                startPos = i + delimiter.Length;
                                break;
                            }
                        }
                    }
                    
                    // Add the last part if any remains
                    if (startPos < audioText.Length)
                    {
                        var lastChunk = audioText.Substring(startPos).Trim();
                        if (!string.IsNullOrEmpty(lastChunk))
                        {
                            sentences.Add(lastChunk);
                        }
                    }
                }
                
                // If we still don't have multiple chunks, create artificial ones
                if (sentences.Count <= 1 && audioText.Length > 30)
                {
                    // Approach 3: Split by character count (last resort)
                    sentences.Clear();
                    int chunkSize = 30;
                    
                    for (int i = 0; i < audioText.Length; i += chunkSize)
                    {
                        int length = Math.Min(chunkSize, audioText.Length - i);
                        sentences.Add(audioText.Substring(i, length));
                    }
                }
                
                _logger.LogInformation($"Split audio text into {sentences.Count} chunks");
                
                // Process each chunk with a delay between them
                int chunkCount = 0;
                foreach (var sentence in sentences)
                {
                    // Skip the first sentence as we already sent the full text
                    if (chunkCount++ == 0) 
                        continue;
                        
                    if (!string.IsNullOrWhiteSpace(sentence))
                    {
                        try
                        {
                            // Translate the chunk
                            var translatedChunk = await _translationService.TranslateTextAsync(
                                sourceLanguage, targetLanguage, sentence);
                                
                            // Send as a separate transcription event
                            await _hubContext.Clients.All.SendAsync(
                                "ReceiveVideoSpeechTranslation", 
                                sentence, 
                                translatedChunk,
                                sourceLanguage,
                                targetLanguage
                            );
                            
                            _logger.LogInformation($"Sent chunk {chunkCount}: {sentence}");
                            
                            // Add a delay between chunks (simulate real-time transcription)
                            await Task.Delay(1500);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error processing chunk: {sentence}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio text chunks");
            }
        }

        [HttpPost("stop-video")]
        public async Task<IActionResult> StopVideoTranslation()
        {
            try
            {
                await _videoService.StopVideoProcessingAsync();
                return Ok(new { message = "Video translation stopped" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping video translation");
                return StatusCode(500, new { error = ex.Message });
            }
        }
        
        public class TranslationRequest
        {
            public string SourceLanguage { get; set; } = "en";
            public string TargetLanguage { get; set; } = "es";
        }
    }
}
