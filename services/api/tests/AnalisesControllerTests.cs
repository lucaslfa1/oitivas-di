using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SinistroAPI.Controllers;
using SinistroAPI.Data;
using SinistroAPI.Models.Entities;
using SinistroAPI.Models.Dtos;
using Xunit;

public class AnalisesControllerTests
{
    [Fact]
    public async Task Salvar_SetsDateAndReturnsId()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);
        var controller = new AnalisesController(db, NullLogger<AnalisesController>.Instance);

        var model = new AnaliseModel
        {
            Tipo = "Oitiva",
            Conteudo = "conteudo",
            Arquivo = "file.wav"
        };

        var result = await controller.Salvar(model);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<OperacaoResponse>(ok.Value);

        Assert.NotNull(response.Id);
        Assert.NotEqual(default, model.Data);
    }

    [Fact]
    public async Task Listar_LimitsTo50()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);

        for (var i = 0; i < 60; i++)
        {
            db.Analises.Add(new AnaliseModel
            {
                Tipo = "Teste",
                Conteudo = $"conteudo-{i}",
                Data = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();

        var controller = new AnalisesController(db, NullLogger<AnalisesController>.Instance);
        var result = await controller.Listar();

        var ok = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsType<List<AnaliseModel>>(ok.Value);

        Assert.Equal(50, items.Count);
    }
}
