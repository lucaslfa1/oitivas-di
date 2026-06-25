// Sentinel - Sistema de Análise Forense de Sinistros Veiculares
// Transcrição: Azure Speech/Whisper | Laudo e Visão: Azure OpenAI (GPT-4o)

using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using SinistroAPI.Configuration;
using SinistroAPI.Data;
using SinistroAPI.Services;
using SinistroAPI.Interfaces;

var builder = WebApplication.CreateBuilder(args);



// --- CARREGAR CONFIGURAÇÕES LOCAIS (API KEYS) ---
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// --- CARREGAR CONFIGURAÇÕES EXTERNAS (Config-Driven Design) ---
builder.Configuration.AddJsonFile("Configuration/prompts.json", optional: false, reloadOnChange: true);
builder.Configuration.AddJsonFile("Configuration/text_corrections.json", optional: false, reloadOnChange: true);

var uploadLimits = builder.Configuration.GetSection("UploadLimits").Get<UploadLimitsOptions>() ?? new UploadLimitsOptions();
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

// --- LIMITE DE TAMANHO KESTREL (para arquivos grandes) ---
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = uploadLimits.MaxRequestBodyBytes;
});


// --- CONFIGURAÇÕES ---
builder.Services.Configure<FormOptions>(x => { 
    x.MultipartBodyLengthLimit = uploadLimits.MaxRequestBodyBytes;
});

builder.Services.Configure<PromptsOptions>(builder.Configuration);
builder.Services.Configure<TextCorrectionsWrapper>(builder.Configuration);
builder.Services.Configure<ResilienceOptions>(builder.Configuration.GetSection("Resilience"));
builder.Services.Configure<UploadLimitsOptions>(builder.Configuration.GetSection("UploadLimits"));

// --- CORS (Google Cloud Run) ---
builder.Services.AddCors(options =>
{
    options.AddPolicy("Desenvolvimento", policy => 
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
    
    // Produção: AllowAnyOrigin para piloto no Cloud Run
    // TODO: Restringir para domínio *.run.app específico após validação
    options.AddPolicy("Producao", policy => 
    {
        if (corsOrigins.Length == 0)
        {
            policy.SetIsOriginAllowed(_ => false);
            return;
        }

        policy.WithOrigins(corsOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// --- CONTROLLERS ---
builder.Services.AddControllers();

// --- SERVIÇOS DE IA (Azure OpenAI) ---
builder.Services.AddScoped<ImagemAnaliseService>();
builder.Services.AddScoped<VideoAnaliseService>();
builder.Services.AddScoped<DescricaoAnaliseService>();
builder.Services.AddHttpClient<OpenAITranscricaoService>();
builder.Services.AddScoped<ITranscricaoService, TranscricaoOrquestradorService>();
builder.Services.AddScoped<IDescricaoAnaliseService>(sp => sp.GetRequiredService<DescricaoAnaliseService>());

// --- SIGNALR ---
builder.Services.AddSignalR();

// --- PERFORMANCE & HEALTH ---
    // builder.Services.AddResponseCompression(options =>
    // {
    //    options.EnableForHttps = true;
    // });
builder.Services.AddHealthChecks();

// --- SWAGGER ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- PYTHON MEDIA PROCESSOR ---
builder.Services.Configure<MediaProcessorSettings>(builder.Configuration.GetSection(MediaProcessorSettings.SectionName));
builder.Services.AddHttpClient<MediaProcessorService>();
builder.Services.AddScoped<IMediaProcessorService>(sp => sp.GetRequiredService<MediaProcessorService>());

// --- AZURE SPEECH (WHISPER) ---
builder.Services.Configure<AzureSpeechSettings>(builder.Configuration.GetSection("AzureSpeech"));
builder.Services.AddHttpClient<AzureFastTranscricaoService>();
builder.Services.AddHttpClient<AzureWhisperService>();

// --- AZURE OPENAI (GPT-4o) ---
builder.Services.Configure<AzureOpenAISettings>(builder.Configuration.GetSection("AzureOpenAI"));
builder.Services.AddHttpClient<AzureOpenAIService>();

// --- AZURE TEXT ANALYTICS (SENTIMENT) ---
builder.Services.AddScoped<IAzureTextAnalyticsService, AzureTextAnalyticsService>();

// --- GOOGLE CLOUD SPEECH (TODO: implementar GoogleSpeechTranscricaoService) ---
builder.Services.Configure<GoogleCloudSpeechSettings>(builder.Configuration.GetSection(GoogleCloudSpeechSettings.SectionName));
// builder.Services.AddScoped<GoogleSpeechTranscricaoService>(); // Classe ainda não implementada

// --- DATABASE ---
var dbPath = Environment.GetEnvironmentVariable("DB_PATH") ?? "sinistros.db";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

var app = builder.Build();

// --- INICIALIZAÇÃO DO BANCO ---
try 
{
    using (var scope = app.Services.CreateScope()) 
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        
        // Garantir que o usuário Fabricio existe no banco
        if (!db.Users.Any(u => u.Username == "Fabricio"))
        {
            db.Users.Add(new SinistroAPI.Models.Entities.UserModel 
            { 
                Username = "Fabricio", 
                Password = "Fabricio@2026", 
                Role = "Admin" 
            });
            db.SaveChanges();
            Console.WriteLine("[DB] Usuário Fabricio criado com sucesso.");
        }

        Console.WriteLine($"[DB] Banco de dados inicializado em: {dbPath}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[CRITICAL] Falha ao inicializar o banco de dados: {ex.Message}");
    // Não encerra o processo para permitir que o Cloud Run suba e possamos debugar
}

// --- MIDDLEWARE ---
if (app.Environment.IsDevelopment())
{
    app.UseCors("Desenvolvimento");
}
else
{
    app.UseCors("Producao");
    // Cloud Run gerencia HTTPS via Load Balancer, não precisa de UseHttpsRedirection()
}

// --- EXCEPTION HANDLER GLOBAL (Cloud Run) ---
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        
        var exceptionHandlerPathFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var exception = exceptionHandlerPathFeature?.Error;
        var errorMessage = exception?.Message ?? "Ocorreu um erro interno no servidor.";
        // Escapar aspas para evitar JSON inválido se a mensagem contiver aspas
        errorMessage = errorMessage.Replace("\"", "'").Replace("\r", "").Replace("\n", " ");
        
        await context.Response.WriteAsync($"{{\"error\": \"[DIAGNOSTIC] {errorMessage}\"}}");
    });
});

// --- PERFORMANCE: COMPRESSÃO ---
// app.UseResponseCompression();

app.UseDefaultFiles();
app.UseStaticFiles();

// --- SWAGGER (Docs) ---
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// --- HEALTH CHECKS ---
app.MapHealthChecks("/health");

// --- CONTROLLERS & HUBS ---
// Log diagnostico
using (var scope = app.Services.CreateScope())
{
    var corrections = scope.ServiceProvider.GetService<Microsoft.Extensions.Options.IOptions<TextCorrectionsWrapper>>();
    app.Logger.LogWarning("STARTUP TEST - Correcoes carregadas: {Count}", corrections?.Value?.Corrections?.Count ?? 0);
}

app.MapControllers();
app.MapHub<SinistroAPI.Hubs.AnalysisHub>("/hubs/analysis");

app.Run();
