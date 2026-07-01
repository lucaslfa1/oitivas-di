using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SinistroAPI.Configuration;
using SinistroAPI.Controllers;
using SinistroAPI.Interfaces;
using SinistroAPI.Models.Dtos;
using SinistroAPI.Services;
using Xunit;

public class TranscricaoControllerTests
{
    [Fact]
    public async Task Transcrever_ReturnsBadRequest_WhenNoFile()
    {
        var controller = CreateController(new FakeTranscricaoService(), new UploadLimitsOptions());
        var result = await controller.Transcrever(new UploadDto());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Transcrever_ReturnsBadRequest_WhenWrongContentType()
    {
        var controller = CreateController(new FakeTranscricaoService(), new UploadLimitsOptions());
        var dto = new UploadDto
        {
            Arquivo = CreateFormFile(new byte[] { 1 }, "text/plain")
        };

        var result = await controller.Transcrever(dto);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Transcrever_ReturnsBadRequest_WhenNotConfigured()
    {
        var controller = CreateController(new FakeTranscricaoService { IsConfigured = false }, new UploadLimitsOptions());
        var dto = new UploadDto
        {
            Arquivo = CreateFormFile(new byte[] { 1 }, "audio/wav")
        };

        var result = await controller.Transcrever(dto);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Transcrever_Returns413_WhenAudioTooLarge()
    {
        var limits = new UploadLimitsOptions { MaxAudioUploadMB = 0 };
        var controller = CreateController(new FakeTranscricaoService(), limits);
        var dto = new UploadDto
        {
            Arquivo = CreateFormFile(new byte[] { 1 }, "audio/wav")
        };

        var result = await controller.Transcrever(dto);
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(413, objectResult.StatusCode);
    }

    private static TranscricaoController CreateController(ITranscricaoService transcricaoService, UploadLimitsOptions limits)
    {
        var controller = new TranscricaoController(
            transcricaoService,
            new FakeDescricaoAnaliseService(),
            new FakeMediaProcessorService(),
            new FakeAzureTextAnalyticsService(),
            Options.Create(limits),
            NullLogger<TranscricaoController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static IFormFile CreateFormFile(byte[] content, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", "file.bin")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private sealed class FakeTranscricaoService : ITranscricaoService
    {
        public bool IsConfigured { get; set; } = true;

        public Task<string> TranscreverAudio(byte[] audioBytes, string mimeType, string? connectionId = null)
        {
            return Task.FromResult("ok");
        }

        public Task<Dictionary<string, string>> ExtrairDadosOitiva(string transcricao)
        {
            return Task.FromResult(new Dictionary<string, string>());
        }
    }

    private sealed class FakeDescricaoAnaliseService : IDescricaoAnaliseService
    {
        public bool IsConfigured => true;

        public Task<string> AnalisarTranscricaoOitiva(string transcricao, string duracao, string contextoUsuario = "", string tipoOperacao = "Viagem")
        {
            return Task.FromResult("ok");
        }

        public Task<string> AuditarConformidade(string transcricao, string roteiroConformidade)
        {
            return Task.FromResult("ok");
        }
    }

    private sealed class FakeMediaProcessorService : IMediaProcessorService
    {
        public bool IsEnabled => false;

        public Task<SentimentResult?> AnalyzeSentimentAsync(byte[] audioBytes, string mimeType)
        {
            return Task.FromResult<SentimentResult?>(null);
        }

        public Task<ProcessedAudioResult?> MergeAudiosAsync(List<byte[]> audioFiles, string mimeType)
        {
            return Task.FromResult<ProcessedAudioResult?>(null);
        }

        public Task<string?> AnnotateImageAsync(string imageBase64, List<ImageAnnotation> annotations)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class FakeAzureTextAnalyticsService : IAzureTextAnalyticsService
    {
        public bool IsConfigured => false;

        public Task<SentimentResult?> AnalyzeSentimentAsync(string transcriptText)
        {
            return Task.FromResult<SentimentResult?>(null);
        }
    }
}
