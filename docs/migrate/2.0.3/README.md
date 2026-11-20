# Migrating to 2.0.3

Version **2.0.3** deprecates APIs that a later major version will remove.
Code that uses them keeps compiling and running, but you see `[Obsolete]`
warnings that name the replacement. If your build treats warnings as errors,
either migrate or suppress the specific diagnostic until you can.

One source-incompatible change comes with it: `PubSubNodeManagerFactory`,
`WotConnectivityNodeManagerFactory` and the Quickstarts `BoilerNodeManagerFactory`
are now asynchronous factories only. Code that called their `Create(...)` method
or typed them as `INodeManagerFactory` no longer compiles; call `CreateAsync(...)`
or use them as `IAsyncNodeManagerFactory`. Registration through the builder
extensions is unaffected. See
[Changed factories](node-managers.md#changed-factories).

## Contents

- [Migrating to 2.0.3](#migrating-to-203)
  - [Migration sub-doc index](#migration-sub-doc-index)
  - [All sub-documents](#all-sub-documents)
  - [See also](#see-also)

## Migration sub-doc index

| When you hit… | Read |
| --- | --- |
| `UA0031` (or `CS0618` on .NET Framework) on `CustomNodeManager2`, `INodeManager`, `INodeManager2`, `INodeManager3`, `INodeManagerFactory`, `AsyncNodeManagerAdapter`, `AddSyncNodeManager`, `IStandardServer.AddNodeManager(INodeManagerFactory)`, `IMasterNodeManager.NodeManagers` / `RegisterNamespaceManager(string, INodeManager)` | [`node-managers.md`](node-managers.md) |

## All sub-documents

- [`node-managers.md`](node-managers.md) — The synchronous node manager API is deprecated in favour of the asynchronous seam (issue #4735)

## See also

- [Migrating from 1.5.378 to 2.0.x](../2.0.x/README.md)
- [Migration Guide landing page](../../MigrationGuide.md)
- [Node Managers](../../NodeManagers.md)
- [Support of the TAP for server operations](../../AsyncServerSupport.md)
