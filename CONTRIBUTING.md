# Contribuire

Grazie per l'interesse. Prima di aprire una pull request leggi queste regole.

## Prima di iniziare

1. Per modifiche non banali apri una issue e descrivi il cambiamento atteso.
2. Lavora su un ramo dedicato nato da `main`.
3. Leggi [SPEC.md](SPEC.md): è la fonte delle regole e del piano tecnico.

## Regole del codice

- Nessuna libreria di terzi nel prodotto: solo .NET e le API del sistema.
- La build tratta gli avvisi come errori: non deve comparire alcun avviso.
- Vietate le soppressioni: `#pragma warning disable`, `SuppressMessage`,
  `NOSONAR`, catch vuoti e fallback che nascondono errori. Se un controllo
  fallisce, correggi la causa.
- Ogni modifica a `src/Core` arriva con i test MSTest che la coprono.
- Validazione input, output encoding e messaggi di errore senza dettagli
  sensibili: seguire NIST CSF 2.0 e SSDF (SP 800-218).
- Nessun segreto hardcoded nei sorgenti o nei file di configurazione.

## Verifica locale

Serve l'SDK .NET indicato in `global.json`.

```sh
dotnet build EdexUI.slnx -c Release
dotnet test EdexUI.slnx -c Release
dotnet list EdexUI.slnx package --vulnerable --include-transitive
python3 -m unittest discover -s tests/tools
```

## Commit e pull request

- Commit piccoli, firmati GPG, con messaggio in italiano chiaro.
- Un argomento per commit; nessuna riga `Co-authored-by`.
- Compila il modello della pull request e attendi la CI verde (test su Windows,
  Linux e macOS, SonarCloud, Codacy, Socket).
- Aggiorna `CHANGELOG.md` quando cambia il comportamento.

Partecipando accetti il [codice di condotta](CODE_OF_CONDUCT.md).
