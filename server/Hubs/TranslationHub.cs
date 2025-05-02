using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;

namespace SpeechTranslator.Hubs
{
    public class TranslationHub : Hub
    {
        private readonly ILogger<TranslationHub> _logger;
        
        public TranslationHub(ILogger<TranslationHub> logger)
        {
            _logger = logger;
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
    }
}
