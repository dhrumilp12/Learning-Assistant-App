import React from 'react';
import { Routes, Route } from 'react-router-dom';
import { Container, Box } from '@mui/material';
import Navbar from './components/Navbar';
import SpeechTranslator from './components/SpeechTranslator';
import VideoTranslator from './components/VideoTranslator';
import Home from './components/Home';
import { HubConnectionProvider } from './contexts/HubConnectionContext';

const App: React.FC = () => {
  return (
    <HubConnectionProvider>
      <Navbar />
      <Container maxWidth="lg">
        <Box sx={{ mt: 4, mb: 4 }}>
          <Routes>
            <Route path="/" element={<Home />} />
            <Route path="/speech" element={<SpeechTranslator />} />
            <Route path="/video" element={<VideoTranslator />} />
          </Routes>
        </Box>
      </Container>
    </HubConnectionProvider>
  );
};

export default App;
