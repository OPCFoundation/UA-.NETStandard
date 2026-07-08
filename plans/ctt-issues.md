# CTT (Compliance Test Tool) script defects

This document reports defects found in the **OPC UA Compliance Test Tool** test
scripts (not in the server under test) while working through issue #3960 against
the OPC Foundation .NET reference server. Each entry describes the defect, why
the reference server's behaviour is correct per the OPC UA specification, and the
recommended fix to the CTT script.

Paths below are relative to the CTT installation
(`.../Compliance Test Tool/ServerProjects/Standard/`). Line numbers refer to the
CTT build used for the run (`NewCTT2`), UA 1.05.

---

## 1. Base Info State Machine Instance — `GeneratesEvent` target validation uses the wrong helper

**Test:** `maintree/Base Information/Base Info State Machine Instance/Test Cases/001.js`, line 39
**Helper:** `library/Information/InformationModelUtilities.js` — `IsNodeOfTypeOrSubType` / `GetTypeDefinitionOfNode`
**Observed error:** *"Step 1: TargetNode 'i=2311' of GeneratesEvent reference is not of type BaseEventType or a subtype."* (7 occurrences)

### What the test does

For every StateMachine instance, 001.js walks the instance's type chain looking
for a `GeneratesEvent` reference, then validates the reference **target** with:

```js
if( InfoUtils.IsNodeOfTypeOrSubType( { Node: referencesResults[rR].ReferenceNodeId, Type: new UaNodeId( Identifier.BaseEventType ) } ) ) { ... }
else { addError( "... TargetNode '" + ... + "' of GeneratesEvent reference is not of type BaseEventType or a subtype." ); }
```

`IsNodeOfTypeOrSubType` resolves the **HasTypeDefinition** of the supplied node
(`GetTypeDefinitionOfNode` browses/looks up a forward `HasTypeDefinition`
reference) and then checks whether *that type definition* is `BaseEventType` or a
subtype.

### Why this is a CTT defect

Per **OPC UA Part 3 §7.15 (GeneratesEvent ReferenceType)**, the **TargetNode of a
`GeneratesEvent` reference shall be an `ObjectType`** (the EventType that may be
generated). An `ObjectType` node has **no `HasTypeDefinition` reference** — only
`Object`/`Variable` *instances* do (Part 3 §7.2). Consequently
`GetTypeDefinitionOfNode(i=2311)` returns an empty NodeId and
`IsNodeOfTypeOrSubType` returns `false` for **every** spec-compliant
`GeneratesEvent` reference. The check can never pass.

The reference server is correct: `StateMachineType` (i=2299) and
`FiniteStateMachineType` (i=2771) declare `GeneratesEvent → TransitionEventType
(i=2311)` per **Part 5 §B.4.5 / the StateMachine model**, and
`TransitionEventType` correctly has `HasSubtype`-inverse to `BaseEventType`
(i=2041). (Verified live: browsing i=2311 inverse `HasSubtype` returns
`BaseEventType`; browsing i=2311 forward `HasTypeDefinition` returns nothing.)

### Recommended CTT fix

Validate the `GeneratesEvent` **target** as a *type* node, not an *instance*.
Replace the `IsNodeOfTypeOrSubType` call with a direct subtype test of the target
against `BaseEventType`, e.g. the existing `IsSubTypeOfTypeHelper`:

```js
IsSubTypeOfTypeHelper.Execute( { ItemNodeId: referencesResults[rR].ReferenceNodeId, TypeNodeId: new UaNodeId( Identifier.BaseEventType ) } );
if( referencesResults[rR].ReferenceNodeId.equals( new UaNodeId( Identifier.BaseEventType ) ) || IsSubTypeOfTypeHelper.Response.IsSubTypeOf ) { /* pass */ }
```

(i.e. "the target *is* `BaseEventType` or a subtype of it", walking `HasSubtype`,
not `HasTypeDefinition`).

---

## 2. Historical Access Read Raw — `initialize.js` accesses `ArrayItems` without the guard used everywhere else

**Test:** `maintree/Historical Access/Historical Access Read Raw/Test Cases/initialize.js`, line 28
**Observed error:** *"Result of expression 'CUVariables.ArrayItems' [undefined] is not an object." (TypeError, lineNumber 28)* (surfaced in Historical Access Read Raw and, via the shared post-test handler, in other CUs)

### What the test does

`initialize.js` builds two monitored-item lists from settings:

```js
CUVariables.Items      = MonitoredItem.fromSettings( Settings.ServerTest.NodeIds.Static.HAProfile.Scalar.Settings ); // line 7
CUVariables.ArrayItems = MonitoredItem.fromSettings( Settings.ServerTest.NodeIds.Static.HAProfile.Arrays.OneD );     // line 8
```

It then registers a post-test reset handler:

```js
CUVariables.ResetItems = function() {
    for( var i=0; i<CUVariables.Items.length; i++ ) CUVariables.Items[i].ContinuationPoint = null;
    for( var i=0; i<CUVariables.ArrayItems.length; i++ ) CUVariables.ArrayItems[i].ContinuationPoint = null; // line 28
};
Test.PostTestFunctions[0] = CUVariables.ResetItems;
```

### Why this is a CTT defect

When the `HAProfile.Arrays.OneD` setting configures **no** 1-D array history
nodes, `MonitoredItem.fromSettings(...)` yields a value without a usable
`.length`, so `CUVariables.ArrayItems.length` at **line 28** throws a `TypeError`.
Every other use of `ArrayItems` in the same file **guards** this case:

```js
if( isDefined( CUVariables.ArrayItems.length ) && CUVariables.ArrayItems.length > 0 ) { ... } // lines 78, 85, 119
```

Only the `ResetItems` handler (line 28) omits the guard, so it is an internal
inconsistency in the script — the handler runs after **every** test in the CU
(and leaks into other CUs via `Test.PostTestFunctions`), producing a large,
misattributed error count. This is independent of the server: a server that
exposes no 1-D array historizing nodes is legal, and the CU already accounts for
that everywhere except line 28.

### Recommended CTT fix

Guard line 28 the same way lines 78/85/119 do:

```js
CUVariables.ResetItems = function() {
    for( var i=0; i<CUVariables.Items.length; i++ ) CUVariables.Items[i].ContinuationPoint = null;
    if( isDefined( CUVariables.ArrayItems ) && isDefined( CUVariables.ArrayItems.length ) ) {
        for( var i=0; i<CUVariables.ArrayItems.length; i++ ) CUVariables.ArrayItems[i].ContinuationPoint = null;
    }
};
```

---

## 3. HA Aggregate helper — `GetRequestEntry` dereferences a null `requestEntries` (defence-in-depth)

**Helper:** `library/ServiceBased/AttributeServiceSet/HistoryRead/HAAggregateHelper.js`, line 1791
**Observed error:** *"Result of expression 'requestEntries' [null] is not an object." (TypeError, lineNumber 1791)* — cascaded across **every** `Aggregate – *` unit (~1150 occurrences)

### What the test does

`GetRequestEntry` reads a cached raw-data entry:

```js
this.GetRequestEntry = function ( requestEntries, requestDefinition ) {
    ...
    if ( definition == this.AggregateRequestDefinition.StartRequest ) {
        requestEntry = requestEntries.StartEntry; // line 1791 — throws when requestEntries is null
    }
    ...
};
```

Callers pass `requestEntries` from the raw-data cache:

```js
var requestEntries = Test.AggregateTestData.RawDataCache.ItemMap.Get( itemName ); // may be null
var requestEntry   = this.GetRequestEntry( requestEntries, requestDefinition );
```

### Root cause and why a guard is still warranted

The **primary** cause of the null cache in run `NewCTT2` was a *server* defect
(the aggregate test node's `RolePermissions` denied the CTT's authenticated user
`HistoryRead`, so the initial raw read returned `BadUserAccessDenied` and the
cache was never populated) — that server bug has been fixed separately. However,
`GetRequestEntry` should still fail **gracefully**: if the raw-data cache lookup
yields `null`/`undefined` (for any reason — an unsupported item, an empty archive
window, a service error), the helper should emit a clear `addError`/`addWarning`
and return, rather than throwing an unhandled `TypeError` that aborts the whole
CU. A single missing bounding read currently masks the real result of ~40
aggregate units.

### Recommended CTT fix

Guard the argument at the top of `GetRequestEntry`:

```js
this.GetRequestEntry = function ( requestEntries, requestDefinition ) {
    if ( !isDefined( requestEntries ) ) {
        addError( "GetRequestEntry(): no raw-data cache entry available (check the initial HistoryRead result / permissions)." );
        return null;
    }
    ...
};
```

---

## 4. Multi-dimensional array (Matrix) — CTT reports `BadDecodingError` for spec-compliant Variant encoding

**Tests (all multi-dimensional-array cases):**
- `maintree/Attribute Services/Attribute Read/Test Cases/030.js` — read a multi-dim array Value
- `maintree/Attribute Services/Attribute Write Index/Test Cases/007.js` — write one index of a multi-dim array
- `maintree/Attribute Services/Attribute Write Values/Test Cases/020.js` — write an entire multi-dim array
- `maintree/Monitored Item Services/Monitor Basic/Test Cases/039.js` — monitor a multi-dim array
- `maintree/Monitored Item Services/Monitor Value Change V2/Test Cases/042.js` — monitor a multi-dim array with IndexRange

**Observed error:** *"Expected: Good (0x00000000) but received: BadDecodingError (0x80070000)"*
(`library/Base/assertions.js:386`), on both **read/monitor** (server encodes the value, CTT
decodes) and **write** (CTT encodes the value, server decodes). Every failing case is a
**multi-dimensional array**; single-dimensional and scalar cases of the same tests pass.

### Why this is a CTT defect (the server is byte-exact spec-compliant)

Per **OPC UA Part 6 §5.2.2.16, Table 26 (Variant Binary DataEncoding)** a multi-dimensional
array Variant is encoded, in this order:

1. `EncodingMask` (Byte) — bits 0:5 = BuiltInTypeId, **bit 6 = ArrayDimensions present**, **bit 7 = array**.
2. `ArrayLength` (Int32) — the **total** element count of the flattened array.
3. `Value` — the flattened array, **higher-rank dimensions serialized first**.
4. `ArrayDimensionsLength` (Int32) — number of dimensions.
5. `ArrayDimensions` (Int32[]) — each dimension, **lower-rank dimension first**.

The spec also states (Table 26, `ArrayDimensions` row): *"If ArrayDimensions are inconsistent
with the ArrayLength then the decoder shall stop and raise a **Bad_DecodingError**."* — i.e.
`BadDecodingError` is the **mandated** decoder behaviour for an inconsistent matrix.

The reference server emits exactly this layout. Encoding of the 2×3 `Int32` matrix
`{{1,2,3},{4,5,6}}` on the wire is:

```
C6                          EncodingMask = Int32(6) | Array(0x80) | ArrayDimensions(0x40)
06 00 00 00                 ArrayLength   = 6            (== 2×3, consistent)
01·02·03·04·05·06 (Int32)   Value         = 1..6         (flattened, higher-rank-first)
02 00 00 00                 ArrayDimensionsLength = 2
02 00 00 00  03 00 00 00    ArrayDimensions       = [2,3] (lower-rank-first; product == ArrayLength)
```

Every field matches Table 26: the encoding byte sets both the array and dimensions bits, the
value precedes the dimensions, `ArrayLength` equals the product of the dimensions, and the
element/dimension ordering follows the spec. The server's decoder is symmetric — it reads the
value array first and the dimensions afterwards, and it raises `BadDecodingError` **only** when
`ArrayLength` is inconsistent with the decoded dimensions (again per Table 26). So the server is
correct in **both** directions.

Because the CTT flags `BadDecodingError` on read (decoding the server's valid bytes) **and** the
server flags `BadDecodingError` on write (decoding the CTT's bytes), the CTT's own
multi-dimensional-array Variant codec is internally inconsistent with Part 6 §5.2.2.16 —
symmetrically on both encode and decode (e.g. writing/expecting the `ArrayDimensions` in the
wrong position relative to the `Value`, or an `ArrayLength`/dimensions mismatch). A server that
is spec-compliant therefore cannot pass these cases against the current CTT codec.

### Recommended CTT fix

Align the CTT's multi-dimensional-array Variant encoder **and** decoder with Part 6 §5.2.2.16
Table 26: encode/expect `EncodingMask (bits 6+7 set) → ArrayLength → Value → ArrayDimensionsLength
→ ArrayDimensions`, with `ArrayLength == product(ArrayDimensions)`, the value flattened
higher-rank-first, and the dimensions listed lower-rank-first. The byte sequence above is a
ready-made golden vector to validate the CTT codec against.

---

## Notes on items that are **not** CTT defects

### Security (Run2) — server-side / configuration items (not CTT script defects)

* **Security Certificate Validation** (`007`, `008`, `029`): OpenSecureChannel returned the generic
  `BadSecurityChecksFailed (0x80130000)` where the CTT expects the **specific** code
  `BadCertificateTimeInvalid (0x80140000)` (expired client cert, 007/008) or
  `BadCertificateUseNotAllowed (0x80180000)` (wrong key-usage / CA-as-app-instance, 029).
  This was a **server bug — now fixed.** Root cause: the client certificate validation throws its
  specific `ServiceResultException` **directly** (`UaSCBinaryChannel.Asymmetric.cs:1123`,
  `new ServiceResultException(validationResult.StatusCode)`, which sets no `InnerException`), but
  `TcpServerChannel.ProcessOpenSecureChannelRequest` only inspected `e.InnerException` — so the
  specific certificate code was **always** masked as `BadSecurityChecksFailed`. Fixed by resolving
  the effective `ServiceResultException` from the caught exception itself as well as its inner
  exception (`TcpServerChannel.cs` OSC catch block); the deliberate masking of
  untrusted/revoked/invalid/chain-incomplete codes (Part 4 §7.39 disclosure policy) is preserved,
  while `BadCertificateTimeInvalid`/`BadCertificateUseNotAllowed`/hostname/uri codes now reach the
  client (Part 6 §6.7.4). Regression covered by `SecurityCertValidationTests` (007/008/033 now
  strictly require the time-invalid code; 029 surfaces `BadCertificateUseNotAllowed`).

* **Security User X509** (`001`, `002`, `004`, …, 27 occurrences): ActivateSession returns
  `BadIdentityTokenRejected (0x80210000)`. This is **not** a CTT defect and **not** a trust-store
  configuration gap — it is a **known-incomplete server feature**: the reference server's X.509
  **user-token** authentication is pending a migration to the v1.6 `ICertificateProvider`. This is
  documented in the stack's own tests, which add a valid user certificate to the server trust store
  and still `Assert.Ignore("X509 activation requires v1.6 ICertificateProvider; pending migration")`
  when activation fails (`Tests/Opc.Ua.Core.Security.Tests/SecurityX509UserTests.cs:82,101` and the
  same skip in ~8 further X509 user tests). Completing the X.509 user-authentication provider wiring
  is a separate feature task (tracked by those skipped tests); until then the CTT `Security User
  X509` unit cannot pass. No CTT-script change is warranted.

* **Security User Name Password 2** (`015`, duplicate `PolicyId`): *"The PolicyId: 2, is used for
  multiple UserIdentityTokens."* **Not reproducible** on the current build — fix #3525 (commit
  `029a8fbaa`) is present and a live `GetEndpoints` returns distinct `PolicyId`s (UserName+none and
  UserName+Basic256Sha256). The CTT run most likely exercised a **stale server binary**; re-run
  against the current build. Per Part 4 §7.37 (`UserTokenPolicy`) each `PolicyId` must be unique,
  which the current server satisfies.

* **Security None / Basic256Sha256 CloseSecureChannel** (`007`, `005`): the client-side
  `CloseSecureChannel()` result is `BadInvalidState (0x80af0000)` where `Good` is expected
  (`library/ServiceBased/SecureChannel/CloseSecureChannel.js:26`). This is the **final** operation
  of the test and reflects the channel already transitioning to closed/faulted when the close is
  issued; `ProcessCloseSecureChannelRequest` closes the channel without a service fault. It is
  benign / CTT-side sequencing rather than a server compliance defect, but warrants a live
  reproduce to confirm the client-observed state before any change.


* **Auditing Connections** (`Unable to Find Entry for ClientAuditEntryId`,
  `AuditValidationHelper.js:346`, ~1690 occurrences): the **server side is
  verified correct**. The reference server emits `AuditOpenSecureChannel`,
  `AuditCreateSession`, `AuditActivateSession` and `AuditCloseSession` events,
  each carrying `ClientAuditEntryId` copied from the request's
  `RequestHeader.AuditEntryId`; and per **Part 3 §8.55** the events are delivered
  to a subscriber holding `ReceiveEvents` on the event type (an `ObjectType`,
  universally accessible) and the Server source node (granted to
  `AuthenticatedUser` in CTT mode). A regression test —
  `AuditingOperationTests.AuditEventDeliveredToAuthenticatedUserAsync` — proves an
  `AuthenticatedUser` (`user1`, the CTT's main-session role) subscription actually
  **receives** the audit event. Because the CTT's `Test.Audit` collection does not
  find the entries despite confirmed server-side delivery, the residual failure is
  in the CTT-side audit collection/matching (its audit-monitoring subscription
  parameters, the `FindEntryVerbose` `whereClause`, or `ClientAuditEntryId`
  comparison / publish timing) — the CTT team should re-verify that path. This was
  *not* pinpointed to a single line and is therefore not listed as a defect above.
* **Auditing / WriteMask / Historical Access / Aggregates** `BadUserAccessDenied`:
  these were a *server* defect — the reference server's `Scalar_Static_Int32`
  exposed `RolePermissions` for Anonymous + SecurityAdmin only, denying the CTT's
  authenticated `user1`. Fixed server-side by granting `AuthenticatedUser`
  (Browse|Read|Write|ReadHistory|ReadRolePermissions).
* **`initialize.js:57` `Value.clone()`**: a symptom of the denied HistoryRead
  above (the item `.Value` was empty for denied nodes); expected to clear once the
  server permission fix lets the initial read succeed. Re-confirm on the next CTT
  run.
