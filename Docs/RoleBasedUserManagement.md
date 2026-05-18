# Role-Based Security (OPC UA Part 18)

## Overview

The OPC UA .NET Standard Stack implements the role-based security model from
[OPC UA Part 18: Role-Based Security (v1.05.06)](https://reference.opcfoundation.org/Core/Part18/v105/docs/)
together with the well-known roles defined in
[OPC UA Part 3 4.9.2](https://reference.opcfoundation.org/Core/Part3/v105/docs/4.9.2).

The nine well-known roles are pre-registered at server start:

| Role | NodeId | Reserved? | Default identities |
|------|--------|-----------|--------------------|
| `Anonymous` | `i=15644` | Yes | `Anonymous`, `AuthenticatedUser` |
| `AuthenticatedUser` | `i=15656` | Yes | `AuthenticatedUser` |
| `TrustedApplication` | `i=18625` | Yes | `TrustedApplication` |
| `Observer` | `i=15668` | No | (empty) |
| `Operator` | `i=15680` | No | (empty) |
| `Engineer` | `i=16036` | No | (empty) |
| `Supervisor` | `i=15692` | No | (empty) |
| `ConfigureAdmin` | `i=15716` | No | (empty) |
| `SecurityAdmin` | `i=15704` | No | (empty) |

Reserved roles cannot be modified or removed per Part 18 4.3 - any attempt to call `AddIdentity`, `AddApplication`, `AddEndpoint`, `RemoveIdentity`, `RemoveApplication`, `RemoveEndpoint`, or `RemoveRole` on them returns `Bad_RequestNotAllowed`.

See `Libraries/Opc.Ua.Server/RoleBasedUserManagement/` for the source.

## Server side

### IRoleManager

The `IRoleManager` interface (`Opc.Ua.Server.IRoleManager`) is the server-side extensibility surface. The default implementation `Opc.Ua.Server.RoleManager` is fully in-memory; integrators that need persistence implement `IRoleManager` directly and inject the instance via `IServerInternal.SetRoleManager` before the server starts.

Every mutator returns a `ServiceResult` that mirrors the spec-defined status codes:

- `AddIdentity` (4.4.5) - Good or Bad_InvalidArgument / Bad_AlreadyExists / Bad_RequestNotAllowed / Bad_NodeIdUnknown.
- `RemoveIdentity` (4.4.6) - Good or Bad_NotFound / Bad_RequestNotAllowed / Bad_NodeIdUnknown.
- `AddApplication` (4.4.7) - Good or Bad_InvalidArgument / Bad_AlreadyExists / Bad_RequestNotAllowed / Bad_NodeIdUnknown.
- `RemoveApplication` (4.4.8) - Good or Bad_NotFound / Bad_RequestNotAllowed / Bad_NodeIdUnknown.
- `AddEndpoint` (4.4.9) - Good or Bad_InvalidArgument / Bad_AlreadyExists / Bad_RequestNotAllowed / Bad_NodeIdUnknown.
- `RemoveEndpoint` (4.4.10) - Good or Bad_NotFound / Bad_RequestNotAllowed / Bad_NodeIdUnknown.
- `AddRole` (4.2.2) - Good or Bad_InvalidArgument / Bad_AlreadyExists.
- `RemoveRole` (4.2.3) - Good or Bad_NodeIdUnknown / Bad_RequestNotAllowed.
- `SetApplicationsExclude` / `SetEndpointsExclude` / `SetCustomConfiguration` - Good or Bad_RequestNotAllowed / Bad_NodeIdUnknown.

`IdentityMappingRuleType` is validated per Part 18 4.4.3 before storage:

- `Anonymous`, `AuthenticatedUser`, `TrustedApplication` - criteria must be empty.
- `Thumbprint` - upper-case hexadecimal, no spaces.
- `X509Subject` - `Name=Value(/Name=Value)*` grammar with names from Part 18 Table 10.

`EndpointType` comparisons in identity resolution honour Part 18 4.4.2: fields set to their default value on the rule side act as wildcards.

### RoleConfigurationChanged event

`IRoleManager.RoleConfigurationChanged` fires after every successful mutation with the role NodeId and a `RoleConfigurationChangeKind`. Integrators can subscribe to:

- materialize dynamic-role address-space nodes when `RoleAdded` fires (the default in-memory `RoleManager` does not create address-space nodes for `AddRole` - the well-known roles are already in the standard nodeset);
- re-evaluate active sessions per Part 18 4.4.1 ("If the configuration of a Role is changed, the Role assignment to active Session shall be re-evaluated and applied");
- emit audit events beyond the spec-defined `RoleMappingRuleChangedAuditEvent`.

### Typed-proxy address-space binding

`RoleStateBinding.Bind(diagnosticsNodeManager, roleManager, auditServer)` uses the source-generated typed proxies (`RoleSetState`, `RoleState`, `AddIdentityMethodState`, `AddRoleMethodState`, ...). Each typed `OnCallAsync` delegate:

1. Enforces `RoleAuthorizationGate.CheckAdmin` (SecurityAdmin role over a `SignAndEncrypt` channel) - returns `Bad_SecurityModeInsufficient` or `Bad_UserAccessDenied` otherwise (Part 18 4.2 / 4.4).
2. Delegates to `IRoleManager`.
3. Reports a `RoleMappingRuleChangedAuditEvent` (Part 18 4.5).
4. Keeps the typed `Identities`, `Applications`, `Endpoints`, `ApplicationsExclude`, `EndpointsExclude` and `CustomConfiguration` property values in sync with the manager via the `RoleConfigurationChanged` event.

`DiagnosticsNodeManager.AddBehaviourToPredefinedNodeAsync` upgrades passive `BaseObjectState` instances of `RoleSetType` and `RoleType` to the typed `RoleSetState` and `RoleState` proxies at predefined-node load time.

### Default impersonation flow

Per Part 3 4.9 and Part 18 4.3 the default `RoleManager` rules already grant:

- `Anonymous` for sessions without authentication;
- `AuthenticatedUser` for any session with a non-anonymous token;
- `TrustedApplication` when the session was authenticated with a trusted ApplicationInstance certificate over a signed channel.

Integrators returning a `RoleBasedIdentity` from `SessionManager_ImpersonateUser` layer additional roles on top of those defaults.

### User name / password storage

The `IUserDatabase` interface (`Opc.Ua.Server.UserDatabase.IUserDatabase`) remains the extension point for username/password storage. The shipped implementations are:

- `LinqUserDatabase` - in-memory.
- `JsonUserDatabase` - JSON file backed.

External implementations may be provided by integrators (e.g. backed by SQL, LDAP, or a vendor-specific store).

## Client side

`Opc.Ua.Client.Roles.RoleManagementClient` is a strongly-typed async client over the standard `Server.ServerCapabilities.RoleSet` object:

```csharp
using Opc.Ua.Client.Roles;

RoleManagementClient client = session.OpenRoleManagementClient();

// List every role exposed by the server.
IReadOnlyList<RoleInfo> roles = await client.ListRolesAsync();

// Add a UserName identity rule to the Observer role.
await client.AddIdentityAsync(
    Opc.Ua.ObjectIds.WellKnownRole_Observer,
    new IdentityMappingRuleType
    {
        CriteriaType = IdentityCriteriaType.UserName,
        Criteria = "alice"
    });

// Toggle the ApplicationsExclude flag on the Operator role.
await client.SetApplicationsExcludeAsync(
    Opc.Ua.ObjectIds.WellKnownRole_Operator, value: false);

// Add a custom role under a vendor namespace.
NodeId newRoleId = await client.AddRoleAsync(
    "OpsLead",
    namespaceUri: "urn:example:roles");

// Remove a dynamic role.
await client.RemoveRoleAsync(newRoleId);
```

All mutator methods require the calling session to hold the `SecurityAdmin` role and use a `SignAndEncrypt` secure channel; otherwise the server returns `Bad_UserAccessDenied` / `Bad_SecurityModeInsufficient` which surfaces as a `ServiceResultException`.

## GDS

The GDS adds the following roles in addition to the standard well-known set:

- `DiscoveryAdmin`
- `SecurityAdmin`
- `CertificateAuthorityAdmin`
- `RegistrationAuthorityAdmin`
- `ApplicationSelfAdmin`

See [GdsRole.cs](https://github.com/OPCFoundation/UA-.NETStandard/blob/main/Libraries/Opc.Ua.Gds.Server.Common/RoleBasedUserManagement/GdsRole.cs).

## Known limitations

- **AddRole address-space materialization**: the default `RoleManager` registers the role in its identity-resolution table but does not create a `RoleType` instance node for dynamically added roles. Integrators that want the new role to be browseable can subscribe to `IRoleManager.RoleConfigurationChanged` and call `AddNodeAsync` on the diagnostics node manager to create the materialized node.
- **Live session re-evaluation**: per Part 18 4.4.1 the role assignment of active sessions should be re-evaluated when a role configuration changes. Today, re-evaluation happens on the next session activation via `SessionManager.AddMandatoryRoles`. A full live re-evaluation implementation is on the roadmap.
- **UserManagement (Part 18 5)**: the spec-defined `UserManagement` object (`AddUser` / `ModifyUser` / `RemoveUser` / `ChangePassword` on `ServerConfiguration.UserManagement`, i=24290) is not yet bound; user CRUD is currently exposed only via the non-spec `IUserDatabase` extension point.

## References

- [OPC UA Part 18 v1.05.06 - Role-Based Security](https://reference.opcfoundation.org/Core/Part18/v105/docs/)
- [OPC UA Part 3 4.9 - Well-Known Roles](https://reference.opcfoundation.org/Core/Part3/v105/docs/4.9)
- `Libraries/Opc.Ua.Server/RoleBasedUserManagement/`
- `Libraries/Opc.Ua.Client/Roles/`
- `Tests/Opc.Ua.Server.Tests/RoleManagerTests.cs`