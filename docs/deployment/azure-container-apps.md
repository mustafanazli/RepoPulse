# RepoPulse.AuthApi — Azure Container Apps manuel deployment adımları

> **Bu doküman yalnızca gelecekteki bir deployment için bir kontrol listesidir.**
> Hiçbir komut bu turda çalıştırılmadı, hiçbir Azure kaynağı oluşturulmadı.
> Aşağıdaki tüm `<...>` değerleri kasıtlı olarak placeholder'dır — gerçek bir
> subscription ID, tenant ID, resource group adı, registry adı, Key Vault
> adı, secret değeri veya URL burada **asla** yazılmamalıdır. Mimari
> gerekçe için bkz. [ADR-004](../adr/004-production-hosting.md).

## Ön koşullar

- Bir Azure aboneliği (`<AZURE_SUBSCRIPTION_ID>`) ve bir Azure AD tenant'ı (`<AZURE_TENANT_ID>`).
- Kaynakların oluşturulacağı bir resource group (`<RESOURCE_GROUP_NAME>`) ve bölge (`<AZURE_REGION>`, ör. `westeurope`).
- Gerçek GitHub OAuth `client_secret` değeri — **yalnızca bu adımı gerçekten uygulayan kişi tarafından**, güvenli bir kanaldan Key Vault'a girilecek. Bu değer hiçbir zaman bu dosyaya, bir commit mesajına, bir CI logına veya bir agent/asistan konuşmasına yazılmamalı.

## 1. Azure Container Registry (ACR) oluştur

```
az acr create \
  --resource-group <RESOURCE_GROUP_NAME> \
  --name <ACR_NAME> \
  --sku Basic
```

## 2. Image'ı build edip ACR'ye push et

```
az acr build \
  --registry <ACR_NAME> \
  --image repopulse-authapi:<IMAGE_TAG> \
  --file Dockerfile .
```

## 3. Azure Key Vault oluştur ve gerçek secret'ı ekle

```
az keyvault create \
  --resource-group <RESOURCE_GROUP_NAME> \
  --name <KEY_VAULT_NAME> \
  --location <AZURE_REGION>

az keyvault secret set \
  --vault-name <KEY_VAULT_NAME> \
  --name GitHubOAuth-ClientSecret \
  --value "<REAL_GITHUB_CLIENT_SECRET — yalnızca burada, terminalde, elle girilir>"
```

## 4. Container Apps ortamı oluştur

```
az containerapp env create \
  --resource-group <RESOURCE_GROUP_NAME> \
  --name <CONTAINERAPPS_ENVIRONMENT_NAME> \
  --location <AZURE_REGION>
```

## 5. Container App'i system-assigned managed identity ile oluştur

- Min replica: `0` (bkz. ADR-004).
- Target port: `8080`.
- Ingress: external, HTTPS zorunlu (Container Apps'in kendi TLS sonlandırması).
- `--system-assigned` bayrağı ile system-assigned managed identity etkinleştirilir.

```
az containerapp create \
  --resource-group <RESOURCE_GROUP_NAME> \
  --name <CONTAINERAPP_NAME> \
  --environment <CONTAINERAPPS_ENVIRONMENT_NAME> \
  --image <ACR_NAME>.azurecr.io/repopulse-authapi:<IMAGE_TAG> \
  --target-port 8080 \
  --ingress external \
  --min-replicas 0 \
  --max-replicas <MAX_REPLICA_COUNT> \
  --cpu 0.25 --memory 0.5Gi \
  --system-assigned \
  --registry-server <ACR_NAME>.azurecr.io
```

## 6. Managed identity'ye Key Vault üzerinde "Key Vault Secrets User" rolünü ver

```
az role assignment create \
  --assignee <CONTAINERAPP_MANAGED_IDENTITY_PRINCIPAL_ID> \
  --role "Key Vault Secrets User" \
  --scope <KEY_VAULT_RESOURCE_ID>
```

Yalnızca bu rol — daha geniş bir Key Vault yetkisi (ör. "Key Vault Administrator") **verilmemeli**.

## 7. Key Vault secret'ını Container Apps secret reference olarak bağla

```
az containerapp secret set \
  --resource-group <RESOURCE_GROUP_NAME> \
  --name <CONTAINERAPP_NAME> \
  --secrets github-oauth-client-secret=keyvaultref:<KEY_VAULT_SECRET_URI>,identityref:system

az containerapp update \
  --resource-group <RESOURCE_GROUP_NAME> \
  --name <CONTAINERAPP_NAME> \
  --set-env-vars \
    GitHubOAuth__ClientSecret=secretref:github-oauth-client-secret \
    Hosting__BehindTlsTerminatingProxy=true
```

`Hosting__BehindTlsTerminatingProxy=true` bu noktada açıkça set edilmeli — aksi halde uygulama kendi HTTP→HTTPS yönlendirmesini yapmaya çalışır ve Container Apps ingress'i arkasında bir yönlendirme döngüsü oluşur (bkz. ADR-004).

## 8. Azure Budget / cost alert oluştur (deployment'tan önce önerilir)

```
az consumption budget create \
  --budget-name <BUDGET_NAME> \
  --amount <BUDGET_AMOUNT> \
  --resource-group <RESOURCE_GROUP_NAME> \
  --time-grain Monthly \
  --start-date <BUDGET_START_DATE> \
  --end-date <BUDGET_END_DATE>
```

## 9. Doğrulama

- `https://<CONTAINERAPP_NAME>.<CONTAINERAPPS_ENVIRONMENT_DOMAIN>/health` → `200 {"status":"healthy"}`.
- Gerçek bir tarayıcıdan HTTP (`http://...`) ile erişim denenip 8080'de bir yönlendirme döngüsü **oluşmadığı** doğrulanmalı.
- 🛑 **PRODUCTION DEPLOYMENT BLOCKER — bu adım tamamlanmadan gerçek/genel production trafiği bu backend'e açılmamalı** (bkz. ADR-004): `POST /oauth/github/exchange` rate limiter'ının partition key'i (`RemoteIpAddress`) Container Apps ingress'i arkasında gerçek istemci IP'sini mi yoksa proxy'nin kendi IP'sini mi görüyor, staging'de doğrulanmalı. Container Apps'in gönderdiği `X-Forwarded-For` (veya eşdeğeri) header'ı incelenmeli. **Bu doğrulama sırasında gerçek/hassas IP değerleri hiçbir log satırına, rapora veya commit'e yazılmamalı** — yalnızca "beklenen davranış gözlendi/gözlenmedi" sonucu kaydedilmeli. Yalnızca bu doğrulamadan sonra, Container Apps'in gerçek çıkış IP aralıklarıyla sınırlı güvenli bir `ForwardedHeaders`/`KnownProxies` yapılandırması eklenmeli. Bu adım atlanıp doğrudan production trafiği açılırsa, rate limiter niyet edilenden çok daha agresif (tüm istemciler tek partition'da) veya etkisiz çalışabilir.

## 10. CI/CD için GitHub Actions OIDC (ayrı, sonraki bir görev)

Bu adımlar tamamlandıktan ve elle doğrulandıktan **sonra**, bir CI/CD pipeline'ı GitHub Actions OIDC (federe kimlik bilgileri, `azure/login@v2`) ile Azure'a bağlanacak şekilde ayrıca kurulacak — uzun ömürlü bir Azure servis sorumlusu parolası GitHub Actions secret'ı olarak **eklenmeyecek**. Bu doküman bu adımın komutlarını içermez; ayrı bir görev olarak ele alınacaktır.
