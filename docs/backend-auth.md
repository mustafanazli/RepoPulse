# RepoPulse.AuthApi — token exchange endpoint kontratı

> Bu doküman RP-004 kapsamında **tasarım** amaçlıdır. `/oauth/github/exchange` endpoint'i henüz uygulanmadı — yalnızca `GET /health` mevcuttur. Bkz. [ADR-003](adr/003-github-oauth-token-exchange.md) için bu backend'in neden gerekli olduğu.

## Neden bir backend?

GitHub'ın klasik OAuth App tipi, authorize adımında PKCE parametrelerini (`code_challenge`, `code_challenge_method`) kabul etse de, token exchange adımında hâlâ `client_secret` istiyor. Mobil bir uygulamaya gömülen hiçbir değer gerçekten gizli kalamaz, bu yüzden `client_secret` yalnızca güvenilir bir sunucu ortamında tutulmalı. Bu backend'in tek görevi: `client_secret`'ı hiç bilmeyen mobil istemci ile onu ihtiyaç duyan GitHub token endpoint'i arasında ince, stateless bir aracı olmak.

## Planlanan endpoint

```
POST /oauth/github/exchange
```

### Mobil istemciden alınacaklar (request body)

| Alan | Açıklama |
|---|---|
| `code` | GitHub'ın callback'te döndürdüğü authorization code (tek kullanımlık) |
| `codeVerifier` | Mobil istemcinin authorize isteğinde ürettiği PKCE `code_verifier` |

### Mobil istemciden ASLA alınmayacaklar

Aşağıdaki değerler istemciden **kabul edilmemeli** — backend'in kendi güvenilir configuration'ından (`GitHubOAuthOptions`) okunmalı:

| Alan | Neden istemciden alınmaz |
|---|---|
| `clientSecret` | Mobil istemci bunu hiçbir zaman bilmemeli/taşımamalı |
| `redirectUri` | Sabit olmalı; istemcinin gönderdiği bir değer kabul edilirse redirect_uri değiştirme saldırılarına kapı açılır |
| `clientId` | Public olsa da, backend'in kendi yapılandırdığı OAuth App ile tutarlılık için istemciden değil configuration'dan gelmeli |
| `tokenEndpoint` | İstemcinin backend'e "hangi URL'e istek at" demesine izin vermek, backend'i keyfi bir SSRF proxy'sine dönüştürür |

Bu ayrım kasıtlı: istemci yalnızca *o anki yetkilendirmeye özgü, tek kullanımlık* değerleri (`code`, `codeVerifier`) gönderir; *sabit, uygulamaya özgü* değerler her zaman backend'in kontrolündedir.

### Planlanan davranış

1. `code` ve `codeVerifier` doğrulanır (boş/eksik → `400 Bad Request`, gerçek OAuth değerleri log'a yazılmaz).
2. Backend, kendi `GitHubOAuthOptions`'ından `client_id`, `client_secret`, `redirect_uri`'yi ekleyerek GitHub'ın token endpoint'ine `POST` yapar.
3. GitHub'ın yanıtı ayrıştırılır; başarılıysa yalnızca `access_token`/`token_type`/`scope` mobil istemciye döndürülür.
4. Backend, aldığı token'ı **saklamaz** (stateless proxy) — RP-004 kapsamında kalıcılık yok.

## Operasyonel gereksinimler (RP-004 uygulaması bunları karşılamalı)

- **HTTPS zorunluluğu**: Endpoint yalnızca HTTPS üzerinden sunulmalı; HTTP isteği reddedilmeli veya yönlendirilmeli. Gerçek `code`/`codeVerifier` düz metin HTTP üzerinden asla taşınmamalı.
- **Rate limiting**: Endpoint, aynı istemciden/IP'den kısa sürede çok sayıda isteğe karşı sınırlanmalı — token exchange brute-force denemelerine karşı.
- **Maksimum request boyutu**: İstek gövdesi küçük ve sabit boyutlu olmalı (yalnızca iki kısa string alanı); aşırı büyük request body'ler erken reddedilmeli.
- **Timeout**: GitHub'a yapılan giden istek için makul bir timeout (ör. 10-15 saniye) olmalı; GitHub yanıt vermezse istemciye zaman aşımı hatası dönmeli, istek asılı kalmamalı.
- **Hassas veri redaction**: `code`, `codeVerifier`, `client_secret`, `access_token` hiçbir log satırında, hata mesajında veya telemetri olayında ham olarak görünmemeli — RP-002/RP-003'te mobil tarafta uygulanan aynı disiplin.
- **Sabit redirect URI**: `redirect_uri` her zaman backend'in configuration'ından gelen sabit `repopulse://oauth/callback` değeri olmalı, hiçbir zaman istekten alınmamalı.
- **GitHub hata cevaplarının doğrudan kullanıcıya aktarılmaması**: GitHub'ın döndürdüğü `error`/`error_description` ham haliyle istemciye iletilmemeli; RP-003'teki mobil `GitHubOAuthClient` deseninde olduğu gibi, yalnızca genel/güvenli bir hata sınıflandırması (ör. `oauth_error`, `network_error`) döndürülmeli.

## Kapsam dışı (RP-004'ün ilerleyen alt görevleri)

- Gerçek GitHub'a HTTP isteği
- Access/refresh token modeli ve döndürülmesi
- Mobil uygulamanın bu backend'e bağlanması
- Hosting/deployment, Docker
- Kalıcı depolama (bu backend zaten stateless olacak)
