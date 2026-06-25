/**
 * Serviço de Exportação
 * Copia textos e gera PDFs
 */

import { TITULOS_PDF, HTML2PDF_OPTIONS } from '../config/constants.js';
import { capitalize, formatarDataHora, refreshIcons } from '../core/utils.js';
import { toast } from '../ui/toast.js';

/**
 * Copia texto do output para clipboard
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
 * Exporta conteúdo para PDF
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
 * Exporta item salvo para PDF
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
 * Extrai dados estruturados da transcrição diretamente do DOM
 * Lê os elementos HTML em vez do texto bruto
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
        // Fallback: texto simples sem formatação HTML
        const rawText = container.innerText;
        const lines = rawText.split('\n').filter(l => l.trim());

        let currentBlock = null;

        lines.forEach(line => {
            // Tenta extrair timestamp e speaker
            const match = line.match(/^\[(\d{1,3}:\d{2}(?::\d{2})?)\]\s*([^:]+):\s*(.*)$/);

            if (match) {
                if (currentBlock) blocks.push(currentBlock);

                const speaker = match[2].trim();
                currentBlock = {
                    timestamp: `[${match[1]}]`,
                    speaker: speaker,
                    text: match[3].trim(),
                    isOperador: speaker.toLowerCase().includes('operador'),
                    isMotorista: speaker.toLowerCase().includes('motorista')
                };
            } else if (currentBlock) {
                // Linha de continuação
                currentBlock.text += ' ' + line.trim();
            } else {
                // Linha solta (título, etc)
                blocks.push({ type: 'text', content: line });
            }
        });

        if (currentBlock) blocks.push(currentBlock);
    }

    return blocks;
}

/**
 * Exporta conteúdo para Word (DOCX)
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

        // Separador
        children.push(new Paragraph({ border: { bottom: { color: 'CCCCCC', size: 6, style: BorderStyle.SINGLE } }, spacing: { after: 300 } }));

        // Conteúdo
        if (type === 'transcricao') {
            const blocks = extractTranscriptionFromDOM(output);

            blocks.forEach(block => {
                if (block.type === 'text') {
                    // Texto genérico
                    children.push(new Paragraph({ children: [new TextRun({ text: block.content, size: 22 })], spacing: { after: 120 } }));
                } else {
                    // Bloco de fala estruturado
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
            // Se for áudio, tenta incluir tabela de dados identificados
            if (type === 'audio') {
                const dadosView = document.getElementById('dadosIdentificadosView');
                if (dadosView && dadosView.innerText.trim()) {
                    children.push(new Paragraph({
                        children: [new TextRun({ text: "DADOS IDENTIFICADOS", bold: true, size: 24, color: "FF6B00" })],
                        spacing: { before: 200, after: 200 }
                    }));

                    const dadosLines = dadosView.innerText.split('\n').filter(l => l.trim());
                    dadosLines.forEach(linha => {
                        children.push(new Paragraph({ children: [new TextRun({ text: linha, size: 22 })], spacing: { after: 120 } }));
                    });

                    children.push(new Paragraph({ border: { bottom: { color: 'CCCCCC', size: 6, style: BorderStyle.SINGLE } }, spacing: { after: 300 } }));
                }
            }

            // Conteúdo genérico (laudos)
            const linhas = output.innerText.split('\n').filter(l => l.trim());
            linhas.forEach(linha => {
                const isHeader = linha.startsWith('#');
                if (isHeader) {
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

        Packer.toBlob(doc).then(docBlob => {
            const blob = new Blob([docBlob], { type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' });
            const timestamp = new Date().toISOString().slice(0, 10).replace(/-/g, '');
            const filename = `${type === 'transcricao' ? 'transcricao' : 'laudo'}_${type}_${timestamp}.docx`;
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
 * Exporta item salvo para Word (DOCX)
 */
export function exportarItemWord(item) {
    if (!item) return;
    if (typeof docx === 'undefined') {
        toast.error('Biblioteca DOCX não carregada.');
        return;
    }

    try {
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

        // Conteúdo - parse simples para itens salvos
        const isTranscription = item.tipo?.toLowerCase().includes('transcri');
        const conteudo = item.conteudo || '';
        const linhas = conteudo.split('\n').filter(l => l.trim());

        if (isTranscription) {
            let currentBlock = null;

            linhas.forEach(line => {
                const match = line.match(/^\[(\d{1,3}:\d{2}(?::\d{2})?)\]\s*([^:]+):\s*(.*)$/);

                if (match) {
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

                    const speaker = match[2].trim();
                    currentBlock = {
                        timestamp: `[${match[1]}]`,
                        speaker: speaker,
                        text: match[3].trim(),
                        isOperador: speaker.toLowerCase().includes('operador'),
                        isMotorista: speaker.toLowerCase().includes('motorista')
                    };
                } else if (currentBlock) {
                    currentBlock.text += ' ' + line.trim();
                } else {
                    children.push(new Paragraph({ children: [new TextRun({ text: line, size: 22 })], spacing: { after: 100 } }));
                }
            });

            // Último bloco
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
            linhas.forEach(linha => {
                children.push(new Paragraph({ children: [new TextRun({ text: linha.replace(/\*\*/g, ''), size: 22 })], spacing: { after: 100 } }));
            });
        }

        // Rodapé
        children.push(new Paragraph({ border: { top: { color: 'E0E0E0', size: 6, style: BorderStyle.SINGLE } }, spacing: { before: 400 } }));
        children.push(new Paragraph({ children: [new TextRun({ text: 'nstech © 2026', size: 18, color: '666666' })], alignment: AlignmentType.CENTER }));

        const doc = new Document({ sections: [{ children: children }] });

        Packer.toBlob(doc).then(docBlob => {
            const blob = new Blob([docBlob], { type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' });
            const timestamp = new Date().toISOString().slice(0, 10).replace(/-/g, '');
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



