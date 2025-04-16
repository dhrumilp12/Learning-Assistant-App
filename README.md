# Learning Assistant App

A console-based application that provides real-time speech translation and captioning for lectures or recorded audio.

## Features

- Live audio recording and processing
- Processing of pre-recorded audio files
- Speech-to-text transcription using OpenAI's Whisper API
- Text translation using Azure Translator
- Multiple language support

## Prerequisites

- Python 3.8 or higher
- OpenAI API key
- Azure Translator API key and region

## Setup Instructions

1. Clone this repository:
```bash
git clone https://github.com/yourusername/Learning-Assistant-App.git
cd Learning-Assistant-App
```

2. Install dependencies:
```bash
pip install -r backend/requirements.txt
```

3. Create a `.env` file in the backend directory with your API keys:
