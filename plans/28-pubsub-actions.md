# Part 14 PubSub Actions (request/response over PubSub)

## Problem & goal

OPC UA 1.05 Part 14 defines **Actions** — a request/response interaction pattern
carried over PubSub (publish an *action request*, receive correlated *action
responses*), the PubSub analogue of a Client/Server `Call`. The stack today has
only the **type artifacts** and no runtime:

- `ActionTargetDataType`, `ActionState` exist in
  `Stack/Opc.Ua.Core/Schema/Opc.Ua.NodeSet.xml` and
  `Tools/Opc.Ua.SourceGeneration.Core/Design/StandardTypes.xml`.
- `JsonActionMetaDataMessage`, `JsonActionRequestMessage`,
  `JsonActionResponseMessage` are present as schema/source-gen design types only.
- There is **no** action writer/reader runtime, request/response correlation,
  encoder wiring, app API, or MCP tooling.

**Goal:** implement Part 14 Actions end-to-end in `Opc.Ua.PubSub` and expose them
through a correctly-named MCP `PubSubActionTools` (the current
`PubSubActionTools` were misnamed configuration wrappers and were removed in
favour of the generic `Call` tool). This is a **follow-up PR**; this document is
the design + staging.

## Spec background (Part 14 §6.2.x / Annex B)

- An **Action request** is a DataSetMessage published by an *action requester*
  to an *action target*; it carries a request id, the target action, and input
  arguments.
- One or more **action responders** receive the request, execute it, and publish
  an **action response** correlated by request id, carrying a `StatusCode` and
  output arguments.
- `ActionTargetDataType` identifies the target (target id + addressing);
  `ActionState` models the lifecycle. `JsonAction{Request,Response,MetaData}Message`
  are the JSON wire envelopes; the UADP equivalents must be added.

## Design

### Encoding
- Add UADP action messages (mirror the JSON ones): `UadpActionRequestMessage`,
  `UadpActionResponseMessage`, `UadpActionMetaDataMessage`, routed through a new
  `UadpActionCoder` alongside `UadpDiscoveryCoder`.
- Wire request/response/metadata into `UadpEncoder` / `JsonEncoder`
  encode/decode dispatch (mirror the discovery routing).

### Runtime
- `ActionDataSetWriter` (requester side): publishes an action request, assigns a
  `RequestId`, and registers a pending-response awaiter.
- `ActionDataSetReader` (responder side): receives action requests, dispatches to
  a registered `IActionHandler` (target id → handler), and publishes the
  correlated response.
- Correlation service: maps `RequestId` → completion source with a timeout; no
  exposed locks (SemaphoreSlim / Channel).
- `ActionState` transitions; `ActionTargetDataType` resolution.

### App API (`IPubSubApplication`)
- Requester: `ValueTask<ActionResponse> InvokeActionAsync(ActionTarget target,
  ArrayOf<Variant> inputs, TimeSpan timeout, CancellationToken)`.
- Responder: `RegisterActionHandler(ActionTarget target, IActionHandler handler)`
  / fluent `AddActionResponder(...)` on `PubSubApplicationBuilder`; DI wiring.

### MCP tools (`PubSubActionTools`, the real one)
- `pubsub_invoke_action` (target, inputs) → awaits the response via the in-proc
  `PubSubRuntimeManager`.
- `pubsub_list_action_targets` — list locally-known / discovered action targets.
- `pubsub_register_action_responder` (demo/echo handler) for round-trip testing.

## Stages
1. UADP action message types + `UadpActionCoder` + encoder/decoder wiring (+ unit tests).
2. Correlation service + `ActionDataSetWriter` requester runtime.
3. `ActionDataSetReader` responder runtime + `IActionHandler` registration.
4. `InvokeActionAsync` / responder app API + fluent + DI.
5. MCP `PubSubActionTools` over `PubSubRuntimeManager`.
6. Integration test (UDP loopback round-trip: requester ↔ responder), ≥80%
   coverage, docs (Diagnostics.md / PubSub.md + McpServer.md), AOT sanity.

## Conventions & constraints
- Reuse the discovery plumbing patterns (coder, receive-loop routing,
  correlation) added in the discovery work.
- `ArrayOf<T>` / `ByteString` / `Variant` in public API, never `object`;
  `INullable` via `.IsNull`; TAP only; sealed; multi-TFM
  (net472;net48;netstandard2.1;net8/9/10); NativeAOT-clean.
- Maintain 1.5.378 source compatibility where applicable; mark superseded API
  `[Obsolete]` rather than removing.

## Risks / open questions
- Confirm the exact 1.05.07 Action wire layout (Annex B
  `Action{Request,Response,Target,Responder}DataType`) before encoder work.
- Decide whether action transport reuses the existing writer/reader group model
  or introduces dedicated action groups.  This needs to be exactly per spec, so defer to spec content.
- Security: action requests/responses must use the same UADP message security
  (Aes-CTR) + SKS key path as DataSet messages.
