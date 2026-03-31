/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Core.Tests.Types.DataGeneratorTests
{
    /// <summary>
    /// Tests for <see cref="RandomSource"/> and <see cref="DataGenerator"/>.
    /// </summary>
    [TestFixture]
    [Category("DataGenerator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class RandomSourceTests
    {
        #region RandomSource

        /// <summary>
        /// RandomSource.Default returns a non-null instance.
        /// </summary>
        [Test]
        public void DefaultPropertyReturnsNonNullInstance()
        {
            Assert.That(RandomSource.Default, Is.Not.Null);
        }

        /// <summary>
        /// NextBytes fills the full buffer with random bytes.
        /// </summary>
        [Test]
        public void NextBytesFillsBuffer()
        {
            var source = new RandomSource(42);
            byte[] buffer = new byte[16];
            source.NextBytes(buffer, 0, buffer.Length);
            // A buffer of 16 bytes of all zeros is astronomically unlikely if the
            // random source works correctly.
            bool allZero = true;
            foreach (byte b in buffer)
            {
                if (b != 0)
                {
                    allZero = false;
                    break;
                }
            }
            Assert.That(allZero, Is.False);
        }

        /// <summary>
        /// NextBytes with offset fills only the specified range.
        /// </summary>
        [Test]
        public void NextBytesWithOffsetFillsSpecifiedRange()
        {
            var source = new RandomSource(42);
            byte[] buffer = new byte[16];
            // Fill only positions 4..7
            source.NextBytes(buffer, 4, 4);
            // Positions 0..3 and 8..15 must remain zero
            for (int i = 0; i < 4; i++)
            {
                Assert.That(buffer[i], Is.EqualTo(0), $"Position {i} should be zero");
            }
            for (int i = 8; i < 16; i++)
            {
                Assert.That(buffer[i], Is.EqualTo(0), $"Position {i} should be zero");
            }
        }

        /// <summary>
        /// NextBytes with a null buffer throws ArgumentNullException.
        /// </summary>
        [Test]
        public void NextBytesNullBufferThrowsArgumentNullException()
        {
            var source = new RandomSource();
            Assert.Throws<ArgumentNullException>(() => source.NextBytes(null, 0, 4));
        }

        /// <summary>
        /// NextBytes with a negative offset throws ArgumentOutOfRangeException.
        /// </summary>
        [Test]
        public void NextBytesNegativeOffsetThrowsArgumentOutOfRangeException()
        {
            var source = new RandomSource();
            byte[] buffer = new byte[8];
            Assert.Throws<ArgumentOutOfRangeException>(() => source.NextBytes(buffer, -1, 4));
        }

        /// <summary>
        /// NextBytes with a negative count throws ArgumentOutOfRangeException.
        /// </summary>
        [Test]
        public void NextBytesNegativeCountThrowsArgumentOutOfRangeException()
        {
            var source = new RandomSource();
            byte[] buffer = new byte[8];
            Assert.Throws<ArgumentOutOfRangeException>(() => source.NextBytes(buffer, 0, -1));
        }

        /// <summary>
        /// NextBytes with count that exceeds buffer size throws ArgumentOutOfRangeException.
        /// </summary>
        [Test]
        public void NextBytesExceedingBufferThrowsArgumentOutOfRangeException()
        {
            var source = new RandomSource();
            byte[] buffer = new byte[4];
            Assert.Throws<ArgumentOutOfRangeException>(() => source.NextBytes(buffer, 0, 8));
        }

        /// <summary>
        /// NextInt32 with max 0 returns 0.
        /// </summary>
        [Test]
        public void NextInt32MaxZeroReturnsZero()
        {
            var source = new RandomSource();
            int result = source.NextInt32(0);
            Assert.That(result, Is.EqualTo(0));
        }

        /// <summary>
        /// NextInt32 returns values in range [0, max] inclusive.
        /// </summary>
        [Test]
        public void NextInt32ReturnsValueInRange()
        {
            var source = new RandomSource(42);
            for (int i = 0; i < 100; i++)
            {
                int result = source.NextInt32(10);
                Assert.That(result, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(10));
            }
        }

        /// <summary>
        /// NextInt32 with a negative max throws ArgumentOutOfRangeException.
        /// </summary>
        [Test]
        public void NextInt32NegativeMaxThrowsArgumentOutOfRangeException()
        {
            var source = new RandomSource();
            Assert.Throws<ArgumentOutOfRangeException>(() => source.NextInt32(-1));
        }

        /// <summary>
        /// Two RandomSource instances with the same seed produce the same sequence.
        /// </summary>
        [Test]
        public void SameSeedProducesSameSequence()
        {
            var source1 = new RandomSource(12345);
            var source2 = new RandomSource(12345);

            for (int i = 0; i < 20; i++)
            {
                Assert.That(source1.NextInt32(100), Is.EqualTo(source2.NextInt32(100)));
            }
        }

        #endregion
    }

    /// <summary>
    /// Tests for <see cref="DataGenerator"/>.
    /// </summary>
    [TestFixture]
    [Category("DataGenerator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DataGeneratorTests
    {
        private DataGenerator m_generator;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_generator = new DataGenerator(new RandomSource(42), telemetry);
        }

        #region Primitive scalar generators

        /// <summary>
        /// GetRandomBoolean returns a boolean (true or false) without throwing.
        /// </summary>
        [Test]
        public void GetRandomBooleanReturnsBool()
        {
            bool value = m_generator.GetRandomBoolean();
            Assert.That(value, Is.TypeOf<bool>());
        }

        /// <summary>
        /// GetRandomSByte returns a value in the valid sbyte range.
        /// </summary>
        [Test]
        public void GetRandomSByteReturnsSByteRange()
        {
            for (int i = 0; i < 50; i++)
            {
                sbyte value = m_generator.GetRandomSByte();
                Assert.That(value, Is.GreaterThanOrEqualTo(sbyte.MinValue).And.LessThanOrEqualTo(sbyte.MaxValue));
            }
        }

        /// <summary>
        /// GetRandomByte returns a value in the valid byte range.
        /// </summary>
        [Test]
        public void GetRandomByteReturnsByteRange()
        {
            for (int i = 0; i < 50; i++)
            {
                byte value = m_generator.GetRandomByte();
                Assert.That(value, Is.GreaterThanOrEqualTo(byte.MinValue).And.LessThanOrEqualTo(byte.MaxValue));
            }
        }

        /// <summary>
        /// GetRandomInt16 returns a value in the valid short range.
        /// </summary>
        [Test]
        public void GetRandomInt16ReturnsInt16Range()
        {
            for (int i = 0; i < 50; i++)
            {
                short value = m_generator.GetRandomInt16();
                Assert.That(value, Is.GreaterThanOrEqualTo(short.MinValue).And.LessThanOrEqualTo(short.MaxValue));
            }
        }

        /// <summary>
        /// GetRandomUInt16 returns a value in the valid ushort range.
        /// </summary>
        [Test]
        public void GetRandomUInt16ReturnsUInt16Range()
        {
            for (int i = 0; i < 50; i++)
            {
                ushort value = m_generator.GetRandomUInt16();
                Assert.That(value, Is.GreaterThanOrEqualTo(ushort.MinValue).And.LessThanOrEqualTo(ushort.MaxValue));
            }
        }

        /// <summary>
        /// GetRandomInt32 returns a value in the valid int range.
        /// </summary>
        [Test]
        public void GetRandomInt32ReturnsInt32Range()
        {
            for (int i = 0; i < 50; i++)
            {
                int value = m_generator.GetRandomInt32();
                Assert.That(value, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(int.MaxValue));
            }
        }

        /// <summary>
        /// GetRandomUInt32 returns a value that fits in a uint.
        /// </summary>
        [Test]
        public void GetRandomUInt32ReturnsUInt32Range()
        {
            for (int i = 0; i < 20; i++)
            {
                uint value = m_generator.GetRandomUInt32();
                Assert.That(value, Is.GreaterThanOrEqualTo(uint.MinValue).And.LessThanOrEqualTo(uint.MaxValue));
            }
        }

        /// <summary>
        /// GetRandomInt64 returns any long value without throwing.
        /// </summary>
        [Test]
        public void GetRandomInt64ReturnsLong()
        {
            for (int i = 0; i < 20; i++)
            {
                _ = m_generator.GetRandomInt64();
            }
            Assert.Pass("GetRandomInt64 did not throw");
        }

        /// <summary>
        /// GetRandomUInt64 returns any ulong value without throwing.
        /// </summary>
        [Test]
        public void GetRandomUInt64ReturnsULong()
        {
            for (int i = 0; i < 20; i++)
            {
                _ = m_generator.GetRandomUInt64();
            }
            Assert.Pass("GetRandomUInt64 did not throw");
        }

        /// <summary>
        /// GetRandomFloat returns a float value without throwing.
        /// </summary>
        [Test]
        public void GetRandomFloatReturnsFloat()
        {
            for (int i = 0; i < 20; i++)
            {
                _ = m_generator.GetRandomFloat();
            }
            Assert.Pass("GetRandomFloat did not throw");
        }

        /// <summary>
        /// GetRandomDouble returns a double value without throwing.
        /// </summary>
        [Test]
        public void GetRandomDoubleReturnsDouble()
        {
            for (int i = 0; i < 20; i++)
            {
                _ = m_generator.GetRandomDouble();
            }
            Assert.Pass("GetRandomDouble did not throw");
        }

        #endregion

        #region String generators

        /// <summary>
        /// GetRandomString returns a non-null string within MaxStringLength.
        /// </summary>
        [Test]
        public void GetRandomStringReturnsNonNullString()
        {
            string value = m_generator.GetRandomString();
            Assert.That(value, Is.Not.Null);
        }

        /// <summary>
        /// GetRandomString with a locale returns a string.
        /// </summary>
        [Test]
        public void GetRandomStringWithLocaleReturnsString()
        {
            string value = m_generator.GetRandomString("en");
            Assert.That(value, Is.Not.Null);
        }

        /// <summary>
        /// GetRandomSymbol returns a non-null string.
        /// </summary>
        [Test]
        public void GetRandomSymbolReturnsNonNullString()
        {
            string value = m_generator.GetRandomSymbol();
            Assert.That(value, Is.Not.Null);
        }

        #endregion

        #region Structured value generators

        /// <summary>
        /// GetRandomDateTime returns a value within the configured date range.
        /// </summary>
        [Test]
        public void GetRandomDateTimeReturnsValueInConfiguredRange()
        {
            for (int i = 0; i < 20; i++)
            {
                DateTimeUtc dt = m_generator.GetRandomDateTime();
                Assert.That(dt.Value, Is.GreaterThanOrEqualTo(m_generator.MinDateTimeValue.Value)
                    .And.LessThanOrEqualTo(m_generator.MaxDateTimeValue.Value));
            }
        }

        /// <summary>
        /// GetRandomGuid returns distinct values across calls (not always the same).
        /// </summary>
        [Test]
        public void GetRandomGuidReturnsDifferentValues()
        {
            var guids = new System.Collections.Generic.HashSet<Uuid>();
            for (int i = 0; i < 20; i++)
            {
                guids.Add(m_generator.GetRandomGuid());
            }
            // With a seeded RNG, should produce more than 1 unique Uuid across 20 calls
            Assert.That(guids.Count, Is.GreaterThan(1));
        }

        /// <summary>
        /// GetRandomByteString returns a non-null byte string.
        /// </summary>
        [Test]
        public void GetRandomByteStringReturnsNonNull()
        {
            ByteString bs = m_generator.GetRandomByteString();
            // ByteString can be empty but must not throw
            Assert.Pass("GetRandomByteString did not throw");
        }

        /// <summary>
        /// GetRandomNodeId returns a non-null node ID.
        /// </summary>
        [Test]
        public void GetRandomNodeIdReturnsNonNull()
        {
            NodeId nodeId = m_generator.GetRandomNodeId();
            Assert.That(nodeId, Is.Not.EqualTo(NodeId.Null));
        }

        /// <summary>
        /// GetRandomQualifiedName returns a qualified name with a non-null Name.
        /// </summary>
        [Test]
        public void GetRandomQualifiedNameReturnsNonNull()
        {
            QualifiedName qn = m_generator.GetRandomQualifiedName();
            Assert.That(qn.Name, Is.Not.Null);
        }

        /// <summary>
        /// GetRandomLocalizedText returns a non-null localized text.
        /// </summary>
        [Test]
        public void GetRandomLocalizedTextReturnsNonNull()
        {
            LocalizedText lt = m_generator.GetRandomLocalizedText();
            Assert.That(lt.IsNullOrEmpty, Is.False);
        }

        /// <summary>
        /// GetRandomStatusCode returns a valid StatusCode.
        /// </summary>
        [Test]
        public void GetRandomStatusCodeReturnsValidStatusCode()
        {
            for (int i = 0; i < 20; i++)
            {
                _ = m_generator.GetRandomStatusCode();
            }
            Assert.Pass("GetRandomStatusCode did not throw");
        }

        /// <summary>
        /// GetRandomVariant returns a variant with a non-null TypeInfo.
        /// </summary>
        [Test]
        public void GetRandomVariantReturnsNonEmpty()
        {
            Variant v = m_generator.GetRandomVariant();
            Assert.That(v.TypeInfo.BuiltInType, Is.Not.EqualTo(BuiltInType.Null));
        }

        /// <summary>
        /// GetRandomDataValue returns a non-null data value.
        /// </summary>
        [Test]
        public void GetRandomDataValueReturnsNonNull()
        {
            DataValue dv = m_generator.GetRandomDataValue();
            Assert.That(dv, Is.Not.Null);
        }

        /// <summary>
        /// GetRandomXmlElement returns a non-null XmlElement.
        /// </summary>
        [Test]
        public void GetRandomXmlElementReturnsNonNull()
        {
            XmlElement xe = m_generator.GetRandomXmlElement();
            Assert.That(xe.IsNull, Is.False);
        }

        #endregion

        #region Array generators

        /// <summary>
        /// GetRandomArray returns an array of the requested element type with the
        /// correct length when fixedLength is true.
        /// </summary>
        [Test]
        public void GetRandomArrayFixedLengthReturnsCorrectLength()
        {
            int[] arr = m_generator.GetRandomArray<int>(false, 10, true);
            Assert.That(arr, Is.Not.Null);
            Assert.That(arr.Length, Is.EqualTo(10));
        }

        /// <summary>
        /// GetNullArray returns an array of nulls (default values for T).
        /// </summary>
        [Test]
        public void GetNullArrayReturnsArrayOfDefaults()
        {
            string[] arr = m_generator.GetNullArray<string>(5, true);
            Assert.That(arr, Is.Not.Null);
            Assert.That(arr.Length, Is.EqualTo(5));
            foreach (string s in arr)
            {
                Assert.That(s, Is.Null);
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// MaxArrayLength property can be read and set.
        /// </summary>
        [Test]
        public void MaxArrayLengthPropertyCanBeSet()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var gen = new DataGenerator(new RandomSource(), telemetry)
            {
                MaxArrayLength = 50
            };
            Assert.That(gen.MaxArrayLength, Is.EqualTo(50));
        }

        /// <summary>
        /// MaxStringLength property can be read and set.
        /// </summary>
        [Test]
        public void MaxStringLengthPropertyCanBeSet()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var gen = new DataGenerator(new RandomSource(), telemetry)
            {
                MaxStringLength = 20
            };
            Assert.That(gen.MaxStringLength, Is.EqualTo(20));
        }

        /// <summary>
        /// BoundaryValueFrequency property can be read and set.
        /// </summary>
        [Test]
        public void BoundaryValueFrequencyPropertyCanBeSet()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var gen = new DataGenerator(new RandomSource(), telemetry)
            {
                BoundaryValueFrequency = 10
            };
            Assert.That(gen.BoundaryValueFrequency, Is.EqualTo(10));
        }

        #endregion
    }
}
