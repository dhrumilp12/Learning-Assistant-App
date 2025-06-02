import React, { useState, useEffect, useRef, useCallback, memo } from 'react';
import { 
  Box, Typography, Paper, Button, 
  Alert, AlertTitle, LinearProgress, Card, CardContent,
  Grid, Divider, Chip, ToggleButtonGroup, ToggleButton, Stack
} from '@mui/material';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import StopIcon from '@mui/icons-material/Stop';
import VideocamIcon from '@mui/icons-material/Videocam';
import { useHubConnection } from '../contexts/HubConnectionContext';
import { uploadVideoForTranslation, stopVideoTranslation } from '../services/api';
import LanguageSelector from './shared/LanguageSelector';

interface VideoMetadata {
  fps: number;
  totalFrames: number;
  width: number;
  height: number;
}

interface DetectedText {
  id: number;
  originalText: string;
  translatedText: string;
  boundingBox: {
    x: number;
    y: number;
    width: number;
    height: number;
  };
}

interface AudioTranscription {
  id: number;
  originalText: string;
  translatedText: string;
  timestamp: Date;
  isFullText?: boolean;
}

// Create memoized components for better performance
const DetectedTextItem = memo(({ text }: { text: DetectedText }) => (
  <Box sx={{ mb: 2, pb: 2, borderBottom: '1px solid #eee' }}>
    <Typography variant="body2" sx={{ mb: 0.5, fontWeight: 500 }}>
      Original:
    </Typography>
    <Typography variant="body2" sx={{ mb: 1 }}>
      {text.originalText}
    </Typography>
    <Typography variant="body2" sx={{ mb: 0.5, fontWeight: 500 }}>
      Translated:
    </Typography>
    <Typography variant="body2" sx={{ color: 'primary.main' }}>
      {text.translatedText}
    </Typography>
  </Box>
));

const AudioTranscriptionItem = memo(({ transcription }: { transcription: AudioTranscription }) => (
  <Box 
    sx={{ 
      mb: 2, 
      pb: 2, 
      borderBottom: '1px solid #eee',
      backgroundColor: transcription.isFullText ? 'rgba(0,0,0,0.03)' : 'transparent'
    }}
  >
    <Typography variant="body2" sx={{ mb: 1, fontWeight: 500 }}>
      Original:
      {transcription.isFullText && (
        <Typography component="span" variant="caption" sx={{ ml: 1, color: 'text.secondary' }}>
          (complete transcript)
        </Typography>
      )}
    </Typography>
    <Typography variant="body2" sx={{ mb: 2 }}>
      {transcription.originalText}
    </Typography>
    <Typography variant="body2" sx={{ mb: 1, fontWeight: 500 }}>
      Translated:
    </Typography>
    <Typography variant="body2" sx={{ color: 'primary.main', mb: 1 }}>
      {transcription.translatedText}
    </Typography>
    <Typography variant="caption" color="text.secondary">
      {transcription.timestamp.toLocaleTimeString()}
    </Typography>
  </Box>
));

const VideoTranslator: React.FC = () => {
  const { connection, connectionState } = useHubConnection();
  const [sourceLanguage, setSourceLanguage] = useState<string>('en');
  const [targetLanguage, setTargetLanguage] = useState<string>('es');
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [isProcessing, setIsProcessing] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState<number>(0);
  const [currentFrame, setCurrentFrame] = useState<string | null>(null);
  const [videoMetadata, setVideoMetadata] = useState<VideoMetadata | null>(null);
  const [detectedTexts, setDetectedTexts] = useState<DetectedText[]>([]);
  const [frameNumber, setFrameNumber] = useState<number>(0);
  const [totalFrames, setTotalFrames] = useState<number>(0);
  const [audioTranscriptions, setAudioTranscriptions] = useState<AudioTranscription[]>([]);
  const [processingStatus, setProcessingStatus] = useState<string>('');
  const [showAudioPanel, setShowAudioPanel] = useState<boolean>(true);
  const [showTextPanel, setShowTextPanel] = useState<boolean>(true);
  const [connectionLost, setConnectionLost] = useState<boolean>(false);
  
  // New states for real-time camera mode
  const [mode, setMode] = useState<'upload' | 'camera'>('upload');
  const [cameraStream, setCameraStream] = useState<MediaStream | null>(null);
  const [isLiveTranslating, setIsLiveTranslating] = useState<boolean>(false);
  const [cameraError, setCameraError] = useState<string | null>(null);
  const [frameRate, setFrameRate] = useState<number>(5); // Frames per second to process
  const [processingFrame, setProcessingFrame] = useState<boolean>(false);

  // Add new state for audio recording
  const [audioRecorder, setAudioRecorder] = useState<MediaRecorder | null>(null);
  const [isRecordingAudio, setIsRecordingAudio] = useState<boolean>(false);
  const [audioChunks, setAudioChunks] = useState<Blob[]>([]);
  const audioIntervalRef = useRef<number | null>(null);

  const canvasRef = useRef<HTMLCanvasElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const captureCanvasRef = useRef<HTMLCanvasElement>(null);
  const audioScrollRef = useRef<HTMLDivElement>(null);
  const textScrollRef = useRef<HTMLDivElement>(null);
  const frameIntervalRef = useRef<number | null>(null);

  // Fix the issue where stopCameraStream is called before it's defined
  // Define the function early in the component
  const stopCameraStream = () => {
    // Stop frame capture interval
    if (frameIntervalRef.current) {
      window.clearInterval(frameIntervalRef.current);
      frameIntervalRef.current = null;
    }
    
    // Stop audio recording if active
    if (audioRecorder && isRecordingAudio) {
      audioRecorder.stop();
      setIsRecordingAudio(false);
      
      if (audioIntervalRef.current) {
        window.clearInterval(audioIntervalRef.current);
        audioIntervalRef.current = null;
      }
    }
    
    // Stop all camera stream tracks
    if (cameraStream) {
      cameraStream.getTracks().forEach(track => track.stop());
      setCameraStream(null);
    }
  };

  // Define stopAudioRecording function
  const stopAudioRecording = () => {
    if (audioRecorder && isRecordingAudio) {
      audioRecorder.stop();
      setIsRecordingAudio(false);
      console.log('Stopped audio recording');
    }
    
    if (audioIntervalRef.current) {
      window.clearInterval(audioIntervalRef.current);
      audioIntervalRef.current = null;
    }
  };

  useEffect(() => {
    if (!connection) return;

    // Set up SignalR event handlers
    connection.on('VideoMetadata', (metadata: VideoMetadata) => {
      setVideoMetadata(metadata);
      setTotalFrames(metadata.totalFrames);
    });

    connection.on('ReceiveVideoFrame', (frameBase64: string, frameNum: number, totalFrames: number) => {
      setCurrentFrame(frameBase64);
      setFrameNumber(frameNum);
      setTotalFrames(totalFrames);
      
      // Calculate progress percentage
      const progressPercent = (frameNum / totalFrames) * 100;
      setProgress(progressPercent);
    });

    connection.on('TextDetectedInVideo', (originalText: string, translatedText: string, boundingBox: any) => {
      const newText: DetectedText = {
        id: Date.now(),
        originalText,
        translatedText,
        boundingBox
      };
      
      setDetectedTexts(prev => [...prev, newText]);
    });

    // Add handler for real-time frame processing response
    connection.on('ReceiveProcessedFrame', (frameBase64: string, detectedTexts: DetectedText[]) => {
      setCurrentFrame(frameBase64);
      
      // Update detected texts
      if (detectedTexts && detectedTexts.length > 0) {
        setDetectedTexts(prev => {
          // Filter out duplicates and keep only recent texts (last 30)
          const combinedTexts = [...prev, ...detectedTexts];
          // Remove duplicates by comparing original text
          const uniqueTexts = combinedTexts.filter((text, index, self) => 
            index === self.findIndex(t => t.originalText === text.originalText)
          );
          return uniqueTexts.slice(-30);
        });
      }
      
      // Signal that we can process the next frame
      setProcessingFrame(false);
    });

    connection.on('VideoProcessingComplete', () => {
      setIsProcessing(false);
      console.log('Video processing completed');
    });

    connection.on('VideoProcessingError', (errorMessage: string) => {
      setError(errorMessage);
      setIsProcessing(false);
    });

    connection.on('ReceiveVideoSpeechTranslation', 
      (original: string, translated: string, sourceLang: string, targetLang: string) => {
        const newTranscription: AudioTranscription = {
          id: Date.now(),
          originalText: original,
          translatedText: translated,
          timestamp: new Date()
        };
        
        // Check for duplicates before adding
        setAudioTranscriptions(prev => {
          // Check if this exact text was just added in the last second (to avoid duplicates)
          const isDuplicate = prev.some(item => 
            item.originalText === original && 
            item.translatedText === translated &&
            Date.now() - item.timestamp.getTime() < 1000
          );
          
          if (isDuplicate) return prev;
          return [...prev, newTranscription];
        });
      }
    );

    connection.on('ReceiveFullVideoSpeechTranslation', 
      (original: string, translated: string, sourceLang: string, targetLang: string) => {
        const fullTranscription: AudioTranscription = {
          id: Date.now(),
          originalText: original,
          translatedText: translated,
          timestamp: new Date(),
          isFullText: true
        };
        
        // Check for duplicates before adding
        setAudioTranscriptions(prev => {
          // Avoid adding if we already have 5+ translations or if this is a duplicate
          if (prev.length > 5) return prev;
          
          const isDuplicate = prev.some(item => 
            item.originalText === original &&
            item.translatedText === translated &&
            item.isFullText === true
          );
          
          if (isDuplicate) return prev;
          return [...prev, fullTranscription];
        });
      }
    );

    connection.on('VideoProcessingStatus', (status: string) => {
      setProcessingStatus(status);
    });

    connection.on('VideoProcessingProgress', (percent: number, currentFrame: number, totalFrames: number) => {
      setProgress(percent);
      setFrameNumber(currentFrame);
      setTotalFrames(totalFrames);
    });

    return () => {
      connection.off('VideoMetadata');
      connection.off('ReceiveVideoFrame');
      connection.off('TextDetectedInVideo');
      connection.off('VideoProcessingComplete');
      connection.off('VideoProcessingError');
      connection.off('ReceiveVideoSpeechTranslation');
      connection.off('ReceiveFullVideoSpeechTranslation');
      connection.off('VideoProcessingStatus');
      connection.off('VideoProcessingProgress');
      connection.off('ReceiveProcessedFrame');
      
      // Clean up camera stream when component unmounts
      stopCameraStream();
    };
  }, [connection]);

  useEffect(() => {
    if (audioScrollRef.current && audioTranscriptions.length > 0) {
      audioScrollRef.current.scrollTop = audioScrollRef.current.scrollHeight;
    }
  }, [audioTranscriptions]);

  const updateCanvas = useCallback((frameData: string) => {
    if (!canvasRef.current) return;
    
    const canvas = canvasRef.current;
    const ctx = canvas.getContext('2d');
    
    if (!ctx) return;
    
    const img = new Image();
    img.onload = () => {
      canvas.width = img.width;
      canvas.height = img.height;
      ctx.drawImage(img, 0, 0);
    };
    img.src = `data:image/jpeg;base64,${frameData}`;
  }, []);

  useEffect(() => {
    if (currentFrame) updateCanvas(currentFrame);
  }, [currentFrame, updateCanvas]);

  useEffect(() => {
    if (connectionState !== 'Connected' && isProcessing) {
      setConnectionLost(true);
    } else if (connectionState === 'Connected' && connectionLost) {
      setConnectionLost(false);
      // Optional: Reload the page to get a clean state after reconnection
      window.location.reload();
    }
  }, [connectionState, isProcessing, connectionLost]);

  const handleFileChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    if (event.target.files && event.target.files.length > 0) {
      setSelectedFile(event.target.files[0]);
    }
  };

  const handleUpload = async () => {
    if (!selectedFile) {
      setError('Please select a video file');
      return;
    }

    try {
      setError(null);
      setIsProcessing(true);
      setProgress(0);
      setDetectedTexts([]);
      setAudioTranscriptions([]);
      setCurrentFrame(null);

      await uploadVideoForTranslation(selectedFile, sourceLanguage, targetLanguage);
    } catch (err: any) {
      setError(err.message || 'Failed to upload video');
      setIsProcessing(false);
    }
  };

  const handleStop = async () => {
    try {
      await stopVideoTranslation();
      setIsProcessing(false);
    } catch (err: any) {
      setError(err.message || 'Failed to stop video processing');
    }
  };

  const toggleAudioPanel = () => setShowAudioPanel(prev => !prev);
  const toggleTextPanel = () => setShowTextPanel(prev => !prev);

  // Camera mode functions
  const startCameraStream = async () => {
    try {
      setCameraError(null);
      
      console.log("Starting camera stream...");
      
      // Get user media with video and audio for transcription
      const stream = await navigator.mediaDevices.getUserMedia({ 
        video: true,
        audio: true  // Make sure audio is enabled
      });
      
      console.log("Camera access granted, stream obtained");
      
      setCameraStream(stream);
      
      // Use setTimeout to ensure the video element is in the DOM
      setTimeout(() => {
        if (videoRef.current) {
          console.log("Setting video source to stream");
          videoRef.current.srcObject = stream;
          videoRef.current.onloadedmetadata = () => {
            console.log("Video metadata loaded, playing video");
            videoRef.current?.play().catch(err => {
              console.error("Error playing video:", err);
              setCameraError(`Error starting camera feed: ${err.message}`);
            });
          };
        } else {
          console.error("Video reference is null");
          setCameraError("Could not initialize video element");
        }
      }, 100);
    } catch (err: any) {
      console.error("Error accessing camera:", err);
      setCameraError(err.message || "Couldn't access your camera");
      setCameraStream(null);
    }
  };
  
  // Add function to check supported MIME types
  const getSupportedMimeType = (): string | null => {
    const types = [
      'audio/webm',
      'audio/webm;codecs=opus',
      'audio/mp4',
      'audio/ogg',
      'audio/wav',
      'audio/aac'
    ];
    
    for (const type of types) {
      if (MediaRecorder.isTypeSupported(type)) {
        console.log(`Browser supports MIME type: ${type}`);
        return type;
      }
    }
    
    console.warn('None of the preferred MIME types are supported');
    return null; // Let the browser use its default
  };

  // Add function to start audio recording with more robust error handling
  const startAudioRecording = () => {
    if (!cameraStream) return;
    
    try {
      console.log("Starting audio recording...");
      
      // Check if MediaRecorder is supported
      if (!window.MediaRecorder) {
        throw new Error("MediaRecorder is not supported in this browser");
      }
      
      // Create a separate audio stream from the existing stream
      // This can help avoid conflicts with video processing
      const audioTracks = cameraStream.getAudioTracks();
      if (audioTracks.length === 0) {
        throw new Error("No audio tracks found in camera stream");
      }
      
      const audioStream = new MediaStream(audioTracks);
      console.log(`Got ${audioTracks.length} audio tracks for recording`);
      
      // Get supported MIME type or use browser default
      const mimeType = getSupportedMimeType();
      let recorderOptions = undefined;
      
      try {
        if (mimeType) {
          recorderOptions = { mimeType };
          console.log("Creating MediaRecorder with options:", recorderOptions);
        } else {
          console.log("Creating MediaRecorder with default options");
        }
        
        // Create audio recorder from stream
        const recorder = new MediaRecorder(audioStream, recorderOptions);
        setAudioRecorder(recorder);
        
        const chunks: Blob[] = [];
        recorder.ondataavailable = (e) => {
          if (e.data && e.data.size > 0) {
            chunks.push(e.data);
            setAudioChunks(prevChunks => [...prevChunks, e.data]);
            console.log(`Audio data chunk received: ${e.data.size} bytes`);
          }
        };
        
        recorder.onstop = async () => {
          console.log('Audio recorder stopped, processing chunks');
          if (chunks.length > 0) {
            await processAudioChunks(chunks);
            setAudioChunks([]);
          }
        };
        
        recorder.onerror = (event: Event) => {
          console.error("MediaRecorder error:", event);
          const errorEvent = event as any;
          setCameraError(`MediaRecorder error: ${errorEvent.name || 'Unknown error'}`);
        };
        
        // Try starting with a small timeslice to get frequent data events
        // 100ms is a good compromise between responsiveness and overhead
        try {
          recorder.start(100);
          console.log('Started audio recording with 100ms timeslice');
        } catch (startErr) {
          // If that fails, try starting without a timeslice parameter
          console.warn('Failed to start with timeslice, trying without:', startErr);
          recorder.start();
          console.log('Started audio recording without timeslice');
        }
        
        setIsRecordingAudio(true);
        
        // Set up interval to process audio in chunks every 3 seconds
        audioIntervalRef.current = window.setInterval(() => {
          if (recorder && recorder.state === 'recording') {
            // Request data (which will trigger the ondataavailable event)
            try {
              recorder.requestData();
            } catch (e) {
              console.warn('Error requesting data from recorder:', e);
              // Just ignore this error - some browsers don't support requestData
            }
            processLatestAudioChunk();
          }
        }, 3000);
        
      } catch (initErr) {
        console.error("Error initializing MediaRecorder:", initErr);
        
        // Try one more approach: create a new MediaRecorder with absolutely no options
        try {
          console.log("Trying fallback MediaRecorder with no options");
          const fallbackRecorder = new MediaRecorder(audioStream);
          setAudioRecorder(fallbackRecorder);
          
          const fallbackChunks: Blob[] = [];
          fallbackRecorder.ondataavailable = (e) => {
            if (e.data && e.data.size > 0) {
              fallbackChunks.push(e.data);
              setAudioChunks(prevChunks => [...prevChunks, e.data]);
            }
          };
          
          fallbackRecorder.onstop = async () => {
            if (fallbackChunks.length > 0) {
              await processAudioChunks(fallbackChunks);
              setAudioChunks([]);
            }
          };
          
          fallbackRecorder.start();
          setIsRecordingAudio(true);
          console.log('Started fallback audio recorder');
          
          // Use requestData less frequently for the fallback approach
          audioIntervalRef.current = window.setInterval(() => {
            try {
              fallbackRecorder.requestData();
              processLatestAudioChunk();
            } catch (e) {
              console.warn('Fallback recorder - error requesting data:', e);
            }
          }, 5000);
          
        } catch (fallbackErr) {
          console.error("Fallback MediaRecorder also failed:", fallbackErr);
          setCameraError(`Audio recording not supported in this browser. Video-only processing will continue.`);
        }
      }
    } catch (err: any) {
      console.error("Error starting audio recording:", err);
      setCameraError(`Error recording audio: ${err.message}. Live audio processing will be disabled.`);
      
      // Continue with video processing even if audio recording fails
      console.log("Continuing with video-only processing");
    }
  };
  
  // Process the latest audio chunk with better error handling
  const processLatestAudioChunk = async () => {
    if (audioChunks.length === 0) return;
    
    try {
      const latestChunk = audioChunks[audioChunks.length - 1];
      if (latestChunk.size < 100) {
        console.log("Audio chunk too small, skipping processing");
        return;
      }
      
      await processAudioChunks([latestChunk]);
    } catch (err) {
      console.error("Error processing audio chunk:", err);
    }
  };
  
  // Process audio chunks for speech recognition
  const processAudioChunks = async (chunks: Blob[]) => {
    if (!connection || chunks.length === 0) return;
    
    try {
      console.log(`Processing ${chunks.length} audio chunks`);
      
      // Create a blob from all chunks - get the MIME type from the first chunk
      const mimeType = chunks[0].type || 'audio/webm';
      const audioBlob = new Blob(chunks, { type: mimeType });
      console.log(`Created audio blob of type ${mimeType}, size: ${audioBlob.size} bytes`);
      
      // Skip small audio blobs that likely don't contain speech
      if (audioBlob.size < 1000) {
        console.log("Audio blob too small, skipping");
        return;
      }
      
      // Convert to base64
      const reader = new FileReader();
      reader.readAsDataURL(audioBlob);
      
      reader.onloadend = async () => {
        const base64data = reader.result as string;
        // Remove the data URL prefix (data:audio/webm;base64,)
        const base64Audio = base64data.split(',')[1];
        
        // Send to server for processing
        try {
          await connection.invoke('ProcessLiveAudio', base64Audio, sourceLanguage, targetLanguage);
          console.log('Sent audio chunk for processing');
        } catch (err) {
          console.error("Error sending audio for processing:", err);
        }
      };
    } catch (err) {
      console.error("Error processing audio chunks:", err);
    }
  };
  
  const startLiveTranslation = async () => {
    try {
      if (!cameraStream) {
        await startCameraStream();
      }
      
      setIsLiveTranslating(true);
      setDetectedTexts([]);
      setAudioTranscriptions([]);
      setError(null);
      
      // Start capturing frames at the specified frame rate
      frameIntervalRef.current = window.setInterval(() => {
        captureAndSendFrame();
      }, 1000 / frameRate);
      
      // Try to start audio recording but continue even if it fails
      try {
        // Delay audio recording start slightly to avoid conflicts with video processing
        setTimeout(() => {
          startAudioRecording();
        }, 500);
      } catch (err) {
        console.error("Audio recording failed but continuing with video:", err);
      }
      
    } catch (err: any) {
      setError(err.message || "Couldn't start live translation");
    }
  };
  
  const stopLiveTranslation = () => {
    setIsLiveTranslating(false);
    
    // Stop frame capture interval
    if (frameIntervalRef.current) {
      window.clearInterval(frameIntervalRef.current);
      frameIntervalRef.current = null;
    }
    
    // Stop audio recording
    stopAudioRecording();
  };
  
  const captureAndSendFrame = () => {
    // Skip if we're still processing the previous frame or connection isn't ready
    if (processingFrame || !connection || connectionState !== 'Connected') return;
    
    try {
      const video = videoRef.current;
      const canvas = captureCanvasRef.current;
      
      if (!video || !canvas || video.paused || video.ended) return;
      
      // Set canvas dimensions to match video
      canvas.width = video.videoWidth;
      canvas.height = video.videoHeight;
      
      // Draw the current video frame to the canvas
      const ctx = canvas.getContext('2d');
      if (!ctx) return;
      
      ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
      
      // Convert canvas to base64 image
      const frameBase64 = canvas.toDataURL('image/jpeg', 0.8).split(',')[1];
      
      // Send the frame to the server for processing
      setProcessingFrame(true);
      connection.invoke('ProcessLiveVideoFrame', frameBase64, sourceLanguage, targetLanguage)
        .catch(err => {
          console.error("Error sending frame for processing:", err);
          setProcessingFrame(false);
        });
    } catch (err) {
      console.error("Error capturing frame:", err);
      setProcessingFrame(false);
    }
  };
  
  const handleModeChange = (_event: React.MouseEvent<HTMLElement>, newMode: 'upload' | 'camera' | null) => {
    if (newMode !== null) {
      setMode(newMode);
      
      // Clean up resources when switching modes
      if (newMode === 'upload') {
        stopCameraStream();
        setIsLiveTranslating(false);
      } else {
        stopVideoTranslation();
        setIsProcessing(false);
        startCameraStream();
      }
      
      // Reset state
      setCurrentFrame(null);
      setDetectedTexts([]);
      setAudioTranscriptions([]);
      setError(null);
    }
  };

  return (
    <Box>
      <Paper elevation={3} sx={{ p: 3, mb: 4 }}>
        <Typography variant="h5" component="h1" gutterBottom>
          Video Translation
        </Typography>
        <Typography variant="body1" sx={{ mb: 3 }}>
          Translate text in videos. Choose between uploading a video or using your camera for real-time translation.
        </Typography>
        
        {/* Mode selection toggle */}
        <Box sx={{ mb: 3 }}>
          <ToggleButtonGroup
            value={mode}
            exclusive
            onChange={handleModeChange}
            aria-label="Video translation mode"
            fullWidth
            size="small"
          >
            <ToggleButton value="upload" disabled={isProcessing || isLiveTranslating}>
              Upload Video
            </ToggleButton>
            <ToggleButton value="camera" disabled={isProcessing || isLiveTranslating}>
              Live Camera
            </ToggleButton>
          </ToggleButtonGroup>
        </Box>

        <Box sx={{ display: 'flex', flexDirection: { xs: 'column', sm: 'row' }, gap: 2, mb: 3 }}>
          <LanguageSelector 
            label="Source Language"
            value={sourceLanguage}
            onChange={(e) => setSourceLanguage(e.target.value as string)}
            disabled={isProcessing || isLiveTranslating}
          />
          
          <LanguageSelector 
            label="Target Language"
            value={targetLanguage}
            onChange={(e) => setTargetLanguage(e.target.value as string)}
            disabled={isProcessing || isLiveTranslating}
          />
        </Box>

        {/* Upload mode UI */}
        {mode === 'upload' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, mb: 3 }}>
            <input
              accept="video/*"
              style={{ display: 'none' }}
              id="video-upload"
              type="file"
              onChange={handleFileChange}
              disabled={isProcessing}
            />
            <label htmlFor="video-upload">
              <Button
                variant="contained"
                component="span"
                startIcon={<UploadFileIcon />}
                disabled={isProcessing}
              >
                Select Video
              </Button>
            </label>
            {selectedFile && (
              <Typography variant="body2">
                Selected: {selectedFile.name} ({(selectedFile.size / (1024 * 1024)).toFixed(2)} MB)
              </Typography>
            )}
            
            <Box sx={{ display: 'flex', gap: 2 }}>
              {!isProcessing ? (
                <Button 
                  variant="contained" 
                  color="primary" 
                  onClick={handleUpload}
                  disabled={!selectedFile}
                >
                  Start Processing
                </Button>
              ) : (
                <Button 
                  variant="contained" 
                  color="secondary" 
                  startIcon={<StopIcon />}
                  onClick={handleStop}
                >
                  Stop Processing
                </Button>
              )}
            </Box>
          </Box>
        )}

        {/* Live camera mode UI */}
        {mode === 'camera' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, mb: 3 }}>
            <Box sx={{ display: 'flex', gap: 2 }}>
              {!isLiveTranslating ? (
                <Button 
                  variant="contained" 
                  color="primary" 
                  startIcon={<VideocamIcon />}
                  onClick={startLiveTranslation}
                  disabled={connectionState !== 'Connected'}
                >
                  Start Live Translation
                </Button>
              ) : (
                <Button 
                  variant="contained" 
                  color="secondary" 
                  startIcon={<StopIcon />}
                  onClick={stopLiveTranslation}
                >
                  Stop Live Translation
                </Button>
              )}
              
              {cameraStream && !isLiveTranslating && (
                <Button 
                  variant="outlined"
                  onClick={stopCameraStream}
                >
                  Turn Off Camera
                </Button>
              )}
            </Box>
            
            {/* Camera status message */}
            {cameraStream ? (
              <Typography variant="body2" color="success.main">
                Camera is active. Click "Start Live Translation" to begin.
              </Typography>
            ) : (
              <Typography variant="body2" color="text.secondary">
                Click "Start Live Translation" to enable your camera and begin translating text in real-time.
              </Typography>
            )}
            
            {/* Frame rate info when live translating */}
            {isLiveTranslating && (
              <Typography variant="body2" color="text.secondary">
                Processing {frameRate} frames per second. Higher frame rates may impact performance.
              </Typography>
            )}
          </Box>
        )}

        {error && (
          <Alert severity="error" sx={{ mt: 2 }}>
            <AlertTitle>Error</AlertTitle>
            {error}
          </Alert>
        )}

        {cameraError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            <AlertTitle>Camera Error</AlertTitle>
            {cameraError}
            <Typography variant="body2" sx={{ mt: 1 }}>
              Please make sure you have granted camera permissions to this website.
            </Typography>
          </Alert>
        )}

        {connectionLost && (
          <Alert severity="warning" sx={{ mt: 2 }}>
            <AlertTitle>Connection Lost</AlertTitle>
            The connection to the server has been lost. Attempting to reconnect...
            <Button 
              variant="outlined" 
              size="small" 
              sx={{ mt: 1 }}
              onClick={() => window.location.reload()}
            >
              Reload Page
            </Button>
          </Alert>
        )}

        {isProcessing && (
          <Box sx={{ mt: 2 }}>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
              <Typography variant="body2">
                Processing video: {frameNumber} / {totalFrames} frames ({progress.toFixed(1)}%)
              </Typography>
              <Typography variant="body2" color="primary.main" fontWeight="medium">
                {processingStatus}
              </Typography>
            </Box>
            <LinearProgress variant="determinate" value={progress} sx={{ mb: 2 }} />
          </Box>
        )}
      </Paper>

      {/* Video/Camera preview */}
      <Grid container spacing={3}>
        <Grid item xs={12} md={8}>
          {(currentFrame || mode === 'camera') && (
            <Box>
              <Typography variant="h6" sx={{ mb: 2 }}>
                {mode === 'camera' ? 'Camera Preview' : 'Video Preview'}
              </Typography>
              <Box sx={{ 
                overflow: 'hidden', 
                maxWidth: '100%', 
                border: '1px solid #ddd',
                minHeight: '300px',
                display: 'flex',
                justifyContent: 'center',
                alignItems: 'center',
                position: 'relative' // Important for absolute positioning children
              }}>
                {/* IMPORTANT: Always render both elements, control visibility through styles */}
                
                {/* Camera feed */}
                <video 
                  ref={videoRef}
                  style={{ 
                    width: '100%',
                    height: 'auto',
                    maxHeight: '70vh',
                    display: mode === 'camera' && cameraStream && !isLiveTranslating ? 'block' : 'none'
                  }} 
                  autoPlay 
                  playsInline 
                  muted
                />
                
                {/* Canvas for processed frames */}
                <canvas 
                  ref={canvasRef} 
                  style={{ 
                    maxWidth: '100%', 
                    height: 'auto',
                    minHeight: '300px',
                    display: (mode === 'upload' && currentFrame) || (mode === 'camera' && isLiveTranslating) ? 'block' : 'none'
                  }} 
                  width={640} 
                  height={480}
                />
                
                {/* Camera status message */}
                {mode === 'camera' && !cameraStream && (
                  <Box sx={{ 
                    position: 'absolute',
                    top: '50%',
                    left: '50%',
                    transform: 'translate(-50%, -50%)',
                    textAlign: 'center',
                    color: 'text.secondary'
                  }}>
                    <Typography>
                      Camera inactive. Click "Start Live Translation" to enable your camera.
                    </Typography>
                  </Box>
                )}

                {/* Debug info */}
                {mode === 'camera' && cameraStream && !isLiveTranslating && (
                  <Box sx={{ 
                    position: 'absolute', 
                    top: 0, 
                    right: 0, 
                    bgcolor: 'rgba(0,0,0,0.5)', 
                    color: 'white',
                    p: 1,
                    fontSize: '12px'
                  }}>
                    Camera active
                  </Box>
                )}
              </Box>
            </Box>
          )}
        </Grid>

        <Grid item xs={12} md={4}>
          {/* Audio transcription panel with toggle */}
          {audioTranscriptions.length > 0 && (
            <Card sx={{ mb: 3 }}>
              <CardContent sx={{ pb: 1 }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
                  <Typography variant="h6">
                    Audio Transcription
                    {isProcessing && (
                      <Typography variant="caption" color="text.secondary" sx={{ ml: 1 }}>
                        (updating as video plays)
                      </Typography>
                    )}
                  </Typography>
                  <Button 
                    size="small"
                    onClick={toggleAudioPanel}
                  >
                    {showAudioPanel ? 'Hide' : 'Show'}
                  </Button>
                </Box>
                
                {showAudioPanel && (
                  <Box 
                    ref={audioScrollRef}
                    sx={{ 
                      maxHeight: '400px', 
                      overflow: 'auto',
                      scrollBehavior: 'smooth'
                    }}
                  >
                    {audioTranscriptions.length > 30 ? (
                      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                        Showing {Math.min(30, audioTranscriptions.length)} of {audioTranscriptions.length} transcriptions
                      </Typography>
                    ) : null}
                    
                    {audioTranscriptions
                      .slice(-30)
                      .map((transcription) => (
                        <AudioTranscriptionItem 
                          key={transcription.id} 
                          transcription={transcription} 
                        />
                      ))}
                  </Box>
                )}
              </CardContent>
            </Card>
          )}

          {/* Detected text panel with toggle */}
          {detectedTexts.length > 0 && (
            <Card>
              <CardContent sx={{ pb: 1 }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
                  <Typography variant="h6">
                    Detected Text
                    {isLiveTranslating && (
                      <Typography variant="caption" color="text.secondary" sx={{ ml: 1 }}>
                        (real-time)
                      </Typography>
                    )}
                  </Typography>
                  <Button 
                    size="small"
                    onClick={toggleTextPanel}
                  >
                    {showTextPanel ? 'Hide' : 'Show'}
                  </Button>
                </Box>
                
                {showTextPanel && (
                  <Box 
                    ref={textScrollRef}
                    sx={{ maxHeight: '300px', overflow: 'auto' }}
                  >
                    {detectedTexts.length > 30 ? (
                      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                        Showing {Math.min(30, detectedTexts.length)} of {detectedTexts.length} detected texts
                      </Typography>
                    ) : null}
                    
                    {detectedTexts
                      .slice(-30)
                      .map((text) => (
                        <DetectedTextItem key={text.id} text={text} />
                      ))}
                  </Box>
                )}
              </CardContent>
            </Card>
          )}
        </Grid>
      </Grid>
      
      {/* Hidden canvas for frame capture - keep this outside the visible area */}
      <canvas 
        ref={captureCanvasRef} 
        style={{ display: 'none' }}
      />
    </Box>
  );
};

export default VideoTranslator;
