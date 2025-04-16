import os
from dotenv import load_dotenv

load_dotenv()  # Load environment variables from .env file

# API Keys
OPENAI_API_KEY = os.getenv("OPENAI_API_KEY")
AZURE_SPEECH_KEY = os.getenv("AZURE_SPEECH_KEY")
AZURE_SPEECH_REGION = os.getenv("AZURE_SPEECH_REGION")
AZURE_TRANSLATOR_KEY = os.getenv("AZURE_TRANSLATOR_KEY")
AZURE_TRANSLATOR_REGION = os.getenv("AZURE_TRANSLATOR_REGION")

# Default settings
DEFAULT_LANGUAGE = "en"  # English as default source language
DEFAULT_TARGET_LANGUAGE = "es"  # Spanish as default target language
DEFAULT_SAMPLE_RATE = 44100  # Audio sample rate
DEFAULT_CHANNELS = 1  # Mono audio
