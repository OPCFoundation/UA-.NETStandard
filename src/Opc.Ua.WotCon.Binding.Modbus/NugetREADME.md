# OPC UA WoT Connectivity — Modbus TCP Executor

`OPCFoundation.NetStandard.Opc.Ua.WotCon.Binding.Modbus` executes Modbus TCP
WoT Connectivity binding forms compiled by the Modbus planner in
`Opc.Ua.WotCon.Binding`.

It provides a minimal, robust Modbus TCP client and executor supporting coils,
discrete inputs, holding and input registers, the required read / write
function codes, unit id / address / quantity addressing, byte / word order and
data-type conversion, with strict bounds, transaction ids and timeouts.

Register it with `builder.AddModbusWotBinding()`.
