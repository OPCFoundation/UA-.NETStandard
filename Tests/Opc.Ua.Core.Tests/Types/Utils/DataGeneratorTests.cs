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

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    [TestFixture]
    [Category("DataGenerator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class RandomSourceTests
    {
        [Test]
        public void DefaultConstructorCreatesInstance()
        {
            var source = new RandomSource();
            Assert.That(source, Is.Not.Null);
        }

        [Test]
        public void SeededConstructorCreatesReproducibleInstance()
        {
            var source1 = new RandomSource(42);
            var source2 = new RandomSource(42);
            Assert.That(source1.NextInt32(1000), Is.EqualTo(source2.NextInt32(1000)));
        }

        [Test]
        public void DefaultPropertyReturnsSharedInstance()
        {
            RandomSource source = RandomSource.Default;
            Assert.That(source, Is.Not.Null);
            Assert.That(RandomSource.Default, Is.SameAs(source));
        }

        [Test]
        public void NextInt32ReturnsValueInRange()
        {
            var source = new RandomSource(42);
            int result = source.NextInt32(100);
            Assert.That(result, Is.InRange(0, 100));
        }

        [Test]
        public void NextInt32WithZeroMaxReturnsZero()
        {
            var source = new RandomSource(42);
            int result = source.NextInt32(0);
            Assert.That(result, Is.EqualTo(0));
        }

        [Test]
        public void NextInt32WithNegativeMaxThrows()
        {
            var source = new RandomSource(42);
            Assert.That(() => source.NextInt32(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void NextInt32WithMaxIntReturnsNonNegativeValue()
        {
            var source = new RandomSource(42);
            int result = source.NextInt32(int.MaxValue);
            Assert.That(result, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void NextBytesFillsEntireArray()
        {
            var source = new RandomSource(42);
            byte[] bytes = new byte[16];
            source.NextBytes(bytes, 0, bytes.Length);
            bool hasNonZero = false;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != 0)
                { hasNonZero = true; break; }
            }
            Assert.That(hasNonZero, Is.True);
        }

        [Test]
        public void NextBytesWithOffsetFillsPartialArray()
        {
            var source = new RandomSource(42);
            byte[] bytes = new byte[10];
            source.NextBytes(bytes, 5, 5);
            for (int i = 0; i < 5; i++)
            {
                Assert.That(bytes[i], Is.EqualTo(0));
            }
        }

        [Test]
        public void NextBytesWithNullArrayThrows()
        {
            var source = new RandomSource(42);
            Assert.That(() => source.NextBytes(null, 0, 1), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void NextBytesWithNegativeOffsetThrows()
        {
            var source = new RandomSource(42);
            byte[] bytes = new byte[10];
            Assert.That(() => source.NextBytes(bytes, -1, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void NextBytesWithOffsetBeyondArrayThrows()
        {
            var source = new RandomSource(42);
            byte[] bytes = new byte[10];
            Assert.That(() => source.NextBytes(bytes, 10, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void NextBytesWithNegativeCountThrows()
        {
            var source = new RandomSource(42);
            byte[] bytes = new byte[10];
            Assert.That(() => source.NextBytes(bytes, 0, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void NextBytesWithCountExceedingArrayThrows()
        {
            var source = new RandomSource(42);
            byte[] bytes = new byte[10];
            Assert.That(() => source.NextBytes(bytes, 0, 11), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void NextBytesWithEmptyArrayDoesNotThrow()
        {
            var source = new RandomSource(42);
            byte[] bytes = Array.Empty<byte>();
            Assert.That(() => source.NextBytes(bytes, 0, 0), Throws.Nothing);
        }
    }

    [TestFixture]
    [Category("DataGenerator")]
    public class DataGeneratorTests
    {
        private ITelemetryContext m_telemetry;
        private DataGenerator m_generator;
        private DataGenerator m_boundaryGenerator;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_generator = new DataGenerator(new RandomSource(42), m_telemetry);
            m_boundaryGenerator = new DataGenerator(new RandomSource(99), m_telemetry);
            m_boundaryGenerator.BoundaryValueFrequency = 100;
        }

        [Test]
        public void ConstructorWithNullRandomSourceCreatesWorkingGenerator()
        {
            var generator = new DataGenerator(null, m_telemetry);
            Assert.That(generator, Is.Not.Null);
            bool value = generator.GetRandomBoolean();
            Assert.That(value, Is.TypeOf<bool>());
        }

        [Test]
        public void ConstructorSetsDefaultPropertyValues()
        {
            var generator = new DataGenerator(new RandomSource(1), m_telemetry);
            Assert.That(generator.MaxArrayLength, Is.EqualTo(100));
            Assert.That(generator.MaxStringLength, Is.EqualTo(100));
            Assert.That(generator.MaxXmlAttributeCount, Is.EqualTo(10));
            Assert.That(generator.MaxXmlElementCount, Is.EqualTo(10));
            Assert.That(generator.BoundaryValueFrequency, Is.EqualTo(20));
            Assert.That(generator.NamespaceUris, Is.Not.Null);
            Assert.That(generator.ServerUris, Is.Not.Null);
        }

        [Test]
        public void PropertiesAreSettable()
        {
            var generator = new DataGenerator(new RandomSource(1), m_telemetry);
            generator.MaxArrayLength = 50;
            generator.MaxStringLength = 50;
            generator.MaxXmlAttributeCount = 5;
            generator.MaxXmlElementCount = 5;
            generator.BoundaryValueFrequency = 50;
            generator.MinDateTimeValue = new DateTimeUtc(2000, 1, 1, 0, 0, 0);
            generator.MaxDateTimeValue = new DateTimeUtc(2050, 1, 1, 0, 0, 0);
            generator.NamespaceUris = new NamespaceTable();
            generator.ServerUris = new StringTable();

            Assert.That(generator.MaxArrayLength, Is.EqualTo(50));
            Assert.That(generator.MaxStringLength, Is.EqualTo(50));
            Assert.That(generator.MaxXmlAttributeCount, Is.EqualTo(5));
            Assert.That(generator.MaxXmlElementCount, Is.EqualTo(5));
            Assert.That(generator.BoundaryValueFrequency, Is.EqualTo(50));
        }

        [Test]
        public void GetRandomBooleanProducesValue()
        {
            bool result = m_generator.GetRandomBoolean();
            Assert.That(result, Is.TypeOf<bool>());
        }

        [Test]
        public void GetRandomSByteProducesValueInRange()
        {
            sbyte result = m_generator.GetRandomSByte();
            Assert.That(result, Is.InRange(sbyte.MinValue, sbyte.MaxValue));
        }

        [Test]
        public void GetRandomByteProducesValueInRange()
        {
            byte result = m_generator.GetRandomByte();
            Assert.That(result, Is.InRange(byte.MinValue, byte.MaxValue));
        }

        [Test]
        public void GetRandomInt16ProducesValueInRange()
        {
            short result = m_generator.GetRandomInt16();
            Assert.That(result, Is.InRange(short.MinValue, short.MaxValue));
        }

        [Test]
        public void GetRandomUInt16ProducesValueInRange()
        {
            ushort result = m_generator.GetRandomUInt16();
            Assert.That(result, Is.InRange(ushort.MinValue, ushort.MaxValue));
        }

        [Test]
        public void GetRandomInt32ProducesValue()
        {
            int result = m_generator.GetRandomInt32();
            Assert.That(result, Is.TypeOf<int>());
        }

        [Test]
        public void GetRandomUInt32ProducesValue()
        {
            uint result = m_generator.GetRandomUInt32();
            Assert.That(result, Is.TypeOf<uint>());
        }

        [Test]
        public void GetRandomInt64ProducesValue()
        {
            long result = m_generator.GetRandomInt64();
            Assert.That(result, Is.TypeOf<long>());
        }

        [Test]
        public void GetRandomUInt64ProducesValue()
        {
            ulong result = m_generator.GetRandomUInt64();
            Assert.That(result, Is.TypeOf<ulong>());
        }

        [Test]
        public void GetRandomFloatProducesValue()
        {
            float result = m_generator.GetRandomFloat();
            Assert.That(result, Is.TypeOf<float>());
        }

        [Test]
        public void GetRandomDoubleProducesValue()
        {
            double result = m_generator.GetRandomDouble();
            Assert.That(result, Is.TypeOf<double>());
        }

        [Test]
        public void GetRandomStringProducesNonNullString()
        {
            string result = m_generator.GetRandomString();
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void GetRandomStringWithLocaleProducesNonNullString()
        {
            string result = m_generator.GetRandomString("en-US");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void GetRandomStringWithUnknownLocaleFallsBackToEnUs()
        {
            string result = m_generator.GetRandomString("xx-XX");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void GetRandomSymbolProducesNonNullString()
        {
            string result = m_generator.GetRandomSymbol();
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void GetRandomSymbolWithLocaleProducesNonNullString()
        {
            string result = m_generator.GetRandomSymbol("en-US");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        [Test]
        public void GetRandomDateTimeProducesValue()
        {
            DateTimeUtc result = m_generator.GetRandomDateTime();
            Assert.That(result, Is.Not.EqualTo(default(DateTimeUtc)));
        }

        [Test]
        public void GetRandomGuidProducesValue()
        {
            Uuid result = m_generator.GetRandomGuid();
            Assert.That(result, Is.Not.EqualTo(Uuid.Empty));
        }

        [Test]
        public void GetRandomByteStringProducesValue()
        {
            ByteString result = m_generator.GetRandomByteString();
            Assert.That(result.Length, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void GetRandomXmlElementProducesValue()
        {
            XmlElement result = m_generator.GetRandomXmlElement();
            Assert.That(result, Is.Not.EqualTo(default(XmlElement)));
        }

        [Test]
        public void GetRandomNodeIdProducesValue()
        {
            NodeId result = m_generator.GetRandomNodeId();
            Assert.That(result, Is.Not.EqualTo(default(NodeId)));
        }

        [Test]
        public void GetRandomExpandedNodeIdProducesValue()
        {
            ExpandedNodeId result = m_generator.GetRandomExpandedNodeId();
            Assert.That(result, Is.Not.EqualTo(default(ExpandedNodeId)));
        }

        [Test]
        public void GetRandomQualifiedNameProducesValue()
        {
            QualifiedName result = m_generator.GetRandomQualifiedName();
            Assert.That(result, Is.Not.EqualTo(default(QualifiedName)));
        }

        [Test]
        public void GetRandomLocalizedTextProducesValue()
        {
            LocalizedText result = m_generator.GetRandomLocalizedText();
            Assert.That(result, Is.Not.EqualTo(default(LocalizedText)));
        }

        [Test]
        public void GetRandomStatusCodeProducesValue()
        {
            StatusCode result = m_generator.GetRandomStatusCode();
            Assert.That(result, Is.TypeOf<StatusCode>());
        }

        [Test]
        public void GetRandomExtensionObjectProducesValue()
        {
            ExtensionObject result = m_generator.GetRandomExtensionObject();
            Assert.That(result, Is.Not.EqualTo(default(ExtensionObject)));
        }

        [Test]
        public void GetRandomDataValueProducesValue()
        {
            DataValue result = m_generator.GetRandomDataValue();
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void GetRandomDiagnosticInfoProducesValue()
        {
            DiagnosticInfo result = m_generator.GetRandomDiagnosticInfo();
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void GetRandomNumberProducesNumericVariant()
        {
            Variant result = m_generator.GetRandomNumber();
            Assert.That(result, Is.Not.EqualTo(Variant.Null));
        }

        [Test]
        public void GetRandomIntegerProducesSignedIntVariant()
        {
            Variant result = m_generator.GetRandomInteger();
            Assert.That(result, Is.Not.EqualTo(Variant.Null));
        }

        [Test]
        public void GetRandomUIntegerProducesUnsignedIntVariant()
        {
            Variant result = m_generator.GetRandomUInteger();
            Assert.That(result, Is.Not.EqualTo(Variant.Null));
        }

        [Test]
        public void GetRandomVariantProducesValue()
        {
            Variant result = m_generator.GetRandomVariant();
            Assert.That(result, Is.TypeOf<Variant>());
        }

        [Test]
        public void GetRandomVariantWithoutArraysProducesScalar()
        {
            Variant result = m_generator.GetRandomVariant(false);
            Assert.That(result, Is.TypeOf<Variant>());
        }

        [Test]
        public void GetRandomVariantWithBuiltInTypeScalarProducesValue()
        {
            Variant result = m_generator.GetRandomVariant(BuiltInType.Int32, false);
            Assert.That(result, Is.Not.EqualTo(Variant.Null));
        }

        [Test]
        public void GetRandomVariantWithBuiltInTypeArrayProducesValue()
        {
            Variant result = m_generator.GetRandomVariant(BuiltInType.Int32, true);
            Assert.That(result, Is.Not.EqualTo(Variant.Null));
        }

        [Test]
        public void GetRandomVariantWithNullTypeReturnsNull()
        {
            Variant result = m_generator.GetRandomVariant(BuiltInType.Null, false);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [TestCase(BuiltInType.Boolean)]
        [TestCase(BuiltInType.SByte)]
        [TestCase(BuiltInType.Byte)]
        [TestCase(BuiltInType.Int16)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Double)]
        [TestCase(BuiltInType.String)]
        [TestCase(BuiltInType.DateTime)]
        [TestCase(BuiltInType.Guid)]
        [TestCase(BuiltInType.ByteString)]
        [TestCase(BuiltInType.XmlElement)]
        [TestCase(BuiltInType.NodeId)]
        [TestCase(BuiltInType.ExpandedNodeId)]
        [TestCase(BuiltInType.QualifiedName)]
        [TestCase(BuiltInType.LocalizedText)]
        [TestCase(BuiltInType.StatusCode)]
        [TestCase(BuiltInType.Variant)]
        [TestCase(BuiltInType.Enumeration)]
        [TestCase(BuiltInType.ExtensionObject)]
        [TestCase(BuiltInType.DataValue)]
        [TestCase(BuiltInType.Number)]
        [TestCase(BuiltInType.Integer)]
        [TestCase(BuiltInType.UInteger)]
        public void GetRandomScalarDoesNotThrowForValidType(BuiltInType builtInType)
        {
            Assert.That(() => m_generator.GetRandomScalar(builtInType), Throws.Nothing);
        }

        [TestCase(BuiltInType.Null)]
        [TestCase(BuiltInType.DiagnosticInfo)]
        public void GetRandomScalarReturnsNullForSpecialTypes(BuiltInType builtInType)
        {
            Variant result = m_generator.GetRandomScalar(builtInType);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void GetRandomScalarThrowsForInvalidType()
        {
            Assert.That(
                () => m_generator.GetRandomScalar((BuiltInType)99),
                Throws.TypeOf<ServiceResultException>());
        }

        [TestCase(BuiltInType.Boolean)]
        [TestCase(BuiltInType.SByte)]
        [TestCase(BuiltInType.Byte)]
        [TestCase(BuiltInType.Int16)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Double)]
        [TestCase(BuiltInType.String)]
        [TestCase(BuiltInType.DateTime)]
        [TestCase(BuiltInType.Guid)]
        [TestCase(BuiltInType.ByteString)]
        [TestCase(BuiltInType.XmlElement)]
        [TestCase(BuiltInType.NodeId)]
        [TestCase(BuiltInType.ExpandedNodeId)]
        [TestCase(BuiltInType.QualifiedName)]
        [TestCase(BuiltInType.LocalizedText)]
        [TestCase(BuiltInType.StatusCode)]
        [TestCase(BuiltInType.Variant)]
        [TestCase(BuiltInType.Enumeration)]
        public void GetRandomArrayDoesNotThrowForValidType(BuiltInType builtInType)
        {
            Assert.That(() => m_generator.GetRandomArray(builtInType, 5), Throws.Nothing);
        }

        [TestCase(BuiltInType.Null)]
        [TestCase(BuiltInType.ExtensionObject)]
        [TestCase(BuiltInType.DataValue)]
        [TestCase(BuiltInType.DiagnosticInfo)]
        [TestCase(BuiltInType.Number)]
        [TestCase(BuiltInType.Integer)]
        [TestCase(BuiltInType.UInteger)]
        public void GetRandomArrayReturnsNullForSpecialTypes(BuiltInType builtInType)
        {
            Variant result = m_generator.GetRandomArray(builtInType, 5);
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void GetRandomArrayThrowsForInvalidType()
        {
            Assert.That(
                () => m_generator.GetRandomArray((BuiltInType)99, 5),
                Throws.TypeOf<ServiceResultException>());
        }

        [TestCase(BuiltInType.Boolean)]
        [TestCase(BuiltInType.SByte)]
        [TestCase(BuiltInType.Byte)]
        [TestCase(BuiltInType.Int16)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Double)]
        [TestCase(BuiltInType.String)]
        [TestCase(BuiltInType.DateTime)]
        [TestCase(BuiltInType.Guid)]
        [TestCase(BuiltInType.ByteString)]
        [TestCase(BuiltInType.XmlElement)]
        [TestCase(BuiltInType.NodeId)]
        [TestCase(BuiltInType.ExpandedNodeId)]
        [TestCase(BuiltInType.QualifiedName)]
        [TestCase(BuiltInType.LocalizedText)]
        [TestCase(BuiltInType.StatusCode)]
        [TestCase(BuiltInType.Variant)]
        [TestCase(BuiltInType.Enumeration)]
        public void GetRandomMatrixDoesNotThrowForValidType(BuiltInType builtInType)
        {
            Assert.That(
                () => m_generator.GetRandomMatrix(builtInType, 6, [2, 3]),
                Throws.Nothing);
        }

        [Test]
        public void GetRandomMatrixThrowsForInvalidType()
        {
            Assert.That(
                () => m_generator.GetRandomMatrix((BuiltInType)99, 6, [2, 3]),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void AllTypedArrayMethodsReturnFixedLengthArrays()
        {
            const int length = 5;
            Assert.That(m_generator.GetRandomBooleanArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomSByteArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomByteArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomInt16Array(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomUInt16Array(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomInt32Array(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomUInt32Array(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomInt64Array(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomUInt64Array(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomFloatArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomDoubleArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomStringArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomDateTimeArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomGuidArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomByteStringArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomXmlElementArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomNodeIdArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomExpandedNodeIdArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomQualifiedNameArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomLocalizedTextArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomStatusCodeArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomExtensionObjectArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomDataValueArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomDiagnosticInfoArray(false, length, true), Has.Length.EqualTo(length));
        }

        [Test]
        public void AllTypedArrayMethodsReturnEmptyForNegativeLength()
        {
            Assert.That(m_generator.GetRandomBooleanArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomSByteArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomByteArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomInt16Array(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomUInt16Array(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomInt32Array(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomUInt32Array(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomInt64Array(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomUInt64Array(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomFloatArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomDoubleArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomStringArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomDateTimeArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomGuidArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomByteStringArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomXmlElementArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomNodeIdArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomExpandedNodeIdArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomQualifiedNameArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomLocalizedTextArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomStatusCodeArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomExtensionObjectArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomDataValueArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomDiagnosticInfoArray(false, -1, true), Is.Empty);
        }

        [Test]
        public void ArrayMethodsWithVariableLengthReturnShorterOrEqualArray()
        {
            const int maxLength = 20;
            int[] result = m_generator.GetRandomInt32Array(false, maxLength, false);
            Assert.That(result, Has.Length.LessThanOrEqualTo(maxLength));
        }

        [Test]
        public void VariantArrayMethodsProduceArrays()
        {
            const int length = 5;
            Assert.That(m_generator.GetRandomVariantArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomNumberArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomIntegerArray(false, length, true), Has.Length.EqualTo(length));
            Assert.That(m_generator.GetRandomUIntegerArray(false, length, true), Has.Length.EqualTo(length));
        }

        [Test]
        public void VariantArrayMethodsReturnEmptyForNegativeLength()
        {
            Assert.That(m_generator.GetRandomVariantArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomNumberArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomIntegerArray(false, -1, true), Is.Empty);
            Assert.That(m_generator.GetRandomUIntegerArray(false, -1, true), Is.Empty);
        }

        [Test]
        public void BoundaryValuesForSByteProducesValidValues()
        {
            sbyte result = m_boundaryGenerator.GetRandomSByte(true);
            Assert.That(result, Is.InRange(sbyte.MinValue, sbyte.MaxValue));
        }

        [Test]
        public void BoundaryValuesForByteProducesKnownValues()
        {
            byte result = m_boundaryGenerator.GetRandomByte(true);
            Assert.That(result, Is.AnyOf(byte.MinValue, byte.MaxValue));
        }

        [Test]
        public void BoundaryValuesForInt16ProducesValidValues()
        {
            short result = m_boundaryGenerator.GetRandomInt16(true);
            Assert.That(result, Is.InRange(short.MinValue, short.MaxValue));
        }

        [Test]
        public void BoundaryValuesForUInt16ProducesKnownValues()
        {
            ushort result = m_boundaryGenerator.GetRandomUInt16(true);
            Assert.That(result, Is.AnyOf(ushort.MinValue, ushort.MaxValue));
        }

        [Test]
        public void BoundaryValuesForInt32ProducesKnownValues()
        {
            int result = m_boundaryGenerator.GetRandomInt32(true);
            Assert.That(result, Is.AnyOf(int.MinValue, 0, int.MaxValue));
        }

        [Test]
        public void BoundaryValuesForUInt32ProducesKnownValues()
        {
            uint result = m_boundaryGenerator.GetRandomUInt32(true);
            Assert.That(result, Is.AnyOf(uint.MinValue, uint.MaxValue));
        }

        [Test]
        public void BoundaryValuesForInt64ProducesKnownValues()
        {
            long result = m_boundaryGenerator.GetRandomInt64(true);
            Assert.That(result, Is.AnyOf(long.MinValue, 0L, long.MaxValue));
        }

        [Test]
        public void BoundaryValuesForUInt64ProducesKnownValues()
        {
            ulong result = m_boundaryGenerator.GetRandomUInt64(true);
            Assert.That(result, Is.AnyOf(ulong.MinValue, ulong.MaxValue));
        }

        [Test]
        public void BoundaryValuesForStatusCodeProducesKnownValues()
        {
            StatusCode result = m_boundaryGenerator.GetRandomStatusCode(true);
            Assert.That(
                (uint)result,
                Is.AnyOf((uint)StatusCodes.Good, (uint)StatusCodes.Uncertain, (uint)StatusCodes.Bad));
        }

        [Test]
        public void BoundaryValuesForGuidProducesKnownValues()
        {
            Uuid result = m_boundaryGenerator.GetRandomGuid(true);
            Assert.That(result, Is.EqualTo(Uuid.Empty));
        }

        [Test]
        public void BoundaryValuesForNodeIdProducesKnownValues()
        {
            NodeId result = m_boundaryGenerator.GetRandomNodeId(true);
            Assert.That(result, Is.InstanceOf<NodeId>());
        }

        [Test]
        public void BoundaryValuesForExpandedNodeIdProducesKnownValues()
        {
            ExpandedNodeId result = m_boundaryGenerator.GetRandomExpandedNodeId(true);
            Assert.That(result, Is.InstanceOf<ExpandedNodeId>());
        }

        [Test]
        public void BoundaryValuesForStringCanReturnEmptyOrNull()
        {
            string result = m_boundaryGenerator.GetRandomString(true);
            Assert.That(result, Is.Null.Or.Empty.Or.Not.Empty);
        }

        [Test]
        public void BoundaryValuesForDateTimeProducesValue()
        {
            DateTimeUtc result = m_boundaryGenerator.GetRandomDateTime(true);
            Assert.That(result, Is.TypeOf<DateTimeUtc>());
        }

        [Test]
        public void BoundaryValuesForByteStringProducesValue()
        {
            ByteString result = m_boundaryGenerator.GetRandomByteString(true);
            Assert.That(result, Is.Not.EqualTo(default(ByteString)).Or.EqualTo(default(ByteString)));
        }

        [Test]
        public void BoundaryValuesForXmlElementProducesValue()
        {
            Assert.That(() => m_boundaryGenerator.GetRandomXmlElement(true), Throws.Nothing);
        }

        [Test]
        public void BoundaryValuesForQualifiedNameProducesValue()
        {
            Assert.That(() => m_boundaryGenerator.GetRandomQualifiedName(true), Throws.Nothing);
        }

        [Test]
        public void BoundaryValuesForLocalizedTextProducesValue()
        {
            Assert.That(() => m_boundaryGenerator.GetRandomLocalizedText(true), Throws.Nothing);
        }

        [Test]
        public void BoundaryValuesForExtensionObjectProducesValue()
        {
            Assert.That(() => m_boundaryGenerator.GetRandomExtensionObject(true), Throws.Nothing);
        }

        [Test]
        public void BoundaryValuesForDataValueProducesValue()
        {
            Assert.That(() => m_boundaryGenerator.GetRandomDataValue(true), Throws.Nothing);
        }

        [Test]
        public void BoundaryValuesForDiagnosticInfoProducesValue()
        {
            Assert.That(() => m_boundaryGenerator.GetRandomDiagnosticInfo(true), Throws.Nothing);
        }

        [Test]
        public void GetRandomScalarWithBoundaryValuesDoesNotThrow()
        {
            Assert.That(
                () => m_boundaryGenerator.GetRandomScalar(BuiltInType.Int32, true),
                Throws.Nothing);
        }

        [Test]
        public void GetRandomArrayWithBoundaryValuesDoesNotThrow()
        {
            Assert.That(
                () => m_generator.GetRandomArray(BuiltInType.Int32, 5, true),
                Throws.Nothing);
        }

        [Test]
        public void GetRandomMatrixWithBoundaryValuesDoesNotThrow()
        {
            Assert.That(
                () => m_generator.GetRandomMatrix(BuiltInType.Int32, 6, [2, 3], true),
                Throws.Nothing);
        }

        [Test]
        public void MultipleCallsWithSameSeedProduceIdenticalResults()
        {
            var gen1 = new DataGenerator(new RandomSource(123), m_telemetry);
            var gen2 = new DataGenerator(new RandomSource(123), m_telemetry);

            Assert.That(gen1.GetRandomInt32(), Is.EqualTo(gen2.GetRandomInt32()));
            Assert.That(gen1.GetRandomDouble(), Is.EqualTo(gen2.GetRandomDouble()));
            Assert.That(gen1.GetRandomBoolean(), Is.EqualTo(gen2.GetRandomBoolean()));
        }

        [Test]
        public void GeneratorWithSmallMaxStringLengthProducesShortStrings()
        {
            var generator = new DataGenerator(new RandomSource(42), m_telemetry);
            generator.MaxStringLength = 5;
            string result = generator.GetRandomString();
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void GeneratorWithSmallMaxArrayLengthProducesShortArrays()
        {
            var generator = new DataGenerator(new RandomSource(42), m_telemetry);
            generator.MaxArrayLength = 3;
            Variant result = generator.GetRandomVariant(BuiltInType.Int32, true);
            Assert.That(result, Is.Not.EqualTo(Variant.Null));
        }

        [Test]
        public void GetRandomBooleanWithBoundaryValuesProducesBoolean()
        {
            bool result = m_boundaryGenerator.GetRandomBoolean(true);
            Assert.That(result, Is.TypeOf<bool>());
        }

        [Test]
        public void GetRandomFloatWithBoundaryValuesProducesSpecialValue()
        {
            float result = m_boundaryGenerator.GetRandomFloat(true);
            Assert.That(result, Is.TypeOf<float>());
        }

        [Test]
        public void GetRandomDoubleWithBoundaryValuesProducesSpecialValue()
        {
            double result = m_boundaryGenerator.GetRandomDouble(true);
            Assert.That(result, Is.TypeOf<double>());
        }

        [Test]
        public void GetRandomNumberCalledMultipleTimesCoversVariousTypes()
        {
            for (int i = 0; i < 20; i++)
            {
                Variant result = m_generator.GetRandomNumber();
                Assert.That(result, Is.Not.EqualTo(Variant.Null));
            }
        }

        [Test]
        public void GetRandomIntegerCalledMultipleTimesCoversVariousTypes()
        {
            for (int i = 0; i < 20; i++)
            {
                Variant result = m_generator.GetRandomInteger();
                Assert.That(result, Is.Not.EqualTo(Variant.Null));
            }
        }

        [Test]
        public void GetRandomUIntegerCalledMultipleTimesCoversVariousTypes()
        {
            for (int i = 0; i < 20; i++)
            {
                Variant result = m_generator.GetRandomUInteger();
                Assert.That(result, Is.Not.EqualTo(Variant.Null));
            }
        }

        [Test]
        public void GetRandomVariantCalledMultipleTimesCoversVariousTypes()
        {
            for (int i = 0; i < 20; i++)
            {
                Variant result = m_generator.GetRandomVariant();
                Assert.That(result, Is.TypeOf<Variant>());
            }
        }
    }
}
