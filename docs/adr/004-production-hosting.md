# ADR-004: RepoPulse.AuthApi için production hosting

## Durum

Accepted — 2026-08-22. **Bu ADR yalnızca bir mimari karardır. Bu turda hiçbir Azure kaynağı oluşturulmadı, hiçbir dağıtım yapılmadı ve hiçbir ücret başlatılmadı.**

## Bağlam

RP-003/RP-004/RP-005 ile RepoPulse.AuthApi, gerçek bir Android emülatöründe, gerçek bir GitHub hesabıyla uçtan uca doğrulandı — ancak yalnızca geliştiricinin kendi makinesinde, `dotnet run` ile ve `https://10.0.2.2:7082` / `https://localhost:7082` development adresleriyle. Bu backend'i gerçek kullanıcılara açmak için, geliştiricinin makinesinden bağımsız, herkese açık bir hosting ortamı gerekiyor.

## Hosting seçimi: Azure Container Apps (Consumption plan)

**Seçildi.** Gerekçe:
- RepoPulse.AuthApi, tamamen stateless, tek bir HTTP endpoint'i (+`/health`) olan minimal bir ASP.NET Core Minimal API — App Service veya AKS gibi daha ağır/daha pahalı seçeneklerin karmaşıklığını gerektirmiyor.
- Consumption plan, **min replica = 0** ile trafik yokken sıfır maliyetli boşta kalmayı destekliyor — bu erken aşama, düşük trafikli bir proje için önemli.
- Container Apps, TLS sonlandırmasını (ingress katmanında) ve HTTP(S) yönlendirmesini yönetilen bir servis olarak sağlıyor; uygulamanın kendi sertifika yönetimiyle uğraşmasına gerek kalmıyor.
- Managed identity + Key Vault entegrasyonu birinci sınıf destekleniyor (aşağıya bakınız).

### Kaynak profili

- **Plan**: Consumption (sunucusuz, istek bazlı ölçeklenen).
- **Min replica**: 0 — trafik yokken hiçbir container çalışmaz, maliyet doğurmaz. (Soğuk başlatma gecikmesi bilinen bir ödünleşimdir; bu erken aşamada kabul edilebilir.)
- **Başlangıç CPU/RAM profili**: küçük — 0.25 vCPU / 0.5 GiB. RepoPulse.AuthApi'nin iş yükü (tek stateless proxy endpoint'i, GitHub'a giden tek bir outbound çağrı) bunun çok üzerine çıkmayı gerektirmiyor; gerçek trafik verisiyle gerekirse büyütülür.
- **Container target port**: **8080** — bkz. repo kökündeki `Dockerfile` (`EXPOSE 8080`, `ASPNETCORE_URLS=http://+:8080`).

## Container mimarisi

`Dockerfile` (repo kökü), yalnızca `src/RepoPulse.AuthApi`'yi hedefleyen çok aşamalı bir build:
- **Build aşaması**: `mcr.microsoft.com/dotnet/sdk:10.0` — restore + `dotnet publish -c Release`.
- **Runtime aşaması**: `mcr.microsoft.com/dotnet/aspnet:10.0` — yalnızca yayımlanan çıktı kopyalanır.
- **Root olmayan kullanıcı**: runtime aşaması `USER $APP_UID` ile çalışır — resmi .NET runtime imajının kendi root-olmayan kullanıcısı, root değil.
- **Yalnızca HTTP 8080**: container içinde hiçbir TLS sonlandırması yok; `EXPOSE 8080` ve `ASPNETCORE_URLS=http://+:8080`.
- Hiçbir secret, token veya development sertifikası image'a kopyalanmıyor (bkz. `.dockerignore`).

## Dış HTTPS: Container Apps ingress tarafından sağlanıyor

Container, yalnızca düz HTTP (8080) dinliyor. Dış HTTPS zorunluluğu ve TLS sonlandırması tamamen **Azure Container Apps ingress katmanı** tarafından sağlanacak — uygulamanın kendisi hiçbir sertifika yönetmiyor.

Bunu desteklemek için `src/RepoPulse.AuthApi/Program.cs`'e `Hosting:BehindTlsTerminatingProxy` adlı, tip güvenli bir konfigürasyon seçeneği eklendi (`HostingOptions.cs`):
- **`false`** (varsayılan, mevcut local/dev davranışı): uygulama kendi `UseHttpsRedirection()`'ını çalıştırır — **değiştirilmedi**.
- **`true`** (Azure Container Apps gibi TLS sonlandıran bir proxy arkasında): uygulama kendi HTTP→HTTPS yönlendirmesini **atlar** — çünkü bu topolojide Kestrel'e ulaşan her istek zaten düz HTTP görünür (dış HTTPS zaten ingress tarafından sonlandırıldı); uygulama içi bir yönlendirme sonsuz bir yönlendirme döngüsü oluşturur.

Bu değer yalnızca başlangıçta configuration'dan bağlanıyor, hiçbir request alanından (header, body, query) asla okunmuyor veya etkilenmiyor — bkz. `HostingOptionsTests.ClientSuppliedHostingFields_DoNotOverrideConfiguredProxyMode`.

**Bilinçli olarak eklenmedi**: `app.UseForwardedHeaders(...)`, geniş kapsamlı bir `ForwardedHeadersOptions` yapılandırması, `KnownNetworks`/`KnownProxies` temizlenmesi. Gerçek proxy topolojisi (Container Apps'in hangi IP aralıklarından geldiği) doğrulanmadan `X-Forwarded-For` gibi header'lara körü körüne güvenmek, IP sahteciliğine kapı açar.

## 🛑 PRODUCTION DEPLOYMENT BLOCKER: rate limiter'ın client IP tespiti (ÇÖZÜLMEDİ)

`POST /oauth/github/exchange` üzerindeki rate limiter, partition key olarak `HttpContext.Connection.RemoteIpAddress`'i kullanıyor (bkz. `Program.cs`). Bu, TCP bağlantısının **doğrudan karşı tarafının** IP'sidir.

Azure Container Apps ingress'i arkasında, bu değer muhtemelen **gerçek istemci IP'si değil, Container Apps'in kendi dahili proxy IP'sidir** — çünkü yukarıda açıklandığı gibi `ForwardedHeaders` işleme bilinçli olarak eklenmedi. Sonuç: rate limiter, potansiyel olarak **tüm dış istemcileri tek bir partition'da** (proxy'nin IP'si) toplayabilir, bu da niyet edilenden çok daha agresif (herkes için ortak) bir rate limit'e yol açabilir.

**Bu, "çözüldü" olarak işaretlenmiyor ve şimdilik güvenli bir tahmine dayalı kod değişikliği de yapılmıyor** — gerçek Container Apps `X-Forwarded-For` davranışı bilinmeden yazılacak herhangi bir `ForwardedHeaders`/`KnownProxies` yapılandırması, doğrulanmamış bir varsayıma dayanır ve IP sahteciliğine kapı açabilir.

**Bu madde, bu iş için bir production deployment blocker'ıdır:** RepoPulse.AuthApi'ye gerçek/genel production trafiği açılmadan önce, staging ortamında (gerçek bir Azure Container Apps dağıtımında) şu doğrulama yapılmalıdır:
1. `HttpContext.Connection.RemoteIpAddress` değerinin gerçekte ne olduğu gözlemlenmeli (Container Apps'in kendi proxy'si mi, gerçek istemci mi).
2. Container Apps'in gönderdiği `X-Forwarded-For` (veya eşdeğeri) header'ının gerçek istemci IP'sini nasıl taşıdığı belirlenmeli.
3. Bu doğrulama yapılırken **gerçek/hassas IP değerleri hiçbir log satırına, rapora veya commit'e yazılmamalı** — yalnızca "beklenen davranış gözlendi/gözlenmedi" şeklinde bir sonuç kaydedilmeli, ham IP değerleri değil.
4. Yalnızca bu doğrulamadan **sonra**, güvenli bir `ForwardedHeaders`/`KnownProxies` yapılandırması (Container Apps'in gerçek çıkış IP aralıklarıyla sınırlı) eklenmeli.

Bu adımlar tamamlanana ve doğrulanana kadar **production trafiği bu backend'e açılmamalıdır** — aksi halde rate limiter, ya etkisiz (tüm istemciler tek partition'da) ya da yanlış davranabilir. Bu ADR'nin kapsamı, bu riski **belgelemek**tir, çözmek değil.

## Secret yönetimi ve managed identity

- **Gerçek `client_secret` hiçbir zaman şurada tutulmayacak**: Docker image içinde, repo içinde (kaynak kodu, appsettings, .env dosyası) veya GitHub Actions workflow/secret olarak düz metin şeklinde.
- GitHub OAuth `ClientSecret`, **Azure Key Vault**'ta bir secret olarak tutulacak.
- Container App, bir **system-assigned managed identity** kullanacak.
- Bu managed identity'ye Key Vault üzerinde yalnızca **"Key Vault Secrets User"** rolü (yalnızca okuma) verilecek — daha geniş bir yetki değil.
- Key Vault'taki secret, bir **Container Apps secret reference** aracılığıyla `GitHubOAuth__ClientSecret` ortam değişkenine bağlanacak (ASP.NET Core'un çift alt çizgi (`__`) konfigürasyon-anahtarı-ayırma kuralı, `GitHubOAuth:ClientSecret`'a karşılık gelir — bkz. `GitHubOAuthOptions.cs`). Bu sayede secret, Container App tanımının kendisinde düz metin olarak görünmez.

## CI/CD kimlik doğrulama: GitHub Actions OIDC

Gelecekteki bir CI/CD dağıtım pipeline'ı, Azure'a kimlik doğrularken **uzun ömürlü bir Azure servis sorumlusu parolası/sırrı** GitHub Actions secret'ı olarak saklamak yerine **GitHub Actions OIDC** (`azure/login@v2` ile federe kimlik bilgileri) kullanacak. Bu, GitHub'da hiçbir kalıcı Azure kimlik bilgisinin tutulmaması anlamına gelir. **Bu turda hiçbir Azure kimlik bilgisi veya OIDC federasyonu GitHub Actions'a eklenmedi.**

## Maliyet koruması

Herhangi bir gerçek deployment'tan **önce**, bir **Azure Budget / cost alert** oluşturulacak, böylece beklenmeyen bir maliyet artışı (ör. sonsuz döngü, hatalı ölçekleme) fark edilmeden büyümez. Bu turda hiçbir Azure Budget kaynağı oluşturulmadı — henüz hiçbir Azure kaynağı yok.

## Bu turun kapsamı ve kapsam dışı bırakılanlar

**Bu turda yapılanlar**: Dockerfile + `.dockerignore`, `Hosting:BehindTlsTerminatingProxy` konfigürasyon seçeneği ve buna karşılık gelen testler, bu ADR, ve `docs/deployment/azure-container-apps.md` altında yalnızca placeholder'larla yazılmış gelecekteki manuel deployment adımları.

**Kapsam dışı (bilinçli olarak yapılmadı)**:
- Gerçek Azure kaynağı oluşturma (Container App, Container Registry, Key Vault, Log Analytics, Budget — hiçbiri).
- Azure'a giriş yapma (`az login`) veya herhangi bir Azure API çağrısı.
- Gerçek `client_secret` değerinin okunması, listelenmesi, taşınması veya istenmesi.
- Container image'ın herhangi bir registry'ye push edilmesi.
- GitHub Actions'a Azure kimlik bilgisi eklenmesi.
- Production URL'sinin mobil uygulamaya yazılması.
- Rate limiter'ın gerçek istemci IP tespiti sorununun çözülmesi (yukarıya bakınız — yalnızca belgelendi).

## Sonraki görev

`docs/deployment/azure-container-apps.md`'de sıralanan manuel adımların gerçek bir Azure aboneliğinde uygulanması — bu ADR'nin onaylanmasından sonra, ayrı bir görev olarak.
