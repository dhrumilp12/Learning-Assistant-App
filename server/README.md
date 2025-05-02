# AI Agent - Speech & Video Translator

## Description
The AI Agent - Speech & Video Translator is a real-time translation application. It leverages Microsoft Cognitive Services for speech recognition, text detection in videos, and translation, enabling users to transcribe spoken words and visual text, then translate them into a target language. Additionally, the agent provides real-time audio feedback by speaking the translated text and overlays translated text onto video content.

## Features
- **Real-Time Speech Recognition**: Converts spoken words into text using Microsoft Cognitive Services.
- **Real-Time Translation**: Translates recognized text into a target language.
- **Video Content Translation**: Detects and translates text in video frames using OCR technology.
- **Visual Text Overlay**: Replaces original text in videos with translated text.
- **Audio Feedback**: Speaks the translated text back to the user in real-time.
- **Multi-Language Support**: Supports multiple source and target languages.
- **Web Interface**: Access translation services through a modern web interface.
- **Real-time Updates**: View translations as they happen using SignalR technology.

## Setup Instructions

### Prerequisites
1. Install [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).
2. Install [FFmpeg](https://ffmpeg.org/) (required for audio extraction from video files).
3. Set up Azure Cognitive Services:
   - Create a Speech resource in the Azure portal.
   - Create a Translator resource in the Azure portal.
   - Create a Computer Vision resource in the Azure portal.

### Environment Variables
Create a `.env` file in the root directory of the project with the following variables:
```
SPEECH_API_KEY=<Your Azure Speech API Key>
SPEECH_REGION=<Your Azure Speech Region>
SPEECH_ENDPOINT=<Your Azure Speech Endpoint>
TRANSLATOR_API_KEY=<Your Azure Translator API Key>
TRANSLATOR_REGION=<Your Azure Translator Region>
TRANSLATOR_ENDPOINT=<Your Azure Translator Endpoint or https://api.cognitive.microsofttranslator.com/>
VISION_API_KEY=<Your Azure Computer Vision API Key>
VISION_ENDPOINT=<Your Azure Computer Vision Endpoint>
```

### Installation
1. Clone the repository:
   ```bash
   git clone https://github.com/Georgia-Southwestern-State-Univeristy/capstone-project-study-buddy.git
   cd AI-agent-SpeechTranslator
   ```
2. Restore NuGet packages:
   ```bash
   dotnet restore
   ```

## Usage
1. Run the application:
   ```bash
   dotnet run
   ```
2. The application will start on:
   - http://localhost:5000 (HTTP)
   - https://localhost:5001 (HTTPS)
   
3. Access the web interface in your browser and:
   - Select source and target languages
   - Start the translation session
   - Speak into your microphone or upload a video
   - View real-time translations
   - Stop the session when finished

4. API endpoints:
   - `GET /api/speech/status` - Check if the service is running
   - `POST /api/speech/start` - Start a translation session
   - `POST /api/speech/stop` - Stop the current translation session
   - `POST /api/video/translate` - Translate text in a video

## Project Structure
- **Program.cs**: Entry point of the application, configures services and middleware.
- **Controllers/**: API endpoints:
  - `SpeechController.cs`: Handles speech translation requests.
  - `VideoController.cs`: Handles video translation requests.
- **Services/**: Contains the core services:
  - `SpeechToTextService.cs`: Handles speech recognition.
  - `TranslationService.cs`: Handles text translation.
  - `VideoProcessingService.cs`: Handles video frame processing and OCR.
- **Hubs/**: SignalR hubs for real-time communication:
  - `TranslationHub.cs`: Manages real-time translation updates.

## Dependencies
- [Microsoft.CognitiveServices.Speech](https://www.nuget.org/packages/Microsoft.CognitiveServices.Speech/)
- [Azure.AI.Translation.Text](https://www.nuget.org/packages/Azure.AI.Translation.Text/)
- [Azure.AI.FormRecognizer](https://www.nuget.org/packages/Azure.AI.FormRecognizer/) (for OCR)
- [dotenv.net](https://www.nuget.org/packages/dotenv.net/)
- [Microsoft.AspNetCore.SignalR](https://www.nuget.org/packages/Microsoft.AspNetCore.SignalR/)

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

## Contributing
Contributions are welcome! Please feel free to submit a Pull Request.