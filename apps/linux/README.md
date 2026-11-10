# Linux Bakım Merkezi v0.001

Geliştiren **Oguzhan DUYAR** · Developed by **Oguzhan DUYAR**

Masaüstünde Türkçe/İngilizce, koyu/açık tema, arama, oturum favorileri ve canlı CPU, RAM, takas, disk ve ağ ölçümleri. Aynı çekirdek, grafik oturum olmadan komut satırında da çalışır.

## Başlatma

Python 3.9 veya üzeri gerekir. Grafik arayüz ayrıca Python Tk ve çalışan bir X11 veya XWayland oturumu ister.

~~~sh
python3 LinuxBakimMerkezi-v0.001.pyz gui
python3 LinuxBakimMerkezi-v0.001.pyz --lang en --json info
python3 LinuxBakimMerkezi-v0.001.pyz diagnose --scenario network
~~~

Dağıtımlar arası kurucu için [kurulum kılavuzuna](packaging/KURULUM.md) bakın. Kurucu uygulamayı kullanıcının dizinine yerleştirir ve varsa eksik Python/Tk paketlerini yerel paket yöneticisiyle kurar.

Debian ailesinde yerel paket deneme sanal makinesine şu şekilde kurulabilir. APT varsayılan olarak önerilen grafik arayüz bağımlılığını da kurar.

~~~sh
sudo apt install ./linuxbakim_0.001-2_all.deb
linuxbakim gui
linuxbakim health
~~~

| Komut | İşlev |
|---|---|
| `linuxbakim list` | Araç kimlikleri ve adları |
| `linuxbakim info` | Dağıtım, kernel, mimari, init ve paket yöneticisi |
| `linuxbakim diagnose --scenario slow` | Seçilen sorun için kontrol dizisi |
| `linuxbakim run package-verify` | Tek bir aracı çalıştırır |
| `linuxbakim updates --check` | Mevcut önbellekten güncelleme bilgisi |
| `linuxbakim security --audit` | Yerel güvenlik göstergeleri |
| `linuxbakim target 127.0.0.1 --port 22` | Bir hedefte tek TCP portunu dener |
| `linuxbakim --json monitor --samples 5` | Beş kaynak ölçümünü tek JSON belgesinde döndürür |
| `linuxbakim report --output inceleme.json` | Ham çıktı içermeyen yerel özet |
| `linuxbakim repair apt-dependencies` | APT bağımlılık onarımı önizlemesi |

`--lang en` ve `--json` komuttan önce gelir. Çıkış kodları `0` tamamlandı, `1` inceleme veya kısmi erişim, `2` hata, `3` kullanılamıyor, `130` iptal. Tamamlandı durumu komutun çalıştığını belirtir. Ayrıntılı çıktı, IP adresleri ve günlük satırları gibi makineye özgü bilgiler içerebilir.

## Onarım kapsamı

Sistem değiştiren tek işlev, desteklenen yerel Debian ailesi kurulumlarında APT bağımlılık onarımıdır. `linuxbakim repair apt-dependencies --apply` ile etkileşimli önizleme, açık onay ve paket veritabanı denetimi kullanılır. Sistem çapında geri alma bulunmadığından gerçek makineden önce atılabilir bir Linux VM’de doğrulanmalıdır. Yetkili adım otomatik testlerde taklit edilmiştir.

[Uyumluluk](DISTRO_COMPATIBILITY.md) · [Özellikler](FEATURE_MATRIX.md) · [Testler](TEST_RESULTS.md)
