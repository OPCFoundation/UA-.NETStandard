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

For dependency-injection-hosted GDS servers the provider is registered with the symmetric
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

Status key: ✅ Implemented · ⚠️ Partial · 🔲 Optional, provider-gated (exposed only when configured) · ❌ Not implemented · N/A Not applicable

This matrix links every implemented OPC 10000-12 (Part 12) requirement in the ServerConfiguration / Push Certificate Management, TrustList, certificate-alarm, KeyCredentialService and AuthorizationService areas to the source that implements it and to the automated tests that exercise it. Clause numbers are **OPC 10000-12 v1.05.07**. When Part 12 behaviour changes, keep the matrix truthful: every new Method, alarm or optional child gets a row with its source *and* at least one test, and any behaviour that regresses to partial/unsupported is downgraded here. The push/transaction behaviour summarised below is documented in depth in [CertificateManager.md → PushManagement Transactions](CertificateManager.md#pushmanagement-transactions-opc-ua-part-12-7102-71011).

### Applicable Part 12 profiles and conformance units

The server-side Part 12 features map to these Facets in the OPC UA profile registry (`profiles.opcfoundation.org`, profile group **UACore 1.05**). Conformance units are marked (M)andatory or (O)ptional as declared by the Facet.

| Facet (UACore 1.05) | Key conformance units | Repo status |
|---------------------|-----------------------|-------------|
| Global Certificate Management Server Facet (`Server/GlobalCertificateManagement`) | *Pull or Push Model* (M); *Push Model for Global Certificate and TrustList Management* (O); *Pull Model for Global Certificate and TrustList Management* (O) | ✅ Push model (`ServerConfiguration`); ✅ Pull model (GDS `CertificateDirectory`) |
| A & C CertificateExpiration 2022 Server Facet (`Server/ACCertificateExpiration2022`) | *A & C CertificateExpiration*, *A & C Alarm*, *A & C Acknowledge* (M); *A & C Comment / Confirm / Shelving* (O) | ✅ mandatory CUs; optional Comment/Confirm/Shelving inherited from the base condition model |
| KeyCredential Service Server Facet (`Server/KeyCredentialManagement`) | *Push Model for KeyCredential Service*, *KeyCredential Authentication Mechanism Support* (M); *ProfileURI – MQTT UserName / AMQP SASL Plain / UA transport UserName* (O) | ✅ push model + client proxy; ProfileURIs are provider-defined |
| Global Service Authorization Request Server Facet (`Server/GlobalServiceAuthorization`) | *GDS Authorization Service Server* (M) | ✅ token issuance (`AuthorizationServiceManager`) |
| Authorization Service Server Facet (`Server/AuthorizationServiceConfiguration`) | *Authorization Service Configuration Server* (M) | ⚠️ access-token validation is provided through pluggable [Identity Providers](IdentityProviders.md); the Part 12 `ServerConfiguration.AuthorizationServices` configuration folder is not exposed |
| Global Discovery and Certificate Mgmt 2022 Server (`Server/GlobalDiscoveryAndCertificateManagement2022`) | *GDS Application Directory*, *GDS Certificate Manager Pull Model*, *GDS Query Applications*, *GDS LDS-ME Connectivity* (M) | ✅ full GDS server |

### Automated verification

The automated conformance checks that run in CI are the NUnit suites: the unit suites in `Opc.Ua.Server.Tests` (address-space exposure, transaction coordinator, TrustList staging, certificate alarms, optional surface) and the end-to-end GDS integration suites in `Opc.Ua.Gds.Tests` — notably `PushTest.cs`, which drives a live push server through the `ServerConfiguration` Methods, and `TrustListValidationTest.cs`. The applicable Facets / conformance units above were identified with the OPC UA profile-graph tooling against `profiles.opcfoundation.org`.

There is no fully-automated CTT harness in this repository, and the formal **OPC UA Compliance Test Tool (UACTT / CTT)** — a licensed OPC Foundation corporate-member GUI application — was **not** run to produce this matrix; no CTT results are claimed here. Run it against the reference server (`Applications/ConsoleReferenceServer`; see [Applications/README.md](../Applications/README.md)) for formal certification. Known UACTT *script* defects encountered in prior manual runs are catalogued in [`plans/ctt-issues.md`](../plans/ctt-issues.md).

## GDS Directory (§6.5)

| Section | Feature | Status | Source | Tests |
|---------|---------|--------|--------|-------|
| §6.5.3 | DirectoryType | ✅ | `ApplicationsNodeManager.cs` | `ClientTest.cs`, `GdsApplicationDirectoryTests.cs` |
| §6.5.4 | FindApplications | ✅ | `OnFindApplications` | `GdsApplicationDirectoryTests.cs` |
| §6.5.5 | ApplicationRecordDataType / rcp+ rules | ✅ | `ApplicationsDatabaseBase.ValidateApplication` | `ApplicationsDatabaseBaseTests.cs`, `LinqApplicationsDatabaseTests.cs` |
| §6.5.6 | RegisterApplication | ✅ | `OnRegisterApplication` + `DiscoveryAdminOrAppAdmin` | `GdsApplicationDirectoryTests.cs`, `RegisteredApplicationTests.cs` |
| §6.5.7 | UpdateApplication | ✅ | `OnUpdateApplication` + `DiscoveryAdminOrSelfAdminOrAppAdmin` | `GdsApplicationDirectoryTests.cs` |
| §6.5.8 | UnregisterApplication | ✅ | `OnUnregisterApplicationAsync` + cert revocation | `GdsApplicationDirectoryTests.cs`, `ClientTest.cs` |
| §6.5.9 | GetApplication | ✅ | `OnGetApplication` | `GdsApplicationDirectoryTests.cs` |
| §6.5.10 | QueryApplications | ✅ | `OnQueryApplications` | `GdsQueryApplicationsTests.cs` |
| §6.5.11 | QueryServers | ✅ | `OnQueryServers` | `GdsQueryApplicationsTests.cs`, `ClientTest.cs` |
| §6.5.12 | ApplicationRegistrationChangedAuditEvent | ✅ | `AuditEvents.ReportApplicationRegistrationChangedAuditEvent` | `AuditEventsTests.cs` |

## Certificate Management — Pull Model (§7.9)

Pull Certificate Management moved from §7.6 (1.04) to **§7.9** (v1.05.07); the `CertificateDirectoryType` Methods below use the current clause numbers.

| Section | Feature | Status | Source | Tests |
|---------|---------|--------|--------|-------|
| §7.9.3 | StartSigningRequest | ✅ | `OnStartSigningRequestAsync` + audit | `GdsCertificateManagementTests.cs`, `ClientTest.cs` |
| §7.9.4 | StartNewKeyPairRequest | ✅ | `OnStartNewKeyPairRequest` + audit | `GdsCertificateManagementTests.cs`, `ClientTest.cs` |
| §7.9.5 | FinishRequest | ✅ | `OnFinishRequestAsync` + issuer chain | `GdsCertificateManagementTests.cs`, `ClientTest.cs` |
| §7.9.6 | RevokeCertificate | ✅ | `OnRevokeCertificateAsync` + audit | `GdsCertificateManagementTests.cs`, `CertificateGroupTests.cs` |
| §7.9.7 | GetCertificateGroups | ✅ | `OnGetCertificateGroups` | `ClientTest.cs`, `CustomCertificateGroupIntegrationTest.cs` |
| §7.9.8 | GetCertificates | ✅ | `OnGetCertificates` | `GdsCertificateManagementTests.cs` |
| §7.9.9 | GetTrustList | ✅ | `OnGetTrustList` | `ClientTest.cs`, `CustomCertificateGroupIntegrationTest.cs` |
| §7.9.10 | GetCertificateStatus | ✅ | `OnGetCertificateStatus` | `ClientTest.cs` |
| §7.9.11 | CheckRevocationStatus | ✅ | `OnCheckRevocationStatusAsync` + ValidityTime | `GdsCertificateManagementTests.cs` |

## Roles and Privileges (§7.2)

| Feature | Status | Source | Tests |
|---------|--------|--------|-------|
| DiscoveryAdmin | ✅ | `GdsRole.DiscoveryAdmin` | `AuthorizationHelperTests.HasAuthorizationSucceedsWhenUserHasDiscoveryAdminRole` |
| CertificateAuthorityAdmin | ✅ | `GdsRole.CertificateAuthorityAdmin` | `AuthorizationHelperTests.HasAuthorizationSucceedsWithCertificateAuthorityAdminRole` |
| RegistrationAuthorityAdmin | ✅ | `GdsRole.RegistrationAuthorityAdmin` | `AuthorizationHelperTests.cs` |
| ApplicationSelfAdmin | ✅ | `GdsRole.ApplicationSelfAdmin` | `AuthorizationHelperTests.HasAuthorizationSucceedsWithSelfAdminForOwnApplication`, `...ThrowsWithSelfAdminForDifferentApplication` |
| ApplicationAdmin | ✅ | `GdsRole.ApplicationAdmin` + `AdministeredApplicationIds` | `AuthorizationHelperTests.cs`, `GdsApplicationSelfAdminProviderTests.cs` |
| BadSecurityModeInsufficient (channel) | ✅ | `AuthorizationHelper.HasAuthenticatedSecureChannel` | `AuthorizationHelperTests.cs` (asserts `BadSecurityModeInsufficient`), `RoleAuthorizationGateTests.cs` |

## Push Management — ServerConfiguration (§7.10)

`ConfigurationNodeManager` implements the full push transaction model: every certificate/TrustList Method **stages** work that only takes effect at `ApplyChanges`. Clause numbers are corrected to v1.05.07 (the 1.04 numbering was §7.7.x).

| Section | Feature | Status | Source | Tests |
|---------|---------|--------|--------|-------|
| §7.10.5 | UpdateCertificate | ✅ | `ConfigurationNodeManager.UpdateCertificateAsync` (staged; ApplicationCertificate TrustList validation; supplied issuers ignored for app groups) | `ConfigurationNodeManagerPushTests` (`UpdateCertificateWith*`, `...IgnoresSuppliedIssuerCertificatesAsync`), `PushTest` (`UpdateCertificate*`) |
| §7.10.6 | CreateSelfSignedCertificate | ✅ | `...CreateSelfSignedCertificateAsync` (occupied slot → `Bad_InvalidState`; subject/SAN/key-size validation) | `ConfigurationNodeManagerPushValidationTests` (`CreateDefaultApplicationCertificate*`, `ValidateKeySize*`), `ConfigurationNodeManagerPushTests` (`CreateSelfSignedCertificate*`) |
| §7.10.7 | DeleteCertificate | ✅ | `...DeleteCertificateAsync` — empty slot → `Bad_InvalidState`; endpoint-reference check enforced at `ApplyChanges` | `ConfigurationNodeManagerPushTests` (`DeleteCertificate*`, `DeleteCertificateReferencedByEndpointIsRejectedByApplyChangesAsync`, `RotatedCertificateBecomesEndpointReferenced*`) |
| §7.10.8 | GetCertificates | ✅ | `...GetCertificates` (occupied slots only, aligned pairs) | `ConfigurationNodeManagerPushValidationTests.SelectOccupiedCertificateSlotsOmitsEmptySlotsAndPreservesOrder`, `ConfigurationNodeManagerPushTests.GetCertificates*`, `PushTest.GetCertificatesAsync` |
| §7.10.9 | ApplyChanges | ✅ | `...ApplyChanges` (ordered commit + reverse rollback) | `PushConfigurationTransactionCoordinatorTests` (`ApplyChanges*`), `PushTest` (`ApplyChangesAsync`, `ApplyChangesForcesChannelRenegotiateAsync`) |
| §7.10.10 | CreateSigningRequest | ✅ | `...CreateSigningRequestAsync` (nonce ≥ 32 bytes genuinely mixed in) | `ConfigurationNodeManagerPushTests` (`CreateSigningRequest*`, `...WithRegenerateAndShortNonceThrowsBadInvalidArgument`), `PushTest.CreateSigningRequest*` |
| §7.10.11 | CancelChanges | ✅ | `...CancelChanges` (discard; `Bad_RequestCancelledByClient` diagnostics) | `PushConfigurationTransactionCoordinatorTests.CancelChanges*`, `ConfigurationNodeManagerPushTests.CancelChanges*` |
| §7.10.12 | GetRejectedList | ✅ | `...GetRejectedList` | `ConfigurationNodeManagerPushTests.GetRejectedList*`, `PushTest.GetRejectedListAsync` |

Per-Method SecureChannel/Role enforcement (`Bad_SecurityModeInsufficient` for the channel vs `Bad_UserAccessDenied` for the Role), the §7.10.10 `Nonce` entropy requirement, and the §7.10.7 endpoint-reference rule are described in [CertificateManager.md → PushManagement Transactions → *Method security and validation*](CertificateManager.md#pushmanagement-transactions-opc-ua-part-12-7102-71011).

### Transaction model, diagnostics and lifecycle (§7.10.2, §7.10.9, §7.10.11, §7.10.17)

| Requirement | Status | Source | Tests |
|-------------|--------|--------|-------|
| §7.10.2 transaction lifecycle: single owning Session; other Sessions get `Bad_TransactionPending`; ownership cleared atomically on apply | ✅ | `PushConfigurationTransactionCoordinator` | `PushConfigurationTransactionCoordinatorTests` (`StageStartsTransactionOwnedByStagingSession`, `StageFromAnotherSessionThrowsBadTransactionPending`, `ApplyChangesClearsOwnershipAtomicallySoANewSessionCanImmediatelyStageAsync`) |
| §7.10.2 `SupportsTransactions = true` exposed | ✅ | `ConfigurationNodeManager.CreateServerConfiguration` | `ConfigurationNodeManagerPushTests.SupportsTransactionsIsExposedAndTrue` |
| Staging blocked during an in-flight commit (`Bad_InvalidState` same-owner / `Bad_TransactionPending` other Session) | ✅ | coordinator commit guard | `PushConfigurationTransactionCoordinatorTests` (`StageFromOwningSessionDuringInFlightCommitReturnsBadInvalidStateThenSucceedsAsync`, `StagingFromAnotherSessionDuringAnInFlightCommit...`) |
| §7.10.9 ordered commit, reverse-order rollback, `Bad_NothingToDo`, open-writer `Bad_InvalidState` | ✅ | coordinator prepare/commit/compensate | `PushConfigurationTransactionCoordinatorTests` (`ApplyChangesCommitsOperationsInRequestOrderAsync`, `ApplyChangesWithFailingCommitRollsBackEarlierOperationsInReverseOrderAsync`, `ApplyChangesWithNoActiveTransactionReturnsBadNothingToDoAsync`, `ApplyChangesWithOpenTrustListWriterReturnsBadInvalidStateWithoutClearingTransactionAsync`) |
| §7.10.11 wrong-Session apply/cancel → `Bad_SessionIdInvalid`; Session-close cancels its transaction | ✅ | coordinator ownership checks | `PushConfigurationTransactionCoordinatorTests` (`ApplyChangesFromWrongSessionReturnsBadSessionIdInvalidAsync`, `CancelChangesFromWrongSessionReturnsBadSessionIdInvalid`, `CancelForSessionClose*`) |
| §7.10.17 `TransactionDiagnostics` DataValue status: `Bad_OutOfService` before any transaction, `Bad_InvalidState` while active, `Good` after completion | ✅ | `ConfigurationNodeManager` diagnostics binding + coordinator snapshot | `ConfigurationNodeManagerPushTests` (`TransactionDiagnosticsReportBadOutOfServiceBeforeAnyTransactionAsync`, `...BadInvalidStateWhileTransactionActiveAsync`, `...GoodResultAfterCompletedTransactionAsync`), `PushConfigurationTransactionCoordinatorTests.GetSnapshot*` |
| §7.10.10 nonce entropy genuinely mixed into regenerated key (HMAC-DRBG) | ✅ | `AdditionalEntropyCertificateKeyGenerator` | `AdditionalEntropyCertificateKeyGeneratorTests.cs` |
| §7.10.10 regenerated private key persisted across Sessions and restart | ✅ | `DirectoryPendingCertificateKeyStore` / `InMemoryPendingCertificateKeyStore` | `PendingCertificateKeyStoreTests.cs` |
| Coordinator scoped per server (no cross-server sharing); injected coordinator honored | ✅ | `ConfigurationNodeManager` private per-server default | `ConfigurationNodeManagerPushTests` (`EachConfigurationNodeManagerOwnsADistinctDefaultCoordinator`, `AnInjectedCoordinatorIsSharedAcrossConfigurationNodeManagers`) |
| Deferred post-apply effects drained/cancelled on shutdown | ✅ | `ConfigurationNodeManager.DeleteAddressSpaceAsync` / `Dispose` | `ConfigurationNodeManagerShutdownTests.cs` |

### Optional ServerConfiguration surface (§7.10.13, §7.10.14, §7.10.20)

These optional members are exposed **only when their backing option/provider is configured** (see [CertificateManager.md → Optional ServerConfiguration Surface](CertificateManager.md#optional-serverconfiguration-surface-opc-ua-part-12-7103-71013-71020)); otherwise the node is suppressed.

| Member (§) | Status | Source | Tests |
|------------|--------|--------|-------|
| Identity variables `ApplicationUri` / `ProductUri` / `ApplicationType` / `ApplicationNames` (§7.10.3) | ✅ always | `ServerConfigurationOptions` from `ApplicationConfiguration` | `ServerConfigurationSurfaceTests.IdentityPropertiesAlwaysExposedAndValuedAsync` |
| `HasSecureElement` / `InApplicationSetup` (§7.10.3) | 🔲 provider-gated | `ServerConfigurationOptions` | `ServerConfigurationSurfaceTests` (`HasSecureElementExposedWithValueWhenConfiguredAsync`, `InApplicationSetupExposedWithValueWhenConfiguredAsync`, `OptionalMembersSuppressedWhenNotConfiguredAsync`) |
| `ResetToServerDefaults` (§7.10.13) | 🔲 provider-gated | `IServerConfigurationResetProvider` | `ServerConfigurationSurfaceTests.ResetToServerDefaultsExposedOnlyWhenProviderConfiguredAsync`, `ServerConfigurationResetTests.cs` |
| `ConfigurationFile` / `ApplicationConfigurationFileType` incl. `CloseAndUpdate` / `ConfirmUpdate` (§7.10.20) | 🔲 provider-gated | `IApplicationConfigurationFileProvider` + `ApplicationConfigurationFile.cs` | `ServerConfigurationSurfaceTests` (`ConfigurationFileExposedWithChildrenWhenProviderConfiguredAsync`, `ConfigurationFilePropertiesSeededFromProviderAsync`, `ConfigurationFileReadReturnsProviderContentAsync`) |

### GDS-managed applications proxy (§7.10.14–§7.10.16)

The GDS-side proxy that exposes *other* applications' configurations under a `ManagedApplications` folder is a separate, partially-implemented feature (out of scope for a self-managed push Server).

| Section | Feature | Status | Source |
|---------|---------|--------|--------|
| §7.10.14 | `ApplicationConfigurationType` | ⚠️ | `DefaultManagedApplicationsNodeManager` creates `ApplicationConfigurationState` nodes; `IConfigurationDataStore` abstracts persistence |
| §7.10.16 | `ManagedApplications` folder | ⚠️ | `DefaultManagedApplicationsNodeManager`; optimistic concurrency via version tracking |

## TrustList (§7.8)

| Section | Feature | Status | Source | Tests |
|---------|---------|--------|--------|-------|
| §7.8.2.2 | TrustListType / Open / OpenWithMasks (Read + Write+EraseExisting only) | ✅ | Core `TrustList.cs` | `TrustListTests.OpenWithMasksSyncAllReturnsGood` |
| §7.8.2.5 | CloseAndUpdate — staged, applied at `ApplyChanges` | ✅ | `TrustList.cs` + coordinator; `TrustListUpdatedAuditEvent` post-commit | `TrustListTransactionTests` (`CloseAndUpdateStagesInsteadOfApplyingImmediatelyAsync`, `...RollsBackWhenALaterOperationFailsAsync`, `...SelfCompensatesWhenOnlyOneOfSeveralStoresFailsAsync`), `PushTest.UpdateTrustListAsync` |
| §7.8.2.6/.7 | AddCertificate / RemoveCertificate — staged | ✅ | `TrustList.cs` | `PushTest` (`AddRemoveCertAsync`, `AddRemoveCATrustedCertAsync`, `AddRemoveCAIssuerCertAsync`), `TrustListTransactionTests.cs` |
| §7.8.2 | Open-for-write blocked with `Bad_TransactionPending` while another Session's transaction is active | ✅ | `TrustList.cs` + coordinator | `TrustListTransactionTests.cs`, `PushConfigurationTransactionCoordinatorTests.ApplyChangesWithOpenTrustListWriter*` |
| §7.8.2 | Read/write access control | ✅ | `TrustList.cs` | `TrustListTests` (`OpenReadWithoutReadAccessThrowsBadUserAccessDenied`, `OpenWriteWithoutWriteAccessThrowsBadUserAccessDenied`) |
| §7.8.2 | `LastUpdateTime` set on init + after committed update | ✅ | `TrustList.cs` | `TrustListTransactionTests.cs` |
| §7.8.2 | Writable / UserWritable; ActivityTimeout / DefaultValidationOptions | ✅ | Set to true for GDS groups; generated from model CSV | `TrustListTests.cs` |
| §8.4.5 | `MaxTrustListSize` advertised honestly + resource-protection safety ceiling; oversize → `Bad_EncodingLimitsExceeded` | ✅ | `TrustList.cs` + `ServerConfigurationOptions.MaxTrustListSizeSafetyCeiling` | `TrustListValidationTest` (`NormalSizeTrustListAsync`, `WriteTrustListExceedsSizeLimit`, `TrustListJustUnderLimitAsync`), `ConfigurationNodeManagerPushTests.MaxTrustListSizeAdvertisesHonestFiniteEffectiveLimit` |

### Certificate and TrustList alarms (§7.8.3)

Each `CertificateGroup` exposes the two optional standard alarm instances with **full active/inactive transitions and events** (previously property-only). `ConfigurationNodeManager` creates and wires them in `CreateAddressSpace` and `StandardServer.OnServerStarted` starts periodic monitoring (60&nbsp;s) once the subscription infrastructure is ready; all thresholds and timers flow through the injected `TimeProvider`. See [CertificateManager.md → Certificate-Expiration and TrustList-Staleness Alarms](CertificateManager.md#certificate-expiration-and-trustlist-staleness-alarms-opc-ua-part-12-783).

| Alarm | Status | Source | Tests |
|-------|--------|--------|-------|
| `CertificateExpirationAlarmType` (`CertificateExpired`) — active within `ExpirationLimit` / expired, Medium/High severity, `Retain`, Acknowledge, cleared after certificate replacement | ✅ | `CertificateGroupAlarmMonitor` + `ConfigurationNodeManager.EvaluateCertificateAlarms` / `StartAlarmMonitoring` | `CertificateAlarmMonitoringTests` (`CertificateExpiredActivatesWithMediumSeverity*`, `...HighSeverityWhenAlreadyExpired`, `...DeactivatesAfterCertificateReplacement`, `RepeatedEvaluationsDoNotEmitDuplicateTransitionEvents`, `AcknowledgedAlarmClearsRetainAfterDeactivation`, `ClientAcknowledgeMethodMarksAlarmAcknowledged`) |
| `TrustListOutOfDateAlarmType` (`TrustListOutOfDate`) — active when `UpdateFrequency` elapses, disabled when non-positive | ✅ | same | `CertificateAlarmMonitoringTests` (`TrustListOutOfDateActivatesWhenStale`, `TrustListOutOfDateStaysInactiveWhenFresh`, `TrustListOutOfDateDisabledWhenUpdateFrequencyIsZero`) |

### TrustList-change effects on channels and Sessions (§7.10.9)

| Requirement | Status | Source | Tests |
|-------------|--------|--------|-------|
| Committed TrustList change forces SecureChannel renegotiation (application group) and revalidates certificate user identities / closes invalidated Sessions (user-token group) | ✅ | `PushConfigurationTrustListEffectHandler` + `ConfigurationNodeManager` | `ConfigurationNodeManagerTrustListEffectsTests` (`BuildTrustListEffects*`, `ApplyChangesDispatches*EffectFor*`, `ApplyChangesUsesCommittedTargetsNotAConcurrentSessionsStagedTargetsAsync`), `PushConfigurationTrustListEffectHandlerTests.cs` |

## Audit Events

| Event Type | Status | Source | Tests |
|------------|--------|--------|-------|
| ApplicationRegistrationChangedAuditEvent | ✅ | Register/Update/Unregister | `AuditEventsTests.cs` |
| CertificateRequestedAuditEvent | ✅ | StartNewKeyPair + StartSigning | `AuditEventsTests.cs` |
| CertificateDeliveredAuditEvent | ✅ | FinishRequest | `AuditEventsTests.cs` |
| CertificateRevokedAuditEvent | ✅ | RevokeCertificate | `AuditEventsTests.cs` |
| CertificateUpdateRequestedAuditEvent | ✅ | UpdateCertificate (push) | `AuditEventsTests.cs`, `PushTest.cs` |
| TrustListUpdatedAuditEvent | ✅ | Core TrustList.cs | `TrustListTransactionTests.cs`, `PushTest.cs` |
| KeyCredentialRequestedAuditEvent | ✅ | StartRequest | `AuditEventsTests.cs` |
| KeyCredentialDeliveredAuditEvent | ✅ | FinishRequest | `AuditEventsTests.cs` |
| KeyCredentialRevokedAuditEvent | ✅ | Revoke | `AuditEventsTests.cs` |
| AccessTokenIssuedAuditEvent | ✅ | RequestAccessToken | `AuthorizationService/RefreshTokenTests.cs` |
| Secrets redacted from audit payloads | ✅ | RedactedPrivateKeyPassword / RedactedPrivateKey | `AuditEventsTests.cs` |

## KeyCredentialService (§8)

| Feature | Status | Source | Tests |
|---------|--------|--------|-------|
| StartRequest | ✅ | `OnKeyCredentialStartRequest` | `KeyCredentialRequestStoreTests.StartRequestReturnsNonNullIdAsync` |
| FinishRequest | ✅ | `OnKeyCredentialFinishRequest` | `KeyCredentialRequestStoreTests` (`FinishRequestReturnsCredentialAsync`, `FinishRequestWithCancelRejectsRequestAsync`, `FinishRequestWithUnknownIdThrows`) |
| Revoke | ✅ | `OnKeyCredentialRevoke` | `KeyCredentialRequestStoreTests` (`RevokeMarksCredentialAsRejectedAsync`, `RevokeUnknownCredentialThrows`) |
| InMemoryKeyCredentialRequestStore | ✅ | `IKeyCredentialRequestStore.cs` | `KeyCredentialRequestStoreTests.cs` |
| Client proxy | ✅ | `KeyCredentialServiceClient.cs` | `KeyCredentialRequestStoreTests.cs` |
| KeyCredential Push binding | ✅ | `WithKeyCredentialPush` / `KeyCredentialPushSubject` | `ConfigurationNodeManagerPushTests` (`BindKeyCredentialPushWithExistingFolderBindsSubjectAsync`, `BindKeyCredentialPushWithNullSubjectThrowsArgumentNullException`) |

## AuthorizationService (§9)

| Feature | Status | Source | Tests |
|---------|--------|--------|-------|
| GetServiceDescription | ✅ | `OnGetServiceDescription` | `AuthorizationService/RefreshTokenTests.GetServiceDescriptionReportsConfiguredServiceUri` |
| RequestAccessToken (obsolete single-call) | ✅ | Legacy compatibility path via `IAccessTokenProvider.RequestAccessTokenAsync` | `AuthorizationService/StartRequestTokenTests.LegacyRequestAccessTokenIssuesAnonymousUnprivilegedToken` |
| StartRequestToken (RC) | ✅ | `OnStartRequestToken` / `AuthorizationServiceManager` | `AuthorizationService/StartRequestTokenTests.cs` |
| FinishRequestToken (RC) | ✅ | `OnFinishRequestToken` / `AuthorizationServiceManager` | `AuthorizationService/StartRequestTokenTests.cs` |
| RefreshToken (RC) | ✅ | `OnRefreshTokenAsync` / `AuthorizationServiceManager` | `AuthorizationService/RefreshTokenTests.cs`, `AuthorizationService/InMemoryAccessTokenProviderRefreshTests.cs` |
| SupportedRoles (RC) | ✅ | Model property exposed | — (model property; not separately unit-tested) |
| Client proxy | ✅ | `AuthorizationServiceClient.cs` | `AuthorizationService/StartRequestTokenTests.cs`, `AuthorizationService/RefreshTokenTests.cs` |

### Refresh tokens

The default in-memory AuthorizationService provider issues a refresh token from
`FinishRequestToken` and accepts it through `RefreshToken` (OPC 10000-12 §9.7). Refresh
tokens are opaque, single-use secrets: each successful refresh consumes the current token,
issues a new access token, and rotates to a new refresh token. The refresh-token lifetime
slides by `AuthorizationServiceOptions.DefaultRefreshTokenLifetime` (7 days by default).
Set `AuthorizationServiceOptions.EnableRefreshTokens = false` to preserve the legacy
behavior where `FinishRequestToken` returns no refresh token and `RefreshToken` returns
`Bad_NotSupported`.

```csharp
var (newJwt, newJwtExpiresAt, newRefreshToken, newRefreshExpiresAt) =
    await authClient.RefreshTokenAsync(resourceId, refreshToken);

refreshToken = newRefreshToken;
```

Refresh-token audit events include only the `resourceId` input and the outcome. The
refresh token itself is a secret and is never placed in audit payloads.

## Remaining optional / unsupported Part 12 items

The following optional Part 12 members are **not** implemented (or only partially). None are required for the mandatory conformance units of the applicable Facets above; they are listed here so the matrix stays honest.

| Item (§) | Status | Notes |
|----------|--------|-------|
| `ServerConfiguration.AuthorizationServices` folder (§9.6.2 / *Authorization Service Configuration Server* CU) | ❌ not exposed | A push Server does not expose the configuration folder listing trusted Authorization Services. Client access-token validation is instead handled by the pluggable [Identity Providers](IdentityProviders.md). |
| `ManagedApplications` / `ApplicationConfigurationType` proxy (§7.10.14–§7.10.16) | ⚠️ partial | `DefaultManagedApplicationsNodeManager` creates the nodes with an `IConfigurationDataStore`; the full GDS-as-proxy update/confirm flow to *remote* managed applications is not complete. |
| KeyCredential authentication `ProfileUris` — AMQP SASL Plain / MQTT UserName / UA transport UserName (§8, optional CUs) | 🔲 provider-defined | The push binding is implemented; the concrete ProfileUri set advertised is defined by the credential provider, not fixed by the stack. |
| A & C CertificateExpiration optional CUs — *Comment* / *Confirm* / *Shelving* | 🔲 inherited | Available through the standard OPC 10000-9 condition model on the alarm instances; not separately exercised by the certificate-alarm tests. |
| Onboarding / OPC 10000-21 device setup interplay with `ResetToServerDefaults` / `InApplicationSetup` (§7.10.13, Annex G) | 🔲 provider-gated | The stack exposes `InApplicationSetup` and defers the reset to `IServerConfigurationResetProvider`; it does not itself implement an OPC 10000-21 onboarding state machine. |

## LDS / LDS-ME (§4–5)

| Feature | Status | Source | Tests |
|---------|--------|--------|-------|
| FindServers | ✅ | `LdsServer.FindServersAsync` | `LocalDiscoveryTests.cs` |
| GetEndpoints | ✅ | `LdsServer.GetEndpointsAsync` | `LocalDiscoveryTests.cs` |
| RegisterServer | ✅ | `LdsServer.RegisterServerAsync` | `LocalDiscoveryTests.cs` |
| RegisterServer2 | ✅ | `LdsServer.RegisterServer2Async` | `LocalDiscoveryTests.cs` |
| FindServersOnNetwork | ✅ | `LdsServer.FindServersOnNetworkAsync` | `LocalDiscoveryTests.cs`, `LdsServerStaticTests.cs` |
| LDS-ME capability | ✅ | `ComputeServerCapabilities` | `ServerCapabilitiesTests.cs`, `ServerCapabilityTests.cs` |
| mDNS _opcua-tcp._tcp | ✅ | `MulticastDiscovery.OpcUaServiceType` | `LdsServerStaticTests.cs` |
| rcp+ reverse-connect | ✅ | `MulticastDiscovery.ReverseConnectScheme` | `LdsServerStaticTests.cs` |
| Annex C TXT keys | ✅ | path / caps / rc | `LdsServerStaticTests.cs` |
| Annex D identifiers | ✅ | LDS / LDS-ME | `LdsServerStaticTests.cs` |

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
