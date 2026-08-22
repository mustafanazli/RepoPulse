# RepoPulse

> **GitHub projelerinin nabzını ölçen, mobil odaklı açık kaynak proje sağlık asistanı.**

## 1. Ürün vizyonu

RepoPulse, GitHub depolarındaki ham verileri anlaşılır istatistiklere, sağlık puanlarına ve uygulanabilir önerilere dönüştüren bir .NET MAUI mobil uygulamasıdır.

Uygulama yalnızca commit, issue ve pull request sayılarını göstermez. Kullanıcıya şu soruların cevabını verir:

- Proje aktif olarak geliştiriliyor mu?
- Issue ve pull request süreçleri sağlıklı mı?
- CI/CD süreci güvenilir mi?
- Dokümantasyon ve topluluk dosyaları yeterli mi?
- Projenin şu anda en çok hangi konuda iyileştirmeye ihtiyacı var?

## 2. Hedef kullanıcılar

- Kendi projelerinin durumunu takip eden geliştiriciler
- Birden fazla açık kaynak proje yöneten maintainers
- Katkıda bulunacak sağlıklı projeler arayan geliştiriciler
- Teknik portföyünü ölçmek isteyen öğrenciler ve junior geliştiriciler
- Ekiplerinin repository süreçlerini mobil cihazdan izlemek isteyen teknik liderler

## 3. Temel değer önerisi

RepoPulse'un farkı, **veri göstermek yerine veriyi yorumlamasıdır**.

Örnek:

```text
RepoPulse Health Score: 82/100

Aktivite          91/100
Bakım             76/100
CI/CD             84/100
Dokümantasyon     80/100
Topluluk          68/100

Önerilen aksiyonlar
1. 30 günden uzun süredir bekleyen 4 issue'yu incele.
2. CONTRIBUTING.md dosyası ekle.
3. Son 10 workflow çalışmasındaki 2 hatayı kontrol et.
```

## 4. MVP kapsamı

İlk sürüm küçük, güvenilir ve yayınlanabilir olmalıdır.

### Kimlik doğrulama

- GitHub hesabıyla güvenli giriş: MVP için **OAuth Authorization Code + PKCE** akışı kullanılır.
  - Android tarafında sistem tarayıcısı (Custom Tabs) açılır, GitHub yetkilendirmesinin ardından uygulamaya özel bir **custom URI scheme** (ör. `repopulse://oauth/callback`) ile geri dönülür ve intent filter üzerinden yakalanır.
  - PKCE (`code_verifier` / `code_challenge`) kullanıldığı için **client secret uygulamaya gömülmez**; GitHub OAuth App'i "public client" olarak yapılandırılır, secret gerekmez.
  - Token değişimi doğrudan cihazdan GitHub'ın token endpoint'ine yapılır, ayrı bir backend zorunlu değildir.
  - Bu akışın Android'de custom URI callback prototipi **Faz 0 sonunda** doğrulanmalıdır (bkz. §11, Faz 0 çıkış kriterleri).
- Token'ların cihazın güvenli depolama alanında tutulması (SecureStorage)
- Oturum kapatma ve yerel verileri temizleme

> **Alternatif (yalnızca callback prototipi başarısız olursa)**: GitHub **OAuth Device Flow** (kullanıcı tarayıcıda bir kod girer, uygulama polling ile token bekler). Custom URI scheme/intent filter Android'de güvenilir çalışmazsa veya derin bağlantı çakışmaları yaşanırsa bu akışa geçilir. Device Flow client secret gerektirmez ve callback yönetimi yoktur, ancak kullanıcı deneyimi bir ekstra adım (kodu elle girme) içerir — bu yüzden MVP birincil akışı olarak seçilmedi, sadece dokümante edilmiş bir yedek plandır.

### Repository ekranları

- Kullanıcının repository listesi
- Arama, sıralama ve favorilere ekleme
- Repository özet ekranı
- Kullanılan diller, star, fork, issue ve pull request verileri
- Son commit ve son aktivite bilgisi

### Analiz

- Genel sağlık puanı
- Aktivite, bakım, dokümantasyon, CI/CD ve topluluk alt puanları
- Puanın neden yükseldiğini veya düştüğünü açıklayan göstergeler
- En fazla üç öncelikli iyileştirme önerisi

### Mobil deneyim

- Android desteği; sonraki aşamada iOS
- Açık ve koyu tema
- Yenilemek için aşağı çekme
- Temel verileri SQLite ile çevrimdışı görüntüleme
- Erişilebilir renkler ve ekran okuyucu etiketleri

## 5. MVP dışında tutulacaklar

Kapsamın büyümemesi için ilk sürümde şunlar yapılmayacaktır:

- Issue veya pull request düzenleme
- Takım ve organizasyon yönetimi
- Yapay zekâ ile kod inceleme
- Sosyal ağ özellikleri
- Çok ayrıntılı contributor karşılaştırmaları
- Ücretli abonelik sistemi

## 6. Sağlık puanı modeli

İlk sürümde deterministik ve açıklanabilir bir model kullanılmalıdır.

```text
Genel Sağlık Puanı =
  Aktivite          × %25
+ Bakım             × %25
+ CI/CD             × %20
+ Dokümantasyon     × %15
+ Topluluk          × %15
```

### Alt puan örnekleri

**Aktivite**

- Son commit'in güncelliği
- Son 30 ve 90 gündeki commit sıklığı
- Düzenli geliştirme trendi

**Bakım**

- Açık issue'ların yaşı
- Issue kapanma süresi
- Bekleyen pull request'lerin yaşı
- Pull request birleştirme oranı

**CI/CD**

- Workflow bulunması
- Son çalıştırmaların başarı oranı
- Ana branch üzerindeki son workflow sonucu

**Dokümantasyon**

- README varlığı
- Kurulum ve kullanım bölümleri
- LICENSE, CHANGELOG ve örnek yapılandırma dosyaları

**Topluluk**

- CONTRIBUTING.md ve CODE_OF_CONDUCT.md
- Issue ve pull request şablonları
- SECURITY.md
- Contributor çeşitliliği ve yanıt süreleri

> Her puanın ayrıntılı açıklaması uygulamada gösterilmeli, eşikler dokümante edilmeli ve algoritma sürümlenmelidir. Kullanıcıya açıklanamayan “sihirli” bir puandan kaçınılmalıdır.

### Algoritma versiyonlama

- Puanlama modeli semver ile versiyonlanır (örn. `scoring-v1.0.0`); ağırlık veya eşik değişikliği minor, formül/alt puan yapısı değişikliği major sürüm artışı gerektirir.
- Her analiz sonucu, hangi algoritma sürümüyle üretildiği bilgisiyle birlikte SQLite'a kaydedilir; böylece geçmiş puanlar farklı algoritma sürümleriyle doğrudan kıyaslanmaz, kullanıcıya "bu puan v1.2 ile hesaplandı" gibi bir bağlam gösterilir.
- CHANGELOG.md içinde algoritma sürüm geçmişi ayrı bir bölümde tutulur.

## 7. Önerilen teknik mimari

### Teknoloji seti

- .NET MAUI
- C# ve güncel kararlı .NET sürümü
- CommunityToolkit.Mvvm
- GitHub REST API; gerektiğinde GraphQL
- Refit veya typed HttpClient
- SQLite
- Polly tabanlı hata toleransı
- SecureStorage
- LiveCharts2 veya benzeri bir grafik kütüphanesi
- xUnit
- GitHub Actions

### Katmanlar

```text
RepoPulse.Mobile
    ├── Presentation
    ├── Application
    ├── Domain
    └── Infrastructure
            ├── GitHub API
            ├── SQLite
            └── Secure Storage
```

MVP için ayrı bir backend zorunlu değildir. GitHub API ile doğrudan ve güvenli biçimde çalışmak geliştirme süresini azaltır. İleride bildirim, geçmiş analizleri veya takım özellikleri gerektiğinde ASP.NET Core backend eklenebilir.

### MVP için aşamalı mimari

Yukarıdaki katmanlı yapı (`Application` / `Domain` / `Infrastructure` ayrı projeler) **hedef mimaridir, RP-001 itibarıyla henüz uygulanmamıştır**. RP-001'de mevcut VS tarafından oluşturulmuş tek `RepoPulse` MAUI projesi olduğu gibi korunmuş, sadece `tests/RepoPulse.UnitTests` eklenmiştir. Boş/içeriksiz `Domain`, `Application`, `Infrastructure` projeleri bilinçli olarak açılmamıştır — katman ayrımı, bu katmanlarda gerçek bir ihtiyaç (ör. ilk domain modeli veya puanlama kuralı) ortaya çıktığında, o kodla birlikte tek seferde yapılacaktır. README ve dokümantasyonda, henüz var olmayan bir katmanlı/Clean Architecture yapısı kurulmuş gibi ifade edilmemelidir.

### Önerilen repository yapısı

```text
RepoPulse/
├── src/
│   ├── RepoPulse.Mobile/
│   ├── RepoPulse.Application/
│   ├── RepoPulse.Domain/
│   └── RepoPulse.Infrastructure/
├── tests/
│   ├── RepoPulse.UnitTests/
│   └── RepoPulse.IntegrationTests/
├── docs/
│   ├── architecture.md
│   ├── health-score.md
│   └── screenshots/
├── .github/
│   ├── ISSUE_TEMPLATE/
│   └── workflows/
├── CHANGELOG.md
├── CONTRIBUTING.md
├── LICENSE
└── README.md
```

## 8. Veri akışı

```text
GitHub API
    ↓
API istemcisi ve rate-limit yönetimi
    ↓
Yerel önbellek / SQLite
    ↓
Analiz ve sağlık puanı motoru
    ↓
ViewModel
    ↓
.NET MAUI kullanıcı arayüzü
```

## 9. Kritik teknik konular

- GitHub API rate-limit durumunu kullanıcıya açıkça göstermek: authenticated istekler 5000/saat, unauthenticated istekler 60/saat sınırına tabidir; oturum açık olduğu sürece her zaman authenticated istek kullanılır, kalan limit `X-RateLimit-Remaining` header'ından okunup arayüzde gösterilir
- REST API varsayılan istemci olarak kullanılır; bir ekran tek istekte birden fazla ilişkili kaynağı (ör. repo özeti + son commit + workflow durumu) gerektirdiğinde ve REST çoklu istek/aşırı veri çekme (over-fetching) sorunu yaratıyorsa GraphQL'e geçilir — bu karar ekran bazında Faz 1/2 sırasında değerlendirilir
- Sayfalama ve büyük repository'lerde performans
- Token veya kişisel verileri loglamamak
- Ağ bağlantısı kesildiğinde son başarılı analizi göstermek
- İptal edilebilir istekler ve doğru loading/error/empty durumları
- Puan algoritmasını GitHub API istemcisinden bağımsız tutmak
- Eksik veri ile sıfır değer arasındaki farkı korumak

## 10. Test stratejisi

- Sağlık puanı motoru için kapsamlı unit testler
- GitHub API yanıtları için fixture tabanlı testler
- Hata, rate-limit ve eksik veri senaryoları
- SQLite repository testleri
- ViewModel durum geçişi testleri
- Android üzerinde temel UI smoke testleri
- Pull request'lerde otomatik build ve test

Özellikle puanlama algoritması yüzde yüz güvenilir ve tekrar üretilebilir olmalıdır.

## 11. Yol haritası

Her faz bir GitHub Milestone'dur. Her madde ayrı bir GitHub Issue olarak açılır; issue numaraları ilk issue'lar açıldığında güncellenir (aşağıdaki `#N` değerleri planlama sırasıdır, gerçek issue numaralarıyla eşleşmeyebilir). Bir faz, yalnızca o fazın **çıkış kriterleri** karşılandığında tamamlanmış sayılır.

**RP-001 (proje iskeleti ve CI temeli) — durum: TAMAMLANDI.** Kapsam: mevcut VS MAUI projesinin korunması, `.gitignore`, `tests/RepoPulse.UnitTests` (placeholder altyapı testi), `.github/workflows/android-build.yml`. Yerelde `dotnet build`/`dotnet test` doğrulandı; `main` branch'e push edildikten sonra GitHub Actions üzerinde `dotnet workload restore` → `dotnet restore` → `dotnet build` → `dotnet test` adımlarının tamamı yeşil geçti (repo: `mustafanazli/RepoPulse`). CI'ın ilk denemesinde `dotnet workload install android` yetersiz kaldığı için (`maui-android` workload'u eksikti) `dotnet workload restore RepoPulse.slnx` ile değiştirildi; bu düzeltmeyle birlikte doğrulandı.

**RP-002 (Android OAuth callback deep-link altyapısı, Faz 0 `#4`'ün karşılığı) — durum: TAMAMLANDI.** Kapsam: `repopulse://oauth/callback` için dar (wildcard'sız) intent-filter, platformdan bağımsız `RepoPulse.Core` class library'sinde (namespace `RepoPulse.Core.Authentication`) yaşayan bağımsız query parser/model, cold-start ve warm-start callback yakalama, tüket-ve-temizle (consume-once) semantiğine sahip bir broker, geliştirme amaçlı 3 durumlu durum ekranı. Gerçek OAuth isteği, PKCE üretimi, token exchange, SecureStorage ve GitHub OAuth App'i bu kapsamın dışında bırakıldı — sıradaki issue'ların konusu. `Pixel_6_API36` emülatöründe adb ile cold-start/warm-start/geçersiz-callback/eski-callback-tekrar-gösterilmemesi senaryolarının dördü de doğrulandı; kod/state/error_description hiçbir logda veya ekranda ham olarak görünmüyor. `main`'e push sonrası GitHub Actions yeşil geçti.

**RP-003 (GitHub OAuth Authorization Code + PKCE giriş prototipi) — durum: TAMAMLANDI — gerçek Authorization Code + PKCE akışı doğrulandı.** Kapsam: merkezi OAuth yapılandırması (public Client ID, endpoint sabitleri), RFC 7636 uyumlu `PkceGenerator` (bilinen test vektörüyle doğrulandı), bellekte tek-aktif/süreli/tek-kullanımlık `AuthorizationSessionStore` (state sabit-zamanlı karşılaştırma ile), `GitHubAuthorizationUrlBuilder`, MAUI ekranına "GitHub ile giriş yap" butonu. **Gerçek Pixel_6_API36 emülatöründe, kullanıcının kendi GitHub hesabıyla** uçtan uca doğrulandı: authorize URL → gerçek GitHub giriş/onay ekranı → gerçek `code`+`state` ile callback → state doğrulaması → RepoPulse.AuthApi üzerinden gerçek `client_secret` ile token exchange → gerçek `GET /user` → ekranda gerçek kullanıcı adı ve avatar. Önceki blokaj (GitHub'ın klasik OAuth App tipinin token adımında hâlâ `client_secret` istemesi, kod hatası değil platform kısıtlaması — bkz. [ADR-003](docs/adr/003-github-oauth-token-exchange.md)) RP-004'teki backend ve RP-005'teki mobil entegrasyonla çözüldü; ADR-003 kararı geçerliliğini koruyor. Uçtan uca doğrulamanın tüm ayrıntısı (negatif senaryolar dahil) RP-005 altında.

**RP-004 (minimal ASP.NET Core token-exchange backend) — durum: TAMAMLANDI — AuthApi token exchange yerelde gerçek GitHub ile doğrulandı; production hosting ayrı görev.** Kapsam: `src/RepoPulse.AuthApi` (ASP.NET Core Minimal API, net10.0), `GitHubOAuthOptions` + `ValidateOnStart()`, `POST /oauth/github/exchange` (gerçek GitHub form alanları + hata sözleşmesi + rate limiting + request-boyutu sınırı + no-store cache header'ları), `GET /health`. Detaylı sözleşme: [`docs/backend-auth.md`](docs/backend-auth.md). 48/48 `RepoPulse.AuthApi.Tests` testi geçiyor — tamamı sahte `HttpMessageHandler` ile. Gerçek `client_secret` .NET User Secrets üzerinden yerel geliştirme ortamına eklendi (agent tarafından hiçbir noktada okunmadı/loglanmadı); `ValidateOnStart()` başarıyla doğrulandı ve RP-005 kapsamında Android emülatöründen tetiklenen gerçek bir GitHub token exchange isteği başarıyla tamamlandı (AuthApi konsol logunda yalnızca `POST https://github.com/login/oauth/access_token` → `200` satırı — hiçbir gövde, header veya secret değeri yok). Kalan tek açık konu production hosting adresi/dağıtımıdır — ayrı, henüz planlanmamış bir görev (bkz. `RepoPulseAuthApiOptions.cs`'teki DEBUG-only placeholder uyarısı). Not: bu, mevcut Faz issue numaralandırmasına (`#1`–`#28`) eklenmiş yeni bir numaralı issue değildir — RP-00X etiketleri gibi ayrı bir takip kaydıdır; **mevcut `#1`–`#28` numaraları sessizce değiştirilmedi.**

**RP-005 (mobil → RepoPulse.AuthApi bağlantısı) — durum: TAMAMLANDI — mobil→backend→GitHub akışı emülatörde doğrulandı.** Eski birleşik `GitHubOAuthClient` kaldırıldı, yerine iki tek-sorumluluklu client geldi: `RepoPulseAuthApiClient` (yalnızca `POST /oauth/github/exchange`, request'te yalnızca `code`+`codeVerifier`) ve `GitHubApiClient` (yalnızca `GET /user`). `OAuthConstants.TokenEndpoint` tamamen kaldırıldı. Access/refresh token yalnızca bellekte tutuluyor. Android DEBUG derlemelerinde backend adresi otomatik olarak `https://10.0.2.2:7082`'ye çözülüyor (emülatörün host-machine localhost takma adı); bu bağlantı yalnızca AuthApi `HttpClient`'ına özel, DEBUG-only, dar kapsamlı bir `DevelopmentCertificateValidator` (host beyaz listesi + sertifika subject/issuer + geçerlilik tarihi kontrolü) ile korunuyor — `GitHubApiClient` ve Release derlemeleri her zaman standart platform TLS doğrulamasını kullanıyor; Release IL'inde bu callback hiç yok (derleme zamanında `#if DEBUG` ile hariç tutuluyor, yalnızca beklenen `#warning` üretiliyor). 65/65 `RepoPulse.UnitTests` (13 yeni `DevelopmentCertificateValidatorTests` dahil) ve 48/48 `RepoPulse.AuthApi.Tests` geçiyor. **Gerçek Pixel_6_API36 emülatöründe, kullanıcının kendi GitHub hesabıyla uçtan uca doğrulandı:** GitHub ile giriş yap → gerçek GitHub onay ekranı → gerçek callback → `RepoPulseAuthApiClient` üzerinden gerçek AuthApi'ye (gerçek `client_secret` ile) token exchange → gerçek `GET /user` → ekranda gerçek avatar ve kullanıcı adı. Dört negatif senaryo da doğrulandı: (1) kullanıcı iptali (`error=access_denied` → "Kullanıcı iptal etti", oturum temizleniyor), (2) eş zamanlı ikinci giriş denemesi (bekleyen oturum varken reddediliyor; Activity yeniden oluşturulsa bile singleton session store korunuyor), (3) eski/tekrar kullanılan callback (state eşleşmiyor → "Geçersiz callback", AuthApi'ye hiç istek gitmiyor), (4) AuthApi kapalıyken güvenli ağ hatası ("Sunucuya ulaşılamadı.", hiçbir istisna detayı veya hassas değer sızmıyor). Loglarda/ekranlarda hiçbir noktada secret/token/code/state/verifier ham olarak görünmedi. Detay: [`docs/backend-auth.md`](docs/backend-auth.md#mobil-client-akışı-rp-005).

### Faz 0 — Tasarım ve doğrulama

Issue'lar:
- `#1` Ürün gereksinimleri dokümanını (bu plan) kesinleştir ve onayla
- `#2` Beş ana ekran için wireframe hazırla: Giriş, Repository Listesi, Repository Detayı, Analiz/Health Score, Ayarlar
- `#3` GitHub REST API deneme çağrıları yap (repo, commits, issues, pulls, workflow runs) ve örnek response'ları `tests/fixtures/` altına kaydet
- `#4` Android'de OAuth Authorization Code + PKCE + custom URI scheme callback'ini izole bir prototipte doğrula
- `#5` Puanlama kurallarının ilk taslağını (ağırlıklar, eşikler) `docs/health-score.md` olarak yaz

**Çıkış kriterleri (Milestone: Faz 0)**
- [ ] Beş ekranın wireframe'i repoda veya tasarım linkinde erişilebilir durumda
- [ ] PKCE + custom URI callback prototipi gerçek bir Android cihaz/emülatörde uçtan uca çalışıyor: GitHub onayından sonra uygulama access token'ı alıyor ve bunu ekranda/log'da gösterebiliyor — **kısmen karşılandı (RP-003):** authorize + callback + state doğrulaması gerçek cihazda çalışıyor, ancak token exchange GitHub'ın `client_secret` gereksinimi nedeniyle backend olmadan tamamlanamıyor (bkz. ADR-003). Bu kriter RP-004 (token-exchange backend) tamamlanınca kapatılabilir.
- [ ] `docs/health-score.md` taslağı commit edilmiş, beş alt puan için ağırlık ve en az üç eşik örneği tablo halinde yazılmış
- [ ] En az 5 farklı gerçek repodan alınmış örnek API response'u `tests/fixtures/` altında mevcut

### Faz 1 — Temel uygulama

Issue'lar:
- `#6` `src/` altında katmanlı proje iskeletini oluştur (Mobile / Application / Domain / Infrastructure), `RepoPulse.slnx`'e ekle
- `#7` AppShell navigasyon akışı: Giriş → Repository Listesi → Repository Detayı → Ayarlar
- `#8` PKCE + custom URI callback akışını uygulamaya entegre et, token'ı SecureStorage'a yaz — **RP-004 (token-exchange backend) tamamlanmadan bu issue gerçek bir access token üretemez; sıralama RP-004 → `#8` şeklindedir**
- `#9` Typed HttpClient/Refit ile GitHub API istemcisi (repo listesi + repo detay uç noktaları)
- `#10` SQLite şeması ve yerel önbellek repository'si (repo listesi + detay için)
- `#11` Repository listesi ekranı: arama, sıralama, favorilere ekleme
- `#12` Repository özet/detay ekranı: dil, star, fork, açık issue/PR sayısı, son commit bilgisi

**Çıkış kriterleri (Milestone: Faz 1)**
- [ ] Kullanıcı gerçek GitHub hesabıyla giriş yapıp token alabiliyor; uygulama kapatılıp yeniden açıldığında oturum korunuyor
- [ ] Repository listesi gerçek API'den geliyor; arama, sıralama ve favori ekleme/çıkarma çalışıyor ve favoriler cihaz yeniden başlatıldığında kalıcı
- [ ] Repo detay ekranı gerçek verilerle (dil, star, fork, açık issue/PR sayısı, son commit tarihi) doluyor
- [ ] Ağ bağlantısı kesikken (uçak modu) istek denemesi çöküşe yol açmıyor, hata durumu ekranda gösteriliyor

### Faz 2 — Analiz motoru

Issue'lar:
- `#13` Domain katmanında alt puan hesaplama kuralları (Aktivite, Bakım, CI/CD, Dokümantasyon, Topluluk) — API istemcisinden bağımsız saf fonksiyonlar olarak
- `#14` Genel sağlık puanı ağırlıklı toplama motoru + algoritma sürüm etiketleme (`scoring-vX.Y.Z`)
- `#15` Puan gerekçesi/açıklama metinlerinin üretimi (neden yükseldi/düştü)
- `#16` En fazla üç öncelikli iyileştirme önerisi üretme kuralları
- `#17` Puanlama motoru için xUnit + fixture tabanlı test paketi

**Çıkış kriterleri (Milestone: Faz 2)**
- [ ] En az 10 farklı gerçek repo fixture'ı ile puanlama motoru test ediliyor; aynı girdi her çalıştırmada aynı puanı üretiyor (determinizm doğrulanmış)
- [ ] Her alt puan ve genel puan, gerekçe metniyle birlikte en az bir ekran testinde/manuel kontrolde gösterilebiliyor
- [ ] Puanlama motoru katmanında CI raporunda görünen satır kapsamı ≥ %90
- [ ] En az üç farklı repo örneğinde en fazla üç öncelikli öneri üretiliyor ve öneriler repoya özgü, jenerik değil

### Faz 3 — Ürün kalitesi

Issue'lar:
- `#18` LiveCharts2 (veya seçilen kütüphane) ile alt puan görselleştirme
- `#19` Açık/koyu tema desteği
- `#20` Pull-to-refresh
- `#21` Offline mod: son analiz sonucunun SQLite'tan gösterilmesi + "çevrimdışı" göstergesi
- `#22` Hata ve boş durum ekranları (ağ yok, repo bulunamadı, rate limit aşıldı)
- `#23` Erişilebilirlik: ekran okuyucu etiketleri, kontrast kontrolü, dinamik font ölçekleme

**Çıkış kriterleri (Milestone: Faz 3)**
- [ ] Uçak modunda uygulama açıldığında son analiz sonucu offline gösteriliyor, çökme yok
- [ ] TalkBack açıkken beş ana ekranın tamamında interaktif öğeler doğru seslendiriliyor (manuel test ile doğrulanmış)
- [ ] Açık/koyu tema geçişi anlık ve tüm ekranlarda tutarlı
- [ ] Rate limit aşıldığında kullanıcıya jenerik hata yerine anlamlı bir mesaj ve kalan süre gösteriliyor

### Faz 4 — Açık kaynak lansmanı

Issue'lar:
- `#24` İngilizce README (değer önerisi + kurulum + demo GIF yeri)
- `#25` Demo GIF ve ekran görüntülerini çek, README'ye ekle
- `#26` `docs/architecture.md` ve `docs/health-score.md`'yi son haline getir
- `#27` `CONTRIBUTING.md`, issue şablonları, en az 3 `good-first-issue` etiketli görev aç
- `#28` v1.0.0 release: GitHub Actions ile Android APK/AAB build + release notes

**Çıkış kriterleri (Milestone: Faz 4 — §15 ile birebir eşleşir)**
- [ ] README, mimari ve puanlama dokümantasyonu eksiksiz ve İngilizce
- [ ] En az 3 `good-first-issue` etiketli görev açık durumda
- [ ] CI pipeline main branch'te yeşil, her PR'da otomatik build+test çalışıyor
- [ ] v1.0.0 GitHub Releases'te kurulabilir bir Android APK ile birlikte yayınlanmış

## 12. Gelecek sürümler

- Birden fazla repository karşılaştırması
- Repository sağlık geçmişi ve trendler
- Organizasyon dashboard'u
- Ana ekran widget'ı
- Build başarısız olduğunda bildirim
- Public repository'leri giriş yapmadan inceleme
- Paylaşılabilir sağlık raporu kartı
- RepoPulse web dashboard'u
- Plugin tabanlı özel puanlama kuralları

## 13. GitHub'da ilgi görme stratejisi

İyi kod tek başına star garantisi vermez. RepoPulse'un keşfedilebilir olması için:

- Projenin değerini ilk 10 saniyede anlatan İngilizce README hazırla.
- README'nin başına kısa ve kaliteli bir demo GIF'i koy.
- Gerçek repository'lerle hazırlanmış örnek analizler paylaş.
- Puanlama algoritmasını açık ve tartışılabilir biçimde dokümante et.
- Küçük ve iyi tanımlanmış `good first issue` görevleri oluştur.
- Düzenli sürüm çıkar ve anlaşılır changelog yayınla.
- Reddit, Dev.to, Hacker News, LinkedIn ve ilgili .NET topluluklarında teknik geliştirme yazısı paylaş.
- .NET MAUI ve GitHub API ile ilgili öğrendiklerini ayrı yazılara dönüştür.
- Kullanıcı geri bildirimlerini issue ve discussion üzerinden görünür şekilde yönet.
- Projeyi yalnızca “portföy uygulaması” olarak değil, gerçekten kullanılabilen bir araç olarak geliştir.

## 14. Başarı ölçütleri

Star sayısının yanında şu metrikler izlenmelidir:

- Uygulamayı gerçekten kullanan kişi sayısı
- Tekrar gelen kullanıcı oranı
- Açılan ve çözülen issue sayısı
- Dışarıdan gelen contributor sayısı
- Release indirme sayısı
- Analiz edilen repository sayısı
- Kullanıcıların önerilen aksiyonlarla etkileşimi

## 15. İlk sürüm için tamamlanma kriterleri

- Kullanıcı GitHub hesabıyla giriş yapabiliyor.
- Repository listesini görüntüleyip arayabiliyor.
- Bir repository için beş alt puanı ve genel puanı görebiliyor.
- Her puanın gerekçesi açıklanıyor.
- En az üç tür uygulanabilir öneri oluşturuluyor.
- Son analiz çevrimdışı görüntülenebiliyor.
- Puanlama motorunun kritik senaryoları test ediliyor.
- CI üzerinde build ve testler başarıyla çalışıyor.
- Kurulum, mimari ve katkı rehberi eksiksiz bulunuyor.
- Android için kurulabilir bir ilk sürüm yayınlanıyor.

## 16. Kısa ürün mesajı

**RepoPulse turns GitHub repository activity into clear health scores and actionable insights — right from your phone.**
