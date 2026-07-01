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
    /// Detecta o speaker de um segmento com base em heurísticas de conteúdo (Operador BAS vs Motorista).
    /// </summary>
    /// <remarks>
    /// COMO funciona (pipeline de decisão, avaliado em ordem de prioridade decrescente):
    /// 1. Se o texto é claramente uma pergunta ou um direcionamento do operador
    ///    (<see cref="EhPerguntaOuDirecionamentoOperador"/>), atribui ao Operador — EXCETO quando
    ///    o texto é composto apenas por dígitos (regex <c>^\d+(?:\s+\d+)*\??$</c>): nesse caso é quase
    ///    sempre o motorista ditando/confirmando CPF, telefone ou placa, então mantém o último falante.
    /// 2. Calcula dois scores independentes via <see cref="PontuarOperador"/> e <see cref="PontuarMotorista"/>.
    /// 3. Janela de resposta: se estamos logo após uma condução do operador
    ///    (<paramref name="aguardandoRespostaMotorista"/>) e o score do operador é fraco (&lt; 4),
    ///    o segmento é tratado como resposta do Motorista quando houver qualquer pista de motorista
    ///    (score &gt;= 1), for resposta curta, ou simplesmente não for uma pergunta. O limiar 4 é alto
    ///    de propósito: só um sinal MUITO forte de operador (ex.: nova pergunta + tratamento "o senhor")
    ///    impede que a janela atribua a fala ao motorista.
    /// 4. Decisão por margem: exige vantagem de pelo menos 2 pontos sobre o oponente E um piso absoluto
    ///    para evitar decidir no ruído. Operador precisa de margem &gt;= 2 e score &gt;= 3 (piso mais alto
    ///    porque falas de operador costumam acumular pontos com saudação + pergunta + tratamento formal).
    ///    Motorista precisa de margem &gt;= 2 e score &gt;= 2 (piso menor porque respostas do motorista
    ///    são curtas e marcam poucos pontos).
    /// 5. Desempate: resposta curta típica de motorista → Motorista; indicador forte exclusivo de um
    ///    lado → esse lado.
    /// 6. Fallback: sem evidência forte, mantém <paramref name="ultimoSpeaker"/> para não criar
    ///    alternância artificial de interlocutores em falas neutras.
    /// </remarks>
    /// <param name="textoNormalizado">Texto do segmento já normalizado (minúsculas, sem acentos, espaços colapsados).</param>
    /// <param name="ultimoSpeaker">Speaker atribuído ao segmento imediatamente anterior; usado como fallback e em confirmações numéricas.</param>
    /// <param name="aguardandoRespostaMotorista">Indica que o segmento anterior foi pergunta/condução do operador, abrindo uma janela de resposta para o motorista.</param>
    /// <returns><see cref="SpeakerOperador"/> ou <see cref="SpeakerMotorista"/>.</returns>
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

    /// <summary>
    /// Pontua o quanto um texto "parece" fala do Operador BAS (entrevistador).
    /// </summary>
    /// <remarks>
    /// COMO funciona: soma pesos por padrões léxicos típicos de quem conduz a oitiva. Quanto mais
    /// específico/forte o sinal, maior o peso (+1 fraco, +2 forte):
    /// - +1: saudações de abertura ("alo", "bom dia", "boa tarde/noite") — comuns mas também o
    ///   motorista pode dizer, por isso peso fraco.
    /// - +2: auto-apresentação institucional ("estou ligando", "aqui e", "meu nome e",
    ///   "sou da central", "trabalho aqui") — quase exclusivo do operador.
    /// - +1: confirmações de continuidade ("ok", "certo", "perfeito", "entendi") — o operador
    ///   reconhece a resposta antes da próxima pergunta; peso fraco pois o motorista também usa.
    /// - +2: imperativos de solicitação ("pode", "consegue", "me fala/conta/diga") — direcionam o motorista.
    /// - +2: pronomes interrogativos no início ("qual", "como", "quando", "onde", "confirma") — perguntas diretas.
    /// - +2: verbos de condução do roteiro ("informar", "vamos", "agora a gente").
    /// - +2: marcadores de início de relato ("vamos/pode comecar", "seu relato", "as perguntas que eu fizer").
    /// - +2: tratamento formal ("o senhor", "a senhora") — o operador trata o motorista por pronome de respeito.
    /// - +1: termos do domínio de sinistro ("cpf", "placa", "sinistro", "ocorrencia", "roteiro") — peso fraco
    ///   porque o motorista também pode citá-los ao responder.
    /// Os pesos +2 marcam sinais praticamente inequívocos do operador; os +1 reforçam sem decidir sozinhos.
    /// Os limiares que consomem esse score estão em <see cref="DetectarSpeaker"/>.
    /// </remarks>
    /// <param name="textoNormalizado">Texto do segmento normalizado.</param>
    /// <returns>Soma dos pesos (0 = nenhuma evidência de operador).</returns>
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

    /// <summary>
    /// Pontua o quanto um texto "parece" fala do Motorista (entrevistado).
    /// </summary>
    /// <remarks>
    /// COMO funciona: soma pesos por padrões léxicos típicos de quem responde a oitiva (+1 fraco, +2 forte):
    /// - +2: respostas afirmativas/negativas no início ("sim", "nao", "isso", "exato") — reação direta a pergunta.
    /// - +2: confirmações coloquiais ("tudo bem", "gracas a deus", "aham", "uhum") — interjeições de quem responde.
    /// - +2: texto só de dígitos (regex <c>^\d+(?:\s+\d+)*\??$</c>) — motorista ditando CPF/telefone/placa.
    /// - +2: sujeito em primeira pessoa no início ("eu", "a gente", "nos", "meu", "minha") — narração própria.
    /// - +1: primeira pessoa / passado no meio da frase (" eu ", " fui ", " estava ", " tava ", "aconteceu")
    ///   — pista de relato, peso fraco por aparecer também em falas mistas.
    /// - +1: início narrativo ("foi", "e ", "era") — começo de relato de acontecimento.
    /// - +2: expressões típicas do relato de sinistro ("fui abordado", "sai").
    /// Os pisos de decisão para motorista são deliberadamente menores que os do operador
    /// (ver <see cref="DetectarSpeaker"/>), porque respostas do motorista são curtas e acumulam menos pontos.
    /// </remarks>
    /// <param name="textoNormalizado">Texto do segmento normalizado.</param>
    /// <returns>Soma dos pesos (0 = nenhuma evidência de motorista).</returns>
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

    /// <summary>
    /// Indica se o texto é uma pergunta ou um direcionamento/comando típico do operador.
    /// </summary>
    /// <remarks>
    /// COMO funciona: retorna verdadeiro se (a) o texto termina em "?" (<see cref="EhPergunta"/>), OU
    /// (b) começa com um pronome interrogativo ("qual", "como", "quando", "onde", "por que"), um imperativo
    /// de solicitação ("pode", "consegue", "me fala/conta/diga", "confirma"), ou contém o tratamento formal
    /// ("o senhor"/"a senhora"). Cobre os casos em que a transcrição não pontuou o "?" mas a fala ainda é
    /// claramente uma condução do operador. É o gatilho de prioridade máxima em <see cref="DetectarSpeaker"/>
    /// e abre a janela de resposta do motorista em <see cref="RebalancearInterlocutoresPorTurno"/>.
    /// </remarks>
    /// <param name="textoNormalizado">Texto do segmento normalizado.</param>
    /// <returns><c>true</c> se for pergunta ou direcionamento do operador.</returns>
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

    /// <summary>
    /// Indica se o texto é uma resposta curta característica do motorista.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    /// 1. Se o texto é só de dígitos (regex <c>^\d+(?:\s+\d+)*\??$</c>) é resposta de motorista
    ///    (ex.: ditando CPF/telefone), independente do tamanho.
    /// 2. Caso contrário, exige texto curto: mais de 24 caracteres já não conta como "resposta curta"
    ///    (o limiar 24 cobre confirmações como "tudo bem, gracas a deus" sem capturar frases longas).
    /// 3. Dentro desse limite, retorna verdadeiro para confirmações/afirmações típicas no início
    ///    ("sim", "nao", "isso", "exato", "certo", "aham", "uhum", "tudo bem").
    /// Usada como desempate em <see cref="DetectarSpeaker"/> e no pós-processamento de turnos.
    /// </remarks>
    /// <param name="textoNormalizado">Texto do segmento normalizado.</param>
    /// <returns><c>true</c> se for uma resposta curta típica de motorista.</returns>
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

    /// <summary>
    /// Indica se o texto termina com ponto de interrogação (é uma pergunta explícita).
    /// </summary>
    /// <param name="textoNormalizado">Texto do segmento normalizado.</param>
    /// <returns><c>true</c> se o texto, ignorando espaços finais, terminar em "?".</returns>
    public static bool EhPergunta(string textoNormalizado)
    {
        return textoNormalizado.TrimEnd().EndsWith("?");
    }

    /// <summary>
    /// Indica se o texto contém uma frase de auto-apresentação do operador.
    /// </summary>
    /// <remarks>
    /// COMO funciona: casa prefixos/trechos de apresentação institucional ("aqui e", "meu nome e",
    /// "estou ligando", "falo da", "sou da central"), sinais quase exclusivos de quem inicia a ligação.
    /// </remarks>
    /// <param name="textoNormalizado">Texto do segmento normalizado.</param>
    /// <returns><c>true</c> se houver intro de operador.</returns>
    public static bool TemIntroOperador(string textoNormalizado)
    {
        return textoNormalizado.StartsWith("aqui e") ||
               textoNormalizado.StartsWith("meu nome e") ||
               textoNormalizado.StartsWith("estou ligando") ||
               textoNormalizado.StartsWith("falo da") ||
               textoNormalizado.Contains("sou da central");
    }

    /// <summary>
    /// Indica presença de um sinal forte e quase inequívoco de operador.
    /// </summary>
    /// <remarks>
    /// COMO funciona: casa auto-apresentação ("estou ligando", "aqui e", "meu nome e", "sou da central"),
    /// verbos de condução do roteiro no início ("vamos ", "agora ") ou o tratamento formal
    /// ("o senhor"/"a senhora"). Usado como desempate em <see cref="DetectarSpeaker"/> e como barreira que
    /// impede reclassificar um segmento como motorista no rebalanceamento e na suavização de turnos.
    /// </remarks>
    /// <param name="textoNormalizado">Texto do segmento normalizado.</param>
    /// <returns><c>true</c> se houver indicador forte de operador.</returns>
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

    /// <summary>
    /// Indica presença de um sinal forte de motorista (narração em primeira pessoa do evento).
    /// </summary>
    /// <remarks>
    /// COMO funciona: casa relatos em primeira pessoa no início ("eu estava/fui/tava/vi/sai",
    /// "meu carro/veiculo", "minha moto", "a gente estava/tava") ou pistas de narração no meio da frase
    /// (" eu ", " fui ", " estava ", "aconteceu"). Espelha o desempate do <see cref="TemIndicadorOperadorForte"/>
    /// para o lado do motorista em <see cref="DetectarSpeaker"/> e no pós-processamento.
    /// </remarks>
    /// <param name="textoNormalizado">Texto do segmento normalizado.</param>
    /// <returns><c>true</c> se houver indicador forte de motorista.</returns>
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

    /// <summary>
    /// Indica se um token é ruído curto (número de 1-2 dígitos ou interjeição de hesitação).
    /// </summary>
    /// <remarks>
    /// COMO funciona: considera ruído um número de 1 ou 2 dígitos (regex <c>^\d{1,2}$</c>, capturando
    /// contagens/hesitações soltas) ou uma das interjeições de preenchimento ("um", "uh", "hm", "hmm",
    /// "ah", "ha", "a"). Usado por <see cref="FiltrarRunsRepetitivos"/> e <see cref="CompactarFraseDominante"/>
    /// para decidir quando descartar repetições que são alucinação/ruído da transcrição.
    /// </remarks>
    /// <param name="token">Token de texto a avaliar.</param>
    /// <returns><c>true</c> se o token for ruído curto.</returns>
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
    /// <remarks>
    /// COMO funciona:
    /// 1. Ignora listas com menos de 3 segmentos (sem contexto suficiente de turnos).
    /// 2. Para cada segmento que seja do Operador E pergunta/direcionamento
    ///    (<see cref="EhPerguntaOuDirecionamentoOperador"/>), abre uma janela e varre os segmentos seguintes.
    /// 3. A janela fecha quando o gap de tempo passa de 35 segundos (delta &gt; 35d) — além disso a fala já
    ///    não é resposta direta àquela pergunta — ou quando aparece outra pergunta do operador com indicador
    ///    forte (início de novo turno do operador).
    /// 4. Dentro da janela, um candidato vira Motorista se for resposta curta, tiver indicador forte de
    ///    motorista, ou simplesmente não for pergunta/direcionamento do operador — desde que NÃO tenha
    ///    indicador forte de operador (<see cref="TemIndicadorOperadorForte"/> protege contra reclassificar
    ///    uma fala que claramente é do operador).
    /// 5. Após reclassificar um candidato "substancial" (texto &gt;= 30 caracteres OU duração &gt;= 2.2s),
    ///    encerra a janela: assume-se que essa foi a resposta principal do motorista e os próximos segmentos
    ///    pertencem a um novo turno. Respostas curtas (&lt; 30 chars e &lt; 2.2s) não fecham a janela, pois
    ///    podem ser confirmações encadeadas ("sim", "isso", número) antes da resposta de fato.
    /// </remarks>
    /// <param name="segmentos">Segmentos já formatados, em ordem cronológica.</param>
    /// <returns>Nova lista com speakers rebalanceados (cópia; a entrada não é mutada).</returns>
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
    /// <remarks>
    /// COMO funciona:
    /// 1. Ignora listas com menos de 3 segmentos.
    /// 2. Percorre tríades (anterior, atual, próximo). Há "troca isolada" quando anterior e próximo têm o
    ///    mesmo speaker e o atual difere — o padrão A-B-A em que B é provavelmente um erro de diarização.
    /// 3. Só corrige B se ele for "curto" (texto &lt;= 20 caracteres OU duração &lt;= 1.5s) E não tiver
    ///    evidência forte do próprio speaker. A evidência é checada conforme o lado de B: se B é Motorista,
    ///    exige indicador forte de motorista ou resposta curta; se é Operador, exige indicador forte de
    ///    operador ou pergunta/direcionamento. Os limiares baixos (20 chars / 1.5s) garantem que só falas
    ///    minúsculas e sem sinal próprio sejam absorvidas pelo speaker vizinho; uma fala longa ou com
    ///    evidência sólida é preservada mesmo no padrão sanduíche.
    /// </remarks>
    /// <param name="segmentos">Segmentos já formatados, em ordem cronológica.</param>
    /// <returns>Nova lista com trocas isoladas suavizadas (cópia; a entrada não é mutada).</returns>
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
    /// Filtra runs repetitivos (tokens de ruído repetidos 6+ vezes) e compacta repetições longas.
    /// </summary>
    /// <remarks>
    /// COMO funciona: agrupa "runs" de segmentos consecutivos com o MESMO texto normalizado e decide o
    /// destino de cada run pelo seu tamanho (runLength = quantos segmentos iguais em sequência):
    /// - DESCARTAR o run inteiro quando: é ruído curto repetido 6+ vezes (runLength &gt;= 6 e
    ///   <see cref="EhTokenRuidoCurto"/>) — alucinação típica de ASR; ou um texto muito curto
    ///   (&lt;= 12 chars) repetido 20+ vezes (runLength &gt;= 20) — loop degenerado da transcrição.
    /// - COMPACTAR para uma única ocorrência quando: o texto não é ruído nem pergunta e se repete 4+ vezes
    ///   (runLength &gt;= 4). Mantém uma cópia (índice i) e remove as demais. Perguntas são preservadas
    ///   (não compactadas) porque repetição de pergunta costuma ser reformulação real do operador.
    /// - Caso contrário, mantém todos os segmentos do run intactos.
    /// Os limiares 6/20/4 escalam com o quão "barulhento" é o conteúdo: quanto mais inócuo o texto, mais
    /// repetições são toleradas antes de descartar.
    /// </remarks>
    /// <param name="segmentos">Segmentos já formatados, em ordem cronológica.</param>
    /// <returns>Nova lista sem os runs de ruído e com runs longos compactados.</returns>
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
    /// <remarks>
    /// COMO funciona:
    /// 1. Ignora transcrições pequenas (&lt; 12 segmentos), onde uma repetição não distorce o resultado.
    /// 2. Encontra o texto normalizado mais frequente (a "frase dominante") agrupando por conteúdo.
    /// 3. Só age se a frase for de fato dominante: aparecer pelo menos 8 vezes (piso absoluto que evita
    ///    mexer em repetições pequenas) E representar &gt;= 45% de todos os segmentos
    ///    (<c>Math.Ceiling(count * 0.45)</c>) — sinal de alucinação/loop da transcrição em vez de fala real.
    /// 4. Mesmo dominante, NÃO compacta se for ruído curto (<see cref="EhTokenRuidoCurto"/>) — esse caso é
    ///    tratado por <see cref="FiltrarRunsRepetitivos"/> — nem se for pergunta (repetição pode ser legítima).
    /// 5. Quando age, varre na ordem e mantém apenas as 2 primeiras ocorrências da frase dominante,
    ///    descartando as demais; todos os outros segmentos passam intactos. Mantém 2 (e não 1) para
    ///    preservar a indicação de que a frase realmente foi dita mais de uma vez.
    /// </remarks>
    /// <param name="segmentos">Segmentos já formatados, em ordem cronológica.</param>
    /// <returns>Nova lista com a frase dominante limitada a 2 ocorrências, ou a entrada original se nada se aplica.</returns>
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
    /// Remove segmentos duplicados contíguos (mesma fala, mesmo speaker, separados por quase nenhum tempo).
    /// </summary>
    /// <remarks>
    /// COMO funciona: compara cada segmento com o último já aceito. Remove apenas quando o texto normalizado
    /// E o speaker são idênticos E o gap temporal é &lt;= 0.2s. O limiar de 0.2s (reduzido de 12s na Fase 8)
    /// é proposital: uma pessoa pode repetir a mesma frase poucos segundos depois por nervosismo
    /// ("eu tava ali... eu tava ali"), e essa repetição é legítima — só descartamos quando o gap é quase
    /// nulo, sinal de alucinação literal da API que emitiu o mesmo segmento duas vezes.
    /// </remarks>
    /// <param name="segmentos">Segmentos já formatados, em ordem cronológica.</param>
    /// <returns>Nova lista sem as duplicatas contíguas quase-simultâneas.</returns>
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
    /// Mescla segmentos consecutivos do mesmo speaker com gap pequeno, formando falas contínuas.
    /// </summary>
    /// <remarks>
    /// COMO funciona: percorre os segmentos acumulando no último resultado. Quando o atual pode ser mesclado
    /// com o anterior (<see cref="PodeMesclarSegmentos"/>), funde os textos via <see cref="UnirTextos"/>,
    /// recalcula o <c>TextoNormalizado</c> e estende a duração: o novo fim é o maior fim entre os dois
    /// segmentos (cobre sobreposição) e a duração resultante é <c>fim - inicio</c> (limitada a &gt;= 0 para
    /// evitar valores negativos por arredondamento). O <c>Timestamp</c> de início é preservado do anterior.
    /// Não mescla quando <see cref="PodeMesclarSegmentos"/> reprova; nesse caso o atual entra como novo bloco.
    /// </remarks>
    /// <param name="segmentos">Segmentos já formatados, em ordem cronológica.</param>
    /// <returns>Nova lista com segmentos consecutivos do mesmo speaker fundidos.</returns>
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

    /// <summary>
    /// Normaliza um texto para comparação: minúsculas, sem espaços nas pontas e com espaços internos colapsados.
    /// </summary>
    /// <remarks>
    /// COMO funciona: aplica <c>ToLowerInvariant</c> e <c>Trim</c> e, em seguida, substitui qualquer sequência
    /// de espaços em branco (regex <c>\s+</c>) por um único espaço. É a forma canônica usada em todas as
    /// comparações de igualdade de fala e nas heurísticas léxicas do serviço.
    /// </remarks>
    /// <param name="texto">Texto bruto a normalizar.</param>
    /// <returns>Texto normalizado.</returns>
    public static string NormalizarTexto(string texto)
    {
        return Regex.Replace(texto.ToLowerInvariant().Trim(), @"\s+", " ");
    }

    /// <summary>
    /// Formata um instante como "MM:SS" (minutos totais com dois dígitos, segundos com dois dígitos).
    /// </summary>
    /// <remarks>
    /// COMO funciona: usa o total de minutos arredondado para baixo (<c>Math.Floor(TotalMinutes)</c>) como
    /// componente de minutos — assim 75 minutos viram "75:..." em vez de reiniciar em horas — e o componente
    /// de segundos do <see cref="TimeSpan"/>. Ambos preenchidos a 2 dígitos (formato "00").
    /// </remarks>
    /// <param name="timestamp">Instante relativo ao início do áudio.</param>
    /// <returns>String no formato "MM:SS".</returns>
    public static string FormatarTimestamp(TimeSpan timestamp)
    {
        var totalMinutes = (int)Math.Floor(timestamp.TotalMinutes);
        return $"{totalMinutes:00}:{timestamp.Seconds:00}";
    }

    /// <summary>
    /// Une dois trechos de texto respeitando hifenização e pontuação de junção.
    /// </summary>
    /// <remarks>
    /// COMO funciona:
    /// 1. Se um dos lados é vazio/branco, retorna o outro já aparado.
    /// 2. Caso contrário, apara a cauda do primeiro e o início do segundo. Quando o primeiro termina em "-"
    ///    (palavra quebrada) OU o segundo começa com pontuação (regex <c>^[\.,;:\!\?]</c>), concatena SEM
    ///    espaço para não separar a palavra/pontuação. Nos demais casos junta com um espaço simples.
    /// </remarks>
    /// <param name="primeiro">Primeiro trecho.</param>
    /// <param name="segundo">Segundo trecho.</param>
    /// <returns>Os dois trechos unidos.</returns>
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

    /// <summary>
    /// Decide se dois segmentos consecutivos podem ser fundidos em uma única fala contínua.
    /// </summary>
    /// <remarks>
    /// COMO funciona: só permite a mesclagem quando TODAS as condições são satisfeitas:
    /// 1. Mesmo speaker — falas de interlocutores diferentes nunca se fundem.
    /// 2. Gap pequeno: o intervalo entre o fim do anterior e o início do atual deve ser &lt;= 3.0s.
    ///    Acima disso há pausa longa demais para tratar como continuação.
    /// 3. O anterior não pode terminar em "?" ou "!": pergunta/exclamação encerra a ideia, então o próximo
    ///    segmento é uma fala nova (e provavelmente uma resposta).
    /// 4. Se o anterior termina em ".", só funde com gap curto (&lt;= 1.3s); um ponto final com pausa maior
    ///    sinaliza fim de frase e não deve ser colado à seguinte.
    /// 5. Limite de tamanho: a soma dos textos não pode passar de 380 caracteres, para não gerar blocos
    ///    longos demais e ilegíveis na transcrição final.
    /// </remarks>
    /// <param name="anterior">Segmento já acumulado (candidato a absorver o próximo).</param>
    /// <param name="atual">Segmento seguinte, candidato a ser mesclado no anterior.</param>
    /// <returns><c>true</c> se os dois podem ser mesclados.</returns>
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
