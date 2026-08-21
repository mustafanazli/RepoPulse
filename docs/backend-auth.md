# RepoPulse.AuthApi — token exchange endpoint

> `POST /oauth/github/exchange` **uygulandı** ve yalnızca sahte `HttpMessageHandler`'larla test edildi (48/48 test geçiyor). Gerçek `client_secret` hiç oluşturulmadı/kullanılmadı, mobil uygulama bu endpoint'e henüz bağlanmadı, gerçek GitHub ağına hiçbir testte çıkılmadı. Bkz. [ADR-003](adr/003-github-oauth-token-exchange.md) için bu backend'in neden gerekli olduğu.

## Neden bir backend?

GitHub'ın klasik OAuth App tipi, authorize adımında PKCE parametrelerini (`code_challenge`, `code_challenge_method`) kabul etse de, token exchange adımında hâlâ `client_secret` istiyor. Mobil bir uygulamaya gömülen hiçbir değer gerçekten gizli kalamaz, bu yüzden `client_secret` yalnızca güvenilir bir sunucu ortamında tutulmalı. Bu backend'in tek görevi: `client_secret`'ı hiç bilmeyen mobil istemci ile onu ihtiyaç duyan GitHub token endpoint'i arasında ince, stateless bir aracı olmak.

## Endpoint

```
POST /oauth/github/exchange
Content-Type: application/json
```

### Request body

```json
{
  "code": "...",
  "codeVerifier": "..."
}
```

| Alan | Kural |
|---|---|
| `code` | Boş olamaz, en fazla 512 karakter |
| `codeVerifier` | RFC 7636: 43–128 karakter, yalnızca `A-Z a-z 0-9 - . _ ~` |

Değerler trim edilmez; kurallara uymuyorsa `400 invalid_request` döner. **Bilinmeyen JSON alanı reddedilir** (`JsonSerializerOptions.UnmappedMemberHandling = Disallow`) — `clientId`, `clientSecret`, `redirectUri`, `tokenEndpoint` gibi alanlar request body'de zaten tanımlı değil; gönderilirse istek tamamen reddedilir, sessizce yok sayılmaz.

### Mobil istemciden ASLA alınmayanlar

| Alan | Neden istemciden alınmaz | Nereden gelir |
|---|---|---|
| `clientSecret` | Mobil istemci bunu hiçbir zaman bilmemeli/taşımamalı | `GitHubOAuthOptions` (user-secrets / env var) |
| `redirectUri` | Sabit olmalı; aksi halde redirect_uri değiştirme saldırısına kapı açılır | `GitHubOAuthOptions` (appsettings.json, sabit) |
| `clientId` | Public olsa da backend'in kendi OAuth App'iyle tutarlı olmalı | `GitHubOAuthOptions` (appsettings.json) |
| `tokenEndpoint` | İstemcinin "hangi URL'e istek at" demesine izin vermek backend'i SSRF proxy'sine çevirir | `GitHubOAuthOptions` (appsettings.json, sabit) |

`GitHubOAuthOptionsValidator`, `RedirectUri`/`TokenEndpoint`'i yalnızca "geçerli bir URI mi" diye değil, **tam olarak beklenen değere eşit mi** diye kontrol eder (scheme/host/path/query normalize edilip karşılaştırılır — farklı host/path/query kabul edilmez). Configuration bu değerlerden saparsa host `ValidateOnStart()` ile hiç başlamaz.

### GitHub'a giden form alanları

`client_id`, `client_secret`, `code`, `redirect_uri`, `code_verifier` — ilk ikisi ve `redirect_uri` her zaman backend configuration'ından, `code`/`code_verifier` yalnızca request'ten. `Accept: application/json`, `User-Agent: RepoPulse-AuthApi`. Tek istek, otomatik retry yok (authorization code tek kullanımlık). `HttpClient` DI üzerinden (`AddHttpClient<IGitHubTokenExchangeService, GitHubTokenExchangeService>`), 10 saniye timeout, gelen `CancellationToken` ile birlikte.

### Başarılı response (200)

```json
{
  "accessToken": "...",
  "tokenType": "bearer",
  "scope": "...",
  "expiresIn": 28800,
  "refreshToken": "...",
  "refreshTokenExpiresIn": 15811200
}
```

`expiresIn`/`refreshToken`/`refreshTokenExpiresIn` GitHub OAuth App'in "Expire user access tokens" ayarına göre gelmeyebilir — model bunları nullable tutar, eksikse `null` döner, hata oluşmaz.

Her yanıt (başarılı veya başarısız) `Cache-Control: no-store` ve `Pragma: no-cache` header'larıyla döner — token hiçbir ara katmanda önbelleğe alınmamalı.

### Hata sözleşmesi

| Durum | HTTP | `title` |
|---|---|---|
| Geçersiz request (eksik/uzun `code`, geçersiz `codeVerifier`, bilinmeyen alan, bozuk JSON) | 400 | `invalid_request` |
| GitHub OAuth reddi (`error` alanlı yanıt) | 400 | `oauth_exchange_failed` |
| GitHub beklenmeyen/bozuk cevap (JSON parse hatası, `access_token` yok, `error` yok) | 502 | `upstream_error` |
| GitHub'a giden istek zaman aşımına uğradı | 504 | `upstream_timeout` |
| Rate limit aşıldı | 429 | `rate_limited` |
| Beklenmeyen dahili hata | 500 | `internal_error` |

Tüm hata gövdeleri `{"type":"about:blank","title":"...","status":...}` biçiminde — GitHub'ın ham `error`/`error_description`'ı, exception mesajı veya stack trace **hiçbir zaman** istemciye iletilmez (`GitHubTokenExchangeService` bunu GitHub'dan aldığı anda genel bir `FailureKind`'a indirger; `app.UseExceptionHandler` tüm işlenmemiş exception'ları da aynı şekilde genel `internal_error`'a indirger).

## Koruma katmanları (uygulandı)

- **Rate limiting**: ASP.NET Core'un yerleşik `Microsoft.AspNetCore.RateLimiting`'i, IP başına sabit pencere (10 istek/dakika, `QueueLimit=0`). Yalnızca `/oauth/github/exchange`'e uygulanır; `/health` rate-limit dışında. 429 gövdesi token/code bilgisi içermez.
- **Request boyutu**: `/oauth/github/exchange` için 4096 bayt sabit üst sınır, uygulama seviyesinde middleware ile (Kestrel'in kendi limitine ek olarak — TestServer'da da çalışır).
- **Timeout**: GitHub'a giden istek için 10 saniye `HttpClient.Timeout` + gelen `CancellationToken`.
- **HTTPS**: `app.UseHttpsRedirection()` her ortamda; `app.UseHsts()` yalnızca Production'da.
- **Forwarded headers**: **Bilinçli olarak eklenmedi.** Hosting/reverse-proxy topolojisi netleşmeden `X-Forwarded-*` header'larına güvenmek sahte IP/scheme bildirimine izin verebilir (ör. rate limiter'ın IP partition anahtarı yanıltılabilir). Güvenli varsayılan: hiçbir forwarded header'a güvenme.
- **Hassas veri redaction**: `code`/`codeVerifier`/`client_secret`/`access_token`/`refresh_token` hiçbir log satırında görünmüyor — `LogSafetyTests` bunu ayırt edici sahte değerlerle doğruluyor.

## Mobil client akışı (RP-005)

Mobil taraf artık **iki ayrı, tek sorumluluklu client**'a bölündü (eski birleşik `GitHubOAuthClient` kaldırıldı):

- **`RepoPulseAuthApiClient`** (`RepoPulse.Core.Authentication`) — yalnızca `POST /oauth/github/exchange`'i bilir. `client_id`/`client_secret`/`redirect_uri`/`state`/`tokenEndpoint` hiçbirini göndermez; yalnızca `{"code":"...","codeVerifier":"..."}` gönderir. Backend'in base adresi DI'da sabit (`RepoPulseAuthApiOptions.DevelopmentBaseAddress = "https://localhost:7082"`, **yalnızca geliştirme** — production hosting adresi ayrı bir görev).
- **`GitHubApiClient`** (`RepoPulse.Core.Authentication`) — yalnızca `GET https://api.github.com/user`'ı bilir, OAuth/PKCE hakkında hiçbir şey bilmez, sadece zaten elde edilmiş bir access token alır.

Mobil uygulama artık `https://github.com/login/oauth/access_token`'ı **hiçbir yerde** doğrudan çağırmıyor — `OAuthConstants.TokenEndpoint` sabiti tamamen kaldırıldı.

`MainPage` akışı: callback alınır → `AuthorizationSessionStore.TryConsume` ile state doğrulanır → `RepoPulseAuthApiClient.ExchangeAsync` ile backend'den token alınır → `GitHubApiClient.GetCurrentUserAsync` ile kullanıcı bilgisi çekilir → login/avatar gösterilir. PKCE/session/tek-kullanımlık-state davranışları (RP-003) değişmedi.

Backend'in `title` alanlı hata sözleşmesi (`invalid_request`/`oauth_exchange_failed`/`upstream_error`/`upstream_timeout`/`rate_limited`/`internal_error`), `RepoPulseAuthApiClient` içinde `AuthApiExchangeFailureKind` enum'una eşleniyor; `MainPage` bu enum'u kısa, güvenli Türkçe mesajlara çeviriyor — backend'in ham response body'si veya bir exception mesajı hiçbir zaman kullanıcıya gösterilmiyor.

**Token'ların bellek davranışı**: `accessToken`/`refreshToken`, `MainPage` üzerinde yalnızca özel (private) alanlarda, yalnızca bellekte tutuluyor. SecureStorage/SQLite/Preferences/dosya kullanılmıyor — uygulama kapanınca kayboluyor, bu RP-005 kapsamında kabul edilebilir. Refresh akışı henüz uygulanmadı.

## Kapsam dışı (sonraki alt görevler)

- Gerçek backend'e karşı gerçek mobil↔backend entegrasyon testi (emülatörde)
- Gerçek `client_secret` ile uçtan uca doğrulama
- SecureStorage ile kalıcı oturum
- Refresh token yenileme akışı
- Hosting/deployment, Docker
- Gerçek GitHub ağına karşı uçtan uca doğrulama (yalnızca sahte handler'larla test edildi)
