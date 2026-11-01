# Sviluppo

## Test e coverage

I test girano su Windows, Linux e macOS nella CI. Ogni sistema misura solo il codice che esegue, quindi `tools/coverage-to-sonar.py` unisce i tre report
(una riga è coperta se almeno un sistema la copre) e produce `sonar-coverage.xml` per SonarCloud.

In locale su Windows, Linux si prova con Docker:

```
docker run --rm -v "<repo>:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0 bash -c "cp -r /src /work && cd /work && dotnet test"
```

## Regole

- Nessuna libreria di terzi nel prodotto. Per i test solo MSTest e Microsoft.CodeCoverage.
- `TreatWarningsAsErrors` e analizzatori .NET attivi: la build non passa con avvisi.
- Nessuna soppressione di avvisi (`#pragma warning disable`, `SuppressMessage`, `NOSONAR`).
- Ogni lettura dal sistema che può fallire passa da `Safe.Run` o da un'interfaccia sostituibile nei test.

## CI

`.github/workflows/ci.yml`: test su tre sistemi, poi analisi SonarCloud (richiede il segreto `SONAR_TOKEN`; senza, il passo viene saltato).
