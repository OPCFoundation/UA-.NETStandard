# Missing Nodes Validation Report

**Date:** 2026-04-28  
**Commit:** `ab25d9f0c` (asfixes branch, local)  
**Server:** ConsoleReferenceServer with GDS (`opc.tcp://localhost:48010`)  
**Spec:** `Opc.Ua.NodeSet2.xml` v1.05.07

## Result

| Metric | Value |
|--------|------:|
| Spec ns=0 nodes | 5,283 |
| Server ns=0 nodes (export) | 6,573 |
| Missing from export | 694 |

All nodes exist in the server. **Zero type-level children missing** -- all ObjectType/VariableType hierarchies are fully browseable.

| Category | Count | Status |
|----------|------:|--------|
| Encoding objects | 384 | **Present** -- non-hierarchical; not reachable by browse from Root |
| Access-restricted nodes (spec-defined) | 236 | **Present** -- require SignAndEncrypt to read |
| Other (instance + orphan) | 74 | **Present** -- see below |
| **Total** | **694** | |

## Remaining 74 Nodes

| Structural Category | Count | Status |
|---------------------|------:|--------|
| Instance-level children (diagnostics, HA, PubSub, FileSystem) | 48 | **Present** -- access-restricted or dynamic |
| Orphan property templates (no ParentNodeId) | 26 | **Present** -- no hierarchical parent to browse from |

## Fix Summary

| Category | Count | Status |
|----------|------:|--------|
| Encoding objects | 384 | **Present** (non-hierarchical) |
| Access-restricted (spec-defined) | 236 | **Present** (need encryption) |
| Instance-level (diagnostics, HA, PubSub, FileSystem) | 48 | **Present** (access-restricted or dynamic) |
| Orphan templates | 26 | **Present** (no hierarchical parent) |
| **Total** | **694** | **No type-level issues remaining** |

### Source Generator Fixes Applied

1. **ModellingRule=None children** ([Part 3, 6.3.3](https://reference.opcfoundation.org/v105/Core/docs/Part3/6.3.3/)) -- Extended GetChildren to include Variables/Methods with ModellingRule=None when explicitly defined on a type. Added ~52 nodes.

2. **DefaultAccessRestrictions on type definitions** ([Part 3, 5.2.11](https://reference.opcfoundation.org/v105/Core/docs/Part3/5.2.11/)) -- AccessRestrictions and RolePermissions emitted on all nodes as metadata, but enforcement bypassed for type hierarchy nodes via IsPartOfTypeHierarchy.

3. **IsPartOfTypeHierarchy runtime bypass** -- New flag on NodeState and NodeMetadata. Set on ObjectType/VariableType in generated !forInstance block, propagated to children in AddPredefinedNode. ValidateRolePermissions and ValidateAccessRestrictions early-return Good when flag is set.

4. **forInstance propagation in type factories** -- Type factory children now use forInstance:forInstance when RootIsTypeDefinition. Resolved 102 deep children (TrueState/FalseState etc).

5. **IsPartOfTypeHierarchy in GetNodeMetadataAsync** -- The Browse service's per-reference ValidateRolePermissionsAsync calls GetNodeMetadataAsync (not GetPermissionMetadataAsync). Propagating the flag there resolved the final 99 type children that were filtered from Browse results.
