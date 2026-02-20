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
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
            object randomData = DataGenerator.GetRandom(builtInType);
            var variant1 = new Variant(randomData);
            Assert.AreEqual(builtInType, variant1.TypeInfo.BuiltInType);
            var variant2 = new Variant(randomData, TypeInfo.Create(builtInType, ValueRanks.Scalar));
            Assert.AreEqual(builtInType, variant2.TypeInfo.BuiltInType);
            var variant3 = new Variant(variant2);
            Assert.AreEqual(builtInType, variant3.TypeInfo.BuiltInType);
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
            object randomData = DataGenerator.GetRandomArray(
                builtInType,
                useBoundaryValues,
                100,
                false);
            var variant1 = new Variant(randomData);
            Assert.AreEqual(builtInType, variant1.TypeInfo.BuiltInType);
            var variant2 = new Variant(randomData, TypeInfo.Create(builtInType, ValueRanks.OneDimension));
            Assert.AreEqual(builtInType, variant2.TypeInfo.BuiltInType);
        }
    }
}
