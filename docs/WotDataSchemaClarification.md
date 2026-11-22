# Proposal: Keep OPC UA payload schemas readable by generic WoT consumers

Status: Proposed specification update. This document does not change the specification or the exporter.

Target: OPC UA WoT Binding, "DataType definitions" and "Schema equivalence and authority".

## Problem

The Binding permits an ordinary WoT DataSchema to be omitted when an explicit OPC UA
DataType definition is available. It also makes the loaded model authoritative for a type
referenced by `uav:mapToType`.

These rules preserve the OPC UA type contract for a binding-aware consumer. They do not
give the same information to a generic WoT consumer. Such a consumer cannot use
`uav:mapToType`, `uav:dataTypeDefinition`, or `uav:fields` to determine the payload shape.
These terms are not standard WoT schema references.

As a result, an exported Thing Model can preserve the OPC UA model but omit information
needed to display an input form, generate a client, or validate a payload with standard
WoT DataSchema processing.

An outer `required` array does not solve this problem. It identifies mandatory members
of that object only. It does not identify mandatory fields inside a nested object.

## Example: AutoID Scan Settings

The AutoID NodeSet declares the `Scan` Method with one scalar input argument, `Settings`.
Its DataType is `ScanSettings`, identified by:

```text
nsu=http://opcfoundation.org/UA/AutoID/;i=3010
```

The NodeSet definition and AutoID specification, section 9.3.7, Table 53, agree:

| Field | OPC UA DataType | Optional |
| --- | --- | --- |
| Duration | Duration | False |
| Cycles | Int32 | False |
| DataAvailable | Boolean | False |
| LocationType | LocationTypeEnumeration | True |

### Reference-only representation

The generated action input contains:

```json
{
  "type": "object",
  "uav:argumentLayout": "named",
  "uav:fieldOrder": ["Settings"],
  "properties": {
    "Settings": {
      "uav:mapToType": "nsu=http://opcfoundation.org/UA/AutoID/;i=3010",
      "uav:valueRank": -1
    }
  },
  "required": ["Settings"]
}
```

A binding-aware consumer can resolve `ScanSettings` and obtain its fields and optionality.
A generic WoT consumer can determine that `Settings` must be supplied. It cannot determine
that `Settings` is an object with three mandatory fields and one optional field.

The omission does not make the native OPC UA fields optional. It makes their requirements
unavailable through standard WoT DataSchema processing.

### Self-describing representation

The exporter should retain the OPC UA reference and also expose the standard schema.
This fragment illustrates field types and requiredness. Other constraints, including the
enumeration alternatives for `LocationType`, are omitted from this example.

```json
{
  "type": "object",
  "uav:argumentLayout": "named",
  "uav:fieldOrder": ["Settings"],
  "properties": {
    "Settings": {
      "type": "object",
      "uav:mapToType": "nsu=http://opcfoundation.org/UA/AutoID/;i=3010",
      "uav:valueRank": -1,
      "uav:fieldOrder": ["Duration", "Cycles", "DataAvailable", "LocationType"],
      "properties": {
        "Duration": {
          "type": "number"
        },
        "Cycles": {
          "type": "integer",
          "minimum": -2147483648,
          "maximum": 2147483647
        },
        "DataAvailable": {
          "type": "boolean"
        },
        "LocationType": {
          "type": "integer",
          "uav:mapToType": "nsu=http://opcfoundation.org/UA/AutoID/;i=3009"
        }
      },
      "required": ["Duration", "Cycles", "DataAvailable"]
    }
  },
  "required": ["Settings"]
}
```

`Duration` is represented as a number of milliseconds, consistent with the OPC UA
`Duration` type and the AutoID field definition. The proposed change does not require
copying unrelated schema choices from the baseline.

## Proposed normative specification update

The following text is proposed for adoption. The keywords **shall**, **shall not**,
**should**, and **may** express requirements in the proposed specification text.

### DataSchema availability

A producer that describes a value exchanged by a WoT interaction **shall** provide the
ordinary WoT DataSchema for that value in addition to any OPC UA DataType mapping.
An OPC UA DataType reference or definition **shall not** replace that DataSchema.

This requirement applies to properties, action input and output, event payloads, and
other locations where this Binding uses a WoT DataSchema.

The DataSchema **shall** expose the payload shape and constraints that the canonical
mapping expresses in standard WoT DataSchema terms. These include the value type,
object members, required members, array item schemas, enumeration alternatives, and
applicable value constraints. This requirement **shall** apply recursively to nested
values.

A producer **may** reuse a schema through a reference mechanism defined by the
applicable WoT specification for that document kind. After standard WoT reference
resolution, the consumer **shall** be able to obtain the schema without interpreting
OPC UA Binding terms or loading an OPC UA information model. An OPC UA NodeId or
`uav:mapToType` value alone does not meet this requirement.

For a non-Union Structure, the object schema **shall** include all inherited and locally
declared fields in `properties`. Its `required` set **shall** contain exactly the fields
whose effective `IsOptional` value is `false`. An empty `required` set **may** be omitted.
For a Union, the existing canonical Union mapping **shall** apply instead.

If a type cannot be represented faithfully with the applicable standard WoT DataSchema
mechanisms, the producer **shall** report that limitation. It **shall not** silently omit
the schema and report the resulting payload description as complete for generic WoT
consumers. This rule also applies to recursive types for which no applicable standard
reference mechanism can express the recursion.

### DataType authority and consistency

The loaded OPC UA model remains authoritative for an existing DataType identified by
`uav:mapToType`. An explicit authored DataType definition remains authoritative for a
newly authored type.

The producer **shall** derive the ordinary DataSchema from that authoritative definition.
The existing schema equivalence and normalization rules **shall** apply to the result.
Neither a missing standard schema member nor a contradictory standard schema member
**shall** change the authoritative OPC UA definition.

A binding-aware consumer **shall** report a conflict between a supplied DataSchema and
the authoritative DataType definition. It **shall not** silently select a conflicting
contract.

A standalone DataType definition that is not itself an interaction payload schema
**may** omit an accompanying ordinary DataSchema. A reference to that definition from
an interaction **shall not** exempt the interaction from the DataSchema availability
requirements above.

### Change to the existing omission rule

Replace:

> For a newly authored type, the explicit DTD is authoritative. Its ordinary DataSchema
> **may** be omitted. If supplied, a converter **shall**:

With:

> For a newly authored type, the explicit DTD is authoritative. An ordinary DataSchema
> used to describe an interaction payload **shall** be provided as specified under
> DataSchema availability. For each supplied ordinary DataSchema, a converter **shall**:

Retain the existing canonical derivation, normalization, and semantic equality steps.
Add the same DataSchema availability requirement to the paragraph that defines
`uav:mapToType`, so that it covers existing types as well as newly authored types.

## Rationale

OPC UA type identity and WoT payload description serve different purposes. The OPC UA
definition preserves field order, encoding identities, inheritance, and native type
metadata. The ordinary DataSchema exposes the payload contract to WoT consumers.
Both are needed when the published document is intended for both consumer groups.

The proposal keeps one source of truth. Producers derive the standard schema from the
authoritative OPC UA definition. The existing equivalence checks prevent the two
representations from defining different contracts.

The proposal does not require a generic WoT consumer to invoke an OPC UA Method without
protocol support. It requires the consumer to be able to inspect the payload contract
without that support.

It also does not require all OPC UA metadata to have a standard WoT equivalent.
OPC UA-specific facts remain in the Binding vocabulary. A producer must report cases
where standard WoT schema mechanisms cannot describe the value contract faithfully.

## Compatibility and acceptance checks

This proposal tightens the draft's explicit permission to omit an ordinary DataSchema.
It is a normative producer requirement, not only an editorial clarification.
Existing reference-only output needs an update before it meets the proposed requirement.

For the AutoID example, acceptance checks should confirm that:

- Standard WoT schema processing exposes all four `Settings` fields without OPC UA lookup.
- `Settings` is required at the action-input level.
- `Duration`, `Cycles`, and `DataAvailable` are required within `Settings`.
- `LocationType` can be omitted.
- A binding-aware comparison confirms agreement with the authoritative `ScanSettings` definition.
- An unavailable DataType or an unrepresentable schema produces an explicit diagnostic.

The same checks should cover nested Structures, inherited fields, arrays of Structures,
and the existing canonical Union representation.

## Sources

- [W3C WoT Thing Description 1.1](https://www.w3.org/TR/wot-thing-description11/):
  ObjectSchema, `required`, DataSchema processing, and Thing Model schema reuse.
- [OPC UA AutoID, section 9.3.7, ScanSettings](https://reference.opcfoundation.org/AutoID/v101/docs/9.3.7):
  Table 53 defines field types and optionality.
- [AutoID NodeSet](../src/Opc.Ua.AutoID/Model/Opc.Ua.AutoID.NodeSet2.xml):
  `ScanSettings` (`ns=1;i=3010`) and the `Scan` input argument (`ns=1;i=6050`).
- [OPC UA WoT Binding working draft](https://github.com/OPCF-Members/OPC10100-WoT/blob/main/source/WoT-Binding/spec.md):
  "Runtime Node and DataType mapping terms", "Structure fields", "Schema-only inference",
  and "Schema equivalence and authority". The proposal is based on the local working
  draft reviewed on 2026-11-19, not a claim about an adopted published revision.
