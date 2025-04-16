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
    print(text.strip())
    print("\n----- Translation -----")
    print(translated_text.strip())
    print("\nPress Ctrl+C to stop recording.")
