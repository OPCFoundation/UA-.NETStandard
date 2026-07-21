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

namespace Opc.Ua.WotCon.Binding.Modbus
{
    /// <summary>
    /// The Modbus register addressing parsed from a compiled form, re-validated by
    /// the executor before the values are narrowed to <see cref="ushort"/> /
    /// <see cref="byte"/>. Although the planner already enforces the same bounds,
    /// the executor independently re-checks them so a hand-built or tampered
    /// compiled form can never silently truncate an out-of-range address or
    /// quantity through an unchecked cast.
    /// </summary>
    internal readonly struct ModbusAddressing
    {
        private const int MaxAddress = 65535;

        private ModbusAddressing(
            string entity, ushort address, ushort quantity, byte unitId,
            string type, bool msbFirst, bool mswFirst)
        {
            Entity = entity;
            Address = address;
            Quantity = quantity;
            UnitId = unitId;
            Type = type;
            MsbFirst = msbFirst;
            MswFirst = mswFirst;
        }

        public string Entity { get; }

        public ushort Address { get; }

        public ushort Quantity { get; }

        public byte UnitId { get; }

        public string Type { get; }

        public bool MsbFirst { get; }

        public bool MswFirst { get; }

        /// <summary>
        /// Parses and validates the addressing carried by a compiled Modbus form,
        /// throwing <see cref="ArgumentOutOfRangeException"/> when the address,
        /// quantity, addressed range or unit id is out of the Modbus bounds.
        /// </summary>
        public static ModbusAddressing FromForm(WotCompiledForm form)
        {
            if (form is null)
            {
                throw new ArgumentNullException(nameof(form));
            }
            ImmutableDictionary<string, string> map = form.Addressing.Metadata;
            string entity = GetString(map, "entity", "holdingRegister");
            int address = GetInt(map, "address", 0);
            int quantity = Math.Max(1, GetInt(map, "quantity", 1));
            int unitId = GetInt(map, "unitId", 1);

            if (address is < 0 or > MaxAddress)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(form), address, $"The Modbus address must be between 0 and {MaxAddress}.");
            }
            if (quantity is < 1 or > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(form), quantity, $"The Modbus quantity must be between 1 and {ushort.MaxValue}.");
            }
            if (address + quantity - 1 > MaxAddress)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(form), address,
                    $"The Modbus range starting at {address} for {quantity} items exceeds the maximum " +
                    $"address {MaxAddress}.");
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
                entity, (ushort)address, (ushort)quantity, (byte)unitId, type, msbFirst, mswFirst);
        }

        private static string GetString(ImmutableDictionary<string, string> map, string key, string fallback)
            => map.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value) ? value : fallback;

        private static int GetInt(ImmutableDictionary<string, string> map, string key, int fallback)
            => map.TryGetValue(key, out string? value) &&
               int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? result : fallback;

        private static bool GetBool(ImmutableDictionary<string, string> map, string key, bool fallback)
            => map.TryGetValue(key, out string? value) && bool.TryParse(value, out bool result) ? result : fallback;
    }
}
