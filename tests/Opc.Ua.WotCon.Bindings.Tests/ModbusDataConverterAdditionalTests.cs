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
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Modbus;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Additional unit tests for <see cref="ModbusDataConverter"/> covering
    /// type aliases and byte / word order combinations not exercised by
    /// the baseline test file.
    /// </summary>
    [TestFixture]
    public sealed class ModbusDataConverterAdditionalTests
    {
        [TestCase("ushort", 1)]
        [TestCase("uint", 2)]
        [TestCase("float", 2)]
        [TestCase("int16", 1)]
        [TestCase("uint16", 1)]
        [TestCase("int32", 2)]
        [TestCase("uint32", 2)]
        [TestCase("float32", 2)]
        [TestCase("int64", 4)]
        [TestCase("uint64", 4)]
        [TestCase("float64", 4)]
        public void RegisterCountRecognizesCanonicalAndAliasNames(string type, int expected)
        {
            Assert.That(ModbusDataConverter.RegisterCount(type), Is.EqualTo(expected));
        }

        [TestCase("int16", (short)-1000)]
        [TestCase("uint16", (ushort)50000)]
        public void Int16RoundTripsWithLsbFirstByteOrder(string type, object value)
        {
            Variant input = value is short s ? new Variant(s) : new Variant((ushort)value);
            ushort[] registers = ModbusDataConverter.ToRegisters(input, type, msbFirst: false, mswFirst: true);
            Variant actual = ModbusDataConverter.ToVariant(registers, type, msbFirst: false, mswFirst: true);

            Assert.That(
                Convert.ToDouble(actual.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(Convert.ToDouble(value, CultureInfo.InvariantCulture)).Within(0.001));
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void Uint32RoundTripsAcrossAllByteWordOrders(bool msbFirst, bool mswFirst)
        {
            const uint expected = 0xDEADBEEFu;
            ushort[] registers = ModbusDataConverter.ToRegisters(
                new Variant(expected), "uint32", msbFirst, mswFirst);
            Variant actual = ModbusDataConverter.ToVariant(registers, "uint32", msbFirst, mswFirst);

            Assert.That(actual.AsBoxedObject(), Is.EqualTo(expected));
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void Float32RoundTripsAcrossAllByteWordOrders(bool msbFirst, bool mswFirst)
        {
            const float expected = -123.456f;
            ushort[] registers = ModbusDataConverter.ToRegisters(
                new Variant(expected), "float32", msbFirst, mswFirst);
            Variant actual = ModbusDataConverter.ToVariant(registers, "float32", msbFirst, mswFirst);

            Assert.That(
                System.Convert.ToSingle(actual.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(expected).Within(0.001f));
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void Int64RoundTripsAcrossAllByteWordOrders(bool msbFirst, bool mswFirst)
        {
            const long expected = -9876543210L;
            ushort[] registers = ModbusDataConverter.ToRegisters(
                new Variant(expected), "int64", msbFirst, mswFirst);
            Variant actual = ModbusDataConverter.ToVariant(registers, "int64", msbFirst, mswFirst);

            Assert.That(actual.AsBoxedObject(), Is.EqualTo(expected));
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void Uint64RoundTripsAcrossAllByteWordOrders(bool msbFirst, bool mswFirst)
        {
            const ulong expected = 0xFEDCBA9876543210uL;
            ushort[] registers = ModbusDataConverter.ToRegisters(
                new Variant(expected), "uint64", msbFirst, mswFirst);
            Variant actual = ModbusDataConverter.ToVariant(registers, "uint64", msbFirst, mswFirst);

            Assert.That(actual.AsBoxedObject(), Is.EqualTo(expected));
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void Float64RoundTripsAcrossAllByteWordOrders(bool msbFirst, bool mswFirst)
        {
            const double expected = 3.141592653589793;
            ushort[] registers = ModbusDataConverter.ToRegisters(
                new Variant(expected), "float64", msbFirst, mswFirst);
            Variant actual = ModbusDataConverter.ToVariant(registers, "float64", msbFirst, mswFirst);

            Assert.That(
                System.Convert.ToDouble(actual.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(expected).Within(1e-10));
        }

        [Test]
        public void Uint16ViaUshortAliasRoundTrips()
        {
            const ushort expected = 12345;
            ushort[] registers = ModbusDataConverter.ToRegisters(
                new Variant(expected), "ushort", msbFirst: true, mswFirst: true);
            Variant actual = ModbusDataConverter.ToVariant(registers, "ushort", msbFirst: true, mswFirst: true);

            Assert.That(actual.AsBoxedObject(), Is.EqualTo(expected));
        }

        [Test]
        public void Uint32ViaUintAliasRoundTrips()
        {
            const uint expected = 987654321u;
            ushort[] registers = ModbusDataConverter.ToRegisters(
                new Variant(expected), "uint", msbFirst: true, mswFirst: true);
            Variant actual = ModbusDataConverter.ToVariant(registers, "uint", msbFirst: true, mswFirst: true);

            Assert.That(actual.AsBoxedObject(), Is.EqualTo(expected));
        }

        [Test]
        public void Float32ViaFloatAliasRoundTrips()
        {
            const float expected = 9.99f;
            ushort[] registers = ModbusDataConverter.ToRegisters(
                new Variant(expected), "float", msbFirst: true, mswFirst: true);
            Variant actual = ModbusDataConverter.ToVariant(registers, "float", msbFirst: true, mswFirst: true);

            Assert.That(
                System.Convert.ToSingle(actual.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void DefaultTypeIsUint16WhenTypeIsNull()
        {
            const ushort expected = 42;
            ushort[] registers = ModbusDataConverter.ToRegisters(
                new Variant(expected), null!, msbFirst: true, mswFirst: true);
            Variant actual = ModbusDataConverter.ToVariant(registers, null!, msbFirst: true, mswFirst: true);

            Assert.That(actual.AsBoxedObject(), Is.EqualTo(expected));
        }

        [Test]
        public void ToVariantThrowsWhenRegisterCountIsTooSmallForInt64()
        {
            ModbusException ex = Assert.Throws<ModbusException>(
                () => ModbusDataConverter.ToVariant([0x1234, 0x5678], "int64",
                    msbFirst: true, mswFirst: true))!;

            Assert.That(ex.Message, Does.Contain("requires 4 registers"));
        }

        [Test]
        public void ToVariantThrowsWhenRegisterCountIsTooSmallForFloat32()
        {
            ModbusException ex = Assert.Throws<ModbusException>(
                () => ModbusDataConverter.ToVariant([0x1234], "float32",
                    msbFirst: true, mswFirst: true))!;

            Assert.That(ex.Message, Does.Contain("requires 2 registers"));
        }

        [Test]
        public void Int16LsbFirstAndMsbFirstProduceDifferentRegisters()
        {
            Variant value = new Variant((short)0x1234);

            ushort[] msb = ModbusDataConverter.ToRegisters(value, "int16", msbFirst: true, mswFirst: true);
            ushort[] lsb = ModbusDataConverter.ToRegisters(value, "int16", msbFirst: false, mswFirst: true);

            // MSB first: high byte in high bits of register word.
            // LSB first: bytes are swapped within the register.
            Assert.That(msb[0], Is.Not.EqualTo(lsb[0]));
        }

        [Test]
        public void Int32MswFirstAndLswFirstProduceDifferentRegisterOrder()
        {
            const int value = 0x12345678;

            ushort[] msw = ModbusDataConverter.ToRegisters(
                new Variant(value), "int32", msbFirst: true, mswFirst: true);
            ushort[] lsw = ModbusDataConverter.ToRegisters(
                new Variant(value), "int32", msbFirst: true, mswFirst: false);

            // The register order should be reversed.
            Assert.That(msw[0], Is.EqualTo(lsw[1]));
            Assert.That(msw[1], Is.EqualTo(lsw[0]));
        }
    }
}
