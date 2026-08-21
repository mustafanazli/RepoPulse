# ADR-003: GitHub OAuth token exchange için backend gereksinimi

## Durum

Accepted — 2026-08-21

## Bağlam

RP-003 kapsamında Android'de gerçek bir GitHub Authorization Code + PKCE akışı uçtan uca doğrulandı: merkezi OAuth yapılandırması, RFC 7636 uyumlu `code_verifier`/`code_challenge` üretimi, bellekte tek-aktif/süreli/tek-kullanımlık authorization session yönetimi, dar bir Android intent-filter üzerinden `repopulse://oauth/callback` yakalama ve callback'in `state` değerinin sabit-zamanlı karşılaştırmayla doğrulanması.

Planın önceki bir kararı (bkz. plan §4 "Kimlik doğrulama"), GitHub OAuth App'in **public client** olarak yapılandırılabileceğini ve PKCE kullanıldığı için `client_secret`'ın hiç üretilmeyeceğini/mobil uygulamaya gömülmeyeceğini varsayıyordu. Bu varsayım RFC 7636'nın "public client" modeline dayanıyordu: `code_verifier` bilgisi tek başına, gizli bir `client_secret` olmadan, authorization code'un sahibini doğrulamaya yeterli kabul edilir.

## Gerçek emülatör testinde elde edilen bulgu

RP-003'te, gerçek bir Android emülatöründe (Pixel_6_API36) ve **kullanıcının kendi GitHub hesabıyla** uçtan uca bir test yapıldı (agent hiçbir kimlik bilgisine erişmedi/dokunmadı):

- ✅ Authorization URL doğru oluşturuldu, GitHub'ın gerçek giriş ekranı ("Sign in to GitHub to continue to **RepoPulse Development**") sorunsuz açıldı.
- ✅ Kullanıcı gerçek hesabıyla giriş yapıp uygulamayı yetkilendirdi; GitHub OAuth App ayarlar sayfasında "1 user" olarak kaydedildi.
- ✅ `repopulse://oauth/callback` deep-link'i gerçek `code` ve `state` değerleriyle Android tarafından uygulamaya doğru yönlendirildi (`outcome=Success, hasCode=True, hasState=True`).
- ✅ Callback'in `state` değeri, bellekteki pending session'ın state'iyle başarıyla eşleşti (CSRF koruması çalıştı).
- ❌ **Token exchange (`POST https://github.com/login/oauth/access_token`) GitHub tarafından `error=incorrect_client_credentials` ile reddedildi.**

## GitHub OAuth App token exchange sırasında client_secret gereksinimi

GitHub OAuth App ayarlar sayfası (Developer settings → OAuth Apps → RepoPulse Development) açıkça şunu belirtiyor:

> "Client secrets — You need a client secret to authenticate as the application to the API."

Canlı API çağrısından dönen `incorrect_client_credentials` hatasıyla birlikte değerlendirildiğinde, bulgu şudur: **GitHub'ın klasik "OAuth App" tipi, authorize adımında `code_challenge`/`code_challenge_method=S256` parametrelerini kabul etse bile, token exchange adımında `client_secret` göndermeyen istekleri reddediyor.** Yani PKCE, GitHub OAuth Apps için `client_secret` ihtiyacını RFC 7636'nın public-client modelindeki gibi tamamen ortadan kaldırmıyor; GitHub'ın PKCE desteği ek bir güvenlik katmanı olarak çalışıyor, `client_secret`'ın yerini almıyor.

Bu, planın önceki mimari varsayımını (§4) geçersiz kılan, kodda değil GitHub platformunun kendisinde bulunan bir kısıtlamadır.

## Değerlendirilen seçenekler

### 1. Client secret'ı mobil uygulamaya gömmek — reddedildi

`client_secret`'ı APK içine gömmek teknik olarak token exchange'i çalıştırırdı, ancak dağıtılan bir mobil ikili dosyaya gömülen hiçbir değer gerçekten gizli kalamaz (decompile ile çıkarılabilir). Bu, PKCE'nin tam olarak önlemeye çalıştığı güvenlik açığını yeniden açar ve RP-003 talimatlarında zaten açıkça yasaklanmıştı. **Reddedildi.**

### 2. Device Flow — reddedildi

Plan (§4) Device Flow'u callback prototipi başarısız olursa devreye girecek bir yedek olarak belgelemişti. Ancak RP-002'de callback/deep-link altyapısı tamamen başarıyla doğrulandı; başarısız olan callback değil, token exchange'in kendisi. Device Flow'a geçmek callback/PKCE için zaten doğrulanmış çalışan altyapıyı gereksiz yere terk eder, kullanıcı deneyimine bir kod-girme adımı ekler ve GitHub OAuth App ayarlarında şu an kapalı durumda. **Reddedildi** — sorunla orantısız bir çözüm.

### 3. Minimal ASP.NET Core token-exchange backend — kabul edildi

`client_secret`'ı yalnızca sunucu tarafında tutan, mobil uygulamadan `code` + `code_verifier` alıp GitHub'a `client_secret` ekleyerek token exchange isteğini ileten minimal bir backend endpoint'i. Mobil uygulama hiçbir zaman `client_secret`'ı görmez/taşımaz. Bu, GitHub'ın kendi resmi dokümantasyonunun da işaret ettiği standart, güvenli desendir (bkz. aşağıdaki bağlantı). **Kabul edildi.**

## Güvenlik sonuçları

- `client_secret` yalnızca backend'in kendi ortamında (ör. sunucu tarafı gizli değişken) tutulur; mobil APK'da hiçbir zaman bulunmaz.
- Mobil uygulama, backend'e yalnızca `code` ve `code_verifier` gönderir — ikisi de tek kullanımlıktır ve tek başlarına backend dışında bir değer taşımaz.
- Backend, GitHub'dan aldığı `access_token`'ı olduğu gibi mobil uygulamaya döndürür; backend token'ı kalıcı olarak saklamaz (stateless proxy).
- Mobil tarafta zaten var olan kurallar geçerliliğini korur: token/code/state/verifier hiçbir yerde loglanmaz, SecureStorage RP-004 kapsamı dışındadır.
- Backend'in kendisi yeni bir saldırı yüzeyi oluşturur (ör. rate-limiting, backend'in kendi kimlik doğrulaması/yetkilendirmesi, HTTPS zorunluluğu) — bunlar RP-004'te ayrıca ele alınmalıdır.

## Kapsam ve sonraki görevler

Bu ADR yalnızca mimari kararı belgeler; backend'in kendisi bu turda **yazılmadı**. Sonraki görev (RP-004) minimal ASP.NET Core token-exchange backend'inin tasarımı ve uygulamasıdır — plan dokümanına eklenmiştir (bkz. §11).

## Resmî GitHub dokümantasyonu

- GitHub OAuth Apps yetkilendirme akışı: https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps
