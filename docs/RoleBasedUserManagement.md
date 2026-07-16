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

See `src/Opc.Ua.Server/RoleBasedUserManagement/` for the source.

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

> **Identity-claim criteria support**:
>
> - `IdentityCriteriaType.GroupId` probes the returned `IUserIdentity` for `Opc.Ua.Identity.IIdentityClaims` (see [Identity Providers](IdentityProviders.md)) and matches `IIdentityClaims.Groups`.
> - `IdentityCriteriaType.Role` matches roles asserted **inside the access token** via `IIdentityClaims.Roles`, optionally prefixed by the issuer URI as `iss/roleName`, per Part 18 4.4.4.


### Authoring an identity-mapping rule with claims

JWT authenticators that return an identity implementing `IIdentityClaims`
allow Part 18 rules to match Entra-style claims. The in-box
`RoleManager` treats entries in a role's `Identities` collection as OR
criteria, per the standard `IdentityMappingRuleType[]` shape. Add one
entry for each accepted claim value:

```csharp
IRoleManager roles = server.CurrentInstance.RoleManager;

roles.AddIdentity(
    ObjectIds.WellKnownRole_Engineer,
    new IdentityMappingRuleType
    {
        CriteriaType = IdentityCriteriaType.Role,
        Criteria = "https://login.microsoftonline.com/{tenant}/v2.0/OpcUa.Engineer"
    });

roles.AddIdentity(
    ObjectIds.WellKnownRole_Engineer,
    new IdentityMappingRuleType
    {
        CriteriaType = IdentityCriteriaType.GroupId,
        Criteria = "00000000-1111-2222-3333-444444444444"
    });
```

The first rule matches an access-token role claim `OpcUa.Engineer` only
when `IIdentityClaims.Issuer` is the Entra issuer. The second matches a
`groups` claim containing the Entra group object id. If your policy must
require both a role and a group simultaneously, implement a custom
`IRoleManager` (or a custom authorization layer) because the shipped
`RoleConfigurationOptions` DTO does not define an AND-rule collection.

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

Additional identity-mapping rules are evaluated by `IRoleManager.ResolveGrantedRoles` after authentication. New integrations should expose claims through `IIdentityClaims` and add `IdentityMappingRuleType` entries to the role manager.

### User Management (Part 18 §5)

The Part 18 §5 `UserManagementType` is bound to the standard
`Server.ServerConfiguration.UserManagement` object (NodeId `i=24290`).
Integrators inject an `IUserManagement` instance via
`IServerInternal.SetUserManagement` before the configuration node manager
binds the address space; the default `UserManagement` implementation wraps
an existing `IUserDatabase` for credential persistence and stores the
per-user `UserConfigurationMask` and description in memory:

```csharp
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;

var users = new LinqUserDatabase();
users.CreateUser("alice", "secret"u8, [Role.AuthenticatedUser]);

var userManagement = new UserManagement(
    users,
    passwordLength: new Range { Low = 8, High = 256 },
    passwordOptions:
        PasswordOptionsMask.SupportDisableUser
        | PasswordOptionsMask.SupportInitialPasswordChange
        | PasswordOptionsMask.RequiresDigitCharacters);

serverInternal.SetUserManagement(userManagement);
```

`UserManagementBinding.Bind` (called automatically by
`ConfigurationNodeManager.CreateServerConfiguration` when an
`IUserManagement` is injected) wires the typed `AddUser`, `ModifyUser`,
`RemoveUser` and `ChangePassword` method-state proxies, enforces
`RoleAuthorizationGate.CheckAdmin` on the admin methods (SecurityAdmin +
SignAndEncrypt) and `RoleAuthorizationGate.CheckSelfUserName` on
`ChangePassword`, and closes any active sessions for a deactivated user
via the supplied `ISessionManager`.

Spec result codes honoured:

| Operation | Failure codes |
|-----------|---------------|
| `AddUser` (§5.2.5) | `Bad_AlreadyExists`, `Bad_OutOfRange`, `Bad_NotSupported`, `Bad_ConfigurationError`, `Bad_UserAccessDenied`, `Bad_SecurityModeInsufficient`, `Bad_ResourceUnavailable` |
| `ModifyUser` (§5.2.6) | `Bad_NotFound`, `Bad_OutOfRange`, `Bad_NotSupported`, `Bad_ConfigurationError`, `Bad_UserAccessDenied`, `Bad_SecurityModeInsufficient`, `Bad_InvalidSelfReference` |
| `RemoveUser` (§5.2.7) | `Bad_NotFound`, `Bad_UserAccessDenied`, `Bad_NotSupported` (NoDelete), `Bad_SecurityModeInsufficient`, `Bad_InvalidSelfReference` |
| `ChangePassword` (§5.2.8) | `Bad_IdentityTokenInvalid`, `Bad_OutOfRange`, `Bad_InvalidState`, `Bad_NotSupported` (NoChangeByUser), `Bad_SecurityModeInsufficient`, `Bad_AlreadyExists` (new == old) |

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

See [GdsRole.cs](https://github.com/OPCFoundation/UA-.NETStandard/blob/main/src/Opc.Ua.Gds.Server.Common/RoleBasedUserManagement/GdsRole.cs).

## Known limitations

- **Integration tests for end-to-end RoleSet / UserManagement scenarios** (e.g. AddRole over the wire followed by browse + AddIdentity + activated session under the new grant) are tracked as a follow-up.

## References

- [OPC UA Part 18 v1.05.06 - Role-Based Security](https://reference.opcfoundation.org/Core/Part18/v105/docs/)
- [OPC UA Part 3 4.9 - Well-Known Roles](https://reference.opcfoundation.org/Core/Part3/v105/docs/4.9)
- `src/Opc.Ua.Server/RoleBasedUserManagement/`
- `src/Opc.Ua.Client/Roles/`
- `tests/Opc.Ua.Server.Tests/RoleManagerTests.cs`
