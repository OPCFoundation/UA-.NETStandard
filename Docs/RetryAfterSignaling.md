# Retry-after signaling for OPC UA backpressure

This document surveys robust ways to carry a server retry-after hint for rate limiting and admission control in OPC UA without depending on service diagnostics. It complements [Rate Limiting and Admission Control](RateLimiting.md), which documents the implemented server and client rate-limit behavior, and [Server Session Scalability](ServerScalability.md), which explains the connect-storm feedback loop this signal is meant to break. The related specification-change proposal is [RetryAfter](proposals/RetryAfter.md).

## Implementation status

Two diagnostics-independent carriers are implemented and on by default, feeding the client's adaptive reconnect policy:

- **`ResponseHeader.additionalHeader`** (all UA transports): the server attaches a structured `RetryAfterMs` value (a whole-millisecond `Int64` in the standard `AdditionalParametersType`) to a `BadServerTooBusy` `ServiceFault` via `RetryAfterHeader` / `ServerBusyException` (`EndpointBase.CreateFault`). The client reads it in `ClientBase.ValidateResponse` and surfaces it to the reconnect policy. Delivered regardless of `ReturnDiagnostics`.
- **HTTP `Retry-After`** (HTTPS transport): the `AddHttpsRateLimiter` gate sets the header on its HTTP 429; the client's `HttpsTransportChannel` maps a 429/503 with `Retry-After` to `BadServerTooBusy` carrying the hint.

Two further pieces are also delivered:

- **UA-TCP `Error` reason (client honoring)**: the UA-TCP client honors a `RetryAfterMs=N` token carried in a transient server-busy `Error` (ERR) message reason as a lower bound on channel-reconnect backoff (`RetryAfterHint`, clamped to the channel reconnect policy's max delay). The connection-level rejection deliberately stays a cheap socket-drop, so the reference server does not emit an ERR there; the client honors the token from any server that does send one.
- **Load-based `Server.ServiceLevel`**: the reference server now computes `Server.ServiceLevel` from session-establishment headroom (`ServerServiceLevelCalculator`), staying at 255 at low load and scaling toward a floor of 1 as sessions approach `MaxSessionCount`, with hysteresis to avoid oscillation. Clients can read or subscribe to it as a proactive capacity signal.

The client honors the reactive hints through `IReconnectPolicy.TryGetNextDelay` (the retry-after survives the diagnostics gate and client exception re-wrapping). The legacy `RetryAfterMs=N` `AdditionalInfo` token remains as a best-effort compatibility hint. Remaining future work: a structured retry-after field standardized in the spec, an ERR retry-after emitted by the server on connection-level rejection, and client-side proactive server reselection driven by `ServiceLevel`.

## Problem with the current fault `AdditionalInfo` token

PR #3946 added server and client rate limiting and currently communicates a best-effort retry-after hint for session-establishment overload by embedding a machine-readable `RetryAfterMs=N` token in a `BadServerTooBusy` fault's diagnostic `AdditionalInfo`, together with a human-readable `Retry after N ms.` message. That is useful for stack-local experimentation but is not a reliable protocol carrier.

First, a fault's `DiagnosticInfo.AdditionalInfo` is gated by the client's `RequestHeader.returnDiagnostics`; the server fault builder reads `request.RequestHeader.ReturnDiagnostics` into the diagnostics mask and then builds `ResponseHeader.ServiceDiagnostics` from the `ServiceResult` (`Stack/Opc.Ua.Core/Stack/Server/EndpointBase.cs:421-474`). If the client did not request diagnostics, the hint is not delivered even though the `BadServerTooBusy` status code is delivered.

Second, even when diagnostics are delivered, client exception wrapping can lose the original machine-readable `AdditionalInfo`: `new ServiceResult(ex)` assigns `AdditionalInfo = e.Message` in non-debug builds (`Stack/Opc.Ua.Types/Utils/ServiceResult.cs:358-365`). A retry-after token carried only in diagnostic additional information therefore has both delivery and preservation risks.

A robust retry-after signal should be delivered without diagnostics, should be machine-readable where possible, and should work both for service-level rejections such as `CreateSession` / `ActivateSession` and for transport-level rejections before a session or service request exists.

## Candidate mechanisms

| Mechanism | Spec grounding | Delivered without `returnDiagnostics` | Structure | Applies to | Stack server emit point | Stack client honor point |
| --- | --- | --- | --- | --- | --- | --- |
| `ResponseHeader.additionalHeader` `ExtensionObject` | OPC UA Part 4 §7.28 `ResponseHeader` | Yes | Strongly structured `ExtensionObject` | All service responses and `ServiceFault`, all UA transports | **Implemented:** `EndpointBase.CreateFault` attaches a `RetryAfterMs` `Int64` (via `RetryAfterHeader` / `ServerBusyException`) to `ResponseHeader.AdditionalHeader` on `BadServerTooBusy` faults | **Implemented:** `ClientBase.ValidateResponse` reads the `AdditionalHeader` on a bad response and surfaces the hint to the adaptive reconnect policy |
| HTTP `Retry-After` response header | RFC 9110; OPC UA Part 6 HTTPS mapping | Yes | Standard HTTP header: delay seconds or HTTP date | HTTPS / WSS-over-HTTP request rejection | **Implemented:** `AddHttpsRateLimiter` sets the `Retry-After` header on its HTTP 429 (`HttpsRateLimiterStartupContributor`) | **Implemented:** `HttpsTransportChannel` maps a 429/503 with `Retry-After` to `BadServerTooBusy` before `EnsureSuccessStatusCode` |
| UA-TCP Error message `Reason` | OPC UA Part 6 §7.1.2.5 Error Message | Yes | String only, so structured weakly by convention | UA-TCP connection-level rejection, including before a secure channel or session exists | The connection rate limiter still sheds by dropping the accepted socket; the server does not emit an ERR at connection level (deliberate — see below) | **Implemented (client):** the UA-TCP client honors a `RetryAfterMs=N` token in a transient server-busy ERR `Reason` as a lower bound on channel-reconnect backoff (`RetryAfterHint`) |
| `Server.ServiceLevel` variable | OPC UA Part 5 §6.10, Server Object `ServiceLevel` (NodeId `i=2267`) | Yes | Standard `Byte` capacity indicator, subscribable/readable | Any established session, all transports | **Implemented:** the reference server computes `Server.ServiceLevel` from session-establishment headroom (`ServerServiceLevelCalculator`) | `DefaultServerRedundancyHandler` reads `Server.ServiceLevel` for failover selection; proactive reselection based on it is future work |
| New first-class response field | Part 4 revised-value pattern, for example `RevisedSessionTimeout` in CreateSession §5.6.2.2 and revised subscription parameters in CreateSubscription §5.13.2.2 | Yes | Strongly structured field | Specific services whose response types are revised | Would require schema/API changes in generated service types and server implementations | Would require generated client/service response handling |

### `ResponseHeader.additionalHeader`

`ResponseHeader.additionalHeader` is the best always-on service-level carrier for binary and all other OPC UA service transports because it is part of the `ResponseHeader`, not part of diagnostics. A `ServiceFault` contains only a `ResponseHeader`, so this carrier covers busy rejections for `CreateSession` and `ActivateSession` where the response body is not otherwise available. A small standard `RetryAfter` `ExtensionObject` with a duration or millisecond field could be emitted on faults and possibly on successful responses that revise client behavior.

This is implemented in the stack: the server attaches the hint to the fault's `ResponseHeader.AdditionalHeader` in `EndpointBase.CreateFault` (via `RetryAfterHeader` and `ServerBusyException`), and the client reads it in `ClientBase.ValidateResponse` — the choke point the source-generated clients call for every service — surfacing it to the adaptive reconnect policy. Successful session-establishment responses are additionally processed by `Session.ProcessResponseAdditionalHeader`.

### HTTP `Retry-After`

For HTTPS transports, the most standards-compliant carrier is the HTTP `Retry-After` response header defined by RFC 9110. OPC UA Part 6's HTTPS mapping rides on HTTP, and HTTP 429 plus `Retry-After` is the established way for an HTTP server to tell clients when to retry without requiring any OPC UA diagnostic fields.

This is implemented for HTTPS without a UA schema change: the `AddHttpsRateLimiter` gate sets the `Retry-After` header on its HTTP 429, and the client's `HttpsTransportChannel` inspects the status before `EnsureSuccessStatusCode()`, translating a 429/503 with `Retry-After` into `BadServerTooBusy` carrying the hint.

### UA-TCP Error message `Reason`

UA-TCP can reject a connection before a service request, secure channel, or session exists. At that layer, the UA-TCP Error message from Part 6 §7.1.2.5 is the only protocol signal with payload: it carries an `Error` `StatusCode` and a `Reason` string. For connection-level overload (`Bad_TcpServerTooBusy`), a retry-after token in `Reason` is weakly structured but reliably delivered when an ERR can be sent.

The client side is implemented: the UA-TCP client parses a `RetryAfterMs=N` token from a transient server-busy ERR `Reason` and honors it as a lower bound on channel-reconnect backoff (`RetryAfterHint`). The connection rate limiter still sheds cheaply by disposing the accepted socket rather than sending an ERR (a deliberate performance choice), so the reference server does not currently emit this token; a client honors it from any server that does. A specification proposal should prefer a structured UA-TCP retry-after field if the wire format can be revised compatibly, or otherwise reserve a standard token grammar for `Reason`.

### `Server.ServiceLevel`

`Server.ServiceLevel` is not a reactive retry-after value, but it is a standard proactive load signal. OPC UA Part 5 §6.10 defines it as a `Byte` capacity indicator on the Server object, and clients can read or subscribe to it without diagnostics. Part 5 also uses related ServerStatus fields such as `SecondsTillShutdown`, `ShutdownReason`, and `State` as structured operational hints, which is precedent for timed operational guidance outside diagnostics.

The reference server now computes `ServiceLevel` from session-establishment headroom (`ServerServiceLevelCalculator`) — 255 at low load, scaling toward a floor as sessions approach `MaxSessionCount`, with hysteresis. The client reads it in `DefaultServerRedundancyHandler` and uses it for non-transparent redundancy failover selection; using it to proactively slow connection attempts or prefer less-loaded redundant endpoints (rather than only on hard failover) remains future work. This complements, not replaces, reactive retry-after on faults and transport rejections.

### First-class response fields and other precedents

Part 4 already uses revised-value fields to return server-selected limits without diagnostics: `RevisedSessionTimeout` in CreateSession §5.6.2.2 and `RevisedPublishingInterval`, `RevisedLifetimeCount`, and `RevisedMaxKeepAliveCount` in CreateSubscription §5.13.2.2. This pattern is strongest when the hint belongs to a specific successful service response or when a service-specific fault response can be extended in a future version.

Part 5's `ServerStatus.SecondsTillShutdown`, `ShutdownReason`, and `State` (`Suspended`, `CommunicationFault`) are also useful precedents: they provide structured, time-related server state to clients through the address space without requiring diagnostics. By contrast, `StatusCode` itself has no room for a millisecond retry value and should not be used as the carrier; status codes should classify the condition, while a separate field or header carries the timing.

## Recommendation tiers

| Tier | Recommendation | Why |
| --- | --- | --- |
| Available now, no OPC UA spec change | For HTTPS, emit and honor HTTP 429 `Retry-After` in `AddHttpsRateLimiter` / `HttpsTransportChannel`. | It is already standardized by HTTP, applies to OPC UA HTTPS mappings, and is delivered without UA diagnostics. |
| Available now, stack convention | For UA-TCP connection rejection, send ERR `Bad_TcpServerTooBusy` with a stable `Reason` token such as `RetryAfterMs=N` instead of silently disposing the socket. | It is the only available pre-session UA-TCP payload, but it is string-based and should be treated as an interoperability bridge. |
| Available now, proactive complement | Make `Server.ServiceLevel` load-based and let clients use it to stagger connects or prefer less-loaded redundant servers. | It is a standard address-space variable, delivered without diagnostics, and complements reactive retry-after. |
| Spec proposal, preferred service-level carrier | Standardize `RetryAfter` as either a first-class field in `ResponseHeader` or a standard `RetryAfter` DataType carried in `ResponseHeader.additionalHeader`. | It is structured, applies to `ServiceFault`, works across transports, and follows existing extension and revised-value precedents. |
| Spec proposal, preferred transport-level carrier | Standardize retry-after behavior for UA-TCP ERR and require/encourage HTTP `Retry-After` for HTTPS overload responses. | It covers connection-level overload before a service response exists and aligns UA transports with HTTP practice. |

## Delivered stack shape

The carriers are layered as follows. The existing `RetryAfterMs=N` diagnostic token is kept only as a best-effort compatibility hint. HTTPS `Retry-After` emission and parsing for HTTP 429/503 is implemented. On the client, a UA-TCP ERR `RetryAfterMs=N` reason token is honored as a lower bound on channel reconnect (the reference server does not emit it at connection-level rejection — a deliberate performance choice, so it interoperates with any server that does). A structured `RetryAfterMs` value on the fault `ResponseHeader.additionalHeader` is emitted and honored across all UA transports. The retry-after reaches `IReconnectPolicy.TryGetNextDelay` regardless of the carrier and survives the diagnostics gate and client exception re-wrapping (the client foundation in `ManagedSession` / `ConnectionStateMachine`).

For specification work, [Docs/proposals/RetryAfter.md](proposals/RetryAfter.md) proposes a structured service-level carrier plus transport-level carriers so clients can back off during connect storms without enabling diagnostics.
