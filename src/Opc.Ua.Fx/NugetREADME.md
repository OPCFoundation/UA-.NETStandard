# OPC UA FX information models

`OPCFoundation.NetStandard.Opc.Ua.Fx` provides source-generated Part 81 Data,
AutomationComponent and ConnectionManager types, identifiers, node states and
asynchronous client proxies. It reuses the stack's Device Integration model.

The published UAFX 1.00.04 NodeSets are pinned to
`OPCFoundation/UA-Nodeset@dd178cf655ed3d74f1d085ef04e0e226a1d3ed9f`.
The XML files are unmodified upstream inputs; generated C# is not checked in.

This package supplies model contracts, not a ConnectionManager implementation
or a UAFX conformance claim. Application authorization, commissioning,
identifier reservation, endpoint lifecycle and network-service support require
an implementation of the corresponding Part 81 and selected Part 84 behavior.

See [UAFX information models](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/UAFX.md)
for registration examples and the pinned input blob identifiers.
