# Sicurezza

## Versioni supportate

Riceve correzioni di sicurezza solo il ramo `main`, cioè la riscrittura nativa
dell'applicazione. Il vecchio codice Electron (tag `archivio/electron`) non è
più
mantenuto.

## Segnalare una vulnerabilità

Non aprire una issue pubblica per una vulnerabilità. Usa la segnalazione privata
di GitHub: scheda **Security** del repository, poi **Report a vulnerability**
(<https://github.com/LightYagami28/edex-ui/security/advisories/new>).

Sul mirror GitLab apri una issue riservata (**This issue is confidential**).

Nella segnalazione indica:

- versione o commit interessato;
- sistema operativo;
- passi per riprodurre il problema e impatto previsto.

Non allegare segreti, token o dati personali: la correzione viene pubblicata su
`main` e la segnalazione resta privata fino al rilascio.

## Controlli automatici

- SonarCloud e CodeQL analizzano il codice C# a ogni pull request.
- Dependabot controlla ogni giorno pacchetti NuGet e GitHub Actions.
- Socket controlla le dipendenze nelle pull request.
- `dotnet list package --vulnerable --include-transitive` va eseguito prima di
  ogni rilascio; le GitHub Action devono restare pinnate a SHA completo.

## Riferimenti e framework

La postura segue OWASP Top 10 2025 (A01–A10), OWASP Top 10 for LLM/GenAI 2025
dove presenti componenti AI, NIST CSF 2.0, SSDF (SP 800-218) e, per i sistemi
AI, MITRE ATLAS. Ogni PR verifica: zero errori, zero avvisi, zero soppressioni.

## Postura di sicurezza

- **Least privilege**: l'app non richiede elevazione; le operazioni sui file
  restano nella directory utente.
- **A02 Security Misconfiguration**: l'applicazione non espone server web; se in
  futuro verrà aggiunto un endpoint HTTP, dovrà inviare CSP, HSTS,
    X-Content-Type-Options, X-Frame-Options e Referrer-Policy, con debug e
  directory
  listing disattivati in produzione.
- **Validazione input**: `AppSettings.Validate()` verifica soglie numeriche e
  percorsi assoluti; `SettingsStore.LoadStrict()` segnala errori di lettura e
  valori non validi senza cadere su default silenziosi.
- **Output encoding**: i dati del terminale passano dal parser ANSI che
  interpreta le sequenze di controllo senza eseguirle.
- **Audit logging**: le tracce diagnostic registrano tipo ed errore, mai
  contenuto di terminale, credenziali o percorsi sensibili.
- **Errori senza leak**: i messaggi destinati all'utente non espongono stack
  trace, token o dettagli interni di percorso; in caso di errore si fail-closed
  (A10 Mishandling of Exceptional Conditions).
- **A08 Integrità**: gli aggiornamenti vanno verificati per firma e hash prima
  dell'installazione; vietata la deserializzazione non sicura di dati esterni.
- **A03 Catena di fornitura**: lockfile con hash, GitHub Action pinnate a SHA
  completo, SBOM generato in CI, zero pacchetti deprecati o vulnerabili.
- **A04 Crittografia**: TLS 1.3 per ogni comunicazione in rete; vietati MD5 e
  SHA1 per scopi di sicurezza; chiavi gestite fuori dal repository.
- **A05 Injection**: input validato e output codificato per contesto; nessuna
  concatenazione di comandi o query con dati utente.
- **A06/A07 Progettazione e autenticazione**: threat model, rate limiting,
  policy password e protezione brute-force; l'app non gestisce sessioni web.
- **A09 Logging e alerting**: log audit strutturati senza segreti o PII, con
  alerting sulle condizioni di errore ripetute.
- **Componenti AI**: il progetto non usa LLM/RAG; se introdotti, separare
  istruzioni di sistema da input utente, trattare l'output come non attendibile,
  limitare agency e consumi (token/cost cap, timeout, quota).

## Applicazione delle checklist JARVIS

Le tre checklist in `00 Jarvis/Squadra/Sicurezza/` sono state riviste punto per
punto. Esito per il progetto:

- **Checklist siti web**: non applicabile al prodotto (applicazione desktop
senza
    server web). Applicabili e rispettate: gestione segreti, dipendenze e
  lockfile,
  GitHub Action pinnate, log senza PII, errori generici e rollback. Se in futuro
  verrà aggiunto un endpoint HTTP, gli header HTTPS obbligatori sono specificati
  in questa sezione.
- **Checklist database**: non applicabile: il progetto non usa database.
- **Checklist codice C#**: applicate le sezioni 1 (validazione input), 2 (output
e
    stringhe), 6 (serializzazione), 10 (segreti/configurazione), 11
  (crittografia),
  12 (eccezioni e log), 13 (concorrenza), 14 (NuGet), 15 (build/rilascio),
  16 (desktop), 19 (test/analisi) e 20 (pre-pubblicazione). Non applicabili:
  sezione 3 (SQL), 4/5 (autenticazione e ASP.NET Core), 17 (servizi Windows) e
  18 (interoperabilità nativa: il PTY usa API di sistema tramite wrapper del
  runtime, senza assembly non verificati).

## Eccezioni HTTPS documentate

- `media/logo.svg`, `packaging/windows/**/*.xml` e `*.nuspec` contengono URI di
    namespace XML (`http://www.w3.org/2000/svg`,
  `http://schemas.microsoft.com/...`):
  sono identificatori normalizzati da specifica, non URL richiesti in rete.
  Convertirli romperebbe gli schema; non sono richieste di rete.
- `LICENSE` riporta `<http://fsf.org/>` nel testo originale: il testo della FSF
  vieta modifiche e va conservato verbatim.
