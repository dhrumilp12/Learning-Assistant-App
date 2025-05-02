import React, { useState, useEffect } from 'react';
import { 
  Box, Typography, Paper, Button, CircularProgress, 
  Alert, AlertTitle, Card, CardContent,
  Dialog, DialogTitle, DialogContent, DialogActions,
  TextField, IconButton, Snackbar, Grid, Divider
} from '@mui/material';
import MicIcon from '@mui/icons-material/Mic';
import StopIcon from '@mui/icons-material/Stop';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import CloseIcon from '@mui/icons-material/Close';
import { useHubConnection } from '../contexts/HubConnectionContext';
import { startSpeechTranslation, stopSpeechTranslation } from '../services/api';
import LanguageSelector from './shared/LanguageSelector';
import { TranslationResult } from '../types/translations';

const SpeechTranslator: React.FC = () => {
  const { connection, connectionState } = useHubConnection();
  const [isTranslating, setIsTranslating] = useState<boolean>(false);
  const [sourceLanguage, setSourceLanguage] = useState<string>('en');
  const [targetLanguage, setTargetLanguage] = useState<string>('es');
  const [translations, setTranslations] = useState<TranslationResult[]>([]);
  const [interimTranslation, setInterimTranslation] = useState<TranslationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  
  // Add states for the full translation dialog
  const [showFullTranslation, setShowFullTranslation] = useState<boolean>(false);
  const [fullOriginalText, setFullOriginalText] = useState<string>('');
  const [fullTranslatedText, setFullTranslatedText] = useState<string>('');
  const [copySuccess, setCopySuccess] = useState<string | null>(null);

  useEffect(() => {
    if (!connection) return;

    // Handle connected event
    connection.on('Connected', (message: string) => {
      console.log('Connected to hub:', message);
    });

    // Handle translation started event
    connection.on('TranslationStarted', () => {
      setError(null);
    });

    // Handle translated text
    connection.on('ReceiveTranslation', 
      (original: string, translated: string, sourceLang: string, targetLang: string) => {
        const newTranslation: TranslationResult = {
          id: Date.now(),
          originalText: original,
          translatedText: translated,
          sourceLanguage: sourceLang,
          targetLanguage: targetLang,
          isInterim: false,
          timestamp: new Date()
        };

        setTranslations(prev => [...prev, newTranslation]);
        setInterimTranslation(null); // Clear interim once we have final
      }
    );

    // Handle interim results
    connection.on('ReceiveInterimTranslation',
      (original: string, translated: string, sourceLang: string, targetLang: string) => {
        const interimResult: TranslationResult = {
          id: Date.now(),
          originalText: original,
          translatedText: translated,
          sourceLanguage: sourceLang,
          targetLanguage: targetLang,
          isInterim: true,
          timestamp: new Date()
        };

        setInterimTranslation(interimResult);
      }
    );

    // Handle full translation (received when stopping)
    connection.on('ReceiveFullTranslation', (original: string, translated: string) => {
      setFullOriginalText(original);
      setFullTranslatedText(translated);
      setShowFullTranslation(true);
    });

    // Handle translation ended
    connection.on('TranslationEnded', () => {
      setIsTranslating(false);
      setInterimTranslation(null);
    });

    // Handle errors
    connection.on('TranslationError', (errorMessage: string) => {
      setError(errorMessage);
      setIsTranslating(false);
    });

    return () => {
      // Clean up event listeners when component unmounts
      connection.off('Connected');
      connection.off('TranslationStarted');
      connection.off('ReceiveTranslation');
      connection.off('ReceiveInterimTranslation');
      connection.off('ReceiveFullTranslation');
      connection.off('TranslationEnded');
      connection.off('TranslationError');
    };
  }, [connection]);

  const handleStartTranslation = async () => {
    try {
      setError(null);
      setIsTranslating(true);
      
      await startSpeechTranslation(sourceLanguage, targetLanguage);
    } catch (err: any) {
      setError(err.message || 'Failed to start translation');
      setIsTranslating(false);
    }
  };

  const handleStopTranslation = async () => {
    try {
      await stopSpeechTranslation();
      // TranslationEnded event will set isTranslating to false
      // ReceiveFullTranslation event will trigger the dialog
    } catch (err: any) {
      setError(err.message || 'Failed to stop translation');
      setIsTranslating(false);
    }
  };

  const handleCopyText = (text: string, type: string) => {
    navigator.clipboard.writeText(text)
      .then(() => {
        setCopySuccess(`${type} text copied to clipboard`);
        setTimeout(() => setCopySuccess(null), 3000);
      })
      .catch(err => {
        setError(`Failed to copy text: ${err.message}`);
      });
  };

  const handleCloseDialog = () => {
    setShowFullTranslation(false);
  };

  return (
    <Box>
      <Paper elevation={3} sx={{ p: 3, mb: 4 }}>
        <Typography variant="h5" component="h1" gutterBottom>
          Speech Translation
        </Typography>
        <Typography variant="body1" sx={{ mb: 3 }}>
          Translate speech in real-time using your microphone. Choose your source and target languages below.
        </Typography>

        <Box sx={{ display: 'flex', flexDirection: { xs: 'column', sm: 'row' }, gap: 2, mb: 3 }}>
          <LanguageSelector 
            label="Source Language"
            value={sourceLanguage}
            onChange={(e) => setSourceLanguage(e.target.value as string)}
            disabled={isTranslating}
          />
          
          <LanguageSelector 
            label="Target Language"
            value={targetLanguage}
            onChange={(e) => setTargetLanguage(e.target.value as string)}
            disabled={isTranslating}
          />
        </Box>
        
        <Box sx={{ display: 'flex', gap: 2 }}>
          {!isTranslating ? (
            <Button 
              variant="contained" 
              color="primary" 
              startIcon={<MicIcon />} 
              onClick={handleStartTranslation}
              disabled={connectionState !== 'Connected'}
            >
              Start Listening
            </Button>
          ) : (
            <Button 
              variant="contained" 
              color="secondary" 
              startIcon={<StopIcon />}
              onClick={handleStopTranslation}
            >
              Stop Listening
            </Button>
          )}
        </Box>

        {error && (
          <Alert severity="error" sx={{ mt: 2 }}>
            <AlertTitle>Error</AlertTitle>
            {error}
          </Alert>
        )}

        {isTranslating && (
          <Box sx={{ display: 'flex', alignItems: 'center', mt: 2 }}>
            <CircularProgress size={20} sx={{ mr: 1 }} />
            <Typography variant="body2" color="text.secondary">
              Listening...
            </Typography>
          </Box>
        )}
      </Paper>

      {/* Display interim results */}
      {interimTranslation && (
        <Card sx={{ mb: 2, bgcolor: 'background.default', border: '1px dashed' }}>
          <CardContent>
            <Typography variant="body1" className="original-text interim">
              {interimTranslation.originalText}
            </Typography>
            <Typography variant="body1" className="translated-text interim">
              {interimTranslation.translatedText}
            </Typography>
          </CardContent>
        </Card>
      )}

      {/* Display translation results */}
      <Box>
        <Typography variant="h6" sx={{ mb: 2 }}>
          Translation Results
        </Typography>

        {translations.length === 0 ? (
          <Typography variant="body2" color="text.secondary">
            No translations yet. Start listening to see results.
          </Typography>
        ) : (
          translations.map((translation) => (
            <Card key={translation.id} sx={{ mb: 2 }}>
              <CardContent>
                <Typography variant="body1" className="original-text">
                  {translation.originalText}
                </Typography>
                <Typography variant="body1" className="translated-text">
                  {translation.translatedText}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {translation.timestamp.toLocaleTimeString()}
                </Typography>
              </CardContent>
            </Card>
          ))
        )}
      </Box>

      {/* Full Translation Dialog */}
      <Dialog 
        open={showFullTranslation} 
        onClose={handleCloseDialog}
        maxWidth="md"
        fullWidth
      >
        <DialogTitle>
          Complete Translation
          <IconButton
            aria-label="close"
            onClick={handleCloseDialog}
            sx={{
              position: 'absolute',
              right: 8,
              top: 8,
            }}
          >
            <CloseIcon />
          </IconButton>
        </DialogTitle>
        <DialogContent dividers>
          <Grid container spacing={3}>
            <Grid item xs={12} md={6}>
              <Typography variant="h6" gutterBottom>
                Original Text 
                <IconButton 
                  size="small"
                  color="primary"
                  onClick={() => handleCopyText(fullOriginalText, 'Original')}
                  title="Copy original text"
                >
                  <ContentCopyIcon fontSize="small" />
                </IconButton>
              </Typography>
              <TextField
                multiline
                fullWidth
                minRows={8}
                maxRows={15}
                value={fullOriginalText}
                InputProps={{
                  readOnly: true,
                }}
                variant="outlined"
              />
            </Grid>
            
            <Grid item xs={12} md={6}>
              <Typography variant="h6" gutterBottom>
                Translated Text
                <IconButton 
                  size="small"
                  color="primary"
                  onClick={() => handleCopyText(fullTranslatedText, 'Translated')}
                  title="Copy translated text"
                >
                  <ContentCopyIcon fontSize="small" />
                </IconButton>
              </Typography>
              <TextField
                multiline
                fullWidth
                minRows={8}
                maxRows={15}
                value={fullTranslatedText}
                InputProps={{
                  readOnly: true,
                }}
                variant="outlined"
              />
            </Grid>
            
            <Grid item xs={12}>
              <Divider sx={{ my: 2 }} />
              <Button 
                variant="contained" 
                color="primary"
                onClick={() => handleCopyText(`Original: ${fullOriginalText}\n\nTranslation: ${fullTranslatedText}`, 'Complete')}
                startIcon={<ContentCopyIcon />}
                fullWidth
              >
                Copy Both Texts
              </Button>
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={handleCloseDialog}>Close</Button>
        </DialogActions>
      </Dialog>

      {/* Copy success notification */}
      <Snackbar
        open={copySuccess !== null}
        autoHideDuration={3000}
        onClose={() => setCopySuccess(null)}
        message={copySuccess}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      />
    </Box>
  );
};

export default SpeechTranslator;
