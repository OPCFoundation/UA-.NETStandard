# Opc.Ua.Aas

The **OPC UA for Asset Administration Shell** V3 companion model
(`http://opcfoundation.org/UA/I4AAS/v3/`) and the lossless mapping between an AAS
`Environment` and an OPC UA AddressSpace.

This package contains:

- the AAS V3 companion namespace, **compiled into the assembly by the OPC UA model source
  generator** — the ObjectTypes, their Methods and Variables, the `AAS*DataType` structures
  and enumerations, the generated `NodeState` classes and the `*TypeClient` ObjectType
  proxies. No NodeSet2 XML is parsed at runtime;
- `AasNodeIdEncoding`, `AasIdShortPath` and `AasBrowseNameAllocator` — the deterministic
  identity rules of clause 6.1.3, so that two implementations materializing the same AAS
  produce the same nodes;
- `AasXsdTypeMap` and `AasLexicalCanonicalizer` — the clause 6.3.1 assignment of each of the
  thirty `DataTypeDefXsd` values to one OPC UA DataType, and the XSD canonical lexical forms
  a round trip emits;
- the AAS object model plus readers and writers for the AAS JSON, AAS XML and AASX
  serializations;
- `AasNodeSetMaterializer` and `AasNodeSetSerializer` — the clause 6.1.6 materialization and
  its inverse, which together satisfy the `AAS-LosslessRoundTrip` conformance unit;
- `Dpp` — the Digital Product Passport identifier construction, SSSOM mapping set and
  access-tier mapping.

The registry half of the specification builds on the abstract xRegistry base model, which
ships separately as `Opc.Ua.XRegistry`.

It has no dependency on the OPC UA server or client SDKs; those pieces live in
`Opc.Ua.Aas.Server`, `Opc.Ua.Aas.Client` and `Opc.Ua.Aas.WoT`.
