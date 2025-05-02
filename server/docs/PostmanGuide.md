# Testing Speech & Video Translator API with Postman

This guide provides instructions for testing the Speech & Video Translator API endpoints using Postman.

## Setting Up Postman

1. **Download and Install Postman**:
   - Download Postman from [https://www.postman.com/downloads/](https://www.postman.com/downloads/)
   - Install and launch the application

2. **Create a New Collection**:
   - Click on "Collections" tab in the sidebar
   - Click "+" to create a new collection
   - Name it "Speech & Video Translator API"

3. **Set Up Environment Variables** (optional but recommended):
   - Click "Environments" in the sidebar
   - Click "+" to create a new environment
   - Name it "Speech Translator Local"
   - Add the following variables:
     - `baseUrl`: `http://localhost:5000`
     - `httpsUrl`: `https://localhost:5001`

## API Endpoints

### 1. Check API Status

Tests if the API is running correctly.

- **Request Type**: GET
- **URL**: `{{baseUrl}}/api/speech/status`
- **Headers**: None required
- **Response**: JSON with status information

### 2. Start Speech Translation

Starts a real-time speech translation session.

- **Request Type**: POST
- **URL**: `{{baseUrl}}/api/speech/start`
- **Headers**: 
  - `Content-Type: application/json`
- **Body** (raw JSON):
  ```json
  {
    "sourceLanguage": "en",
    "targetLanguage": "es"
  }
  ```
- **Response**: JSON confirmation

### 3. Stop Speech Translation

Stops the current speech translation session.

- **Request Type**: POST
- **URL**: `{{baseUrl}}/api/speech/stop`
- **Headers**: None required
- **Response**: JSON with translation results

### 4. Translate Video

Uploads and translates text in a video file.

- **Request Type**: POST
- **URL**: `{{baseUrl}}/api/speech/translate-video`
- **Headers**: None required (Postman will set the correct Content-Type for form data)
- **Body** (form-data):
  - Key: `videoFile`, Type: File, Value: *Select video file*
  - Key: `sourceLanguage`, Type: Text, Value: `en`
  - Key: `targetLanguage`, Type: Text, Value: `es`
- **Response**: JSON confirmation

### 5. Stop Video Translation

Stops the current video translation process.

- **Request Type**: POST
- **URL**: `{{baseUrl}}/api/speech/stop-video`
- **Headers**: None required
- **Response**: JSON confirmation

## Testing the SignalR Connection

For real-time updates via SignalR, you'll need a different approach since Postman doesn't support WebSockets natively.

You can use a simple HTML page with the SignalR client to test:

```html
<!DOCTYPE html>
<html>
<head>
    <title>SignalR Test</title>
    <script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/7.0.0/signalr.min.js"></script>
</head>
<body>
    <div id="messages"></div>
    <script>
        const connection = new signalR.HubConnectionBuilder()
            .withUrl('http://localhost:5000/translationHub')
            .build();
        
        connection.on('ReceiveTranslation', (originalText, translatedText) => {
            const messagesDiv = document.getElementById('messages');
            messagesDiv.innerHTML += `<p>Original: ${originalText}<br>Translation: ${translatedText}</p>`;
        });
        
        connection.on('ReceiveVideoFrame', (frameBase64) => {
            console.log('Received video frame');
            // You could display the frame here
        });
        
        connection.start()
            .then(() => console.log('Connected to SignalR hub'))
            .catch(err => console.error('Error connecting to SignalR hub:', err));
    </script>
</body>
</html>
```

## Troubleshooting

### CORS Issues
If you encounter CORS errors when testing from Postman, ensure:
1. Your API's CORS policy is correctly configured
2. You're using the correct URL (http vs https)

### File Upload Issues
For video translation:
1. Ensure the file is not too large (check your server configuration for max file size)
2. Verify the file format is supported

### SignalR Connection Issues
If the SignalR connection fails:
1. Check that the hub URL is correct
2. Verify CORS is configured to allow WebSocket connections
3. Ensure the client is using the correct SignalR client version

## Example Testing Sequence

1. Check API Status to verify the service is running
2. Start a translation session
3. (Manually speak into microphone if testing speech)
4. Stop the translation session and verify results
5. Upload a test video for translation
6. Monitor real-time updates via SignalR
7. Stop video translation when complete

## Exporting the Collection

You can export the collection to share with others:
1. Click on the "..." next to your collection name
2. Select "Export"
3. Choose "Collection v2.1" format
4. Save the JSON file
