# Note Codacy

## Falsi positivi noti

- `SonarCSharp_S2333` (`'partial' e' gratuitous`): le classi segnalate
  dichiarano metodi `partial`. Rimuovere la parola chiave rende la build
  impossibile (CS0751), quindi la dichiarazione e' necessaria.
- `SonarCSharp_S1451`: il progetto usa `// SPDX-License-Identifier:
  GPL-3.0-only` come intestazione; `sonar.cs.fileHeader` e' stato impostato
  su quel valore in `sonar-project.properties`.
- `SonarCSharp_S3990`: l'attributo `CLSCompliant` e' ora dichiarato a livello
  di assembly tramite `Directory.Build.props`; l'assemblaggio usa tipi
  `uint`/`ulong` nelle API pubbliche, quindi il valore dichiarato e' `false`.

Nessuna segnalazione e' stata chiusa come "accepted" o "won't fix".
