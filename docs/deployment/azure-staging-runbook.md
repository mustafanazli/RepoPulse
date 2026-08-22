# RepoPulse Azure staging — deployment runbook

> **Bu doküman, gelecekte gerçek bir insan tarafından elle uygulanacak bir kontrol listesidir.**
> `infra/azure/` altındaki Bicep şablonları bu turda **hiçbir Azure kaynağına karşı çalıştırılmadı** —
> ne `az deployment ... create`, ne `az deployment ... what-if`. Aşağıdaki tüm komutlar
> incelenmek ve **daha sonra, açık onayınızla** çalıştırılmak üzere yazılmıştır.
> Mimari gerekçe için bkz. [ADR-004](../adr/004-production-hosting.md).

## 0. Bu turda NELERİN yapılmadığı

- Hiçbir `az provider register` çalıştırılmadı (Microsoft.App, Microsoft.KeyVault,
  Microsoft.ManagedIdentity hâlâ **NotRegistered**).
- Hiçbir Azure resource group, Key Vault, Container Apps environment veya Container App
  oluşturulmadı.
- Hiçbir `az deployment` komutu (ne gerçek deployment ne de `what-if`) çalıştırılmadı.
- Hiçbir Azure Container Registry oluşturulmadı veya `Microsoft.ContainerRegistry` kaydedilmedi
  — bu mimari **kasıtlı olarak** ACR kullanmıyor, bunun yerine public bir GHCR image referansı
  kullanıyor.
- Hiçbir Key Vault secret'ı oluşturulmadı.
- Hiçbir GitHub secret'ı oluşturulmadı, hiçbir image GHCR'a push edilmedi.
- Abonelik Pay-As-You-Go'ya yükseltilmedi ve bu runbook'un hiçbir adımı bunu gerektirmiyor.

## 1. Maliyet korumaları (deployment'tan ÖNCE okuyun)

- **Bu mimari "cost-controlled" (maliyet kontrollü) bir staging ortamıdır — "zero-cost"
  (tamamen ücretsiz) bir garanti DEĞİLDİR.** Container App, Key Vault ve Container Apps
  environment gibi burada tanımlanan Azure kaynakları, çalıştırıldığında Azure for Students
  kredinizi tüketebilir. Dışarıdan gerçek bir ödeme çıkmaması yalnızca bu abonelik
  Pay-As-You-Go'ya yükseltilmediği sürece geçerlidir; abonelik yükseltilirse veya kredi
  tamamen tükenirse gerçek para ile faturalandırma başlayabilir.
- **Azure for Students aboneliği asla Pay-As-You-Go'ya yükseltilmemelidir.** Azure bazen
  kredi bittiğinde veya süre dolduğunda yükseltme öneren bir bildirim gösterebilir — bunu
  kabul etmeyin.
- **Azure Container Registry ve Log Analytics workspace bu mimaride bilinçli olarak
  oluşturulmuyor** — ikisi de sürekli (idle'dayken bile) ücret doğurabilir; bunun yerine
  public GHCR image'ı ve workspace-less bir Container Apps environment kullanılıyor. Bu
  kararı ADR-004'te tekrar okuyun; "biraz daha görünürlük için" ACR veya Log Analytics
  eklemeyin.
- **`minReplicas: 0` ve `maxReplicas: 1` sınırları değiştirilmemelidir.** Bu sınırlar,
  Container App'in boştayken **compute (vCPU/bellek) ücretini sıfıra indirmesini** ve
  yanlışlıkla birden fazla replika ölçeklenip beklenmedik ücret oluşmasını önlemek için var.
  Bu yalnızca Container App'in compute ücretiyle ilgilidir — Key Vault ve Container Apps
  environment gibi diğer kaynakların maliyeti için bir sonraki maddeye bakın.
- **Kalan kredi süresi bu dokümanda sabit bir tarih olarak yazılmıyor.** Bu dokümanı
  yazarken kalan kredinin yaklaşık 45 gün olduğu belirtilmişti, ancak bu değer zamanla
  değişir — **gerçek bir deployment'tan hemen önce, Azure portalında "Cost Management +
  Billing" → "Azure for Students" sayfasından kalan kredi ve bitiş tarihi mutlaka tekrar
  kontrol edilmelidir.**
- **Bir Azure Budget/cost alert oluşturmak harcamayı otomatik olarak DURDURMAZ** — yalnızca
  bir eşiğe ulaşıldığında bir bildirim/e-posta gönderir. Bütçe alarmı kurulmuş olması, kredi
  bitene kadar kaynakların güvenle çalışmaya devam edebileceği anlamına gelmez.
- **Azure Marketplace'ten hiçbir ürün/kaynak kullanılmayacaktır** — bu mimarideki tüm
  kaynaklar (Container Apps, Key Vault) doğrudan Microsoft birinci taraf hizmetleridir,
  üçüncü taraf Marketplace teklifleri değildir. Gelecekte bu mimariye bir şey eklerken
  Marketplace kaynaklarından kaçının.
- **Krediniz bitmeden, kullanmadığınız zaman kaynaklar kaldırılmalıdır** — bkz. §6
  "Cleanup" bölümü. `minReplicas: 0` boştayken compute ücretini sıfırlar, ancak Key Vault ve
  Container Apps environment'ın kendisi hâlâ (küçük de olsa) bir maliyet taşıyabilir; uzun
  süre kullanılmayacaksa tamamen kaldırın.

## 2. Ön koşullar (elle, sırayla)

1. Azure portalında kalan Azure for Students kredisini ve bitiş tarihini tekrar kontrol edin (bkz. §1).
2. Bir Azure Budget/cost alert oluşturun (bu runbook'un kapsamı dışında — Azure portalı
   üzerinden "Cost Management + Billing" → "Budgets" ile elle yapılır).
3. Aşağıdaki provider'ları kaydedin (**bu turda ÇALIŞTIRILMADI**, yalnızca gelecekte, açık
   onayınızla çalıştırılacak referans komutlar):

   ```
   az provider register --namespace Microsoft.App
   az provider register --namespace Microsoft.KeyVault
   az provider register --namespace Microsoft.ManagedIdentity
   ```

   `Microsoft.ContainerRegistry` **kasıtlı olarak bu listede yok** — bu mimari ACR
   kullanmıyor.

4. Kayıt durumunu doğrulayın:

   ```
   az provider show --namespace Microsoft.App --query registrationState -o tsv
   az provider show --namespace Microsoft.KeyVault --query registrationState -o tsv
   az provider show --namespace Microsoft.ManagedIdentity --query registrationState -o tsv
   ```

5. `infra/azure/main.example.bicepparam` dosyasını `infra/azure/main.bicepparam` olarak
   kopyalayın (bu dosya `.gitignore`'da — asla commit etmeyin) ve gerçek `tenantId` ile
   benzersiz bir `keyVaultName` girin. `containerImage` alanını henüz **doldurmayın** — bu,
   GHCR'a gerçek bir image push edildikten sonra, gerçek bir commit SHA ile doldurulacak
   (bkz. §4).

## 3. Bicep doğrulama (deployment değildir, Azure'a bağlanmaz)

```
az bicep build --file infra/azure/main.bicep
az bicep build --file infra/azure/modules/containerAppsEnvironment.bicep
az bicep build --file infra/azure/modules/keyVault.bicep
az bicep build --file infra/azure/modules/containerApp.bicep
az bicep build --file infra/azure/modules/keyVaultAccess.bicep
```

Bunlar yalnızca yerel derleme/söz dizimi kontrolüdür — hiçbir Azure kaynağına dokunmaz,
kimlik doğrulaması gerektirmez. CI'da da otomatik çalışıyor (bkz.
`.github/workflows/bicep-validate.yml`).

## 4. GHCR image'ını hazırlama (bu runbook'un kapsamı dışında, ayrı bir görev)

Bu Bicep şablonu, `ghcr.io/mustafanazli/repopulse-authapi:<immutable-commit-sha>` biçiminde
**public** bir GHCR image referansı bekliyor. Image'ın gerçekten GHCR'a push edilmesi,
mevcut `.github/workflows/authapi-container-build.yml` CI job'unun kapsamı dışındadır (o
job yalnızca build eder, push etmez — bkz. o dosyanın kendi yorumları). GHCR'a push eden bir
workflow, **ayrı bir görev** olarak, ayrı bir onayla eklenmelidir. **`latest` etiketi asla
kullanılmamalıdır** — yalnızca değişmez (immutable) bir commit SHA etiketi.

## 5. Deployment adımları (Faz A: bootstrap altyapı — bu turda ÇALIŞTIRILMADI)

Yalnızca §2 ve §4 tamamlandıktan, ve siz Azure portalında kredi/bütçe durumunu son kez
kontrol ettikten **sonra**, aşağıdaki komutlar sırayla çalıştırılabilir. Önce `what-if` ile
önizleme, ardından gerçek deployment:

```
az deployment sub what-if \
  --location westeurope \
  --template-file infra/azure/main.bicep \
  --parameters infra/azure/main.bicepparam

az deployment sub create \
  --location westeurope \
  --template-file infra/azure/main.bicep \
  --parameters infra/azure/main.bicepparam
```

**Bu, Faz A'dır — yalnızca bootstrap altyapıyı (resource group, Container Apps environment,
boş Key Vault, `GitHubOAuth__ClientSecret` olmadan Container App, ve Container App'in
kimliğine Key Vault Secrets User rolü) oluşturur.** Container App bu noktada `ClientSecret`
eksik olduğu için **başlatılamayacak ve crash-loop'a girecektir** — bu, `Program.cs`'teki
`GitHubOAuthOptionsValidator.ValidateOnStart()`'ın kasıtlı "fail fast" davranışıdır, bir hata
değildir. Faz B tamamlanana kadar bu beklenen bir durumdur.

## 6. Deployment adımları (Faz B: secret bağlama — bu turda ÇALIŞTIRILMADI, elle yapılır)

Bu faz, gerçek `client_secret` değerini içerdiği için **tamamen elle, yalnızca sizin
tarafınızdan** yapılmalıdır — hiçbir agent/asistan bu değeri görmemeli/taşımamalıdır.

1. Gerçek GitHub OAuth `client_secret` değerini Key Vault'a ekleyin:

   ```
   az keyvault secret set \
     --vault-name <KEY_VAULT_NAME> \
     --name GitHubOAuth-ClientSecret \
     --value "<GERÇEK DEĞERİ YALNIZCA BURADA, TERMİNALDE, ELLE GİRİN>"
   ```

2. Container App'e bu secret'ı bir Container Apps secret reference olarak bağlayın ve
   `GitHubOAuth__ClientSecret` ortam değişkenini buna işaret edecek şekilde güncelleyin:

   ```
   az containerapp secret set \
     --resource-group rg-repopulse-staging \
     --name ca-repopulse-authapi-staging \
     --secrets github-oauth-client-secret=keyvaultref:<KEY_VAULT_SECRET_URI>,identityref:system

   az containerapp update \
     --resource-group rg-repopulse-staging \
     --name ca-repopulse-authapi-staging \
     --set-env-vars GitHubOAuth__ClientSecret=secretref:github-oauth-client-secret
   ```

3. `https://<containerAppFqdn>/health` → `200 {"status":"healthy"}` olduğunu doğrulayın.

## 7. 🛑 Rate-limit / client-IP staging testi (production trafiği açmadan ÖNCE zorunlu)

Bu test, [ADR-004](../adr/004-production-hosting.md)'te işaretlenen **production deployment
blocker**'ı gidermek için gereklidir. **Gerçek IP değerleri, token, code, verifier veya
secret hiçbir noktada log'a veya rapora yazılmamalıdır** — yalnızca "beklenen davranış
gözlendi/gözlenmedi" şeklinde bir sonuç kaydedilir.

1. Bir ağdan (ör. ev Wi-Fi'ı), `POST /oauth/github/exchange` uç noktasına art arda geçersiz
   (fakat biçimsel olarak geçerli) istekler göndererek rate limit'in ne zaman devreye
   girdiğini (kaçıncı istekte `429 rate_limited` döndüğünü) gözlemleyin — yalnızca **istek
   sayısını ve dönen durum kodunu** kaydedin, gönderilen IP'yi değil.
2. **Bağımsız, ikinci bir ağdan** (ör. telefonun mobil veri bağlantısı, ev Wi-Fi'ı değil)
   aynı testi tekrarlayın.
3. **Eğer ikinci ağ, birinci ağın rate limit kotasını paylaşmıyorsa** (yani ikinci ağdan
   gelen istekler kendi bağımsız limitine sahipse): partition ayrımı, staging ortamında
   beklendiği gibi çalışıyor demektir.
4. **Eğer iki ağ aynı kotayı paylaşıyorsa** (ör. birinci ağ limite ulaştıktan hemen sonra
   ikinci ağdan gelen ilk istek de doğrudan `429` alıyorsa): bu, Container Apps ingress'i
   arkasında `RemoteIpAddress`'in gerçek istemci IP'si yerine proxy'nin kendi IP'sini
   gördüğünü doğrular — **production deployment blocker'ı hâlâ geçerlidir, kod
   değiştirilmeden production trafiği açılmamalıdır.**
5. Bu testin sonucu (yalnızca "ayrıştı" / "ayrışmadı" sonucu, hiçbir ham IP/istek detayı
   olmadan) ADR-004'e bir güncelleme olarak eklenmelidir.
6. **Bu test tamamlanıp sonucu belgelenmeden production trafiği bu backend'e açılmamalıdır.**

## 8. Cleanup (kredi bitmeden kaynakları kaldırma — bu turda ÇALIŞTIRILMADI)

Aşağıdaki komutlar **yalnızca bu runbook'un oluşturduğu `rg-repopulse-staging` kaynak
grubunu** hedefler. **Bu abonelikte önceden var olan, RepoPulse ile ilgisiz
`TranslatorAppGrubu` kaynak grubuna KESİNLİKLE dokunulmamalıdır** — aşağıdaki hiçbir komut
onu hedeflemez ve elle çalıştırırken de hedeflenmemelidir.

**Silme komutunu çalıştırmadan ÖNCE, sırayla, üç ayrı doğrulama yapın:**

1. Doğru abonelikte olduğunuzu doğrulayın (birden fazla abonelikle çalışıyorsanız özellikle
   önemli — yanlış abonelikte çalıştırılan bir `az group delete`, hedef adı doğru olsa bile
   tamamen farklı bir ortamı silebilir):

   ```
   az account show --query "{name:name, id:id}" -o table
   ```

2. Hedef kaynak grubunun var olduğunu VE adının tam olarak `rg-repopulse-staging` olduğunu
   doğrulayın:

   ```
   az group show --name rg-repopulse-staging --query name -o tsv
   ```

3. Silmeden önce bu kaynak grubunun içinde gerçekten NELERİN olduğunu görün — beklemediğiniz
   bir kaynak varsa (ör. yanlışlıkla başka bir projenin kaynağı buraya eklenmişse) durun ve
   araştırın, silme komutunu çalıştırmayın:

   ```
   az resource list --resource-group rg-repopulse-staging -o table
   ```

Yalnızca yukarıdaki üç adım da beklenen sonucu verdikten sonra silme komutunu çalıştırın:

```
# Yalnızca RepoPulse staging kaynak grubunu siler — TranslatorAppGrubu'nu ETKİLEMEZ.
az group delete --name rg-repopulse-staging --yes --no-wait
```

## 9. Özet: gerçek deployment öncesi açık kullanıcı onayları

Bu turda kod olarak hazırlanan bu altyapı, aşağıdakiler **siz tarafından, açıkça**
onaylanmadan/yapılmadan gerçek bir Azure kaynağına dönüşmeyecektir:

- [ ] Azure portalında kalan kredi/bitiş tarihinin son kez kontrolü
- [ ] Bir Azure Budget/cost alert'in oluşturulması
- [ ] `Microsoft.App`, `Microsoft.KeyVault`, `Microsoft.ManagedIdentity` provider'larının
      kaydedilmesi onayı
- [ ] Gerçek bir GHCR image'ının build edilip push edilmesi (ayrı görev)
- [ ] `infra/azure/main.bicepparam`'ın gerçek değerlerle doldurulması
- [ ] Faz A bootstrap deployment'ının (`az deployment sub create`) çalıştırılması onayı
- [ ] Faz B secret bağlama adımlarının elle tamamlanması
- [ ] §7'deki rate-limit/client-IP staging testinin tamamlanması ve sonucunun belgelenmesi
- [ ] Yalnızca yukarıdakilerin hepsi tamamlandıktan sonra production trafiğinin açılması
