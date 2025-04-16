import requests
import uuid
from config import AZURE_TRANSLATOR_KEY, AZURE_TRANSLATOR_REGION

class Translator:
    def __init__(self):
        self.key = AZURE_TRANSLATOR_KEY
        self.region = AZURE_TRANSLATOR_REGION
        self.endpoint = "https://api.cognitive.microsofttranslator.com"
        
        if not self.key or not self.region:
            raise ValueError("Azure Translator key or region is missing. Please check your environment variables.")
            
    def translate_text(self, text, target_language, source_language=None):
        """
        Translate text using Azure Translator
        
        Parameters:
            text (str): Text to translate
            target_language (str): Target language code
            source_language (str, optional): Source language code
            
        Returns:
            str: Translated text
        """
        if not text:
            return ""
            
        # Construct request
        url = f"{self.endpoint}/translate"
        params = {
            'api-version': '3.0',
            'to': target_language
        }
        
        if source_language:
            params['from'] = source_language
            
        headers = {
            'Ocp-Apim-Subscription-Key': self.key,
            'Ocp-Apim-Subscription-Region': self.region,
            'Content-type': 'application/json',
            'X-ClientTraceId': str(uuid.uuid4())
        }
        
        body = [{'text': text}]
        
        try:
            response = requests.post(url, headers=headers, params=params, json=body)
            response.raise_for_status()  # Raise exception for HTTP errors
            
            translations = response.json()
            if translations and len(translations) > 0:
                return translations[0]['translations'][0]['text']
            return ""
        except Exception as e:
            print(f"Error translating text: {e}")
            return ""
