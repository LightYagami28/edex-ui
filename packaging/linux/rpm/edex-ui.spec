Name:           edex-ui
Version:        @VERSION@
Release:        1%{?dist}
Summary:        Terminale a schermo intero in stile fantascienza
License:        GPL-3.0-only
URL:            https://github.com/LightYagami28/edex-ui
ExclusiveArch:  x86_64 aarch64

%ifarch x86_64
%global rid linux-x64
%endif
%ifarch aarch64
%global rid linux-arm64
%endif

Source0:        @URL_BASE@/edex-ui-%{version}-%{rid}.tar.gz

%global debug_package %{nil}
%global __strip /bin/true

%description
eDEX-UI nativo (.NET) con pannelli di sistema: CPU, RAM, rete, processi,
file e tastiera su schermo. Eseguibile autonomo, non richiede il runtime .NET.

%prep
%setup -q -n edex-ui-%{version}-%{rid}

%build

%install
install -Dm755 edex-ui %{buildroot}%{_bindir}/edex-ui
install -Dm644 share/applications/edex-ui.desktop %{buildroot}%{_datadir}/applications/edex-ui.desktop
install -Dm644 share/icons/hicolor/256x256/apps/edex-ui.png %{buildroot}%{_datadir}/icons/hicolor/256x256/apps/edex-ui.png

%files
%license LICENSE
%{_bindir}/edex-ui
%{_datadir}/applications/edex-ui.desktop
%{_datadir}/icons/hicolor/256x256/apps/edex-ui.png

%changelog
* Wed Nov 11 2026 Light Yagami <maule2703@gmail.com> - @VERSION@-1
- Pacchetto binario dal tar.gz standalone
