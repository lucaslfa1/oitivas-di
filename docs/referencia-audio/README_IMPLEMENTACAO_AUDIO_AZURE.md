# Referencia de Implementacao de Audio (Azure-Only)

## Objetivo
Padronizar uma implementacao de transcricao de audio focada em Azure, com:
- `Azure Speech-to-Text` como motor principal (`pt-BR`, diarizacao, phrase list).
- `Azure OpenAI Whisper` como fallback automatico.
- Saida em formato de oitiva: `[mm:ss] Interlocutor: frase`.

## Arquitetura recomendada
1. Cliente envia audio para `POST /api/transcrever`.
2. Orquestrador tenta `Azure Speech-to-Text` (Fast Transcription API).
3. Se resultado vier fraco ou falhar, cai para `Azure Whisper`.
4. Backend retorna transcricao consolidada com `fonte: "azure"`.

## Estrutura de codigo (exemplo deste projeto)
- Configuracao: `Backend/Configuration/AzureSpeechSettings.cs`
- STT principal: `Backend/Services/AzureFastTranscricaoService.cs`
- Fallback Whisper: `Backend/Services/AzureWhisperService.cs`
- Orquestracao: `Backend/Services/TranscricaoOrquestradorService.cs`
- Registro DI: `Backend/Program.cs`
- Endpoint: `Backend/Controllers/TranscricaoController.cs`

## Passo a passo de implementacao
1. Criar classe de settings com blocos separados:
- Credenciais/endpoints do Whisper.
- Credenciais/endpoints do Speech-to-Text.
- Flags (`Enabled`, `SpeechToTextEnabled`) e parametros (`locale`, diarizacao, `maxSpeakers`, `phraseList`).

2. Implementar servico `AzureFastTranscricaoService`:
- Montar endpoint:
  - `https://{region}.api.cognitive.microsoft.com/speechtotext/transcriptions:transcribe?api-version={version}`
- Enviar `multipart/form-data` com:
  - `audio` (arquivo)
  - `definition` (JSON com `locales`, `diarization`, `phraseList`)
- Ler `phrases` e montar texto com timestamps.
- Mapear speaker diarizado para `Operador BAS` e `Motorista`.
- Aplicar correcoes de texto (ex.: Opentech).

3. Atualizar `TranscricaoOrquestradorService`:
- Prioridade: STT -> Whisper.
- Adicionar validacao de qualidade da transcricao STT (vazio, repeticao dominante, densidade muito baixa).
- Se validar, retorna STT.
- Se falhar/baixa qualidade, tenta Whisper.

4. Atualizar controller:
- Retornar `new TranscricaoResponse(transcricao, "azure")` (fonte generica).

5. Ajustar frontend (opcional):
- Loading sem nome de provider (apenas barra/percentual).

## Configuracao segura
- Nunca commitar chaves reais.
- Usar `appsettings.Local.json` (ignorado no git) e/ou variaveis de ambiente:
  - `AZURE_SPEECH_TO_TEXT_KEY`
  - `AZURE_SPEECH_TO_TEXT_REGION`

Template pronto:
- `docs/referencia-audio/appsettings.Local.audio.example.json`

## Checklist de validacao
1. `dotnet build Backend/SinistroAPI.csproj`
2. Subir backend local (`http://localhost:5252`)
3. Testar `POST /api/transcrever` com audio curto e audio longo
4. Confirmar:
- Timestamps no formato `[mm:ss]`
- Interlocutor identificado (Operador/Motorista)
- Correcoes de termos criticos (ex.: Opentech)
- Sem mensagens de provider no loading da UI

## Troubleshooting rapido
- `STT retorna vazio`: verificar `SpeechToTextKey`, `SpeechToTextRegion`, `api-version` e MIME do arquivo.
- `Muitos erros de speaker`: aumentar contexto de mesclagem e ajustar heuristica de mapeamento por `speakerId`.
- `Repeticao de frase`: manter filtro de repeticao dominante no orquestrador.
- `Timeout`: aumentar timeout de `HttpClient` para audios longos.
