import React from 'react';
import { AppBar, Toolbar, Typography, Button, Box, Badge } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import { useHubConnection } from '../contexts/HubConnectionContext';
import MicIcon from '@mui/icons-material/Mic';
import VideocamIcon from '@mui/icons-material/Videocam';
import HomeIcon from '@mui/icons-material/Home';
import FiberManualRecordIcon from '@mui/icons-material/FiberManualRecord';
import * as signalR from '@microsoft/signalr';

const Navbar: React.FC = () => {
  const { connectionState } = useHubConnection();
  
  const getConnectionStatusColor = () => {
    switch (connectionState) {
      case signalR.HubConnectionState.Connected:
        return 'success';
      case signalR.HubConnectionState.Connecting:
      case signalR.HubConnectionState.Reconnecting:
        return 'warning';
      default:
        return 'error';
    }
  };

  return (
    <AppBar position="static">
      <Toolbar>
        <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
          Speech Translator
        </Typography>
        
        <Box sx={{ mr: 2 }}>
          <Badge badgeContent="" color={getConnectionStatusColor()} variant="dot">
            <FiberManualRecordIcon fontSize="small" />
          </Badge>
        </Box>
        
        <Button 
          color="inherit" 
          component={RouterLink} 
          to="/"
          startIcon={<HomeIcon />}
        >
          Home
        </Button>
        
        <Button 
          color="inherit" 
          component={RouterLink} 
          to="/speech"
          startIcon={<MicIcon />}
        >
          Speech
        </Button>
        
        <Button 
          color="inherit" 
          component={RouterLink} 
          to="/video"
          startIcon={<VideocamIcon />}
        >
          Video
        </Button>
      </Toolbar>
    </AppBar>
  );
};

export default Navbar;
