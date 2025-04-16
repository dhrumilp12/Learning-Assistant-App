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



---
## Install FFmpeg and Add FFmpeg to System PATH

### Download and Extract FFmpeg Properly

1. **Download FFmpeg Again:**
   - Go to the [FFmpeg download page](https://ffmpeg.org/download.html).
   - Click on the link for Windows builds provided by Gyan or BtbN (these are popular and reliable sources). This will redirect you to their respective pages where you can download the build.
   - Choose a static build (which includes all necessary files in a single package) and download the zip file.

2. **Extract the FFmpeg Zip File:**
   - Once downloaded, right-click on the zip file and choose 'Extract All...' or use any preferred extraction tool like 7-Zip or WinRAR.
   - Choose a location where you want to extract the files. You can extract them directly to `C:\FFmpeg` to keep things organized.

3. **Verify the Contents:**
   - Navigate to the folder where you extracted the files.
   - You should see a `bin` folder inside this directory. Inside `bin`, there will be at least three files: `ffmpeg.exe`, `ffplay.exe`, and `ffprobe.exe`.

### Add FFmpeg to System PATH

If you've successfully located the `bin` folder now:

1. **Edit the PATH Environment Variable:**
   - Press `Windows key + R`, type `sysdm.cpl`, and press Enter.
   - Go to the 'Advanced' tab and click on 'Environment Variables'.
   - Under 'System Variables', scroll down to find the 'Path' variable and click on 'Edit'.
   - Click 'New' and add the full path to the `bin` folder, e.g., `C:\FFmpeg\bin`.
   - Click 'OK' to save your changes and close all remaining windows by clicking 'OK'.

2. **Verify FFmpeg Installation:**
   - Open a new command prompt or PowerShell window (make sure to open it after updating the PATH).
   - Type `ffmpeg -version` and press Enter. This command should now return the version of FFmpeg, confirming it's installed correctly and recognized by the system.
