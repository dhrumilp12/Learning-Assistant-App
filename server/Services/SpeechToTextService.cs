using Microsoft.AspNetCore.SignalR;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using SpeechTranslator.Hubs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SpeechTranslator.Services
{
    public class SpeechToTextService
    {
        private readonly SpeechConfig _speechConfig;
        private readonly TranslationService _translationService;
    private readonly LatencyTracker _latencyTracker;
    private readonly ILogger<SpeechToTextService> _logger;
    private enum RecognitionStage : byte { InterimPending = 0, FinalPending = 1 }
    private readonly ConcurrentDictionary<string, RecognitionStage> _activeRecognitions = new();
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

        public SpeechToTextService(
            string speechEndpoint,
            string speechKey,
            TranslationService translationService,
            LatencyTracker latencyTracker,
            ILogger<SpeechToTextService> logger)
        {
            if (string.IsNullOrEmpty(speechEndpoint))
                throw new ArgumentNullException(nameof(speechEndpoint));
            if (string.IsNullOrEmpty(speechKey))
                throw new ArgumentNullException(nameof(speechKey));
                
            _speechConfig = SpeechConfig.FromEndpoint(new Uri(speechEndpoint), speechKey);
            _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
            _latencyTracker = latencyTracker ?? throw new ArgumentNullException(nameof(latencyTracker));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _isListening = false;
        }

        public async Task<string> ConvertSpeechToTextAsync()
        {
            using var recognizer = new SpeechRecognizer(_speechConfig);
            var requestId = $"mic-{Guid.NewGuid()}";
            _latencyTracker.StartTracking(requestId);

            SpeechRecognitionResult? result = null;

            try
            {
                Console.WriteLine("Speak into your microphone.");
                result = await recognizer.RecognizeOnceAsync();

                if (result.Reason == ResultReason.RecognizedSpeech)
                {
                    return result.Text;
                }

                throw new Exception("Speech could not be recognized.");
            }
            finally
            {
                var latency = _latencyTracker.EndTracking(requestId);
                if (latency.HasValue)
                {
                    _logger.LogInformation("One-shot speech recognition latency: {Latency}ms (reason: {Reason})", latency.Value, result?.Reason);
                }
            }
        }

        public async Task<string> ConvertSpeechToTextAsync(string audioFilePath)
        {
            using var audioConfig = AudioConfig.FromWavFileInput(audioFilePath);
            using var recognizer = new SpeechRecognizer(_speechConfig, audioConfig);
            var requestId = $"audio-{Guid.NewGuid()}";
            _latencyTracker.StartTracking(requestId);
            SpeechRecognitionResult? result = null;

            try
            {
                Console.WriteLine("Processing audio file...");
                result = await recognizer.RecognizeOnceAsync();

                if (result.Reason == ResultReason.RecognizedSpeech)
                {
                    return result.Text;
                }

                throw new Exception("Speech could not be recognized from the audio file.");
            }
            finally
            {
                var latency = _latencyTracker.EndTracking(requestId);
                if (latency.HasValue)
                {
                    _logger.LogInformation("Audio file speech recognition latency: {Latency}ms (reason: {Reason})", latency.Value, result?.Reason);
                }
            }
        }

        public async Task<string> ConvertSpeechToTextFromVideoAsync(string videoFilePath)
        {
            var requestId = $"video-{Guid.NewGuid()}";
            _latencyTracker.StartTracking(requestId);
            string response = "Error processing video audio. Try with a video that has clearer speech.";
            bool success = false;

            try 
            {
                Console.WriteLine($"Extracting audio from video: {videoFilePath}");
                string extractedAudioPath = ExtractAudioFromVideo(videoFilePath);
                Console.WriteLine($"Audio extracted to: {extractedAudioPath}");

                try
                {
                    string firstAttempt = await ConvertSpeechToTextAsync(extractedAudioPath);
                    if (!string.IsNullOrWhiteSpace(firstAttempt))
                    {
                        response = firstAttempt;
                        success = true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"First recognition attempt failed: {ex.Message}. Trying alternate method...");
                }

                if (!success)
                {
                    try
                    {
                        using var audioConfig = AudioConfig.FromWavFileInput(extractedAudioPath);
                        var specializedConfig = SpeechConfig.FromEndpoint(new Uri(_speechConfig.EndpointId), _speechConfig.SubscriptionKey);
                        specializedConfig.SetProperty("SpeechServiceResponse_Detailed", "true");
                        specializedConfig.EnableAudioLogging();
                        
                        using var recognizer = new SpeechRecognizer(specializedConfig, audioConfig);
                        
                        Console.WriteLine("Processing audio with specialized configuration...");
                        var result = await recognizer.RecognizeOnceAsync();
                        
                        if (result.Reason == ResultReason.RecognizedSpeech)
                        {
                            Console.WriteLine($"Second attempt successful: {result.Text}");
                            response = result.Text;
                            success = true;
                        }
                        else
                        {
                            Console.WriteLine($"Second attempt failed with reason: {result.Reason}");
                            response = result.Text ?? "Limited or no speech detected in video.";
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in second recognition attempt: {ex.Message}");
                        throw new Exception("Multiple attempts to recognize speech from the video failed.", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting text from video: {ex.Message}");
                response = "Error processing video audio. Try with a video that has clearer speech.";
            }
            finally
            {
                var latency = _latencyTracker.EndTracking(requestId);
                if (latency.HasValue)
                {
                    _logger.LogInformation("Video speech recognition latency: {Latency}ms (success: {Success})", latency.Value, success);
                }
            }

            return response;
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
                var resultId = e.Result.ResultId;
                if (!string.IsNullOrWhiteSpace(resultId) && _activeRecognitions.TryAdd(resultId, RecognitionStage.InterimPending))
                {
                    _latencyTracker.StartTracking(resultId);
                    _logger.LogDebug("Started latency tracking for interim result {ResultId}", resultId);
                }

                if (!string.IsNullOrWhiteSpace(e.Result.Text) && e.Result.Text != _lastInterimText)
                {
                    _lastInterimText = e.Result.Text;
                    Console.WriteLine($"Interim Recognized: {e.Result.Text}");
                    
                    // Translate the interim result
                    try
                    {
                        string interimText = e.Result.Text;
                        var translationTimer = Stopwatch.StartNew();
                        string translatedText = await _translationService.TranslateTextAsync(sourceLanguage, targetLanguage, interimText);
                        translationTimer.Stop();
                        _latencyTracker.RecordTranslationLatency(translationTimer.Elapsed.TotalMilliseconds);
                        
                        // Queue the interim result with the IsInterim flag set to true
                        translationPairs.Enqueue((interimText, translatedText, true));

                        if (!string.IsNullOrWhiteSpace(resultId) &&
                            _activeRecognitions.TryGetValue(resultId, out var stage) &&
                            stage == RecognitionStage.InterimPending)
                        {
                            var latency = _latencyTracker.EndTracking(resultId);
                            if (latency.HasValue)
                            {
                                _logger.LogInformation("Interim speech recognition latency: {Latency}ms for result {ResultId}", latency.Value, resultId);
                            }

                            _latencyTracker.StartTracking(resultId);
                            _activeRecognitions[resultId] = RecognitionStage.FinalPending;
                        }
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
                        var translationTimer = Stopwatch.StartNew();
                        string translatedText = await _translationService.TranslateTextAsync(sourceLanguage, targetLanguage, originalText);
                        translationTimer.Stop();
                        _latencyTracker.RecordTranslationLatency(translationTimer.Elapsed.TotalMilliseconds);
                        
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

                var resultId = e.Result.ResultId;
                if (!string.IsNullOrWhiteSpace(resultId))
                {
                    if (_activeRecognitions.TryRemove(resultId, out var stage))
                    {
                        var latency = _latencyTracker.EndTracking(resultId);
                        if (latency.HasValue)
                        {
                            if (stage == RecognitionStage.FinalPending)
                            {
                                _logger.LogInformation("Final speech recognition latency: {Latency}ms for result {ResultId}", latency.Value, resultId);
                            }
                            else
                            {
                                _logger.LogInformation("Speech recognition latency without interim translation: {Latency}ms for result {ResultId}", latency.Value, resultId);
                            }
                        }
                    }
                    else
                    {
                        _latencyTracker.CancelTracking(resultId);
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

            foreach (var pendingId in _activeRecognitions.Keys)
            {
                if (_activeRecognitions.TryRemove(pendingId, out _))
                {
                    if (_latencyTracker.CancelTracking(pendingId))
                    {
                        _logger.LogDebug("Cancelled latency tracking for incomplete recognition {ResultId}", pendingId);
                    }
                }
            }

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