import os
import time
import threading
from config.config import DEFAULT_LANGUAGE, DEFAULT_TARGET_LANGUAGE
from handler.audio_handler import AudioHandler
from services.transcription import Transcriber
from services.translation import Translator
import utils.utils as utils

class LearningAssistantApp:
    def __init__(self):
        self.audio_handler = AudioHandler()
        self.transcriber = Transcriber()
        self.translator = Translator()
        self.source_language = DEFAULT_LANGUAGE
        self.target_language = DEFAULT_TARGET_LANGUAGE
        
    def process_live_audio(self):
        """Record and process live audio"""
        try:
            print(f"Recording live audio...")
            print("Source language: {self.source_language or 'Auto-detect'}")
            print(f"Target language: {self.target_language}")
            print("Press Ctrl+C to stop recording...")
            
            # Start recording
            self.audio_handler.start_recording()
            
            # Get the recorded audio file path
            audio_path = self.audio_handler.temp_file
            if not audio_path:
                print("No audio was recorded.")
                return
                
            self._process_audio_file(audio_path)
            
        except KeyboardInterrupt:
            print("\nRecording stopped by user.")
        finally:
            self.audio_handler.cleanup()
    
    def process_recorded_audio(self):
        """Process an existing audio file"""
        file_path = utils.get_file_path()
        if not file_path:
            return
            
        print(f"Processing audio file: {file_path}")
        print("Source language: {self.source_language or 'Auto-detect'}")
        print(f"Target language: {self.target_language}")
        
        # Load and process the audio file
        audio_path = self.audio_handler.load_audio_file(file_path)
        if not audio_path:
            print("Failed to load audio file.")
            return
            
        self._process_audio_file(audio_path)
    
    def _process_audio_file(self, audio_path):
        """Process audio file: transcribe and translate"""
        print("Processing audio...")
        
        # Split audio into chunks for efficient processing
        chunks = self.audio_handler.get_audio_chunks()
        if not chunks:
            print("Error splitting audio into chunks.")
            return
            
        # Transcribe audio
        print("Transcribing audio...")
        transcription = self.transcriber.transcribe_chunks(chunks, self.source_language)
        
        if not transcription:
            print("Transcription failed or resulted in empty text.")
            return
            
        # Translate text
        print("Translating text...")
        translation = self.translator.translate_text(
            transcription, 
            self.target_language,
            self.source_language
        )
        
        # Display results
        print("\n===== RESULTS =====")
        print("\nOriginal Transcription:")
        print(transcription)
        print("\nTranslation:")
        print(translation)
        print("\n==================")
    
    def process_realtime_audio(self):
        """Record and process audio in real-time with immediate translation"""
        try:
            print(f"Starting real-time translation...")
            print(f"Source language: {self.source_language or 'Auto-detect'}")
            print(f"Target language: {self.target_language}")
            print("Press Ctrl+C to stop recording...")
            print("\nListening...\n")
            
            # Reset the transcriber state
            self.transcriber.reset_state()
            
            # Lock for updating the display
            self.display_lock = threading.Lock()
            
            # Buffers for accumulated text
            self.current_transcription = ""
            self.current_translation = ""
            
            # Debug mode flag - set to False for cleaner output
            debug_mode = False
            
            # Function to process each audio chunk
            def process_chunk(audio_chunk_path):
                # Transcribe the chunk with real-time flag set to True
                text = self.transcriber.transcribe_audio(
                    audio_chunk_path, 
                    self.source_language,
                    is_realtime=True,
                    debug_mode=debug_mode
                )
                
                if text.strip():
                    # Translate the text
                    translation = self.translator.translate_text(
                        text,
                        self.target_language,
                        self.source_language
                    )
                    
                    # Update the buffers
                    with self.display_lock:
                        # If too long, trim the history
                        if len(self.current_transcription) > 1000:
                            self.current_transcription = self.current_transcription[-500:]
                            self.current_translation = self.current_translation[-500:]
                        
                        # Add new content
                        self._add_text_smartly(text, translation)
                    
                    # Display the updated text
                    utils.display_live_caption(self.current_transcription, self.current_translation)
                
                # Clean up the temporary file
                if os.path.exists(audio_chunk_path):
                    os.remove(audio_chunk_path)
            
            # Start real-time recording with shorter chunks for better responsiveness
            self.audio_handler.start_realtime_recording(
                process_chunk, 
                chunk_duration_seconds=1.5,
                chunk_overlap_seconds=0.5,
                debug_mode=debug_mode
            )
            
            # Keep the main thread alive until Ctrl+C
            try:
                while True:
                    time.sleep(0.1)
            except KeyboardInterrupt:
                pass
            
        except Exception as e:
            print(f"Error in real-time processing: {e}")
        finally:
            # Clean up audio buffer
            for path in self.audio_buffer:
                if os.path.exists(path):
                    os.remove(path)
            self.audio_buffer = []
            
            self.audio_handler.stop_realtime_recording()
            self.audio_handler.cleanup()
            
    def _add_text_smartly(self, new_text, new_translation):
        """Add new text and translation without redundancy"""
        # First addition
        if not self.current_transcription:
            self.current_transcription = new_text
            self.current_translation = new_translation
            return
            
        # Find potential overlapping content
        words = new_text.split()
        if len(words) <= 3:  # Very short text, just append
            self.current_transcription += " " + new_text
            self.current_translation += " " + new_translation
            return
            
        # Try to find overlap to avoid duplication
        current_words = self.current_transcription.split()
        overlap_found = False
        
        # Check for overlapping phrases (3+ words)
        for i in range(3, min(len(words) + 1, 10)):  # Check phrases up to 10 words long
            phrase_to_check = " ".join(words[:i])
            if self.current_transcription.strip().endswith(phrase_to_check):
                # Found overlap - append only the new part
                self.current_transcription += " " + " ".join(words[i:])
                self.current_translation += " " + new_translation  # Translation is harder to split, append all
                overlap_found = True
                break
                
        # If no overlap, just append with proper spacing
        if not overlap_found:
            # Check if we should start a new line for a clearly different sentence
            if (new_text[0].isupper() and 
                self.current_transcription and 
                self.current_transcription[-1] in '.!?'):
                self.current_transcription += "\n" + new_text
                self.current_translation += "\n" + new_translation
            else:
                self.current_transcription += " " + new_text
                self.current_translation += " " + new_translation
    
    def _is_new_content(self, text):
        """Determine if the text contains meaningful new content"""
        # Skip empty text
        if not text.strip():
            return False
            
        # If there's no previous text, this is new
        if not self.last_processed_text:
            return True
            
        # Check for similarity with last processed text
        # Simple approach: if the text is too similar to previous, skip it
        from difflib import SequenceMatcher
        similarity = SequenceMatcher(None, self.last_processed_text.lower(), 
                                    text.lower()).ratio()
        
        # If similarity is high (e.g., >0.7), it's probably a repetition
        if similarity > 0.7:
            return False
            
        # Check if there's enough new content
        unique_words_count = len(set(text.lower().split()) - 
                                 set(self.last_processed_text.lower().split()))
        if unique_words_count < 1:
            return False
        
        return True
    
    def change_settings(self):
        """Change application settings"""
        print("\n===== Settings =====")
        print("1. Change source language")
        print("2. Change target language")
        print("3. Back to main menu")
        
        choice = input("Select an option (1-3): ")
        
        if choice == '1':
            self.source_language = utils.display_language_options(for_target=False)
            print(f"Source language set to: {self.source_language or 'Auto-detect'}")
        elif choice == '2':
            self.target_language = utils.display_language_options(for_target=True)
            print(f"Target language set to: {self.target_language}")
    
    def run(self):
        """Run the main application loop"""
        print("Welcome to Learning Assistant App!")
        print("This app transcribes and translates audio in real-time.")
        
        while True:
            utils.display_menu()
            choice = input("Select an option (1-5): ")
            
            if choice == '1':
                self.process_realtime_audio()  # New real-time mode
            elif choice == '2':
                self.process_live_audio()      # Original recording mode
            elif choice == '3':
                self.process_recorded_audio()
            elif choice == '4':
                self.change_settings()
            elif choice == '5':
                print("Exiting application. Goodbye!")
                break
            else:
                print("Invalid choice. Please try again.")

if __name__ == "__main__":
    # Check if required API keys are set
    if not os.environ.get("OPENAI_API_KEY"):
        print("Error: OPENAI_API_KEY environment variable is not set.")
        print("Please set it before running the application.")
        exit(1)
        
    if not os.environ.get("AZURE_TRANSLATOR_KEY"):
        print("Error: AZURE_TRANSLATOR_KEY environment variable is not set.")
        print("Please set it before running the application.")
        exit(1)
        
    app = LearningAssistantApp()
    app.run()
