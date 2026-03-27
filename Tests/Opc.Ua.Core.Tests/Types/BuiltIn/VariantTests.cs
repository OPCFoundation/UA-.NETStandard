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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Test;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VariantTests
    {
        protected const int kRandomStart = 4840;
        protected const int kRandomRepeats = 100;
        protected RandomSource RandomSource { get; private set; }
        protected DataGenerator DataGenerator { get; private set; }
        protected ITelemetryContext Telemetry { get; private set; }

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {
            // ensure tests are reproducible, reset for every test
            Telemetry = NUnitTelemetryContext.Create();
            RandomSource = new RandomSource(kRandomStart);
            DataGenerator = new DataGenerator(RandomSource, Telemetry);
        }

        [TearDown]
        protected void TearDown()
        {
        }

        /// <summary>
        /// Ensure repeated tests get different seed.
        /// </summary>
        protected void SetRepeatedRandomSeed()
        {
            int randomSeed = TestContext.CurrentContext.CurrentRepeatCount + kRandomStart;
            Telemetry = NUnitTelemetryContext.Create();
            RandomSource = new RandomSource(randomSeed);
            DataGenerator = new DataGenerator(RandomSource, Telemetry);
        }

        /// <summary>
        /// Ensure tests are reproducible with same seed.
        /// </summary>
        protected void SetRandomSeed(int randomSeed)
        {
            Telemetry = NUnitTelemetryContext.Create();
            RandomSource = new RandomSource(randomSeed + kRandomStart);
            DataGenerator = new DataGenerator(RandomSource, Telemetry);
        }

        [DatapointSource]
        public static readonly BuiltInType[] BuiltInTypes =
#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
        [
            .. Enum.GetValues<BuiltInType>()
#else
        [
            .. Enum.GetValues(typeof(BuiltInType))
                .Cast<BuiltInType>()
#endif
                .Where(b => b is > BuiltInType.Null and < BuiltInType.DataValue)
        ];

        /// <summary>
        /// Initialize Variant with BuiltInType Scalar.
        /// </summary>
        [Theory]
        [Repeat(kRandomRepeats)]
        public void VariantScalarFromBuiltInType(BuiltInType builtInType)
        {
            SetRepeatedRandomSeed();
            object randomData = GetRandom(builtInType);
#pragma warning disable CS0618 // Type or member is obsolete
            var variant1 = new Variant(randomData);
            Assert.That(variant1.TypeInfo.BuiltInType, Is.EqualTo(builtInType));
            var variant2 = new Variant(randomData, TypeInfo.Create(builtInType, ValueRanks.Scalar));
            Assert.That(variant2.TypeInfo.BuiltInType, Is.EqualTo(builtInType));
            var variant3 = new Variant(variant2);
#pragma warning restore CS0618 // Type or member is obsolete
            Assert.That(variant3.TypeInfo.BuiltInType, Is.EqualTo(builtInType));
            // implicit
        }

        /// <summary>
        /// Initialize Variant with BuiltInType Array.
        /// </summary>
        [Theory]
        [Repeat(kRandomRepeats)]
        public void VariantArrayFromBuiltInType(BuiltInType builtInType, bool useBoundaryValues)
        {
            SetRepeatedRandomSeed();
            object randomData = GetRandomArray(
                builtInType,
                useBoundaryValues,
                100,
                false);
#pragma warning disable CS0618 // Type or member is obsolete
            var variant1 = new Variant(randomData);
            Assert.That(variant1.TypeInfo.BuiltInType, Is.EqualTo(builtInType));
            var variant2 = new Variant(randomData, TypeInfo.Create(builtInType, ValueRanks.OneDimension));
            Assert.That(variant2.TypeInfo.BuiltInType, Is.EqualTo(builtInType));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        /// <summary>
        /// Returns a random value of the specified built-in type.
        /// Used only by tests
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object GetRandom(BuiltInType expectedType)
        {
            switch (expectedType)
            {
                case BuiltInType.DiagnosticInfo:
                    return DataGenerator.GetRandomDiagnosticInfo();
                case BuiltInType.Null:
                    return null;
                case BuiltInType.Number:
                case BuiltInType.Integer:
                case BuiltInType.UInteger:
                case BuiltInType.Variant:
                    return DataGenerator.GetRandomScalar(expectedType);
                default:
                    return DataGenerator.GetRandomScalar(expectedType).AsBoxedObject();
            }
        }

        /// <summary>
        /// Returns a random value of the specified built-in type.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Array GetRandomArray(
            BuiltInType expectedType,
            bool useBoundaryValues,
            int length,
            bool fixedLength)
        {
            switch (expectedType)
            {
                case BuiltInType.Boolean:
                    return DataGenerator.GetRandomBooleanArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.SByte:
                    return DataGenerator.GetRandomSByteArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.Byte:
                    return DataGenerator.GetRandomByteArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.Int16:
                    return DataGenerator.GetRandomInt16Array(useBoundaryValues, length, fixedLength);
                case BuiltInType.UInt16:
                    return DataGenerator.GetRandomUInt16Array(useBoundaryValues, length, fixedLength);
                case BuiltInType.Int32:
                    return DataGenerator.GetRandomInt32Array(useBoundaryValues, length, fixedLength);
                case BuiltInType.UInt32:
                    return DataGenerator.GetRandomUInt32Array(useBoundaryValues, length, fixedLength);
                case BuiltInType.Int64:
                    return DataGenerator.GetRandomInt64Array(useBoundaryValues, length, fixedLength);
                case BuiltInType.UInt64:
                    return DataGenerator.GetRandomUInt64Array(useBoundaryValues, length, fixedLength);
                case BuiltInType.Float:
                    return DataGenerator.GetRandomFloatArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.Double:
                    return DataGenerator.GetRandomDoubleArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.String:
                    return DataGenerator.GetRandomStringArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.DateTime:
                    return DataGenerator.GetRandomDateTimeArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.Guid:
                    return DataGenerator.GetRandomGuidArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.ByteString:
                    return DataGenerator.GetRandomByteStringArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.XmlElement:
                    return DataGenerator.GetRandomXmlElementArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.NodeId:
                    return DataGenerator.GetRandomNodeIdArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.ExpandedNodeId:
                    return DataGenerator.GetRandomExpandedNodeIdArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.QualifiedName:
                    return DataGenerator.GetRandomQualifiedNameArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.LocalizedText:
                    return DataGenerator.GetRandomLocalizedTextArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.StatusCode:
                    return DataGenerator.GetRandomStatusCodeArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.Variant:
                    return DataGenerator.GetRandomVariantArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.ExtensionObject:
                    return DataGenerator.GetRandomExtensionObjectArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.Number:
                    return DataGenerator.GetRandomNumberArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.Integer:
                    return DataGenerator.GetRandomIntegerArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.UInteger:
                    return DataGenerator.GetRandomUIntegerArray(useBoundaryValues, length, fixedLength);
                case BuiltInType.Enumeration:
                    return DataGenerator.GetRandomInt32Array(useBoundaryValues, length, fixedLength);
                case BuiltInType.Null:
                case BuiltInType.DataValue:
                case BuiltInType.DiagnosticInfo:
                    return null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {expectedType}");
            }
        }
    }
}
