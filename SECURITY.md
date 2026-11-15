# Sicurezza

## Versioni supportate

Riceve correzioni di sicurezza solo il ramo `master`, cioè la riscrittura
nativa dell'applicazione. Il vecchio codice Electron (tag
`archivio/electron`) non è più mantenuto.

## Segnalare una vulnerabilità

Non aprire una issue pubblica per una vulnerabilità.

Usa la segnalazione privata di GitHub: scheda **Security** del repository,
poi **Report a vulnerability**
(<https://github.com/LightYagami28/edex-ui/security/advisories/new>).

Sul mirror GitLab apri una issue e spunta **This issue is confidential**.

Nella segnalazione indica:

- versione o commit interessato;
- sistema operativo;
- passi per riprodurre il problema e impatto previsto.

Riceverai una risposta nella stessa segnalazione. La correzione viene
pubblicata su `master` e la segnalazione resta privata fino al rilascio.

## Controlli automatici

- SonarCloud e CodeQL analizzano il codice C# a ogni pull request.
- Dependabot controlla ogni giorno pacchetti NuGet e GitHub Actions.
- Socket controlla le dipendenze nelle pull request.
