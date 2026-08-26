# Opc.Ua.Aas

The **OPC UA for Asset Administration Shell** package carries two alternative
metamodel generations: published OPC 30270 / AAS V2.0.1
(`http://opcfoundation.org/UA/I4AAS/`) and the AAS V3 draft
(`http://opcfoundation.org/UA/I4AAS/v3/`). A server should host one generation
or the other, not both.

This package contains:

- the AAS V2 and V3 companion namespaces, **compiled into the assembly by the OPC UA model
  source generator** — the ObjectTypes, their Methods and Variables, the `AAS*DataType`
  structures and enumerations, the generated `NodeState` classes and the `*TypeClient`
  ObjectType proxies. No NodeSet2 XML is parsed at runtime;
- the AAS V2 object model plus ingestion-only readers for AAS JSON, AAS XML and AASX;
- `AasNodeIdEncoding`, `AasIdShortPath` and `AasBrowseNameAllocator` — the deterministic
  identity rules of clause 6.1.3, so that two implementations materializing the same AAS
  produce the same nodes;
- `AasXsdTypeMap` and `AasLexicalCanonicalizer` — the clause 6.3.1 assignment of each of the
  thirty `DataTypeDefXsd` values to one OPC UA DataType, and the XSD canonical lexical forms
  a round trip emits;
- the AAS V3 object model plus readers and writers for the AAS JSON, AAS XML and
  AASX serializations;
- V2 and V3 materializers, plus the V3 inverse serializer that satisfies the
  `AAS-LosslessRoundTrip` conformance unit;
- `Dpp` — the Digital Product Passport identifier construction, SSSOM mapping set and
  access-tier mapping.

The V3 registry half of the draft specification builds on the abstract xRegistry base
model, which ships separately as `Opc.Ua.XRegistry`. V2 has no registry, packages,
federation or DPP surface.

It has no dependency on the OPC UA server or client SDKs; those pieces live in
`Opc.Ua.Aas.Server`, `Opc.Ua.Aas.Client` and `Opc.Ua.Aas.WoT`.
