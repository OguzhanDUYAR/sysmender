# Değişiklikler

## 0.001 · Düzeltme 1 (0.0.1.1)

- Eski yönetici günlük klasöründe kişisel yazma izni algılanırsa uygulama ayrı korumalı konum hazırlar.
- Onarım kuyruğu doğrulanmış yolu kullanır. Yönetici raporu, işlem geçmişi, sürücü yedeği ve çocuk süreç geçici dizini aynı yolu izler.
- Eski günlük dosyaları ve eski klasör izinleri olduğu yerde kalır. Güvenli günlük bulunamazsa Windows işlemleri başlamaz ve sorunlu klasör belirtilir.

## 0.001

- Sürüm etiketi, ürün bilgisi ve dağıtım dosya adları v0.001 oldu.
- 14 araç eklendi. Katalogda 66 BT aracı var. Eklenenler arasında BCD okuma, Windows Update servisleri, CBS/DISM günlüğü, USB, TCP/UDP, rota, yazılım envanteri, lisans ve yerel güvenlik denetimleri bulunuyor.
- Yedi belirti için tanılama asistanı, ayrı onarım planı ve takip kontrolü eklendi.
- Büyük dosya ve içerik eşleşmesi incelemesi ile SHA-256 hesaplama/doğrulama eklendi.
- İşlem geçmişi, iki JSON raporu karşılaştırma ve azaltılmış paylaşım özeti eklendi.
- Açık tema, Ctrl+K genel arama ve favoriler eklendi.
- Canlı izlemeye çalışma süresi ve ağ aktarım hızı eklendi. Disk ve ağ okumaları bağımsız yürütülür.

## 5.0

- BT araçları bölümüne 52 işlem, kategori ve arama filtresi, dört okuma profili ve hedefe özel DNS/ICMP/TCP testi eklendi.
- Olaylar, donanım, güncelleme geçmişi, güvenlik, ağ, yazdırma, depolama ve sürücü araçları aynı kuyruğa ve günlüğe bağlandı.
- Yönetici eylemleri için etki ve izin bilgisi, yeniden başlatma durumu ve raporlanan sonuçlar eklendi.
- Tam çalışma günlüğü ve adım sonuçları HTML veya JSON olarak dışa aktarılabiliyor.
- WinUtil'in yeniden başlatma okuması sabit commit üzerinden uyarlandı. Lisans görüntüleme eklendi.
- Uzun plan açıklamaları satıra sığacak biçimde düzenlendi. Küçük pencerede ağ hedefi satırı yalnızca ilgili araç seçildiğinde görünür.
- Kaynak kökeni, lisans, güvenlik, araştırma, test ve SBOM belgeleri eklendi.

