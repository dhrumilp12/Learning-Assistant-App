using dotenv.net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Http.Features;
using SpeechTranslator.Hubs;
using SpeechTranslator.Services;

namespace SpeechTranslator
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Configure Kestrel for proper HTTP/HTTPS
            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                // Configure HTTP endpoint (disable HTTP/2 without TLS to avoid warnings)
                serverOptions.ListenLocalhost(5000, options => 
                {
                    options.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
                });
                
                // Configure HTTPS endpoint with HTTP/2 support
                serverOptions.ListenLocalhost(5001, options => 
                {
                    options.UseHttps();
                    options.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
                });
            });

            // Load .env variables
            DotEnv.Load(new DotEnvOptions(envFilePaths: [".env"]));

            // Add services to the container
            builder.Services.AddControllers();
            builder.Services.AddSignalR();

             // Configure file size limits for request body
            builder.Services.Configure<IISServerOptions>(options =>
            {
                options.MaxRequestBodySize = 200_000_000; // Approximately 200MB
            });
            
            builder.Services.Configure<KestrelServerOptions>(options =>
            {
                options.Limits.MaxRequestBodySize = 200_000_000; // Approximately 200MB
            });
            
            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = 200_000_000; // Approximately 200MB
            });

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy", builder =>
                {
                    builder
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .SetIsOriginAllowed(_ => true) // Allow any origin for testing
                        .AllowCredentials();
                });
            });

            // Configure Azure Speech Service
            string? speechApiKey = Environment.GetEnvironmentVariable("SPEECH_API_KEY");
            string? speechEndpoint = Environment.GetEnvironmentVariable("SPEECH_ENDPOINT");
            string? translatorApiKey = Environment.GetEnvironmentVariable("TRANSLATOR_API_KEY");
            string? translatorEndpoint = Environment.GetEnvironmentVariable("TRANSLATOR_ENDPOINT") ?? "https://api.cognitive.microsofttranslator.com/";
            string? translatorRegion = Environment.GetEnvironmentVariable("TRANSLATOR_REGION");
            string? visionApiKey = Environment.GetEnvironmentVariable("VISION_API_KEY");
            string? visionEndpoint = Environment.GetEnvironmentVariable("VISION_ENDPOINT");

            // Validate required configuration
            if (string.IsNullOrEmpty(speechApiKey) || string.IsNullOrEmpty(speechEndpoint))
            {
                throw new ArgumentException("Speech service configuration is missing. Please provide ApiKey and Endpoint in .env file.");
            }

            if (string.IsNullOrEmpty(translatorApiKey) || string.IsNullOrEmpty(translatorRegion))
            {
                throw new ArgumentException("Translator service configuration is missing. Please provide ApiKey and Region in .env file.");
            }

            if (string.IsNullOrEmpty(visionApiKey) || string.IsNullOrEmpty(visionEndpoint))
            {
                throw new ArgumentException("Computer Vision service configuration is missing. Please provide ApiKey and Endpoint in .env file.");
            }

            // Register services
            builder.Services.AddSingleton<SpeechToTextService>(sp => new SpeechToTextService(speechEndpoint!, speechApiKey!));
            builder.Services.AddSingleton<TranslationService>(sp => new TranslationService(translatorApiKey!, translatorEndpoint!, translatorRegion!));
            builder.Services.AddSingleton<VideoProcessingService>(sp => 
                new VideoProcessingService(
                    visionApiKey!, 
                    visionEndpoint!, 
                    sp.GetRequiredService<TranslationService>(),
                    sp.GetRequiredService<ILogger<VideoProcessingService>>()
                )
            );

            // Add logging
            builder.Logging.AddConsole();
            
            var app = builder.Build();

            // Configure the HTTP request pipeline
            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseCors("CorsPolicy");
            app.UseRouting();
            
            // Add a simple welcome page
            app.MapGet("/", () => "Speech Translator API is running. Use /api/speech endpoints for service operations.");
            
            // Map controller endpoints and SignalR hub
            app.MapControllers();
            app.MapHub<TranslationHub>("/translationHub");
            
            app.Run();
        }
    }
}