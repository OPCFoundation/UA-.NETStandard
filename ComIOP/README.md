# OPC Foundation UA .Net Standard Library COM Server Wrapper

## Introduction
This OPC UA COM Server Wrapper is designed to map OPC Classic server information to OPC UA to help vendors migrate to OPC UA based systems while still being able to access information from existing OPC Classic systems. 
For more information please refer of the OPC Unified Architecture specification:
- for OPC Data Access (DA) refer to the OPC UA specification, Part 8: Data Access, ANNEX A
- for OPC Alarms and Events (AE) refer to the OPC UA specification, Part 9: Alarms & Conditions, ANNEX D
- for OPC Historical Data Access (HDA) refer to the OPC UA specification, Part 11: Historical Access, ANNEX A

## How to build and run the Windows OPC COM Server Wrapper
1. Start the OPC Classic Server.
2. Open the solution **UA COM Interop.sln** with VisualStudio.
3. Choose the **UA COM Server Wrapper** project in the Solution Explorer and set it with a right click as `Startup Project`.
4. Enter the `ServerUrl` for the OPC Classic Server DA, AE and HDA connection in the configuration file: **Opc.Ua.ComServerWrapper.Config.xml**.
5. Hit `F5` to build and execute the sample.
5. Connect to the **UA COM Server Wrapper** with a OPC UA Client to access the namespace of the OPC Classic Server.



