using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace SpeechTranslator.Services
{
    /// <summary>
    /// Provides thread-safe tracking and reporting of speech-to-text latency metrics.
    /// </summary>
    public class LatencyTracker 
    {
        /// <summary>
        /// Target latency threshold in milliseconds; entries beyond this are logged as warnings.
        /// </summary>
        private const double SuccessThresholdMs = 3000;

        /// <summary>
        /// Keeps track of when each request started so we can compute elapsed time on completion.
        /// </summary>
        private readonly Dictionary<string, DateTime> _requestStartTimes = new();

        /// <summary>
        /// Historical speech-recognition latency measurements used to compute aggregate statistics.
        /// </summary>
        private readonly List<double> _latencyMeasurements = new();

        /// <summary>
        /// Historical translation latency measurements for end-to-end monitoring.
        /// </summary>
        private readonly List<double> _translationLatencyMeasurements = new();

        /// <summary>
        /// Historical video overlay latency measurements for frame refresh monitoring.
        /// </summary>
        private readonly List<double> _videoOverlayLatencyMeasurements = new();

        /// <summary>
        /// Synchronizes access to shared state across multiple threads.
        /// </summary>
        private readonly object _lock = new();

        /// <summary>
        /// Application logger for surfacing latency information.
        /// </summary>
        private readonly ILogger<LatencyTracker> _logger;
        
        public LatencyTracker(ILogger<LatencyTracker> logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Starts the latency timer for a specific request.
        /// </summary>
        public void StartTracking(string requestId)
        {
            lock (_lock)
            {
                _requestStartTimes[requestId] = DateTime.UtcNow;
            }
        }
        
        /// <summary>
        /// Stops the timer for the request and records the measured latency.
        /// Returns null when the request is unknown (for example, already cancelled).
        /// </summary>
        public double? EndTracking(string requestId)
        {
            DateTime startTime;
            lock (_lock)
            {
                if (!_requestStartTimes.TryGetValue(requestId, out startTime))
                {
                    return null;
                }

                _requestStartTimes.Remove(requestId);
            }

            var endToEndLatency = (DateTime.UtcNow - startTime).TotalMilliseconds;

            RecordLatencyInternal(_latencyMeasurements, endToEndLatency);

            // Emit telemetry so we can diagnose slow recognitions.
            if (endToEndLatency > SuccessThresholdMs)
            {
                _logger.LogWarning($"⚠️ Latency exceeded target: {endToEndLatency:F0}ms");
            }
            else
            {
                _logger.LogDebug($"✅ Latency within target: {endToEndLatency:F0}ms");
            }

            return endToEndLatency;
        }

        /// <summary>
        /// Removes a pending request without recording a latency measurement.
        /// </summary>
        public bool CancelTracking(string requestId)
        {
            lock (_lock)
            {
                return _requestStartTimes.Remove(requestId);
            }
        }
        
        /// <summary>
        /// Returns aggregate latency metrics (average, p95, p99) alongside the sample count.
        /// </summary>
        public (double Average, double P95, double P99, int TotalMeasurements) GetLatencyStats()
        {
            List<double> snapshot;
            lock (_lock)
            {
                if (!_latencyMeasurements.Any()) return (0, 0, 0, 0);
                snapshot = new List<double>(_latencyMeasurements);
            }

            var sorted = snapshot.OrderBy(x => x).ToList();
            var average = sorted.Average();
            var p95 = sorted.ElementAtOrDefault((int)(sorted.Count * 0.95));
            var p99 = sorted.ElementAtOrDefault((int)(sorted.Count * 0.99));
            
            return (average, p95, p99, sorted.Count);
        }
        
        /// <summary>
        /// Calculates the percentage of requests that completed within the target threshold.
        /// </summary>
        public double GetSuccessRate()
        {
            lock (_lock)
            {
                if (!_latencyMeasurements.Any()) return 0;
                var successfulRequests = _latencyMeasurements.Count(x => x <= SuccessThresholdMs);
                return (double)successfulRequests / _latencyMeasurements.Count * 100;
            }
        }

        /// <summary>
        /// Records the time taken to translate recognized text.
        /// </summary>
        public void RecordTranslationLatency(double latencyMs)
        {
            RecordLatencyInternal(_translationLatencyMeasurements, latencyMs);

            if (latencyMs > SuccessThresholdMs)
            {
                _logger.LogWarning($"⚠️ Translation latency exceeded target: {latencyMs:F0}ms");
            }
            else
            {
                _logger.LogDebug($"✅ Translation latency within target: {latencyMs:F0}ms");
            }
        }

        /// <summary>
        /// Returns aggregate translation latency metrics.
        /// </summary>
        public (double Average, double P95, double P99, int TotalMeasurements) GetTranslationLatencyStats()
        {
            List<double> snapshot;
            lock (_lock)
            {
                if (!_translationLatencyMeasurements.Any()) return (0, 0, 0, 0);
                snapshot = new List<double>(_translationLatencyMeasurements);
            }

            var sorted = snapshot.OrderBy(x => x).ToList();
            var average = sorted.Average();
            var p95 = sorted.ElementAtOrDefault((int)(sorted.Count * 0.95));
            var p99 = sorted.ElementAtOrDefault((int)(sorted.Count * 0.99));

            return (average, p95, p99, sorted.Count);
        }

        /// <summary>
        /// Calculates the percentage of translations that completed within the target threshold.
        /// </summary>
        public double GetTranslationSuccessRate()
        {
            lock (_lock)
            {
                if (!_translationLatencyMeasurements.Any()) return 0;
                var successfulRequests = _translationLatencyMeasurements.Count(x => x <= SuccessThresholdMs);
                return (double)successfulRequests / _translationLatencyMeasurements.Count * 100;
            }
        }

        /// <summary>
        /// Records the time taken to render and broadcast a video frame with overlays.
        /// </summary>
        public void RecordVideoOverlayLatency(double latencyMs)
        {
            RecordLatencyInternal(_videoOverlayLatencyMeasurements, latencyMs);

            if (latencyMs > SuccessThresholdMs)
            {
                _logger.LogWarning($"⚠️ Video overlay latency exceeded target: {latencyMs:F0}ms");
            }
            else
            {
                _logger.LogDebug($"✅ Video overlay latency within target: {latencyMs:F0}ms");
            }
        }

        /// <summary>
        /// Returns aggregate video overlay latency metrics.
        /// </summary>
        public (double Average, double P95, double P99, int TotalMeasurements) GetVideoOverlayLatencyStats()
        {
            List<double> snapshot;
            lock (_lock)
            {
                if (!_videoOverlayLatencyMeasurements.Any()) return (0, 0, 0, 0);
                snapshot = new List<double>(_videoOverlayLatencyMeasurements);
            }

            var sorted = snapshot.OrderBy(x => x).ToList();
            var average = sorted.Average();
            var p95 = sorted.ElementAtOrDefault((int)(sorted.Count * 0.95));
            var p99 = sorted.ElementAtOrDefault((int)(sorted.Count * 0.99));

            return (average, p95, p99, sorted.Count);
        }

        /// <summary>
        /// Calculates the percentage of video overlays that completed within the target threshold.
        /// </summary>
        public double GetVideoOverlaySuccessRate()
        {
            lock (_lock)
            {
                if (!_videoOverlayLatencyMeasurements.Any()) return 0;
                var successfulRequests = _videoOverlayLatencyMeasurements.Count(x => x <= SuccessThresholdMs);
                return (double)successfulRequests / _videoOverlayLatencyMeasurements.Count * 100;
            }
        }

        private void RecordLatencyInternal(List<double> bucket, double latencyMs)
        {
            lock (_lock)
            {
                bucket.Add(latencyMs);
            }
        }
    }
}