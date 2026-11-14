# Linux: .rpm (Fedora, openSUSE)

Stato: NON VERIFICATO. Serve Linux con `rpmbuild`, che non c'è su questo PC.

## Cosa produce
`edex-ui-<versione>-1.<dist>.x86_64.rpm` e `.aarch64.rpm` (binario, da tar.gz standalone). Installa `/usr/bin/edex-ui`, il file `.desktop` e l'icona. Lo spec disattiva `strip` e debuginfo perché lo strip corrompe un eseguibile single-file .NET.

## Prerequisiti
Fedora, openSUSE o container equivalente con `rpm-build` e `rpmdevtools` (`spectool`), architettura da costruire (o QEMU).

## Costruire
```
bash packaging/build.sh                       # genera dist/linux/rpm/edex-ui.spec con URL e versione
mkdir -p ~/rpmbuild/SOURCES ~/rpmbuild/SPECS
cp packaging/dist/linux/rpm/edex-ui.spec ~/rpmbuild/SPECS/
cp packaging/dist/edex-ui-<versione>-linux-x64.tar.gz ~/rpmbuild/SOURCES/     # oppure -linux-arm64 su aarch64
rpmbuild -bb ~/rpmbuild/SPECS/edex-ui.spec
rpmlint ~/rpmbuild/RPMS/*/edex-ui-*.rpm
```
(In alternativa `spectool -g -R` scarica la sorgente dall'URL della release, quando esiste.)

## Installare, aggiornare, disinstallare
```
sudo dnf install ./edex-ui-<versione>-1.*.rpm      # Fedora; openSUSE: sudo zypper install ./edex-ui-*.rpm
sudo dnf upgrade ./edex-ui-<nuova>-1.*.rpm
sudo dnf remove edex-ui                            # openSUSE: sudo zypper remove edex-ui
```
Dove finiscono i file: `rpm -ql edex-ui`.

## Verificare la somma SHA-256
`sha256sum -c edex-ui-<versione>-linux-x64.tar.gz.sha256` per la sorgente; per l'rpm, `sha256sum` e confronto con il valore pubblicato. Firma del pacchetto: `rpmsign --addsign` con la chiave GPG del proprietario.

## Pubblicare (passi del proprietario)
- Release GitHub: caricare gli rpm firmati.
- Fedora COPR: account Fedora (FAS), creare un progetto su https://copr.fedorainfracloud.org e inviare lo spec/SRPM (`copr-cli build`, token API personale).
- openSUSE Build Service: account su build.opensuse.org, pacchetto con lo spec e le sorgenti.
- Repository ufficiali Fedora: richiede una sorgente costruibile da sorgenti, non un binario.
