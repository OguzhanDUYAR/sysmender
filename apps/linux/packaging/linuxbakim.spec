Name:           linuxbakim
Version:        0.001
Release:        1%{?dist}
Summary:        Linux maintenance and diagnostics console
License:        LicenseRef-LocalDelivery
Source0:        LinuxBakimMerkezi-Source.zip
BuildArch:      noarch
BuildRequires:  python3 >= 3.9
BuildRequires:  unzip
BuildRequires:  desktop-file-utils
Requires:       python3 >= 3.9
Suggests:       python3-tkinter

%description
Local Linux diagnostics, monitoring and maintenance from the desktop or CLI.

%prep
%setup -q -n LinuxBakimMerkezi

%build
%{__python3} build_release.py --portable-only --out build

%check
%{__python3} -W error -m unittest discover -s tests
desktop-file-validate packaging/org.oguzhanduyar.LinuxBakim.desktop

%install
install -Dm755 build/LinuxBakimMerkezi-v%{version}.pyz %{buildroot}%{_bindir}/linuxbakim
install -Dm644 packaging/org.oguzhanduyar.LinuxBakim.desktop %{buildroot}%{_datadir}/applications/org.oguzhanduyar.LinuxBakim.desktop
install -Dm644 packaging/org.oguzhanduyar.LinuxBakim.svg %{buildroot}%{_datadir}/icons/hicolor/scalable/apps/org.oguzhanduyar.LinuxBakim.svg

%files
%license packaging/copyright
%doc README.md
%{_bindir}/linuxbakim
%{_datadir}/applications/org.oguzhanduyar.LinuxBakim.desktop
%{_datadir}/icons/hicolor/scalable/apps/org.oguzhanduyar.LinuxBakim.svg
