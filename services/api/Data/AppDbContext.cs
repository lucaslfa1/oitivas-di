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

        // Seed inicial de usuário administrador.
        // ATENÇÃO: senha apenas de exemplo (placeholder). NÃO versionar senhas reais.
        // TODO (Fase 0 do handoff): aplicar hash (PasswordHasher) e definir a senha via
        // configuração/variável de ambiente, nunca em texto puro no código.
        // Ver docs/PLANO_MELHORIA_HANDOFF.md.
        modelBuilder.Entity<UserModel>().HasData(
            new UserModel { Id = 1, Username = "admin", Password = "CHANGE_ME", Role = "Admin" }
        );
    }
}

