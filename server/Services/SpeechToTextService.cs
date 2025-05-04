using Microsoft.AspNetCore.SignalR;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using SpeechTranslator.Hubs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SpeechTranslator.Services
{
    public class SpeechToTextService
    {
        private readonly SpeechConfig _speechConfig;
        private readonly TranslationService _translationService;
        private SpeechRecognizer? _speechRecognizer;
        private SpeechSynthesizer? _speechSynthesizer;
        private bool _isListening;

        // Add properties to accumulate text
        private readonly StringBuilder _accumulatedOriginalText = new();
        private readonly StringBuilder _accumulatedTranslatedText = new();
        
        // Track the last interim text to avoid duplicates
        private string _lastInterimText = string.Empty;

        // Add property to access the accumulated texts
        public (string Original, string Translated) AccumulatedTexts => 
            (_accumulatedOriginalText.ToString(), _accumulatedTranslatedText.ToString());

        public SpeechToTextService(string speechEndpoint, string speechKey)
        {
            if (string.IsNullOrEmpty(speechEndpoint))
                throw new ArgumentNullException(nameof(speechEndpoint));
            if (string.IsNullOrEmpty(speechKey))
                throw new ArgumentNullException(nameof(speechKey));
                
            _speechConfig = SpeechConfig.FromEndpoint(new Uri(speechEndpoint), speechKey);
            
            // Get environment variables with proper null checking and defaults
            string translatorApiKey = Environment.GetEnvironmentVariable("TRANSLATOR_API_KEY") 
                ?? throw new ArgumentException("TRANSLATOR_API_KEY environment variable is not set");
            string translatorEndpoint = Environment.GetEnvironmentVariable("TRANSLATOR_ENDPOINT") 
                ?? "https://api.cognitive.microsofttranslator.com/";
            string translatorRegion = Environment.GetEnvironmentVariable("TRANSLATOR_REGION") 
                ?? throw new ArgumentException("TRANSLATOR_REGION environment variable is not set");
            
            _translationService = new TranslationService(
                translatorApiKey,
                translatorEndpoint,
                translatorRegion
            );
            _isListening = false;
        }

        public async Task<string> ConvertSpeechToTextAsync()
        {
            using var recognizer = new SpeechRecognizer(_speechConfig);

            Console.WriteLine("Speak into your microphone.");
            var result = await recognizer.RecognizeOnceAsync();

            if (result.Reason == ResultReason.RecognizedSpeech)
            {
                return result.Text;
            }

            throw new Exception("Speech could not be recognized.");
        }

        public async Task<string> ConvertSpeechToTextAsync(string audioFilePath)
        {
            using var audioConfig = AudioConfig.FromWavFileInput(audioFilePath);
            using var recognizer = new SpeechRecognizer(_speechConfig, audioConfig);

            Console.WriteLine("Processing audio file...");
            var result = await recognizer.RecognizeOnceAsync();

            if (result.Reason == ResultReason.RecognizedSpeech)
            {
                return result.Text;
            }

            throw new Exception("Speech could not be recognized from the audio file.");
        }

        public async Task<string> ConvertSpeechToTextFromVideoAsync(string videoFilePath)
        {
            try 
            {
                Console.WriteLine($"Extracting audio from video: {videoFilePath}");
                // Extract audio from video file
                string extractedAudioPath = ExtractAudioFromVideo(videoFilePath);
                Console.WriteLine($"Audio extracted to: {extractedAudioPath}");

                // Try multiple recognition attempts with different configurations
                // First attempt - standard recognition
                try
                {
                    string result = await ConvertSpeechToTextAsync(extractedAudioPath);
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        Console.WriteLine($"Successfully recognized text from video audio: {result.Substring(0, Math.Min(50, result.Length))}...");
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"First recognition attempt failed: {ex.Message}. Trying alternate method...");
                }

                // Second attempt - with enhanced configuration
                try
                {
                    // Create a more specialized configuration for potential noisy audio
                    using var audioConfig = AudioConfig.FromWavFileInput(extractedAudioPath);
                    // Create a specialized speech config with noise tolerance settings
                    var specializedConfig = SpeechConfig.FromEndpoint(new Uri(_speechConfig.EndpointId), _speechConfig.SubscriptionKey);
                    specializedConfig.SetProperty("SpeechServiceResponse_Detailed", "true");
                    specializedConfig.EnableAudioLogging();
                    
                    using var recognizer = new SpeechRecognizer(specializedConfig, audioConfig);
                    
                    Console.WriteLine("Processing audio with specialized configuration...");
                    var result = await recognizer.RecognizeOnceAsync();
                    
                    if (result.Reason == ResultReason.RecognizedSpeech)
                    {
                        Console.WriteLine($"Second attempt successful: {result.Text}");
                        return result.Text;
                    }
                    else
                    {
                        Console.WriteLine($"Second attempt failed with reason: {result.Reason}");
                        // If there's any partial recognition, return that
                        return result.Text ?? "Limited or no speech detected in video.";
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in second recognition attempt: {ex.Message}");
                    throw new Exception("Multiple attempts to recognize speech from the video failed.", ex);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting text from video: {ex.Message}");
                return "Error processing video audio. Try with a video that has clearer speech.";
            }
        }

        private string ExtractAudioFromVideo(string videoFilePath)
        {
            string audioFilePath = Path.ChangeExtension(videoFilePath, ".wav");

            try
            {
                // Use more optimized FFmpeg settings for speech recognition
                // -ac 1: Convert to mono (single audio channel)
                // -ar 16000: Sample rate of 16kHz (good for speech)
                // -vn: Disable video
                // -q:a 0: Highest audio quality
                // -af "loudnorm=I=-16:TP=-1.5:LRA=11": Normalize audio levels for better speech recognition
                string ffmpegCommand = 
                    $"ffmpeg -i \"{videoFilePath}\" -ac 1 -ar 16000 -vn -q:a 0 " +
                    $"-af \"loudnorm=I=-16:TP=-1.5:LRA=11\" \"{audioFilePath}\" -y";

                // Execute the command
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/C {ffmpegCommand}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                
                // Read the error output for debugging
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"FFmpeg error output: {stderr}");
                    throw new Exception($"Failed to extract audio from video. FFmpeg error code: {process.ExitCode}");
                }

                Console.WriteLine($"Successfully extracted audio to {audioFilePath}");
                return audioFilePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting audio: {ex.Message}");
                throw;
            }
        }

        public async IAsyncEnumerable<(string Original, string Translated, bool IsInterim)> GetSpeechStreamAsync(string sourceLanguage, string targetLanguage)
        {
            _speechRecognizer = new SpeechRecognizer(_speechConfig);
            _speechSynthesizer = new SpeechSynthesizer(_speechConfig);

            // Clear previous accumulated text when starting a new session
            _accumulatedOriginalText.Clear();
            _accumulatedTranslatedText.Clear();
            _lastInterimText = string.Empty;

            var translationPairs = new Queue<(string Original, string Translated, bool IsInterim)>();
            _isListening = true;

            // Handle interim results (while speaking)
            _speechRecognizer.Recognizing += async (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Result.Text) && e.Result.Text != _lastInterimText)
                {
                    _lastInterimText = e.Result.Text;
                    Console.WriteLine($"Interim Recognized: {e.Result.Text}");
                    
                    // Translate the interim result
                    try
                    {
                        string interimText = e.Result.Text;
                        string translatedText = await _translationService.TranslateTextAsync(sourceLanguage, targetLanguage, interimText);
                        
                        // Queue the interim result with the IsInterim flag set to true
                        translationPairs.Enqueue((interimText, translatedText, true));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error translating interim text: {ex.Message}");
                    }
                }
            };

            // Handle final results (after pauses)
            _speechRecognizer.Recognized += async (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Result.Text))
                {
                    string originalText = e.Result.Text;
                    _lastInterimText = string.Empty; // Reset interim tracking
                    
                    try
                    {
                        string translatedText = await _translationService.TranslateTextAsync(sourceLanguage, targetLanguage, originalText);
                        
                        Console.WriteLine($"Original: {originalText}");
                        Console.WriteLine($"Translated: {translatedText}");
                        
                        // Accumulate the final text
                        if (_accumulatedOriginalText.Length > 0)
                        {
                            _accumulatedOriginalText.Append(" ");
                            _accumulatedTranslatedText.Append(" ");
                        }
                        _accumulatedOriginalText.Append(originalText);
                        _accumulatedTranslatedText.Append(translatedText);
                        
                        // Store both original and translated text with IsInterim flag set to false
                        translationPairs.Enqueue((originalText, translatedText, false));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error translating final text: {ex.Message}");
                    }
                }
            };

            await _speechRecognizer.StartContinuousRecognitionAsync();

            while (_isListening)
            {
                while (translationPairs.Count > 0)
                {
                    yield return translationPairs.Dequeue();
                }

                await Task.Delay(10); // Reduced delay for more responsive processing
            }

            await _speechRecognizer.StopContinuousRecognitionAsync();
            yield break;
        }

        public async Task StopListeningAsync()
        {
            _isListening = false;

            if (_speechRecognizer != null)
            {
                await _speechRecognizer.StopContinuousRecognitionAsync();
                _speechRecognizer.Dispose();
            }

            if (_speechSynthesizer != null)
            {
                _speechSynthesizer.Dispose();
            }
        }

        // Add method to get the full accumulated text
        public (string Original, string Translated) GetFullText()
        {
            return AccumulatedTexts;
        }
    }
}