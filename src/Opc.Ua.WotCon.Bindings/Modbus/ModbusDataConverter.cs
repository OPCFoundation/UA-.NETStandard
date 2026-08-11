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
using System.Globalization;

namespace Opc.Ua.WotCon.Bindings.Modbus
{
    /// <summary>
    /// Converts between Modbus register words and OPC UA values, honouring the
    /// <c>modv:type</c> data type and the byte / word order flags
    /// (<c>modv:mostSignificantByte</c> / <c>modv:mostSignificantWord</c>).
    /// </summary>
    internal static class ModbusDataConverter
    {
        public static int RegisterCount(string type)
        {
            return ModbusDataTypes.RegisterCount(type);
        }

        public static Variant ToVariant(ushort[] registers, string type, bool msbFirst, bool mswFirst)
        {
            string normalized = ModbusDataTypes.Normalize(type);
            int needed = RegisterCount(normalized);
            if (registers.Length < needed)
            {
                throw new ModbusException(
                    $"The Modbus data type '{type}' requires {needed} registers but {registers.Length} were read.");
            }
            ushort[] slice = new ushort[needed];
            Array.Copy(registers, slice, needed);
            byte[] bigEndian = Canonical(slice, msbFirst, mswFirst);
            byte[] host = ToHostOrder(bigEndian);
            return normalized switch
            {
                "int16" => new Variant(BitConverter.ToInt16(host, 0)),
                "uint16" => new Variant(BitConverter.ToUInt16(host, 0)),
                "int32" => new Variant(BitConverter.ToInt32(host, 0)),
                "uint32" => new Variant(BitConverter.ToUInt32(host, 0)),
                "float32" => new Variant(BitConverter.ToSingle(host, 0)),
                "int64" => new Variant(BitConverter.ToInt64(host, 0)),
                "uint64" => new Variant(BitConverter.ToUInt64(host, 0)),
                "float64" => new Variant(BitConverter.ToDouble(host, 0)),
                _ => new Variant(BitConverter.ToUInt16(host, 0))
            };
        }

        public static ushort[] ToRegisters(Variant value, string type, bool msbFirst, bool mswFirst)
        {
            string normalized = ModbusDataTypes.Normalize(type);
            byte[] bigEndian = normalized switch
            {
                "int16" => BigEndianBytes(BitConverter.GetBytes(ToInt16(value))),
                "uint16" => BigEndianBytes(BitConverter.GetBytes(ToUInt16(value))),
                "int32" => BigEndianBytes(
                    BitConverter.GetBytes(Convert.ToInt32(BoxOf(value), CultureInfo.InvariantCulture))),
                "uint32" => BigEndianBytes(
                    BitConverter.GetBytes(Convert.ToUInt32(BoxOf(value), CultureInfo.InvariantCulture))),
                "float32" => BigEndianBytes(
                    BitConverter.GetBytes(Convert.ToSingle(BoxOf(value), CultureInfo.InvariantCulture))),
                "int64" => BigEndianBytes(
                    BitConverter.GetBytes(Convert.ToInt64(BoxOf(value), CultureInfo.InvariantCulture))),
                "uint64" => BigEndianBytes(
                    BitConverter.GetBytes(Convert.ToUInt64(BoxOf(value), CultureInfo.InvariantCulture))),
                "float64" => BigEndianBytes(
                    BitConverter.GetBytes(Convert.ToDouble(BoxOf(value), CultureInfo.InvariantCulture))),
                _ => BigEndianBytes(BitConverter.GetBytes(ToUInt16(value)))
            };
            return FromCanonical(bigEndian, msbFirst, mswFirst);
        }

        private static byte[] Canonical(ushort[] registers, bool msbFirst, bool mswFirst)
        {
            int words = registers.Length;
            byte[][] wordBytes = new byte[words][];
            for (int i = 0; i < words; i++)
            {
                byte hi = (byte)(registers[i] >> 8);
                byte lo = (byte)(registers[i] & 0xFF);
                wordBytes[i] = msbFirst ? [hi, lo] : [lo, hi];
            }
            if (!mswFirst)
            {
                Array.Reverse(wordBytes);
            }
            byte[] result = new byte[words * 2];
            for (int i = 0; i < words; i++)
            {
                result[i * 2] = wordBytes[i][0];
                result[(i * 2) + 1] = wordBytes[i][1];
            }
            return result;
        }

        private static ushort[] FromCanonical(byte[] bigEndian, bool msbFirst, bool mswFirst)
        {
            int words = bigEndian.Length / 2;
            byte[][] wordBytes = new byte[words][];
            for (int i = 0; i < words; i++)
            {
                wordBytes[i] = [bigEndian[i * 2], bigEndian[(i * 2) + 1]];
            }
            if (!mswFirst)
            {
                Array.Reverse(wordBytes);
            }
            ushort[] registers = new ushort[words];
            for (int i = 0; i < words; i++)
            {
                byte b0 = wordBytes[i][0];
                byte b1 = wordBytes[i][1];
                byte hi = msbFirst ? b0 : b1;
                byte lo = msbFirst ? b1 : b0;
                registers[i] = (ushort)((hi << 8) | lo);
            }
            return registers;
        }

        private static byte[] ToHostOrder(byte[] bigEndian)
        {
            byte[] copy = (byte[])bigEndian.Clone();
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(copy);
            }
            return copy;
        }

        private static byte[] BigEndianBytes(byte[] hostOrder)
        {
            byte[] copy = (byte[])hostOrder.Clone();
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(copy);
            }
            return copy;
        }

        private static object BoxOf(Variant value)
        {
            return value.AsBoxedObject() ?? 0;
        }

        private static short ToInt16(Variant value)
        {
            return Convert.ToInt16(BoxOf(value), CultureInfo.InvariantCulture);
        }

        private static ushort ToUInt16(Variant value)
        {
            return Convert.ToUInt16(BoxOf(value), CultureInfo.InvariantCulture);
        }
    }
}
