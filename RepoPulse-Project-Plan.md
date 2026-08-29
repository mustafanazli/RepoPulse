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

**Staging AuthApi entegrasyonu (RP-006 öncesi son entegrasyon adımı) — durum: TAMAMLANDI.** Mobil uygulama artık geliştirici makinesinden bağımsız, canlı Azure Container Apps staging backend'ine bağlanıyor. Android DEBUG derlemelerinin varsayılan AuthApi adresi, `RepoPulseAuthApiOptions.StagingBaseAddress` sabiti aracılığıyla `10.0.2.2:7082`'den staging URL'sine değiştirildi; yerel backend testi hâlâ mümkün — `MauiProgram.UseLocalDevelopmentAuthApi` bayrağı `true` yapılarak yerelde açıkça devre dışı bırakılabiliyor. `DevelopmentCertificateValidator`'ın DEBUG-only sertifika callback'i artık yalnızca gerçek yerel geliştirme host'larında (`localhost`/`127.0.0.1`/`10.0.2.2`, yeni `IsLocalDevelopmentHost` yardımcı metoduyla kontrol ediliyor) devreye giriyor; staging'in gerçek, herkese güvenilir Azure sertifikası standart platform TLS doğrulamasından geçiyor, hiçbir custom callback bağlanmıyor. Release derlemeleri hâlâ staging'e bağlanmıyor — mevcut DEBUG-only placeholder + `#warning` davranışı değişmeden korundu, staging kasıtlı olarak "production" olarak ele alınmıyor (bkz. [ADR-004](docs/adr/004-production-hosting.md)'teki çözülmemiş rate-limiter/client-IP riski). 77/77 `RepoPulse.UnitTests` (12 yeni test: `RepoPulseAuthApiOptionsTests` + `IsLocalDevelopmentHost` kapsamı) ve 51/51 `RepoPulse.AuthApi.Tests` geçiyor. **Gerçek Pixel_6_API36 emülatöründe, kullanıcının kendi GitHub hesabıyla, gerçek canlı staging backend'i üzerinden uçtan uca doğrulandı:** GitHub ile giriş yap → kullanıcı GitHub onay ekranını kendisi tamamladı → gerçek callback → staging AuthApi'ye exchange → gerçek `GET /user` → ekranda gerçek avatar ve `@mustafanazli` kullanıcı adı. Loglarda hiçbir secret/token/code/state/verifier görünmedi. Test sonrası emülatör kontrollü olarak kapatıldı; hiçbir Azure kaynağı değiştirilmedi/silinmedi.

**RP-006 (tekil GitHub repository araması) — durum: TAMAMLANDI.** Faz 1'in `#9`/`#12` issue'larının kasıtlı olarak daraltılmış bir alt kümesi: repository listesi, SQLite/favori kalıcılığı, PR/commit verisi ve sağlık puanı bu kapsamın dışında bırakıldı — yalnızca tek bir repository'nin gerçek GitHub verisiyle sorgulanıp gösterilmesi. `RepoPulse.Core.Repositories` altında MAUI'den bağımsız `GitHubRepository` modeli, `RepositoryIdentifierParser` (yalnızca `owner/repository` ve `https://github.com/owner/repository` biçimlerini kabul eder; GitHub dışı host, fazladan path segmenti, query/fragment ve geçersiz karakterleri reddeder) ve `GitHubRepositoryResult`/`GitHubRepositoryFailureKind` (NotFound/Unauthorized/RateLimited/NetworkError/Unexpected) eklendi. `GitHubApiClient`, tek sorumluluk ilkesi korunarak `GET /repos/{owner}/{repository}` ile genişletildi — owner/repository segmentleri percent-encode ediliyor, token yalnızca `Authorization` header'ında, GitHub'ın ham hata gövdesi hiçbir zaman kullanıcıya/loglara aktarılmıyor. MAUI şablonundaki demo öğeleri (Hello World, sayaç butonu, dotnet bot görseli) kaldırıldı; giriş sonrası repository arama alanı + sonuç kartı eklendi, çift tıklamada ikinci istek engelleniyor, tüm interaktif öğelerde `SemanticProperties` var, açık/koyu tema mevcut global stillerle uyumlu. **Android emülatöründe canlı testte, `RepoPulseAuthApiClient.ExchangeAsync` içinde gerçek bir uygulama-çökertme hatası bulundu ve düzeltildi:** Xamarin.Android'in HTTP handler'ı bazı soket hatalarını (`Socket closed`) `HttpRequestException` yerine yakalanmayan bir `System.Net.WebException` olarak fırlatabiliyor — bu, RP-005'ten kalan ve RP-006'nın kendi ağ-hatası tipli sonuç garantisini de bozan bir açıktı; üç ağ çağrısına da (`ExchangeAsync`, `GetCurrentUserAsync`, `GetRepositoryAsync`) `WebException` yakalama eklendi. 134/134 `RepoPulse.UnitTests` (yeni: `RepositoryIdentifierParserTests`, `GetRepositoryAsync`/`WebException` testleri) ve 51/51 `RepoPulse.AuthApi.Tests` geçiyor. **Gerçek Pixel_6_API36 emülatöründe, canlı staging backend üzerinden uçtan uca doğrulandı:** GitHub ile giriş → `mustafanazli/RepoPulse` araması → gerçek repository kartı (0 yıldız/fork/açık issue+PR, `Ana dil: C#`, gerçek son güncelleme tarihi) → geçersiz giriş testi ("Repository adını \"owner/repository\" biçiminde girin.") → var olmayan repository testi ("Repository bulunamadı.") → art arda çift tıklamada tek sonuç. Loglarda/ekranlarda hiçbir secret/token/code/state/verifier görünmedi. Test sonrası emülatör kontrollü olarak kapatıldı.

**RP-007 (Faz 1 `#7` AppShell navigasyon akışı) — durum: TAMAMLANDI.** Tekil `MainPage`'e sıkışmış uygulama, dört ayrı sayfalı bir Shell akışına dönüştürüldü: `LoginPage` → `RepositoryListPage` → `RepositoryDetailPage` → `SettingsPage`. `MainPage.xaml`/`MainPage.xaml.cs` tamamen kaldırıldı; RP-006'nın arama mantığı ve OAuth giriş akışı, işlevleri değişmeden `RepositoryListPage`/`LoginPage`'e taşındı (`"{Stars} yıldız · {Forks} fork · {OpenIssuesAndPullRequests} açık issue + PR"` etiketi dahil, birebir korundu). Rota adları `RepoPulse.Core.Navigation.AppRoutes` altında merkezileştirildi ve `AppShell.xaml`'da `x:Static` ile referans veriliyor. Oturum güvenliği: yeni bir `RepoPulse.Core.Authentication.UserSessionStore` singleton'ı, erişim/refresh token + login + avatar'ı **yalnızca bellekte** tutuyor — SecureStorage/SQLite/Preferences'a hiçbir alan yazılmıyor; bu, uygulama yeniden başlatıldığında oturumun kaybolduğu, RP-007 kapsamında **bilinçli olarak alınmış bir karardır** (kalıcı oturum RP-008+ kapsamında ele alınabilir). Erişim token'ı hiçbir zaman Shell route/query string'ine, log satırına veya ekrana yazılmıyor; sayfalar arası taşınan `GitHubRepository` nesnesi (`RepositoryNavigationQueryBuilder` ile, `IQueryAttributable` üzerinden) token alanı içermiyor — reflection tabanlı bir testle doğrulandı. Navigasyon koruması: `AppShell.OnNavigating`, MAUI'den bağımsız, test edilebilir bir `NavigationGuard` sınıfına devrederek `RepositoryList`/`RepositoryDetail`/`Settings` rotalarına oturum açılmadan erişimi engelliyor ve `Login`'e yönlendiriyor; `FlyoutBehavior="Disabled"` olduğundan bu guard, korumalı sayfalara giden tek kapı. Girişten sonra `RepositoryList`'e mutlak (`//`) rota ile geçiliyor — geri tuşu/gesture'ı `Login`'e dönemiyor; çıkış yapıldığında da aynı şekilde mutlak rota ile `Login`'e dönülüyor ve `UserSessionStore.SignOut()` tüm alanları temizliyor. **Android emülatöründe canlı testte iki gerçek çökme bulundu ve düzeltildi:** (1) `AppShell.xaml`'da art arda iki çıplak `ShellContent` kardeşinin (görünür `TabBar`/flyout sarmalayıcısı olmadan) MAUI Shell'in `CurrentItem`'ı hiç ayarlamamasına yol açtığı bir çerçeve hatası (`Active Shell Item not set`) — `AppShell` constructor'ında `CurrentItem` açıkça ayarlanarak ve `OnNavigating`'in Shell'in kendi bootstrap navigasyonu sırasında asla çökmeyeceğinden emin olacak bir savunma katmanıyla giderildi; (2) `RepositoryDetailPage.xaml.cs`'de `InitializeComponent()` çağıran bir constructor'ın hiç var olmaması — sayfa, isimlendirilmiş XAML elemanları hâlâ `null` iken `ApplyQueryAttributes` üzerinden dolduruluyordu, bu da her detay navigasyonunda yakalanan bir `NullReferenceException`'a yol açıyordu; eksik constructor eklendi. 156/156 `RepoPulse.UnitTests` (22 yeni test: `AppRoutesTests`, `UserSessionStoreTests`, `NavigationGuardTests`, `RepositoryNavigationQueryBuilderTests`) ve 51/51 `RepoPulse.AuthApi.Tests` geçiyor. **Gerçek Pixel_6_API36 emülatöründe, kullanıcının kendi GitHub hesabıyla, gerçek canlı staging backend'i üzerinden uçtan uca doğrulandı:** uygulama açılışı → `LoginPage` → GitHub ile giriş → `RepositoryListPage`'e mutlak geçiş (geri tuşu `Login`'e değil uygulamadan çıkışa gidiyor) → `mustafanazli/RepoPulse` araması (0 yıldız/fork/açık issue+PR) → "Detayları Gör" → `RepositoryDetailPage` tüm alanlarla render edildi → geri tuşu `RepositoryListPage`'e (sonuçlar korunarak) döndü → "Ayarlar" → `SettingsPage` avatar+login gösterdi → "Çıkış yap" → `LoginPage`'e mutlak dönüş → geri tuşu artık korumalı hiçbir sayfaya dönemiyor, uygulamadan çıkıyor. Süreç yeniden başlatıldığında oturumun kaybolduğu (bellek-içi tasarım gereği) ayrıca doğrulandı. Loglarda/ekranlarda hiçbir noktada secret/token/code/state/verifier görünmedi (`adb logcat` taramasıyla doğrulandı, sıfır `FATAL EXCEPTION`). Test sonrası emülatör kontrollü olarak (`adb emu kill`) kapatıldı.

**RP-008 (Faz 1 `#8`'in eksik kalan bölümü: OAuth oturumunu SecureStorage ile kalıcılaştırma) — durum: TAMAMLANDI.** `#8`'in PKCE/callback/token-exchange yarısı zaten RP-003/005/007'de tamamlanmıştı; bu tur yalnızca "token'ı SecureStorage'a yaz" bölümünü kapattı — refresh-token yenileme protokolü kasıtlı olarak kapsam dışı bırakıldı. Mimari: `RepoPulse.Core.Authentication` altında MAUI'den bağımsız, test edilebilir bir katman — `PersistedSessionPayload` (versioned, düz POCO record: `Version`, `AccessToken`, `RefreshToken`, `Login`, `AvatarUrl`, `AccessTokenExpiresAtUtc`; tip metadata'sı veya polimorfik serileştirme yok), `PersistedSessionPayloadValidator` (JSON parse + doğrulama: sürüm/eksik alan/geçersiz avatar URL'i/aşırı uzun alan/süresi dolmuş token'ı ayrı, adlandırılmış `PersistedSessionRejectionReason` değerleriyle reddeder; 2 dakikalık clock-skew toleransı), `ISecureSessionStorage` soyutlaması ve bunu `UserSessionStore` ile birlikte tek bir `SemaphoreSlim` kapısı altında yöneten `SessionPersistenceStore` (save/restore/sign-out'un asla iç içe geçmemesini garanti eder). Tüm SecureStorage çağrıları (ve olası exception'ları) yalnızca `SessionPersistenceStore` içinde ele alınıyor — MAUI projesindeki `MauiSecureSessionStorage`, `SecureStorage.Default`'a sabit anahtar (`repopulse.auth.session.v1`) üzerinden **tamamen sessiz bir pass-through**; bu sayede Android'in yedekleme/geri yükleme sonrası çözülemeyen şifreli değer senaryosu dahil her hata yolu, gerçek bir MAUI host'u olmadan düz unit testlerle kapsandı, `RemoveAll()` hiçbir yerde çağrılmıyor. Oturum kaydetme: `LoginPage`, `GET /user` başarılı olduktan sonra `SessionPersistenceStore.SignInAsync`'i çağırıyor — bu, SecureStorage yazımı başarılı olmadan `UserSessionStore`'u **hiç doldurmuyor**; yazım başarısız olursa bellek temizleniyor ve yalnızca genel bir hata mesajı gösteriliyor. Cold-start restore: yeni bir `BootstrapPage`, AppShell'in `CurrentItem`'ı olarak eklendi (RP-007'nin `LoginContent`'inin yerini aldı) — `OnAppearing`'de asenkron olarak `RestoreAsync`'i çağırıp sonucuna göre `//repositories` veya `//login`'e mutlak rota ile geçiyor; hiçbir ağ isteği yapmıyor (yalnızca yerel SecureStorage okuma + doğrulama), böylece LoginPage hiçbir zaman kısa süre görünüp kaybolmuyor. Bozuk JSON/desteklenmeyen sürüm/eksik alan/geçersiz avatar URL'i/aşırı uzun alan/süresi dolmuş token içeren bir kayıt sessizce reddedilip yalnızca uygulamanın kendi anahtarı kaldırılıyor. Çıkış yapma: önce persistent anahtar kaldırılıyor, yalnızca bu başarılı olursa bellek temizlenip `//login`'e gidiliyor — persistent silme başarısız olursa (ör. exception) eski oturumun "kaybolmuş gibi görünüp" bir sonraki başlangıçta sessizce geri gelmesini önlemek için bellek de temizlenmiyor, kullanıcıya genel bir hata gösterilip yeniden deneme imkânı sunuluyor. 401 davranışı: restore edilmiş bir token GitHub tarafından sonradan reddedilirse (`GitHubRepositoryFailureKind.Unauthorized`), `RepositoryListPage` hem persistent hem bellek içi oturumu temizleyip `Login`'e yönlendiriyor — bu özel yolda, persistent silmenin başarısı beklenmiyor (GitHub'ın zaten geçersiz saydığı bir token'la "signed in" görünmeye devam etmek, diskte kalabilecek bir artık değerden daha zararlı kabul edildi). 202/202 `RepoPulse.UnitTests` (46 yeni test: `PersistedSessionPayloadValidatorTests`, `SessionPersistenceStoreTests` — sahte `ISecureSessionStorage` ile save/restore/sign-out'un eş zamanlı asla iç içe geçmediğini doğrulayan bir test dahil — artı `NavigationGuardTests`/`UserSessionStoreTests`'e küçük eklemeler) ve 51/51 `RepoPulse.AuthApi.Tests` geçiyor; RP-006/RP-007'nin mevcut testlerinde regresyon yok. Testlerde gerçek token hiç kullanılmadı, yalnızca açıkça sahte (`FAKE-...-TEST-ONLY`, `MARKER-...`) fixture'lar kullanıldı. **Gerçek Pixel_6_API36 emülatöründe, kullanıcının kendi GitHub hesabıyla, gerçek canlı staging backend'i üzerinden uçtan uca doğrulandı:** temiz kurulumda `LoginPage` → GitHub ile giriş → `RepositoryListPage` → **yalnızca `adb shell am force-stop`** (uninstall/veri temizleme değil) → yeniden açılışta Login'e hiç uğramadan doğrudan `RepositoryListPage`'e gelindi → repository araması hâlâ çalıştı → Ayarlar → Çıkış yap → Login'e dönüldü → tekrar force-stop + yeniden açılış → **Login gösterildi, eski oturum geri gelmedi**. `adb logcat` tüm oturum boyunca taranarak sıfır `FATAL EXCEPTION` ve sıfır token/refresh token/code/state/verifier/secret sızıntısı doğrulandı; SecureStorage'ın kendi dosya/değeri hiçbir noktada okunmaya çalışılmadı. Test sonrası emülatör kontrollü olarak (`adb emu kill`) kapatıldı.

**RP-009 (Faz 1 `#9`'un eksik kalan bölümü: `GET /user/repos` ile giriş yapan kullanıcının repository listesi) — durum: TAMAMLANDI — yalnızca Core/API katmanı, UI bağlantısı yok.** `#9`'un repo-detay yarısı zaten RP-006'da tamamlanmıştı (`GetRepositoryAsync`, yeniden yazılmadı); bu tur yalnızca repo-listesi uç noktasını ekledi. `IGitHubApiClient`'a `GetUserRepositoriesAsync(accessToken, cancellationToken)` eklendi: `GET https://api.github.com/user/repos?sort=updated&direction=desc&per_page=100`, mevcut `GetRepositoryAsync`/`GetCurrentUserAsync` ile aynı `User-Agent`/`Accept`/`X-GitHub-Api-Version` başlıkları ve token-yalnızca-Authorization-header kuralı (ortak `ApplyStandardHeaders` yardımcı metoduna çıkarıldı). Sayfalama: `Link` header'ındaki yalnızca `rel="next"` girdisi bulunuyor, ardından URL doğrudan takip edilmiyor — şema `https`, host tam olarak `api.github.com`, varsayılan port, boş userinfo/fragment, path tam olarak `/user/repos` ve yalnızca beklenen (`page`/`per_page`/`sort`/`direction`) query parametreleri doğrulanıyor; bu kontrollerden biri bile başarısız olursa (GitHub'ın "daha fazla veri var" dediği ama URL'sine güvenilemeyen bir durum) sonuç `IsTruncated=true` ile durduruluyor, hiçbir zaman ham URL'ye güvenilmiyor. Geçerli bir `next` bulunduğunda yalnızca sayfa numarası çıkarılıp istek, sabit `OAuthConstants.RepositoryListEndpoint` üzerinden **kendi** query string'imizle yeniden kuruluyor. En fazla 10 sayfa/1000 repository getiriliyor; sınırda hâlâ `next` varsa veya aynı sayfa numarasına tekrar dönülürse (döngü koruması) `IsTruncated=true` işaretleniyor. İkinci veya sonraki bir sayfa hata (401/403/429/ağ hatası/bozuk JSON) döndürürse sessiz kısmi başarı yerine typed `GitHubRepositoryListResult` hatası dönülüyor — kısmen toplanmış repository'ler asla döndürülmüyor. Sonuçlar `FullName` üzerinden case-insensitive dedupe ediliyor (ilk görülen sıra korunarak), GitHub'ın `updated`/`desc` sıralaması istemci tarafında yeniden sıralanmıyor. Mapping: yeni bir `GitHubRepositoryListResult` modeli (mevcut `GitHubRepositoryFailureKind` enum'u yeniden kullanılıyor — `NotFound` bu çağrı için hiç üretilmiyor), tek-repo ayrıştırma mantığı `TryParseRepositoryElement` adıyla ortak bir yardımcıya çıkarılıp hem `GetRepositoryAsync` hem `GetUserRepositoriesAsync` tarafından paylaşılıyor; `open_issues_count` yine `OpenIssuesAndPullRequests`'e gidiyor, nullable description/language/tarih alanları güvenli işleniyor, `owner`/`name`/`full_name`/`html_url`/`default_branch` alanlarından biri eksik olan bir kayıt tüm sayfayı düşürmeden sessizce atlanıyor (diğer kayıtlar döndürülmeye devam ediyor). Yeni NuGet/Refit paketi eklenmedi. 239/239 `RepoPulse.UnitTests` (31 yeni test: boş/tek/çok sayfalı başarı, 100 kayıtlık tam sayfa, `Link` header yokluğu, case-insensitive dedupe, 10 sayfa sınırı + `IsTruncated`, tekrarlayan next-link döngüsü, bozuk `Link` header, yedi ayrı güvensiz next-link reddi senaryosu (HTTP/yanlış host/port/userinfo/fragment/yanlış path/beklenmeyen query param), 401, 403/429, ilk ve ikinci sayfada ağ hatası/`WebException`, ikinci sayfada API hatasının kısmi başarıyı asla döndürmediği, bozuk JSON, non-array JSON, null alanlar, eksik zorunlu alanlı kaydın atlandığı, cancellation, token'ın hiçbir zaman URL/body dışına sızmadığı, tüm isteklerde beklenen header'ların bulunduğu) ve 51/51 `RepoPulse.AuthApi.Tests` geçiyor; RP-006/RP-007/RP-008'in mevcut testlerinde regresyon yok. Testlerde gerçek token hiç kullanılmadı, yalnızca açıkça sahte fixture'lar (`test-access-token`, `SUPER-SECRET-TOKEN-list-abc123`) kullanıldı; bu tur canlı/gerçek GitHub hesabıyla emülatör doğrulaması **kasıtlı olarak yapılmadı** — API-only, UI'a bağlanmamış bir katman olduğu için tamamen fixture tabanlı testlerle kapsandı (bkz. RP-009 kapsam tanımı). `RepositoryListPage`'e bağlama, arama/sıralama/favoriler, SQLite/cache, pull-to-refresh, OAuth scope değişikliği bu turun kapsamı dışında bırakıldı — sıradaki UI/persistence turlarının konusu.

**RP-010 (Faz 1 `#11`'in yalnızca "gerçek repository listesini gösterme" dilimi) — durum: TAMAMLANDI.** RP-009'daki `GetUserRepositoriesAsync` yeniden yazılmadan doğrudan kullanıldı; `#10` (SQLite/offline cache) tamamlanmış sayılmıyor, `#11`'in arama/sıralama/favoriler bölümü de açık bırakıldı — yalnızca gerçek listenin ekranda gösterilmesi bu turun kapsamı. Mimari: `RepoPulse.Core.Repositories` altında MAUI'den bağımsız, test edilebilir bir `RepositoryListController` (yükleme/eşzamanlılık koruması/"aynı token için tekrar yükleme yok" kararını `SessionPersistenceStore`'un `SemaphoreSlim` deseniyle aynı ruhla yönetiyor — ancak burada tek bir "in-flight" bayrağı yeterli) ve saf, MAUI'den bağımsız bir `RepositoryListItem` (yıldız/fork/dil/tarih/rozet metinlerini `GitHubRepository`'den türetir; alan biçimlendirmesi `RepositoryDetailPage`'in mevcut Türkçe ifadeleriyle birebir tutarlı). `RepositoryListPage`, `CollectionView`'ı (sanallaştırılmış, `x:DataType` ile derlenmiş binding kullanan `ItemTemplate`) sayfanın kökü yaptı; RP-006'nın tekil `owner/repository` araması `CollectionView.Header`'a taşındı (kaldırılmadı, yalnızca "GitHub'da Repository Aç" başlıklı ayrı bir bölüm haline geldi) — bu sayede tek bir sanallaştırılmış kaydırma yüzeyinde iki özellik bir arada, `ScrollView` içine iç içe `CollectionView` koyma anti-pattern'i olmadan. Yükleme/boş/hata durumları `CollectionView.EmptyView` içinde yönetiliyor. Seçim: `SelectionMode="Single"`, seçilen öğe navigasyondan hemen sonra `SelectedItem = null` ile temizleniyor (aynı repo tekrar seçilebilir); tekil aramadaki "Detayları Gör" butonuyla PAYLAŞILAN tek bir `isNavigatingToDetail` bayrağı, iki giriş noktasından biri navigasyondayken diğerinin ikinci bir navigasyon başlatmasını engelliyor. Yeniden yükleme kontrolü: `RepositoryListController.HasLoadedFor(accessToken)` — sayfa `OnAppearing`'de yalnızca daha önce BAŞARIYLA yüklenmemişse VEYA token değişmişse (çıkış+farklı/yeni girişten sonra) yeniden yükleme tetikliyor; `RepositoryDetailPage`'den veya `SettingsPage`'den geri dönüşte (aynı token, aynı Shell sayfa örneği) hiçbir gereksiz istek atılmıyor ve `CollectionView`'ın kaydırma konumu doğal olarak korunuyor. İptal: `OnDisappearing`'de bir "navigasyon nedeniyle iptal edildi" bayrağı önce set edilip ardından `CancellationTokenSource.Cancel()` çağrılıyor — bu sayede yakalanan `OperationCanceledException`, sayfa kapanışından mı yoksa gerçek bir zaman aşımından mı geldiğini ayırt edip yalnızca gerçek zaman aşımını kullanıcıya hata olarak gösteriyor, iptali asla hata olarak göstermiyor. Hata/eksik-veri davranışı: başarısız bir yeniden yükleme (`RateLimited`/`NetworkError`/`Unexpected`) `RepositoryListController` içinde önceki başarılı listeyi asla silmiyor — yalnızca durumu değiştiriyor; `Unauthorized` RP-006/008'den değişmeden gelen `HandleInvalidSessionAsync` ile aynı sign-out+Login akışını kullanıyor. Boş liste "Henüz görüntülenecek repository bulunamadı." mesajıyla normal bir başarı olarak gösteriliyor; `IsTruncated=true` "Repository listesinin yalnızca bir bölümü gösteriliyor." banner'ıyla engelleyici olmayan biçimde bildiriliyor. Açık/koyu tema: yalnızca mevcut `Colors.xaml`/`Styles.xaml` kaynakları (`AppThemeBinding`, `Gray*`, hata metinleri için RP-006'nın zaten kullandığı `Crimson`/`OrangeRed` deseni) kullanıldı, yeni hardcoded platform rengi eklenmedi; yeni NuGet paketi eklenmedi. 276/276 `RepoPulse.UnitTests` (25 yeni test: `RepositoryListControllerTests` — başarılı/boş/truncated/Unauthorized/RateLimited/NetworkError-önceki-listeyi-koruma/Unexpected/eşzamanlı-ikinci-çağrının-engellenmesi/cancellation-state'i-bozmadan-fırlatma/`HasLoadedFor` sözleşmesi — ve `RepositoryListItemTests` — alan biçimlendirme + `RepositoryNavigationQueryBuilder` ile yalnızca repository nesnesinin taşındığının doğrulanması) ve 51/51 `RepoPulse.AuthApi.Tests` geçiyor; RP-006/007/008/009'un mevcut testlerinde regresyon yok. Testlerde gerçek token hiç kullanılmadı, yalnızca `test-access-token` gibi açıkça sahte fixture'lar kullanıldı. **Gerçek Pixel_6_API36 emülatöründe, kullanıcının kendi GitHub hesabıyla, gerçek canlı staging backend'i üzerinden uçtan uca doğrulandı:** temiz kurulumda GitHub ile giriş (mevcut tarayıcı oturumuyla "Continue") → `RepositoryListPage`'e mutlak geçiş, gerçek repository listesi (`mustafanazli/RepoPulse`, `mustafanazli/CoreShift` — açıklamalı, ve daha fazlası) `updated`/`desc` sırasıyla yüklendi → liste kaydırıldı (sanallaştırma sorunsuz) → bir öğeye dokunuldu → `RepositoryDetailPage` doğru alanlarla açıldı → geri dönüldü → **liste ve kaydırma konumu birebir korundu**, yeniden yükleme olmadı → aynı öğeye tekrar dokunuldu, tekrar açıldı (seçim doğru temizlenmiş) → tekil `owner/repository` araması (`mustafanazli/RepoPulse`) hâlâ çalıştı, "Detayları Gör" ile detay sayfası açıldı → **yalnızca `adb shell am force-stop`** + yeniden açılış: Login'e hiç uğramadan doğrudan `RepositoryListPage`'e gelindi ve liste yeniden (yeni bir process olduğu için beklendiği gibi) yüklendi → Wi-Fi/mobil veri `adb shell svc` ile kapatılıp force-stop + yeniden açılışta çökme olmadan "GitHub'a ulaşılamadı." mesajı gösterildi, ağ geri açılıp tekrar force-stop + açılışta liste normal yüklendi. `adb logcat` tüm oturum boyunca taranarak RepoPulse'a ait sıfır `FATAL EXCEPTION` ve sıfır token/refresh token/code/state/verifier/secret sızıntısı doğrulandı (loglardaki iki `FATAL EXCEPTION` girdisi emülatördeki tamamen ilgisiz bir üçüncü taraf uygulamasına — `com.example.saat_kronometre` — ait olduğu paket adıyla doğrulandı). Test sonrası emülatör kontrollü olarak (`adb emu kill`) kapatıldı.

**RP-011 (Faz 1 `#11`'in "arama + sıralama" dilimi) — durum: TAMAMLANDI.** RP-010 zaten yüklenmiş `RepositoryListController.State.Repositories` listesini değiştirmeden bırakıyor; `#10` (SQLite/offline cache) tamamen açık kalıyor, `#11`'in favoriler bölümü de açık bırakıldı — yalnızca zaten yüklü listenin istemci tarafında arama+sıralaması bu turun kapsamı. GitHub API'ye (pagination dahil) hiçbir değişiklik yapılmadı. Mimari: `RepoPulse.Core.Repositories` altında MAUI'den bağımsız, saf bir `RepositorySortOrder` enum'u (`UpdatedDescending` varsayılan, `NameAscending`) ve saf, statik bir `RepositoryListProjection.Apply(repositories, searchText, sortOrder)` fonksiyonu eklendi — `IReadOnlyList<GitHubRepository>` alıp yeni bir salt-okunur liste döndürür, kaynak listeyi asla mutate etmez, token/session bilgisi taşımaz (aynı `GitHubRepository` örneklerini yalnızca filtreler/yeniden sıralar, yeni bir sarmalayıcı tip üretmez) ve hiçbir ağ isteği üretemez (imzasında `IGitHubApiClient`/`CancellationToken` yok). Arama: `FullName`+`Description` üzerinde `StringComparison.OrdinalIgnoreCase` ile case-insensitive substring eşleştirmesi, sorgu trim'leniyor, boş/null sorgu tüm listeyi döndürüyor, null `Description` güvenle atlanıyor. Sıralama: `UpdatedDescending`'de `UpdatedAt` değeri olanlar önce (comparer'ın null semantiğine güvenmeden `HasValue` açıkça önce karşılaştırılıyor), sonra `FullName` ile deterministik tie-break; `NameAscending`'de `StringComparer.OrdinalIgnoreCase` + ordinal tie-break. `RepositoryListPage`, arama metnini/sıralama seçimini kendi sayfa-yerel alanlarında tutuyor (controller'a veya `State`'e asla yazmıyor) — bu sayede RP-010'un `HasLoadedFor`/session-generation yeniden-yükleme sözleşmesi bire bir korunuyor; her render sonrası (başarılı/başarısız/yeni session generation farketmeksizin) `ApplyRepositoryListProjection` çağrılarak güncel arama/sıralama, güncel `latestRepositories`'e yeniden uygulanıyor. **Canlı emülatör testinde bulunan gerçek bir odak kaybı hatası düzeltildi:** `CollectionView.Header` (aramanın kendisinin içinde yaşadığı bölüm) MAUI Android'de öğelerle aynı `RecyclerView`'da barındırıldığından, ilk uygulamada her tuş vuruşunda `RepositoryItems.Clear()` + yeniden `Add()` bir `Reset` olayı doğurup adapter'ın tamamını (header dahil) geçersiz kılıyor, bu da arama kutusunun IME odağını her tuş vuruşunda düşürüyordu; düzeltme, `RepositoryItems`'i `Clear()` hiç kullanmadan yalnızca `Remove`/`Insert`/`Move`/indeks-ataması ile minimal-fark senkronizasyonuyla güncelleyen bir `SyncRepositoryItems` metoduna geçti — bu hem gerçek kullanıcı deneyimini düzeltti hem de gereksiz `IsTruncated`/hata durumlarının render'ını bozmadı. UI: "GitHub'da Repository Aç" bölümü hiç değişmeden korundu; ayrı, açıkça başlıklı yeni bir "Repository'lerim" bölümü eklendi — `SearchBar` (placeholder: "Listemde ara", yerleşik erişilebilir temizleme düğmesiyle) ve iki seçenekli bir `Picker` ("Son güncellenen" varsayılan, "Ada göre A-Z"); kaynak liste dolu ama arama sonucu boşsa yeni "Eşleşen repository bulunamadı." mesajı (RP-010'un "Henüz görüntülenecek repository bulunamadı." mesajından ayrı ve önceliksiz), `IsTruncated` banner'ı ve hata mesajları arama/sıralama sırasında bozulmadı; liste yüklenirken arama kutusu/sıralama seçici devre dışı bırakılıyor. Yeni `ScrollView`/NuGet paketi eklenmedi, sanallaştırma korundu, light/dark tema kaynakları değişmeden kullanıldı. 303/303 `RepoPulse.UnitTests` (27 yeni test: `RepositoryListProjectionTests` — null/boş/whitespace sorgu, FullName/Description eşleşmesi, case-insensitivity, null description, sıfır eşleşme, her iki sıralama modu + null-UpdatedAt + eşit-tarih/eşit-isim tie-break'leri, filtre+sıralama birlikte, kaynak listenin mutate edilmemesi, sonuçların aynı `GitHubRepository` referanslarını taşıması, `Apply`'ın stateless olduğu — ve `RepositoryListControllerTests`'e eklenen 2 entegrasyon testi: projeksiyonun tekrar tekrar uygulanmasının sahte handler'ın istek sayısını asla artırmadığı, yeni bir session generation yeniden yüklemesinde aynı projeksiyonun yeni kaynak listeye doğru uygulandığı) ve 51/51 `RepoPulse.AuthApi.Tests` geçiyor; RP-006/007/008/009/010'un mevcut testlerinde (token/session güvenliği ve `HasLoadedFor` sözleşmesi dahil) regresyon yok. **Gerçek Pixel_6_API36 emülatöründe, kullanıcının kendi GitHub hesabıyla, gerçek canlı staging backend'i üzerinden uçtan uca doğrulandı:** cold start → mevcut SecureStorage oturumuyla doğrudan `RepositoryListPage`, gerçek liste yüklendi → arama kutusuna "RepoPulse" yazıldı, liste anında tam olarak `mustafanazli/RepoPulse`'a daraldı (odak kaybı olmadan, düzeltme sonrası) → eşleşmeyen bir sorguda "Eşleşen repository bulunamadı." doğru gösterildi → arama temizlendi, tam liste geri geldi → sıralama "Ada göre A-Z" olarak değiştirildi, liste anında alfabetik olarak yeniden sıralandı → `mustafanazli/CoreShift`'e dokunulup `RepositoryDetailPage` doğru alanlarla açıldı → geri dönüldü, **sıralama seçimi ("Ada göre A-Z") ve liste birebir korundu, gereksiz yeniden yükleme olmadı** → "GitHub'da Repository Aç" tekil araması bölümü değişmeden yerinde duruyordu. `adb logcat` taranarak RepoPulse'a ait sıfır `FATAL EXCEPTION` ve sıfır token/refresh token/code/state/verifier/secret sızıntısı doğrulandı. Test sonrası emülatör kontrollü olarak (`adb emu kill`) kapatıldı.

**RP-012 — Kalıcı SQLite repository favorileri — TAMAMLANDI.** Faz 1 `#10`'un yalnızca minimum SQLite şeması/altyapısı bölümünü ve `#11`'in kalan "favorilere ekleme" dilimini birlikte kapatır; tam repository offline cache'i (stars/description/language/vb. API alanlarının saklanması) bilinçli olarak bu turun dışında bırakıldı — yalnızca favori repository kimliği (`Owner`, `Name`, `NormalizedFullName`) ve `AddedAtUtc` kalıcı. Paket: `sqlite-net-pcl` 1.11.285 (tek paket referansı; transitif bağımlılıkları — `SourceGear.sqlite3`, `SQLitePCLRaw.core`, `SQLitePCLRaw.provider.e_sqlite3` — restore/build/runtime ile doğrulandı, ayrıca bir `SQLitePCLRaw.bundle_green` veya başka native provider eklenmedi; EF Core/Dapper yok). Katman: yeni `src/RepoPulse.Infrastructure` (net10.0 class library, yalnızca burada `sqlite-net-pcl`, `RepoPulse.Core`'a referans) içinde `SqliteFavoriteRepositoryStore : IFavoriteRepositoryStore` — DB absolute path'i `SqliteFavoriteRepositoryStoreOptions` ile dışarıdan alır, hiçbir MAUI API'sine (ör. `FileSystem`) bağımlı değildir; gerçek yol yalnızca `MauiProgram`'da `Path.Combine(FileSystem.AppDataDirectory, "repopulse.db3")` ile kurulur. `RepoPulse.Core.Repositories` içine MAUI/SQLite-bağımsız `FavoriteRepository` domain modeli, `FavoriteRepositoryIdentifier` (RP-011'in `OrdinalIgnoreCase` kimlik semantiğiyle tutarlı, `ToLowerInvariant` ile culture-bağımsız normalizasyon), `IFavoriteRepositoryStore`, typed `FavoriteStoreResult`/`FavoriteListResult`/`FavoriteStatusResult` (boş liste ile store hatası her zaman ayrık), `FavoriteToggleController` (aynı DI singleton'ı hem `RepositoryListPage` hem `RepositoryDetailPage` kullanır — biri değişince diğeri anında yansır; aynı kimlik için double-tap `Ignored` ile engellenir; token/session hiç taşınmaz) ve `FavoriteRowProjection` (favorileri canlı listeyle birleştirip `RepositoryListItem`/`FavoriteIdentityRow` karışımı üretir) eklendi. Schema: `FavoriteRepositories(NormalizedFullName TEXT PRIMARY KEY, Owner TEXT NOT NULL, Name TEXT NOT NULL, AddedAtUtc INTEGER NOT NULL)`, `PRAGMA user_version` ile v1; initialization tek bir path-keyed `SemaphoreSlim` üzerinden serialize edilir (concurrent init/add/remove güvenli), tablo oluşturma + `user_version` yazımı tek transaction içinde; desteklenmeyen gelecek şema versiyonu DB'ye dokunmadan `UnsupportedSchema` döner; corrupt/IO hataları ham exception/path/SQL sızdırmadan `Corrupt`/`IoError`/`Unexpected` olarak tiplenir. `AddAsync` idempotent (casing farkı yeni satır oluşturmaz, mevcut `AddedAtUtc` korunur), tüm sorgular parametreli/tip-güvenli API üzerinden (string interpolation ile SQL üretimi yok). UI: mevcut tek `RepositoryCollectionView` üzerinde `RepositoryListRowTemplateSelector` ile "Tümü" (`RepositoryListItem`, artık `IsFavorite`/`FavoriteToggleLabel` alanlarıyla) ve "Favoriler" (`FavoriteRows` — canlı eşleşme varsa tam kart, yoksa yalnızca `Owner`/`Name`/`AddedAtUtc` + "Ayrıntılar için bağlantı gerekli." mesajıyla `FavoriteIdentityRow`) arasında yeni bir "Görünüm" `Picker`'ı ile geçiş yapılıyor; favori toggle butonu satır seçimini/navigasyonu tetiklemiyor; `RepositoryListItemSynchronizer`'a eklenen generic `Sync<TItem>` aşırı yüklemesi (eski `RepositoryListItem`-özel imza değişmeden, ona delege ederek) favori değişikliklerini de `Clear`/`Reset` kullanmadan indexer-replace ile uyguluyor. Offline identity-only satıra dokunmak güvenli bir tekil `GET /repos/{owner}/{name}` denemesi yapıyor; başarısız olursa (örn. ağ yok) çökmeden `FavoriteToggleErrorLabel` ile kısa, non-blocking bir mesaj gösteriyor ve mevcut liste çalışmaya devam ediyor. 362/362 `RepoPulse.UnitTests` (RP-011'in 303'üne +59: normalize/culture-independence, `FavoriteToggleController` — idempotent toggle/double-tap engeli/store hatası/token alanı yokluğu, `FavoriteRowProjection` — online/offline/sıralama/arama/mutasyon-yok, senkronizatörün generic aşırı yüklemesi ve favori-indexer-replace-no-reset testleri, gerçek SQLite'a karşı 16 entegrasyon testi — fresh schema, `user_version`, add/get/remove/is-favorite, case-insensitive uniqueness, idempotent add + `AddedAtUtc` korunması, close/reopen kalıcılığı, concurrent init/add, unsupported/corrupt/unwritable-path senaryoları, boş-liste-vs-hata ayrımı — yalnızca izole geçici test dizinlerinde, kontrollü temizlik) ve 51/51 `RepoPulse.AuthApi.Tests` geçiyor; RP-006–RP-011'in mevcut testlerinde regresyon yok. **Gerçek Pixel_6_API36 emülatöründe, kullanıcının kendi GitHub hesabıyla uçtan uca doğrulandı:** temiz kurulum → GitHub OAuth ile giriş (gerçek staging backend) → `mustafanazli/RepoPulse` favorilere eklendi (buton anında "Favorilerden çıkar"a döndü, navigasyon tetiklenmedi) → detay sayfasında aynı favori durumu doğrulandı (liste↔detay tutarlılığı) → "Favoriler" görünümünde tam kart olarak göründü → uygulama force-stop edilip yeniden açıldı, favori hâlâ işaretliydi (SQLite kalıcılığı) → Wi-Fi/mobil veri kapatılıp force-stop/relaunch yapıldı, "Favoriler" görünümünde yalnızca owner/name + "Eklendi: ..." + "Ayrıntılar için bağlantı gerekli." gösterildi, satıra dokunulduğunda çökmeden "GitHub'a ulaşılamadı." mesajı çıktı → offline'da favoriden çıkarma (salt yerel SQLite işlemi) başarıyla çalıştı → ağ geri açılıp force-stop/relaunch sonrası kaldırma kalıcı kaldı, favori geri gelmedi. `adb logcat` taranarak tüm oturum boyunca RepoPulse'a ait sıfır `FATAL EXCEPTION`, sıfır token/secret/SQL/path sızıntısı doğrulandı. Test sonrası emülatör kontrollü olarak (`adb emu kill`) kapatıldı.

**RP-012 düzeltmesi — hesaplar arası favori izolasyonu.** Yukarıdaki ilk sürüm `FavoriteRepositories` tablosunu tek bir global tablo olarak tasarlamıştı — `NormalizedFullName` tek başına birincil anahtardı ve favoriler hiçbir GitHub hesabına göre ayrılmıyordu, yani aynı cihazda farklı bir hesapla oturum açıldığında önceki hesabın favorileri görünür kalıyordu. PR henüz merge edilmediği için (üretimde veri yok) düzeltme doğrudan v1 şemasına uygulandı, ayrı bir v1→v2 migration eklenmedi: `PRAGMA user_version` hâlâ 1. Yeni schema: `FavoriteRepositories(AccountLoginNormalized TEXT NOT NULL, NormalizedFullName TEXT NOT NULL, AccountLogin TEXT NOT NULL, Owner TEXT NOT NULL, Name TEXT NOT NULL, AddedAtUtc INTEGER NOT NULL, PRIMARY KEY (AccountLoginNormalized, NormalizedFullName))` — birincil anahtar artık (hesap, repository) bileşik kimliği. `AccountLoginNormalized`, `FavoriteRepositoryIdentifier.TryNormalizeAccountLogin` ile (`Trim` + `ToLowerInvariant`, token/hash asla kullanılmaz) hesaplanıyor — `UserSession.Login` (GitHub kullanıcı adı, zaten non-sensitive) kaynaklı, erişim token'ı hiçbir zaman DB'ye yazılmıyor. `IFavoriteRepositoryStore`'un dört veri metodu (`GetAllAsync`/`AddAsync`/`RemoveAsync`/`IsFavoriteAsync`) artık ilk parametre olarak `accountLogin` alıyor. `FavoriteToggleController` artık `UserSessionStore`'a bağımlı: `LoadAsync` yerine `EnsureLoadedForCurrentSessionAsync(CancellationToken)` — RP-010'un `HasLoadedFor` desenini birebir taklit ederek yalnızca `SessionGeneration` değiştiğinde yeniden yükleniyor (ilk açılış, çıkış, hesap değişimi ve **aynı hesaba tekrar giriş** dahil — her `SignIn`/`SignOut` `SessionGeneration`'ı arttırdığı için); oturum kapatıldığında (`Current is null`) bellekteki favori state'i store'a hiç dokunmadan anında temizleniyor, böylece bir sonraki hesabın render'ı asla önceki hesabın verisini göstermiyor. `ToggleAsync` her çağrıda `UserSessionStore.Current.Login`'i taze okuyor (asla cache'lemiyor). `RepositoryListPage`/`RepositoryDetailPage`'in dışa dönük favori API'si (`IsFavorite`/`ToggleAsync`/`Favorites`) değişmedi; yalnızca `RepositoryListPage.OnAppearing`'in yükleme tetikleyicisi "sayfa ömrü boyunca bir kez" yerine "her `SessionGeneration` değişiminde" olacak şekilde güncellendi. Eski (hesap-ayrımsız) şekilde bir emülatör geliştirme veritabanı varsa kod içinde otomatik/yıkıcı bir migration eklenmedi — kontrollü elle temizlik (uygulamayı kaldırıp yeniden kurmak veya `pm clear`) ile yeniden oluşturulması gerekiyor; üretimde veri olmadığı için bu bilinçli bir tercih. Test sayısı düzeltmesi: bir önceki paragrafın "303'e +59" ifadesi hatalıydı (bir önceki PR #13 farkıyla karıştırılmıştı) — doğru taban 312 idi, ilk RP-012 sürümü 312→362 (+50) getirdi; bu izolasyon düzeltmesi 362→**386** (+24) getirdi (hesap-kapsamlı normalizasyon, `FavoriteToggleController`'ın hesaplar arası izolasyon/logout-login/concurrent-hesap testleri, gerçek SQLite'a karşı hesap-izolasyonu + casing + concurrent-iki-hesap entegrasyon testleri, `FavoriteRepositoryRow`'da token/secret/hash alanı olmadığının yapısal kanıtı). 386/386 `RepoPulse.UnitTests` ve 51/51 `RepoPulse.AuthApi.Tests` geçiyor.

**RP-013 — Repository detayında gerçek son commit bilgisi — TAMAMLANDI.** Faz 1 `#12`'yi kapatır: repository detay ekranındaki tek eksik alan (son commit tarihi) artık `GET /repos/{owner}/{repository}/commits?per_page=1` ile gerçek veriden doluyor; dil/star/fork/açık-issue-PR zaten RP-006'dan beri gerçek API'den geliyordu. `IGitHubApiClient.GetLatestRepositoryCommitAsync` eklendi (MAUI-bağımsız, `RepoPulse.Core`), owner/repository segmentleri `GetRepositoryAsync` ile aynı şekilde percent-encode ediliyor, tek sayfa (`per_page=1`, pagination yok) yeterli. Yeni typed model: `GitHubLatestCommit` (yalnızca `CommittedAtUtc` — ilk sürümde bulunan opsiyonel `ShortSha`/`MessageSummary` alanları, RP-013 sonrası güvenlik/yarış denetiminde hiçbir UI kullanımı olmayan ölü veri olduğu tespit edilip tamamen kaldırıldı; parser artık sha/message hiç okumuyor, veri minimizasyonu) ve `GitHubLatestCommitResult`/`GitHubLatestCommitFailureKind` — "repository var ama commit yok" (200+boş dizi veya GitHub'ın 409 "empty repository" yanıtı) açıkça bir BAŞARI şekli (`NoCommits`), asla bir hata değil. Tarih semantiği: önce `commit.committer.date`, yoksa `commit.author.date`, ikisi de yoksa/parse edilemiyorsa `Unexpected` — `pushed_at`/`updated_at` asla yerine kullanılmıyor, hiçbir tarih uydurulmuyor. 401 → mevcut RP-008 oturum-geçersizleştirme akışı; 403/429 → `RateLimited`; 404 → `NotFound`; ağ hatası/`WebException`/timeout → `NetworkError`; malformed/non-array JSON veya iki tarih de eksik → `Unexpected`; her durumda ham GitHub gövdesi/exception/token UI'ya veya loga hiç sızmıyor (yalnızca enum). `RepositoryDetailPage`: yeni "Son commit" etiketi + erişilebilir `ActivityIndicator`; çift eşzamanlı yükleme ve gecikmiş-eski-isteğin yeni durumu ezmesi, RP-013 sonrası güvenlik/yarış denetiminde eklenen MAUI-bağımsız `LatestCommitLoadCoordinator` (monoton artan operasyon kimliği) ile engelleniyor — ilk sürümdeki bare `isLoadingLatestCommit` bool + tekil `CancellationTokenSource` deseni, "yalnızca tek çağrı noktası" varsayımına dayandığı için bu denetimde değiştirildi (ayrıntı: `LatestCommitLoadCoordinatorTests.cs`); `OnDisappearing`'de devam eden istek iptal ediliyor (navigasyon-kaynaklı iptal ile gerçek timeout ayrıştırılıyor, RP-010/011'in aynı deseniyle); RP-012'nin session-race düzeltmesindeki `UserSessionStore.CaptureSnapshot`/`IsCurrent` mekanizması burada da kullanılarak, istek sırasında oturum değişirse (çıkış/hesap değişimi) geç kalan sonuç sayfa state'ine hiç yazılmıyor; access token yalnızca çağrı anında okunuyor, hiçbir alanda (coordinator dahil) saklanmıyor; navigasyon payload'ı değişmedi (token hâlâ yok). Kapsam dışı bırakılanlar (talep edildiği gibi): SQLite/offline cache genişletmesi, favori altyapısı, liste ekranı, commit geçmişi/diff/branch seçici, health score, yeni NuGet paketi — hiçbiri değişmedi. 430/430 `RepoPulse.UnitTests` (397'ye +33: ilk sürümde +21 — endpoint/`per_page=1`, owner/repository encoding, token yalnızca Authorization header'ında, committer→author tarih fallback'i, boş dizi + 409 → `NoCommits`, 401/403/429/404/ağ/`WebException`/timeout/malformed/non-array/iki-tarih-de-yok → typed failure'lar, cancellation propagasyonu; RP-013 sonrası güvenlik/yarış denetiminde +12 daha — `LatestCommitLoadCoordinatorTests.cs`'nin 9 testi (eski operasyonun yeni state'i ezememesi, navigasyon-iptalinin hata göstermemesi, eşzamanlı `OnAppearing`'in tek istek başlatması, session değişince geç sonucun discard edilmesi dahil) + çok uzun/kontrol-karakterli commit mesajının ve geçersiz SHA şeklinin asla sızmadığının kanıtı + `GitHubLatestCommit`'in yalnızca `CommittedAtUtc` taşıdığının reflection kanıtı) ve 51/51 `RepoPulse.AuthApi.Tests` geçiyor; RP-006–RP-012'nin mevcut testlerinde regresyon yok. Sayfa-seviyesi "çift yükleme engeli" ve "geç kalan session sonucu discard" davranışları artık `LatestCommitLoadCoordinator` üzerinden MAUI-bağımsız olarak doğrudan unit test ediliyor (RepositoryListPage'in `isFetchingFavoriteDetail`/`isNavigatingToDetail` bayrakları gibi sayfa-seviyesi diğer davranışlar hâlâ yalnızca kod incelemesi + gerçek emülatör doğrulamasıyla kanıtlanıyor). **Gerçek Pixel_6_API36 emülatöründe, kullanıcının kendi GitHub hesabıyla, gerçek canlı staging backend'i üzerinden uçtan uca doğrulandı:** GitHub OAuth ile giriş → `mustafanazli/RepoPulse` detayına gidildi, gerçek "Son commit: 27.08.2026 13:22" değeri gösterildi → geri dönülüp aynı repository tekrar açıldı, çökme veya takılı kalan "yükleniyor" durumu olmadan aynı değer tekrar yüklendi → Wi-Fi/mobil veri kapatılıp detay sayfası tekrar açıldığında "Son commit bilgisi alınamadı." güvenli mesajı gösterildi, çökme olmadı → ağ geri açıldı. `adb logcat` tüm oturum boyunca taranarak RepoPulse'a ait sıfır `FATAL EXCEPTION` doğrulandı; taramada yalnızca Android'in kendi `WindowManagerShell` log'larında OAuth authorize isteğinin PKCE `code_challenge`/`state` değerleri (ikisi de tasarım gereği gizli olmayan, tek yönlü/nonce değerler) görüldü — gerçek access/refresh token, `client_secret`, `code_verifier` veya OAuth authorization `code`'u hiçbir logcat satırında bulunmadı. Test sonrası emülatör kontrollü olarak (`adb emu kill`) kapatıldı.

**RP-014 — Faz 1 uçak modu kabul doğrulaması — TAMAMLANDI.** Faz 1'in kalan tek açık çıkış kriterinin (uçak modu: çökmeden güvenli hata) yalnızca doğrudan canlı kanıdı eksik iki akışını kapatır: Login/OAuth ve `RepositoryListPage`'in manuel owner/repository lookup'ı ("GitHub'da Repository Aç"). Kod denetiminde iki gerçek bug bulundu ve düzeltildi: `LoginPage.HandleSuccessfulCallbackAsync` (`ExchangeAsync`+`GetCurrentUserAsync`) ve `RepositoryListPage.OnLookupRepositoryClicked` (`GetRepositoryAsync`) hiçbirinde çağrının kendi 15 saniyelik `CancellationTokenSource`'unun zaman aşımından doğan `OperationCanceledException` yakalanmıyordu — bu üç API metodu, iptal caller'ın KENDİ token'ına atfedilebiliyorsa (anlık ağ reddi değil, gerçekten yavaş/bozuk bir bağlantı) kasıtlı olarak swallow etmeyip rethrow ediyor (bkz. metotların kendi doc comment'leri) — caller yakalamazsa bu, `async void` event handler'dan yakalanmadan kaçıp uygulamayı çökertiyordu. En küçük güvenli düzeltme uygulandı: her iki çağrı noktasına, `RepositoryListPage.OpenFavoriteIdentityRowAsync`'in zaten kullandığı AYNI yerleşik desenle (`catch (OperationCanceledException)` → güvenli tipli/generic mesaj) yerel try/catch eklendi; Core katmanında hiçbir değişiklik yapılmadı (davranış zaten kasıtlıydı, sadece caller'lar eksikti). Bu rethrow-sözleşmesini MAUI-bağımsız olarak sabitleyen 3 yeni test eklendi: `GetRepositoryAsync_CallersOwnTokenCancelled_RethrowsRatherThanSwallowing`, `GetCurrentUserAsync_CallersOwnTokenCancelled_RethrowsRatherThanSwallowing`, `ExchangeAsync_CallersOwnTokenCancelled_RethrowsRatherThanSwallowing`. 433/433 `RepoPulse.UnitTests` (430'a +3) ve 51/51 `RepoPulse.AuthApi.Tests` değişmeden geçiyor.

**Gerçek Pixel_6_API36 emülatöründe, gerçek canlı staging backend'i üzerinden uçtan uca doğrulandı.** Senaryo B (manuel lookup) tam çift yönlü doğrulandı: repository listesi yüklüyken ağ kapatılıp `mustafanazli/RepoPulse` arandı → "GitHub'a ulaşılamadı." güvenli mesajı, mevcut liste/favoriler silinmedi, yanlış navigasyon olmadı, çökme yok → ağ geri açılıp aynı arama tekrar denendiğinde gerçek repository verisiyle başarıyla sonuçlandı. Senaryo A (Login/OAuth): ağ kapalıyken "GitHub ile giriş yap"a basıldığında sistem tarayıcısı GitHub'a ulaşamadı (Chrome'un kendi "No internet" sayfası — uygulama dışı, beklenen davranış); RepoPulse'un kendisi çökmedi ve yarım/kalıcı bir oturum oluşturmadı. Ancak ayrı, gerçek bir UX kısıtı bulundu: bu spesifik alt-senaryoda (tarayıcı hiçbir callback intent'i asla göndermiyor) "GitHub ile giriş yap" butonu kalıcı olarak devre dışı kalıyor — `LoginPage.OnAppearing`'in sistem tarayıcısından dönüşte YENİDEN TETİKLENMEDİĞİ canlı testte doğrulandı (MAUI sayfa yaşam döngüsü olayları, salt Activity resume'de değil, sayfa navigasyonunda tetikleniyor), bu yüzden `OnAppearing` tabanlı bir "terk edilmiş girişimi sıfırla" denemesi etkisiz kaldı ve geri alındı. Sağlam bir düzeltme, Android Activity yaşam döngüsüne (`MainActivity.OnResume`) yeni bir sinyal ekleyip gerçek devam eden bir exchange ile yarışmayacak şekilde tasarlanmayı gerektirir — bu RP'nin "en küçük güvenli düzeltme" sınırını aşıyor, kasıtlı olarak YAPILMADI, ayrı bir gelecek RP adayı olarak işaretlendi (aşağıya bkz.). Ağ geri açılıp normal login gerçek GitHub OAuth ekranından tekrar denendiğinde: ilk denemede gerçek, geçici bir AuthApi `NetworkError`i (muhtemelen Azure Container Apps soğuk başlangıcı) güvenli "Sunucuya ulaşılamadı." mesajıyla doğru şekilde ele alındı, buton tekrar etkinleşti, ikinci denemede gerçek hesapla uçtan uca giriş başarıyla tamamlandı (repository listesi gerçek verilerle yüklendi). `adb logcat` tüm oturum boyunca (~1900 satır) taranarak RepoPulse'a ait sıfır `FATAL EXCEPTION` ve sıfır access/refresh token, `client_secret` veya `code_verifier` sızıntısı doğrulandı. Test sonrası ağ normale döndürüldü, emülatör kontrollü olarak (`adb emu kill`) kapatıldı.

**RP-014 düzeltmesi — terk edilmiş OAuth tarayıcı denemelerinin kurtarılması — TAMAMLANDI.** Yukarıdaki RP-014 turunun sonunda "bilinen, kasıtlı olarak düzeltilmemiş kısıt" olarak işaretlenen davranış (sistem tarayıcısı hiç callback göndermeden kullanıcı uygulamaya döndüğünde giriş butonunun kalıcı olarak devre dışı kalması) bu turda tam olarak düzeltildi — RP-014 talimatındaki "uygulamaya geri dönüldüğünde tekrar kullanılabilir durumda olmalı" kriteri artık karşılanıyor, Faz 1 bu turla birlikte tam anlamıyla kapanmış sayılıyor. Kök neden: MAUI'nin `Page.OnAppearing`/`OnDisappearing` olayları yalnızca Shell/sayfa navigasyonuna bağlı — sistem tarayıcısının üstüne binip inmesiyle tetiklenen ham Android Activity `OnPause`/`OnResume` çiftine hiç bağlı değil (canlı emülatörde iki ayrı denemeyle doğrulandı); bu yüzden düzeltme MAUI sayfa katmanında değil, `MainActivity.OnPause`/`OnResume` seviyesinde yapılmak zorundaydı. Mimari: yeni, MAUI'den tamamen bağımsız `RepoPulse.Core.Authentication.OAuthLoginAttemptCoordinator` — yalnızca monoton artan bir `long` deneme kimliği ve iki `bool` (`isTerminal`, `hasPausedSinceAttemptStart`) taşıyor, tek bir `lock` altında; hiçbir zamanlayıcı/timestamp kullanmıyor, karar tamamen çağrı sırasına dayanıyor. `MainActivity` yalnızca `OnPause`'da `NotifyPaused()`, `OnResume`'da `TryCancelForResumeWithoutCallback()` çağırıyor — hangi ekranın/denemenin aktif olduğunu hiç bilmiyor (`OAuthCallbackBroker.AttemptCoordinator` üzerinden paylaşılan tek örnek). `LoginPage`, tarayıcıyı açmadan hemen önce `StartAttempt()` ile kendi deneme kimliğini alıyor ve gerçek bir callback geldiğinde (`OnOAuthCallbackReceived`'ın en başında, switch'ten önce) `TryConsumeCallback(attemptId)` çağırıyor. Yarış semantiği: Android'in `SingleTop` Activity'sinde gerçek bir callback için `OnNewIntent` her zaman `OnResume`'dan önce çalıştığından, `TryConsumeCallback` ile `TryCancelForResumeWithoutCallback` arasında `lock` altında hangisi önce çalışırsa o "kazanıyor" ve `isTerminal=true` yapıyor — callback her zaman gerçek bir yarışta kazanıyor, geç kalan/tekrarlanan bir callback veya resume sinyali sonrasında asla ikinci bir sonuç üretmiyor; `StartAttempt()` her çağrıldığında yeni bir kimlik verip önceki denemenin kimliğini kalıcı olarak geçersiz kılıyor (eski, iptal edilmiş bir denemenin geç kalan callback'i yeni denemeyle asla eşleşmiyor). "Resume without callback" kazandığında `LoginPage.OnAttemptAbandoned` tetikleniyor: `AuthorizationSessionStore.Reset()` (bekleyen PKCE session'ı temizler, hemen yeni bir deneme başlatılabilir hale getirir) + `EndSignInAttempt()` (butonu yeniden etkinleştirir, `isSignInInProgress` bayrağını sıfırlar) + status etiketini gizler — hepsi uygulama yeniden başlatılmadan. Ekran döndürme, soğuk başlangıç, normal arka plana alma/öne getirme gibi callback-dışı her `OnResume` çağrısı, ya `currentAttemptId == 0` ya `isTerminal` ya da `!hasPausedSinceAttemptStart` kontrollerinden en az biriyle güvenle no-op kalıyor (`MainActivity`'nin `ConfigurationChanges` bildirimi zaten saf ekran döndürmede Activity'yi hiç yeniden başlatmıyor). Token/code/state/verifier coordinator'ın hiçbir alanında tutulmuyor — bunu doğrulayan bir reflection testi de eklendi. 13 yeni unit test (`OAuthLoginAttemptCoordinatorTests.cs`) tüm kritik yarış senaryolarını MAUI'den bağımsız olarak doğrudan doğruluyor: temiz terk edilme, callback-önce-kazanır, resume-önce-kazanır ve geç callback'in reddedilmesi, gerçek eşzamanlı yarışta tam olarak tek bir sonucun üretilmesi (`Task.WhenAll` ile), tekrarlanan resume/callback sinyallerinin ikinci bir sonuç üretmemesi, ekran döndürmenin aktif denemeyi bozmaması, terk edilmenin bekleyen `AuthorizationSessionStore`'u gerçekten temizlediği (gerçek store ile entegrasyon), başarılı callback'in exchange'i tam olarak bir kez başlatması, iptal edilen bir denemeden hemen sonra ikinci girişin anında mümkün olması, ve coordinator'da token/code/state/verifier şeklinde bir alan bulunmadığının yapısal kanıtı. RP-014'ün zaman aşımı/`OperationCanceledException` düzeltmeleri ve onların 3 regresyon testi bu turda değişmeden korundu. 446/446 `RepoPulse.UnitTests` (433'e +13) ve 51/51 `RepoPulse.AuthApi.Tests` geçiyor; yeni NuGet paketi eklenmedi. **Gerçek Pixel_6_API36 emülatöründe, kullanıcının kendi GitHub hesabıyla, gerçek canlı staging backend'i üzerinden uçtan uca doğrulandı:** ağ kapalıyken temiz bir Login ekranından giriş denendi → sistem tarayıcısı Chrome'un kendi "No internet" sayfasını gösterdi → callback hiç gelmeden X ile RepoPulse'a dönüldü → **giriş butonu anında yeniden etkinleşti** (bir önceki turda kalıcı olarak devre dışı kalan aynı senaryonun doğrudan tersi), uygulama çökmedi, süreç canlı kaldı → ağ tekrar açılıp aynı uygulama örneğinde (yeniden başlatma yok) ikinci bir giriş denemesi başlatıldı → gerçek GitHub yetkilendirme ekranı geldi, "Continue"a basıldı, callback tüketildi, token exchange tam olarak bir kez çalıştı → ilk denemede geçici bir AuthApi `NetworkError`i ("Sunucuya ulaşılamadı.") güvenli şekilde gösterildi, üçüncü denemede gerçek hesapla uçtan uca giriş başarıyla tamamlanıp repository listesi gerçek verilerle yüklendi → manuel repository araması için ağ tekrar kapatılıp "GitHub'a ulaşılamadı." güvenli mesajının hâlâ doğru göründüğü doğrulandı (RP-014'ün zaman aşımı düzeltmesinde regresyon yok), ağ açılıp retry gerçek veriyle başarılı oldu. İlk (terk edilmiş) denemenin callback'inin ikinci denemeyle asla eşleşmediği doğrudan canlı olarak zorlanamadı (gerçek tarayıcı asla o denemeye ait bir callback göndermedi) — bu, `OldCallbackFromCancelledAttempt_DoesNotMatchNewAttempt` ve `ResumeBeforeLateCallback_CancelWinsAndLateCallbackIsRejected` unit testleriyle MAUI-bağımsız olarak doğrudan kanıtlanıyor. `adb logcat` tüm oturum boyunca taranarak RepoPulse'a ait sıfır `FATAL EXCEPTION` ve sıfır access/refresh token, `client_secret`, authorization code, state veya `code_verifier` sızıntısı doğrulandı (loglardaki iki `FATAL EXCEPTION` girdisi yine emülatördeki ilgisiz `com.example.saat_kronometre` uygulamasına ait olduğu paket adıyla doğrulandı). Test sonrası ağ normale döndürüldü, emülatör kontrollü olarak (`adb emu kill`) kapatıldı.

**RP-014 düzeltmesi — callback/attemptId entegrasyon denetimi — TAMAMLANDI.** Yukarıdaki paragrafın kapanışından hemen sonra, hedefli bir denetim turunda gerçek bir entegrasyon açığı bulundu ve düzeltildi: `LoginPage.currentAttemptId` her zaman sayfanın KENDİ alanıdır — gerçek bir OAuth deep-link callback'i (yalnızca `code`/`state`/`error` taşır, bkz. `OAuthCallbackParser`) hiçbir zaman bir attempt id taşımaz. Önceki sürümde `OnOAuthCallbackReceived`, `AuthorizationSessionStore` doğrulamasından ÖNCE `coordinator.TryConsumeCallback(currentAttemptId)`'i koşulsuz çağırıyordu; terk edilmiş bir A denemesinin gecikmeli/gerçek callback'i, B denemesi aktifken gelirse, `currentAttemptId` alanı artık B'nin kimliğini tuttuğundan bu çağrı YANLIŞLIKLA B'yi "callback kazandı" olarak terminal yapabiliyordu — B'nin kendi session'ı (`AuthorizationSessionStore`) bu durumda yanlış state nedeniyle reddediliyordu (`TryConsume` başarısız, exchange asla başlamıyordu — bu kısım zaten güvenliydi) ama B'nin session'ı sıfırlanmadan kalıyor ve `EndSignInAttempt()` UI'ı yeniden etkin gösteriyordu — kullanıcı tekrar denediğinde `AuthorizationSessionStore.TryStart` "Zaten devam eden bir giriş denemesi var." ile reddediyordu; oturumun kendi 5 dakikalık ömrü dolana kadar görünüşte etkin ama fiilen kilitli bir buton. Ayrıca Cancelled/Invalid dallarında `sessionStore.Reset()` hep koşulsuz çağrılıyordu — B hâlâ meşru şekilde beklerken, ona ait olmayan bir eski Cancelled/Invalid sinyali B'nin oturumunu telafisiz biçimde silebiliyordu. **Önceki testlerde bu tam olarak modellenmemişti**: `OldCallbackFromCancelledAttempt_DoesNotMatchNewAttempt` doğrudan ESKİ attempt id'yi (`firstAttemptId`) `TryConsumeCallback`'e geçiriyordu — gerçek callback payload'ının hiçbir zaman kendi attempt id'sini taşıyamadığı, `LoginPage`'in her zaman KENDİ güncel alanını (yani yeni denemenin id'sini) kullandığı gerçek entegrasyonu yansıtmıyordu; bu denetimde bu açıkça tespit edildi. Düzeltme: yeni, MAUI'den bağımsız, durumsuz `RepoPulse.Core.Authentication.OAuthCallbackAttemptGate.Evaluate(...)` — Success çıktısı için önce `AuthorizationSessionStore.TryConsume` çağrılıyor (başarısızlıkta oturum tamamen dokunulmadan kalıyor), yalnızca state genuinely eşleştiğinde coordinator'a "callback kazandı" deniyor (ve coordinator'ın kendi dönüş değeri de onurlandırılıyor — eşzamanlı bir resume-without-callback coordinator'ın kilidini önce kazanmışsa exchange ASLA başlamıyor, `ConcurrentValidCallbackAndResume_ExactlyOneTerminalOutcome` testiyle kanıtlandı); Cancelled/Invalid için `coordinator.TryConsumeCallback`'in dönüş değeri kapı görevi görüyor — yalnızca gerçekten hâlâ aktif/terminal-olmayan bir deneme sıfırlanıyor, aksi hâlde no-op. State doğrulaması tamamen `AuthorizationSessionStore`'da kaldı, coordinator'a hiçbir yeni state/token alanı eklenmedi (reflection testiyle kanıtlandı: sınıfın hiç alanı yok). Event/lifetime denetimi ayrıca yapıldı — bulgu: bug yok. `OnAppearing`/`OnDisappearing`, `CallbackReceived` ve `AttemptAbandoned`'ı TEK bir `isSubscribedToOAuthCallbacks` bayrağı altında simetrik olarak abone/çıkış yapıyor (çift abonelik imkânsız); `OnAttemptAbandoned` coordinator'ın kendi kilidi serbest bırakıldıktan SONRA çağrılıyor (deadlock/reentrancy riski yok); tüm UI mutasyonları `MainThread.BeginInvokeOnMainThread` ile sarmalı; soğuk başlangıç/rotasyon `TryCancelForResumeWithoutCallback`'in kendi `currentAttemptId==0`/`isTerminal`/`!hasPausedSinceAttemptStart` kontrolleriyle zaten güvenli no-op. 10 yeni gerçekçi entegrasyon testi eklendi (`OAuthCallbackAttemptGateTests.cs`) — hiçbiri callback'e yapay bir attempt id geçirmiyor, tümü `LoginPage`'in gerçek deseniyle (`currentAttemptId` her zaman çağıranın GÜNCEL bağlamı) eşleşiyor: terk edilmiş A'nın eski state'li callback'i B aktifken asla exchange başlatmıyor ve B'nin kendi denemesini asla kazanmış gibi göstermiyor, eski geçersiz callback UI'ı kurtarılabilir bırakıp C denemesine izin veriyor, geçerli B callback'i exchange'i tam bir kez başlatıyor, doğrulama sırası (validate-then-terminal) kanıtlanıyor, gerçek eşzamanlı callback/resume yarışında tam olarak tek sonuç üretiliyor, geçersiz callback hiçbir session yaratmıyor/kalıcı kılmıyor, callback payload tipinin (`OAuthCallbackResult`) yapısal olarak hiçbir attempt-id alanı taşımadığı kanıtlanıyor. 456/456 `RepoPulse.UnitTests` (446'ya +10) ve 51/51 `RepoPulse.AuthApi.Tests` geçiyor; yeni NuGet paketi eklenmedi; AuthApi/Azure/GHCR/SQLite/repository katmanlarına dokunulmadı.

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
- `#7` AppShell navigasyon akışı: Giriş → Repository Listesi → Repository Detayı → Ayarlar — **RP-007 ile TAMAMLANDI** (kalıcı oturum hâlâ kapsam dışı, bkz. RP-007 girişi)
- `#8` PKCE + custom URI callback akışını uygulamaya entegre et, token'ı SecureStorage'a yaz — **RP-008 ile TAMAMLANDI** (refresh-token yenileme protokolü kasıtlı olarak kapsam dışı, bkz. RP-008 girişi)
- `#9` Typed HttpClient/Refit ile GitHub API istemcisi (repo listesi + repo detay uç noktaları) — **RP-006 (repo detay) + RP-009 (repo listesi) ile TAMAMLANDI** (UI bağlantısı kasıtlı olarak kapsam dışı, bkz. `#11`/`#12` ve RP-009 girişi)
- `#10` SQLite şeması ve yerel önbellek repository'si (repo listesi + detay için) — **yalnızca favoriler için minimum şema/altyapı RP-012 ile TAMAMLANDI** (`FavoriteRepositories` tablosu: kimlik + `AddedAtUtc`); repo listesi/detayının tam offline cache'i (stars/description/language/vb.) kasıtlı olarak açık bırakıldı (bkz. RP-012 girişi)
- `#11` Repository listesi ekranı: arama, sıralama, favorilere ekleme — **gerçek repository listesinin gösterimi RP-010, yerel arama+sıralama RP-011, favorilere ekleme/çıkarma + kalıcılık RP-012 ile TAMAMLANDI**
- `#12` Repository özet/detay ekranı: dil, star, fork, açık issue/PR sayısı, son commit bilgisi — **RP-013 ile TAMAMLANDI**

**Çıkış kriterleri (Milestone: Faz 1)**
- [x] Kullanıcı gerçek GitHub hesabıyla giriş yapıp token alabiliyor; uygulama kapatılıp yeniden açıldığında oturum korunuyor — **RP-008 ile karşılandı**
- [x] Repository listesi gerçek API'den geliyor; arama, sıralama ve favori ekleme/çıkarma çalışıyor ve favoriler cihaz yeniden başlatıldığında kalıcı — **RP-010 (gerçek API), RP-011 (yerel arama/sıralama), RP-012 (favori ekleme/çıkarma + SQLite kalıcılığı) ile karşılandı**
- [x] Repo detay ekranı gerçek verilerle (dil, star, fork, açık issue/PR sayısı, son commit tarihi) doluyor — **RP-006 (dil/star/fork/açık issue+PR) + RP-013 (son commit tarihi) ile karşılandı**
- [x] Ağ bağlantısı kesikken (uçak modu) istek denemesi çöküşe yol açmıyor, hata durumu ekranda gösteriliyor — **RP-010/RP-012/RP-013/RP-014 ile karşılandı**
  - **Kanıt durumu (RP-014 düzeltmesi ile güncellendi, 2026-08-29):** Altı ana ekranın tamamı için ekran bazında kanıt:
    - Repository listesi — **doğrudan canlı kanıt: RP-010** (Wi-Fi/veri `adb shell svc` ile kapatıldı, force-stop+relaunch, "GitHub'a ulaşılamadı." güvenli mesajı, çökme yok)
    - Favoriler/offline görünüm — **doğrudan canlı kanıt: RP-012**
    - Repo detay/son commit — **doğrudan canlı kanıt: RP-013**
    - Tek repository araması (manuel lookup) — **doğrudan canlı kanıt: RP-014** (ağ kapalıyken "GitHub'a ulaşılamadı." güvenli mesajı, liste/favoriler bozulmadı, çökme yok; ağ açılınca retry başarılı; RP-014 düzeltmesi turunda ikinci kez regresyonsuz olarak tekrar doğrulandı)
    - Giriş/OAuth akışı — **doğrudan canlı kanıt: RP-014 + RP-014 düzeltmesi**: RepoPulse'un kendi HTTP istekleri (`ExchangeAsync`/`GetCurrentUserAsync`) tipli ve zaman-aşımı/iptal kaynaklı hatalarda çökmeden güvenli mesaj gösteriyor; `BootstrapPage`'in soğuk başlatma restore akışı hiç ağ çağrısı yapmıyor (RP-008); sistem tarayıcısının hiç callback göndermeden geri dönüldüğü alt-senaryo artık `OAuthLoginAttemptCoordinator` ile tam olarak kapatıldı — giriş butonu uygulama yeniden başlatılmadan anında tekrar kullanılabilir duruma geliyor, bekleyen PKCE session'ı temizleniyor, ikinci deneme aynı uygulama örneğinde uçtan uca başarıyla tamamlanıyor (canlı doğrulandı, bkz. RP-014 düzeltmesi girişi). Daha önce burada not edilen "bilinen UX kısıtı" artık geçerli değil.
    - Ayarlar (`SettingsPage`) — **N/A**: hiçbir ağ çağrısı yapmıyor (`gitHubApiClient`/`HttpClient`/`GetAsync`/`SendAsync` kullanımı yok)

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
