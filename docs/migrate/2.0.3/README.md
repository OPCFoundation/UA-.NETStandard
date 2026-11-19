# Migrating to 2.0.3

Version **2.0.3** removes nothing. It deprecates APIs that a later major
version will remove, so upgrading compiles and runs unchanged, but you see
`[Obsolete]` warnings that name the replacement. If your build treats
warnings as errors, either migrate or suppress the specific diagnostic
until you can.

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
