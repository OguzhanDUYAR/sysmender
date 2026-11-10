# Paket ve test üretimi

Kaynak arşivindeki `LinuxBakimMerkezi` dizininde:

~~~sh
python3 -W error -m unittest discover -s tests -v
python3 build_release.py --out dist
python3 dist/LinuxBakimMerkezi-v0.001.pyz --version
~~~

Derleyici ve PyPI indirmesi gerekmez. Sabit Python/sıkıştırma sürümüyle aynı girdiler aynı arşiv baytlarını üretir. SHA256 manifesti dosya bütünlüğünü denetler. Paketler imzasızdır.

~~~sh
python3 -m tests.linux_smoke
python3 -m tests.gui_smoke
dpkg-deb --info dist/linuxbakim_0.001-1_all.deb
dpkg-deb --contents dist/linuxbakim_0.001-1_all.deb
~~~

Konuk sistemde kurmak için `sudo apt install ./dist/linuxbakim_0.001-1_all.deb`, kaldırmak için `sudo apt remove linuxbakim`. Paket ayrıcalıklı yükleme betiği içermez. Güncelleme, yeni paketin açıkça kurulmasıyla yapılır. GUI için dağıtımın Tk paketi gerekir. Yerel onarım kayıtları `~/.local/state/linuxbakim/` altında bulunur.

Arch için `LinuxBakimMerkezi-PKGBUILD` dosyasını `PKGBUILD` adıyla kaynak ZIP’in yanına koyun ve normal kullanıcı olarak `makepkg` çalıştırın. RPM için kaynak ZIP’i RPM SOURCES dizinine kopyalayın ve Fedora türü ortamda `rpmbuild -bb packaging/linuxbakim.spec` kullanın. Arch ve RPM tarifleri henüz yerel Linux derlemesinden geçmedi. Flatpak host erişimi ve AppImage libc sınırları ayrı değerlendirme gerektirir.
