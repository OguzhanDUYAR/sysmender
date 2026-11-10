# Güvenlik denetimi

- Linux ve yerel yetenek kontrolünden sonra araçlar sabit program ve argüman listesiyle başlatılır. Shell açılmaz. Tek ağ hedefi, doğrulanmış IP/DNS adı ve TCP portuyla sınırlıdır.
- Alt süreçler temizlenmiş ortam, süre sınırı, çıktı sınırı ve iptal işareti kullanır. Ortama bağlı günlük ve güvenlik araçlarının erişim engeli sonuçlarda görünür.
- GUI root olarak açılmaz. APT uygulaması yerel, değiştirilebilir Debian ailesi ve normal kullanıcıyla sınırlandırıldı. WSL, SSH, container ve değişmez sistemlerde kapalıdır.
- APT önce simüle edilir. Kaldırma, okunamayan plan, eksik komut, kesik çıktı, reddedilen onay ve değişen eylem dizisi uygulamayı engeller. Yerel APT komutu `--no-remove` ile çağrılır. Parola yalnızca işletim sisteminin sudo istemine girilir. Uygulama parolayı okumaz.
- Uygulama öncesinde sahiplik ve izin kontrolü yapılmış özel dizine kayıt yazılır. Bitiş kaydı yerel dönüş kodunu ve dpkg/APT doğrulamasını tutar. Başlangıç kaydı olup bitiş kaydı yoksa son durum belirsizdir.
- Özet dışa aktarma ayrıntı alanını tamamen atlar ve mevcut dosyanın üzerine yazmaz. Ayrıntılı CLI çıktısı paylaşılmadan önce gözden geçirilmelidir. Uzak rapor yükleme veya telemetri yoktur.

## Açık noktalar

Önizleme ile APT’nin kendi kilidini alması arasında diğer sistem işlemleri paket durumunu değiştirebilir. İkinci simülasyon bu aralığı daraltır. Yerel APT kendi uygun gördüğü yerde etkileşimli onay kullanır. Dağıtımın APT yapılandırması, depoları ve yüklenen paketin betikleri sistemin güven sınırına dahildir. Bu işlem için otomatik rollback bulunmaz.

APT yükseltme uygulaması, sistemde fiziksel değişiklik yapan araçlar ve Polkit gerçek VM’de güvenlik testine tabi tutulmadı. GUI yetkili işlem gerçekleştirmez. Yalnızca tanılama ve paket önizlemesi sunar. Güvenlik incelemesi uygulamanın kod sınırlarıyla sınırlıdır. Bu teslimat bir CVE taraması veya bağımsız denetim raporu içermez.

APT davranışı için [Debian APT kılavuzu](https://manpages.debian.org/bookworm/apt/apt-get.8.en.html).
