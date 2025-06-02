import React, { useEffect, useState } from 'react';
import { Box, Typography, Paper, Grid, Button, Card, CardContent, CardActions, Alert } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import MicIcon from '@mui/icons-material/Mic';
import VideocamIcon from '@mui/icons-material/Videocam';
import { getServiceStatus } from '../services/api';
import { useHubConnection } from '../contexts/HubConnectionContext';

const Home: React.FC = () => {
  const [serviceStatus, setServiceStatus] = useState<string>('Checking...');
  const [error, setError] = useState<string | null>(null);
  const { connectionState, connectionError, startConnection } = useHubConnection();

  useEffect(() => {
    const checkStatus = async () => {
      try {
        await getServiceStatus();
        setServiceStatus('Online');
        setError(null);
      } catch (err: any) {
        setServiceStatus('Offline');
        setError(err.message || 'Unable to connect to the translation service.');
      }
    };

    checkStatus();

    // Try to restart the connection if we're not connected
    if (connectionState !== 'Connected') {
      startConnection();
    }
  }, [connectionState, startConnection]);

  return (
    <Box>
      <Paper elevation={3} sx={{ p: 3, mb: 4, textAlign: 'center' }}>
        <Typography variant="h4" component="h1" gutterBottom>
          Welcome to Speech Translator
        </Typography>
        <Typography variant="subtitle1" color="text.secondary" sx={{ mb: 2 }}>
          Real-time speech and video translation powered by Azure AI
        </Typography>
        <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', mb: 2 }}>
          <Typography variant="body2" sx={{ mr: 1 }}>
            Service Status:
          </Typography>
          <Typography 
            variant="body2" 
            sx={{ 
              color: serviceStatus === 'Online' ? 'success.main' : 'error.main',
              fontWeight: 'bold'
            }}
          >
            {serviceStatus}
          </Typography>
        </Box>
        
        {/* Display SignalR connection status */}
        <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', mb: 2 }}>
          <Typography variant="body2" sx={{ mr: 1 }}>
            Connection Status:
          </Typography>
          <Typography 
            variant="body2" 
            sx={{ 
              color: connectionState === 'Connected' ? 'success.main' : 'warning.main',
              fontWeight: 'bold'
            }}
          >
            {connectionState}
          </Typography>
        </Box>
        
        {(error || connectionError) && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error || connectionError}
          </Alert>
        )}

        {(serviceStatus === 'Offline' || connectionState !== 'Connected') && (
          <Alert severity="info" sx={{ mb: 2 }}>
            <Typography variant="body2">
              Troubleshooting tips:
            </Typography>
            <ul>
              <li>Make sure the backend server is running at {process.env.REACT_APP_API_URL || 'http://localhost:5000'}</li>
              <li>Check for console errors in developer tools (F12)</li>
              <li>Verify the server is running with <code>dotnet run</code> in the server directory</li>
              <li>Ensure the correct ports (5000 for HTTP) are open and not blocked by firewalls</li>
            </ul>
            <Button 
              variant="outlined" 
              size="small" 
              sx={{ mt: 1 }}
              onClick={() => {
                setServiceStatus('Checking...');
                setError(null);
                startConnection();
                getServiceStatus().then(() => {
                  setServiceStatus('Online');
                }).catch(err => {
                  setServiceStatus('Offline');
                  setError(err.message);
                });
              }}
            >
              Retry Connection
            </Button>
          </Alert>
        )}
      </Paper>

      <Grid container spacing={3}>
        <Grid item xs={12} md={6}>
          <Card>
            <CardContent>
              <Typography variant="h5" component="div" sx={{ mb: 2 }}>
                Speech Translation
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                Start real-time speech translation using your microphone. 
                Support for multiple languages with instant feedback.
              </Typography>
            </CardContent>
            <CardActions>
              <Button 
                size="large" 
                color="primary" 
                component={RouterLink} 
                to="/speech" 
                startIcon={<MicIcon />}
                fullWidth
                disabled={connectionState !== 'Connected'}
              >
                Start Speech Translation
              </Button>
            </CardActions>
          </Card>
        </Grid>

        <Grid item xs={12} md={6}>
          <Card>
            <CardContent>
              <Typography variant="h5" component="div" sx={{ mb: 2 }}>
                Video Translation
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                Use your camera for real-time text translation or upload a video file.
                Translated text is overlaid on the video in real-time.
              </Typography>
            </CardContent>
            <CardActions>
              <Button 
                size="large" 
                color="primary" 
                component={RouterLink} 
                to="/video" 
                startIcon={<VideocamIcon />}
                fullWidth
                disabled={connectionState !== 'Connected'}
              >
                Start Video Translation
              </Button>
            </CardActions>
          </Card>
        </Grid>
      </Grid>
    </Box>
  );
};

export default Home;
