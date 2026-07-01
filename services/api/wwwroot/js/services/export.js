/**
 * Serviço de Exportação (Sentinel)
 *
 * Responsável por extrair o conteúdo já renderizado na UI (transcrições e laudos
 * periciais de sinistro) e exportá-lo em três formatos:
 *   - Texto puro (cópia para a área de transferência);
 *   - PDF (via biblioteca global `html2pdf`, renderizando HTML estilizado inline);
 *   - Word/DOCX (via biblioteca global `docx`, montando o documento programaticamente).
 *
 * Conceitos compartilhados por quase todas as funções deste módulo:
 *   - `type`: discrimina a origem do conteúdo a exportar e determina qual elemento
 *     do DOM contém o texto. Os casos especiais são:
 *       'transcricao' -> usa `#transcricaoView` (preferencial) ou `#outputTranscricao`;
 *       'audio'       -> usa `#laudoView` (laudo de análise de áudio) e tenta anexar
 *                        a tabela auxiliar de `#dadosIdentificadosView`;
 *       demais tipos  -> usam `#output<Type>` (ex.: 'imagem' -> `#outputImagem`).
 *   - Paleta de cores da marca (hex, sem '#', no formato esperado pelo docx):
 *       FF6B00 = laranja nstech (destaques, falante "operador");
 *       C0392B = vermelho (falante "motorista", renderizado em itálico);
 *       333333 = cinza escuro (texto neutro / timestamps).
 *   - As bibliotecas `html2pdf`, `docx` e `marked` são carregadas globalmente via
 *     <script> e podem estar ausentes; por isso há checagens `typeof ... !== 'undefined'`.
 *
 * Observação: este projeto é Azure-only para a etapa de análise; aqui não há
 * dependência de provedores de IA — apenas formatação/exportação no navegador.
 */

import { TITULOS_PDF, HTML2PDF_OPTIONS } from '../config/constants.js';
import { capitalize, formatarDataHora, refreshIcons } from '../core/utils.js';
import { toast } from '../ui/toast.js';

/**
 * Copia para a área de transferência o texto visível do bloco de saída associado ao tipo.
 *
 * Como funciona:
 *   1. Resolve o id do elemento de saída a partir de `type` (ver convenção no
 *      cabeçalho do módulo). Para 'transcricao' há fallback de `#transcricaoView`
 *      para `#outputTranscricao` caso a view formatada não exista.
 *   2. Se o elemento não existir, avisa o usuário (toast) e aborta.
 *   3. Lê `innerText` (texto já renderizado, sem tags HTML) e usa a Clipboard API
 *      assíncrona para copiar, exibindo toast de sucesso ou de erro.
 *
 * @param {string} type - Categoria do conteúdo ('transcricao' | 'audio' | outro tipo de laudo).
 * @returns {void}
 * @sideeffect Escreve na área de transferência via `navigator.clipboard` e dispara toasts.
 */
export function copiarTexto(type) {
    let outputId;
    if (type === 'transcricao') {
        outputId = document.getElementById('transcricaoView') ? 'transcricaoView' : 'outputTranscricao';
    } else if (type === 'audio') {
        outputId = 'laudoView';

    } else {
        outputId = `output${capitalize(type)}`;
    }

    const output = document.getElementById(outputId);
    if (!output) {
        toast.warning('Conteúdo não encontrado.');
        return;
    }

    const texto = output.innerText;
    navigator.clipboard.writeText(texto).then(() => {
        toast.success('Texto copiado!');
    }).catch(err => {
        console.error('Erro ao copiar:', err);
        toast.error('Erro ao copiar texto');
    });
}

/**
 * Exporta o conteúdo visível de um tipo para um arquivo PDF estilizado.
 *
 * Como funciona:
 *   1. Resolve o elemento de saída a partir de `type` (mesma convenção de `copiarTexto`)
 *      e aborta com aviso se não existir.
 *   2. Captura `innerHTML` (preserva a formatação rica) como conteúdo base.
 *   3. Caso especial 'audio': se houver dados em `#dadosIdentificadosView`, prefixa
 *      esse bloco ao conteúdo dentro de um card cinza (#f9f9f9) para que os "Dados
 *      Identificados" apareçam no topo do laudo.
 *   4. Monta um documento HTML completo num <div> "wrapper" fora da tela, com cabeçalho
 *      da marca (nstech), título (vindo de `TITULOS_PDF[type]`, com fallback
 *      'Laudo Pericial'), metadados (data de emissão e responsável técnico) e rodapé.
 *   5. Reescreve as classes CSS da transcrição em estilos inline equivalentes, porque
 *      o html2pdf rasteriza o HTML isolado e NÃO carrega a folha de estilos da página:
 *        - `.timestamp-link`     -> cinza, fonte monoespaçada (timestamps discretos);
 *        - `.speaker-operador`   -> laranja em negrito;
 *        - `.speaker-motorista`  -> vermelho em negrito itálico.
 *   6. Mescla `HTML2PDF_OPTIONS` com um `filename` único baseado em `Date.now()`
 *      (epoch em ms) para evitar sobrescrita, e dispara o download via html2pdf.
 *
 * Pré-condição: a biblioteca global `html2pdf` deve estar carregada; se ausente, a
 * função simplesmente não gera nada (sem erro visível).
 *
 * @param {string} type - Categoria do conteúdo ('transcricao' | 'audio' | outro tipo de laudo).
 * @returns {void}
 * @sideeffect Cria elementos no DOM (temporários), inicia o download do PDF e dispara toasts.
 */
export function exportarPDF(type) {
    let outputId;
    if (type === 'transcricao') {
        outputId = document.getElementById('transcricaoView') ? 'transcricaoView' : 'outputTranscricao';
    } else if (type === 'audio') {
        outputId = 'laudoView';

    } else {
        outputId = `output${capitalize(type)}`;
    }

    const output = document.getElementById(outputId);
    if (!output) {
        toast.warning('Conteúdo não encontrado.');
        return;
    }

    // Se for áudio, tenta incluir tabela de dados identificados
    let contentToExport = output.innerHTML;
    if (type === 'audio') {
        const dadosView = document.getElementById('dadosIdentificadosView');
        if (dadosView && dadosView.innerText.trim()) {
            contentToExport = `<div style="margin-bottom: 30px; padding: 15px; background: #f9f9f9; border-radius: 8px; border: 1px solid #eee;">
                <h3 style="color: #ff6b00; margin-top: 0; font-family: 'Segoe UI', sans-serif;">Dados Identificados</h3>
                ${dadosView.innerHTML}
            </div>` + contentToExport;
        }
    }

    const titulo = TITULOS_PDF[type] || 'Laudo Pericial';

    if (typeof html2pdf !== 'undefined') {
        const wrapper = document.createElement('div');

        wrapper.innerHTML = `
            <div style="font-family: 'Segoe UI', Arial, sans-serif; padding: 40px; color: #333; background: #fff; max-width: 800px; margin: 0 auto;">
                <div style="border-bottom: 2px solid #ff6b00; padding-bottom: 20px; margin-bottom: 40px;">
                    <h1 style="color: #ff6b00; margin: 0; font-size: 32px; font-weight: 800;">nstech</h1>
                </div>
                <div style="margin-bottom: 30px;">
                    <h2 style="color: #1a1a1a; font-size: 24px; font-weight: 700; margin: 0 0 10px 0;">${titulo}</h2>
                    <p style="color: #666; font-size: 14px; margin: 0;">
                        <strong>Data de Emissão:</strong> ${formatarDataHora(new Date())} <br>
                        <strong>Responsável Técnico:</strong> nstech
                    </p>
                </div>
                <div style="color: #333; font-size: 14px; line-height: 1.6;">
                    ${contentToExport
                .replace(/class="timestamp-link"/g, 'style="color: #666; font-size: 11px; font-family: Consolas, monospace;"')
                .replace(/class="speaker-operador"/g, 'style="color: #ff6b00; font-weight: 700;"')
                .replace(/class="speaker-motorista"/g, 'style="color: #c0392b; font-weight: 700; font-style: italic;"')
            }
                </div>
                <div style="margin-top: 60px; padding-top: 20px; border-top: 1px solid #eee; text-align: center;">
                    <p style="color: #999; font-size: 10px;">nstech © 2026</p>
                </div>
            </div>
        `;

        const opt = {
            ...HTML2PDF_OPTIONS,
            filename: `${type === 'transcricao' ? 'transcricao' : 'laudo'}_${type}_${Date.now()}.pdf`,
        };

        html2pdf().set(opt).from(wrapper).save();
        toast.success('PDF gerado com sucesso!');
    }
}

/**
 * Exporta para PDF um item previamente salvo no histórico (em vez do conteúdo da view atual).
 *
 * Diferença em relação a `exportarPDF`: a fonte do conteúdo não é o DOM da tela, e sim
 * o objeto `item` persistido. O conteúdo costuma estar em Markdown, então:
 *   1. Se `marked` estiver disponível, converte `item.conteudo` de Markdown para HTML;
 *      caso contrário, usa fallback que apenas embrulha o texto cru num <pre> com
 *      quebra de linha preservada (`white-space: pre-wrap`).
 *   2. Monta o wrapper HTML com cabeçalho (tipo + nome do arquivo, com fallback
 *      'Documento'), data de geração (`item.dataAnalise` ou `item.data`) e rodapé.
 *   3. Se `html2pdf` existir, gera o nome do arquivo a partir do tipo em minúsculas
 *      (fallback 'documento') + `Date.now()` e dispara o download.
 *
 * @param {Object} item - Item salvo do histórico.
 * @param {string} [item.tipo] - Rótulo do tipo de análise (usado no título e no nome do arquivo).
 * @param {string} [item.arquivo] - Nome do arquivo de origem (exibido no cabeçalho).
 * @param {string} [item.conteudo] - Conteúdo do laudo, normalmente em Markdown.
 * @param {(string|number|Date)} [item.dataAnalise] - Data de análise (preferencial para exibição).
 * @param {(string|number|Date)} [item.data] - Data alternativa, usada se `dataAnalise` ausente.
 * @returns {void} Retorna cedo (sem efeito) se `item` for falsy.
 * @sideeffect Cria elementos no DOM, inicia o download do PDF e dispara toast.
 */
export function exportarItemPDF(item) {
    if (!item) return;

    const tempDiv = document.createElement('div');
    if (typeof marked !== 'undefined') {
        tempDiv.innerHTML = marked.parse(item.conteudo || '');
    } else {
        tempDiv.innerHTML = `<pre style="white-space: pre-wrap;">${item.conteudo}</pre>`;
    }

    const wrapper = document.createElement('div');
    wrapper.innerHTML = `
        <div style="font-family: 'Segoe UI', Arial, sans-serif; padding: 30px; color: #1a1a1a; background: #fff;">
            <div style="border-bottom: 3px solid #ff6b00; padding-bottom: 15px; margin-bottom: 25px;">
                <h1 style="color: #ff6b00; margin: 0; font-size: 24px;">${item.tipo} - ${item.arquivo || 'Documento'}</h1>
                <p style="color: #333; font-size: 12px; margin-top: 8px;">Gerado em: ${formatarDataHora(item.dataAnalise || item.data)} | nstech</p>
            </div>
            <div style="color: #1a1a1a; font-size: 14px; line-height: 1.8;">${tempDiv.innerHTML}</div>
            <div style="margin-top: 40px; padding-top: 15px; border-top: 1px solid #e0e0e0; text-align: center;">
                <p style="color: #666; font-size: 10px;">nstech © 2026</p>
            </div>
        </div>
    `;

    if (typeof html2pdf !== 'undefined') {
        const opt = { ...HTML2PDF_OPTIONS, filename: `${item.tipo?.toLowerCase() || 'documento'}_${Date.now()}.pdf` };
        html2pdf().set(opt).from(wrapper).save();
        toast.success('PDF gerado!');
    }
}

/**
 * Extrai a transcrição como uma lista de blocos estruturados a partir do DOM já renderizado.
 *
 * Por que ler do DOM e não do texto cru: quando a transcrição foi renderizada com
 * formatação (classes CSS por elemento), o DOM carrega a separação confiável entre
 * timestamp, falante e fala — informação que se perderia ao ler apenas `innerText`.
 *
 * Estratégia em dois caminhos:
 *   1. CAMINHO FORMATADO (preferencial): se existir ao menos um `.transcricao-linha`,
 *      itera cada linha e lê os sub-elementos:
 *        `.timestamp-link`      -> carimbo de tempo;
 *        `.speaker-operador`    -> falante é o operador (marca `isOperador`);
 *        `.speaker-motorista`   -> falante é o motorista (marca `isMotorista`);
 *        `.fala-texto`          -> texto da fala.
 *      Linhas totalmente vazias (sem timestamp, falante nem texto) são descartadas.
 *   2. CAMINHO FALLBACK: se não há marcação, parseia `innerText` linha a linha (ver abaixo).
 *
 * @param {HTMLElement} container - Elemento que contém a transcrição renderizada.
 * @returns {Array<Object>} Lista de blocos. Blocos de fala têm a forma
 *   `{ timestamp, speaker, text, isOperador, isMotorista }`; blocos de texto solto
 *   (apenas no fallback) têm a forma `{ type: 'text', content }`.
 */
function extractTranscriptionFromDOM(container) {
    const blocks = [];

    // Busca todas as linhas de transcrição formatadas
    const linhas = container.querySelectorAll('.transcricao-linha');

    if (linhas.length > 0) {
        // Estrutura formatada com classes CSS
        linhas.forEach(linha => {
            const timestampEl = linha.querySelector('.timestamp-link');
            const speakerOperadorEl = linha.querySelector('.speaker-operador');
            const speakerMotoristaEl = linha.querySelector('.speaker-motorista');
            const textEl = linha.querySelector('.fala-texto');

            const timestamp = timestampEl ? timestampEl.textContent.trim() : '';
            let speaker = '';
            let isOperador = false;
            let isMotorista = false;

            if (speakerOperadorEl) {
                speaker = speakerOperadorEl.textContent.trim();
                isOperador = true;
            } else if (speakerMotoristaEl) {
                speaker = speakerMotoristaEl.textContent.trim();
                isMotorista = true;
            }

            const text = textEl ? textEl.textContent.trim() : '';

            if (timestamp || speaker || text) {
                blocks.push({
                    timestamp: timestamp,
                    speaker: speaker,
                    text: text,
                    isOperador: isOperador,
                    isMotorista: isMotorista
                });
            }
        });
    } else {
        // Fallback: texto simples sem formatação HTML.
        // Quebra o texto em linhas não vazias e reconstrói os blocos manualmente,
        // acumulando linhas de continuação no bloco aberto até encontrar o próximo
        // timestamp/falante.
        const rawText = container.innerText;
        const lines = rawText.split('\n').filter(l => l.trim());

        let currentBlock = null;

        lines.forEach(line => {
            // Heurística de cabeçalho de fala. O regex casa linhas no formato
            //   "[mm:ss] Falante: texto..."  ou  "[hh:mm:ss] Falante: texto..."
            // Grupos capturados:
            //   1 = tempo  -> \d{1,3}:\d{2}        => minutos (1 a 3 dígitos, permite >99 min) e segundos;
            //                 (?::\d{2})? opcional => parte de segundos quando o formato é H:MM:SS;
            //   2 = falante -> [^:]+              => tudo até o primeiro ':' (não pode conter ':');
            //   3 = texto   -> (.*)               => restante da linha após "Falante: ".
            const match = line.match(/^\[(\d{1,3}:\d{2}(?::\d{2})?)\]\s*([^:]+):\s*(.*)$/);

            if (match) {
                // Novo cabeçalho encontrado: fecha o bloco anterior (se houver) antes de abrir o novo.
                if (currentBlock) blocks.push(currentBlock);

                const speaker = match[2].trim();
                currentBlock = {
                    timestamp: `[${match[1]}]`,
                    speaker: speaker,
                    text: match[3].trim(),
                    // Classificação do falante por substring (case-insensitive) — define cor/itálico na exportação.
                    isOperador: speaker.toLowerCase().includes('operador'),
                    isMotorista: speaker.toLowerCase().includes('motorista')
                };
            } else if (currentBlock) {
                // Linha de continuação: pertence à fala em andamento (quebra de linha dentro da mesma fala).
                currentBlock.text += ' ' + line.trim();
            } else {
                // Linha solta antes de qualquer cabeçalho (ex.: título): vira bloco de texto genérico.
                blocks.push({ type: 'text', content: line });
            }
        });

        // Não esquecer de emitir o último bloco aberto ao terminar a iteração.
        if (currentBlock) blocks.push(currentBlock);
    }

    return blocks;
}

/**
 * Exporta o conteúdo visível de um tipo para um arquivo Word (.docx).
 *
 * Ao contrário do PDF (que rasteriza HTML), aqui o documento é construído
 * programaticamente com a biblioteca `docx`, montando um array `children` de
 * `Paragraph`/`TextRun` que vira o corpo da seção.
 *
 * Estrutura montada, em ordem:
 *   1. Título centralizado em maiúsculas (de `TITULOS_PDF[type]`, fallback 'Laudo Pericial').
 *   2. Metadados: "Data de Emissão" (data/hora atual) e "Responsável Técnico: nstech".
 *   3. Parágrafo-separador com borda inferior cinza (simula linha horizontal).
 *   4. Corpo, que depende de `type`:
 *      - 'transcricao': usa `extractTranscriptionFromDOM` e, para cada bloco de fala,
 *        emite DUAS linhas — "[timestamp] Falante:" (falante colorido por papel) e o
 *        texto (em itálico quando motorista). Blocos `type:'text'` viram parágrafo simples.
 *      - demais tipos: para 'audio', primeiro anexa a seção "DADOS IDENTIFICADOS"
 *        (linhas de `#dadosIdentificadosView`) seguida de separador; depois trata o
 *        corpo genérico, onde linhas iniciadas por '#' (Markdown heading) viram
 *        títulos HEADING_2 em laranja e as demais viram parágrafos comuns.
 *   5. Cabeçalho ("nstech" à direita, com borda inferior laranja) e rodapé
 *      ("Documento Confidencial | nstech © 2026") aplicados à seção.
 *
 * Unidades da biblioteca docx usadas aqui:
 *   - `size` é em half-points (ex.: 22 = 11pt, 32 = 16pt, 28 = 14pt, 20 = 10pt, 16 = 8pt).
 *   - `spacing.before/after` é em twips (1/20 de ponto; ex.: 400 ≈ 20pt de espaçamento).
 *   - `border.size` é em oitavos de ponto (ex.: 6 = 0,75pt).
 *
 * Ao final, serializa o documento em Blob e força o download via link <a> temporário;
 * o nome do arquivo usa a data atual no formato AAAAMMDD. Erros (ex.: biblioteca
 * indisponível ou falha de geração) são capturados e reportados via toast.
 *
 * @param {string} type - Categoria do conteúdo ('transcricao' | 'audio' | outro tipo de laudo).
 * @returns {void}
 * @sideeffect Cria/remove um <a> no DOM, gera e baixa o .docx (assíncrono via Packer) e dispara toasts.
 */
export function exportarWord(type) {
    let outputId;
    if (type === 'transcricao') {
        outputId = document.getElementById('transcricaoView') ? 'transcricaoView' : 'outputTranscricao';
    } else if (type === 'audio') {
        outputId = 'laudoView';

    } else {
        outputId = `output${capitalize(type)}`;
    }

    const output = document.getElementById(outputId);
    if (!output) {
        toast.warning('Conteúdo não encontrado.');
        return;
    }

    const titulo = TITULOS_PDF[type] || 'Laudo Pericial';

    if (typeof docx === 'undefined') {
        toast.error('Biblioteca DOCX não carregada.');
        return;
    }

    try {
        // Desestrutura os construtores do namespace global `docx`. `children` é o
        // acúmulo de parágrafos que comporão o corpo da seção do documento.
        const { Document, Paragraph, TextRun, HeadingLevel, Packer, AlignmentType, BorderStyle, Header, Footer } = docx;
        const children = [];

        // Título
        children.push(
            new Paragraph({
                children: [new TextRun({ text: titulo.toUpperCase(), bold: true, size: 32, font: "Calibri", color: "1a1a1a" })],
                alignment: AlignmentType.CENTER,
                spacing: { after: 400 }
            })
        );

        // Metadados
        children.push(
            new Paragraph({
                children: [
                    new TextRun({ text: "Data de Emissão: ", bold: true, size: 22, font: "Calibri" }),
                    new TextRun({ text: formatarDataHora(new Date()), size: 22, font: "Calibri" })
                ],
                spacing: { after: 100 }
            })
        );
        children.push(
            new Paragraph({
                children: [
                    new TextRun({ text: "Responsável Técnico: ", bold: true, size: 22, font: "Calibri" }),
                    new TextRun({ text: "nstech", size: 22, font: "Calibri" })
                ],
                spacing: { after: 400 }
            })
        );

        // Separador: parágrafo vazio cuja única função visual é a borda inferior cinza (linha divisória).
        children.push(new Paragraph({ border: { bottom: { color: 'CCCCCC', size: 6, style: BorderStyle.SINGLE } }, spacing: { after: 300 } }));

        // Conteúdo
        if (type === 'transcricao') {
            // Obtém a transcrição já estruturada (ver extractTranscriptionFromDOM).
            const blocks = extractTranscriptionFromDOM(output);

            blocks.forEach(block => {
                if (block.type === 'text') {
                    // Texto genérico (linha solta, ex.: título sem timestamp/falante).
                    children.push(new Paragraph({ children: [new TextRun({ text: block.content, size: 22 })], spacing: { after: 120 } }));
                } else {
                    // Bloco de fala estruturado.
                    // Cor do falante por papel: laranja (operador), vermelho (motorista) ou cinza (neutro).
                    const speakerColor = block.isOperador ? "FF6B00" : (block.isMotorista ? "C0392B" : "333333");

                    // Linha 1: [Timestamp] Speaker:
                    children.push(
                        new Paragraph({
                            children: [
                                new TextRun({ text: block.timestamp, size: 20, font: "Consolas", color: "333333" }),
                                new TextRun({ text: " " }),
                                new TextRun({ text: block.speaker, bold: true, size: 22, font: "Calibri", color: speakerColor })
                            ],
                            spacing: { after: 40 }
                        })
                    );

                    // Linha 2: Texto
                    children.push(
                        new Paragraph({
                            children: [new TextRun({ text: block.text, size: 22, font: "Calibri", italics: block.isMotorista })],
                            spacing: { after: 240 }
                        })
                    );
                }
            });
        } else {
            // Para laudos de áudio, prefixa a seção "Dados Identificados" (uma linha por entrada).
            if (type === 'audio') {
                const dadosView = document.getElementById('dadosIdentificadosView');
                if (dadosView && dadosView.innerText.trim()) {
                    children.push(new Paragraph({
                        children: [new TextRun({ text: "DADOS IDENTIFICADOS", bold: true, size: 24, color: "FF6B00" })],
                        spacing: { before: 200, after: 200 }
                    }));

                    // Cada linha não vazia da view vira um parágrafo simples.
                    const dadosLines = dadosView.innerText.split('\n').filter(l => l.trim());
                    dadosLines.forEach(linha => {
                        children.push(new Paragraph({ children: [new TextRun({ text: linha, size: 22 })], spacing: { after: 120 } }));
                    });

                    // Separador entre a tabela de dados e o corpo do laudo.
                    children.push(new Paragraph({ border: { bottom: { color: 'CCCCCC', size: 6, style: BorderStyle.SINGLE } }, spacing: { after: 300 } }));
                }
            }

            // Conteúdo genérico (laudos): processa o texto linha a linha.
            const linhas = output.innerText.split('\n').filter(l => l.trim());
            linhas.forEach(linha => {
                // Linhas iniciadas por '#' são tratadas como títulos Markdown (heading).
                const isHeader = linha.startsWith('#');
                if (isHeader) {
                    // Remove os marcadores '#' iniciais (e espaços) e aplica estilo de heading laranja.
                    children.push(new Paragraph({
                        children: [new TextRun({ text: linha.replace(/^#+\s*/, ''), bold: true, size: 28, color: "FF6B00" })],
                        heading: HeadingLevel.HEADING_2,
                        spacing: { before: 400, after: 200 }
                    }));
                } else {
                    children.push(new Paragraph({ children: [new TextRun({ text: linha, size: 22 })], spacing: { after: 120 } }));
                }
            });
        }

        const doc = new Document({
            sections: [{
                headers: {
                    default: new Header({
                        children: [new Paragraph({
                            children: [new TextRun({ text: "nstech", bold: true, size: 28, color: "FF6B00", font: "Calibri" })],
                            alignment: AlignmentType.RIGHT,
                            border: { bottom: { color: "FF6B00", size: 6, style: BorderStyle.SINGLE } }
                        })]
                    })
                },
                footers: {
                    default: new Footer({
                        children: [new Paragraph({
                            children: [new TextRun({ text: "Documento Confidencial | nstech © 2026", size: 16, color: "999999" })],
                            alignment: AlignmentType.CENTER
                        })]
                    })
                },
                children: children
            }]
        });

        // Packer.toBlob serializa o documento docx (assíncrono) num Blob OOXML.
        Packer.toBlob(doc).then(docBlob => {
            // Reembrulha no MIME type oficial do .docx (wordprocessingml.document).
            const blob = new Blob([docBlob], { type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' });
            // Carimbo de data AAAAMMDD: pega o "YYYY-MM-DD" de toISOString() e remove os hífens.
            const timestamp = new Date().toISOString().slice(0, 10).replace(/-/g, '');
            const filename = `${type === 'transcricao' ? 'transcricao' : 'laudo'}_${type}_${timestamp}.docx`;
            // Truque de download no browser: cria um <a> com Object URL, clica
            // programaticamente e revoga a URL para liberar memória.
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
            toast.success('Word gerado com sucesso!');
        });
    } catch (err) {
        console.error('Erro ao gerar Word:', err);
        toast.error('Erro ao gerar Word: ' + err.message);
    }
}

/**
 * Exporta para Word (.docx) um item salvo no histórico (fonte é o objeto `item`, não o DOM).
 *
 * Análogo a `exportarItemPDF`, mas montando o documento via `docx`. O conteúdo
 * (`item.conteudo`) é texto/Markdown e é processado de forma mais simples que em
 * `exportarWord` — não há classes CSS para inspecionar, então o parsing é feito por
 * regex sobre as linhas.
 *
 * Fluxo:
 *   1. Aborta cedo se `item` for falsy ou se a biblioteca `docx` não estiver carregada.
 *   2. Monta título (tipo + nome do arquivo, fallback 'Documento'), subtítulo com a
 *      data de geração e um separador laranja.
 *   3. Decide o modo de parsing pelo tipo:
 *      - Se `item.tipo` contém "transcri" (case-insensitive): reconstrói blocos de
 *        fala usando o MESMO regex de cabeçalho de `extractTranscriptionFromDOM`
 *        (ver naquela função a explicação dos grupos), emitindo as duas linhas
 *        timestamp+falante / texto. Linhas de continuação são anexadas ao bloco aberto;
 *        o último bloco é emitido após o loop (padrão "flush do buffer").
 *      - Caso contrário: cada linha vira um parágrafo, removendo marcadores de negrito
 *        Markdown (`**`) já que o docx não interpreta Markdown.
 *   4. Acrescenta separador superior e rodapé "nstech © 2026" centralizado.
 *   5. Serializa em Blob e dispara o download (nome AAAAMMDD; espaços do tipo viram '_').
 *
 * Unidades docx: `size` em half-points; `spacing` em twips; `border.size` em 1/8 de ponto
 * (ver detalhamento em `exportarWord`).
 *
 * @param {Object} item - Item salvo do histórico.
 * @param {string} [item.tipo] - Tipo da análise; se contiver "transcri", ativa o parsing de transcrição.
 * @param {string} [item.arquivo] - Nome do arquivo de origem (exibido no título).
 * @param {string} [item.conteudo] - Conteúdo textual/Markdown a exportar.
 * @param {(string|number|Date)} [item.dataAnalise] - Data de análise (preferencial).
 * @param {(string|number|Date)} [item.data] - Data alternativa, usada se `dataAnalise` ausente.
 * @returns {void} Retorna cedo se `item` for falsy ou se `docx` não estiver disponível.
 * @sideeffect Cria/remove um <a> no DOM, gera e baixa o .docx (assíncrono) e dispara toasts.
 */
export function exportarItemWord(item) {
    if (!item) return;
    if (typeof docx === 'undefined') {
        toast.error('Biblioteca DOCX não carregada.');
        return;
    }

    try {
        // Construtores da lib docx + buffer de parágrafos do corpo.
        const { Document, Paragraph, TextRun, HeadingLevel, Packer, AlignmentType, BorderStyle } = docx;
        const children = [];

        // Título
        children.push(new Paragraph({
            children: [new TextRun({ text: `${item.tipo} - ${item.arquivo || 'Documento'}`, bold: true, size: 32, color: 'FF6B00' })],
            heading: HeadingLevel.HEADING_1,
            spacing: { after: 200 }
        }));
        children.push(new Paragraph({
            children: [new TextRun({ text: `Gerado em: ${formatarDataHora(item.dataAnalise || item.data)} | nstech`, size: 20, color: '666666' })],
            spacing: { after: 400 }
        }));
        children.push(new Paragraph({ border: { bottom: { color: 'FF6B00', size: 12, style: BorderStyle.SINGLE } }, spacing: { after: 300 } }));

        // Conteúdo - parse simples para itens salvos.
        // `isTranscription` decide entre o parser de falas e o parser genérico de linhas.
        const isTranscription = item.tipo?.toLowerCase().includes('transcri');
        const conteudo = item.conteudo || '';
        const linhas = conteudo.split('\n').filter(l => l.trim());

        if (isTranscription) {
            // `currentBlock` é o bloco de fala aberto; só é "emitido" (gera parágrafos)
            // quando o próximo cabeçalho aparece ou ao final do loop.
            let currentBlock = null;

            linhas.forEach(line => {
                // Mesmo regex de cabeçalho de fala usado em extractTranscriptionFromDOM
                // (grupos: 1=tempo, 2=falante, 3=texto).
                const match = line.match(/^\[(\d{1,3}:\d{2}(?::\d{2})?)\]\s*([^:]+):\s*(.*)$/);

                if (match) {
                    // Antes de abrir o novo bloco, materializa o anterior em dois parágrafos.
                    if (currentBlock) {
                        // Cor do falante por papel (laranja=operador, vermelho=motorista, cinza=neutro).
                        const speakerColor = currentBlock.isOperador ? "FF6B00" : (currentBlock.isMotorista ? "C0392B" : "333333");
                        children.push(new Paragraph({
                            children: [
                                new TextRun({ text: currentBlock.timestamp, size: 20, font: "Consolas", color: "333333" }),
                                new TextRun({ text: " " }),
                                new TextRun({ text: currentBlock.speaker, bold: true, size: 22, color: speakerColor })
                            ],
                            spacing: { after: 40 }
                        }));
                        children.push(new Paragraph({
                            children: [new TextRun({ text: currentBlock.text, size: 22, italics: currentBlock.isMotorista })],
                            spacing: { after: 240 }
                        }));
                    }

                    // Abre o novo bloco a partir dos grupos capturados.
                    const speaker = match[2].trim();
                    currentBlock = {
                        timestamp: `[${match[1]}]`,
                        speaker: speaker,
                        text: match[3].trim(),
                        // Classificação do falante por substring (define cor e itálico).
                        isOperador: speaker.toLowerCase().includes('operador'),
                        isMotorista: speaker.toLowerCase().includes('motorista')
                    };
                } else if (currentBlock) {
                    // Continuação da fala atual (linha sem cabeçalho).
                    currentBlock.text += ' ' + line.trim();
                } else {
                    // Linha solta antes de qualquer cabeçalho: parágrafo simples.
                    children.push(new Paragraph({ children: [new TextRun({ text: line, size: 22 })], spacing: { after: 100 } }));
                }
            });

            // Flush do último bloco aberto (não há próximo cabeçalho para acioná-lo dentro do loop).
            if (currentBlock) {
                const speakerColor = currentBlock.isOperador ? "FF6B00" : (currentBlock.isMotorista ? "C0392B" : "333333");
                children.push(new Paragraph({
                    children: [
                        new TextRun({ text: currentBlock.timestamp, size: 20, font: "Consolas", color: "333333" }),
                        new TextRun({ text: " " }),
                        new TextRun({ text: currentBlock.speaker, bold: true, size: 22, color: speakerColor })
                    ],
                    spacing: { after: 40 }
                }));
                children.push(new Paragraph({
                    children: [new TextRun({ text: currentBlock.text, size: 22, italics: currentBlock.isMotorista })],
                    spacing: { after: 240 }
                }));
            }
        } else {
            // Conteúdo não-transcrição: uma linha por parágrafo, removendo `**` do negrito Markdown
            // (o docx não interpreta Markdown, então o marcador seria exibido literalmente).
            linhas.forEach(linha => {
                children.push(new Paragraph({ children: [new TextRun({ text: linha.replace(/\*\*/g, ''), size: 22 })], spacing: { after: 100 } }));
            });
        }

        // Rodapé: separador (borda superior) + linha de copyright centralizada.
        children.push(new Paragraph({ border: { top: { color: 'E0E0E0', size: 6, style: BorderStyle.SINGLE } }, spacing: { before: 400 } }));
        children.push(new Paragraph({ children: [new TextRun({ text: 'nstech © 2026', size: 18, color: '666666' })], alignment: AlignmentType.CENTER }));

        const doc = new Document({ sections: [{ children: children }] });

        // Serializa e baixa (mesmo padrão de exportarWord).
        Packer.toBlob(doc).then(docBlob => {
            const blob = new Blob([docBlob], { type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' });
            // Carimbo AAAAMMDD a partir do ISO date.
            const timestamp = new Date().toISOString().slice(0, 10).replace(/-/g, '');
            // Nome do arquivo: tipo em minúsculas com espaços -> '_' (fallback 'doc').
            const filename = `${item.tipo?.toLowerCase().replace(/\s+/g, '_') || 'doc'}_${timestamp}.docx`;
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
            toast.success('Word gerado!');
        });
    } catch (err) {
        console.error('Erro ao gerar Word:', err);
        toast.error('Erro ao gerar Word: ' + err.message);
    }
}



