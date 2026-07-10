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

## 5. HA Aggregate helper — multi-node path dereferences `possibleNodeId` without an `isDefined` guard

`ServerProjects/Standard/library/ServiceBased/AttributeServiceSet/HistoryRead/HAAggregateHelper.js`,
`PerformMultipleNodeTest` (around line 1484), raises `possibleNodeId [undefined] is not an object`
(a JavaScript `TypeError`) roughly 100 times across the aggregate conformance units, aborting the
affected multi-node aggregate cases.

### What the test does

For the multi-node aggregate cases (`configObject.Items.length > 1`) the helper walks the raw-data
cache and, for every node referenced by a cached request entry, maps the cached position back to the
current test's variable list:

```js
for ( var nodeIndex = 0; nodeIndex < requestEntry.Nodes.length; nodeIndex++ ) {
    var originalItemIndex = requestEntry.Nodes[ nodeIndex ].Index;
    var possibleNodeId = variables.Items[ originalItemIndex ];   // may be undefined
    if ( itemLookup.Contains( possibleNodeId.NodeId.toString() ) ) {   // <-- throws here
        ...
    }
}
```

`originalItemIndex` is an index that was captured against the **full** variable set when the raw-data
cache was built, but `variables.Items` here is the **current** (potentially smaller / re-ordered)
per-configuration subset. When the cached index has no corresponding entry in `variables.Items`,
`possibleNodeId` is `undefined` and the immediate `possibleNodeId.NodeId.toString()` throws.

### Why this is a CTT defect

Every other place in the same helper that indexes `variables.Items` guards the lookup with
`isDefined(...)` before dereferencing (e.g. the single-node path). This one call site does not, so a
perfectly valid server address space (whose node ordering simply differs from the cache's captured
indices) makes the script throw instead of skipping the unmatched entry. The server returns no error
here — the failure is entirely inside the CTT script.

### Recommended CTT fix

Guard the dereference exactly as the sibling code paths already do:

```js
var possibleNodeId = variables.Items[ originalItemIndex ];
if ( isDefined( possibleNodeId ) && itemLookup.Contains( possibleNodeId.NodeId.toString() ) ) {
    ...
}
```

Alternatively, resolve the node through `itemLookup` by the cached NodeId rather than by positional
index, so the current-subset ordering is irrelevant.

---

## 6. Security User X509 — the "prevent user lockout" cleanup activation can fail an otherwise-passing negative test

`ServerProjects/Standard/maintree/Security User Token/Security User X509/Test Cases/*.js` — the
negative cases (`002`, `004`-`010`, `014`-`018`) append a second `ActivateSession` after the real
assertion, commented `// to prevent user lockout`:

```js
// to prevent user lockout
Test.Connect( { OpenSecureChannel: { ... }, SkipActivateSession: true } );
ActivateSessionHelper.Execute( {
    Session: Test.Session,
    UserIdentityToken: UaUserIdentityToken.FromUserCredentials( { ... ctt_usrT } ),
    UserTokenSignature: UaSignatureData.New( { ... ctt_usrT } ) } );   // no ServiceResult
Test.Disconnect();
```

### What the test does

The real assertion runs first (e.g. present an untrusted / expired / invalid user certificate and
require a `Bad…` rejection) and passes. The suite then logs in again with the **trusted** `ctt_usrT`
certificate purely to reset a presumed server-side account lockout, so the next negative case starts
from a clean slate.

### Why this is a CTT defect

That cleanup `ActivateSessionHelper.Execute(...)` is called **without** a `ServiceResult`
(`ExpectedAndAcceptedResults`) and **without** `SuppressErrors`. Per `library/ClassBased/UaR.js:219`,
when no expected result is supplied and the response is `Bad`, the harness raises
`addError("… ServiceResult is Bad: …")` and fails the enclosing test. So if that reset login returns
anything other than `Good` — e.g. because `ctt_usrT` has not been provisioned into the server's
trusted-user store, or because the server (correctly) does not implement lockout — the **cleanup
step fails a test whose actual assertion already passed**. A cleanup / workaround step must never be
able to change the verdict of the case it follows.

The workaround is also questionable in principle: OPC UA does **not** require a server to implement
authentication lockout (Part 4 ActivateSession defines only the per-token `Bad…` results), so a
conformance test should not assume lockout exists and should not need a "reset" login at all.

### Recommended CTT fix

Make the cleanup non-fatal — either pass `SuppressErrors: true`, or supply
`ServiceResult: new ExpectedAndAcceptedResults( [ StatusCode.Good, StatusCode.BadIdentityTokenRejected,
StatusCode.BadUserAccessDenied ] )` so a non-`Good` reset does not fail the case — and gate the reset
on the server actually advertising/implementing lockout. Better still, remove the lockout workaround
entirely and rely on the server returning the correct per-token result for each case (lockout is not
a conformance requirement).

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

* **Security User X509** (`001`, `002`, `004`, …, 27 occurrences): ActivateSession previously
  returned `BadIdentityTokenRejected`/`BadIdentityTokenInvalid`. This is **not** a CTT defect. The
  reference **server** side already validates X.509 user certificates against the `Users` trust list;
  the gap was on the **client** side of the conformance tests. In v1.6 the X.509 user-token signing
  path moved to the provider model (`UserIdentity.CreateAsync(CertificateIdentifier,
  ICertificatePasswordProvider, ICertificateProvider)`), and `X509IdentityTokenHandler.SignAsync`
  needs a resolvable private-key certificate to sign the server nonce. The conformance-test helper
  was still building a verify-only token from a transient in-memory certificate, so the client could
  not produce the user-token signature (`X509IdentityTokenHandler ... must be constructed with a
  CertificateIdentifier + ICertificateProvider to sign`) and every activation was skipped. **Fixed:**
  `X509UserIdentityHelper` now persists the transient user certificate to a client-side directory
  store and builds a signing identity through the provider path, so X.509 user-token activation now
  succeeds end-to-end against the reference server. All `SecurityX509UserTests` /
  `SecurityUserX509DepthTests` run (23 pass, 0 skipped) and assert success/rejection rather than
  skipping. No CTT-script change is warranted.

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

### Alarms & Conditions / Aggregates / GDS (Run1) — diagnosed; blocked on a CTT re-run or the CTT loop

These three clusters were investigated by direct source inspection against the OPC UA
specification. The server-side logic was found correct (or the residual is already-documented
CTT-script noise), so the remaining failures cannot be pinned to a concrete server bug from the
results XML alone — they need either a fresh CTT run (to clear cascades) or the CTT's own state
model / test-script inputs (which the XML does not carry).

* **Aggregates — 1156 `Aggregate –` errors are ~99% already-documented CTT-script defects.** 886
  are the `requestEntries` [null] `TypeError` documented in **§3** above, and 266 are the
  `CUVariables.ArrayItems` [undefined] `TypeError` documented in **§2**; one residual
  `BadUserAccessDenied` is the `Scalar_Static_Int32` RolePermissions gap already fixed server-side.
  The genuine aggregate-value comparisons (Count / PercentGood-Bad / Duration* / Start-End(Bound) /
  WorstQuality / NumberOfTransitions / AnnotationCount via `AggregateCalculator`) are **masked**
  behind that cascade and cannot be assessed until a CTT re-run that includes the RolePermissions
  fix **and** the §2/§3 CTT-script fixes. → **Blocked on a CTT re-run.**

* **Alarms & Conditions — "After Acknowledge Retain in invalid state" (14, one per alarm type) and
  "Error validating variables for state ConditionDisabled" (A&C Enable Test_002): server Retain and
  enable/disable logic verified spec-correct.** The Retain calculation was checked at every layer
  and follows **Part 9**: `ConditionState.UpdateStateAfterDisable` sets `Retain = false` on Disable
  (`ConditionState.cs:781`); `AlarmConditionState.GetRetainState` keeps `Retain = true` while
  `ActiveState.Id` is set (`AlarmConditionState.cs:346-348`); `AcknowledgeableConditionState`/
  `AlarmConditionTypeHolder`/`AcknowledgeableConditionTypeHolder` all keep `Retain = true` until the
  condition is inactive **and** acked **and** (when confirm is supported) confirmed; and the SDK
  updates Retain **before** it reports the Acknowledge event
  (`AcknowledgeableConditionState.cs:181` precedes `:189/:221`). No server-side defect was found.
  The CTT's `AlarmCollector` reports only *"Retain in invalid state"* without the expected-vs-actual
  Retain value or the exact drive sequence, so isolating the discrepancy requires the CTT loop
  (subscribe → drive Active/Ack/Confirm → observe) with the CTT's own state model. → **Blocked on
  the CTT loop** (no speculative change made to spec-correct alarm code).

* **GDS — 186 errors: 134 are the documented `CUVariables.ArrayItems` CTT bug (§2); the ~52 genuine
  ones need the CTT scripts / a stateful register→query loop.** The `GDS Application Directory` /
  `GDS Query Applications` status-code mismatches are **input-specific and mutually contradictory**
  across test cases — some expect the server to be *stricter* (`Good` → `BadInvalidArgument`: 004,
  005, 012, 032, 078), others *looser* (`BadInvalidArgument` → `Good`: 027, 060, 067, 069), and
  others a *different* code (`BadNotFound` → `BadInvalidArgument`: 029, 038, 039). The exact
  argument each numbered CTT case passes (empty/wildcard `ApplicationUri`, malformed `applicationId`,
  specific capability filters, …) is **not** carried in the results XML, so mapping each case to its
  Part 12 validation rule requires the CTT test scripts. The remaining directory/query
  count mismatches (Expected *N* got 0/*M*) and AliasName replication to the GDS are **stateful** and
  need a live register→query CTT loop. → **Blocked on the CTT scripts / loop.**


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

### Latest run (everything except security in pass 1, security in pass 2) — new findings

This run was taken after the RolePermissions / conformance / X.509-signing / cert-error fixes had
landed, so the earlier cascades are cleared and the residual clusters below are what remains.

* **Security User X509 — `ActivateSession` returns `BadUserAccessDenied` for tests 005-018 (server
  brute-force lockout, now fixed by configuration).** The first few X.509 negative cases (001-004)
  return their expected token-specific codes, but from 005 onward **every** activation — including the
  positive cases 011/013 that send a valid token and expect `Good` — returns
  `BadUserAccessDenied (0x801f0000)`. A valid token cannot be access-denied by token validation, which
  pinpoints the cause: the session manager's **brute-force lockout**
  (`SessionManager.cs`, `MaxFailedAuthenticationAttempts`, default **5**) is keyed on the *client
  application-instance certificate thumbprint* — which is identical for every case in the suite — and
  **every rejected attempt counts, even the ones the CTT deliberately expects to fail**. After five
  the client is locked out for five minutes and all remaining activations short-circuit to
  `BadUserAccessDenied`, masking the real per-token result. The lockout is a vendor hardening feature,
  **not** an OPC UA conformance requirement, so it is disabled for compliance testing by setting
  `<MaxFailedAuthenticationAttempts>0</MaxFailedAuthenticationAttempts>` in
  `Ctt.ReferenceServer.Config.xml` (production keeps the default of 5). This behaviour is proven by
  `ClientLockoutTests.ClientIsNotLockedOutWhenLockoutDisabledAsync`. *Note for the CTT team:* because
  a compliant server MAY implement account/endpoint lockout, a robust conformance harness should not
  assume that a long run of intentional authentication failures leaves the client able to
  authenticate — consider spacing the negative cases, using a distinct client instance certificate per
  case, or tolerating a lockout status on the positive cases that follow a burst of failures.

* **Security Certificate Validation — valid client certs report `BadSecurityChecksFailed` where `Good`
  is expected (037/044/051/052): certificate-trust provisioning, not a server code defect.** The
  specific-code surfacing for expired / wrong-usage certs is already fixed (see the Run2 note above:
  `TcpServerChannel` now forwards `BadCertificateTimeInvalid` / `BadCertificateUseNotAllowed`). The
  remaining cases expect `Good` for a **valid** client certificate but receive
  `BadSecurityChecksFailed` — which is exactly the code the server (deliberately, per Part 4 §7.39
  non-disclosure) returns for an **untrusted** certificate. That means the CTT's client certificates
  for these cases were not present in the server's trusted store when the security pass ran. This is a
  certificate-provisioning step in the test environment (push the CTT client certs to the server trust
  list, or run the reference server with auto-accept for the security pass), not a server bug. Confirm
  via a CTT loop once the CTT client certs are trusted.

* **Aggregates — the dominant cluster is the server-vs-CTT value comparison and needs the CTT loop to
  attribute.** With the earlier cascades cleared, the aggregate units now reach
  `HAAggregateHelper.js` `PerformAggregateCheck`, which reads the aggregate from the **server**
  (`ReadProcessedDetails`) and compares it, value by value, against the aggregate the **CTT computes
  itself** from the raw-data cache; a mismatch is reported as *"Query did not result in identical
  readings"* (`:1291`). The equality test (`equals`, `:2411`) requires the `StatusCode` **and** the
  `SourceTimestamp` to match exactly (with a 0.01 numeric tolerance on the value). The reference
  server's calculator timestamps each interval at the slice start
  (`AggregateCalculator.GetTimestamp`, spec-correct per Part 11), and the reference server's own
  history archive for the aggregated nodes is static (seeded, not mutated by the simulation timer), so
  there is no obvious non-determinism. However, the results XML only carries the CTT's `addError`
  lines — the per-value `Server Value = … / CTT Value = …` diagnostics are emitted with `print()` and
  are **not** in the XML — so which of {status, timestamp binning, value} diverges cannot be
  determined from the file alone. Attributing this cluster (server calculator vs CTT-bundled
  calculator version skew vs raw-cache alignment) requires running the CTT against the current
  reference server and capturing its live log. → **Needs the CTT loop.** (The repo's own aggregate
  tests assert only `Good`/`Uncertain` status, not exact values, so they neither confirm nor refute a
  value-level discrepancy.)

### X509-only re-run (Security User X509, 17 errors) — after the lockout fix

With the brute-force lockout disabled for compliance testing, the masking `BadUserAccessDenied` is
gone and the real per-token results surface. Of the 17 residual errors:

* **16 = user-certificate provisioning (environment, not a server or CTT-script defect).** The
  reference server validates X509 **user** identity tokens against the `Users` trust list
  (`TrustedUserCertificates` → `pki/trustedUser`); an untrusted user certificate is correctly rejected
  with `BadIdentityTokenRejected` (Part 4 ActivateSession user-token validation). On the test host the
  `pki/trustedUser` store did not even exist, so **every** X509 user certificate — including the ones
  the positive cases (`001`, `011`, `013`) mark as trusted (`ctt_usrT`, `ctt_ca1T_usrT`,
  `ctt_ca1I_usrT`), and the `ctt_usrT` login used by the `// to prevent user lockout` cleanup in the
  negative cases (see §6) — was rejected. Fix (operator step): provision the CTT's **trusted** user
  certificates into the server's trusted-user store before the run:
  * copy the trusted user leaf/CA certificates into `%LocalApplicationData%/OPC Foundation/pki/trustedUser/certs`
    (and any issuing CA into `%LocalApplicationData%/OPC Foundation/pki/issuerUser/certs`), leaving the
    deliberately-untrusted `…usrU` certificates out;
  * to make this easy, the reference server now writes every rejected X509 **user** certificate to a
    dedicated `pki/rejectedUser` review store (sibling of `pki/trustedUser`), so after one failing run
    an operator can simply move the legitimate `ctt_usrT` / `ctt_ca1T_usrT` / `ctt_ca1I_usrT`
    certificates from `pki/rejectedUser/certs` into `pki/trustedUser/certs` and re-run. The negative
    cases keep failing as required because their `…usrU` certificates stay untrusted.

* **1 = genuine server bug (018), now fixed.** `securityx509_018` presents a valid user certificate but
  builds the user-token signature with a **wrong algorithm**; the CTT accepts
  `BadUserSignatureInvalid` / `BadIdentityTokenInvalid` / `BadIdentityTokenRejected`, but the server
  returned the channel-level `BadSecurityChecksFailed`. Root cause:
  `SecurityPolicies.VerifySignatureData` (shared with the secure-channel signature checks) throws
  `BadSecurityChecksFailed` for an unexpected `SignatureData.Algorithm`, and
  `X509IdentityTokenHandler.VerifyAsync` propagated it unchanged. Fixed by mapping that
  algorithm-mismatch `BadSecurityChecksFailed` to the token-level `BadIdentityTokenInvalid` in the
  user-token verification path (Part 4), leaving the shared channel-signature behaviour untouched.
  Regression: `X509IdentityTokenHandlerTests.VerifyWithMismatchedSignatureAlgorithmYieldsBadIdentityTokenInvalid`.

### Aggregates-only re-run (6016 errors) — server advertised the wrong aggregate defaults (now fixed)

With the CTT console log (`ctt output.txt`) the per-value divergence became visible: the CTT reads each
aggregate from the server (`ReadProcessed`) and compares it to the aggregate it computes itself from the
raw data it read from the same server (the reference server's all-Good ramp). SourceTimestamps match
exactly; the disagreement is the **quality of partial intervals**. For an interval where part of the
window has *no data* (e.g. the first interval, before the first raw sample), the server returns a
partial value with **`UncertainDataSubNormal`** while the CTT expects **`Bad`/`Null`**. Both set the
`Partial` bit — they agree it is partial, they disagree on severity.

* **Root cause was a server bug, now fixed — not a CTT defect.** The `Aggregate - Base 001-01` unit
  (dominant *"Query did not result in identical readings"*) uses `UseServerCapabilitiesDefaults=TRUE`, so
  the CTT reads the server's advertised aggregate defaults from the node's
  `HistoricalDataConfiguration` → `AggregateConfiguration` object and computes with them. The reference
  server *computes* with `PercentDataGood=100 / PercentDataBad=100 / TreatUncertainAsBad=true`
  (`AggregateManager` / Part 13 v1.05.07 §4.2.1.2), under which a partial interval is **Uncertain** — but
  `HistoricalDataConfigurationInstaller.PopulateProperties` materialised the mandatory
  `AggregateConfiguration` child **without populating it**, leaving it at the type's all-zero defaults
  (`PercentDataGood=0 / PercentDataBad=0 / TreatUncertainAsBad=false`). Those are not only inconsistent
  with what the server computes with, they are an invalid `AggregateConfiguration`
  (`PercentDataGood < 100 - PercentDataBad`). A CTT reading `0/0/false` classifies every partial
  interval as **Bad** while the server returns **Uncertain** — the exact systematic divergence seen
  across every aggregate and node. **Fixed** by populating the `AggregateConfiguration` object from the
  server's defaults (`HistorianNodeCapabilities.DefaultAggregateConfiguration`, defaulting to
  `100/100/true/false`) in the installer, so a `UseServerCapabilitiesDefaults` client now reads the same
  configuration the server computes with. A latent client-side bug was fixed alongside:
  `HistoryClient.GetConfigurationAsync` resolved the companion object via `HasAddIn` instead of
  `HasHistoricalConfiguration` (Part 11 §5.2.3) and never read `AggregateConfiguration`. Regression:
  `HistoryClientIntegrationTests.GetConfigurationReturnsHistoricalDataConfigurationAsync` (now asserts
  the advertised `PercentDataGood/PercentDataBad=100`, `TreatUncertainAsBad=true`).

* **CTT-script defects still present (documented previously):** `possibleNodeId [undefined]` (100, §5)
  and `GetRequestEntry failed due to incorrect test configuration` (74). The latter is the CTT aborting
  a unit when a cached raw-data request sub-entry it expects (`StartEntry` / `EndEntry` / `BadDataEntry`)
  is missing from `Test.AggregateTestData.RawDataCache`; it reflects the CTT's own test-data cache
  bootstrap, not a server response, and the harness should skip (`addSkipped`) rather than `throw` so a
  single missing sub-entry does not abort the remaining aggregate cases.

### Alarms & Conditions re-run (152 errors) — findings from the console log (`ctt output 1.txt`)

* **`After Acknowledge Retain in invalid state` (42): server Retain logic is spec-correct; the failures
  reflect outstanding condition *branches*.** The console log shows
  `ValidateRetain failed retain=true, ActiveState=false, AckedState=true, ConfirmedState=Confirmed`.
  The core `GetRetainState` chain (`AlarmConditionState.cs:338`, `AcknowledgeableConditionState.cs:548`,
  `ConditionState.cs:329`) returns `false` for an inactive, acknowledged **and** confirmed condition
  *when it has no retaining branches* — and `OnConfirmCalled`/`OnAcknowledgeCalled` call
  `UpdateRetainState()` **before** `ReportStateChange`, so the reported event carries the post-transition
  Retain. A `Retain=true` in that state therefore means an outstanding **branch** is still retained
  (Part 9 §5.8.2 / §5.7.3: when an active alarm returns to inactive before being acknowledged, the
  Server creates a branch to track the acknowledgement of that prior active occurrence, and the main
  Condition retains while any branch retains). The CTT's `AlarmCollector::ValidateRetain` evaluates only
  the current (main) event's `ActiveState`/`AckedState`/`ConfirmedState` and does not account for a
  branch whose own event has not yet been acknowledged/confirmed, so it flags a spec-correct
  `Retain=true` as invalid. **Recommended CTT fix:** before asserting `Retain=false`, verify there is no
  outstanding branch (e.g. acknowledge/confirm every `ConditionBranchId != null` event first, or treat
  `Retain=true` as valid while any branch event is unacknowledged).

* **`Error validating variables for state ConditionDisabled` (60) and `Unable to read input node`
  (42):** these need a live A&C loop to attribute conclusively; the console log shows the disabling
  transition (`Disabling alarm … / Disabled State ConditionDisabled`) and the input-node read
  (`Test_004.js:43`) failing at the **service** level (`readResult` falsy). Verify the reference
  server's alarm source variable (`ns=7;s=Alarms.AnalogSource`, `Applications/Quickstarts.Servers/Alarms/
  AlarmNodeManager.cs`) is readable and that the disabled-condition event fields match Part 9 §5.5.2.
  Not pinned to a concrete server defect from the log alone.

### AliasName re-run (12 errors) — reference server is FindAlias-only; one CTT-script defect

* **The reference server serves aliases via `FindAlias` (store) but does not materialise browsable
  `AliasNameType` instance nodes under the categories.** `ReferenceServer.ConfigureAliasNameStore`
  registers an `InMemoryAliasNameStore` and seeds tag/topic aliases (`TICN_Setpoint`, `FICN_Flow`,
  `ServerEvents`, `AuditEvents`, …), so `FindAlias`/`FindAliasVerbose` return them (Part 17 §6.3.2, the
  scalable mechanism intended for large alias sets). The CTT conformance units *AliasName Category Tags
  / Topics / Hierarchy* instead **browse** each category (recursively, via `HierarchicalReferences`) for
  `AliasNameType` instance nodes (`GetAliasNamesFromCategories`) and then assert every `FindAlias`
  result corresponds to a browsed instance — producing `No instance of AliasNameType found under
  TagVariables/Topics` and `… AliasName (…) is not part of the current category`. Whether this is a
  server gap or a CTT over-restriction depends on the claimed Part 17 profile: if the server advertises
  only *Base/FindAlias* support, `FindAlias`-only with no per-alias instance nodes is compliant and the
  CTT should not require browsable instances; if the *Category/Hierarchy* browse profile is claimed, the
  reference server must additionally materialise an `AliasNameType` instance node per alias under its
  category. **Recommended:** either (server) expose `AliasNameType` instances for the seeded aliases, or
  (CTT) gate the "returned alias must be a browsed instance" assertion on the server advertising the
  browsable-instance profile rather than on `FindAlias` returning results.

* **CTT-script defect — `AliasName Hierarchy/002.js:80` references an undefined variable.** After the
  per-alias loop the success branch reads `TC_Variables.ListOfNodes.length`, but `ListOfNodes` is never
  assigned in this test (the results were stored in `TC_Variables.OutputArguments`), raising
  `Result of expression 'TC_Variables.ListOfNodes' [undefined] is not an object`. **Recommended CTT
  fix:** use `TC_Variables.OutputArguments.length` (the array actually populated at line 37), or track a
  running count of returned aliases.



