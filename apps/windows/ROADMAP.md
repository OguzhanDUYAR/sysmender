# Geliştirme yol haritası · v0.001

| Öncelik | Kalan iş | Tamamlanma ölçütü |
| --- | --- | --- |
| P0 | Windows 10/11 yönetici laboratuvarı | Yükseltme, SFC/DISM, Spooler, disk, WinGet ve yedekleme gerçek sonuçları gözlenir. Erişim kısıtlı taramadaki açık durumlar tekrar sınanır |
| P0 | Canlı GPU ve disk I/O | Desteklenen yerleşik sayaçlar doğrulanır. Sağlayıcı yoksa belirsiz değer gösterilir. Başlangıç, uyku ve sayaç sıfırlaması test edilir |
| P0 | Ağ tamiri, DHCP ve DNS geri alma | Bağdaştırıcı kimliği ve önceki değerler doğrulanır. Bağlantı kesintisi önizlenir. Geri alma ve başarısız yazım test edilir |
| P1 | Döküm analizi ve açılmayan sistem | Kontrollü sembol sağlayıcısı ve gerçek minidump örnekleriyle kök neden iddiası sınırlandırılır. WinPE ayrı doğrulanır |
| P1 | Yedekleme, profil taşıma ve sürücü geri dönüşü | Kaynak/hedef doğrulanır. Geri yükleme test edilir. Desteklenmeyen aygıtlara sürücü zorlanmaz |
| P1 | Sertifika/TLS, aygıt olayları ve gelişmiş karşılaştırma | Denetim filtreleri, durum anlık görüntüleri ve yerel kimlik gizleme test edilir |
| P2 | AD, Entra, Intune ve yetkili uzaktan yönetim | Ayrı kimlik, izin ve kurum kapsamı tanımlanır. Kimlik bilgisi güvenli saklama ve işlemlerin denetimi doğrulanır |
| P2 | İmzalı eklentiler | Manifest sürümü ve imza zinciri doğrulanır. Bilinmeyen yayıncılar ve kapsam dışı komutlar çalıştırılmaz |

v0.001 kapsamı yerel bilgisayardır. Başka bir cihazı yönetmeye, işletim sistemi yerinde yükseltmesine veya disk bölümlemeye yönelik bir iş akışı bu sürümde bulunmaz.

