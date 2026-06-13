# WoT Connectivity (OPC 10100-1)

> **When to read this:** Read this for behaviour-breaking changes to the
> WoT Connectivity server's management methods, in particular the new
> default access policy that rejects anonymous / unsecured callers on
> the `WoTAssetConnectionManagement` object.

## Security tightening — WoT Connectivity management methods

**Behaviour-breaking, not source-breaking.** The five management methods
on the standard `WoTAssetConnectionManagement` object (`CreateAsset`,
`DeleteAsset`, `DiscoverAssets`, `CreateAssetForEndpoint`,
`ConnectionTest`) now reject anonymous and `None` / `Sign`-only callers
by default. The new `WotConnectivityServerOptions.ManagementAccess`
(`WotManagementAccessPolicy`) defaults to:

- `MinimumSecurityMode = MessageSecurityMode.SignAndEncrypt`
- `AllowAnonymous = false`
- `RequiredRoleId = ObjectIds.WellKnownRole_SecurityAdmin`

Existing deployments that relied on anonymous management over `None`
channels must either configure their clients to use `SignAndEncrypt`
and present a `SecurityAdmin`-roled identity, or explicitly opt-in to
the legacy behaviour:

```csharp
services.AddOpcUa()
    .AddServer(...)
    .AddWotConServer(opts =>
    {
        opts.ManagementAccess = new WotManagementAccessPolicy
        {
            AllowAnonymous = true,
            MinimumSecurityMode = MessageSecurityMode.None,
            RequiredRoleId = ObjectIds.WellKnownRole_Anonymous
        };
    });
```

Internal callers that invoke `AssetRegistry.*Async` directly (startup
restoration of persisted assets, in-process tests) are unaffected — the
enforcement runs only against `OperationContext`-bearing address-space
calls.

---

**See also**

- Related: [identity.md](identity.md), [certificates.md](certificates.md).
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.
- [WoT Connectivity developer guide](../../WoTConnectivity.md) —
  feature documentation for the OPC 10100-1 server / client trio.
