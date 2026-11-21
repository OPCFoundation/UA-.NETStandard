// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.WotCon.Bindings.Modbus
{
    /// <summary>
    /// A Modbus protocol exception carrying the device exception code.
    /// </summary>
    public sealed class ModbusException : Exception
    {
        /// <summary>
        /// Initializes a new Modbus exception.
        /// </summary>
        public ModbusException(byte exceptionCode, string message)
            : base(message)
        {
            ExceptionCode = exceptionCode;
        }

        /// <summary>
        /// Initializes a new Modbus exception without a device code.
        /// </summary>
        public ModbusException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new Modbus exception.
        /// </summary>
        public ModbusException()
        {
        }

        /// <summary>
        /// Initializes a new Modbus exception with an inner exception.
        /// </summary>
        public ModbusException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Gets the Modbus exception code, or <c>0</c> for a transport fault.
        /// </summary>
        public byte ExceptionCode { get; }
    }
}
