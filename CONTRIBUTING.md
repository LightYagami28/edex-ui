# Contribuire

Grazie per l'interesse. Prima di aprire una pull request leggi queste regole.

## Prima di iniziare

1. Per modifiche non banali apri prima una issue e descrivi il cambiamento.
2. Lavora su un ramo dedicato nato da `main`.
3. Leggi [SPEC.md](SPEC.md): è la fonte delle regole e del piano.

## Regole del codice

- Nessuna libreria di terzi nel prodotto: solo .NET e le API del sistema.
- La build tratta gli avvisi come errori: non deve comparire alcun avviso.
- Nessuna soppressione di avvisi (`#pragma warning disable`,
  `SuppressMessage`, `NOSONAR`).
- Ogni modifica a `src/Core` arriva con i test MSTest che la coprono.

## Verifica locale

Serve l'SDK .NET indicato in `global.json`.

```sh
dotnet build -c Release
dotnet test -c Release
python3 -m unittest discover -s tests/tools
```

## Commit e pull request

- Commit piccoli, firmati GPG, con messaggio in italiano chiaro.
- Compila il modello della pull request e attendi la CI verde
  (test su Windows, Linux e macOS, SonarCloud, Codacy, Socket).
- Le pull request vengono riviste e possono richiedere modifiche.

Partecipando accetti il [codice di condotta](CODE_OF_CONDUCT.md).
