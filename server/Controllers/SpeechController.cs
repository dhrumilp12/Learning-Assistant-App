using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SpeechTranslator.Hubs;
using SpeechTranslator.Services;
using System;
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
                    // Also extract audio for speech translation
                    _ = Task.Run(async () => 
                    {
                        try 
                        {
                            // Extract audio from the video
                            var audioText = await _speechService.ConvertSpeechToTextFromVideoAsync(tempFilePath);
                            
                            // Translate it
                            var translatedAudioText = await _translationService.TranslateTextAsync(
                                sourceLanguage, targetLanguage, audioText);
                                
                            // Send it to clients
                            await _hubContext.Clients.All.SendAsync(
                                "ReceiveVideoSpeechTranslation", 
                                audioText, 
                                translatedAudioText,
                                sourceLanguage,
                                targetLanguage
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing video speech");
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
