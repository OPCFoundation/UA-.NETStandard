<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# Replace a provisionable device's node manager

A host can replace the address-space manager for a running
`ProvisionableDevice` without replacing its runtime or its managed
applications. Use `ProvisionableDeviceNodeManagerFactory.QueueReplacement` from
host control-plane code with the exact current `NodeManagerRegistration`.
This applies to a single replacement on the standard server. Generic
`PrepareAsync` and `ApplyAsync` are not the handoff entry points.

Use the same factory retained at registration or resolved from dependency
injection. The worker retains the exact device runtime, application provider,
security and update policies, and the original managed target bindings,
including their effective key stores and file providers. Replacement is not a
way to reload those providers or change the list of applications. The stock
Part 22 `Resources` manager continues to own the parent node.

`Good` means the worker accepted the request, not that publication succeeded.
Use lifecycle inspection and host telemetry to confirm the outcome. Never
wait for publication inside a device callback. Once the worker pauses the
predecessor, new device operations receive retryable `BadWouldBlock` while
admitted calls and their effects finish. If an open file or TrustList handle,
an active transaction, or pending file work cannot transfer, the worker
rejects the replacement instead of cancelling or migrating that work.

The successor uses the same target engine and reservation with new nodes and
handlers; initialization and recovery do not run again. Only the generation
in the published route can admit new effects. A failure before publication
starts can reopen the still-selected predecessor unless shutdown has begun.
A failure after irreversible publication starts but before the route changes
leaves the device closed and the lifecycle writer faulted; restart the host
rather than retrying the operation. If the route has already changed, a
cleanup failure does not reinstate the predecessor.
