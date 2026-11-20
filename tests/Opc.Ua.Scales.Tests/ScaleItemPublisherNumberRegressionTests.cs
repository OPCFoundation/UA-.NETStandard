// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;
using Opc.Ua.Scales.Server.Runtime;

namespace Opc.Ua.Scales.Tests
{
    [TestFixture]
    [Category("Scales")]
    public sealed class ScaleItemPublisherNumberRegressionTests
    {
        /// <summary>Unsupported values and non-finite numbers rejected by the publisher.</summary>
        private static readonly Variant[] s_unsupportedVariants =
        [
            Variant.Null,
            new Variant("123.5"),
            new Variant("not-a-number"),
            new Variant(true),
            new Variant(new LocalizedText("not a numeric value")),
            new Variant(new[] { 1, 2, 3 }),
            new Variant(float.NaN),
            new Variant(float.PositiveInfinity),
            new Variant(float.NegativeInfinity),
            new Variant(double.NaN),
            new Variant(double.PositiveInfinity),
            new Variant(double.NegativeInfinity)
        ];

        [Test]
        public void TryNumberReturnsExpectedDoubleForSupportedNumericVariantTypes()
        {
            (string Name, Variant Value, double Expected)[] cases =
            [
                ("Double", new Variant(12.625d), 12.625d),
                ("Float", new Variant(-3.25f), -3.25d),
                ("SByte", new Variant((sbyte)-12), -12d),
                ("ByteZero", new Variant((byte)0), 0d),
                ("Int16", new Variant((short)-1234), -1234d),
                ("UInt16", new Variant((ushort)60000), 60000d),
                ("Int32", new Variant(-123456789), -123456789d),
                ("UInt32Max", new Variant(uint.MaxValue), (double)uint.MaxValue),
                ("Int64AboveUInt32", new Variant(5_000_000_000L), 5_000_000_000d),
                ("UInt64AboveUInt32", new Variant(8_000_000_000_000_000UL), 8_000_000_000_000_000d)
            ];

            foreach ((string name, Variant value, double expected) in cases)
            {
                bool converted = ScaleItemPublisher.TryNumber(value, out double actual);

                Assert.Multiple(() =>
                {
                    Assert.That(converted, Is.True, $"{name} should be accepted.");
                    Assert.That(actual, Is.EqualTo(expected), $"{name} should retain its exactly representable value.");
                });
            }
        }

        [Test]
        public void TryNumberRejectsUnsupportedOrNonFiniteVariants()
        {
            for (int index = 0; index < s_unsupportedVariants.Length; index++)
            {
                bool converted = ScaleItemPublisher.TryNumber(s_unsupportedVariants[index], out _);
                Assert.That(converted, Is.False, $"Variant at index {index} should be rejected.");
            }
        }

        [Test]
        public void TryUnsignedReadsEachSupportedUnsignedIntegerWidth()
        {
            (string Name, Variant Value, ulong Expected)[] cases =
            [
                ("UInt64", new Variant(9_000_000_000UL), 9_000_000_000UL),
                ("UInt32", new Variant(4_000_000_000U), 4_000_000_000UL),
                ("UInt16", new Variant((ushort)60_000), 60_000UL),
                ("Byte", new Variant((byte)255), 255UL)
            ];

            foreach ((string name, Variant value, ulong expected) in cases)
            {
                bool converted = ScaleItemPublisher.TryUnsigned(value, out ulong actual);

                Assert.Multiple(() =>
                {
                    Assert.That(converted, Is.True, $"{name} should be accepted.");
                    Assert.That(actual, Is.EqualTo(expected), $"{name} should retain its unsigned value.");
                });
            }
        }

        [Test]
        public void TryUnsignedRejectsSignedAndNonIntegerVariants()
        {
            Variant[] unsupported =
            [
                new Variant(-1),
                new Variant(1),
                new Variant(1.0),
                new Variant("1")
            ];

            foreach (Variant value in unsupported)
            {
                bool converted = ScaleItemPublisher.TryUnsigned(value, out _);

                Assert.That(converted, Is.False, $"{value} should not be accepted as UInteger.");
            }
        }
    }
}
