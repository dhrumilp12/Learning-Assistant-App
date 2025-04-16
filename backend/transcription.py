import openai
import tempfile
import os
import time
from config import OPENAI_API_KEY

class Transcriber:
    def __init__(self):
        self.api_key = OPENAI_API_KEY
        if not self.api_key:
            raise ValueError("OpenAI API key is missing. Please check your environment variables.")
        # Set the API key directly for compatibility
        openai.api_key = self.api_key
        # Track consecutive empty transcriptions
        self.empty_count = 0
        # Track last transcription time
        self.last_transcription_time = 0
    
    def transcribe_audio(self, audio_file_path, language=None, is_realtime=False, debug_mode=False):
        """
        Transcribe audio file using OpenAI Whisper API
        
        Parameters:
            audio_file_path (str): Path to the audio file
            language (str, optional): Language code for transcription
            is_realtime (bool): Whether this is for real-time processing
            debug_mode (bool): Whether to print detailed debug information
            
        Returns:
            str: Transcribed text
        """
        # Skip processing if we've had too many consecutive empty results
        # but only in realtime mode and not if enough time has passed
        current_time = time.time()
        if is_realtime and self.empty_count > 3 and (current_time - self.last_transcription_time < 5):
            if debug_mode:
                print("Skipping processing due to consecutive empty results")
            return ""
            
        try:
            # Check file size
            file_size = os.path.getsize(audio_file_path)
            if file_size < 1024:  # Less than 1KB
                if debug_mode:
                    print(f"Audio file too small ({file_size} bytes), likely no speech")
                if is_realtime:
                    self.empty_count += 1
                return ""
                
            if debug_mode:
                print(f"Processing audio file: {audio_file_path} ({file_size} bytes)")
            
            with open(audio_file_path, "rb") as audio_file:
                # Try to use the current OpenAI API version
                try:
                    # For newer OpenAI SDK versions that support the Client model
                    from openai import OpenAI
                    client = OpenAI(api_key=self.api_key)
                    
                    # Whisper API parameters
                    params = {
                        "model": "whisper-1",
                        "file": audio_file,
                    }
                    
                    if language:
                        params["language"] = language
                    
                    # Force high-quality transcription for real-time mode
                    if is_realtime:
                        if debug_mode:
                            print("Using high quality real-time transcription mode")
                        params["response_format"] = "text"
                    
                    # Call API
                    if debug_mode:
                        print("Sending request to OpenAI API...")
                    response = client.audio.transcriptions.create(**params)
                    
                    result = response.text if hasattr(response, 'text') else str(response)
                    
                    # Update tracking variables
                    if result.strip():
                        if debug_mode:
                            print(f"Transcription result: '{result}'")
                        self.empty_count = 0  # Reset empty count
                        self.last_transcription_time = current_time
                    elif is_realtime:
                        self.empty_count += 1
                        if debug_mode:
                            print(f"Empty result (count: {self.empty_count})")
                    
                    return result
                    
                except (ImportError, AttributeError, TypeError) as e:
                    # Fallback for older versions of the OpenAI SDK
                    if debug_mode:
                        print(f"Using fallback OpenAI API method, error: {e}")
                    audio_file.seek(0)  # Reset file pointer
                    
                    # Try the legacy API format
                    response = openai.Audio.transcribe(
                        model="whisper-1",
                        file=audio_file,
                        language=language
                    )
                    
                    result = response.get('text', '')
                    if result.strip():
                        self.empty_count = 0  # Reset empty count
                        self.last_transcription_time = current_time
                    elif is_realtime:
                        self.empty_count += 1
                    
                    return result
        except Exception as e:
            if debug_mode:
                print(f"Error transcribing audio: {e}")
            if is_realtime:
                self.empty_count += 1
            return ""
            
    def reset_state(self):
        """Reset internal state counters for a fresh start"""
        self.empty_count = 0
        self.last_transcription_time = 0
            
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
