export interface TranslationResult {
  id: number;
  originalText: string;
  translatedText: string;
  sourceLanguage: string;
  targetLanguage: string;
  isInterim: boolean;
  timestamp: Date;
}
