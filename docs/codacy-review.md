# Revisione finding Codacy — edex-ui

Data: 2026-11-22. Fonte: API Codacy v3, `gh/LightYagami28/edex-ui`.
Metodo: `POST .../issues/overview` e `POST .../issues/search` con cursori, livelli Error/High/Warning/Info.

## Conteggi

| Livello | Totale |
| --- | ---: |
| Warning | 57 |
| Error | 1 |
| Info | 119 |
| **Totale** | **177** |
| belowThreshold | 177 |
## Falsi positivi accertati

- **`SonarCSharp_S1451` (41)** — `src/Core/Boot/BootSequence.cs:1`, `src/Core/Pty/WindowsPty.cs:1`, `src/Core/Geo/Globe.cs:1`: tutti i file iniziano con `// SPDX-License-Identifier: GPL-3.0-only`. La regola si aspetta un formato di copyright diverso; l'obbligo di licenza e' gia' soddisfatto.
- **`SonarCSharp_S3990` (41)** — convenzione CLS-compliance applicata ad assembly interni: nessun impatto funzionale.
- **`Agentlinter_clarity_undefined-term` (11)** — regola per file di istruzioni agentiche applicata a documentazione e codice C#.

## Finding reali

- `markdownlint_MD022` (6), `MD013` (15): formattazione documentazione.
- `SonarCSharp_S1128` (7), `S3908` (6), `S3264` (5), `S2333` (5): stile C#.
- `Lizard_file-nloc-critical` — `src/Core/Terminal/TerminalBuffer.cs:1`: 1053 righe non-commento, refactor strutturale da PR dedicata.

Nessuna modifica a configurazioni o soglie; nessuna soppressione.

