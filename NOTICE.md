# NOTICE — Attribuzione e Licenze

Questo progetto contiene codice e concetti derivati dal software open-source originale eDEX-UI.

## 1. Progetto Originale eDEX-UI
- **Autore originale**: Gabriel Saillard (git-moss) e i contributori del progetto eDEX-UI.
- **Repository di riferimento**: `https://github.com/GitSquared/edex-ui` (progetto archiviato dal manutentore).
- **Licenza originale**: GNU General Public License v3.0 (GPL-3.0).

## 2. Reimplementazione Nativa .NET
- Il codice presente in `src/Core` e `src/Windows` è una riscrittura completa da zero in C# / .NET 11, priva di Electron, Node.js o librerie JavaScript terze.
- **Licenza applicata**: GNU General Public License v3.0 (GPL-3.0), in conformità con la licenza del progetto originario (testo integrale in LICENSE).
- **Autore della riscrittura**: Alessandro Maule (Light Yagami).

## 3. Dipendenze e Componenti Terzi
- **Piattaforma di esecuzione**: Microsoft .NET SDK 11.0 (MIT License).
- **Interfaccia e Sottosistemi OS**:
  - Windows: Windows Pseudo Console API (ConPTY) e Windows Presentation Foundation (WPF) / DirectX integration.
  - Nessuna dipendenza NuGet esterna a runtime; solo librerie base del runtime .NET.
