# RepoPulse Azure staging — deployment runbook

> **Bu doküman, gelecekte gerçek bir insan tarafından elle uygulanacak bir kontrol listesidir.**
> `infra/azure/` altındaki Bicep şablonları, bu dokümanın son güncellendiği turda **hiçbir
> gerçek Azure kaynağına karşı çalıştırılmadı** — yalnızca salt-okunur `what-if` önizlemeleri
> yapıldı, hiçbir `az deployment ... create` çalıştırılmadı. Aşağıdaki tüm komutlar
> incelenmek ve **daha sonra, açık onayınızla** çalıştırılmak üzere yazılmıştır.
> Mimari gerekçe için bkz. [ADR-004](../adr/004-production-hosting.md).

## 0. İki fazlı altyapı — dosya haritası

Altyapı artık **iki ayrı, sıralı Bicep şablonuna** bölünmüş durumda (bkz. ADR-004'ün
"Bootstrap ve application deployment'ın ayrılması" bölümü):

| Dosya | Scope | İçerik |
|---|---|---|
| `infra/azure/main.bicep` | subscription | **Faz A** — resource group, Container Apps environment, boş Key Vault, user-assigned managed identity + Key Vault rolü. **Container App YOK.** |
| `infra/azure/app.bicep` | resource group | **Faz B** — yalnızca Container App; Faz A'nın kaynaklarını `existing` referanslarla kullanır. |
| `infra/azure/main.example.bicepparam` | — | Faz A örnek parametreleri (tracked). |
| `infra/azure/app.example.bicepparam` | — | Faz B örnek parametreleri (tracked). |
| `infra/azure/main.bicepparam` | — | Faz A gerçek parametreleri (`.gitignore`'da, **asla commit etmeyin**). |
| `infra/azure/app.bicepparam` | — | Faz B gerçek parametreleri (`.gitignore`'da, **asla commit etmeyin**). |

Faz A **her zaman** Faz B'den önce, tamamen bitmiş olmalıdır. Faz B, Faz A'nın çıktısı olan
kaynak adlarını (`resourceGroupName`, `containerAppsEnvironmentName`, `keyVaultName`,
`identityName`) referans alır.

## 1. Bu dokümanın en son güncellendiği turda NELERİN yapılmadığı

- Hiçbir `az provider register` bu turda çalıştırılmadı (önceki bir turda yalnızca
  `Microsoft.App`, `Microsoft.KeyVault`, `Microsoft.ManagedIdentity` kaydedilmişti —
  `Microsoft.ContainerRegistry` ve `Microsoft.OperationalInsights` hâlâ **NotRegistered**).
- Hiçbir Azure resource group, Key Vault, Container Apps environment, user-assigned identity
  veya Container App oluşturulmadı — yalnızca salt-okunur `what-if` önizlemeleri denendi.
- Hiçbir `az deployment ... create` komutu çalıştırılmadı.
- Hiçbir Azure Container Registry oluşturulmadı veya `Microsoft.ContainerRegistry` kaydedilmedi.
- Hiçbir Log Analytics workspace, Application Insights veya `Microsoft.Insights`
  diagnosticSettings kaynağı oluşturulmadı; `Microsoft.Insights`/`Microsoft.OperationalInsights`
  provider'ı hiç kaydedilmedi.
- Hiçbir Key Vault secret'ı oluşturulmadı.
- Abonelik Pay-As-You-Go'ya yükseltilmedi.

## 2. Maliyet korumaları (deployment'tan ÖNCE okuyun)

- **Bu mimari "cost-controlled" (maliyet kontrollü) bir staging ortamıdır — "zero-cost"
  (tamamen ücretsiz) bir garanti DEĞİLDİR.** Container App, Key Vault, Container Apps
  environment ve user-assigned identity gibi burada tanımlanan Azure kaynakları,
  çalıştırıldığında Azure for Students kredinizi tüketebilir. Dışarıdan gerçek bir ödeme
  çıkmaması yalnızca bu abonelik Pay-As-You-Go'ya yükseltilmediği sürece geçerlidir;
  abonelik yükseltilirse veya kredi tamamen tükenirse gerçek para ile faturalandırma
  başlayabilir.
- **Azure for Students aboneliği asla Pay-As-You-Go'ya yükseltilmemelidir.**
- **Azure Container Registry ve Log Analytics workspace bu mimaride bilinçli olarak
  oluşturulmuyor** — ikisi de sürekli (idle'dayken bile) ücret doğurabilir; bunun yerine
  public GHCR image'ı ve `azure-monitor` **routing modu** (kalıcı bir depolama kaynağı
  değil — bkz. §3) kullanılıyor.
- **User-assigned managed identity kendi başına ek ücret oluşturmaz**, ancak Azure'ın
  fiyatlandırma politikaları zamanla değişebilir — **gerçek bir deployment'tan hemen önce
  Azure'ın güncel fiyatlandırma sayfasından tekrar kontrol edin.**
- **`minReplicas: 0` ve `maxReplicas: 1` sınırları değiştirilmemelidir.** Bu sınırlar,
  Container App'in boştayken **compute (vCPU/bellek) ücretini sıfıra indirmesini** ve
  yanlışlıkla birden fazla replika ölçeklenip beklenmedik ücret oluşmasını önlemek için var.
- **Kalan kredi süresi bu dokümanda sabit bir tarih olarak yazılmıyor** — gerçek bir
  deployment'tan hemen önce, Azure portalında "Cost Management + Billing" → "Azure for
  Students" sayfasından kalan kredi ve bitiş tarihi mutlaka tekrar kontrol edilmelidir.
- **Bir Azure Budget/cost alert oluşturmak harcamayı otomatik olarak DURDURMAZ** — yalnızca
  bir eşiğe ulaşıldığında bir bildirim/e-posta gönderir.
- **Azure Marketplace'ten hiçbir ürün/kaynak kullanılmayacaktır.**
- **Bu abonelikte bir "izin verilen deployment bölgeleri" (`sys.regionrestriction`) policy'si
  tespit edildi** — West Europe ve North Europe bu policy tarafından **reddedildi**. Gerçek
  deployment'tan önce, izin verilen bölge listesini kendi aboneliğinizde tekrar kontrol edin:
  ```
  az policy assignment show --name sys.regionrestriction --query "parameters.listOfAllowedLocations.value" -o json
  ```
  Bu liste zamanla değişebilir; bu dokümanı yazarken izinli olan bölgeler arasında
  `polandcentral` de vardı, ancak bunu sabit bir gerçek olarak varsaymayın.
- **Krediniz bitmeden, kullanmadığınız zaman kaynaklar kaldırılmalıdır** — bkz. §9 "Cleanup".

## 3. Log routing kararı: `azure-monitor`, `none` değil

Container Apps environment'ının `appLogsConfiguration.destination` değeri **`azure-monitor`**
olarak ayarlandı (bkz. `modules/containerAppsEnvironment.bicep`), **`none`** değil.

- **Neden değişti:** `'none'` değeri Bicep'in statik tip kontrolünden geçiyordu, ancak
  **gerçek `Microsoft.App/managedEnvironments` (2024-03-01) resource provider'ı**, Poland
  Central'da canlı bir `--validation-level Provider` what-if önizlemesi sırasında bunu
  reddetti: *"App Logs destination 'none' not supported. Supported values: 'log-analytics',
  'azure-monitor'."*
- **`azure-monitor` yalnızca bir routing modudur** — `'log-analytics'`'in aksine (ki o bir Log
  Analytics workspace + `customerId`/`sharedKey` gerektirir), hiçbir ek workspace, diagnostic
  setting veya Application Insights kaynağı **gerektirmez** ve bu şablonda hiçbiri
  oluşturulmuyor.
- **Bu "kesin sıfır maliyet" garantisi DEĞİLDİR** — yalnızca ücretli, kalıcı bir log depolama
  kaynağının (workspace) burada oluşturulmadığı anlamına gelir.
- Staging sırasında gerektiğinde gerçek zamanlı log stream kullanılabilir:
  ```
  az containerapp logs show --resource-group rg-repopulse-staging --name ca-repopulse-authapi-staging --follow
  ```
- Sonradan kalıcı bir log hedefi (Log Analytics workspace + diagnostic settings) eklemek,
  **ayrı bir maliyet/güvenlik kararı** gerektirir — bu şablonun kapsamında değildir.

## 4. Ön koşullar (elle, sırayla)

1. Azure portalında kalan Azure for Students kredisini ve bitiş tarihini tekrar kontrol edin (bkz. §2).
2. Bir Azure Budget/cost alert oluşturun (Azure portalı üzerinden "Cost Management + Billing"
   → "Budgets" ile elle yapılır).
3. İzin verilen deployment bölgelerini tekrar kontrol edin (bkz. §2'deki `az policy assignment
   show` komutu).
4. Aşağıdaki provider'ları kaydedin (**bu dokümanın son güncellendiği turda çalıştırılmadı**,
   yalnızca referans komutlar — önceki bir turda bu üçü zaten kaydedilmişti):

   ```
   az provider register --namespace Microsoft.App
   az provider register --namespace Microsoft.KeyVault
   az provider register --namespace Microsoft.ManagedIdentity
   ```

   `Microsoft.ContainerRegistry` ve `Microsoft.OperationalInsights`/`Microsoft.Insights`
   **kasıtlı olarak bu listede yok.**

5. Kayıt durumunu doğrulayın:

   ```
   az provider show --namespace Microsoft.App --query registrationState -o tsv
   az provider show --namespace Microsoft.KeyVault --query registrationState -o tsv
   az provider show --namespace Microsoft.ManagedIdentity --query registrationState -o tsv
   ```

6. `infra/azure/main.example.bicepparam` dosyasını `infra/azure/main.bicepparam` olarak
   kopyalayın (bu dosya `.gitignore`'da — asla commit etmeyin) ve gerçek `tenantId` ile
   benzersiz bir `keyVaultName` girin.
7. `infra/azure/app.example.bicepparam` dosyasını `infra/azure/app.bicepparam` olarak
   kopyalayın (bu dosya da `.gitignore`'da). `containerImageDigest` alanını henüz
   **doldurmayın** — bu, GHCR'a gerçek bir image push edildikten sonra, registry'nin
   döndürdüğü gerçek `sha256:<64 hex karakter>` digest'i ile doldurulacak (bkz. §6).

## 5. Bicep doğrulama (deployment değildir, Azure'a bağlanmaz)

```
az bicep build --file infra/azure/main.bicep
az bicep build --file infra/azure/app.bicep
az bicep build --file infra/azure/modules/containerAppsEnvironment.bicep
az bicep build --file infra/azure/modules/keyVault.bicep
az bicep build --file infra/azure/modules/userAssignedIdentity.bicep
az bicep build --file infra/azure/modules/containerApp.bicep
az bicep build --file infra/azure/modules/keyVaultAccess.bicep
```

Bunlar yalnızca yerel derleme/söz dizimi kontrolüdür — hiçbir Azure kaynağına dokunmaz,
kimlik doğrulaması gerektirmez, ve **gerçek resource provider'ın canlı doğrulamasını temsil
etmez** (bkz. §3'teki `'none'`/`'azure-monitor'` deneyimi — Bicep build'in geçmesi yalnızca
sözdizimi doğrulamasıdır, gerçek deployment başarısının garantisi değildir). CI'da da otomatik
çalışıyor (bkz. `.github/workflows/bicep-validate.yml`).

## 6. GHCR image'ını hazırlama

Bu Bicep şablonu, `ghcr.io/mustafanazli/repopulse-authapi` reposunu (bu repo adı
`modules/containerApp.bicep` içinde sabittir) yalnızca **image digest'i ile**
(`@sha256:<64 hex karakter>`) adresler — hiçbir etiket (tag) kabul edilmez. Image'ı GHCR'a
push etmek için `.github/workflows/authapi-publish-ghcr.yml` (yalnızca `workflow_dispatch`,
yalnızca `main`) kullanılır. Push sonrası, workflow'un job summary'sinde yazan **gerçek
digest değeri** alınıp `app.bicepparam`'daki `containerImageDigest` parametresine
yazılmalıdır. **`latest` etiketi veya herhangi bir mutable etiket bu tasarımda hiçbir
şekilde kullanılamaz.**

## 7. Deployment sırası (kesin sıra)

1. **Portal kredi/bitiş kontrolü** — bkz. §2.
2. **Faz A what-if** (yalnızca önizleme, Azure'a hiçbir kaynak yazmaz):

   ```
   az deployment sub what-if \
     --name repopulse-staging-preview \
     --location <IZINLI_BOLGE> \
     --template-file infra/azure/main.bicep \
     --parameters infra/azure/main.bicepparam \
     --result-format ResourceIdOnly \
     --validation-level Provider \
     --no-prompt true \
     --only-show-errors
   ```

3. **Kullanıcı onayıyla Faz A deployment:**

   ```
   az deployment sub create \
     --name repopulse-staging \
     --location <IZINLI_BOLGE> \
     --template-file infra/azure/main.bicep \
     --parameters infra/azure/main.bicepparam
   ```

4. **Identity ve Key Vault rolünü doğrulayın** (salt-okunur):

   ```
   az identity show --resource-group rg-repopulse-staging --name id-repopulse-authapi-staging --query principalId -o tsv
   az role assignment list --scope <KEY_VAULT_RESOURCE_ID> --query "[].roleDefinitionName" -o tsv
   ```

   `"Key Vault Secrets User"` rolünün listede olduğunu doğrulayın.

5. **Kullanıcı, gerçek `client_secret` değerini hiçbir agent/asistan görmeden Key Vault'a elle
   ekler** — bkz. §8. **Hiçbir agent bu adımı sizin yerinize çalıştırmamalı veya değeri
   görmemelidir.**

6. **Secret metadata varlığını, değeri okumadan doğrulayın:**

   ```
   az keyvault secret list --vault-name <KEY_VAULT_NAME> --query "[].name" -o tsv
   az keyvault secret show --vault-name <KEY_VAULT_NAME> --name github-oauth-client-secret --query "attributes" -o json
   ```

   Bu komutlar yalnızca secret'ın **var olduğunu ve ne zaman oluşturulduğunu** gösterir —
   `--query value` **asla kullanılmamalıdır.**

7. **Faz B what-if:**

   ```
   az deployment group what-if \
     --resource-group rg-repopulse-staging \
     --name repopulse-app-preview \
     --template-file infra/azure/app.bicep \
     --parameters infra/azure/app.bicepparam \
     --result-format ResourceIdOnly \
     --no-prompt true \
     --only-show-errors
   ```

8. **Kullanıcı onayıyla Faz B deployment:**

   ```
   az deployment group create \
     --resource-group rg-repopulse-staging \
     --name repopulse-app \
     --template-file infra/azure/app.bicep \
     --parameters infra/azure/app.bicepparam
   ```

9. **`/health` doğrulaması ve rate-limit staging testleri** — bkz. §10.
10. **Kontrollü cleanup** — bkz. §9.

**Faz B, Faz A'nın identity/rol ataması tamamlanmadan ve gerçek secret Key Vault'ta
mevcut olmadan ASLA çalıştırılmamalıdır.**

## 8. Gerçek secret'ı Key Vault'a ekleme — yalnızca portal üzerinden, elle

Gerçek `client_secret` değerini **komut satırı geçmişine hiçbir zaman yazdırmayın** —
`az keyvault secret set --value "..."` gibi bir komut, değeri shell history'sine (ör.
`.bash_history`) kaydeder. Bunun yerine:

1. [Azure Portal](https://portal.azure.com) → Key Vault kaynağınız (`<KEY_VAULT_NAME>`) →
   sol menüden **"Objects" → "Secrets"** → **"+ Generate/Import"**.
2. **Name:** `github-oauth-client-secret`
3. **Secret value:** gerçek GitHub OAuth `client_secret` değerini buraya yapıştırın (yalnızca
   tarayıcıda, hiçbir terminale/loga girmeden).
4. **Create**'e tıklayın.

Yalnızca CLI'ya erişiminiz yoksa ve portal kullanamıyorsanız, `az keyvault secret set`'i
**değeri komuta doğrudan yazmadan**, güvenli bir okuma istemiyle çalıştırın (ör. bir terminal
oturumunda `read -s SECRET_VALUE` ile değişkene okutup `--value "$SECRET_VALUE"` şeklinde
kullanmak, ki bu da yalnızca değişkenin adını shell history'sine yazar, değerini değil) —
ancak **portal yöntemi tercih edilen yöntemdir.**

## 9. Cleanup (kredi bitmeden kaynakları kaldırma)

Aşağıdaki komutlar **yalnızca bu runbook'un oluşturduğu `rg-repopulse-staging` kaynak
grubunu** hedefler. **Bu abonelikte önceden var olan, RepoPulse ile ilgisiz
`TranslatorAppGrubu` kaynak grubuna KESİNLİKLE dokunulmamalıdır.**

**Silme komutunu çalıştırmadan ÖNCE, sırayla, üç ayrı doğrulama yapın:**

1. Doğru abonelikte olduğunuzu doğrulayın:

   ```
   az account show --query "{name:name, id:id}" -o table
   ```

2. Hedef kaynak grubunun adının tam olarak `rg-repopulse-staging` olduğunu doğrulayın:

   ```
   az group show --name rg-repopulse-staging --query name -o tsv
   ```

3. Silmeden önce içeriği görün:

   ```
   az resource list --resource-group rg-repopulse-staging -o table
   ```

Yalnızca yukarıdaki üç adım da beklenen sonucu verdikten sonra silme komutunu çalıştırın:

```
# Yalnızca RepoPulse staging kaynak grubunu siler — TranslatorAppGrubu'nu ETKİLEMEZ.
az group delete --name rg-repopulse-staging --yes --no-wait
```

## 10. 🛑 Rate-limit / client-IP staging testi (production trafiği açmadan ÖNCE zorunlu)

Bu test, [ADR-004](../adr/004-production-hosting.md)'te işaretlenen **production deployment
blocker**'ı gidermek için gereklidir. **Gerçek IP değerleri, token, code, verifier veya
secret hiçbir noktada log'a veya rapora yazılmamalıdır.**

1. Bir ağdan (ör. ev Wi-Fi'ı), `POST /oauth/github/exchange` uç noktasına art arda geçersiz
   (fakat biçimsel olarak geçerli) istekler göndererek rate limit'in kaçıncı istekte
   `429 rate_limited` döndüğünü gözlemleyin — yalnızca **istek sayısını ve durum kodunu**
   kaydedin.
2. **Bağımsız, ikinci bir ağdan** (ör. telefonun mobil veri bağlantısı) aynı testi tekrarlayın.
3. **İkinci ağ kendi bağımsız limitine sahipse:** partition ayrımı beklendiği gibi çalışıyor.
4. **İki ağ aynı kotayı paylaşıyorsa:** production deployment blocker'ı hâlâ geçerlidir, kod
   değiştirilmeden production trafiği açılmamalıdır.
5. Sonuç (yalnızca "ayrıştı" / "ayrışmadı", ham detay olmadan) ADR-004'e eklenmelidir.
6. **Bu test tamamlanıp sonucu belgelenmeden production trafiği açılmamalıdır.**

## 11. Özet: gerçek deployment öncesi açık kullanıcı onayları

- [ ] Azure portalında kalan kredi/bitiş tarihinin son kez kontrolü
- [ ] Bir Azure Budget/cost alert'in oluşturulması
- [ ] İzin verilen deployment bölgelerinin (`sys.regionrestriction`) tekrar kontrolü
- [ ] Faz A what-if'in incelenmesi ve onaylanması
- [ ] Faz A deployment'ının (`az deployment sub create`) çalıştırılması onayı
- [ ] Identity + Key Vault rolünün doğrulanması
- [ ] Gerçek secret'ın portal üzerinden Key Vault'a elle eklenmesi
- [ ] Secret metadata'sının (değeri değil) doğrulanması
- [ ] Gerçek bir GHCR image'ının build edilip push edilmesi ve digest'inin alınması
- [ ] `app.bicepparam`'ın gerçek digest ile doldurulması
- [ ] Faz B what-if'in incelenmesi ve onaylanması
- [ ] Faz B deployment'ının (`az deployment group create`) çalıştırılması onayı
- [ ] `/health` ve rate-limit/client-IP staging testinin tamamlanması ve belgelenmesi
- [ ] Yalnızca yukarıdakilerin hepsi tamamlandıktan sonra production trafiğinin açılması
