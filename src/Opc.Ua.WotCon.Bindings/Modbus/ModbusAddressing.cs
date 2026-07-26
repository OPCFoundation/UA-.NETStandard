/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Immutable;
using System.Globalization;

namespace Opc.Ua.WotCon.Bindings.Modbus
{
    /// <summary>
    /// A validated Modbus operation. The numeric values are the function codes.
    /// </summary>
    internal enum ModbusOperation : byte
    {
        ReadCoils = 1,
        ReadDiscreteInputs = 2,
        ReadHoldingRegisters = 3,
        ReadInputRegisters = 4,
        WriteSingleCoil = 5,
        WriteSingleHoldingRegister = 6,
        WriteMultipleCoils = 15,
        WriteMultipleHoldingRegisters = 16
    }

    /// <summary>
    /// The validated Modbus operation and addressing parsed from a compiled form.
    /// The executor checks method, entity, direction, function metadata and bounds
    /// before values are narrowed to <see cref="ushort"/> / <see cref="byte"/>, so
    /// a hand-built or tampered form cannot select a fallback function or silently
    /// truncate an out-of-range value.
    /// </summary>
    internal readonly struct ModbusAddressing
    {
        private ModbusAddressing(
            ModbusOperation operation, ushort address, ushort quantity, byte unitId,
            string type, bool msbFirst, bool mswFirst)
        {
            Operation = operation;
            Address = address;
            Quantity = quantity;
            UnitId = unitId;
            Type = type;
            MsbFirst = msbFirst;
            MswFirst = mswFirst;
        }

        public ModbusOperation Operation { get; }

        public ushort Address { get; }

        public ushort Quantity { get; }

        public byte UnitId { get; }

        public string Type { get; }

        public bool MsbFirst { get; }

        public bool MswFirst { get; }

        /// <summary>
        /// Parses and validates the addressing carried by a compiled Modbus form,
        /// including operation consistency and protocol bounds.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static ModbusAddressing FromForm(WotCompiledForm form, WotBindingBounds? bounds = null)
        {
            if (form is null)
            {
                throw new ArgumentNullException(nameof(form));
            }
            ImmutableDictionary<string, string> map = form.Addressing.Metadata;
            string entity = GetRequiredString(map, "entity", form);
            int address = GetRequiredInt(map, "address", form);
            int quantity = GetRequiredInt(map, "quantity", form);
            int unitId = GetRequiredInt(map, "unitId", form);
            bounds ??= WotBindingBounds.Default;

            if (address is < 0 or > ModbusProtocolLimits.MaxAddress)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(form), address,
                    $"The Modbus address must be between 0 and {ModbusProtocolLimits.MaxAddress}.");
            }
            if (quantity is < 1 or > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(form), quantity, $"The Modbus quantity must be between 1 and {ushort.MaxValue}.");
            }
            ModbusOperation operation = ValidateOperation(form, map, entity);
            bool bitOperation = IsBitOperation(operation);
            int configuredMaxQuantity = bitOperation
                ? bounds.MaxCoilQuantity
                : bounds.MaxRegisterQuantity;
            int protocolMaxQuantity = ProtocolMaximum(operation);
            int maxQuantity = Math.Min(configuredMaxQuantity, protocolMaxQuantity);
            if (IsSingleWrite(operation) && quantity != 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(form), quantity,
                    $"The Modbus {CanonicalMethod(operation)} function requires a quantity of 1.");
            }
            if (quantity > maxQuantity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(form), quantity,
                    $"The Modbus quantity must not exceed {maxQuantity} for '{form.OperationInfo.Method}'.");
            }
            if (address + quantity - 1 > ModbusProtocolLimits.MaxAddress)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(form), address,
                    $"The Modbus range starting at {address} for {quantity} items exceeds the maximum " +
                    $"address {ModbusProtocolLimits.MaxAddress}.");
            }
            if (unitId is < 0 or > 255)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(form), unitId, "The Modbus unit id must be between 0 and 255.");
            }

            string type = GetString(form.Payload.Metadata, "type", "uint16");
            bool msbFirst = GetBool(form.Payload.Metadata, "mostSignificantByte", true);
            bool mswFirst = GetBool(form.Payload.Metadata, "mostSignificantWord", true);

            return new ModbusAddressing(
                operation,
                (ushort)address,
                (ushort)quantity,
                (byte)unitId,
                type,
                msbFirst,
                mswFirst);
        }

        private static ModbusOperation ValidateOperation(
            WotCompiledForm form,
            ImmutableDictionary<string, string> map,
            string entity)
        {
            if (!TryResolveOperation(form.OperationInfo.Method, out ModbusOperation operation))
            {
                throw new ArgumentException(
                    $"The compiled Modbus method '{form.OperationInfo.Method}' is not supported.",
                    nameof(form));
            }
            if (form.OperationInfo.Operation != form.Operation)
            {
                throw new ArgumentException(
                    $"The compiled Modbus operation '{form.OperationInfo.Operation}' does not match " +
                    $"the form operation '{form.Operation}'.",
                    nameof(form));
            }

            bool writeOperation = IsWriteOperation(operation);
            bool writeDirection = form.Operation == WoTBindingCapabilityEnum.WriteProperty;
            bool readDirection = form.Operation is
                WoTBindingCapabilityEnum.ReadProperty or WoTBindingCapabilityEnum.ObserveProperty;
            if ((!writeDirection && !readDirection) || writeOperation != writeDirection)
            {
                throw new ArgumentException(
                    $"The compiled Modbus method '{form.OperationInfo.Method}' is not valid for " +
                    $"the '{form.Operation}' operation.",
                    nameof(form));
            }

            string expectedEntity = ExpectedEntity(operation);
            if (!string.Equals(entity, expectedEntity, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"The compiled Modbus method '{form.OperationInfo.Method}' operates on " +
                    $"'{expectedEntity}', not '{entity}'.",
                    nameof(form));
            }

            if (map.TryGetValue("functionCode", out string? functionCodeText) &&
                (!int.TryParse(
                    functionCodeText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int functionCode) ||
                    functionCode != (int)operation))
            {
                throw new ArgumentException(
                    $"The compiled Modbus function code '{functionCodeText}' does not match " +
                    $"the method '{form.OperationInfo.Method}'.",
                    nameof(form));
            }
            if (map.TryGetValue("function", out string? functionName) &&
                !string.Equals(functionName, CanonicalMethod(operation), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"The compiled Modbus function '{functionName}' does not match " +
                    $"the method '{form.OperationInfo.Method}'.",
                    nameof(form));
            }
            return operation;
        }

        private static bool TryResolveOperation(string method, out ModbusOperation operation)
        {
            operation = method.ToLowerInvariant() switch
            {
                "readcoil" => ModbusOperation.ReadCoils,
                "readdiscreteinput" => ModbusOperation.ReadDiscreteInputs,
                "readholdingregisters" => ModbusOperation.ReadHoldingRegisters,
                "readinputregister" => ModbusOperation.ReadInputRegisters,
                "writesinglecoil" => ModbusOperation.WriteSingleCoil,
                "writesingleholdingregister" => ModbusOperation.WriteSingleHoldingRegister,
                "writemultiplecoils" => ModbusOperation.WriteMultipleCoils,
                "writemultipleholdingregisters" => ModbusOperation.WriteMultipleHoldingRegisters,
                _ => default
            };
            return operation != default &&
                method.Equals(CanonicalMethod(operation), StringComparison.OrdinalIgnoreCase);
        }

        private static string CanonicalMethod(ModbusOperation operation)
        {
            return operation switch
            {
                ModbusOperation.ReadCoils => "readCoil",
                ModbusOperation.ReadDiscreteInputs => "readDiscreteInput",
                ModbusOperation.ReadHoldingRegisters => "readHoldingRegisters",
                ModbusOperation.ReadInputRegisters => "readInputRegister",
                ModbusOperation.WriteSingleCoil => "writeSingleCoil",
                ModbusOperation.WriteSingleHoldingRegister => "writeSingleHoldingRegister",
                ModbusOperation.WriteMultipleCoils => "writeMultipleCoils",
                ModbusOperation.WriteMultipleHoldingRegisters => "writeMultipleHoldingRegisters",
                _ => string.Empty
            };
        }

        private static string ExpectedEntity(ModbusOperation operation)
        {
            return operation switch
            {
                ModbusOperation.ReadCoils or
                ModbusOperation.WriteSingleCoil or
                ModbusOperation.WriteMultipleCoils => "coil",
                ModbusOperation.ReadDiscreteInputs => "discreteInput",
                ModbusOperation.ReadInputRegisters => "inputRegister",
                _ => "holdingRegister"
            };
        }

        private static int ProtocolMaximum(ModbusOperation operation)
        {
            return operation switch
            {
                ModbusOperation.ReadCoils or
                ModbusOperation.ReadDiscreteInputs => ModbusProtocolLimits.MaxReadBits,
                ModbusOperation.WriteMultipleCoils => ModbusProtocolLimits.MaxWriteCoils,
                ModbusOperation.WriteMultipleHoldingRegisters => ModbusProtocolLimits.MaxWriteRegisters,
                ModbusOperation.WriteSingleCoil or
                ModbusOperation.WriteSingleHoldingRegister => 1,
                _ => ModbusProtocolLimits.MaxReadRegisters
            };
        }

        private static bool IsBitOperation(ModbusOperation operation)
        {
            return operation is
                ModbusOperation.ReadCoils or
                ModbusOperation.ReadDiscreteInputs or
                ModbusOperation.WriteSingleCoil or
                ModbusOperation.WriteMultipleCoils;
        }

        private static bool IsWriteOperation(ModbusOperation operation)
        {
            return operation is
                ModbusOperation.WriteSingleCoil or
                ModbusOperation.WriteSingleHoldingRegister or
                ModbusOperation.WriteMultipleCoils or
                ModbusOperation.WriteMultipleHoldingRegisters;
        }

        private static bool IsSingleWrite(ModbusOperation operation)
        {
            return operation is
                ModbusOperation.WriteSingleCoil or
                ModbusOperation.WriteSingleHoldingRegister;
        }

        private static string GetRequiredString(
            ImmutableDictionary<string, string> map,
            string key,
            WotCompiledForm form)
        {
            if (map.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
            throw new ArgumentException(
                $"The compiled Modbus form requires '{key}' metadata.",
                nameof(form));
        }

        private static int GetRequiredInt(
            ImmutableDictionary<string, string> map,
            string key,
            WotCompiledForm form)
        {
            if (map.TryGetValue(key, out string? value) &&
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            {
                return result;
            }
            throw new ArgumentException(
                $"The compiled Modbus form requires integer '{key}' metadata.",
                nameof(form));
        }

        private static string GetString(
            ImmutableDictionary<string, string> map,
            string key,
            string fallback)
        {
            return map.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value)
                ? value
                : fallback;
        }

        private static bool GetBool(ImmutableDictionary<string, string> map, string key, bool fallback)
        {
            return map.TryGetValue(key, out string? value) && bool.TryParse(value, out bool result) ? result : fallback;
        }
    }
}
