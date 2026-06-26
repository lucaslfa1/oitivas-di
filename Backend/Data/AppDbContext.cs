using Microsoft.EntityFrameworkCore;
using SinistroAPI.Models.Entities;

namespace SinistroAPI.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    
    public DbSet<AnaliseModel> Analises { get; set; }
    public DbSet<UserModel> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AnaliseModel>(entity =>
        {
            entity.ToTable("Analises");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Tipo).HasMaxLength(50);
            entity.Property(e => e.Arquivo).HasMaxLength(255);
        });

        modelBuilder.Entity<UserModel>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
        });

        // Seed Users
        modelBuilder.Entity<UserModel>().HasData(
            new UserModel { Id = 1, Username = "admin", Password = "admin", Role = "Admin" },
            new UserModel { Id = 2, Username = "Guerra", Password = "Guerr@2026", Role = "Coordenador" },
            new UserModel { Id = 3, Username = "Supervisor", Password = "Super@2026", Role = "Supervisor" },
            new UserModel { Id = 4, Username = "Analista", Password = "Analista@open2026", Role = "Analista" },
            new UserModel { Id = 5, Username = "Operador", Password = "Operador@2026open", Role = "Operador" },
            new UserModel { Id = 6, Username = "Daniele", Password = "Gestão2026", Role = "Coordenador" },
            new UserModel { Id = 7, Username = "Fabricio", Password = "Fabricio@2026", Role = "Admin" },
            new UserModel { Id = 8, Username = "teste", Password = "teste123", Role = "Admin" }
        );
    }
}

