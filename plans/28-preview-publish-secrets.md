# Provisioning the GitHub secrets for `preview-publish.yml`

The new workflow `.github/workflows/preview-publish.yml` mirrors
`.azurepipelines/preview.yml` end-to-end and publishes to GitHub
Packages.  Until the seven signing-related repository secrets below
exist, runs produce **unsigned** packages (the workflow gracefully
skips the signing steps and logs a warning).

These instructions assume **repository-level** secrets on
`OPCFoundation/UA-.NETStandard`.  All secrets are scoped to that one
repo; nothing org-wide.

---

## 0. What needs to exist where

| GitHub repo secret | Lives in Azure DevOps as |
| --- | --- |
| `OPCFOUNDATION_NETSTANDARD_SNK_BASE64` | **Secure Files → `OPCFoundation.NetStandard.Key.snk`** (base64-encoded for transport) |
| `SIGNING_URL` | Library → Variable group `codesign` → `SigningURL` |
| `SIGNING_VAULT_URL` | `codesign` → `SigningVaultURL` |
| `SIGNING_TENANT_ID` | `codesign` → `SigningTenantId` |
| `SIGNING_CLIENT_ID` | `codesign` → `SigningClientId` |
| `SIGNING_CLIENT_SECRET` | `codesign` → `SigningClientSecret` (masked) |
| `SIGNING_CERT_NAME` | `codesign` → `SigningCertName` |

**Required permissions:**

- Azure DevOps: project administrator on `opcua-netstandard`, OR
  release administrator, OR explicit Reader+User on the Library /
  Secure Files of that project.
- GitHub: Admin on `OPCFoundation/UA-.NETStandard`.

---

## 1. Pull the values from Azure DevOps

### 1a. Strong-name key (`OPCFoundation.NetStandard.Key.snk`)

1. Open
   [Secure Files](https://opcfoundation.visualstudio.com/opcua-netstandard/_library?itemType=SecureFiles).
2. Click **`OPCFoundation.NetStandard.Key.snk`**.
3. ⋯ → **Download secure file** → save to e.g.
   `C:\Temp\OPCFoundation.NetStandard.Key.snk`.
4. Treat the file as production crypto material from this point.
   Delete it from disk after step 2 below.

There is no supported `az pipelines` CLI command to download a Secure
File; the UI path is the only option.

### 1b. The 6 signing values (`codesign` variable group)

1. Open
   [Variable Groups](https://opcfoundation.visualstudio.com/opcua-netstandard/_library?itemType=VariableGroups).
2. Click **`codesign`**.
3. Copy the value of each variable:
   - `SigningURL`, `SigningVaultURL`, `SigningTenantId`,
     `SigningClientId`, `SigningCertName` are plain (visible).
   - `SigningClientSecret` is masked – click the eye icon.  If the
     icon is greyed out you don't have permission; ask the release
     manager.

CLI alternative for the five non-secret values:

```powershell
az pipelines variable-group list `
  --org https://opcfoundation.visualstudio.com `
  --project opcua-netstandard `
  --query "[?name=='codesign'].variables" -o json
```

The CLI returns `"value": null` for the masked client secret, so the
UI is required for that one.

### 1c. If `SigningClientSecret` is expired or rotated

Mint a fresh AAD client secret instead of reusing the AzDO copy:

```powershell
az login
# Find the app (the SigningClientId from step 1b matches `appId`):
az ad app list --display-name "OPCFoundation*" `
   --query "[].{name:displayName,appId:appId}"
# Mint a new 1-year client secret for the signing app:
az ad app credential reset --id <SigningClientId> --years 1
# Capture the printed `password` field — that is the new secret.
```

Requires you to be an **Owner** of the AAD application.

---

## 2. Base64-encode the SNK

```powershell
$snk = "C:\Temp\OPCFoundation.NetStandard.Key.snk"
$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($snk))
$base64.Length      # sanity: a few hundred chars, all base64-safe
Set-Clipboard $base64
# When done, remove the local file:
Remove-Item $snk
```

The workflow's `Decode Strong Name Key` step does the inverse:

```pwsh
[System.IO.File]::WriteAllBytes(
    $snkPath,
    [System.Convert]::FromBase64String($env:OPCFOUNDATION_NETSTANDARD_SNK_BASE64))
```

---

## 3. Add the 7 secrets to the GitHub repository

### 3a. UI

For each of the 7 secrets:

1. Open
   [Repo Actions secrets](https://github.com/OPCFoundation/UA-.NETStandard/settings/secrets/actions).
2. Click **New repository secret**.
3. **Name** = exact GitHub secret name from §0 (case-sensitive).
4. **Secret** = pasted value from §1, or the base64 string from §2 for
   the SNK.
5. Save.

### 3b. CLI alternative (`gh secret set`)

```powershell
# Authenticate as a repo admin:
gh auth status
gh auth refresh -h github.com -s "admin:org,repo,workflow"

cd D:\git\UA-.NETStandard2   # any path inside the repo works

# Plain-text values (paste each verbatim):
gh secret set SIGNING_URL          --body "<SigningURL value>"
gh secret set SIGNING_VAULT_URL    --body "<SigningVaultURL value>"
gh secret set SIGNING_TENANT_ID    --body "<SigningTenantId value>"
gh secret set SIGNING_CLIENT_ID    --body "<SigningClientId value>"
gh secret set SIGNING_CERT_NAME    --body "<SigningCertName value>"

# Sensitive — pipe via stdin so it never lands in shell history:
'<the client secret>' | gh secret set SIGNING_CLIENT_SECRET

# Base64-encoded SNK already in clipboard from §2:
(Get-Clipboard) | gh secret set OPCFOUNDATION_NETSTANDARD_SNK_BASE64
```

### 3c. Verify

```powershell
gh secret list --repo OPCFoundation/UA-.NETStandard
```

Expected output: all 7 of the above plus the pre-existing
`CODECOV_TOKEN`.  Values are never returned by the API.

---

## 4. Smoke-test the workflow

1. Open
   [Preview Publish workflow](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/preview-publish.yml).
2. **Run workflow** → pick branch `master` (or any feature branch) →
   **Run workflow**.
3. Watch the run and confirm:
   - **Decode Strong Name Key** runs (NOT "Warn if Strong Name Key
     absent").
   - **List assemblies to sign** + **Sign Assemblies (Azure Key Vault
     Authenticode)** run (NOT "Warn if Authenticode signing skipped").
   - **Sign NuGet packages (nupkg + snupkg)** runs.
   - **Push to GitHub Packages** ends with `Pushed ... → OK`.
4. After success the packages appear at
   <https://github.com/orgs/OPCFoundation/packages>.  Consumers add
   the feed to their `nuget.config`:

   ```xml
   <packageSources>
     <add key="opcua-gh"
          value="https://nuget.pkg.github.com/OPCFoundation/index.json" />
   </packageSources>
   ```

---

## 5. Optional hardening (do later, not blocking)

### 5a. Gate on a GitHub Environment with required reviewers

Create an Environment named e.g. `signing` with required reviewers,
move the 7 secrets to that environment, and add `environment: signing`
to the `pack-and-publish` job.  A release manager must then approve
every signed publish.

### 5b. Drop `SIGNING_CLIENT_SECRET` via OIDC federation

Replace the client-secret credential with workload identity federation
to GitHub's OIDC provider:

```powershell
az ad app federated-credential create --id <SigningClientId> --parameters '{
  "name": "github-opcf-ua-netstandard-preview-publish",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:OPCFoundation/UA-.NETStandard:environment:signing",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

Then the workflow gains an `azure/login@v2` step (using
`client-id` / `tenant-id` / `subscription-id`) and AzureSignTool /
NuGetKeyVaultSignTool drop the `-kvi`/`-kvt`/`-kvs` arguments in
favour of the established session.  `SIGNING_CLIENT_SECRET` can then
be deleted entirely.

### 5c. Routine rotation

Rotate `SigningClientSecret` every 6–12 months.  Date-stamp the entry
in AzDO's `codesign` variable group when you rotate so the GitHub
copy gets re-synced at the same time.
