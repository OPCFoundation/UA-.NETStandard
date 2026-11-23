<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# Reuse the ISA-95 Job Control model and rules

Reference `OPCFoundation.NetStandard.Opc.Ua.ISA95` and import `Opc.Ua.ISA95.JobControl`. The neutral
records, conversions, and rules work without a UA client, server, or session. For hosting and discovery, see the
[ISA-95 guide](ISA95.md).

## Evaluate a command before storing it

```csharp
using Opc.Ua.ISA95.JobControl;

var order = new Isa95JobOrder { Id = "batch-42" };
Isa95JobDecision decision = Isa95JobControlRules.Evaluate(
    current: null,
    Isa95JobOperation.StoreAndStart,
    Isa95JobControlVersion.V2);

if (decision.ReturnStatus == Isa95JobReturnStatusBits.Success)
{
    // On Create, persist the order and decision.State atomically.
    // Publish a status event after commit only if decision.PublishStatusEvent is true.
}
```

Pass `null` only if no order exists. A durable provider must evaluate and apply each decision in the same
transaction (or use optimistic concurrency and retry from the latest state). Capacity, scheduling, response
retention, and message delivery remain with the caller. `ValidateV1ResponseSelector` checks the V1 requirement
to select by order ID or by state, but not both. V1 Stop removes the order instead of retaining a completed entry.

V2 Store and StoreAndStart begin in Waiting. Start and RevokeStart change permission without resetting readiness.
Signal `MarkReady` when prerequisites are met; signal `Load` or `Unload` for program or configuration changes and
`MarkNotReady` if prerequisites cease to be met. `BeginExecution` requires permission to start and either Ready
or Loaded. V1-created orders retain the legacy execution path. `Close` and Prepare-only transitions change the
stored state without publishing a status event. Subscribe to the server's catalog stream if you need every order
mutation.

## Convert orders and responses

Use the `FromV1Order`/`ToV1Order` and `FromV2Order`/`ToV2Order` pairs on `Isa95JobControlConversions` and the
corresponding pairs for responses. Supply the OPC UA namespace index of
`http://opcfoundation.org/UA/ISA95-JOBCONTROL_V2/` when converting V2 responses or emitting state arrays; the
index comes from the message context or session, not from the NodeSet. On an unchanged V2 response, the converter
preserves the received state array, including a missing Prepare substate. For a changed state, it emits the
applicable standard substate. V2 field presence is retained when a record is copied with `with`, even when the
field's value is the default.

The neutral records retain wire metadata in memory, not as a persistence format. If a durable relay must
replay received wire details after a restart, store the received V2 structure and its namespace context
alongside the canonical data.

Cross-version conversion projects only representable fields. The SDK maps V1 Waiting/Ready by start permission;
the specification does not define this mapping. V1 Loaded projects to V2 NotAllowedToStart/Loaded, and V1 Error
projects to V2 Aborted. Loaded and Error remain distinct on V1 round trips.

## Use neutral records with a UA client

The existing V1 and V2 clients can convert orders and responses while retaining their version-specific methods
and return-status bits. For example, with an `Isa95JobControlV2Client`:

```csharp
ulong status = await client.ReceiveNeutralJobOrderAsync(
    Isa95JobOperation.Store,
    new Isa95JobOrder { Id = "batch-42" },
    ct: cancellationToken);

if ((status & Isa95JobReturnStatusBits.Success) != 0)
{
    var (response, responseStatus) =
        await client.RequestNeutralJobResponseByIdAsync("batch-42", cancellationToken);
}
```

V1 uses `ReceiveNeutralJobOrderAsync` with the V1 command enum and `RequestNeutralJobResponseAsync` with the
existing V1 selectors. Both clients support `ReceiveNeutralJobResponseAsync`.
