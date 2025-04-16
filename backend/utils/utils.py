import os
import time

# List of supported languages with their codes
LANGUAGE_CODES = {
    "English": "en",
    "Spanish": "es",
    "French": "fr",
    "German": "de",
    "Italian": "it",
    "Portuguese": "pt",
    "Chinese": "zh-Hans",
    "Japanese": "ja",
    "Korean": "ko",
    "Russian": "ru",
    "Arabic": "ar",
    "Hindi": "hi"
}

def display_menu():
    """Display the main menu options"""
    print("\n===== Learning Assistant App =====")
    print("1. Real-time translation (translate as you speak)")
    print("2. Record complete audio")
    print("3. Process recorded audio file")
    print("4. Change settings")
    print("5. Exit")
    print("================================")
    
def display_language_options(for_target=False):
    """Display available language options"""
    print("\n===== Language Options =====")
    for i, (name, code) in enumerate(LANGUAGE_CODES.items(), 1):
        print(f"{i}. {name} ({code})")
    print("============================")
    
    if for_target:
        message = "Select target language (number):"
    else:
        message = "Select source language (number) or 0 for auto-detection:"
    
    choice = input(message)
    try:
        choice = int(choice)
        if choice == 0 and not for_target:
            return None  # Auto-detect for source language
        if 1 <= choice <= len(LANGUAGE_CODES):
            return list(LANGUAGE_CODES.values())[choice-1]
    except ValueError:
        pass
    
    print("Invalid selection. Using default.")
    return None
    
def get_file_path():
    """Get audio file path from user"""
    path = input("Enter the path to the audio file: ")
    if os.path.isfile(path):
        return path
    print("File not found. Please check the path and try again.")
    return None

def display_live_caption(text, translated_text):
    """Display live captions in console with clear formatting"""
    os.system('cls' if os.name == 'nt' else 'clear')  # Clear screen
    print("\n===== REAL-TIME TRANSLATION =====")
    
    print("\n----- Original Text -----")
    # Format multiline text for better readability
    formatted_text = format_transcript_text(text)
    print(formatted_text)
    
    print("\n----- Translation -----")
    # Format multiline text for better readability
    formatted_translation = format_transcript_text(translated_text)
    print(formatted_translation)
    
    print("\nPress Ctrl+C to stop recording.")

def format_transcript_text(text):
    """Format transcript text for better readability"""
    # Remove excessive whitespace
    formatted = " ".join(text.split())
    
    # Add line breaks for better readability
    words = formatted.split()
    lines = []
    current_line = []
    
    for word in words:
        current_line.append(word)
        # Add a line break every ~10-15 words or after punctuation
        if len(current_line) > 12 and word[-1] in ".!?,:;":
            lines.append(" ".join(current_line))
            current_line = []
    
    # Add any remaining words
    if current_line:
        lines.append(" ".join(current_line))
    
    return "\n".join(lines)
