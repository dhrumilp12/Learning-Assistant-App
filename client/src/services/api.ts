import axios from 'axios';

// Define the base URL for API requests
const API_BASE_URL = process.env.REACT_APP_API_URL || 'http://localhost:5000';

// Create an axios instance with common configuration
const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json'
  }
});

// Add request/response interceptors for debugging
api.interceptors.request.use(config => {
  console.log(`Making ${config.method?.toUpperCase()} request to ${config.url}`);
  return config;
});

api.interceptors.response.use(
  response => {
    console.log(`Received response from ${response.config.url}:`, response.status);
    return response;
  },
  error => {
    if (error.response) {
      console.error(`API Error (${error.response.status}):`, error.response.data);
    } else if (error.request) {
      console.error('API Error: No response received. Backend may be down or CORS issues.');
    } else {
      console.error('API Error:', error.message);
    }
    return Promise.reject(error);
  }
);

// Speech translation API calls
export const startSpeechTranslation = async (sourceLanguage: string, targetLanguage: string) => {
  try {
    return await api.post('/api/speech/start', { sourceLanguage, targetLanguage });
  } catch (error) {
    console.error('Failed to start speech translation:', error);
    throw error;
  }
};

export const stopSpeechTranslation = async () => {
  try {
    return await api.post('/api/speech/stop');
  } catch (error) {
    console.error('Failed to stop speech translation:', error);
    throw error;
  }
};

// Video translation API calls
export const uploadVideoForTranslation = async (videoFile: File, sourceLanguage: string, targetLanguage: string) => {
  try {
    const formData = new FormData();
    formData.append('videoFile', videoFile);
    formData.append('sourceLanguage', sourceLanguage);
    formData.append('targetLanguage', targetLanguage);

    return await api.post('/api/speech/translate-video', formData, {
      headers: {
        'Content-Type': 'multipart/form-data'
      }
    });
  } catch (error) {
    console.error('Failed to upload video for translation:', error);
    throw error;
  }
};

export const stopVideoTranslation = async () => {
  try {
    return await api.post('/api/speech/stop-video');
  } catch (error) {
    console.error('Failed to stop video translation:', error);
    throw error;
  }
};

// Status check
export const getServiceStatus = async () => {
  try {
    return await api.get('/api/speech/status');
  } catch (error) {
    console.error('Failed to check service status:', error);
    throw error;
  }
};

export default api;
