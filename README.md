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
git clone https://github.com/dhrumilp12/Learning-Assistant-App.git
cd backend
```

2. **Create and activate a virtual environment (optional but recommended)**:
    ```
    python -m venv venv
    source venv/bin/activate  # On Windows use: .\venv\Scripts\Activate.ps1
    ```

3. **Configure environment variables**:
   - Copy the `.env.example` file to a new file named `.env`.
   - Update the `.env` file with your specific configurations.
   ```
   cp .env.example .env
   ```
4.  **Install the required dependencies**:
    ```
    pip install -r requirements.txt
    ```

5. **Run app.py**
   ```
   python app.py
   ```
