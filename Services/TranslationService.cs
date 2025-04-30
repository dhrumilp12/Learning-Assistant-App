using Azure;
using Azure.AI.Translation.Text;
using Azure.Core;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SpeechTranslator.Services
{
    public class TranslationService
    {
        private readonly TextTranslationClient _client;

        public TranslationService(string translatorKey, string translatorEndpoint, string translatorRegion)
        {
            _client = new TextTranslationClient(new AzureKeyCredential(translatorKey), new Uri(translatorEndpoint), translatorRegion);
        }

        public async Task<string> TranslateTextAsync(string sourceLang, string targetLanguage, string text)
        {
            if (sourceLang.Equals(targetLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }

            var response = await _client.TranslateAsync([targetLanguage], [text], sourceLanguage: sourceLang);
            return response.Value[0].Translations[0].Text;
        }

        public async IAsyncEnumerable<string> TranslateTextStreamAsync(string sourceLang, string targetLanguage, IAsyncEnumerable<string> textStream)
        {
            await foreach (var text in textStream)
            {
                if (sourceLang.Equals(targetLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    yield return text;
                }
                else
                {
                    var response = await _client.TranslateAsync([targetLanguage], [text], sourceLanguage: sourceLang);
                    yield return response.Value[0].Translations[0].Text;
                }
            }
        }
    }
}