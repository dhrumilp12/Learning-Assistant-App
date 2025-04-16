import sounddevice as sd
import numpy as np
import wave
import tempfile
import os
import threading
import time
from scipy.io.wavfile import write
from pydub import AudioSegment
from config.config import DEFAULT_SAMPLE_RATE, DEFAULT_CHANNELS

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
    
    def _combine_chunks_with_overlap(self, history_buffer, current_chunk, overlap_seconds):
        """
        Combine audio chunks with proper overlap handling
        
        Parameters:
            history_buffer: List of previous audio chunks
            current_chunk: List of current audio chunks
            overlap_seconds: Amount of overlap in seconds
            
        Returns:
            List of combined audio data
        """
        chunk_data = []
        
        # Include previous audio data for overlap if available
        if overlap_seconds > 0 and history_buffer:
            # Calculate how much history we need
            overlap_frames = int(overlap_seconds * self.sample_rate)
            
            # Add history data if we have enough
            history_frames = 0
            for hist_chunk in reversed(history_buffer):
                chunk_data.insert(0, hist_chunk)
                history_frames += len(hist_chunk)
                if history_frames >= overlap_frames:
                    break
        
        # Add current chunk data
        chunk_data.extend(current_chunk)
        return chunk_data

    def start_realtime_recording(self, chunk_callback, chunk_duration_seconds=3, chunk_overlap_seconds=0, debug_mode=False):
        """
        Start recording audio in chunks with real-time processing and optional overlap
        
        Parameters:
            chunk_callback: Function to call with each chunk's file path
            chunk_duration_seconds: Duration of each audio chunk in seconds
            chunk_overlap_seconds: Overlap between consecutive chunks in seconds
            debug_mode: Whether to print detailed debug information
        """
        self.is_recording = True
        self.current_chunk = []
        self.chunk_ready = threading.Event()
        self.processing_lock = threading.Lock()
        self.last_chunk_time = time.time()
        self.silence_threshold = 0.005  # Reduced threshold to capture quieter speech
        self.overlap_seconds = min(chunk_overlap_seconds, chunk_duration_seconds * 0.9)  # Prevent overlap > duration
        self.history_buffer = []  # Store recent audio data for overlap
        self.last_processed_time = time.time()  # Track last processing time
        self.force_process_interval = 3.0  # Force processing every N seconds regardless of chunk size
        self.debug_mode = debug_mode  # Flag to control verbose output
        
        # Create a thread for processing chunks
        def chunk_processor():
            while self.is_recording:
                try:
                    # Wait for a chunk to be ready or a small timeout
                    self.chunk_ready.wait(timeout=0.5)
                    
                    # Check if we need to force processing due to time elapsed
                    current_time = time.time()
                    force_process = False
                    
                    if current_time - self.last_processed_time >= self.force_process_interval:
                        force_process = True
                        if self.debug_mode:
                            print("Forcing chunk processing due to elapsed time")
                    
                    # Process if we have ready data or need to force processing
                    if self.chunk_ready.is_set() or (force_process and len(self.current_chunk) > 0):
                        with self.processing_lock:
                            self.chunk_ready.clear()
                            
                            # If there's data to process
                            if len(self.current_chunk) > 0:
                                # Update last processing time
                                self.last_processed_time = current_time
                                
                                # Create combined chunk with overlap - use the class method
                                chunk_data = self._combine_chunks_with_overlap(
                                    self.history_buffer, 
                                    self.current_chunk, 
                                    self.overlap_seconds
                                )
                                
                                # Create a temporary file for this chunk
                                fd, temp_path = tempfile.mkstemp(suffix='.wav')
                                os.close(fd)
                                
                                try:
                                    # Combine all audio data in this chunk
                                    chunk_audio = np.concatenate(chunk_data, axis=0)
                                    
                                    # Check if the chunk is mostly silence and not forcing
                                    audio_energy = np.mean(np.abs(chunk_audio))
                                    if audio_energy < self.silence_threshold and not force_process:
                                        if self.debug_mode:
                                            print(f"Skipping silence chunk (energy: {audio_energy:.5f})")
                                        os.remove(temp_path)  # Don't process silence
                                        # Don't fully clear current_chunk, keep some for context
                                        if len(self.current_chunk) > 2:
                                            self.current_chunk = self.current_chunk[-2:]
                                        continue
                                    
                                    # Save to temp file
                                    write(temp_path, self.sample_rate, chunk_audio)
                                    
                                    if self.debug_mode:
                                        print(f"Created audio chunk: {os.path.basename(temp_path)} "
                                              f"({len(chunk_audio)} samples, {audio_energy:.5f} energy)")
                                    
                                    # Store in history buffer before clearing
                                    self.history_buffer.extend(self.current_chunk)
                                    
                                    # Limit history buffer size
                                    if len(self.history_buffer) > 20:  # Increased history size
                                        self.history_buffer = self.history_buffer[-20:]
                                    
                                    # Empty the current chunk buffer but keep a small overlap
                                    # This ensures continuity between chunks
                                    if len(self.current_chunk) > 2:
                                        self.current_chunk = self.current_chunk[-2:]
                                    else:
                                        self.current_chunk = []
                                    
                                    # Call the callback with the file path
                                    chunk_callback(temp_path)
                                except Exception as e:
                                    if self.debug_mode:
                                        print(f"Error processing audio chunk: {e}")
                                    if os.path.exists(temp_path):
                                        os.remove(temp_path)
                except Exception as e:
                    if self.debug_mode:
                        print(f"Error in chunk processor thread: {e}")
        
        # Start the processor thread
        processor_thread = threading.Thread(target=chunk_processor)
        processor_thread.daemon = True
        processor_thread.start()
        
        # Start a watchdog thread to ensure processing continues
        def watchdog():
            while self.is_recording:
                time.sleep(2.0)  # Check every 2 seconds
                current_time = time.time()
                
                # If no processing has happened for too long, force it
                if current_time - self.last_processed_time >= self.force_process_interval * 2:
                    with self.processing_lock:
                        if len(self.current_chunk) > 0:
                            if self.debug_mode:
                                print("Watchdog forcing chunk processing")
                            self.chunk_ready.set()
        
        # Start the watchdog thread
        watchdog_thread = threading.Thread(target=watchdog)
        watchdog_thread.daemon = True
        watchdog_thread.start()
        
        # Audio callback function for the InputStream
        def audio_callback(indata, frames, time_info, status):
            if self.is_recording:
                current_time = time.time()
                
                # Add the current frame to the chunk
                with self.processing_lock:
                    # Check for audio signal in this frame before adding
                    frame_energy = np.mean(np.abs(indata))
                    self.current_chunk.append(indata.copy())
                    
                    # Signal processing if enough time has passed or buffer is getting large
                    if (current_time - self.last_chunk_time >= chunk_duration_seconds or
                            len(self.current_chunk) > int(chunk_duration_seconds * self.sample_rate / 1024)):
                        self.last_chunk_time = current_time
                        self.chunk_ready.set()
        
        # Start the input stream with smaller blocks for more responsive processing
        self.stream = sd.InputStream(
            callback=audio_callback,
            samplerate=self.sample_rate,
            channels=self.channels,
            blocksize=int(self.sample_rate * 0.03)  # Process 30ms blocks for better responsiveness
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
