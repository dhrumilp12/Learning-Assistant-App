import openai
import tempfile
import os
from config import OPENAI_API_KEY

class Transcriber:
    def __init__(self):
        self.api_key = OPENAI_API_KEY
        if not self.api_key:
            raise ValueError("OpenAI API key is missing. Please check your environment variables.")
        # Set the API key directly for compatibility
        openai.api_key = self.api_key
    
    def transcribe_audio(self, audio_file_path, language=None):
        """
        Transcribe audio file using OpenAI Whisper API
        
        Parameters:
            audio_file_path (str): Path to the audio file
            language (str, optional): Language code for transcription
            
        Returns:
            str: Transcribed text
        """
        try:
            with open(audio_file_path, "rb") as audio_file:
                # Try to use the current OpenAI API version
                try:
                    # For newer OpenAI SDK versions that support the Client model
                    from openai import OpenAI
                    client = OpenAI(api_key=self.api_key)
                    
                    response = client.audio.transcriptions.create(
                        model="whisper-1",
                        file=audio_file,
                        language=language if language else None
                    )
                    
                    return response.text if hasattr(response, 'text') else str(response)
                    
                except (ImportError, AttributeError, TypeError):
                    # Fallback for older versions of the OpenAI SDK
                    print("Using fallback OpenAI API method")
                    audio_file.seek(0)  # Reset file pointer
                    
                    # Try the legacy API format
                    response = openai.Audio.transcribe(
                        model="whisper-1",
                        file=audio_file,
                        language=language
                    )
                    
                    return response.get('text', '')
        except Exception as e:
            print(f"Error transcribing audio: {e}")
            return ""
            
    def transcribe_chunks(self, chunk_paths, language=None):
        """Transcribe multiple audio chunks and combine the results"""
        transcriptions = []
        
        for i, chunk_path in enumerate(chunk_paths):
            print(f"Transcribing chunk {i+1}/{len(chunk_paths)}...")
            text = self.transcribe_audio(chunk_path, language)
            transcriptions.append(text)
            # Clean up temporary chunk files
            if os.path.exists(chunk_path):
                os.remove(chunk_path)
                
        return " ".join(transcriptions)
