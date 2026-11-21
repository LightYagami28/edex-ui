# Sicurezza

## Versioni supportate

Riceve correzioni di sicurezza solo il ramo `main`, cioè la riscrittura nativa
dell'applicazione. Il vecchio codice Electron (tag `archivio/electron`) non è più
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

## Postura di sicurezza

- **Least privilege**: l'app non richiede elevazione; le operazioni sui file
  restano nella directory utente.
- **Validazione input**: `AppSettings.Validate()` verifica soglie numeriche e
  percorsi assoluti; `SettingsStore.LoadStrict()` segnala errori di lettura e
  valori non validi senza cadere su default silenziosi.
- **Output encoding**: i dati del terminale passano dal parser ANSI che
  interpreta le sequenze di controllo senza eseguirle.
- **Audit logging**: le tracce diagnostic registrano tipo ed errore, mai
  contenuto di terminale, credenziali o percorsi sensibili.
- **Errori senza leak**: i messaggi destinati all'utente non espongono stack
  trace, token o dettagli interni di percorso.
- **Integrità**: build e test con zero warning, dipendenze verificabili e
  ~/.nuget senza pacchetti deprecati noti.
