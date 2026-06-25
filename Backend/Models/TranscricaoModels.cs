namespace SinistroAPI.Models;

/// <summary>
/// Segmento de transcrição formatado com speaker, timestamp e texto.
/// Compartilhado entre todos os serviços de transcrição Azure.
/// </summary>
public sealed record SegmentoFormatado(
    TimeSpan Timestamp,
    string Speaker,
    string Texto,
    string TextoNormalizado,
    double DuracaoSeconds);

/// <summary>
/// Frase bruta extraída do Azure Speech-to-Text (com speaker ID da diarização).
/// </summary>
public sealed record RawPhrase(
    TimeSpan Timestamp,
    double DuracaoSeconds,
    int SpeakerId,
    string Texto,
    string TextoNormalizado);

/// <summary>
/// Estatísticas de speaker para mapeamento por ID (diarização).
/// </summary>
public sealed record SpeakerStats(
    double PrimeiraFalaSegundos,
    int ScoreOperador,
    int ScoreInterlocutor,
    int Perguntas,
    int IntroOperador);
