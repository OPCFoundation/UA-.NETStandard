# OPC UA Global Discovery Server (GDS) — Developer Guide

This package implements the OPC UA Global Discovery Server (GDS) as
defined in OPC 10000-12 (Part 12). It provides application
registration, certificate lifecycle management (pull and push models),
KeyCredentialService, AuthorizationService, and role-based access
control.

## Quick Links

| Topic | Document |
|-------|----------|
| GDS core (this file) | Directory, certificates, push management, roles |
| [KeyCredentialService](KeyCredentialService.md) | Credential issuance for non-UA services |
| [AuthorizationService](AuthorizationService.md) | OAuth2-style access token issuance |

## Packages

| NuGet Package | Contents |
|---------------|----------|
| `Opc.Ua.Gds.Common` | Model types, encodeable types, design files |
| `Opc.Ua.Gds.Server.Common` | Server-side node managers, providers, authorization |
| `Opc.Ua.Gds.Client.Common` | Client proxies for GDS, push, KeyCredential, AuthorizationService |

---

## 1. Client API

### GlobalDiscoveryServerClient

The primary client for interacting with a GDS. Manages its own
session and exposes the full GDS directory and certificate
management API.

```csharp
using Opc.Ua.Gds.Client;

// Create and connect
var gdsClient = new GlobalDiscoveryServerClient(appConfig);
gdsClient.AdminCredentials = new UserIdentity("admin", "password");
await gdsClient.ConnectAsync("opc.tcp://gds-host:4840");

// Register an application
var appRecord = new ApplicationRecordDataType
{
    ApplicationUri = "urn:my-app",
    ApplicationType = ApplicationType.Server,
    ApplicationNames = new[] { new LocalizedText("My App") }.ToArrayOf(),
    DiscoveryUrls = new[] { "opc.tcp://my-app:4850" }.ToArrayOf()
};
NodeId appId = await gdsClient.RegisterApplicationAsync(appRecord);

// Request a new certificate (pull model)
NodeId requestId = await gdsClient.StartNewKeyPairRequestAsync(
    appId, default, ObjectTypeIds.RsaSha256ApplicationCertificateType,
    "CN=My App", domainNames, "PFX", null);

// Poll for completion
ByteString cert, privateKey;
ArrayOf<ByteString> issuerCerts;
do
{
    await Task.Delay(2000);
    (cert, privateKey, issuerCerts) =
        await gdsClient.FinishRequestAsync(requestId, default);
} while (cert.IsEmpty);

// Clean up
await gdsClient.DisconnectAsync();
```

### ServerPushConfigurationClient

Client for OPC 10000-12 §7.10 push certificate management on a
target server:

```csharp
var pushClient = new ServerPushConfigurationClient(appConfig);
await pushClient.ConnectAsync(serverEndpoint);

// Push a new certificate
bool restartNeeded = await pushClient.UpdateCertificateAsync(
    pushClient.DefaultApplicationGroup,
    ObjectTypeIds.RsaSha256ApplicationCertificateType,
    newCertBlob, "PFX", privateKeyBlob, issuerCerts);

if (restartNeeded)
{
    await pushClient.ApplyChangesAsync();
}

// Create a self-signed certificate on the server
await pushClient.CreateSelfSignedCertificateAsync(
    pushClient.DefaultApplicationGroup,
    ObjectTypeIds.RsaSha256ApplicationCertificateType,
    "CN=NewSubject", domainNames);
```

---

## 2. Server-Side: Building a GDS

### Minimal GDS Server

Use `GlobalDiscoverySampleServer` with dependency injection of
your storage backends:

```csharp
// 1. Create storage backends
IApplicationsDatabase database = JsonApplicationsDatabase.Load(
    "gds-applications.json", telemetry);
ICertificateGroup certGroup = new CertificateGroup(
    gdsConfig.AuthoritiesStorePath, gdsConfig.CertificateGroups);
IUserDatabase userDb = JsonUserDatabase.Load(
    "gds-users.json", telemetry);

// 2. Create the GDS server
// (database implements both IApplicationsDatabase and ICertificateRequest)
var gdsServer = new GlobalDiscoverySampleServer(
    database, database, certGroup, userDb, telemetry,
    autoApprove: true);

// 3. Start via ApplicationInstance
var app = new ApplicationInstance(telemetry)
{
    ApplicationName = "My GDS",
    ApplicationType = ApplicationType.Server,
    ConfigSectionName = "Opc.Ua.GlobalDiscoveryServer"
};
var config = await app.LoadAsync("MyGds.Config.xml");
await app.CheckApplicationInstanceCertificatesAsync(false);
await app.StartAsync(gdsServer);
```

### Using GdsNodeManagerFactory

For embedding GDS into an existing server:

```csharp
// In your server's CreateMasterNodeManager:
var gdsConfig = configuration.ParseExtension<GlobalDiscoveryServerConfiguration>();
var factory = new GdsNodeManagerFactory(gdsConfig);
additionalNodeManagers.Add(factory.Create(server, configuration));
```

### Extension Points

Every pluggable interface and its purpose:

| Interface | Purpose | Default |
|-----------|---------|---------|
| `IApplicationsDatabase` | Application registration store | `LinqApplicationsDatabase` / `JsonApplicationsDatabase` |
| `ICertificateRequest` | Certificate request lifecycle | `LinqApplicationsDatabase` |
| `ICertificateGroup` | CA operations (sign, revoke) | `CertificateGroup` |
| `IGdsUserDatabase` | User store + ApplicationAdmin bindings | Implement over `LinqUserDatabase` |
| `IKeyCredentialRequestStore` | KeyCredential lifecycle | `InMemoryKeyCredentialRequestStore` |
| `ISecretStore` | Secret storage for credentials | `InMemorySecretStore` |
| `IAccessTokenProvider` | Token issuance (AuthorizationService) | `null` (Bad_NotSupported) |
| `IConfigurationDataStore` | ManagedApplications config persistence | `InMemoryConfigurationDataStore` |
| `IManagedApplicationsNodeManager` | ManagedApplications folder | `StubManagedApplicationsNodeManager` |

---

## 3. Implementing Providers

### IApplicationsDatabase

Stores registered application records. Must also implement
`ICertificateRequest` for certificate request lifecycle (or provide
a separate implementation):

```csharp
public class SqlApplicationsDatabase
    : ApplicationsDatabaseBase, ICertificateRequest
{
    // ApplicationsDatabaseBase provides Match() and ValidateApplication()
    // Override the abstract CRUD methods:

    public override NodeId RegisterApplication(
        ApplicationRecordDataType application)
    {
        ValidateApplication(application); // call base validation
        return _db.InsertApplication(application);
    }

    public override void UpdateApplication(
        ApplicationRecordDataType application)
    {
        ValidateApplication(application);
        _db.UpdateApplication(application);
    }
    // ... implement remaining abstract methods
}
```

### ICertificateGroup

Implement to use an external CA (EST, ACME, EJBCA, etc.):

```csharp
public class EstCertificateGroup : ICertificateGroup
{
    public async Task<(Certificate, byte[], CertificateCollection)>
        NewKeyPairRequestAsync(
            ApplicationRecordDataType application,
            string subjectName,
            string[] domainNames,
            string privateKeyFormat,
            string privateKeyPassword)
    {
        // Call your EST server's /simpleenroll endpoint
        // Return (signedCert, privateKey, issuerChain)
    }
    // ... implement remaining interface methods
}
```

### IGdsUserDatabase

Extend `IUserDatabase` to support the `ApplicationAdmin` role
(OPC 10000-12 §7.2). The GDS sample server automatically detects
`IGdsUserDatabase` during username authentication:

```csharp
public class MyGdsUserDatabase : LinqUserDatabase, IGdsUserDatabase
{
    private readonly Dictionary<string, List<NodeId>> _appAdminMap = new();

    public IReadOnlyList<NodeId>? GetAdministeredApplicationIds(
        string userName)
    {
        return _appAdminMap.TryGetValue(userName, out var ids)
            ? ids : null;
    }

    // Admin API to grant ApplicationAdmin for specific apps:
    public void GrantApplicationAdmin(
        string userName, IEnumerable<NodeId> applicationIds)
    {
        _appAdminMap[userName] = applicationIds.ToList();
    }
}
```

### IConfigurationDataStore

Persistence backend for ManagedApplications (§7.10.16). The
`DefaultManagedApplicationsNodeManager` uses this to populate
`ApplicationConfigurationState` nodes:

```csharp
public class FileConfigurationDataStore : IConfigurationDataStore
{
    public async ValueTask<IReadOnlyList<ManagedApplicationInfo>>
        GetManagedApplicationsAsync(CancellationToken ct)
    {
        // Read from config file / database
    }

    public async ValueTask<uint> WriteConfigurationAsync(
        string applicationUri, byte[] data,
        uint currentVersion, CancellationToken ct)
    {
        // Persist with optimistic concurrency check
    }

    public async ValueTask ConfirmUpdateAsync(
        string applicationUri, uint configVersion,
        CancellationToken ct)
    {
        // Mark the configuration as applied
    }
}
```

---

## 4. Roles and Authorization

### GDS Roles (OPC 10000-12 §7.2)

| Role | Purpose |
|------|---------|
| `DiscoveryAdmin` | Register/update/unregister any application |
| `CertificateAuthorityAdmin` | Manage certificate requests and trust lists |
| `RegistrationAuthorityAdmin` | Approve/reject certificate requests |
| `ApplicationSelfAdmin` | Manage own application's registration |
| `ApplicationAdmin` | Manage a configured set of applications |

### ApplicationSelfAdmin Privilege

`GdsApplicationSelfAdminProvider` grants `GdsRole.ApplicationSelfAdmin`
when the secure-channel client certificate matches a certificate stored
for the registered `ApplicationRecordDataType` found by the channel
ApplicationUri. The provider is an `IIdentityAugmenter`, so it layers
SelfAdmin on top of any accepted identity instead of using a custom
`ImpersonateUser` callback.

For DI-hosted GDS servers the provider is registered with the symmetric
builder extension:

```csharp
services.AddOpcUa()
    .AddGdsServer(opt => opt.ApplicationName = "MyGds")
    .AddDefaultIdentityAuthenticators(opt => opt.EnableJwt = false)
    .AddGdsApplicationSelfAdminProvider();
```

`AddDefaultIdentityAuthenticators(...)` on `IGdsServerBuilder` also
adds this provider so standard GDS hosts get OPC 10000-12 §7.2
SelfAdmin behavior automatically.

### ApplicationAdmin Privilege

The `ApplicationAdmin` role allows a user to administer a specific
set of applications (not all applications like `DiscoveryAdmin`).

**How it works:**

1. Assign the `GdsRole.ApplicationAdmin` role to the user via
   `IUserDatabase.CreateUser()`
2. Implement `IGdsUserDatabase.GetAdministeredApplicationIds()` to
   return the `ApplicationId`s the user may administer
3. During username authentication, `GlobalDiscoverySampleServer` constructs a
   `GdsRoleBasedIdentity` with `AdministeredApplicationIds` populated
4. `AuthorizationHelper.CheckApplicationAdminPrivilege()` verifies
   the target application is in the user's administered set

```csharp
// Setup: create a user with ApplicationAdmin role
userDb.CreateUser("app-admin", passwordBytes,
    [GdsRole.ApplicationAdmin]);

// If using IGdsUserDatabase:
((MyGdsUserDatabase)userDb).GrantApplicationAdmin(
    "app-admin",
    [registeredAppId1, registeredAppId2]);
```

### Security: Fail-Closed Channel Validation

`AuthorizationHelper.HasAuthenticatedSecureChannel()` enforces that
all GDS methods are called over a signed (or encrypted) secure
channel. The check **fails closed** — if the system context cannot
be validated, the method throws `BadSecurityModeInsufficient`.

---

## 5. End-to-End Example

Complete example: register an application, get a certificate, push
it to the server.

```csharp
// ── 1. Start the GDS Server ─────────────────────────

var database = JsonApplicationsDatabase.Load("apps.json", telemetry);
var certGroup = new CertificateGroup(authStorePath, certGroupConfigs);
var userDb = JsonUserDatabase.Load("users.json", telemetry);

var gds = new GlobalDiscoverySampleServer(
    database, database, certGroup, userDb, telemetry);

var app = new ApplicationInstance(telemetry) { ... };
await app.StartAsync(gds);

// ── 2. Client: Register and Get Certificate ─────────

var gdsClient = new GlobalDiscoveryServerClient(clientConfig);
gdsClient.AdminCredentials = new UserIdentity("admin", "password");
await gdsClient.ConnectAsync("opc.tcp://localhost:58810");

// Register
var record = new ApplicationRecordDataType
{
    ApplicationUri = "urn:my-server",
    ApplicationType = ApplicationType.Server,
    ApplicationNames = new[] { new LocalizedText("My Server") }.ToArrayOf(),
    DiscoveryUrls = new[] { "opc.tcp://my-server:4840" }.ToArrayOf()
};
NodeId appId = await gdsClient.RegisterApplicationAsync(record);

// Request certificate
NodeId reqId = await gdsClient.StartNewKeyPairRequestAsync(
    appId, default,
    ObjectTypeIds.RsaSha256ApplicationCertificateType,
    "CN=My Server,O=My Org",
    new[] { "my-server", "localhost" }.ToArrayOf(),
    "PFX", null);

// Wait for completion
ByteString cert, key;
ArrayOf<ByteString> issuers;
do
{
    await Task.Delay(2000);
    (cert, key, issuers) =
        await gdsClient.FinishRequestAsync(reqId, default);
} while (cert.IsEmpty);

// ── 3. Push Certificate to Target Server ────────────

var pushClient = new ServerPushConfigurationClient(clientConfig);
await pushClient.ConnectAsync(serverEndpoint);

bool needsRestart = await pushClient.UpdateCertificateAsync(
    pushClient.DefaultApplicationGroup,
    ObjectTypeIds.RsaSha256ApplicationCertificateType,
    cert, "PFX", key, issuers);

if (needsRestart)
{
    await pushClient.ApplyChangesAsync();
}

// ── 4. Clean Up ─────────────────────────────────────

await pushClient.DisconnectAsync();
await gdsClient.DisconnectAsync();
```

---

## Conformance Matrix

Status key: ✅ Implemented | ⚠️ Partial | ❌ Not implemented | N/A Not applicable

## GDS Directory (§6.5)

| Section | Feature | Status | Source |
|---------|---------|--------|--------|
| §6.5.3 | DirectoryType | ✅ | `ApplicationsNodeManager.cs` |
| §6.5.4 | FindApplications | ✅ | `OnFindApplications` |
| §6.5.5 | ApplicationRecordDataType / rcp+ rules | ✅ | `ApplicationsDatabaseBase.ValidateApplication` |
| §6.5.6 | RegisterApplication | ✅ | `OnRegisterApplication` + `DiscoveryAdminOrAppAdmin` |
| §6.5.7 | UpdateApplication | ✅ | `OnUpdateApplication` + `DiscoveryAdminOrSelfAdminOrAppAdmin` |
| §6.5.8 | UnregisterApplication | ✅ | `OnUnregisterApplicationAsync` + cert revocation |
| §6.5.9 | GetApplication | ✅ | `OnGetApplication` |
| §6.5.10 | QueryApplications | ✅ | `OnQueryApplications` |
| §6.5.11 | QueryServers | ✅ | `OnQueryServers` |
| §6.5.12 | ApplicationRegistrationChangedAuditEvent | ✅ | `AuditEvents.ReportApplicationRegistrationChangedAuditEvent` |

## Certificate Management — Pull Model (§7.6)

| Section | Feature | Status | Source |
|---------|---------|--------|--------|
| §7.6.4 | StartNewKeyPairRequest | ✅ | `OnStartNewKeyPairRequest` + audit |
| §7.6.5 | StartSigningRequest | ✅ | `OnStartSigningRequestAsync` + audit |
| §7.6.6 | FinishRequest | ✅ | `OnFinishRequestAsync` + issuer chain |
| §7.6.7 | GetCertificateGroups | ✅ | `OnGetCertificateGroups` |
| §7.6.8 | GetTrustList | ✅ | `OnGetTrustList` |
| §7.6.9 | RevokeCertificate | ✅ | `OnRevokeCertificateAsync` + audit |
| §7.6.10 | GetCertificates | ✅ | `OnGetCertificates` |
| §7.6.11 | CheckRevocationStatus | ✅ | `OnCheckRevocationStatusAsync` + ValidityTime |
| §7.6.12 | GetCertificateStatus | ✅ | `OnGetCertificateStatus` |

## Roles and Privileges (§7.2)

| Feature | Status | Source |
|---------|--------|--------|
| DiscoveryAdmin | ✅ | `GdsRole.DiscoveryAdmin` |
| CertificateAuthorityAdmin | ✅ | `GdsRole.CertificateAuthorityAdmin` |
| RegistrationAuthorityAdmin | ✅ | `GdsRole.RegistrationAuthorityAdmin` |
| ApplicationSelfAdmin | ✅ | `GdsRole.ApplicationSelfAdmin` |
| ApplicationAdmin | ✅ | `GdsRole.ApplicationAdmin` + `AdministeredApplicationIds` |
| BadSecurityModeInsufficient | ✅ | `AuthorizationHelper.HasAuthenticatedSecureChannel` |

## Push Management — ServerConfiguration (§7.10)

| Section | Feature | Status | Source |
|---------|---------|--------|--------|
| §7.10.3 | UpdateCertificate | ✅ | `ConfigurationNodeManager.UpdateCertificateAsync` |
| §7.10.4 | CreateSigningRequest | ✅ | `ConfigurationNodeManager.CreateSigningRequestAsync` |
| §7.10.5 | ApplyChanges | ✅ | `ConfigurationNodeManager.ApplyChanges` |
| §7.10.6 | CreateSelfSignedCertificate | ✅ | `ConfigurationNodeManager.CreateSelfSignedCertificateAsync` |
| §7.10.7 | GetCertificates | ✅ | `ConfigurationNodeManager.GetCertificates` |
| §7.10.8 | GetRejectedList | ✅ | `ConfigurationNodeManager.GetRejectedList` |
| §7.10.10 | ConfirmUpdate | ⚠️ | `DefaultManagedApplicationsNodeManager` + `IConfigurationDataStore` abstraction; optimistic concurrency via version tracking |
| §7.10.16 | ApplicationConfigurationType / ManagedApplications | ⚠️ | `DefaultManagedApplicationsNodeManager` creates `ApplicationConfigurationState` nodes; `IConfigurationDataStore` abstracts persistence |

## TrustList (§7.8)

| Feature | Status | Source |
|---------|--------|--------|
| TrustListType / OpenWithMasks | ✅ | Core `TrustList.cs` |
| CloseAndUpdate | ✅ | Core `TrustList.cs` + TrustListUpdatedAuditEvent |
| AddCertificate / RemoveCertificate | ✅ | Core `TrustList.cs` |
| LastUpdateTime | ✅ | Set during init + CloseAndUpdate |
| Writable / UserWritable | ✅ | Set to true for GDS groups |
| ActivityTimeout / DefaultValidationOptions | ✅ | Generated from model CSV |
| CertificateExpirationAlarm | ⚠️ | Property values populated at startup; timer-based re-evaluation via `StartAlarmMonitoring` |
| TrustListOutOfDateAlarm | ⚠️ | Property values populated at startup; timer-based re-evaluation via `StartAlarmMonitoring` |

## Audit Events

| Event Type | Status | Source |
|------------|--------|--------|
| ApplicationRegistrationChangedAuditEvent | ✅ | Register/Update/Unregister |
| CertificateRequestedAuditEvent | ✅ | StartNewKeyPair + StartSigning |
| CertificateDeliveredAuditEvent | ✅ | FinishRequest |
| CertificateRevokedAuditEvent | ✅ | RevokeCertificate |
| CertificateUpdateRequestedAuditEvent | ✅ | UpdateCertificate (push) |
| TrustListUpdatedAuditEvent | ✅ | Core TrustList.cs |
| KeyCredentialRequestedAuditEvent | ✅ | StartRequest |
| KeyCredentialDeliveredAuditEvent | ✅ | FinishRequest |
| KeyCredentialRevokedAuditEvent | ✅ | Revoke |
| AccessTokenIssuedAuditEvent | ✅ | RequestAccessToken |
| Secrets redacted from audit payloads | ✅ | RedactedPrivateKeyPassword / RedactedPrivateKey |

## KeyCredentialService (§8)

| Feature | Status | Source |
|---------|--------|--------|
| StartRequest | ✅ | `OnKeyCredentialStartRequest` |
| FinishRequest | ✅ | `OnKeyCredentialFinishRequest` |
| Revoke | ✅ | `OnKeyCredentialRevoke` |
| InMemoryKeyCredentialRequestStore | ✅ | `IKeyCredentialRequestStore.cs` |
| Client proxy | ✅ | `KeyCredentialServiceClient.cs` |
| KeyCredential Push binding | ✅ | `WithKeyCredentialPush` / `KeyCredentialPushSubject` |

## AuthorizationService (§9)

| Feature | Status | Source |
|---------|--------|--------|
| GetServiceDescription | ✅ | `OnGetServiceDescription` |
| RequestAccessToken | ✅ | Legacy compatibility path via `IAccessTokenProvider.RequestAccessTokenAsync` |
| StartRequestToken (RC) | ✅ | `OnStartRequestToken` / `AuthorizationServiceManager` |
| FinishRequestToken (RC) | ✅ | `OnFinishRequestToken` / `AuthorizationServiceManager` |
| RefreshToken (RC) | ⚠️ | `IAccessTokenProvider.RefreshTokenAsync` interface defined; default provider returns Bad_NotSupported |
| SupportedRoles (RC) | ✅ | Model property exposed |
| Client proxy | ✅ | `AuthorizationServiceClient.cs` |

### Refresh-token support (planned)

`IAccessTokenProvider.RefreshTokenAsync(refreshToken, requestedRoles, ct)` is already on the interface,
but the default `InMemoryAccessTokenProvider` short-circuits to `Bad_NotSupported`. The plan is to wire
`OnRefreshTokenAsync` on `AuthorizationServiceState` to dispatch to the provider, then update
`InMemoryAccessTokenProvider` to track issued refresh tokens in a Guid-to-refresh-record dictionary,
validate them on inbound calls, and emit a fresh access token plus an optionally rotated refresh token.
`AuthorizationServiceClient.RefreshTokenAsync(...)` should mirror the server-side method. Tests should
cover refresh-token expiry, rotation, replay detection, and the round-trip with a JWT-validating client.
Track this via a follow-up commit on this branch or a separate PR; no schema changes are required because
the `RefreshToken` method state is already source-generated from `OpcUaGdsModel.xml`.

## LDS / LDS-ME (§4–5)

| Feature | Status | Source |
|---------|--------|--------|
| FindServers | ✅ | `LdsServer.FindServersAsync` |
| GetEndpoints | ✅ | `LdsServer.GetEndpointsAsync` |
| RegisterServer | ✅ | `LdsServer.RegisterServerAsync` |
| RegisterServer2 | ✅ | `LdsServer.RegisterServer2Async` |
| FindServersOnNetwork | ✅ | `LdsServer.FindServersOnNetworkAsync` |
| LDS-ME capability | ✅ | `ComputeServerCapabilities` |
| mDNS _opcua-tcp._tcp | ✅ | `MulticastDiscovery.OpcUaServiceType` |
| rcp+ reverse-connect | ✅ | `MulticastDiscovery.ReverseConnectScheme` |
| Annex C TXT keys | ✅ | path / caps / rc |
| Annex D identifiers | ✅ | LDS / LDS-ME |

## Extension Points (Phase 2 Abstractions)

Every extension point a user needs to provide or override:

| Interface | Purpose | Default impl | User replaces when... |
|-----------|---------|-------------|----------------------|
| `IApplicationsDatabase` | Application registration store | `LinqApplicationsDatabase` / `JsonApplicationsDatabase` | Using a SQL/NoSQL database |
| `ICertificateRequest` | Cert request lifecycle | `LinqApplicationsDatabase` | Custom approval workflow |
| `ICertificateGroup` | CA operations | `CertificateGroup` | External CA (EST, ACME, etc.) |
| `IKeyCredentialRequestStore` | KeyCredential lifecycle | `InMemoryKeyCredentialRequestStore` | Persistent store, external IdP |
| `ISecretStore` | Secret storage for credentials | `InMemorySecretStore` | Key Vault, DPAPI, K8s secrets |
| `IAccessTokenProvider` | Token issuance for AuthorizationService | `null` (Bad_NotSupported) | OAuth2/JWT provider |
| `IConfigurationDataStore` | ManagedApplications config persistence | `InMemoryConfigurationDataStore` | File-system, database |
| `IManagedApplicationsNodeManager` | ManagedApplications folder | `StubManagedApplicationsNodeManager` / `DefaultManagedApplicationsNodeManager` | Full push management |
| `IConfigurationNodeManager` | Alarm monitoring | `ConfigurationNodeManager.StartAlarmMonitoring` | Custom thresholds/sources |
| `IUserDatabase` | User/password store | Existing in `GlobalDiscoverySampleServer` | LDAP, database |
