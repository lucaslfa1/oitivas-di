namespace SinistroAPI.Interfaces;

public interface IDescricaoAnaliseService
{
    bool IsConfigured { get; }
    Task<string> AnalisarTranscricaoOitiva(string transcricao, string duracao, string contextoUsuario = "", string tipoOperacao = "Viagem");
    Task<string> AuditarConformidade(string transcricao, string roteiroConformidade);
}
