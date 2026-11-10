# Güvenlik incelemesi

Kapsam v0.001 çalışma zamanı C# dosyaları, Veri altındaki üç PowerShell kaynağı, rapor içe/dışa aktarma, dosya inceleme, yetki ayrımı ve entegre WinUtil bölümüdür. GitHub adayları için seçme incelemesi ayrı belgelenir.

| Yüzey | Bulgular ve kontrol |
| --- | --- |
| Komut çalıştırma | Araç kimlikleri gömülü katalogdan gelir. Yerleşik EXE yolları System32 üzerinden seçilir. PowerShell işlemleri sabit gömülü bloklardan, profil yüklemeden başlar. DNS ve TCP hedefi kabuk komutuna çevrilmez. |
| Yetki | Değişiklik yapan 9 araç ayrı yönetici oturumunda ve incelenmiş planla çalışır. Çalışma zamanı da yetkiyi denetler. Hazır profiller okuma araçlarından oluşur. |
| Yükseltilmiş yazma | Günlük ve sürücü yedeği yalnızca Administrators/SYSTEM erişimli ve kalıtımı kapalı dizindedir. Eski günlük dizini güvenli değilse doğrulanan Windows günlük üst dizininde ayrı korumalı konum kullanılır. Sürücü yedeğinde yeni GUID klasörü kullanılır. |
| Dosya ve rapor güvenliği | HTML içeriği kodlanır ve dış kaynak/script yüklemesini engelleyen CSP içerir. JSON serileştirilir. Dışa aktarma yeni dosya açar ve var olan dosyanın üzerine yazmaz. |
| Veri | Raporlarda makine adı, seri numarası, ağ ve olay ayrıntıları bulunabilir. Raporlar yerel kaydedilir. BitLocker kurtarma anahtarı, Wi-Fi parolası veya sertifika özel anahtarı rapora eklenmez. Hedef testi yalnızca seçilen uç noktaya gider. |
| Kapsam ve iptal | Okuma işlerinde zaman sınırı ve iptal vardır. WinSock yeniden başlatma işareti verirse sıradaki BT değişiklikleri atlanır. Etkin onarım zorla kesilmez. |
| Onarım doğrulaması | Spooler son durumda Running olmalıdır. Defender taraması yeni bitiş zamanı gösteremiyorsa inceleme ister. PnP yedeğinde en az bir INF aranır. Bu kontroller her araç için tam sistem sağlığı anlamına gelmez. |
| Kısmi sonuç | CIM kısıtlandığında donanım, Windows hazırlığı ve disklerde erişilebilen bilgiler ayrı gösterilir. Kısmi sonuca İnceleme gerekli verilir. |
| Dosya inceleme | Seçilen yerel NTFS dizini doğrulanır. Dizin tanıtıcıları işlem boyunca tutulur. Hash okuması açılan dosyanın yolunu ve niteliklerini kontrol eder. Bağlantı ve çevrimdışı girdiler atlanır. Tarama silme işlemi içermez. |
| Rapor karşılaştırması | JSON boyutu, derinliği, uygulama kimliği ve adım sayısı sınırlandırılır. Dış veriler salt metin gösterilir. Paylaşım özetinde ham günlük, cihaz adı ve ayrıntı mesajları çıkarılır. |
| Sorun giderme | İlk aşama salt okunurdur. Sistem değişiklikleri ayrı onay planına girer. Güncelleme asistanı hazır DISM/SFC adımlarını kullanır ve Windows Update ilkelerini değiştirmez. |

Hedefli statik arama, çalışma zamanı kaynaklarında yaygın belirteç/anahtar biçimleri ve uzaktan betik indirme/çalıştırma kalıpları için eşleşme bulmadı. PowerShell ayrıştırıcısı ve uyarıları hata sayan C# derlemesi uygulandı. Kod incelemesi girdi doğrulaması, sabit komutlar, HKLM/HKCU okuma kapsamı ve sonuç kodlarını kapsadı. Ortamda Gitleaks, Trivy ve OSV-Scanner bulunmadı. Genel CVE taraması veya bütün Windows çalışma zamanı bileşenlerinin güvenlik doğrulaması yapılmış sayılmaz.

Kalan riskler işletim sistemi bileşenlerinin ve WinGet/App Installer'ın güncelliği, üretici sürücüsü davranışı, sistem politikaları ve yönetici eylemlerinin canlı etkisidir. EXE geliştirici atfı taşır ve Authenticode sertifikasıyla imzalanmamıştır. Test ortamı sınırları TEST_RESULTS.md dosyasındadır.

