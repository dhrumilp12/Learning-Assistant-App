import sounddevice as sd
import numpy as np
import wave
import tempfile
import os
import threading
import time
from scipy.io.wavfile import write
from pydub import AudioSegment
from config import DEFAULT_SAMPLE_RATE, DEFAULT_CHANNELS

class AudioHandler:
    def __init__(self, sample_rate=DEFAULT_SAMPLE_RATE, channels=DEFAULT_CHANNELS):
        self.sample_rate = sample_rate
        self.channels = channels
        self.is_recording = False
        self.audio_data = []
        self.temp_file = None
        
    def start_recording(self, duration=None):
        """Start recording audio from microphone"""
        self.is_recording = True
        self.audio_data = []
        
        def callback(indata, frames, time, status):
            if self.is_recording:
                self.audio_data.append(indata.copy())
        
        with sd.InputStream(callback=callback, 
                           samplerate=self.sample_rate,
                           channels=self.channels):
            print("Recording started. Press Ctrl+C to stop.")
            try:
                if duration:
                    sd.sleep(int(duration * 1000))
                    self.stop_recording()
                else:
                    while self.is_recording:
                        sd.sleep(100)
            except KeyboardInterrupt:
                self.stop_recording()
                print("Recording stopped.")
                
    def stop_recording(self):
        """Stop the recording and save to a temporary file"""
        self.is_recording = False
        if not self.audio_data:
            return None
            
        # Combine all chunks of audio data
        all_audio = np.concatenate(self.audio_data, axis=0)
        
        # Create a temporary file
        fd, temp_path = tempfile.mkstemp(suffix='.wav')
        os.close(fd)
        self.temp_file = temp_path
        
        # Save audio to the temporary file
        write(temp_path, self.sample_rate, all_audio)
        return temp_path
    
    def load_audio_file(self, file_path):
        """Load an existing audio file"""
        try:
            # Create a new temporary file for consistent processing
            fd, temp_path = tempfile.mkstemp(suffix='.wav')
            os.close(fd)
            
            # Convert to WAV if needed
            audio = AudioSegment.from_file(file_path)
            audio.export(temp_path, format="wav")
            
            self.temp_file = temp_path
            return temp_path
        except Exception as e:
            print(f"Error loading audio file: {e}")
            return None
            
    def get_audio_chunks(self, chunk_size_ms=10000):
        """Split audio into chunks for processing"""
        if not self.temp_file:
            return []
            
        audio = AudioSegment.from_file(self.temp_file)
        chunks = []
        
        # Split audio into chunks
        for i in range(0, len(audio), chunk_size_ms):
            chunk = audio[i:i+chunk_size_ms]
            fd, chunk_path = tempfile.mkstemp(suffix='.wav')
            os.close(fd)
            chunk.export(chunk_path, format="wav")
            chunks.append(chunk_path)
            
        return chunks
        
    def cleanup(self):
        """Delete temporary files"""
        if self.temp_file and os.path.exists(self.temp_file):
            os.remove(self.temp_file)
    
    def start_realtime_recording(self, chunk_callback, chunk_duration_seconds=3):
        """
        Start recording audio in chunks with real-time processing
        
        Parameters:
            chunk_callback: Function to call with each chunk's file path
            chunk_duration_seconds: Duration of each audio chunk in seconds
        """
        self.is_recording = True
        self.current_chunk = []
        self.chunk_ready = threading.Event()
        self.processing_lock = threading.Lock()
        self.last_chunk_time = time.time()
        self.silence_threshold = 0.01  # Threshold for silence detection
        
        # Create a thread for processing chunks
        def chunk_processor():
            while self.is_recording:
                # Wait for a chunk to be ready or a small timeout
                self.chunk_ready.wait(timeout=0.5)
                
                if self.chunk_ready.is_set():
                    with self.processing_lock:
                        # Reset the event
                        self.chunk_ready.clear()
                        
                        # If there's data to process
                        if len(self.current_chunk) > 0:
                            # Create a temporary file for this chunk
                            fd, temp_path = tempfile.mkstemp(suffix='.wav')
                            os.close(fd)
                            
                            # Combine all audio data in this chunk
                            chunk_audio = np.concatenate(self.current_chunk, axis=0)
                            
                            # Check if the chunk is mostly silence
                            audio_energy = np.mean(np.abs(chunk_audio))
                            if audio_energy < self.silence_threshold:
                                os.remove(temp_path)  # Don't process silence
                                self.current_chunk = []
                                continue
                            
                            # Save to temp file
                            write(temp_path, self.sample_rate, chunk_audio)
                            
                            # Empty the current chunk buffer
                            self.current_chunk = []
                            
                            # Call the callback with the file path
                            chunk_callback(temp_path)
            
        # Start the processor thread
        processor_thread = threading.Thread(target=chunk_processor)
        processor_thread.daemon = True
        processor_thread.start()
        
        # Audio callback function for the InputStream
        def audio_callback(indata, frames, time_info, status):
            if self.is_recording:
                current_time = time.time()  # Use the imported time module
                
                # Add the current frame to the chunk
                with self.processing_lock:
                    self.current_chunk.append(indata.copy())
                
                # Check if we've reached the desired chunk duration
                if current_time - self.last_chunk_time >= chunk_duration_seconds:
                    self.last_chunk_time = current_time
                    self.chunk_ready.set()  # Signal that a chunk is ready for processing
        
        # Start the input stream
        self.stream = sd.InputStream(
            callback=audio_callback,
            samplerate=self.sample_rate,
            channels=self.channels,
            blocksize=int(self.sample_rate * 0.1)  # Process 100ms blocks
        )
        self.stream.start()
        
        print("Real-time recording started. Press Ctrl+C to stop.")
    
    def stop_realtime_recording(self):
        """Stop the real-time recording"""
        self.is_recording = False
        if hasattr(self, 'stream'):
            self.stream.stop()
            self.stream.close()
        print("Real-time recording stopped.")
