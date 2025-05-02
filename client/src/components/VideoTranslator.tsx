import React, { useState, useEffect, useRef } from 'react';
import { 
  Box, Typography, Paper, Button, 
  Alert, AlertTitle, LinearProgress, Card, CardContent,
  Grid
} from '@mui/material';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import StopIcon from '@mui/icons-material/Stop';
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

const VideoTranslator: React.FC = () => {
  const { connection } = useHubConnection();
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
  const [audioText, setAudioText] = useState<{ original: string, translated: string } | null>(null);

  const canvasRef = useRef<HTMLCanvasElement>(null);

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
        setAudioText({ original, translated });
      }
    );

    return () => {
      // Clean up event listeners
      connection.off('VideoMetadata');
      connection.off('ReceiveVideoFrame');
      connection.off('TextDetectedInVideo');
      connection.off('VideoProcessingComplete');
      connection.off('VideoProcessingError');
      connection.off('ReceiveVideoSpeechTranslation');
    };
  }, [connection]);

  // Effect to display the current frame
  useEffect(() => {
    if (currentFrame && canvasRef.current) {
      const canvas = canvasRef.current;
      const ctx = canvas.getContext('2d');
      
      if (ctx) {
        const img = new Image();
        img.onload = () => {
          canvas.width = img.width;
          canvas.height = img.height;
          ctx.drawImage(img, 0, 0);
        };
        img.src = `data:image/jpeg;base64,${currentFrame}`;
      }
    }
  }, [currentFrame]);

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
      setAudioText(null);
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

  return (
    <Box>
      <Paper elevation={3} sx={{ p: 3, mb: 4 }}>
        <Typography variant="h5" component="h1" gutterBottom>
          Video Translation
        </Typography>
        <Typography variant="body1" sx={{ mb: 3 }}>
          Upload a video to translate text content within the video. Choose your source and target languages.
        </Typography>

        <Box sx={{ display: 'flex', flexDirection: { xs: 'column', sm: 'row' }, gap: 2, mb: 3 }}>
          <LanguageSelector 
            label="Source Language"
            value={sourceLanguage}
            onChange={(e) => setSourceLanguage(e.target.value as string)}
            disabled={isProcessing}
          />
          
          <LanguageSelector 
            label="Target Language"
            value={targetLanguage}
            onChange={(e) => setTargetLanguage(e.target.value as string)}
            disabled={isProcessing}
          />
        </Box>

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
        </Box>

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

        {error && (
          <Alert severity="error" sx={{ mt: 2 }}>
            <AlertTitle>Error</AlertTitle>
            {error}
          </Alert>
        )}

        {isProcessing && (
          <Box sx={{ mt: 2 }}>
            <Typography variant="body2" sx={{ mb: 1 }}>
              Processing video: {frameNumber} / {totalFrames} frames ({progress.toFixed(1)}%)
            </Typography>
            <LinearProgress variant="determinate" value={progress} sx={{ mb: 2 }} />
          </Box>
        )}
      </Paper>

      {/* Video preview */}
      <Grid container spacing={3}>
        <Grid item xs={12} md={8}>
          {currentFrame && (
            <Box>
              <Typography variant="h6" sx={{ mb: 2 }}>
                Video Preview
              </Typography>
              <Box sx={{ overflow: 'auto', maxWidth: '100%' }}>
                <canvas 
                  ref={canvasRef} 
                  style={{ 
                    maxWidth: '100%', 
                    height: 'auto', 
                    border: '1px solid #ddd' 
                  }} 
                />
              </Box>
            </Box>
          )}
        </Grid>

        <Grid item xs={12} md={4}>
          {/* Audio transcription panel */}
          {audioText && (
            <Card sx={{ mb: 3 }}>
              <CardContent>
                <Typography variant="h6" gutterBottom>
                  Audio Transcription
                </Typography>
                <Typography variant="body2" sx={{ mb: 1, fontWeight: 500 }}>
                  Original:
                </Typography>
                <Typography variant="body2" sx={{ mb: 2 }}>
                  {audioText.original}
                </Typography>
                <Typography variant="body2" sx={{ mb: 1, fontWeight: 500 }}>
                  Translated:
                </Typography>
                <Typography variant="body2" sx={{ color: 'primary.main' }}>
                  {audioText.translated}
                </Typography>
              </CardContent>
            </Card>
          )}

          {/* Detected text panel */}
          {detectedTexts.length > 0 && (
            <Card>
              <CardContent>
                <Typography variant="h6" gutterBottom>
                  Detected Text
                </Typography>
                <Box sx={{ maxHeight: '300px', overflow: 'auto' }}>
                  {detectedTexts.map((text) => (
                    <Box key={text.id} sx={{ mb: 2, pb: 2, borderBottom: '1px solid #eee' }}>
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
                  ))}
                </Box>
              </CardContent>
            </Card>
          )}
        </Grid>
      </Grid>
    </Box>
  );
};

export default VideoTranslator;
