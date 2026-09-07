# Variant scalar storage

`Variant.From`, the typed constructors, `TryGetValue`, getters and
`IVariantBuilder<T>` retain their existing value semantics. Eligible scalar values
use the existing reference and unmanaged union rather than allocating an outer
builtin box. No application opt-in, DI registration or migration is required.

```csharp
var identifier = new NodeId(42u, 2);
Variant value = Variant.From(identifier);
if (value.TryGetValue(out NodeId extracted))
{
    // The kind, namespace and identifier are unchanged.
    Console.WriteLine(extracted);
}
```

## Representation and supported targets

On x64, Variant remains 24 bytes: one managed reference, an eight-byte unmanaged
union, four-byte TypeInfo and padding. The managed-reference positions are
unchanged. Managed references never overlap scalar bits.

| Scalar | Stored state | Eligibility |
|---|---|---|
| QualifiedName | Existing raw name reference and namespace index | All typed values, including null and empty names |
| NodeId | Existing identifier reference and complete eight-byte Inner state | All typed values, including numeric zero |
| ByteString | Raw memory backing object, index and length, including runtime flags | NET8+; older targets retain their boxed representation |
| LocalizedText | Existing raw text reference | Raw locale reference and translation state must both be null |

A private tag in TypeInfo's existing validity byte distinguishes split storage
from an absent or boxed payload. The public `Variant.TypeInfo` returns the
ordinary, untagged TypeInfo. No payload bits are reserved for this tag: NodeId's
cached hash, namespace, identifier kind and reserved byte are retained exactly.

Guid/Uuid, ExpandedNodeId, locale-bearing LocalizedText (including an **empty**
locale), translated/formatted/multilingual LocalizedText, arrays and matrices
retain their existing representations. Explicit-TypeInfo legacy construction
retains its original boxed payload behavior, including mismatched or exceptional
cases; it does not silently repair the supplied type information.

## Ownership and compatibility

Packing does not copy or intern strings, identifiers or buffers. A NodeId keeps
the original Guid/opaque identifier box; eliminating the outer NodeId box does
not eliminate that shared inner object. Reconstruction never recomputes the
cached NodeId hash. Borrowed opaque-buffer mutations remain visible, while the
NodeId retains its original cached hash.

ByteString construction and typed extraction retain the same memory owner and
slice, including MemoryManager ownership and pinned-array flags. Packing does
not extend the underlying owner's usable lifetime: accessing a disposed manager
still has the same failure behavior. Existing Variant conversions between
ByteString and ArrayOf<byte> continue to copy through their existing conversion
paths; those conversions are not made zero-copy by this storage change.

Boxing APIs, the JSON-facing raw value and legacy Value return the semantic builtin
value, never its backing string or memory object. Separate boxing calls have no
general reference-identity guarantee. Before packing, NodeId, QualifiedName and
LocalizedText boxing already rematerialized their scalar values; ByteString's
boxed fallback could return its stored box. Generic ingress dispatches to the
typed construction path rather than adopting a universal caller-box identity
contract.

Strict equality, ValueEquals and ordering remain distinct operations. In
particular, cross-type comparison is not a total order, ByteString and
LocalizedText remain incomparable through Variant's non-generic IComparable
path, and some explicit-TypeInfo mismatches are not reflexive. Packing metadata
must not participate in primitive cross-type comparisons, bitwise operations or union-only equality.
Typed builtin defaults also remain distinguishable from an absent payload where
the existing API distinguishes them.

## Measurement

Use the permanent `VariantStorageBenchmarks` in `Opc.Ua.Types.Tests` with the
existing BenchmarkDotNet entry point. It covers typed and generic ingress,
variable values, typed extraction/hashing, binary codecs and rich-text controls.
`NodeStateReadAttributesBenchmarks` measures consumption through node attributes;
the existing NodeState memory exporter measures cached/unique payload populations.

Matched .NET 10 x64 runs observe a 32-byte outer-box allocation reduction per
eligible QualifiedName, NodeId or ByteString occurrence, and a 40-byte reduction
for eligible text-only LocalizedText. These are runtime-specific observations,
not portable object-size assertions or additive whole-server savings. Payload
allocation/sharing and the Variant stride must remain identical between compared
runs. Retain source and DLL hashes, an identical harness, runtime/GC settings,
paired run order, uncertainty and boxed-fallback controls with each measurement.

Avoid restricting an in-process tiered-JIT benchmark to a single busy core:
compiler scheduling can materially distort results. Longer warmup alone does
not establish steady state. Use repeated paired runs and investigate anomalous
paths separately rather than treating a ShortRun point estimate as proof.
