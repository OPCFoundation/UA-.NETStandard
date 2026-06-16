# PubSub

> **When to read this:** Read this for breaking changes to the
> `Opc.Ua.PubSub.*` namespaces in 2.0.x. This sub-doc is a stub seeded
> by Phase 13; the full PubSub migration story is finalised in Phase 12.

## `JsonEncodingMode` — 1.04 names removed

`Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Reversible` and
`Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.NonReversible` are removed
in favour of the Part 6 §5.4.1 / Part 14 §7.2.5 (v1.05.06) names:

| Old                                            | New                                |
| ---------------------------------------------- | ---------------------------------- |
| `JsonEncodingMode.Reversible`                  | `JsonEncodingMode.Verbose`         |
| `JsonEncodingMode.NonReversible`               | `JsonEncodingMode.Compact`         |
| `JsonEncodingMode.Verbose` (unchanged)         | `JsonEncodingMode.Verbose`         |
| `JsonEncodingMode.Compact` (unchanged)         | `JsonEncodingMode.Compact`         |
| _(new)_                                        | `JsonEncodingMode.RawData`         |

The wire format produced by `Verbose` is byte-identical to the wire
format the old `Reversible` produced; similarly `Compact` ≡ old
`NonReversible`. The rename is a public-API change only. No
`[Obsolete]` aliases exist — consumers update enum references at
upgrade time.

Background: GitHub issue
[#3609](https://github.com/OPCFoundation/UA-.NETStandard/issues/3609).

## UADP RawData field padding

Per Part 14 v1.05.06 §7.2.4.5.11, `String`, `ByteString`, `XmlElement`,
and array fields encoded via `DataSetFieldContentMask.RawData` are now
padded to the maximum size declared in `FieldMetaData.MaxStringLength`
or `FieldMetaData.ArrayDimensions`. The on-wire length prefix is
suppressed for padded fields; consumers receive the exact
`MaxStringLength` bytes with trailing NULs as the spec mandates.
Decoders trim the trailing NUL fill on read.

If your configuration uses RawData but does not declare
`MaxStringLength` or `ArrayDimensions`, the encoder falls back to the
legacy length-prefixed form (variable size) and the configuration
validator surfaces issue code `PSC0025`
(`SpecClause = "7.2.4.5.11"`) so the missing bound is reported at
configuration time.

Closes [#3566](https://github.com/OPCFoundation/UA-.NETStandard/issues/3566).
