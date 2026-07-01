/**
 * Serviço de Transcrição
 * Processa e formata transcrições de áudio
 */

/**
 * Separa transcrição do laudo principal
 * @param {string} markdown - Markdown completo retornado pela API
 * @returns {{ laudo: string, transcricao: string|null }}
 */
export function separarTranscricao(markdown) {
    if (!markdown) return { laudo: '', transcricao: null };

    // 1. Tenta encontrar seção explícita de Transcrição (5.0, 6.0, etc ou apenas TRANSCRIÇÃO)
    // Aceita # ou ##, com ou sem numeração
    const transcricaoRegex = /(#{1,2}\s*(?:[5-9]\.0\s*)?TRANSCRIÇÃO[^\n]*\n)([\s\S]*?)(?=#{1,2}\s*[6-9]\.0|$)/i;
    const match = transcricaoRegex.exec(markdown);

    if (match) {
        const transcricao = (match[1] + match[2]).trim();
        const laudo = markdown.replace(match[0], '').trim();
        console.log('📜 Transcrição separada com sucesso (Header encontrado)');
        return { laudo, transcricao };
    }

    // 2. Fallback: Se não tem header, mas tem timestamps [00:00], assume que é TUDO transcrição
    // Isso resolve o caso onde o modelo retorna apenas a transcrição sem headers
    if (/\[\d{1,3}:\d{2}(?::\d{2})?\]/.test(markdown)) {
        console.log('📜 Texto identificado como transcrição (Timestamps encontrados)');

        // Se tiver seções de laudo ANTES da transcrição, tenta separar
        // Procura o primeiro timestamp
        const firstTimestampIndex = markdown.search(/\[\d{1,3}:\d{2}(?::\d{2})?\]/);
        if (firstTimestampIndex > 50) { // Se o timestamp não está logo no começo
            // Verifica se tem headers de laudo antes
            const textBefore = markdown.substring(0, firstTimestampIndex);
            if (/##\s*(1\.0|Dados|Resumo)/i.test(textBefore)) {
                // É um laudo misturado. Tenta cortar a partir do último header antes do timestamp ou assumir que a transcrição começa no timestamp
                // Mas para segurança, se tem timestamps, retornamos como transcrição para não perder dados
                // O ideal seria separar, mas se o regex principal falhou, melhor mostrar tudo na transcrição do que nada
                return { laudo: '', transcricao: markdown };
            }
        }

        return { laudo: '', transcricao: markdown };
    }

    // 3. Se não tem timestamps e tem headers de laudo, é apenas laudo
    const temSecoesLaudo = /^##?\s*(📋|🕐|👤|📍|🔍|📝|Resumo|Cronologia|Identificação|Detalhes|Análise|Conclusão|1\.0)/mi.test(markdown);
    if (temSecoesLaudo) {
        console.log('📜 Laudo estruturado detectado sem transcrição');
        return { laudo: markdown, transcricao: null };
    }

    // 4. Último caso: retorna como laudo (ou texto genérico)
    console.log('📜 Nenhuma estrutura clara identificada - retornando como laudo');
    return { laudo: markdown, transcricao: null };
}

/**
 * Formata transcrição com cores e estilos HTML
 * @param {string} transcricaoMd - Markdown da transcrição
 * @returns {string} HTML formatado
 */
export function formatarTranscricao(transcricaoMd) {
    if (!transcricaoMd) return '<p>Transcrição não disponível.</p>';

    // Remove o título da transcrição (já está no header do modal)
    let texto = transcricaoMd.replace(/^#{1,2}\s*[5-8]\.0\s*TRANSCRIÇÃO.*?\n/i, '');
    texto = texto.replace(/^#{1,2}\s*.*TRANSCRIÇÃO.*?\n/i, '');

    // Remove mensagens de cortesia do final
    texto = texto.replace(/\n*(Espero que.*|Se precisar.*|Qualquer dúvida.*|Fico à disposição.*|Estou aqui para.*).*/gi, '').trim();

    // Normaliza quebras de linha - junta linhas que estão separadas incorretamente
    // Se uma linha começa com timestamp e a próxima com falante, junta elas
    texto = texto.replace(/\[(\d{1,3}:\d{2}(?::\d{2})?)\]\s*\n\s*\*\*/g, '[$1] **');

    // Também junta se timestamp está sozinho em uma linha
    texto = texto.replace(/^\[(\d{1,3}:\d{2}(?::\d{2})?)\]\s*$/gm, '[$1] ');

    // Processa cada linha como uma fala
    const linhas = texto.split('\n').filter(l => l.trim());
    const resultado = [];

    for (let linha of linhas) {
        linha = linha.trim();
        if (!linha) continue;

        // Extrai timestamp se existir
        const timestampMatch = linha.match(/^\[(\d{1,3}:\d{2}(?::\d{2})?)\]\s*/);
        let timestamp = '';
        let resto = linha;

        if (timestampMatch) {
            timestamp = timestampMatch[1];
            resto = linha.substring(timestampMatch[0].length);
        }

        // Cria o link clicável para o timestamp
        const timestampHtml = timestamp
            ? `<a href="#" class="timestamp-link" onclick="seekTo('${timestamp}'); return false;">[${timestamp}]</a> `
            : '';

        // LIMPEZA DE ARTEFATOS (****, **, etc)
        // Remove asteriscos soltos ou mal formados no início da fala
        resto = resto.replace(/^\s*\*{2,}\s*/, ''); // Remove **** no inicio
        resto = resto.replace(/\s*\*{2,}\s*/g, ' '); // Remove **** no meio trocando por espaço

        // Formata o falante e a fala
        // Detecta padrões de operador: **Operador**, Operador BAS:, Operador(a):
        if (resto.includes('**Operador') ||
            resto.toLowerCase().includes('operador bas:') ||
            resto.toLowerCase().includes('**operador:**') ||
            resto.toLowerCase().includes('operador(a):') ||
            resto.toLowerCase().includes('operador:')) {
            // Remove marcadores markdown e formata
            resto = resto.replace(/\*\*Operador[^*]*\*\*:?/gi, '');
            resto = resto.replace(/Operador\s*\(a\):?/gi, '');
            resto = resto.replace(/Operador BAS:?/gi, '');
            resto = resto.replace(/^Operador:?/gi, '');
            resultado.push(`
                <div class="transcricao-linha">
                    ${timestampHtml}<span class="speaker-operador"><strong>Operador BAS:</strong></span> <span class="fala-texto">${resto.trim()}</span>
                </div>
            `);
        } else if (resto.includes('**Motorista') ||
            resto.toLowerCase().includes('motorista:')) {
            resto = resto.replace(/\*\*Motorista\*\*:?/gi, '');
            resto = resto.replace(/Motorista:?/gi, '');
            resultado.push(`
                <div class="transcricao-linha">
                    ${timestampHtml}<span class="speaker-motorista"><strong>Motorista:</strong></span> <span class="fala-texto">${resto.trim()}</span>
                </div>
            `);
        } else if (resto.includes('**Falante')) {
            // Falante genérico
            const falanteMatch = resto.match(/\*\*\[?Falante\s*(\d+)\]?\*\*:?\s*/i);
            if (falanteMatch) {
                const numFalante = falanteMatch[1];
                resto = resto.replace(falanteMatch[0], '');
                const isOperador = numFalante === '1';
                resultado.push(`
                    <div class="transcricao-linha">
                        ${timestampHtml}<span class="${isOperador ? 'speaker-operador' : 'speaker-motorista'}"><strong>${isOperador ? 'Operador BAS' : 'Motorista'}:</strong></span> <span class="fala-texto">${resto.trim()}</span>
                    </div>
                `);
            } else {
                resultado.push(`<div class="transcricao-linha">${timestampHtml}<span class="fala-texto">${resto}</span></div>`);
            }
        } else {
            // Linha sem falante identificado
            resultado.push(`<div class="transcricao-linha">${timestampHtml}<span class="fala-texto">${resto}</span></div>`);
        }
    }

    if (resultado.length === 0) {
        return `<p>${texto}</p>`;
    }

    return `<div class="transcricao-formatada">${resultado.join('')}</div>`;
}

/**
 * Formata transcrição bruta (sem marcadores) identificando padrões de diálogo
 * @param {string} texto - Texto bruto da transcrição
 * @returns {string} HTML formatado
 */
function formatarTranscricaoBruta(texto) {
    // Divide por sentenças (pontos, interrogações, exclamações)
    const sentencas = texto.split(/(?<=[.?!])\s+/);
    let resultado = [];
    let ultimoFalante = 'operador'; // Assume que operador começa

    // Padrões que indicam OPERADOR (quem faz perguntas/conduz)
    const padroesOperador = [
        /\?$/, // Termina com interrogação
        /^(alô|boa noite|boa tarde|bom dia)/i,
        /^(certo|ok|tá|entendi|entendo)/i,
        /^(pode|qual|como|quando|onde|por que|o que)/i,
        /^(me fala|me conta|me diz|me relata|consegue me)/i,
        /^(é isso|isso aí|perfeito|beleza)/i,
        /^(estou entrando|estou ligando|sou da central)/i,
        /^(o senhor|a senhora|você)/i
    ];

    // Padrões que indicam MOTORISTA (quem responde/relata)
    const padroesMotorista = [
        /^(sim|não|é|foi|estava|tinha|fui|tava)/i,
        /^(pois é|olha|então|aí)/i,
        /^(eu |meu |minha |a gente )/i,
        /^(porque |por causa |devido )/i,
        /^(aconteceu|acontece|acontecia)/i,
        /relatando|relatar/i
    ];

    for (let sentenca of sentencas) {
        sentenca = sentenca.trim();
        if (!sentenca || sentenca.length < 3) continue;

        let falante = ultimoFalante;

        // Verifica padrões
        const ehOperador = padroesOperador.some(p => p.test(sentenca));
        const ehMotorista = padroesMotorista.some(p => p.test(sentenca));

        if (ehOperador && !ehMotorista) {
            falante = 'operador';
        } else if (ehMotorista && !ehOperador) {
            falante = 'motorista';
        } else if (sentenca.endsWith('?')) {
            falante = 'operador';
        }

        // Alterna se detectar padrão diferente do último
        if (falante === 'operador') {
            resultado.push(`<p><span class="speaker-operador"><strong>Operador BAS:</strong></span> ${sentenca}</p>`);
        } else {
            resultado.push(`<p><span class="speaker-motorista"><strong>Motorista:</strong></span> ${sentenca}</p>`);
        }

        ultimoFalante = falante;
    }

    if (resultado.length === 0) {
        return `<p>${texto}</p>`;
    }

    return `<div class="transcricao-formatada">${resultado.join('')}</div>`;
}

/**
 * Remove mensagens de cortesia do texto
 * @param {string} texto - Texto a limpar
 * @returns {string} Texto limpo
 */
export function removerCortesia(texto) {
    if (!texto) return '';
    return texto.replace(/\n*(Espero que.*|Se precisar.*|Qualquer dúvida.*|Fico à disposição.*|Estou aqui para.*).*/gi, '').trim();
}
