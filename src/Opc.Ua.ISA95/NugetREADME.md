# OPC UA .NET Standard — ISA-95 information models

`OPCFoundation.NetStandard.Opc.Ua.ISA95` contains source-generated types and proxies for the OPC UA ISA-95 Common Model (OPC-10030) and ISA-95 Job Control V1/V2 (OPC-10031-4), all three in their own namespace (`Opc.Ua.ISA95`, `Opc.Ua.ISA95.JobControl.V1`, `Opc.Ua.ISA95.JobControl.V2`).

The package is the shared model contract used by `Opc.Ua.ISA95.Client` and `Opc.Ua.ISA95.Server`.

## Overview

The Common Model NodeSet2 XML in this package carries two transparently-documented normative repairs for ReferenceTypes that OPC-10030 requires but the published 2013 NodeSet omitted (`DefinedByMaterialClass`, `AssembledFromSublot`); see the [ISA-95 guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/ISA95.md#normative-nodeset-repairs) for the full rationale and specification references.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [ISA-95 guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/ISA95.md).
