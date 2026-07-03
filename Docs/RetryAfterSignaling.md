# Retry-after signaling for OPC UA backpressure

This document surveys robust ways to carry a server retry-after hint for rate limiting and admission control in OPC UA without depending on service diagnostics. It complements [Rate Limiting and Admission Control](RateLimiting.md), which documents the implemented server and client rate-limit behavior, and [Server Session Scalability](ServerScalability.md), which explains the connect-storm feedback loop this signal is meant to break. The related specification-change proposal is [RetryAfter](proposals/RetryAfter.md).

## Problem with the current fault `AdditionalInfo` token

PR #3946 added server and client rate limiting and currently communicates a best-effort retry-after hint for session-establishment overload by embedding a machine-readable `RetryAfterMs=N` token in a `BadServerTooBusy` fault's diagnostic `AdditionalInfo`, together with a human-readable `Retry after N ms.` message. That is useful for stack-local experimentation but is not a reliable protocol carrier.

First, a fault's `DiagnosticInfo.AdditionalInfo` is gated by the client's `RequestHeader.returnDiagnostics`; the server fault builder reads `request.RequestHeader.ReturnDiagnostics` into the diagnostics mask and then builds `ResponseHeader.ServiceDiagnostics` from the `ServiceResult` (`Stack/Opc.Ua.Core/Stack/Server/EndpointBase.cs:421-474`). If the client did not request diagnostics, the hint is not delivered even though the `BadServerTooBusy` status code is delivered.

Second, even when diagnostics are delivered, client exception wrapping can lose the original machine-readable `AdditionalInfo`: `new ServiceResult(ex)` assigns `AdditionalInfo = e.Message` in non-debug builds (`Stack/Opc.Ua.Types/Utils/ServiceResult.cs:358-365`). A retry-after token carried only in diagnostic additional information therefore has both delivery and preservation risks.

A robust retry-after signal should be delivered without diagnostics, should be machine-readable where possible, and should work both for service-level rejections such as `CreateSession` / `ActivateSession` and for transport-level rejections before a session or service request exists.

## Candidate mechanisms

| Mechanism | Spec grounding | Delivered without `returnDiagnostics` | Structure | Applies to | Stack server emit point | Stack client honor point |
| --- | --- | --- | --- | --- | --- | --- |
| `ResponseHeader.additionalHeader` `ExtensionObject` | OPC UA Part 4 §7.28 `ResponseHeader` | Yes | Strongly structured `ExtensionObject` | All service responses and `ServiceFault`, all UA transports | `EndpointBase.CreateFault` currently sets `ServiceResult`, `ServiceDiagnostics`, and `StringTable` but not `ResponseHeader.AdditionalHeader` (`Stack/Opc.Ua.Core/Stack/Server/EndpointBase.cs:465-478`) | `Session` already calls `ProcessResponseAdditionalHeader` after CreateSession, ActivateSession, and re-activation success responses (`Libraries/Opc.Ua.Client/Session/Session.cs:1425-1426`, `1516-1517`, `1951-1953`); the hook decodes `ResponseHeader.AdditionalHeader` (`Session.cs:5185-5197`) |
| HTTP `Retry-After` response header | RFC 9110; OPC UA Part 6 HTTPS mapping | Yes | Standard HTTP header: delay seconds or HTTP date | HTTPS / WSS-over-HTTP request rejection | `AddHttpsRateLimiter` sets `RateLimiterOptions.RejectionStatusCode = 429` but no `Retry-After` header (`Stack/Opc.Ua.Bindings.Https/Https/HttpsRateLimiterStartupContributor.cs:88-92`) | `HttpsTransportChannel` calls `response.EnsureSuccessStatusCode()` immediately after `PostAsync`, which discards the 429 response and headers from the stack's perspective (`Stack/Opc.Ua.Core/Stack/Https/HttpsTransportChannel.cs:282-287`) |
| UA-TCP Error message `Reason` | OPC UA Part 6 §7.1.2.5 Error Message | Yes | String only, so structured weakly by convention | UA-TCP connection-level rejection, including before a secure channel or session exists | The connection rate limiter currently rejects by disposing the accepted socket after logging the retry-after value (`Stack/Opc.Ua.Core/Stack/Tcp/TcpTransportListener.cs:998-1008`); it does not send an ERR message | UA-TCP client connection/open-channel error handling could parse an ERR `Reason` token such as `RetryAfterMs=N` when the `Error` status is `Bad_TcpServerTooBusy` |
| `Server.ServiceLevel` variable | OPC UA Part 5 §6.10, Server Object `ServiceLevel` (NodeId `i=2267`) | Yes | Standard `Byte` capacity indicator, subscribable/readable | Any established session, all transports | The reference server initializes `Server.ServiceLevel` to a constant 255 (`Libraries/Opc.Ua.Server/Server/ServerInternalData.cs:797-800`) | `DefaultServerRedundancyHandler` reads `Server.ServiceLevel` with redundancy support (`Libraries/Opc.Ua.Client/Session/DefaultServerRedundancyHandler.cs:77-97`) and uses it only to select a hard failover target (`DefaultServerRedundancyHandler.cs:130-143`) |
| New first-class response field | Part 4 revised-value pattern, for example `RevisedSessionTimeout` in CreateSession §5.6.2.2 and revised subscription parameters in CreateSubscription §5.13.2.2 | Yes | Strongly structured field | Specific services whose response types are revised | Would require schema/API changes in generated service types and server implementations | Would require generated client/service response handling |

### `ResponseHeader.additionalHeader`

`ResponseHeader.additionalHeader` is the best always-on service-level carrier for binary and all other OPC UA service transports because it is part of the `ResponseHeader`, not part of diagnostics. A `ServiceFault` contains only a `ResponseHeader`, so this carrier covers busy rejections for `CreateSession` and `ActivateSession` where the response body is not otherwise available. A small standard `RetryAfter` `ExtensionObject` with a duration or millisecond field could be emitted on faults and possibly on successful responses that revise client behavior.

This stack already has useful client-side plumbing: successful session-establishment paths call `ProcessResponseAdditionalHeader` (`Session.cs:1425-1426`, `1516-1517`, `1951-1953`), and that method decodes `ResponseHeader.AdditionalHeader` (`Session.cs:5185-5197`). The missing stack pieces are emitting `AdditionalHeader` from `EndpointBase.CreateFault` (`EndpointBase.cs:465-478`) and teaching the client fault/transport path to preserve and honor the same structured header on `ServiceFault` responses.

### HTTP `Retry-After`

For HTTPS transports, the most standards-compliant carrier is the HTTP `Retry-After` response header defined by RFC 9110. OPC UA Part 6's HTTPS mapping rides on HTTP, and HTTP 429 plus `Retry-After` is the established way for an HTTP server to tell clients when to retry without requiring any OPC UA diagnostic fields.

The stack's HTTPS limiter already rejects with HTTP 429 (`HttpsRateLimiterStartupContributor.cs:88-92`), but it does not set `Retry-After`. The client currently calls `EnsureSuccessStatusCode()` (`HttpsTransportChannel.cs:282-287`), so a 429 is surfaced as a generic HTTP failure and the header is not translated into the reconnect policy's retry-after input. Setting the header server-side and parsing it before `EnsureSuccessStatusCode()` client-side would be available now for HTTPS without a UA schema change.

### UA-TCP Error message `Reason`

UA-TCP can reject a connection before a service request, secure channel, or session exists. At that layer, the UA-TCP Error message from Part 6 §7.1.2.5 is the only protocol signal with payload: it carries an `Error` `StatusCode` and a `Reason` string. For connection-level overload (`Bad_TcpServerTooBusy`), a retry-after token in `Reason` is weakly structured but reliably delivered when an ERR can be sent.

The stack's connection rate limiter currently has a retry-after value but disposes the accepted socket instead of sending ERR (`TcpTransportListener.cs:998-1008`). A practical near-term improvement is to send an ERR with `Bad_TcpServerTooBusy` and `Reason` containing a stable token such as `RetryAfterMs=N`; a specification proposal should prefer a structured UA-TCP retry-after field if the wire format can be revised compatibly, or otherwise reserve a standard token grammar for `Reason`.

### `Server.ServiceLevel`

`Server.ServiceLevel` is not a reactive retry-after value, but it is a standard proactive load signal. OPC UA Part 5 §6.10 defines it as a `Byte` capacity indicator on the Server object, and clients can read or subscribe to it without diagnostics. Part 5 also uses related ServerStatus fields such as `SecondsTillShutdown`, `ShutdownReason`, and `State` as structured operational hints, which is precedent for timed operational guidance outside diagnostics.

The reference server currently sets `ServiceLevel` to 255 (`ServerInternalData.cs:797-800`). The client reads it in `DefaultServerRedundancyHandler` (`DefaultServerRedundancyHandler.cs:77-97`) and uses it for non-transparent redundancy failover selection (`DefaultServerRedundancyHandler.cs:130-143`). Making the server value load-based and having clients proactively slow connection attempts or prefer less-loaded redundant endpoints would complement, not replace, reactive retry-after on faults and transport rejections.

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

## Proposed stack follow-up shape

A complete implementation can layer these carriers. First, keep the existing `RetryAfterMs=N` diagnostic token only as a best-effort compatibility hint. Second, add HTTPS `Retry-After` emission and parsing for HTTP 429. Third, send UA-TCP ERR for connection limiter rejection with `Bad_TcpServerTooBusy` and a stable reason token. Fourth, define an internal retry-after result model that can be populated from HTTP headers, UA-TCP ERR reason, diagnostics, or future `ResponseHeader.additionalHeader`, and feed that model into `IReconnectPolicy.TryGetNextDelay`.

For specification work, [Docs/proposals/RetryAfter.md](proposals/RetryAfter.md) proposes a structured service-level carrier plus transport-level carriers so clients can back off during connect storms without enabling diagnostics.
