/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Globalization;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Modbus;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    [TestFixture]
    public sealed class ModbusDataConverterTests
    {
        [TestCase("short", 1)]
        [TestCase("word", 1)]
        [TestCase("int", 2)]
        [TestCase("dword", 2)]
        [TestCase("single", 2)]
        [TestCase("long", 4)]
        [TestCase("ulong", 4)]
        [TestCase("double", 4)]
        [TestCase("unknown", 1)]
        [TestCase(null, 1)]
        public void RegisterCountRecognizesAliases(string? type, int expected)
        {
            Assert.That(ModbusDataConverter.RegisterCount(type!), Is.EqualTo(expected));
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void Int32RoundTripsAcrossByteAndWordOrders(bool msbFirst, bool mswFirst)
        {
            const int expected = 0x12345678;

            ushort[] registers = ModbusDataConverter.ToRegisters(
                new Variant(expected),
                "int32",
                msbFirst,
                mswFirst);
            var actual = ModbusDataConverter.ToVariant(
                registers,
                "int32",
                msbFirst,
                mswFirst);

            Assert.That(actual.AsBoxedObject(), Is.EqualTo(expected));
        }

        [TestCase("int16", -1234.0)]
        [TestCase("uint16", 54321.0)]
        [TestCase("uint32", 305419896.0)]
        [TestCase("float32", 123.5)]
        [TestCase("int64", -1234567890123.0)]
        [TestCase("uint64", 1234567890123.0)]
        [TestCase("float64", 123456.75)]
        [TestCase("unknown", 42.0)]
        public void NumericTypesRoundTrip(string type, double expected)
        {
            Variant input = type switch
            {
                "int16" => new Variant(Convert.ToInt16(expected)),
                "uint16" or "unknown" => new Variant(Convert.ToUInt16(expected)),
                "uint32" => new Variant(Convert.ToUInt32(expected)),
                "float32" => new Variant(Convert.ToSingle(expected)),
                "int64" => new Variant(Convert.ToInt64(expected)),
                "uint64" => new Variant(Convert.ToUInt64(expected)),
                _ => new Variant(expected)
            };

            ushort[] registers = ModbusDataConverter.ToRegisters(
                input,
                type,
                msbFirst: true,
                mswFirst: true);
            var actual = ModbusDataConverter.ToVariant(
                registers,
                type,
                msbFirst: true,
                mswFirst: true);

            Assert.That(
                Convert.ToDouble(actual.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(expected).Within(0.001));
        }

        [Test]
        public void ToVariantRejectsInsufficientRegisters()
        {
            ModbusException exception = Assert.Throws<ModbusException>(
                () => ModbusDataConverter.ToVariant(
                    [0x1234],
                    "int32",
                    msbFirst: true,
                    mswFirst: true))!;

            Assert.That(exception.Message, Does.Contain("requires 2 registers"));
        }
    }
}
