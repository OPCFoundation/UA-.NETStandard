# OPC UA RetryAfter specification proposal

## Summary

Standardize an OPC UA retry-after signal for overload and admission-control backpressure so servers can tell clients when to retry without requiring `RequestHeader.returnDiagnostics`. The proposal has two complementary parts: a service-level carrier for service responses and `ServiceFault`, preferably a first-class `RetryAfter` field in `ResponseHeader` or a standard `RetryAfter` DataType carried in `ResponseHeader.additionalHeader`; and transport-level carriers for overload before a service response exists, namely a retry-after value for UA-TCP Error messages and use of the HTTP `Retry-After` header for the HTTPS mapping.

## Motivation

Connect storms and session-establishment bursts can overload a server before steady-state operation begins. A server can return `Bad_ServerTooBusy`, `Bad_TcpServerTooBusy`, or an HTTP 429 response, but a status code alone does not tell a cooperating client whether to retry immediately, after a few milliseconds, or after several seconds. Without a machine-readable delay, many clients retry too aggressively and amplify the overload.

Using diagnostic `AdditionalInfo` for this purpose is insufficient because diagnostics are only returned when the client requests them, and clients may discard or rewrite diagnostic text while converting exceptions. Retry-after is operational control information, not troubleshooting diagnostics, so it needs a carrier that is delivered independently of `returnDiagnostics`.

## Precedents

HTTP already defines `Retry-After` in RFC 9110 for 429 and 503 responses, with either a delay in seconds or an HTTP date. OPC UA can reuse this directly for the HTTPS mapping.

OPC UA Part 4 already returns server-selected values as first-class response fields without diagnostics. Examples include `RevisedSessionTimeout` in CreateSession §5.6.2.2 and `RevisedPublishingInterval`, `RevisedLifetimeCount`, and `RevisedMaxKeepAliveCount` in CreateSubscription §5.13.2.2. These revised values show that protocol-visible server guidance belongs in structured response data, not diagnostic text.

OPC UA Part 5 also exposes structured timed operational state through the address space, including `ServerStatus.SecondsTillShutdown`, `ShutdownReason`, and `State` values such as `Suspended` or `CommunicationFault` in §§6.6.3 and 6.10. `Server.ServiceLevel` in Part 5 §6.10 is a standard capacity indicator clients can monitor proactively.

`ResponseHeader.additionalHeader` in Part 4 §7.28 is an existing extension point and is reserved for future use by OPC UA. It is part of the response header, so it can be delivered on `ServiceFault` without relying on service diagnostics; clients that do not understand the extension can ignore it.

## Proposed changes to Part 4: service-level retry-after

Define a standard retry-after semantic for service responses and service faults. The value represents a minimum delay before the client should retry the same operation or a semantically equivalent operation against the same server endpoint. A value of zero means the server is not asking for a delay; absence of the value means no retry-after hint was provided.

The preferred shape is one of the following, in order of interoperability value:

1. Add an optional `RetryAfter` field to `ResponseHeader`, using either `Duration` or `UInt32` milliseconds. `Duration` aligns with existing OPC UA time intervals, while `UInt32` milliseconds is compact and precise enough for admission-control delays. The field is present on all service responses and on `ServiceFault` because `ServiceFault` contains a `ResponseHeader`.
2. If adding a field to `ResponseHeader` is too disruptive, define a standard `RetryAfter` Structure DataType carried in `ResponseHeader.additionalHeader`. The structure should contain at least `Duration RetryAfter` or `UInt32 RetryAfterMilliseconds`, and may optionally include an enum or flags describing the scope, such as `SameRequest`, `SameSession`, `SameEndpoint`, or `ServerWide`.

Servers should include the service-level retry-after value when returning transient overload statuses such as `Bad_ServerTooBusy`, `Bad_TooManyOperations`, `Bad_TooManyPublishRequests`, or analogous future overload codes. Clients should treat the value as a lower bound for their backoff calculation, not as a guarantee that the retry will succeed. Clients may apply jitter and may cap the delay according to local policy.

The response-header carrier should not replace revised fields that already exist for successful service negotiation. Instead, it complements them for transient rejection and for generic response-level backpressure across services.

## Proposed changes to Part 6: transport-level retry-after

### UA-TCP

Extend the UA-TCP Error message behavior in Part 6 §7.1.2.5 so a server rejecting a connection or secure-channel attempt due to transient overload can include a retry-after hint with `Bad_TcpServerTooBusy` or a related transport-level busy status.

If a wire-compatible structured extension to the Error message is feasible in a future protocol version, add a `RetryAfter` field using the same semantic and units as Part 4. If the existing Error message shape must remain unchanged, standardize a token grammar for the existing `Reason` string, for example `RetryAfterMs=<UInt32>`, and state that clients should parse the token only when the `Error` status is a transient busy or throttling status. Human-readable text may also be present, but the token grammar should be stable and language-independent.

A server should send an ERR with `Bad_TcpServerTooBusy` and retry-after when it can do so safely, instead of silently closing the socket. A client receiving such an ERR should feed the parsed retry-after value into its connection retry policy.

### HTTPS mapping

For OPC UA over HTTPS, require or strongly recommend that servers use the standard HTTP `Retry-After` response header when rejecting an OPC UA request due to rate limiting or overload with HTTP 429 or 503. The value follows RFC 9110: either delay seconds or an HTTP date.

A client implementation should inspect the HTTP status and headers before converting the response to a generic transport exception. When `Retry-After` is present, the client should translate it to the same internal retry-after semantic used for OPC UA service and UA-TCP retry-after carriers.

## Proposed changes to Part 5: proactive load signal guidance

Clarify that `Server.ServiceLevel` may be used as a dynamic load/capacity signal, not only as a redundancy-selection value. Servers should update it to reflect the current capacity to accept new sessions or service load, subject to smoothing to avoid oscillation. Clients may use it to stagger new connections, prefer less-loaded redundant servers, or avoid creating new sessions when the value indicates low service capacity.

This is a proactive complement and not a replacement for reactive retry-after. `ServiceLevel` does not carry a duration and is only available after a client can read or subscribe to the address space.

## Client behavior

A client that receives retry-after from any carrier should treat it as a lower bound for retrying the same endpoint and operation scope. The client should combine it with exponential or adaptive backoff, apply jitter to avoid synchronization, and respect local maximum-delay and cancellation policies. If multiple carriers are present, the client should prefer the most specific structured carrier, then transport-specific structured/header values, then standardized string tokens.

Clients should not require diagnostics to honor retry-after. Clients should continue to function when no retry-after is present by falling back to existing retry policy.

## Backward compatibility

A `ResponseHeader.additionalHeader`-based design is backward-compatible because unknown clients are expected to ignore unrecognized extension objects, and existing servers can omit the header. A first-class `ResponseHeader` field would require a versioned schema change and therefore has higher interoperability cost, but it would be the clearest long-term model if adopted in a future OPC UA revision.

The HTTPS `Retry-After` header is already a standard HTTP mechanism and is ignored by clients that do not understand it. A UA-TCP `Reason` token is backward-compatible because existing clients already treat the reason as text; clients that do not parse the token continue to see a normal transport error. A structured UA-TCP Error-message extension would require explicit versioning or negotiation.

## Security considerations

Retry-after values are hints, not authorization decisions. Clients must not assume a retry will succeed after the indicated delay, and servers must still enforce authentication, authorization, session limits, and rate limits on every attempt.

Servers should avoid exposing sensitive capacity details. Coarse or capped retry-after values are preferable to exact internal queue or CPU measurements. Clients should apply jitter so many clients do not reconnect simultaneously at the exact advertised boundary. Servers should cap accepted retry-after values if proxies or intermediaries are involved, and clients should cap honored values according to local policy to avoid denial-of-service through excessive delays.

## Alternatives considered

Diagnostic `AdditionalInfo` was rejected as the primary carrier because delivery depends on `returnDiagnostics` and diagnostic text can be overwritten or lost by exception wrapping. It can remain a best-effort compatibility hint but should not be required for interoperable backoff.

`StatusCode` was rejected as the carrier because it classifies the error condition and has no space for a millisecond or duration value. Defining many status codes for different delay buckets would be imprecise and would not scale.

A service-specific field on only `CreateSession` or `ActivateSession` was rejected as insufficient because overload can occur on any service and because `ServiceFault` contains only a `ResponseHeader`. Service-specific revised values remain appropriate for successful negotiation, but retry-after should be available generically.

`Server.ServiceLevel` alone was rejected as insufficient because it is proactive, coarse, and only available after a session can read or subscribe to the address space. It should be clarified and used as a complement to reactive retry-after, not as the sole mechanism.

## Related implementation note

The stack's implemented server and client retry-after behavior is summarized in [Server retry-after backpressure](../Sessions.md#server-retry-after-backpressure) and [Rate Limiting and Admission Control](../RateLimiting.md). Those sections map the proposal options below to the current server and client emit/honor points in the stack.
