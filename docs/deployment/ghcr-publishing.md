# RepoPulse.AuthApi — GHCR publishing

> Bu doküman, `.github/workflows/authapi-publish-ghcr.yml` iş akışının nasıl ve ne zaman
> çalıştırılacağını açıklar. **Bu workflow bu turda hiç çalıştırılmadı** — hiçbir image
> build/push edilmedi, hiçbir GHCR package'ı oluşturulmadı. Aşağıdakiler, gelecekte gerçek
> bir insan tarafından, açık onayla yapılacak adımlardır.

## 1. Workflow yalnızca `main`'den, elle çalıştırılır

`authapi-publish-ghcr.yml` yalnızca `workflow_dispatch` tetikleyicisine sahiptir — `push`,
`pull_request`, `schedule` veya `release` gibi hiçbir otomatik tetikleyici yoktur. Bir insan,
GitHub Actions sekmesinden ("Run workflow") veya `gh workflow run authapi-publish-ghcr.yml`
ile elle tetiklemedikçe bu workflow asla çalışmaz.

`workflow_dispatch`, `push`/`pull_request`'in aksine, tetikleyici tanımında bir `branches:`
filtresini desteklemez — hangi ref'e karşı çalışacağı, dispatch sırasında seçilir. Bu yüzden
job'un kendisinde `if: github.ref == 'refs/heads/main'` koşulu vardır: workflow `main`
dışında bir branch/ref seçilerek dispatch edilirse, job hiçbir adımı çalıştırmadan
**skipped** olarak sonlanır — güvenli, sessiz bir çıkıştır, yarım kalmış bir push veya
kimlik doğrulama denemesi olmaz.

## 2. İlk publication sonrasında package visibility kontrol edilir

Workflow, push işleminden **sonra**, `docker logout ghcr.io` ile kimlik bilgilerini
düşürüp, dönen digest'i **anonim** olarak (`docker buildx imagetools inspect`) çekmeyi
dener. Bu, "push başarılı oldu" ile "paket herkese açık şekilde çekilebilir" iddialarının
**aynı şey olmadığını** doğrular — yeni oluşturulan bir GHCR package'ı **varsayılan olarak
private**'dır.

- Anonim kontrol **başarılı** olursa: paket zaten public'tir, ek bir işlem gerekmez.
- Anonim kontrol **başarısız** olursa: workflow şu hatayla **başarısız** olur:

  ```
  GHCR package was published but is not publicly accessible; change package visibility to Public manually.
  ```

## 3. Gerekirse kullanıcı GitHub Packages arayüzünden manuel olarak Public yapar

Workflow, package visibility'sini **hiçbir zaman GitHub API üzerinden kendiliğinden
değiştirmez.** Bunun nedeni, bir GHCR package'ını Public yapmanın **geri döndürülemez
olabilecek** bir karar olmasıdır (bir kez herkese açık hale gelen bir image, o ana kadar
push edilmiş tüm katmanlarıyla birlikte potansiyel olarak herkes tarafından çekilebilir
hale gelir). Bu karar kasıtlı olarak **yalnızca insana** bırakılmıştır:

1. `https://github.com/mustafanazli?tab=packages` (veya repo sayfasındaki "Packages"
   bölümü) üzerinden `repopulse-authapi` paketini açın.
2. "Package settings" → "Change visibility" → **Public** seçin ve onaylayın.
3. Onaydan sonra, workflow'un job summary'sinde yazan digest ile tekrar anonim bir
   `docker buildx imagetools inspect ghcr.io/mustafanazli/repopulse-authapi@sha256:<digest>`
   çalıştırarak gerçekten public olduğunu doğrulayın.

## 4. Public yapmadan önce image'ın secret içermediği doğrulanır

Public yapmak geri alınamayabileceği için, visibility'yi değiştirmeden **önce** aşağıdakiler
elle doğrulanmalıdır (bkz. bu PR'ın "Yerel doğrulama" adımları, aynı kontroller image
build edilen her seferinde tekrarlanmalıdır):

- `docker history` çıktısında ve export edilen dosya sisteminde gerçek bir secret, private
  key, sertifika veya `dotnet user-secrets` değeri yok.
- Image, yalnızca `Dockerfile`'da tanımlı, herkese açık `GitHubOAuth__ClientId` /
  `RedirectUri` / `TokenEndpoint` gibi public değerleri içerir — `ClientSecret` hiçbir
  build argümanı, ortam değişkeni veya dosya olarak image'a gömülmemiştir (bu zaten
  Container App tarafında da ayrı bir Key Vault reference olarak, runtime'da enjekte
  edilir — bkz. [azure-staging-runbook.md](azure-staging-runbook.md) §6).

## 5. Public GHCR image anonim çekilebilir

Bir GHCR package'ı Public yapıldıktan sonra, kimlik doğrulaması olmadan (`docker pull`,
`docker buildx imagetools inspect`, ya da Azure Container Apps'in kendi image çekme
mekanizması) herkes tarafından çekilebilir hale gelir — bu, mimarinin neden bir Azure
Container Registry yerine public bir GHCR image kullandığının nedenidir (bkz.
[ADR-004](../adr/004-production-hosting.md) ve
[azure-staging-runbook.md](azure-staging-runbook.md) §1): registry kimlik bilgisi/parola
yönetmeye gerek kalmaz.

## 6. Azure deployment'ta tag değil, `sha256` digest kullanılır

Workflow'un ürettiği `:<commit-sha>` etiketi **yalnızca GHCR arayüzünde insan tarafından
izlenebilirlik içindir** — bir etiket, aynı ada sonradan farklı bir image push edilerek
(technically) değiştirilebilir olduğundan tek başına immutability garantisi vermez. Gerçek
Azure Container Apps deployment'ı (bkz.
[azure-staging-runbook.md](azure-staging-runbook.md) §5, `main.bicepparam`'daki
`containerImageDigest` parametresi), her zaman workflow'un job summary'sinde yazan **gerçek
digest** değerini kullanır:

```
ghcr.io/mustafanazli/repopulse-authapi@sha256:<64 hex karakter>
```

`main.bicep` / `modules/containerApp.bicep`, bu digest'i doğrudan bir parametre olarak
alır — bir tag'i hiçbir zaman kabul etmez (bkz. bu dosyaların kendi açıklamaları).

## 7. GHCR fiyatlandırması ileride değişebilir

Bu doküman yazıldığı sırada, **public** GitHub Container Registry image'ları için depolama
ve bant genişliği ücretsizdir; **private** package'lar ise GitHub'ın depolama/veri
aktarımı planına tabidir. **Bu fiyatlandırma politikası GitHub tarafından gelecekte
değiştirilebilir.** Gerçek bir deployment'tan önce, güncel GitHub Packages/Container
Registry fiyatlandırması resmi GitHub dokümantasyonundan tekrar kontrol edilmelidir — bu
doküman bir fiyatlandırma garantisi vermez.

## 8. Image versiyonu silme/cleanup bu turun kapsamında değildir

Zamanla GHCR'da birikecek eski commit-SHA etiketli image versiyonlarının silinmesi/cleanup
edilmesi (ör. GitHub'ın "container retention" ayarları veya elle silme), **bu dokümanın ve
bu workflow'un kapsamı dışındadır.** Bu, ayrı, gelecekte ele alınacak bir görevdir.
