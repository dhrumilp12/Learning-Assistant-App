using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SpeechTranslator.Hubs;
using SpeechTranslator.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        
        private static readonly Dictionary<string, HashSet<double>> _processedSegments = new Dictionary<string, HashSet<double>>();
        
        // Add a collection to track temporary files for cleanup
        private static readonly Dictionary<string, List<string>> _tempFilesTracker = new Dictionary<string, List<string>>();
        
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
                await _hubContext.Clients.All.SendAsync("TranslationStarted");
                
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
                
                var (originalText, translatedText) = _speechService.GetFullText();
                
                await _hubContext.Clients.All.SendAsync("ReceiveFullTranslation", originalText, translatedText);
                
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

                var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{Path.GetExtension(videoFile.FileName)}");
                
                var sessionId = Guid.NewGuid().ToString();
                _tempFilesTracker[sessionId] = new List<string> { tempFilePath };
                
                using (var stream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await videoFile.CopyToAsync(stream);
                }
                
                _logger.LogInformation($"Video saved to {tempFilePath}, starting processing");
                
                _processedSegments.Clear();
                
                var result = await _videoService.StartVideoProcessingAsync(tempFilePath, sourceLanguage, targetLanguage, _hubContext);
                
                if (result)
                {
                    _ = Task.Run(async () => 
                    {
                        try 
                        {
                            var audioFilePath = Path.ChangeExtension(tempFilePath, ".wav");
                            if (File.Exists(audioFilePath))
                            {
                                if (_tempFilesTracker.ContainsKey(sessionId))
                                    _tempFilesTracker[sessionId].Add(audioFilePath);
                            }

                            await ProcessEntireVideoAudio(tempFilePath, sourceLanguage, targetLanguage, isFullText: true);
                            
                            CleanupTempFiles(sessionId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing video speech in segments");
                            CleanupTempFiles(sessionId);
                        }
                    });
                
                    return Ok(new { message = "Video translation started", sessionId });
                }
                else
                {
                    CleanupTempFiles(sessionId);
                    return StatusCode(500, new { error = "Failed to start video processing" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing video for translation");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private void CleanupTempFiles(string sessionId)
        {
            if (_tempFilesTracker.TryGetValue(sessionId, out var tempFiles))
            {
                foreach (var file in tempFiles)
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                            _logger.LogInformation($"Deleted temporary file: {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to delete temporary file: {file}");
                    }
                }
                
                _tempFilesTracker.Remove(sessionId);
            }
        }

        private async Task<double> GetVideoDurationAsync(string videoPath)
        {
            try
            {
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
            string segmentAudioPath = string.Empty;
            try
            {
                segmentAudioPath = Path.Combine(
                    Path.GetTempPath(), 
                    $"segment_{Guid.NewGuid()}.wav"
                );
                
                string fileKey = Path.GetFileName(videoPath);
                string sessionId = _tempFilesTracker.Keys.FirstOrDefault(k => _tempFilesTracker[k].Any(f => f.Contains(fileKey)));
                if (sessionId != null)
                {
                    _tempFilesTracker[sessionId].Add(segmentAudioPath);
                }

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

                try
                {
                    string recognizedText = await _speechService.ConvertSpeechToTextAsync(segmentAudioPath);
                    
                    try 
                    { 
                        File.Delete(segmentAudioPath);
                        if (sessionId != null && _tempFilesTracker.ContainsKey(sessionId))
                        {
                            _tempFilesTracker[sessionId].Remove(segmentAudioPath);
                        }
                    } 
                    catch (Exception ex) 
                    {
                        _logger.LogWarning(ex, $"Failed to delete segment file: {segmentAudioPath}");
                    }
                    
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
                if (!string.IsNullOrEmpty(segmentAudioPath) && File.Exists(segmentAudioPath))
                {
                    try { File.Delete(segmentAudioPath); } catch { }
                }
                return string.Empty;
            }
        }

        private async Task ProcessEntireVideoAudio(string videoPath, string sourceLanguage, string targetLanguage, bool isFullText = false)
        {
            try
            {
                _logger.LogInformation("Processing entire video audio");
                
                string fileKey = Path.GetFileName(videoPath);
                if (isFullText && _processedSegments.ContainsKey(fileKey) && _processedSegments[fileKey].Contains(-1))
                {
                    _logger.LogInformation("Full text already processed, skipping");
                    return;
                }
                
                if (isFullText && _processedSegments.ContainsKey(fileKey))
                {
                    _processedSegments[fileKey].Add(-1);
                }
                
                var audioText = await _speechService.ConvertSpeechToTextFromVideoAsync(videoPath);
                
                if (string.IsNullOrWhiteSpace(audioText))
                {
                    _logger.LogWarning("No speech detected in the full video");
                    
                    if (!isFullText)
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
                
                var fullTranslation = await _translationService.TranslateTextAsync(
                    sourceLanguage, targetLanguage, audioText);
                    
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

        [HttpPost("stop-video")]
        public async Task<IActionResult> StopVideoTranslation()
        {
            try
            {
                await _videoService.StopVideoProcessingAsync();
                _processedSegments.Clear();
                
                foreach (var sessionId in _tempFilesTracker.Keys.ToList())
                {
                    CleanupTempFiles(sessionId);
                }
                
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
