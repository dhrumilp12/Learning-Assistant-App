using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SpeechTranslator.Hubs;
using SpeechTranslator.Services;
using System;
using System.Threading.Tasks;

namespace SpeechTranslator.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SpeechController : ControllerBase
    {
        private readonly SpeechToTextService _speechService;
        private readonly TranslationService _translationService;
        private readonly IHubContext<TranslationHub> _hubContext;
        private readonly ILogger<SpeechController> _logger;
        
        public SpeechController(
            SpeechToTextService speechService, 
            TranslationService translationService,
            IHubContext<TranslationHub> hubContext,
            ILogger<SpeechController> logger)
        {
            _speechService = speechService;
            _translationService = translationService;
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
        
        public class TranslationRequest
        {
            public string SourceLanguage { get; set; } = "en";
            public string TargetLanguage { get; set; } = "es";
        }
    }
}
