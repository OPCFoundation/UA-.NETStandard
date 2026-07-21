# OPC UA WoT Connectivity — Modbus TCP Executor

`OPCFoundation.NetStandard.Opc.Ua.WotCon.Binding.Modbus` executes Modbus TCP
WoT Connectivity binding forms compiled by the Modbus planner in
`Opc.Ua.WotCon.Binding`.

It provides a minimal, robust Modbus TCP client and executor supporting coils,
discrete inputs, holding and input registers, the required read / write
function codes, unit id / address / quantity addressing, byte / word order and
data-type conversion, with strict bounds, transaction ids and timeouts.

## Addressing and function validation

- The planner enforces a 16-bit address space: `modv:address` must be
  0&ndash;65535 and the addressed range (`address + quantity - 1`) must stay
  within it.
- Function-only forms map exactly onto the Modbus function codes 1, 2, 3, 4, 5,
  6, 15 and 16 (string mnemonics or numeric codes). A `modv:function` that
  conflicts with `modv:entity`, or whose direction conflicts with the operation
  (a write function on a read op or vice versa), is rejected.
- The executor re-validates the address / quantity range before narrowing the
  values to `ushort` / `byte`, so a hand-built or tampered compiled form fails
  fast instead of silently truncating.

Register it with `builder.AddModbusWotBinding()`.
