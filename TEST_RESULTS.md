# Windows Bakım Merkezi v0.001 · Günlük düzeltmesi

Ürün sürümü **0.001**, Windows dosya sürümü **0.0.1.1**.

## Sorunun nedeni

Ekran görüntüsündeki hata DISM veya SFC başlamadan önce oluştu. Mevcut C:\Windows\Logs\WindowsBakimMerkezi klasöründe Administrators ve SYSTEM dışında kişisel Windows hesabına da doğrudan tam denetim verilmişti. Yükseltilmiş işlemlerin kullandığı günlük ve geçici dosya konumuna yönetici yetkisi olmadan erişilebildiği için uygulama klasörü reddetti.

## Düzeltme

Uygulama mevcut klasör güvenliyse onu kullanır. Mevcut klasör denetimi geçmezse C:\Windows\Logs\WindowsBakimMerkezi-Korumali konumunu hazırlar. Yeni dizinin erişim izinleri kalıtımı kapalı olarak yalnızca Administrators ve SYSTEM için tanımlanır. Uygulama dizinin sahipliğini, izinlerini ve bağlantı olmadığını kullanımdan önce tekrar doğrular. Eski klasör ve raporlar yerinde kalır.

İşlem kuyruğuna doğrulanmış dizin döndürülür ve günlük oraya yazılır. Yönetici komutlarının TEMP/TMP konumu, raporlar ve sürücü yedekleri de seçilen dizini kullanır. Güvenli günlük açılamazsa kuyruk başlamaz ve sorunlu klasörün nedeni gösterilir. [Microsoft dizin oluşturma ve erişim izinleri belgeleri](https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.createdirectory?view=netframework-4.8.1)

## Doğrulama

- Gerçek makinenin izinleri salt okunur olarak incelendi. Yeni denetim C:\Windows ve C:\Windows\Logs üst klasörlerini kabul etti ve kişisel tam denetim içeren eski günlük klasörünü reddetti.
- Örnek eski klasör ve izin tanımıyla kuyruk çalıştırıldı. Alternatif dizinde işlem günlüğü oluşturuldu, örnek okuma adımı tamamlandı ve eski günlük aynı içerikle yerinde kaldı.
- 23 otomatik doğrulama grubu geçti. Sabit bağlantı ve yerel TCP alt senaryoları erişim engeli nedeniyle atlandı.
- Dağıtılan EXE arşivden çıkarılan kaynakla derlendi. Türkçe ve İngilizce pencereler açılıp normal kapandı.
- Bu oturumda yönetici yetkisiyle gerçek korumalı dizin oluşturulması ve DISM/SFC onarımı çalıştırılmadı.

EXE SHA-256 değeri **6EDA6042CE40ABAC686C3E15CE6E7EC71C6491B556EF4B33FB07F861471FF9F8**.

