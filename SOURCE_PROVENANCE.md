# Kaynak kökeni

Temel sürüm Windows Bakım Merkezi v4.4'tür. Windows Bakım 3.2 iş akışlarının tarihsel atfı Hugo ZED / Oğuzhan D. olarak uygulamada korunur.

| Dağıtılan konum | Kaynak | Sabit sürüm | Aktarım |
| --- | --- | --- | --- |
| Veri/BT.ps1, readiness bölümü | [WinUtil Get-WinUtilEnvironmentReport.ps1](https://github.com/ChrisTitusTech/winutil/blob/2ef5e5dc7852634bc40ad789a3d1115ee3913c41/functions/private/Get-WinUtilEnvironmentReport.ps1) | 2ef5e5dc7852634bc40ad789a3d1115ee3913c41 | MIT uyarlaması. CBS ve WU anahtarları, boş bekleyen dosya kayıtlarının elenmesi ve okunamayan durumun ayrılması |
| Veri/Uygulamalar.tsv | [WinUtil config/applications.json](https://github.com/ChrisTitusTech/winutil/blob/2ef5e5dc7852634bc40ad789a3d1115ee3913c41/config/applications.json) | 2ef5e5dc7852634bc40ad789a3d1115ee3913c41 | v4.4'ten taşınan seçili paket verisi |
| Ayarlar.cs | [WinUtil config/tweaks.json](https://github.com/ChrisTitusTech/winutil/blob/2ef5e5dc7852634bc40ad789a3d1115ee3913c41/config/tweaks.json) | 2ef5e5dc7852634bc40ad789a3d1115ee3913c41 | v4.4'ten taşınan seçili kullanıcı tercihleri |
| WinUtil-MIT.txt, EXE kaynağı Bakim.WinUtil-MIT.txt | [WinUtil LICENSE](https://github.com/ChrisTitusTech/winutil/blob/2ef5e5dc7852634bc40ad789a3d1115ee3913c41/LICENSE) | 2ef5e5dc7852634bc40ad789a3d1115ee3913c41 | Telif ve lisans metni |

Kullanıcının sağladığı winutil-main.zip arşivinin SHA-256 değeri **014CF303F273EBC132B4A4E39BD8FC7F9771E9427259B835D2BBD7134D0C8520**. ZIP son kayıt yorumunda yukarıdaki tam commit SHA bulunur. GitHub commit sayfası bu referansı doğrular. LICENSE, ortam raporu ve iki katalog dosyası arşiv girdisi ile çıkarılmış çalışma kopyası arasında SHA-256 ile eşleşti. Ortam raporu girdisinin SHA-256 değeri **2EC7AC991BB1C4CC51D18B2EFE9BB7AB561A0F9C43E3BC67E8FED1C11A3A082B**. [GitHub commit](https://github.com/ChrisTitusTech/winutil/commit/2ef5e5dc7852634bc40ad789a3d1115ee3913c41)

Uyarlamada WinUtil'in Chocolatey/WinGet keşfi, günlüğü ve tüm tweak raporu bağımlılıkları çıkarıldı. Yeniden başlatma okuması yalnızca yerel HKLM verileriyle çalışır. Dosya yeniden adlandırma değerlerinin içeriği günlüğe eklenmez. Kaynak içindeki yorum ve MIT bildirimi atfı korur. Diğer BT araçları yerleşik Windows API'leri ve komutları üzerine yazıldı.

