# Recoverable Service Execution (RSE): experimental protocol reference

This reference describes **OPC 10000-27: Recoverable Service Execution (RSE)**,
draft **0.2.0**, dated **2026-10-16**, at Core commit
`8710615ed5765ff780fd54b7f58a6db884d315c8`. RSE is the publication name; the Async
Service API names, ASE capability/conformance identifiers, namespaces and
provisional IDs are unchanged. The initial public discussion is
[#4813](https://github.com/OPCFoundation/UA-.NETStandard/issues/4813),
which is closed. Neither that issue nor this implementation establishes
maintainer approval, certification, or adoption into the OPC UA standard.
See [the implementation guide](AsyncServices.md) for setup, typed APIs and
the distinction between protocol requirements and observed test evidence.

The canonical JSON contract defines the message layout and provisional
allocations. This document explains that contract; it does not define another
set of DTOs or identifiers.

## Why there are three Services

An ordinary request can finish after its Client has stopped waiting. If the
response is lost, sending the original request again can repeat its effects.
This proposal separates starting work from obtaining its result.

| Service | Purpose |
| --- | --- |
| `SubmitService` | Admit and start one eligible original request |
| `GetServiceResult` | Look up that operation and collect its retained result |
| `ControlServiceExecution` | Request work cancellation, acknowledge a received outcome, or give up result recovery |

The original request is sent once. A failed submission exchange is followed by
lookup using the Client operation ID, not transparent replay of that request.
A Server that cannot provide the requested recovery contract rejects admission
rather than executing through a non-recoverable fallback.
An SDK retry using a different MethodId is another original request, not
collection of the admitted operation. The typed Client disables that fallback
for captured Methods.

Ordinary Service APIs retain their existing behaviour. Support must be
explicitly enabled; language-level asynchronous execution alone does not
provide this protocol.

### Interface choice and alternatives

The prototype implements the selected three-Service interface, not an
alternative manager Object. A generic manager could expose submission,
collection and control as Methods over Call and reuse more existing dispatch
and generator code. It would still need send-once recovery, owned execution,
typed acceptance, authorization, retained results and bounded accounting.
It would also need rules for the outer Call status, manager Method status and
multiple manager invocations in one Call. No equivalent manager prototype or
total integration-cost comparison has been produced.

For a certificate-only application that can change its Method interface,
ticket-based Methods may be a simpler choice. Part 12's pull
StartSigningRequest/FinishRequest workflow is not a drop-in replacement for push
CreateSigningRequest. A push ticket API would still have to define recovery
and receipt rules while preserving the device's pending-key and transaction
ownership. UpdateCertificate and ApplyChanges remain separate installation and
application steps; ApplyChanges is not result collection.

Subscriptions and Events address notification delivery, while Programs expose
an application's execution lifecycle. Neither automatically retains an
unchanged original Service response. These alternatives are design rationale,
not additional conformance modes of this prototype.

## Namespace and provisional identities

All declarations use namespace URI `http://opcfoundation.org/UA/`, namespace
index zero. The overlay model version is `1.05.07-ase-draft.0.2.0`; the contract
revision advertised by an enabled Server is `0.2.0`. These are experimental
identifiers, not final OPC Foundation assignments.

| Structure | DataType | Default Binary | Default XML | Default JSON |
| --- | --- | --- | --- | --- |
| ServiceOperationInfoDataType | 1200010 | 1202010 | 1203010 | 1204010 |
| SubmitServiceRequest | 1200020 | 1202020 | 1203020 | 1204020 |
| SubmitServiceResponse | 1200021 | 1202021 | 1203021 | 1204021 |
| GetServiceResultRequest | 1200022 | 1202022 | 1203022 | 1204022 |
| GetServiceResultResponse | 1200023 | 1202023 | 1203023 | 1204023 |
| ControlServiceExecutionRequest | 1200024 | 1202024 | 1203024 | 1204024 |
| ControlServiceExecutionResponse | 1200025 | 1202025 | 1203025 | 1204025 |

For example, the proposed Binary encoding of SubmitServiceRequest is
`i=1202020`. UA JSON's `UaTypeId` identifies the DataType, `i=1200020`, rather
than its allocated Default JSON encoding Object.

`SupportedServices` and `ServiceOperationInfoDataType.ServiceId` identify
original request DataTypes, not encoding Objects. The three wrapper Services
are not eligible original requests.

## Ordered fields

Order below is declaration and wire order. `Structure` denotes an
ExtensionObject-valued field, not permission to submit an arbitrary Structure.
The standard headers retain their existing layouts.

| Structure | Ordered field/type pairs |
| --- | --- |
| ServiceOperationInfoDataType | `ClientOperationId:Guid`, `ServerToken:ByteString`, `ServiceId:NodeId`, `State:ServiceOperationState`, `OutcomeKind:ServiceOperationOutcome`, `OutcomeStatus:StatusCode`, `ExecutionAllowance:Duration`, `ResultRetentionTime:Duration`, `ExecutionRemaining:Duration`, `ResultRemaining:Duration`, `HistoryRemaining:Duration`, `CancellationRequested:Boolean`, `RetryAfter:Duration` |
| SubmitServiceRequest | `RequestHeader:RequestHeader`, `ClientOperationId:Guid`, `Request:Structure`, `ExecutionAllowance:Duration`, `ResultRetentionTime:Duration`, `WaitTime:Duration` |
| GetServiceResultRequest | `RequestHeader:RequestHeader`, `ClientOperationId:Guid`, `ServerToken:ByteString`, `WaitTime:Duration` |
| ControlServiceExecutionRequest | `RequestHeader:RequestHeader`, `ServerToken:ByteString`, `Action:ServiceOperationControlAction` |
| SubmitServiceResponse | `ResponseHeader:ResponseHeader`, `Operation:ServiceOperationInfoDataType`, `Result:Structure` |
| GetServiceResultResponse | `ResponseHeader:ResponseHeader`, `Operation:ServiceOperationInfoDataType`, `Result:Structure` |
| ControlServiceExecutionResponse | `ResponseHeader:ResponseHeader`, `Result:ServiceOperationControlResult`, `Operation:ServiceOperationInfoDataType` |

The response fields named `Result` have different types. Submit/Get return an
optional original response payload. Control returns a control-result
enumeration and never transfers that payload.

## Enumerations

| Enumeration | DataType | EnumStrings | Values |
| --- | --- | --- | --- |
| ServiceOperationState | 1200000 | 1205000 | `Unknown=0`, `Pending=1`, `OutcomeAvailable=2`, `Acknowledged=3`, `Abandoned=4`, `Expired=5` |
| ServiceOperationOutcome | 1200001 | 1205001 | `None=0`, `ServiceResponse=1`, `ExecutionDeadlineExceeded=2`, `ResultUnavailable=3` |
| ServiceOperationControlAction | 1200002 | 1205002 | `Unspecified=0`, `Cancel=1`, `Acknowledge=2`, `Abandon=3` |
| ServiceOperationControlResult | 1200003 | 1205003 | `Unspecified=0`, `Applied=1`, `AlreadyApplied=2`, `NotApplicable=3`, `UnknownOperation=4` |
| ServiceOperationAuditAction | 1200004 | 1205004 | `Admitted=0`, `OutcomeCommitted=1`, `CancellationRequested=2`, `Acknowledged=3`, `Abandoned=4`, `Expired=5`, `Rejected=6`, `Denied=7`, `Collected=8`, `SessionClosed=9`, `HistoryForgotten=10`, `InvocationEnded=11` |

Unspecified is invalid as a requested control action or returned control
result. Unknown is a lookup result: it does not prove that the original work
never ran, cannot still be admitted, or is safe to repeat.

## Identity and ownership

The Client chooses a nonzero random Guid before submission. An admitted
operation also has an opaque Server token of 1 to 64 bytes. This prototype uses
32 random bytes. Neither value is a bearer credential.

GetServiceResult accepts exactly one selector. A nonzero ClientOperationId
with a **null** ServerToken recovers a lost submission reply. Once the token is
known, use it with a null Guid. ControlServiceExecution requires the token.
RequestHeader.requestHandle identifies the individual exchange.

A repeated submission using a remembered Client ID is rejected even if its
inputs are identical. This is not input-fingerprint-based idempotent
submission. Do not reuse an ID for another logical operation or automatically
resend original work after an uncertain result.

The issuing Session, its validated creator and current authorization govern
collection and control. A new Session is not a continuation merely because
the application, user or an identifier is the same. Authorization is checked
again before delayed delivery. The enclosed request cannot select a different
Session through its own header.

Recovery follows existing activation and trust rules. It does not authorize
weaker security settings or a new identity. The opt-in shared Client binding
is distinct from the strict request-context API.

Replacement-channel activation preserves the Client certificate, creator,
SecurityPolicy and MessageSecurityMode required by Part 4; it does not apply a
"same or stronger" substitution. The continuity check does not pin the old
Server certificate. An authorized renewal can be accepted only after validating
the new certificate against the intended Server identity and the mapping's
trust requirements, followed by reactivation of the original Session.

Protection is evaluated from the established transport. TLS can provide HTTPS
integrity and confidentiality without changing UA MessageSecurityMode or policy.
A claimed profile URI, URL scheme or request header is not proof of TLS.
Transport protection does not grant application or user permissions.
HTTPS transport certificates and OPC UA ApplicationCertificates keep their
separate mapping-defined roles.

## Reading a response

The outer ResponseHeader describes the current Submit/Get exchange. The
embedded original response includes its own header, results and diagnostics.
Its contents are fixed once committed. A later collection uses a new outer
header without rewriting the embedded one.

An original response payload is present only when State is OutcomeAvailable
and OutcomeKind is ServiceResponse. Its type must match the submitted Service,
with the normal original ServiceFault exception. OutcomeStatus then equals
the original header's ServiceResult. A Good ServiceResult does not imply that
all items in a batch succeeded.

ExecutionDeadlineExceeded and ResultUnavailable carry no original response.
These outcomes can occur after original work has had effects; they are not
successful replacements for the original Service response.

Unknown replies have no Server token or ServiceId, zero lifetime values,
OutcomeKind None, OutcomeStatus Good, CancellationRequested false, RetryAfter
zero and no Result. A Client-ID lookup echoes the requested ID; a token-only
unknown lookup has a null Guid. Good in this metadata is not business success.

## Time and resource limits

Durations are milliseconds. ExecutionAllowance and ResultRetentionTime must be
finite and positive; WaitTime must be finite and nonnegative. Zero wait is a
poll. The Server accepts requested lifetimes unchanged or rejects them before
dispatch.

ExecutionAllowance, E, is the requested execution budget starting at admission;
admission accepts that exact positive finite budget or rejects it.
ResultRetentionTime, R, starts when the final outcome is available. A fast result
in SubmitService still has the same retention and acknowledgement requirements
as a later result.

When E expires first, the operation records an execution-deadline outcome and
requests cancellation where possible. A late original response cannot replace
it. Invocation resources stay charged until the invocation actually ends; a
protocol deadline does not establish that external effects stopped.

ACK, abandonment or result expiry releases the payload. Terminal history then
lasts another R from release. Polling and repeated controls renew neither
window. Still-running work can require minimal identity and accounting beyond
the public intervals.

Remaining durations are elapsed-time measurements, not a new lease starting
when the Client receives a reply. Transit time consumes the opportunity.
Positive Pending RetryAfter is at most ExecutionRemaining and strictly less
than R. If no positive value is safe, it is zero. Other states use zero.

Admission reserves resources before invocation. Existing message/encoding and
aggregate resource limits apply to the complete envelope, not just its inner
Result. The prototype caps retained results: oversized original output becomes
a small ResultUnavailable outcome. This never authorizes repeating work.

A smaller delivery limit after recovery can prevent an exchange from carrying
an existing result. That failure does not rewrite or acknowledge the saved
outcome or, by itself, invalidate the operation identity. Collection can resume
after permitted capacity is restored within the original R. It cannot renew R,
replay submission, raise an applicable captured safety ceiling or bypass
security, namespace/type validation or other decoder limits. Storage still used
by an in-flight sender stays accounted for after logical release.

## Controls and acceptance

Cancel requests stopping active work where possible. It does not promise
rollback. A repeated request is AlreadyApplied only while that invocation is
active; ended work is NotApplicable. An original cancellation response is an
original Service outcome, distinct from a failed control exchange.

Acknowledgement accepts an available outcome and releases its payload.
Abandonment gives up recovery without requesting work cancellation. These
actions have distinct applicability; they are not aliases.

ACK is a separate exchange. If its reply is lost, retry ACK, not the original
request. Failure of that exchange does not replace an already accepted result.

Acceptance policy is Client-local. Default SDK ACK follows decoding,
validation **and acceptance into the caller completion context**; decoding an
internal object is insufficient. Selected application/storage acceptance must
also succeed before ACK. The same wire action is used for every local policy.
For a typed Method API, this includes decoding and validating its output
arguments through the generated proxy. Decoding a CallResponse alone is not
acceptance of its typed Method result.

Ordinary Cancel by requestHandle ends the wrapper wait after admission, not the
owned invocation. Cancellation observed before admission can prevent dispatch.
Connection loss and stopping a local wait imply neither abandonment nor work
cancellation.

## Existing StatusCodes

No new StatusCodes are allocated.

| Condition | Status |
| --- | --- |
| Invalid identifier, selector, token, action or duration | `BadInvalidArgument` |
| Unsupported positive lifetime | `BadOutOfRange` |
| Remembered duplicate Client ID | `BadInvalidState` |
| Unsupported or excluded original Service | `BadServiceUnsupported` |
| Admission reservation failure | `BadResourceUnavailable` |
| Record known in the requesting Session, but creator or action authorization fails | `BadUserAccessDenied` |
| Security configuration ineligible for recovery | `BadSecurityModeRejected` |
| Execution-deadline outcome | `BadTimeout` |
| Oversized original response | `BadResponseTooLarge` |
| Other original-response encoding failure | `BadEncodingError` |

Lookup is Session-scoped. For an otherwise authorized request, a token or ID
belonging only to another Session is indistinguishable from a nonexistent
reference in this Session. Get returns Good plus Unknown. Control returns Good
plus UnknownOperation and Unknown metadata. Neither reveals the foreign
identity, existence or payload.

This does not conceal every authorization failure. A known record in the
requesting Session can produce BadUserAccessDenied when its creator or current
permission check fails. Normal authentication and general Service-permission
errors also retain their ordinary behaviour.

## Optional runtime model

The generated overlay contains 67 Nodes and 15 types. Compiling its definitions
does not enable the feature. A disabled Server neither installs its capability
root nor executes the new Services.

| Definition or root | NodeId | Base type or parent |
| --- | --- | --- |
| AsyncServiceCapabilitiesType | i=1200040 | BaseObjectType, i=58 |
| AsyncServiceDiagnosticsType | i=1200041 | BaseObjectType, i=58 |
| AuditServiceOperationEventType | i=1200042 | AuditSessionEventType, i=2069 |
| AsyncServiceCapabilities | i=1200100 | ServerCapabilities, i=2268 |
| AsyncServiceDiagnostics | i=1200101 | ServerDiagnostics, i=2274 |

Activation installs the overlay and fixed root anchors. Runtime children get
Server-assigned NodeIds in the application namespace and retain namespace-zero
BrowseNames. A type member's declaration NodeId is not an instance child ID.

The capability root reports ContractVersion, SupportedServices and
minimum/maximum/default E and R. `SupportsRecoverableExecution` is an optional
read-only Boolean Property on a Method instance. Absence is undeclared; True
is not an admission guarantee; False reports ineligibility under current
Server policy. Admission evaluates current policy, not cached Client discovery.
A provider must not publish current False yet ignore it because Call is in
its supported-Service list. A current False on an otherwise callable Method
rejects the whole recoverable Call submission with BadServiceUnsupported before
any item executes. Ordinary per-item validation and permission failures remain
per-item results, not an all-items-authorized transaction.

Diagnostics are privileged aggregate measurements, not an operation browser.
ActiveOperationCount follows invocation lifetime; RetainedOutcomeBytes includes
storage still used by senders. Cumulative counters saturate at UInt64 maximum.

The new audit Event adds ClientOperationId, ServiceId, Action, OperationState,
OutcomeKind and OutcomeStatus to inherited audit fields, including SessionId.
It contains no raw control token, credentials or copied request/result payload.
Audit action success is separate from the original operation's outcome.

The publication-metadata package is not required at runtime and introduces no
runtime namespace dependency.

## Limits

The contract provides bounded recovery within one Session. It does not promise
restart durability, replacement-Session recovery, exactly-once business
effects, batch rollback, scheduling priority, progress or completion estimates.
Codec and in-process endpoint tests are not evidence of native reply loss or
certificate rotation; those require the integration tests described in the
[implementation guide](AsyncServices.md).
Evidence for a mapping must observe the Server's actual encoding and established
protection; configuring an endpoint or silently falling back to another mapping
does not demonstrate it.
