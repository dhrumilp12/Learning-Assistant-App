import requests
import uuid
import json
from config import AZURE_TRANSLATOR_KEY, AZURE_TRANSLATOR_REGION

class Translator:
    def __init__(self):
        self.subscription_key = AZURE_TRANSLATOR_KEY
        self.region = AZURE_TRANSLATOR_REGION
        
        if not self.subscription_key or not self.region:
            raise ValueError(
                "Azure Translator API key or region not found. "
                "Please set the AZURE_TRANSLATOR_KEY and AZURE_TRANSLATOR_REGION environment variables."
            )
        
        self.endpoint = 'https://api.cognitive.microsofttranslator.com'
        self.path = '/translate'
        self.constructed_url = self.endpoint + self.path
    
    def translate_text(self, text, target_language, source_language=None):
        """
        Translate text using Azure Translator API
        
        Args:
            text (str): The text to translate
            target_language (str): Target language code (e.g., 'es' for Spanish)
            source_language (str, optional): Source language code
            
        Returns:
            str: Translated text
        """
        if not text:
            return ""
        
        params = {
            'api-version': '3.0',
            'to': target_language
        }
        
        if source_language:
            params['from'] = source_language
        
        headers = {
            'Ocp-Apim-Subscription-Key': self.subscription_key,
            'Ocp-Apim-Subscription-Region': self.region,
            'Content-type': 'application/json',
            'X-ClientTraceId': str(uuid.uuid4())
        }
        
        body = [{
            'text': text
        }]
        
        try:
            # Call the Azure Translator API
            print(f"Translating to {target_language}...")
            response = requests.post(self.constructed_url, params=params, headers=headers, json=body)
            response.raise_for_status()
            
            translation_result = response.json()
            translated_text = translation_result[0]['translations'][0]['text']
            
            return translated_text
        except Exception as e:
            print(f"Error translating text: {e}")
            return text  # Return original text if translation fails
