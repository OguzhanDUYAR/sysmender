# Geliştirici kılavuzu · v0.001

Dağıtım tek EXE'dir. Kaynak C# 5 / Windows Forms ve Windows PowerShell 5.1 kullanır. Uygulama .NET Framework 4.8/4.8.1 ile Windows 10 22H2 ve Windows 11'i hedefler. Derleme ek paket veya NuGet geri yüklemesi yapmaz.

## Derleme ve doğrulama

1. Kaynak ZIP'i yerel bir klasöre çıkarın.
2. PowerShell'de `.\Derle.ps1 -Hedef .\WindowsBakimMerkezi-v0.001-Duzeltme1.exe` çalıştırın.
3. `.\Derle.ps1 -Dogrula` regresyon kontrollerini ve arayüz görüntülerini ayrı bir Doğrulama klasörüne üretir.
4. Uygulama ürün sürümü `0.001` olarak görünür. Windows'un dört parçalı sayısal dosya sürümü `0.0.1.1` olarak kaydedilir.

## Modüller

| Dosya | Sorumluluk |
| --- | --- |
| Program.cs | Dil/tema ve ayrı yönetici oturumu, tek örnek ve sürüm |
| Pencere.cs, BtPanel.cs | Ekranlar, seçimler, inceleme planı, sonuç ve erişilebilir araçlar |
| Islem.cs | Job, sonuç türleri, arka plan sırası, durdurma, çıktı sınırları ve Windows komutları |
| BtAraclari.cs, Veri/BT.tsv, Veri/BT.ps1 | Sabit araç kataloğu, yerleşik komut seçimi ve ayrıştırılmış betik gövdesi |
| YerelTani.cs | Doğrudan registry, servis, ağ ve günlük okuma |
| DosyaAraclari.cs, GeciciDosyalar.cs | Sınırlı dosya inceleme ve dizin/dosya tanıtıcısı doğrulaması |
| DestekAkisi.cs | Belirti profilleri, ayrı onarım ve kontrol planları |
| CanliIzleme.cs | CPU/RAM, disk boş alanı ve ayrı ağ sayaçları |
| DestekRaporu.cs, RaporPaneli.cs | Rapor serileştirme, salt okunur geçmiş ve sonuç farkları |
| CalismaAlaniArama.cs, Gorunum.cs | TR/EN çalışma alanı araması ve iki tema |

## Yeni araç ekleme

`Veri/BT.tsv` satırında kimlik, Türkçe ve İngilizce kategori/ad/açıklama, yönetici gereksinimi ve değişiklik işaretini tanımlayın. Kimlik benzersizdir. Değişiklik işareti olan bir BT aracı yönetici oturumunu gerektirir. Etki sütunundaki sınıflandırmayı yeni eyleme göre güncelleyin.

Yerleşik komut için `ItTools.Native` içine sabit EXE ve parametre koyun. Kullanıcı girdisini kabuk metni yapmayın. Ağ hedefi gibi bir girdi gerekiyorsa doğrulanmış DNS adı veya kanonik IP ile tek hedefe bağlayın. CIM gerekiyorsa `Veri/BT.ps1` içine aynı kimlikle bir gömülü araç bloğu ekleyin. Yönetici eylemi yalnızca sabit erişim kontrolü ve uygulama içi plan onayıyla bağlanır.

`ResultKind.Done` bir komut çalıştığında yalnızca belgelenmiş kapsamı ifade eder. Okunamayan veya kısmi değerler, işlem sonrası kanıt eksikliği ve bulgular uygun şekilde Review, Failed veya Skipped ile döndürülür. Onarımın sonuç doğrulaması ve olası geri alma ihtiyacı ayrı düşünülür.

Katalog ve betikler derleme sırasında EXE'ye alınır. Dış kaynak betik veya DLL eklentisi yükleme hattı mevcut değildir. Güvenilir bir dış eklenti sistemi ayrı imza ve politika tasarımı gerektirir.

## Test düzeni

`Testler` kaynakları yalnızca `-Dogrula` derlemesinde çalışır. Gerçek yerel okumaları örnek servis ve kayıt girdilerinden ayırın. Gerçek yönetici değişiklikleri için izole Windows test makinesi kullanın. Rapor ve günlük çıktılarını cihaz bilgisi içerebilecek test klasöründe tutun.



