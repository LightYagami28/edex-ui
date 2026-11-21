# NOTICE — attribuzioni e licenze

Questo progetto contiene codice e concetti derivati dal software open source
originale eDEX-UI.

## 1. Progetto originale

- **Autori originali**: Gabriel Saillard (git-moss) e i contributori di eDEX-UI.
- **Repository di riferimento**: <https://github.com/GitSquared/edex-ui>
  (progetto archiviato dal manutentore).
- **Licenza originale**: GNU General Public License v3.0 (GPL-3.0).

## 2. Riscrittura nativa

Il codice in `src/Core` e `src/Windows` è una riscrittura completa in C# / .NET
11, senza Electron, Node.js o librerie JavaScript di terze parti. La licenza
applicata è GPL-3.0-only, in conformità con il progetto originario; il testo
integrale è in [LICENSE](LICENSE). Autore della riscrittura: Alessandro Maule
(Light Yagami).

## 3. Componenti di terze parti

- **Runtime**: Microsoft .NET SDK 11.0 (licenza MIT).
- **Windows**: Windows Pseudo Console API (ConPTY) e Windows Presentation
  Foundation (WPF) con integrazione DirectX.
- **Test**: MSTest SDK e Microsoft.Testing.Platform (licenza MIT).
- **Strumenti di analisi**: SonarCloud, CodeQL, Codacy e Socket, usati solo in CI.

Non ci sono dipendenze NuGet a runtime nel prodotto: le dipendenze elencate
riguardano esclusivamente la fase di sviluppo e di verifica.

## 4. Marchi

I nomi e i marchi citati appartengono ai rispettivi proprietari. L'uso è
finale a descrivere l'integrazione tecnica e non implica affiliazione.
