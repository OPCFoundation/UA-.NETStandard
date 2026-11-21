// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// Modbus application-protocol limits used by both planning and execution.
    /// </summary>
    internal static class ModbusProtocolLimits
    {
        public const int MaxAddress = ushort.MaxValue;

        public const int MaxReadBits = 2000;

        public const int MaxWriteCoils = 1968;

        public const int MaxReadRegisters = 125;

        public const int MaxWriteRegisters = 123;
    }
}
