using SinistroAPI.Models;
using System.Text.RegularExpressions;

namespace SinistroAPI.Services;

/// <summary>
/// Serviço compartilhado de detecção e pós-processamento de speakers.
/// Centraliza toda a lógica de heurísticas de oitivas (Operador BAS vs Motorista)
/// usada tanto pelo Azure Speech-to-Text quanto pelo Azure Whisper.
/// </summary>
public static class SpeakerDetectionService
{
    public const string SpeakerOperador = "Operador BAS";
    public const string SpeakerMotorista = "Motorista";

    // ==================== DETECÇÃO DE SPEAKER ====================

    /// <summary>
    /// Detecta o speaker de um segmento com base em heurísticas de conteúdo.
    /// </summary>
    public static string DetectarSpeaker(string textoNormalizado, string ultimoSpeaker, bool aguardandoRespostaMotorista)
    {
        if (EhPerguntaOuDirecionamentoOperador(textoNormalizado))
        {
            // Pergunta composta so por digitos geralmente e confirmacao de CPF/telefone do motorista.
            if (Regex.IsMatch(textoNormalizado, @"^\d+(?:\s+\d+)*\??$"))
            {
                return ultimoSpeaker;
            }

            return SpeakerOperador;
        }

        var scoreOperador = PontuarOperador(textoNormalizado);
        var scoreMotorista = PontuarMotorista(textoNormalizado);

        // Janela de resposta apos conducoes do operador.
        if (aguardandoRespostaMotorista && scoreOperador < 4)
        {
            if (scoreMotorista >= 1 || EhRespostaCurtaMotorista(textoNormalizado) || !EhPergunta(textoNormalizado))
            {
                return SpeakerMotorista;
            }
        }

        if (scoreOperador >= scoreMotorista + 2 && scoreOperador >= 3)
        {
            return SpeakerOperador;
        }

        if (scoreMotorista >= scoreOperador + 2 && scoreMotorista >= 2)
        {
            return SpeakerMotorista;
        }

        if (EhRespostaCurtaMotorista(textoNormalizado))
        {
            return SpeakerMotorista;
        }

        if (TemIndicadorOperadorForte(textoNormalizado) && !TemIndicadorMotoristaForte(textoNormalizado))
        {
            return SpeakerOperador;
        }

        if (TemIndicadorMotoristaForte(textoNormalizado) && !TemIndicadorOperadorForte(textoNormalizado))
        {
            return SpeakerMotorista;
        }

        // Sem evidencia forte, mantem o ultimo falante para evitar alternancia artificial.
        return ultimoSpeaker;
    }

    // ==================== SCORING ====================

    public static int PontuarOperador(string textoNormalizado)
    {
        var score = 0;

        if (textoNormalizado.StartsWith("alo") || textoNormalizado.StartsWith("boa tarde") ||
            textoNormalizado.StartsWith("boa noite") || textoNormalizado.StartsWith("bom dia")) score += 1;
        if (textoNormalizado.StartsWith("estou ligando") || textoNormalizado.StartsWith("aqui e") ||
            textoNormalizado.StartsWith("meu nome e") || textoNormalizado.Contains("sou da central") ||
            textoNormalizado.Contains("trabalho aqui")) score += 2;
        if (textoNormalizado.StartsWith("ok") || textoNormalizado.StartsWith("certo") ||
            textoNormalizado.StartsWith("perfeito") || textoNormalizado.StartsWith("entendi")) score += 1;
        if (textoNormalizado.StartsWith("pode") || textoNormalizado.StartsWith("consegue") ||
            textoNormalizado.StartsWith("me fala") || textoNormalizado.StartsWith("me conta") ||
            textoNormalizado.StartsWith("me diga")) score += 2;
        if (textoNormalizado.StartsWith("qual") || textoNormalizado.StartsWith("como") ||
            textoNormalizado.StartsWith("quando") || textoNormalizado.StartsWith("onde") ||
            textoNormalizado.StartsWith("confirma")) score += 2;
        if (textoNormalizado.StartsWith("informar") || textoNormalizado.StartsWith("vamos") ||
            textoNormalizado.StartsWith("agora a gente")) score += 2;
        if (textoNormalizado.Contains("vamos comecar") || textoNormalizado.Contains("pode comecar") ||
            textoNormalizado.Contains("seu relato") || textoNormalizado.Contains("as perguntas que eu fizer")) score += 2;
        if (textoNormalizado.Contains("o senhor") || textoNormalizado.Contains("a senhora")) score += 2;
        if (textoNormalizado.Contains("cpf") || textoNormalizado.Contains("placa") ||
            textoNormalizado.Contains("sinistro") || textoNormalizado.Contains("ocorrencia") ||
            textoNormalizado.Contains("roteiro")) score += 1;

        return score;
    }

    public static int PontuarMotorista(string textoNormalizado)
    {
        var score = 0;

        if (textoNormalizado.StartsWith("sim") || textoNormalizado.StartsWith("nao") ||
            textoNormalizado.StartsWith("isso") || textoNormalizado.StartsWith("exato")) score += 2;
        if (textoNormalizado.StartsWith("tudo bem") || textoNormalizado.StartsWith("gracas a deus") ||
            textoNormalizado.StartsWith("aham") || textoNormalizado.StartsWith("uhum")) score += 2;
        if (Regex.IsMatch(textoNormalizado, @"^\d+(?:\s+\d+)*\??$")) score += 2;
        if (textoNormalizado.StartsWith("eu ") || textoNormalizado.StartsWith("a gente") ||
            textoNormalizado.StartsWith("nos ") || textoNormalizado.StartsWith("meu ") ||
            textoNormalizado.StartsWith("minha ")) score += 2;
        if (textoNormalizado.Contains(" eu ") || textoNormalizado.Contains(" fui ") ||
            textoNormalizado.Contains(" estava ") || textoNormalizado.Contains(" tava ") ||
            textoNormalizado.Contains("aconteceu")) score += 1;
        if (textoNormalizado.StartsWith("foi") || textoNormalizado.StartsWith("e ") || textoNormalizado.StartsWith("era")) score += 1;
        if (textoNormalizado.Contains("fui abordado") || textoNormalizado.Contains("sai")) score += 2;

        return score;
    }

    // ==================== INDICADORES ====================

    public static bool EhPerguntaOuDirecionamentoOperador(string textoNormalizado)
    {
        if (EhPergunta(textoNormalizado))
        {
            return true;
        }

        return textoNormalizado.StartsWith("qual ") ||
               textoNormalizado.StartsWith("como ") ||
               textoNormalizado.StartsWith("quando ") ||
               textoNormalizado.StartsWith("onde ") ||
               textoNormalizado.StartsWith("por que ") ||
               textoNormalizado.StartsWith("pode ") ||
               textoNormalizado.StartsWith("consegue ") ||
               textoNormalizado.StartsWith("me fala") ||
               textoNormalizado.StartsWith("me conta") ||
               textoNormalizado.StartsWith("me diga") ||
               textoNormalizado.StartsWith("confirma ") ||
               textoNormalizado.Contains("o senhor") ||
               textoNormalizado.Contains("a senhora");
    }

    public static bool EhRespostaCurtaMotorista(string textoNormalizado)
    {
        if (Regex.IsMatch(textoNormalizado, @"^\d+(?:\s+\d+)*\??$"))
        {
            return true;
        }

        if (textoNormalizado.Length > 24)
        {
            return false;
        }

        return textoNormalizado.StartsWith("sim") ||
               textoNormalizado.StartsWith("nao") ||
               textoNormalizado.StartsWith("isso") ||
               textoNormalizado.StartsWith("exato") ||
               textoNormalizado.StartsWith("certo") ||
               textoNormalizado.StartsWith("aham") ||
               textoNormalizado.StartsWith("uhum") ||
               textoNormalizado.StartsWith("tudo bem");
    }

    public static bool EhPergunta(string textoNormalizado)
    {
        return textoNormalizado.TrimEnd().EndsWith("?");
    }

    public static bool TemIntroOperador(string textoNormalizado)
    {
        return textoNormalizado.StartsWith("aqui e") ||
               textoNormalizado.StartsWith("meu nome e") ||
               textoNormalizado.StartsWith("estou ligando") ||
               textoNormalizado.StartsWith("falo da") ||
               textoNormalizado.Contains("sou da central");
    }

    public static bool TemIndicadorOperadorForte(string textoNormalizado)
    {
        return textoNormalizado.StartsWith("estou ligando") ||
               textoNormalizado.StartsWith("aqui e") ||
               textoNormalizado.StartsWith("meu nome e") ||
               textoNormalizado.StartsWith("vamos ") ||
               textoNormalizado.StartsWith("agora ") ||
               textoNormalizado.Contains("sou da central") ||
               textoNormalizado.Contains("o senhor") ||
               textoNormalizado.Contains("a senhora");
    }

    public static bool TemIndicadorMotoristaForte(string textoNormalizado)
    {
        return textoNormalizado.StartsWith("eu estava") ||
               textoNormalizado.StartsWith("eu fui") ||
               textoNormalizado.StartsWith("eu tava") ||
               textoNormalizado.StartsWith("eu vi") ||
               textoNormalizado.StartsWith("eu sai") ||
               textoNormalizado.StartsWith("meu carro") ||
               textoNormalizado.StartsWith("meu veiculo") ||
               textoNormalizado.StartsWith("minha moto") ||
               textoNormalizado.StartsWith("a gente estava") ||
               textoNormalizado.StartsWith("a gente tava") ||
               textoNormalizado.Contains(" eu ") ||
               textoNormalizado.Contains(" fui ") ||
               textoNormalizado.Contains(" estava ") ||
               textoNormalizado.Contains("aconteceu");
    }

    public static bool EhTokenRuidoCurto(string token)
    {
        if (Regex.IsMatch(token, @"^\d{1,2}$"))
        {
            return true;
        }

        return token is "um" or "uh" or "hm" or "hmm" or "ah" or "ha" or "a";
    }

    // ==================== PÓS-PROCESSAMENTO DE SEGMENTOS ====================

    /// <summary>
    /// Rebalanceia speakers após perguntas do operador.
    /// Segmentos logo após uma pergunta tendem a ser respostas do motorista.
    /// </summary>
    public static List<SegmentoFormatado> RebalancearInterlocutoresPorTurno(List<SegmentoFormatado> segmentos)
    {
        if (segmentos.Count < 3)
        {
            return segmentos;
        }

        var rebalanceados = new List<SegmentoFormatado>(segmentos);

        for (var i = 0; i < rebalanceados.Count - 1; i++)
        {
            var atual = rebalanceados[i];
            if (atual.Speaker != SpeakerOperador || !EhPerguntaOuDirecionamentoOperador(atual.TextoNormalizado))
            {
                continue;
            }

            for (var j = i + 1; j < rebalanceados.Count; j++)
            {
                var candidato = rebalanceados[j];
                var delta = (candidato.Timestamp - atual.Timestamp).TotalSeconds;
                if (delta > 35d)
                {
                    break;
                }

                if (EhPerguntaOuDirecionamentoOperador(candidato.TextoNormalizado) &&
                    TemIndicadorOperadorForte(candidato.TextoNormalizado))
                {
                    break;
                }

                var deveVirarMotorista =
                    EhRespostaCurtaMotorista(candidato.TextoNormalizado) ||
                    TemIndicadorMotoristaForte(candidato.TextoNormalizado) ||
                    !EhPerguntaOuDirecionamentoOperador(candidato.TextoNormalizado);

                if (deveVirarMotorista && !TemIndicadorOperadorForte(candidato.TextoNormalizado))
                {
                    rebalanceados[j] = candidato with { Speaker = SpeakerMotorista };

                    if (candidato.TextoNormalizado.Length >= 30 || candidato.DuracaoSeconds >= 2.2d)
                    {
                        break;
                    }
                }
            }
        }

        return rebalanceados;
    }

    /// <summary>
    /// Suaviza trocas isoladas de speaker (sanduíche A-B-A onde B é curto).
    /// </summary>
    public static List<SegmentoFormatado> SuavizarTrocaIsoladaDeSpeaker(List<SegmentoFormatado> segmentos)
    {
        if (segmentos.Count < 3)
        {
            return segmentos;
        }

        var suavizados = new List<SegmentoFormatado>(segmentos);

        for (var i = 1; i < suavizados.Count - 1; i++)
        {
            var anterior = suavizados[i - 1];
            var atual = suavizados[i];
            var proximo = suavizados[i + 1];

            var trocaIsolada = anterior.Speaker == proximo.Speaker && atual.Speaker != anterior.Speaker;
            if (!trocaIsolada)
            {
                continue;
            }

            var atualEhCurto = atual.TextoNormalizado.Length <= 20 || atual.DuracaoSeconds <= 1.5d;
            var temEvidenciaForte = atual.Speaker == SpeakerMotorista
                ? TemIndicadorMotoristaForte(atual.TextoNormalizado) || EhRespostaCurtaMotorista(atual.TextoNormalizado)
                : TemIndicadorOperadorForte(atual.TextoNormalizado) || EhPerguntaOuDirecionamentoOperador(atual.TextoNormalizado);
            if (atualEhCurto && !temEvidenciaForte)
            {
                suavizados[i] = atual with { Speaker = anterior.Speaker };
            }
        }

        return suavizados;
    }

    /// <summary>
    /// Filtra runs repetitivos (tokens de ruído repetidos 6+ vezes).
    /// </summary>
    public static List<SegmentoFormatado> FiltrarRunsRepetitivos(List<SegmentoFormatado> segmentos)
    {
        if (segmentos.Count == 0)
        {
            return segmentos;
        }

        var filtrados = new List<SegmentoFormatado>(segmentos.Count);
        var i = 0;

        while (i < segmentos.Count)
        {
            var atual = segmentos[i].TextoNormalizado;
            var j = i + 1;

            while (j < segmentos.Count && segmentos[j].TextoNormalizado == atual)
            {
                j++;
            }

            var runLength = j - i;
            var descartarRun = (runLength >= 6 && EhTokenRuidoCurto(atual)) || (runLength >= 20 && atual.Length <= 12);
            var compactarRun = runLength >= 4 && !EhTokenRuidoCurto(atual) && !EhPergunta(atual);

            if (!descartarRun && !compactarRun)
            {
                for (var k = i; k < j; k++)
                {
                    filtrados.Add(segmentos[k]);
                }
            }
            else if (compactarRun)
            {
                filtrados.Add(segmentos[i]);
            }

            i = j;
        }

        return filtrados;
    }

    /// <summary>
    /// Compacta frase dominante (>45% dos segmentos) para no máximo 2 ocorrências.
    /// </summary>
    public static List<SegmentoFormatado> CompactarFraseDominante(List<SegmentoFormatado> segmentos)
    {
        if (segmentos.Count < 12)
        {
            return segmentos;
        }

        var fraseDominante = segmentos
            .GroupBy(s => s.TextoNormalizado)
            .Select(g => new { Texto = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .FirstOrDefault();

        if (fraseDominante is null)
        {
            return segmentos;
        }

        var minimoDominancia = (int)Math.Ceiling(segmentos.Count * 0.45);
        var ehDominante = fraseDominante.Count >= 8 && fraseDominante.Count >= minimoDominancia;
        if (!ehDominante || EhTokenRuidoCurto(fraseDominante.Texto) || EhPergunta(fraseDominante.Texto))
        {
            return segmentos;
        }

        var filtrados = new List<SegmentoFormatado>(segmentos.Count);
        var mantidosDaDominante = 0;

        foreach (var segmento in segmentos)
        {
            if (segmento.TextoNormalizado == fraseDominante.Texto)
            {
                if (mantidosDaDominante < 2)
                {
                    filtrados.Add(segmento);
                    mantidosDaDominante++;
                }

                continue;
            }

            filtrados.Add(segmento);
        }

        return filtrados;
    }

    /// <summary>
    /// Remove segmentos duplicados contíguos (mesma fala, mesmo speaker, dentro de 12s).
    /// </summary>
    public static List<SegmentoFormatado> RemoverDuplicatasContiguas(List<SegmentoFormatado> segmentos)
    {
        if (segmentos.Count < 2)
        {
            return segmentos;
        }

        var filtrados = new List<SegmentoFormatado>(segmentos.Count) { segmentos[0] };

        for (var i = 1; i < segmentos.Count; i++)
        {
            var atual = segmentos[i];
            var ultimo = filtrados[^1];

            var mesmaFala = atual.TextoNormalizado == ultimo.TextoNormalizado && atual.Speaker == ultimo.Speaker;
            // Fase 8: Reduzido de 12s para 0.2s. Uma pessoa pode repetir a mesma frase segundos depois
            // por nervosismo ("eu tava ali... eu tava ali"). Apenas removemos se for uma alucinação 
            // literal da API (gap quase zero).
            var muitoProximoNoTempo = Math.Abs((atual.Timestamp - ultimo.Timestamp).TotalSeconds) <= 0.2d;

            if (mesmaFala && muitoProximoNoTempo)
            {
                continue;
            }

            filtrados.Add(atual);
        }

        return filtrados;
    }

    /// <summary>
    /// Mescla segmentos consecutivos do mesmo speaker com gap pequeno.
    /// </summary>
    public static List<SegmentoFormatado> MesclarSegmentosConsecutivos(List<SegmentoFormatado> segmentos)
    {
        if (segmentos.Count < 2)
        {
            return segmentos;
        }

        var mesclados = new List<SegmentoFormatado>(segmentos.Count) { segmentos[0] };

        for (var i = 1; i < segmentos.Count; i++)
        {
            var atual = segmentos[i];
            var ultimo = mesclados[^1];

            if (!PodeMesclarSegmentos(ultimo, atual))
            {
                mesclados.Add(atual);
                continue;
            }

            var textoMesclado = UnirTextos(ultimo.Texto, atual.Texto);
            var inicio = ultimo.Timestamp.TotalSeconds;
            var fim = Math.Max(
                ultimo.Timestamp.TotalSeconds + ultimo.DuracaoSeconds,
                atual.Timestamp.TotalSeconds + atual.DuracaoSeconds);

            mesclados[^1] = ultimo with
            {
                Texto = textoMesclado,
                TextoNormalizado = NormalizarTexto(textoMesclado),
                DuracaoSeconds = Math.Max(0d, fim - inicio)
            };
        }

        return mesclados;
    }

    // ==================== UTILITÁRIOS ====================

    public static string NormalizarTexto(string texto)
    {
        return Regex.Replace(texto.ToLowerInvariant().Trim(), @"\s+", " ");
    }

    public static string FormatarTimestamp(TimeSpan timestamp)
    {
        var totalMinutes = (int)Math.Floor(timestamp.TotalMinutes);
        return $"{totalMinutes:00}:{timestamp.Seconds:00}";
    }

    public static string UnirTextos(string primeiro, string segundo)
    {
        if (string.IsNullOrWhiteSpace(primeiro))
        {
            return segundo.Trim();
        }

        if (string.IsNullOrWhiteSpace(segundo))
        {
            return primeiro.Trim();
        }

        var a = primeiro.TrimEnd();
        var b = segundo.TrimStart();

        if (a.EndsWith("-", StringComparison.Ordinal) || Regex.IsMatch(b, @"^[\.,;:\!\?]"))
        {
            return a + b;
        }

        return $"{a} {b}";
    }

    private static bool PodeMesclarSegmentos(SegmentoFormatado anterior, SegmentoFormatado atual)
    {
        if (anterior.Speaker != atual.Speaker)
        {
            return false;
        }

        var gap = atual.Timestamp.TotalSeconds - (anterior.Timestamp.TotalSeconds + anterior.DuracaoSeconds);
        if (gap > 3.0d)
        {
            return false;
        }

        var textoAnterior = anterior.Texto.TrimEnd();
        if (textoAnterior.EndsWith("?", StringComparison.Ordinal) ||
            textoAnterior.EndsWith("!", StringComparison.Ordinal))
        {
            return false;
        }

        if (textoAnterior.EndsWith(".", StringComparison.Ordinal) && gap > 1.3d)
        {
            return false;
        }

        if ((anterior.Texto.Length + atual.Texto.Length) > 380)
        {
            return false;
        }

        return true;
    }
}
