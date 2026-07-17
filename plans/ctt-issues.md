# CTT (Compliance Test Tool) conformance findings

This document records CTT script defects and server-side findings discovered while
working through issue #3960 against the OPC Foundation .NET reference server. Each
entry classifies the observed behavior, records the available evidence, and identifies
the appropriate server or CTT corrective action.

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

## 4. Multi-dimensional array (Matrix) — invalid server Variant poisoned batched CTT reads

**Tests (all multi-dimensional-array cases):**
- `maintree/Attribute Services/Attribute Read/Test Cases/030.js` — read a multi-dim array Value
- `maintree/Attribute Services/Attribute Write Index/Test Cases/007.js` — write one index of a multi-dim array
- `maintree/Attribute Services/Attribute Write Values/Test Cases/020.js` — write an entire multi-dim array
- `maintree/Monitored Item Services/Monitor Basic/Test Cases/039.js` — monitor a multi-dim array
- `maintree/Monitored Item Services/Monitor Value Change V2/Test Cases/042.js` — monitor a multi-dim array with IndexRange

**Observed error:** *"Expected: Good (0x00000000) but received: BadDecodingError (0x80070000)"*
(`library/Base/assertions.js:386`). The read-side failures were initially attributed to the CTT
decoder. That conclusion was incorrect: the reference server returned an invalid copied Value for
`ns=2;s=Scalar_Static_Arrays2D_Variant`, and the CTT read these configured matrix nodes in a batch.
The one invalid Variant made the encoded response undecodable and therefore hid the otherwise good
neighboring results.

### Run 12 Attribute Services evidence

`D:\git\NewCTT2.results 12.xml` (CTT script `1.05.513`, specification `1.05.006`) contains
exactly **96 Error nodes**:

The installed scripts were inspected under
`C:\Program Files\OPC Foundation\UA 1.05\Compliance Test Tool\ServerProjects\Standard\maintree\Attribute Services`.

| Failure class | Count | Tests and interpretation |
|---|---:|---|
| `BadDecodingError` | 14 | Read `030`-`034`; Write Index `007`-`010`; Write Values `020`/`021`. The batched read failures are explained by the invalid server `Scalar_Static_Arrays2D_Variant`. The write-side entries require a rerun or independent wire capture; the result code alone does not prove a CTT encoder defect. |
| `BadTypeMismatch` | 40 | 19 whole-matrix writes in each of Write Values `020` and `021`, plus one indexed write in each of Write Index `008` and `009`. These results must be re-evaluated after the server fix and are no longer attributed to CTT matrix construction without payload evidence. |
| Write Index `010` follow-on assertions | 38 | 19 `BadIndexRangeInvalid` checks plus 19 "SourceTimestamp not set" checks. The test continues to construct/read ranges after its initial matrix `BadDecodingError`; the timestamp checks are secondary because Part 4 §5.11.2.2 only associates an Attribute Value with a successful operation result. |
| CTT JavaScript exceptions | 4 | Attribute Read `026`/`036` reject configured `StatusCode` arrays; Read `034` indexes `Dimensions[-1]`; Write Index `007` dereferences an undefined decoded matrix. |

Per-test Error-node totals are: Read `026` (1), `030` (1), `031` (1), `032` (1),
`033` (1), `034` (2), `036` (1); Write Index `007` (2), `008` (3), `009` (3),
`010` (39); Write Values `020` (20), `021` (21).

The counts reconcile exactly: **14 + 40 + 38 + 4 = 96**. In particular, the 19 missing
timestamp messages in Write Index `010` are not independent server timestamp defects. Part 4
§7.27 says `Bad_IndexRangeInvalid` is reserved for invalid `NumericRange` syntax; the installed
script continues after the preceding matrix decode failure and derives the next range from
unusable matrix metadata. A fixed literal valid range is covered by the in-process server proof
below.

### Root cause: the server corrupted a Variant matrix while copying the read value

The node was initialized with a valid `MatrixOf<Variant>`. However, `Variant.Copy()` copied matrices
whose BuiltInType was `Variant`, `Number`, `Integer`, or `UInteger` through `GetVariantArray()`.
For a matrix TypeInfo that array accessor returns a null `ArrayOf<Variant>`, so the copy retained a
rank-2 TypeInfo but no longer contained a shaped `MatrixOf<Variant>`.

Before the fix, binary encoding that corrupted copy produced:

```
D8                          EncodingMask = Variant(24) | Array(0x80) | ArrayDimensions(0x40)
FF FF FF FF                 ArrayLength = -1 (null flattened array)
01 00 00 00                 ArrayDimensionsLength = 1
00 00 00 00                 ArrayDimensions = [0]
```

This is not a valid matrix encoding: dimensions are present but the rank is less than 2, the
dimension is not greater than zero, and its product cannot equal `ArrayLength = -1`. Per
**OPC UA Part 6 §5.2.2.16, Table 26 (Variant Binary DataEncoding)** a multi-dimensional array
Variant is encoded, in this order:

1. `EncodingMask` (Byte) — bits 0:5 = BuiltInTypeId, **bit 6 = ArrayDimensions present**, **bit 7 = array**.
2. `ArrayLength` (Int32) — the **total** element count of the flattened array.
3. `Value` — the flattened array, **higher-rank dimensions serialized first**.
4. `ArrayDimensionsLength` (Int32) — number of dimensions.
5. `ArrayDimensions` (Int32[]) — each dimension, **lower-rank dimension first**.

The spec also states (Table 26, `ArrayDimensions` row): *"If ArrayDimensions are inconsistent
with the ArrayLength then the decoder shall stop and raise a **Bad_DecodingError**."* — i.e.
`BadDecodingError` is the mandated decoder behaviour for an inconsistent matrix. The CTT was
therefore correct to reject the invalid server response.

For comparison, encoding of the valid 2×3 `Int32` matrix
`{{1,2,3},{4,5,6}}` on the wire is:

```
C6                          EncodingMask = Int32(6) | Array(0x80) | ArrayDimensions(0x40)
06 00 00 00                 ArrayLength   = 6            (== 2×3, consistent)
01·02·03·04·05·06 (Int32)   Value         = 1..6         (flattened, higher-rank-first)
02 00 00 00                 ArrayDimensionsLength = 2
02 00 00 00  03 00 00 00    ArrayDimensions       = [2,3] (lower-rank-first; product == ArrayLength)
```

Every field in that golden vector matches Table 26. It proves the ordinary `Int32` matrix path, but
it does **not** prove that the old `Scalar_Static_Arrays2D_Variant` response was valid and it does
not prove that the CTT encoder or decoder is symmetrically wrong.

### Corrective action and regression proof

The server fix changes `Variant.Copy()` to use `GetVariantMatrix()` for `Variant`, `Number`,
`Integer`, and `UInteger` matrices, preserving both the flattened values and dimensions. Matrix
codec validation is also hardened so encoders reject invalid shapes and decoders consistently
report `BadDecodingError` for malformed dimensions.

The repository locks the corrected behavior down with deterministic proof:

- `BinaryEncoderTests.WriteVariantWithInt32MatrixMatchesPart6Table26` compares the complete
  encoded 2x3 `Int32` Variant to the 41-byte Table 26 golden vector above.
- `VariantCoverageTests.CopyPreservesVariantMatrixShape` verifies that copying a
  `MatrixOf<Variant>` preserves its rank, dimensions, count, and values.
- `VariantMatrixConformanceTests` exercises binary, JSON, and both XML decoders with a valid 2x3
  matrix, null and zero-length arrays, invalid rank-1 dimensions, a zero dimension, and a
  dimension-product mismatch.
- `ReferenceServerTests.VariantMatrixDoesNotPoisonBatchedReadEncodingAsync` reads the Variant
  matrix between two valid matrix nodes, binary-encodes and decodes the full result array, and
  verifies that all three results remain good.
- `ReferenceServerTests.MatrixReadWriteAndNumericRangeAsync` writes and reads a deterministic
  3x3 `Int32` matrix in-process, reads the valid Part 4 §7.27 range `1,0:2`, writes the matching
  `1,1:2` slice, and verifies the updated row.

The CTT matrix cases must be rerun against the corrected server. Any remaining CTT codec claim
requires separate proof from a captured payload or an isolated CTT codec test; the Run 12 status
codes are not sufficient evidence.

### CTT script defect: Write Index `007.js` dereferences a failed matrix decode

Installed script:
`maintree/Attribute Services/Attribute Write Index/Test Cases/007.js`, lines 38-44.
After the batch Value read reports `BadDecodingError`, line 41 calls `getMatrixValues` before
checking `Results[i].StatusCode`; the skip message then evaluates
`MDArrays[i].Value.Value[1].length`. Run 12 consequently records:

> `Result of expression 'TC_Variables.MDArrays[i].Value.Value[1]' [undefined] is not an object`

This is an unsafe error path in the test, not a second server failure. The exact script fix is:

1. Check `ReadHelper.Response.Results[i].StatusCode` immediately after the read and remove/skip
   the item before calling `getMatrixValues`.
2. After a good result, require a defined `Dimensions` array with at least two entries and
   `Dimensions[1] >= 2` (the test selects index 1).
3. Report the guarded `Dimensions[1]`; never inspect `Value.Value[1]` in the failure message.

This follows Part 4 §5.11.2.2: a Value is available when the per-operation StatusCode indicates
success. It also preserves the Part 6 §5.2.2.16 requirement that a matrix dimension mismatch
terminates decoding with `Bad_DecodingError`.

### CTT script defect: Attribute Read `026.js`/`036.js` omit `StatusCode` array support

Both tests iterate every configured array setting, including the `StatusCode` node for which
`UaNodeId.GuessType(...)` returns BuiltInType Id **19**. Run 12 records the same JavaScript
exception in both tests:

> `Built in type not specified or detectable within the parameter: StatusCode (19)`

There are two missing helper paths:

- `026.js` line 12 calls `generateArrayWriteValue(...)` even though this is a read test.
  `library/Base/indexRangeRelatedUtilities.js` has no `BuiltInType.StatusCode` case in either
  `getWriteValues` or `generateArrayWriteValue`.
- `036.js` line 24 calls `GetArrayTypeToNativeType(...)`.
  `library/Base/UaVariantToSimpleType.js` has no `BuiltInType.StatusCode` branch.

Recommended CTT changes:

1. Remove the unused `item.Value.Value = generateArrayWriteValue(...)` assignment from `026.js`.
2. For callers that do need generated StatusCode arrays, create a `UaStatusCodes`, populate it
   with `UaStatusCode` values, and call `UaVariant.setStatusCodeArray(...)`.
3. Add `case BuiltInType.StatusCode: returnValue = uaValue.toStatusCodeArray();` to
   `GetArrayTypeToNativeType`.

Part 6 §5.1.9 permits Variants containing arrays of any built-in type, and Part 6 §5.2.2.11
defines `StatusCode` as a built-in UInt32 encoding. A generic array test must therefore either
support BuiltInType 19 or explicitly exclude that configured node.

### CTT script defect: Attribute Read `034.js` uses a negative dimension index after decode failure

Line 40 calls `getMatrixValues` before checking the operation StatusCode. The next expression
uses `Dimensions[Dimensions.length - 1]`; after the matrix `BadDecodingError`, the dimensions are
empty and the CTT throws `CttInt32s: Trying to access element -1`. Check the StatusCode first,
then require a non-empty defined `Dimensions` collection before calculating the last-dimension
index.

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

### Aggregates-only re-run 11 (5576 errors) — additional server bugs now fixed

The aggregate-default advertisement fix reduced the total from 6016 to 5576 and changed several
families materially (for example Count 180 → 60 and Average 90 → 36), but the new console log
(`ctt output 2.txt`) exposed additional independent calculator and HistoryRead routing defects.
These are **server fixes**, not CTT issues:

* **Equal StartTime and EndTime returned Good instead of `BadInvalidArgument`.** The CTT Base
  `001-01` (single node) and `002-01` (multiple nodes) cases use `StartTime == EndTime`. Part 11
  §6.5.4.2 states that the Server shall return `Bad_InvalidArgument` because there is no meaningful
  interpretation of the request. The server previously calculated and returned per-node Good results.
  `HistorianDispatcher.DispatchProcessedReadAsync` now returns per-node `BadInvalidArgument` while the
  HistoryRead service call itself succeeds, matching the CTT and Part 11. Regression coverage includes
  multi-node routing and ensures reverse ranges are not accidentally rejected.

* **Reverse processed reads fed raw values in forward order.** The dispatcher normalised the requested
  time range but always set `HistorianReadRequest.IsForward = true`. For `StartTime > EndTime`, Part 11
  §6.5.4.2 requires data in reverse order; the backward calculator consequently saw the wrong stream
  order and produced `BadNoData`, 1 ms durations, and incorrect bounds. `IsForward` now follows the
  request direction.

* **Exact reverse interpolation bounds were replaced by synthesized values.** When a raw value landed
  exactly on an interpolated boundary, the reverse slice could miss it and interpolate/extrapolate
  instead. `AggregateCalculator.Interpolate` now preserves an exact **non-Bad** raw point (Good or
  Uncertain) regardless of `TreatUncertainAsBad`; an exact Bad point remains excluded from interpolation
  as required. Forward/reverse Good, Uncertain, and Bad boundary tests assert exact value, StatusCode,
  aggregate bits, and timestamp.

* **PercentGood/PercentBad ignored `TreatUncertainAsBad`.** Part 13 §5.4.3.2.1 requires Uncertain
  regions to contribute to Good when `TreatUncertainAsBad=false`, and to Bad when it is true. The status
  aggregate calculator previously counted only native Good/Bad regions, producing the inverted 100/0
  versus 0/100 comparisons visible in the log. It now applies the explicit configuration.

* **WorstQuality/WorstQuality2 mishandled eligible values and StatusCodes.** WorstQuality now preserves
  the first StatusCode at the worst severity (for example `GoodClamped`, rather than collapsing it to
  plain Good) and sets `MultipleValues` for repeated Good worst-quality values. WorstQuality2 now uses
  the request start bound plus in-domain raw values and excludes the end bound, per Part 13
  §5.4.3.35-.36; the excluded end bound can no longer spuriously set `MultipleValues`.

* **Reverse Minimum/Maximum used the chronological lower bound to decide Raw versus Calculated.** For a
  reverse interval the request-direction start is the later timestamp. Min/Max and Min/Max2 now compare
  the selected raw sample to `GetTimestamp(slice)` (the returned interval timestamp), so a value at the
  reverse interval start is marked Raw as required by Part 13 §5.4.3.10-.11. ActualTime variants retain
  the selected sample timestamp.

The fixes are covered by direct calculator and live in-memory historian Part 11/13 oracle tests for
forward/reverse ten-interval requests, one-interval ranges, Start/End/StartBound/EndBound,
PercentGood/Bad, WorstQuality/2, DurationInState, Min/Max/Range/TimeAverage, and equal-time multi-node
validation. Residual CTT comparisons involving string-status aggregates, integer EndBound conversion,
or Boolean duration calculations require the next focused CTT run before either side is classified;
no speculative compatibility changes were made.

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
  server's alarm source variable (`ns=7;s=Alarms.AnalogSource`, `samples/Quickstarts.Servers/Alarms/
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

## 7. Aggregate `Err-004.js` creates an unintended equal-time request when ProcessingInterval is blank

**Tests:** every Aggregate Conformance Unit reuses
`maintree/Aggregates/Aggregate - Base/Test Cases/Err-004.js`.

**Observed in run 14:** 552 errors:

- 276 generic `HistoryRead.js` / `assertions.js` errors reporting per-node
  `BadInvalidArgument`;
- 276 explicit `HAAggregateHelper.js:1764` errors rejecting the same
  `BadInvalidArgument`.

The normal Base `001-01` / `002-01` comparisons correctly accept this response (the console log
contains hundreds of `Server and CTT have status codes of BadInvalidArgument` confirmations).
Only `Err-004.js` rejects it.

### Why this is a CTT/configuration defect

`Err-004.js:25` calls `PerformExpectedErrorTest`. The helper constructs the request range from the
configured Aggregate `ProcessingInterval`, but the run's CTT configuration leaves that setting blank.
JavaScript coerces the blank value multiplied by ten to zero, so the helper sends
`StartTime == EndTime`.

OPC UA Part 11 §6.5.4.2 is explicit: when `StartTime` and `EndTime` are equal, the Server shall
return `Bad_InvalidArgument` because the request has no meaningful processed time domain. Per Part 4
§5.11.3.2, this is the per-node operation result while the HistoryRead ServiceResult remains Good.
The reference server therefore returns the required result; the error test accidentally combines its
intended invalid condition with a second invalid condition and then rejects the mandated status.

### Recommended CTT fix

Validate the setting before building the request in `HAAggregateHelper.js`:

```js
var interval = parseInt(
    Settings.ServerTest.NodeIds.Static.HAProfile.Aggregates.ProcessingInterval);
if (isNaN(interval) || interval <= 0) {
    interval = 1;
}
```

Alternatively, skip the test with a clear configuration error when a positive interval is missing.
Apply the same guard to `PerformMismatchTest`. The immediate configuration workaround is to set
Aggregate `ProcessingInterval` to a positive value.

## 8. Historical Access Read Raw scripts contain independent result-validation defects

Run 15 exposed five CTT script defects alongside genuine server raw-history defects (the server
ordering/bounds/paging/continuation fixes are described below).

### `004.js` rejects correct reverse ordering

At lines 78, 91, and 105 the test uses:

```js
if (OPCF.HA.Analysis.Date.FlowsBackward(...)) result = false;
```

Those branches are specifically validating reverse reads, so `FlowsBackward(...) == true` is success,
not failure. Replace each predicate with:

```js
if (!OPCF.HA.Analysis.Date.FlowsBackward(...)) result = false;
```

Part 11 §6.5.3.2 requires raw values to be returned in the direction implied by StartTime/EndTime.

### `014.js` indexes a nonexistent second node result

The test requests one node but lines 46 and 78 inspect `Response.Results[1]`. The intended check is the
second returned `DataValue` for the first node. Validate
`haItems[0].Value[1].StatusCode` (with length guards) and describe it as record 2, not result 2.

### `019.js` bypasses the CTT test harness

The script invokes `readraw019()` directly while the normal `Test.Execute` wrapper is commented out.
Use:

```js
Test.Execute({ Procedure: readraw019 });
```

This ensures exceptions, result accounting, setup, and cleanup follow the same path as the other
Historical Access cases.

### `Err-013.js` describes an operation error as a ServiceResult

Reusing a consumed ContinuationPoint shall produce per-node
`BadContinuationPointInvalid`; the HistoryRead ServiceResult remains Good. Update the message to:

> HistoryRead test #3 expected a Good ServiceResult and
> `Results[0].StatusCode` `BadContinuationPointInvalid`.

This matches Part 11 §6.3 and Part 4 §5.11.3.2.

### `Err-019.js` uses an undefined loop variable in error messages

Lines 25 and 43 interpolate undeclared `i`. Use literal test numbers `#1` and `#2` (or define a
proper case index) so a failed assertion reports the actual case instead of throwing another
JavaScript error.

### Aggregate run 14 residual value findings (not additional server fixes)

Focused direct-calculator and live-historian Part 13 oracle tests reproduce the reference server's
actual seed data: an all-Good linear ramp (`value = sample index`, 10 s spacing). They establish:

- Float/Double sloped Interpolative, StartBound, and EndBound results match the Part 13 linear oracle.
- Int32 interpolation differs from the CTT by exactly one where the mathematical value is fractional:
  the stack converts with `Convert.ToInt32` (round-to-nearest, unchanged from 1.5.378), while the CTT
  truncates. Part 13 §5.4.3.2.2 does not mandate integer truncation, so the CTT must not require that
  conversion convention; compare the mathematical result using the source type's documented rounding
  policy or accept either conforming conversion.
- DurationGood/Bad, PercentGood/Bad, WorstQuality2, and DurationInState results over the configured
  reference data are necessarily all-Good/full-duration. The CTT log warns that no start of Bad data
  is configured but still compares against an oracle that assumes a Bad-data region. Configure the
  required Bad/Uncertain test region before running those cases, or skip them when the prerequisite is
  absent.
- Small TimeAverage/Total/NumberOfTransitions boundary-convention deltas remain unclassified. No CTT
  issue or server compatibility change is claimed without a normative oracle.

### Aggregate run 16 regression (server fix, not a new CTT defect)

Run 16 increased from 3270 to 4126 errors without changing the CTT execution topology, scripts,
configuration warnings, or tested node set. The +856 delta mapped exactly to tests `005-01`,
`005-02`, `005-03`, `002-02`, and `002-03` across the aggregate families.

The regression was introduced by the run-15 raw-history bounds fix. The in-memory historian correctly
synthesizes `BadBoundNotFound` `DataValue`s to represent missing FIRST/LAST raw bounds (Part 11 §4.6),
but the normal processed-read path queued those protocol markers into `IAggregateCalculator` as if
they were archived Bad data. The AtTime collector already excluded the same synthetic status.

Part 11 and Part 13 treat `BadBoundNotFound` as a missing-bound marker, not aggregate input. Feeding it
into the calculator changed first/last partial intervals across every family: values became
`BadNoData`, Percent/Duration calculations counted a false Bad region, WorstQuality returned
`BadBoundNotFound`, and numeric aggregates lost the expected Good+Partial status.

The server now ignores both `BadNoData` and `BadBoundNotFound` placeholders in
`AggregateCalculator.QueueRawValue`. Direct and live historian regression tests cover
DurationGood, PercentGood, WorstQuality2, and StartBound with a synthesized missing leading bound.
This restores run-14 behavior while preserving the raw HistoryRead FIRST/LAST values. No new CTT
issue is logged for the run-14 → run-16 increments; the pre-existing CTT issues above remain.

### Historical Access run 15 server defects now fixed

The server fixes (not CTT issues) cover:

- correct start-only forward and end-only reverse reads;
- exact equal-time raw reads;
- exact versus outside bounding values, including synthetic `BadBoundNotFound` FIRST/LAST values at
  the requested timestamps;
- bounds counting toward `NumValuesPerNode` and continuation-point paging of a trailing bound;
- no residual ContinuationPoint for a completed open-ended N-value request;
- fresh single-use ContinuationPoint identifiers after every resume;
- `BadInvalidArgument` for modified reads with `ReturnBounds=true`;
- exclusion of synthetic `BadBoundNotFound` protocol markers from AtTime interpolation input.

These behaviors are covered by historian provider/dispatcher and end-to-end tests on net10 and net48.

## 9. Node Management AddNodes — invalid reference and requested-NodeId CTT configuration

Run 18 (`NewCTT2.results 18.xml`) contains 15 AddNodes failures, all of which are CTT project/script
configuration defects; no server defect remains. The three server validation gaps exposed by run 17
(`NewCTT2.results 17 1.xml`, 17 failures) were fixed in `dd66b88f9` and are confirmed resolved in
run 18: `Err-005.js`, `Err-006.js`, and `Err-007.js` now pass.

### `002.js` enables references that cannot add a Variable under the configured parent

`Node Management Add Node/Test Cases/002.js` loops every ReferenceType enabled under:

`/Server Test/NodeIds/NodeManagement/SupportedReferences`

and always adds a **Variable**, expecting `Good`. The project enabled all candidates. The 14 reported
`BadReferenceNotAllowed` results correspond to the 13 non-hierarchical references plus `HasSubtype`:

`HasModellingRule`, `HasEncoding`, `HasDescription`, `HasTypeDefinition`, `GeneratesEvent`,
`AlwaysGeneratesEvent`, `FromState`, `HasCause`, `HasEffect`, `HasSubStateMachine`,
`HasTrueSubState`, `HasFalseSubState`, `HasCondition`, and `HasSubtype`.

OPC UA Part 4 §5.8.2 requires the new Node to be the target of a **HierarchicalReference**.
`BadReferenceNotAllowed` is therefore correct. `HasSubtype` is hierarchical but is a type-system
reference whose source and target must be compatible Type Nodes; the server now correctly rejects it
for the Variable instance created by this script.

**Recommended CTT project fix:** restrict the configured set to reference types compatible with the
configured parent and a Variable target—typically `Organizes`, `HasProperty`, and `HasComponent`.
Do not mark every known ReferenceType as supported merely because the server supports that
ReferenceType elsewhere in its information model.

### `Err-008.js` tests duplicate NodeIds while client-specified NodeIds are disabled

`Err-008.js` sends the same AddNodes item twice and expects the second call to return
`BadNodeIdExists`. In this project `/NodeManagement/RequestedNodeId` is disabled, so
`CUVariables.RequestedNewNodeId()` returns a null NodeId. Each call legitimately asks the server to
allocate a fresh NodeId; the second `Good` result represents a different node and is spec-correct.

**Recommended CTT fix:** skip `Err-008.js` when client-specified NodeIds are disabled, or enable the
setting and configure a concrete NodeId in a writable namespace owned by a NodeManager that supports
NodeManagement before testing duplication.

### Server defects fixed in `dd66b88f9` (verified in run 18)

The server fixes (not CTT issues) are:

- return `BadNodeIdRejected`, rather than `BadUserAccessDenied`, when a client-requested NodeId targets
  a namespace where the Server does not allow client-specified NodeIds (Part 4 §5.8.2);
- validate hierarchical ReferenceType source/target NodeClass constraints, rejecting invalid
  `HasSubtype` instance relationships with `BadReferenceNotAllowed`;
- retain successful behavior for valid instance-hierarchy references and requested NodeIds in owned,
  opted-in namespaces.

These paths are covered by MasterNodeManager and AsyncCustomNodeManager tests on net10 and net48.

## 10. Run 18 Historical Access and Attribute script/configuration defects

### Historical Access `012.js` expects `BadIndexRangeNoData` at the wrong level

The test reads historized array values with a syntactically valid IndexRange that is outside the
array bounds. The server returns:

- `HistoryReadResult.StatusCode = Good`;
- each returned `DataValue.StatusCode = BadIndexRangeNoData`.

`012.js` instead expects `Results[0].StatusCode = BadIndexRangeNoData`, producing two errors.
OPC UA Part 11 §6.4 applies the IndexRange independently to each historical value; the HistoryRead
operation succeeds while values for which no indexed data exists carry `BadIndexRangeNoData`.

**Recommended CTT fix:** require the per-node result to be Good, decode `HistoryData`, and assert
`BadIndexRangeNoData` on each affected `DataValue.StatusCode`.

### Historical Access `Err-012.js` uses a non-historizing node for an access-denied test

The configured node does not support history, so the server returns
`BadHistoryOperationUnsupported` before any history authorization check can produce
`BadUserAccessDenied`.

**Recommended CTT project fix:** configure a node that is historizing and readable by an authorized
identity but explicitly denies HistoryRead to the identity used by this case. A test cannot validate
access denial with a node that has no supported history operation.

### Attribute array helpers omit `NodeId[]` conversion

Run 18 records the same JavaScript exception for:

- Attribute Read `032.js`;
- Attribute Read `034.js`;
- Attribute Write Index `007.js`.

`UaNodeId.GuessType(...)` correctly identifies BuiltInType `NodeId (17)`, but the generic CTT array
conversion/generation helper has no NodeId branch and throws:

> Built in type not specified or detectable within the parameter: NodeId (17)

**Recommended CTT fix:** add NodeId-array support to both directions:

- decode with the appropriate `toNodeIdArray()` accessor;
- generate/populate a `UaNodeIds` collection and set it with the NodeId-array Variant setter.

As with the existing StatusCode-array defect (`026.js`/`036.js`), a generic built-in array test must
support every configured built-in type or explicitly exclude unsupported types before executing.

### Run 18 server defect now fixed

Historical Access `Err-010.js` supplied `" 1:3"` (leading whitespace). The previous NumericRange
parser accepted whitespace through `Convert.ToInt32`, returning Good instead of
`BadIndexRangeInvalid`. NumericRange parsing now uses strict invariant integer syntax
(`NumberStyles.None`) and rejects leading, trailing, and embedded whitespace. NumericRange and
HistoryRead regressions cover the fix.

## 11. Run 19 Base Information model and diagnostics defects

### The Core Structure walker treats permission-hidden Mandatory children as absent

Run 19 reports Mandatory children missing from the standard Role, RoleSet, UserManagement, ServerConfiguration, and ServerDiagnostics instances. The affected children include `RoleType.Identities`, `RoleSetType.AddRole`/`RemoveRole`, `UserManagementType.Users` and its methods, `ServerConfigurationType.UpdateCertificate`/`ApplyChanges`/`CreateSigningRequest`/`GetRejectedList`, and `ServerDiagnosticsType.ServerDiagnosticsSummary`/`SubscriptionDiagnosticsArray`.

The children are present at their standard NodeIds and retain their Mandatory modelling rules. The current OPC UA NodeSet assigns restrictive RolePermissions to these administrative and diagnostics surfaces, primarily SecurityAdmin. A low-privilege Browse therefore omits the references or returns `BadUserAccessDenied`, but the CTT model walker interprets that permission-filtered result as proof that the server failed to instantiate the Mandatory declaration.

This is a CTT defect. OPC UA Part 3 access control requires Browse results to be filtered by the session's permissions; a model-conformance walker cannot distinguish an absent node from a permission-hidden node while running as an identity that is not allowed to browse it.

**Recommended CTT fix:** run structural model validation with a SecurityAdmin identity over an encrypted channel, or make the walker permission-aware and retry restricted parents with an appropriately privileged session before reporting a missing Mandatory child. The repository CTT project now selects the username policy, uses `sysadmin`/`demo`, and requests SignAndEncrypt with Basic256Sha256 for its main session so the current scripts satisfy both RolePermissions and AccessRestrictions without weakening the server security model.

The regression work also found and fixed two separate dynamic-instantiation defects. Generated `CreateInstanceOf<Type>` and optional-child factories now support both path-based NodeId factories and factories that allocate only for null NodeIds, assign distinct roots and descendants where the factory supports allocation, and remap internal references from declaration IDs to the instance IDs; predefined concrete-instance factories remain unchanged. Runtime-created custom roles also used a request context that was not guaranteed to carry a NodeIdFactory and therefore reused type-declaration NodeIds for `Identities` and optional descendants. Dynamic roles now use generated metadata-preserving adders plus a role-specific path NodeId factory, keeping child identifiers disjoint from RoleManager's numeric root identifiers. Neither defect caused run 19 because the reported well-known roles are predefined standard instances.

### The Core Structure comparison uses a UA 1.04 reference model for a UA 1.05 server

The run identifies the server as UA 1.05.006 but compares its address space with a UA 1.04 `NodeSetFile`. That can produce false additions, removals, modelling-rule, DataType, and ValueRank errors for nodes introduced or changed after 1.04.

**Recommended CTT fix:** select a reference NodeSet whose specification version matches the server model under test. At minimum, the CTT must not report a 1.05 node as non-conformant solely because it differs from the bundled 1.04 reference.

### ServerDiagnostics validation reuses stale browse data

The ServerDiagnostics unit reports that its cached model is stale and then continues validating that cache, producing missing-child errors for the always-present `ServerDiagnosticsSummary` and `SubscriptionDiagnosticsArray` containers. Once the cache is known to be stale, subsequent structural assertions do not describe the current server address space.

**Recommended CTT fix:** invalidate and rebuild the ServerDiagnostics browse cache after the stale-cache condition, or abort the affected assertions as inconclusive. The refreshed browse must use the same SecurityAdmin session required for the restricted diagnostics nodes.

### `ConformanceUnits` is tested as a scalar instead of `QualifiedName[]`

The current standard node `i=24101` (`Server.ServerCapabilities.ConformanceUnits`) has `DataType=QualifiedName` and `ValueRank=1`; its value is a one-dimensional `QualifiedName` array. Run 19 expects a scalar QualifiedName and reports the conformant array value as an error.

**Recommended CTT fix:** update the expected ValueRank to one dimension and decode/compare a `QualifiedName[]` value using the matching UA 1.05 NodeSet definition.

### ResendData `007.js` was stopped by the operator

The recorded ResendData `007.js` failure is a user-aborted test and does not identify a server or CTT conformance defect. Re-run the unit to completion before classifying it.

## 12. Latest MonitoredItem and A&C regressions

### Monitor Value Change V2 `036.js` exposed an ambiguous `DataValue` constructor call (server defect fixed)

`036.js` monitors an array with IndexRange `2:4`, then replaces the array with two elements. The next notification shall carry `BadIndexRangeNoData`. `MonitoredNode2.ApplyRangeAndEncoding` correctly received that range error, but constructed the result with `new DataValue(applyResult.StatusCode)`. Because `StatusCode` is also convertible to `Variant` and the `Variant` constructor has overload priority, the compiler selected the value constructor: the error code became the Variant value while `DataValue.StatusCode` remained `Good`.

The server now constructs an explicit null-valued `DataValue` with the range status and the original snapshot timestamps. `IndexRangeBecomesOutOfBoundsQueuesBadIndexRangeNoData` reproduces the exact five-element-to-two-element sequence and verifies the initial slice plus the subsequent `BadIndexRangeNoData` notification on net10 and net48.

### Monitor Value Change V2 `042.js` does not identify the missing item and does not guarantee its write changes the value

`042.js` creates 19 matrix monitored items with IndexRange `1,1,...`, writes each whole matrix, and only reports the aggregate count (`Expected 19 but got 18`). It never reports the missing ClientHandle/NodeId, so the result cannot identify which data type failed. The repository's `MatrixIndexRangeReportsEveryChangedTypeAsync` creates the same 19 configured monitored items, writes a representably different selected element for every matrix type, and receives every ClientHandle.

The script also computes `indexValue` from itself before initialization at line 243 (`var indexValue = Dimensions[u] * (indexValue + 1)`), so value verification is invalid once the count assertion passes. In addition, the configured deterministic Double and Float matrix elements can be very large (for example approximately `-8.19E+24` and `-1.03E+33`); adding one does not necessarily produce a representably different floating-point value. **Recommended CTT fix:** initialize the flat index, record and report missing ClientHandles, and verify that `UaVariant.Increment` actually changed each selected value (use the next representable floating-point value or a known different finite value).

### Dynamic certificate alarm instances replaced standard type declarations (server defect fixed)

Run 20 reported two dynamic Diagnostics-namespace NodeIds missing from the CTT model map and six standard alarm declarations without ModellingRules. The two dynamic IDs were the per-group `CertificateExpired` and `TrustListOutOfDate` roots. Their pre-materialized descendants retained namespace-0 declaration NodeIds such as `i=13325`; registering the runtime subtrees therefore replaced the real `CertificateExpirationAlarmType` and `TrustListOutOfDateAlarmType` declarations in the Configuration node manager. The dynamic roots were also children of namespace-0 certificate-group nodes owned by another node manager, but no external forward references made them reachable to the global model crawler.

`ConfigurationNodeManager` now removes only stale runtime index entries, rebases each dynamic root and every descendant, restores the standard type declaration subtrees, and publishes the cross-node-manager certificate-group references. Client/server regressions verify both dynamic alarms are reachable, every runtime descendant has a nonstandard NodeId, and all six reported declarations expose `HasModellingRule -> Mandatory`.

### Method-triggered events were incorrectly restricted to the caller's Session (server defect fixed)

The channel-based `MonitoredNode2` event path discarded an event whenever the event's `ISessionSystemContext.SessionId` differed from the Session owning the event MonitoredItem. That made an Acknowledge event visible only to the Session that called Acknowledge and caused Confirm `Err_004.js` to report `Acknowledged Alarm extra event not received` for every alarm type. OPC UA event delivery is notifier- and permission-scoped; it is not restricted to the Session that caused the state change.

The caller-Session filter is removed. Permission validation remains per monitored item. Unit coverage verifies both Sessions receive the event, and the live two-Session Confirm test now proves both subscriptions receive the same Acknowledge EventId before the second Confirm returns `BadConditionBranchAlreadyConfirmed`.

### Remaining run-20 A&C script findings

* Alarm `Test_002.js` still evaluates Retain from only the main event's Active/Acked/Confirmed fields. The focused cycle confirms `Retain=true` after Confirm while the prior active branch remains outstanding; this is the Part 9 branch case already documented above, not a stale server value.
* Alarm `Test_004.js` invokes the global `ReadHelper` synchronously from its alarm callback and receives a client-side `BadInvalidState`. An independent client/server regression resolves every AlarmCondition `InputNode` from the event model and reads every referenced source with `Good`. **Recommended CTT fix:** queue the Read outside the callback or use a Read helper/session that is valid on the alarm thread.
* Enable `Test_002.js` calls `collector.AddMessage(testCase, category, conditionId, reason)` even though `AddMessage` accepts only three arguments. JavaScript drops the fourth argument, producing the empty `Error: ns=...` entries and hiding whether EnabledState, Retain, Event Time, or TransitionTime failed. The representative type- and instance-method disable/enable cycle passes with correct EnabledState and Retain. **Recommended CTT fix:** concatenate `conditionId` and `reason` into the third argument, then rerun before attributing the generic `Error validating variables for state ConditionDisabled`.
* Enable `Test_003.js` was stopped by the operator and is not a conformance result.

### Run 21 confirms the A&C server fixes and exposes one CTT project-node error

`NewCTT2.results 21.xml` no longer contains the run-20 dynamic-model-map errors, missing standard ModellingRules, or secondary-Session Confirm failures. Its 117 A&C errors consist of the already documented branch-unaware Retain checks, re-entrant InputNode Read failures, hidden Enable validation reasons, their per-type summary errors, and one initialization failure: `CreateMonitoredItems` reports three Good results plus `Results[2] = BadAttributeIdInvalid`.

The failing configured item is `ns=7;s=Alarms`, an Object whose Value Attribute is invalid. `AlarmTester.js` creates the Server event item and the unique configured alarm input-data items in one batch, so this one invalid Object makes initialization fail and can leave the shared event MonitoredItemId invalid. Refresh2 `Err_002.js` then reports `BadMonitoredItemIdInvalid` on its first refresh call as a cascade, not as an independent ConditionRefresh2 server defect.

The CTT project now points numeric Limit, Level, and RateOfChange inputs to `ns=7;s=Alarms.AnalogSource`, Discrete input to `ns=7;s=Alarms.BooleanSource`, and Deviation setpoints to `ns=7;s=Alarms.SetpointSource`. All three targets are readable Variables created by `AlarmNodeManager`; the live `EveryAlarmInputNodeIsReadableAsync` regression independently verifies every alarm instance resolves to a readable source Variable.

## 13. Additional defects in the latest full-run CTT scripts

### Core Structure reads TransactionDiagnostics as if a transaction had already occurred

Core Structure `001.js` reports `BadOutOfService` for `i=32337` through `i=32340` as a datatype/read failure. OPC UA Part 12 §7.10.17 explicitly states: *"If no transaction has started the values of all Variables have a status of Bad_OutOfService."* The server implements that requirement and `TransactionDiagnosticsReportBadOutOfServiceBeforeAnyTransactionAsync` verifies every TransactionDiagnostics variable. **Recommended CTT fix:** accept `BadOutOfService` while walking TransactionDiagnostics before the first transaction, or create a completed transaction before validating values.

### SemanticChange `001.js` decodes the `Changes` array as one ExtensionObject

The test receives a SemanticChange event, then calls `EventFields[0].toExtensionObject()` at line 275. OPC UA Part 5 Table 174 defines `SemanticChangeEventType.Changes` as `SemanticChangeStructureDataType[]` with ValueRank 1, not a scalar ExtensionObject. The scalar conversion therefore returns null and the script throws before validating the event. **Recommended CTT fix:** decode the field as an ExtensionObject array and convert each element to `SemanticChangeStructureDataType`.

### Historical Access Read Raw `013.js` reuses continuation points after changing IndexRange

`013.js` reuses the same `HistoryReadValueId` objects for three different IndexRanges and does not clear or consume the ContinuationPoints returned by the preceding call. With seven configured matrix nodes, the two later iterations produce the observed 14 `BadContinuationPointInvalid` results. A HistoryRead continuation point is opaque state for the original request (Part 11 §6.4.3.3); changing IndexRange while resubmitting it invalidates that state. **Recommended CTT fix:** fully drain/release every continuation point before the next IndexRange, or create fresh `HistoryReadValueId` objects with empty ContinuationPoints for each independent request.

### Security User Name Password `015.js` requires PolicyId uniqueness across unrelated endpoints

The script flattens `UserIdentityTokens` from every `EndpointDescription` into one array and compares `PolicyId` globally. OPC UA Part 4 §7.36.2.2 requires each `UserTokenPolicy` to have a unique `PolicyId` within the `UserIdentityTokens` array of one `EndpointDescription`; it does not require global uniqueness across all endpoints. The current reference server's live endpoints have unique PolicyIds within every endpoint. The script also uses `foundTokens[i]` instead of the token it just appended inside the nested loop at line 20. **Recommended CTT fix:** reset the seen-PolicyId set for each endpoint and validate only that endpoint's array.

### Security Certificate Validation residuals do not reproduce on the current server

The latest XML again reports generic `BadSecurityChecksFailed` for expired/not-yet-valid trusted certificates (`007`/`008`) and rejection of valid certificates (`037`/`044`/`051`/`052`). Current focused tests strictly receive `BadCertificateTimeInvalid` for `007`/`008` and pass all six scenarios when their certificates are provisioned. The valid-certificate CTT cases still require the CTT certificates/issuers in the configured trust stores as documented above. Re-run the security pass against the rebuilt server with the expected trust material before reopening server code.

### Aggregate run 21 console evidence isolates one additional server defect

`ctt output 8.txt` provides 31,114 Server/CTT value pairs for the later Aggregate Conformance Units. The file begins partway through DurationGood, so it does not contain the value-bearing AnnotationCount, Count, DeltaBounds, DurationBad, or initial DurationGood comparisons. The XML contains 2,184 primary `Query did not result in identical readings` errors, 2,184 matching per-node summary errors, 100 existing `possibleNodeId [undefined]` exceptions, and 74 existing `GetRequestEntry failed due to incorrect test configuration` exceptions.

One additional server defect is proven: NumberOfTransitions is lower than the CTT oracle by exactly one in 160 comparisons. Part 13 §5.4.3.24 requires the earliest non-Bad value to count as a transition when no previous non-Bad value exists, and requires Uncertain values to participate because only Bad values are excluded from the count. The calculator previously counted only changes after a Good prior value and also excluded Uncertain values when `TreatUncertainAsBad=true`. It now tracks the previous non-Bad value independently of quality calculation, counts the first non-Bad value when no prior value exists, compares the actual Variants, and continues excluding the interval end. Direct calculator regressions cover no-prior, matching-prior, and prior-Uncertain cases; live historian regressions cover forward and reverse boundary inclusion plus the excluded end.

The remaining evidenced value families primarily expose CTT configuration/oracle differences rather than proving more server changes:

* The log prints `Requested Bad Data Entry - Bad Data Entry no found, using start data` 1,815 times. The current reference-server seed does contain a deterministic mixed-quality pattern (index modulo 10: one `BadDataUnavailable`, one `UncertainSubstituteValue`, eight Good) on every configured historical node, but the CTT project does not identify a `BadDataEntry`; `HAAggregateHelper.GetRequestEntry` silently substitutes `StartEntry`. The related `BadDataStartRequest` cases have no fallback and produce the 74 existing configuration exceptions. The project needs explicit Bad/Uncertain entry metadata instead of silently changing the requested scenario.
* The same seeded status timeline is applied to Int32, Float, Double, Boolean, and String nodes. Status-only aggregates therefore return the same DurationGood/Bad and PercentGood/Bad values for each data type. The log shows the CTT cached oracle matching the numeric nodes, then producing different values only for the Boolean/String nodes (for example server `PercentGood=83.193277...` for every type while the CTT changes to `82.608771...` and then `0`). This proves a CTT cached-history decode/oracle defect for non-numeric values; the server calculation cannot legitimately depend on the raw value type.
* Every Interpolative mismatch is the Int32 conversion `24` versus `23`; Float and Double match exactly. The TimeAverage and Total mismatches are likewise confined to Int32 nodes while Float and Double match. The server rounds the interpolated source-typed bounds to nearest; the CTT truncates. Part 13 defines the interpolation and result type but does not mandate the integer conversion convention.
* StartBound and EndBound support all source data types under Part 13 §5.4.2.3 and use Simple Bounding Values. The CTT cached oracle repeatedly returns `BadNoData` for valid Boolean and String bounds while the server returns the nearest valid value. Numeric Float/Double bounds match, with only the same Int32 rounding convention above.
* MinimumActualTime2 and MaximumActualTime2 server results use an eligible sloped End bound and timestamp it at EffectiveEndTime, as required by Part 13 §§5.4.3.17-.18 and §5.4.2.4. The CTT comparisons instead select an earlier raw value in those cases. Related `*2` comparisons also contain the non-numeric oracle and Int32 conversion differences above.

Run 21 therefore justifies the NumberOfTransitions fix above, not broad compatibility changes to the other aggregate calculators. AnnotationCount, Count, DeltaBounds, DurationBad, and the beginning of DurationGood still need a non-truncated value-bearing log. Tail-of-range Start/End and WorstQuality comparisons that differ only between `BadDataUnavailable` and `BadNoData`, or by the Partial bit, also remain open pending a focused request/raw-data trace.

### Durable Subscription `008.js` misspells `MoreNotifications` and does not drain the queue

The script correctly checks `PublishHelper.Response.MoreNotifications` at line 35, but line 101 uses the misspelled `MoreNotifcations`. The drain loop therefore does not run when additional notification responses are queued, and the final Publish sees a notification that the script incorrectly calls unexpected. OPC UA Part 4 §5.14.5.2 permits subsequent responses when `moreNotifications=TRUE`. Lines 105-108 also omit braces, leaving `result = false` unconditional. **Recommended CTT fix:** use `MoreNotifications`, drain until it is false, then perform the no-more-data assertion with braces around the failure branch.

### Durable Subscription `012.js` uses an insufficiently secured diagnostics Session and then sends an empty Read

The unit's `initialize.js` selects a username endpoint but does not require SignAndEncrypt. `012.js` then assumes the restricted SubscriptionDiagnostics children are browsable. The reference server requires an encrypted SecurityAdmin Session for these standard diagnostics; an insecure or non-admin Browse correctly returns `BadUserAccessDenied`. After that Browse fails, `subscriptionIdProperties` is empty but line 30 still calls Read, so the helper sends an empty operation list and the server correctly returns `BadNothingToDo` (Part 4 §§5.11.2.3 and 7.38.2).

**Recommended CTT fix:** select a SignAndEncrypt username endpoint and the configured SecurityAdmin credentials before browsing diagnostics; if Browse is denied or returns no properties, skip/abort this verification instead of issuing an empty Read.

### Subscription Minimum 02 `020.js` accepts unrelated audit events

The event MonitoredItem has SelectClauses but no WhereClause, so it accepts every event emitted by the Server. The test's scalar Write generates an `AuditWriteUpdateEvent` when auditing is enabled, and a Server-root event subscriber is expected to receive it. The script then reports any event as unexpected; it may also leave a trigger event queued because the preceding step does not drain `MoreNotifications`.

**Recommended CTT fix:** select EventType, filter for only the trigger event the test is validating, and drain every response while `MoreNotifications` is true. Do not treat correctly emitted audit events as Subscription-Minimum failures.
