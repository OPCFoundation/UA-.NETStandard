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
using NUnit.Framework;
using Opc.Ua.Test;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    /// <summary>
    /// Verifies generated doubles preserve random bits and typed variant arrays honor scalar and boundary selections.
    /// </summary>
    [TestFixture]
    [Category("DataGenerator")]
    [Parallelizable(ParallelScope.All)]
    public sealed class DataGeneratorTypeRegressionTests
    {
        /// <summary>
        /// Verifies double generation preserves the exact 64-bit input representation without narrowing to Single.
        /// </summary>
        [TestCase(1.0000000000000002)]
        [TestCase(-1.25)]
        [TestCase(double.MaxValue)]
        public void RandomDoublePreservesAllSixtyFourRandomBits(double expected)
        {
            var generator = new DataGenerator(
                new FixedRandomBytes(BitConverter.GetBytes(expected)),
                NUnitTelemetryContext.Create());
            double actual = generator.GetRandomDouble();
            Assert.That(
                BitConverter.DoubleToInt64Bits(actual),
                Is.EqualTo(BitConverter.DoubleToInt64Bits(expected)));
        }

        /// <summary>
        /// Verifies typed variant arrays use the selected scalar built-in type for every element.
        /// </summary>
        [TestCase("unsigned", BuiltInType.UInt16)]
        [TestCase("integer", BuiltInType.Int16)]
        [TestCase("number", BuiltInType.Byte)]
        [TestCase("variant", BuiltInType.SByte)]
        public void TypedVariantArraysUseTheSelectedScalarType(string family, BuiltInType expectedType)
        {
            var generator = new DataGenerator(new UnitRandomSource(), NUnitTelemetryContext.Create());
            Variant[] values = family switch
            {
                "unsigned" => generator.GetRandomUIntegerArray(false, 4, true),
                "integer" => generator.GetRandomIntegerArray(false, 4, true),
                "number" => generator.GetRandomNumberArray(false, 4, true),
                _ => generator.GetRandomVariantArray(false, 4, true)
            };
            Assert.That(values, Has.Length.EqualTo(4));
            foreach (Variant value in values)
            {
                Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(expectedType));
                Assert.That(value.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            }
        }

        /// <summary>
        /// Verifies boundary-value selection remains distinct from ordinary typed-array generation.
        /// </summary>
        [Test]
        public void TypedVariantArraysHonorBoundaryValueSelection()
        {
            var generator = new DataGenerator(new UnitRandomSource(), NUnitTelemetryContext.Create())
            {
                BoundaryValueFrequency = 100
            };
            Variant[] ordinary = generator.GetRandomUIntegerArray(false, 2, true);
            Variant[] boundary = generator.GetRandomUIntegerArray(true, 2, true);
            Assert.That(ordinary[0].TryGetValue(out ushort ordinaryValue), Is.True);
            Assert.That(boundary[0].TryGetValue(out ushort boundaryValue), Is.True);
            Assert.That(ordinaryValue, Is.EqualTo(1));
            Assert.That(boundaryValue, Is.EqualTo(ushort.MaxValue));
        }

        /// <summary>
        /// Supplies an exact byte sequence to expose any loss of floating-point precision.
        /// </summary>
        private sealed class FixedRandomBytes : Test.ISecureRandomSource
        {
            /// <summary>
            /// Captures the bit pattern that each byte request must consume in full.
            /// </summary>
            public FixedRandomBytes(byte[] bytes)
            {
                m_bytes = bytes;
            }

            /// <summary>
            /// Copies the fixed bytes and verifies the generator requested the complete representation.
            /// </summary>
            public void NextBytes(byte[] bytes, int offset, int count)
            {
                Assert.That(count, Is.EqualTo(m_bytes.Length));
                m_bytes.AsSpan().CopyTo(bytes.AsSpan(offset, count));
            }

            /// <summary>
            /// Selects the first available alternative deterministically.
            /// </summary>
            public int NextInt32(int max)
            {
                return 0;
            }

            /// <summary>
            /// Stores the exact floating-point representation supplied to the generator.
            /// </summary>
            private readonly byte[] m_bytes;
        }

        /// <summary>
        /// Uses deterministic unit-valued choices to distinguish typed values from boundary-value substitutions.
        /// </summary>
        private sealed class UnitRandomSource : Test.ISecureRandomSource
        {
            /// <summary>
            /// Fills the requested byte range with the deterministic ordinary value.
            /// </summary>
            public void NextBytes(byte[] bytes, int offset, int count)
            {
                bytes.AsSpan(offset, count).Fill(1);
            }

            /// <summary>
            /// Returns one when a choice exists and zero for an empty range.
            /// </summary>
            public int NextInt32(int max)
            {
                return max == 0 ? 0 : 1;
            }
        }
    }
}
