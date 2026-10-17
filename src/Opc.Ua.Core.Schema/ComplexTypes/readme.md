# OPC UA Structure Encoding Reference

Use `StructureDefinition.StructureType` together with each `StructureField`
to determine field presence and permitted subtypes. Then apply the OPC UA
Binary encoding rules for the field's DataType and ValueRank.

This reference follows the linked OPC UA specifications. It does not infer
historical version support from a DataType's abstractness.

## Structure metadata

[Part 3, 8.48](https://reference.opcfoundation.org/specs/OPC-10000-3/8.48)
defines `StructureDefinition`:

| Member | Purpose |
| --- | --- |
| `DefaultEncodingId` | Default DataTypeEncoding NodeId. It is null for abstract types and may be null for types used only inside nested structures rather than directly in ExtensionObjects. Otherwise it identifies the Default Binary encoding. |
| `BaseDataType` | Direct supertype's NodeId. |
| `StructureType` | Selects the structure or union kind below. |
| `Fields` | Ordered field definitions, beginning with inherited fields for a derived structure. |

[Part 3, 8.51](https://reference.opcfoundation.org/specs/OPC-10000-3/8.51)
defines `StructureField` members: `Name`, `Description`, `DataType`,
`ValueRank`, `ArrayDimensions`, `MaxStringLength` and `IsOptional`.

`ValueRank` is scalar (-1) or a fixed array rank (at least 1).
`ArrayDimensions` describes maximum dimension lengths, with 0 for an unknown
maximum; it is not necessarily the size of each encoded value.

## StructureType and IsOptional

The enumeration is defined in
[Part 3, 8.49](https://reference.opcfoundation.org/specs/OPC-10000-3/8.49).

| Value | StructureType | Meaning of StructureField.IsOptional |
| --- | --- | --- |
| 0 | `Structure` | Must be false; ignored. All fields are mandatory and none permits subtyping. |
| 1 | `StructureWithOptionalFields` | True means the field may be absent. Fields do not permit subtyping. |
| 2 | `Union` | Must be false; ignored. The union switch selects the field. |
| 3 | `StructureWithSubtypedValues` | True permits subtypes of the field's declared DataType. It does not make the field optional. |
| 4 | `UnionWithSubtypedValues` | True permits subtypes of the field's declared DataType. The union switch still selects the field. |

`AllowSubTypes` is not a separate `StructureField` member. Abstractness alone
does not select subtyped-value encoding. In structure kinds 3 and 4,
`IsOptional` is the per-field subtyping indicator.

For kinds 0, 1 and 2, Part 3 restricts the field DataType to a concrete type,
`BaseDataType`, or `Structure`. Do not treat an arbitrary abstract DataType
as an implicit legacy ExtensionObject rule.

## Decide field presence before encoding its value

| StructureType | Binary prefix | Fields encoded |
| --- | --- | --- |
| `Structure` | None | Every field, in definition order. |
| `StructureWithOptionalFields` | UInt32 encoding mask | Every mandatory field and optional fields whose bits are set. |
| `Union` | UInt32 switch | The selected field, or none for switch 0. |
| `StructureWithSubtypedValues` | None | Every field, in definition order. |
| `UnionWithSubtypedValues` | UInt32 switch | The selected field, or none for switch 0. |

For optional-field structures, assign bits only to optional fields in their
definition order. At most 32 optional fields are supported by the mask;
unassigned bits must be zero. Inherited optional fields retain their mask
positions. See
[Part 6, 5.2.7](https://reference.opcfoundation.org/specs/OPC-10000-6/5.2.7).

For unions, the switch is the selected field's one-based index. Zero represents
a null union; a value greater than the number of defined fields is an encoding
or decoding error. See
[Part 6, 5.2.8](https://reference.opcfoundation.org/specs/OPC-10000-6/5.2.8).

Do not use a kind-3 or kind-4 field's `IsOptional` value to assign a presence-mask
bit or omit that field.

## Encode the present field

Resolve the field's DataType and apply its specified encoding, including
subtype support where the metadata permits it. A concrete nested structure
without subtype support is encoded inline; an ExtensionObject carries the
concrete encoding identifier when that representation is required.
Not every abstract value is an ExtensionObject: `Number`, `Integer` and
`UInteger` fields use Variant encoding
([Part 6, 5.1.6](https://reference.opcfoundation.org/specs/OPC-10000-6/5.1.6)).

Use the type-specific rules rather than a decision based solely on
`IsAbstract`:

- [Built-in types](https://reference.opcfoundation.org/specs/OPC-10000-6/5.2.2)
- [Enumerations](https://reference.opcfoundation.org/specs/OPC-10000-6/5.2.4)
- [Structures](https://reference.opcfoundation.org/specs/OPC-10000-6/5.2.6)
- [Variant](https://reference.opcfoundation.org/specs/OPC-10000-6/5.2.2.16)

### Arrays and matrices

A one-dimensional array is an Int32 element count followed by its elements;
-1 represents a null array.

A multidimensional array in a structure field uses the inline matrix
representation: the dimensions, encoded as an Int32 array, precede the values.
The number of values is the product of the dimensions; if any dimension is
non-positive, no values are encoded. This differs from a multidimensional
Variant, whose dimensions follow the encoded values. See
[Part 6, 5.2.5](https://reference.opcfoundation.org/specs/OPC-10000-6/5.2.5).

### ExtensionObject envelope

[Part 6, 5.2.2.15](https://reference.opcfoundation.org/specs/OPC-10000-6/5.2.2.15)
defines the binary envelope:

| Field | Encoding |
| --- | --- |
| `TypeId` | NodeId of the DataTypeEncoding, not merely the DataType NodeId. |
| `Encoding` | Byte: 0 means no body, 1 means a binary ByteString body, 2 means an XML body. |
| `Length` | Int32 body length, present when a body is encoded. |
| `Body` | Bytes in the selected encoding. |

The envelope is separate from the contained structure's optional-field mask
or union switch. A null ExtensionObject uses TypeId `i=0` and Encoding 0.
