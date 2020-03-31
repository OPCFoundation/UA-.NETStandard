/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture, Category("BuiltIn")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class BuiltInTests
    {
        protected const int RandomStart = 4840;
        protected const int RandomRepeats = 100;
        protected RandomSource RandomSource { get; private set; }
        protected DataGenerator DataGenerator { get; private set; }

        #region Test Setup
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
            RandomSource = new RandomSource(RandomStart);
            DataGenerator = new DataGenerator(RandomSource);
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
            int randomSeed = TestContext.CurrentContext.Random.Next() + RandomStart;
            RandomSource = new RandomSource(randomSeed);
            DataGenerator = new DataGenerator(RandomSource);
        }

        /// <summary>
        /// Ensure tests are reproducible with same seed.
        /// </summary>
        protected void SetRandomSeed(int randomSeed)
        {
            RandomSource = new RandomSource(randomSeed + RandomStart);
            DataGenerator = new DataGenerator(RandomSource);
        }
        #endregion


        #region DataPointSources
        [DatapointSource]
        public static BuiltInType[] BuiltInTypes = ((BuiltInType[])Enum.GetValues(typeof(BuiltInType)))
            .ToList().Where(b => (b > BuiltInType.Null) && (b < BuiltInType.DataValue)).ToArray();
        #endregion

        #region Test Methods
        /// <summary>
        /// Initialize Variant with BuiltInType Scalar.
        /// </summary>
        [Theory]
        [Category("BuiltInType"), Repeat(RandomRepeats)]
        public void VariantScalarFromBuiltInType(BuiltInType builtInType)
        {
            SetRepeatedRandomSeed();
            object randomData = DataGenerator.GetRandom(builtInType);
            Variant variant1 = new Variant(randomData);
            Variant variant2 = new Variant(randomData, new TypeInfo(builtInType, ValueRanks.Scalar));
        }

        /// <summary>
        /// Initialize Variant with BuiltInType Array.
        /// </summary>
        [Theory]
        [Category("BuiltInType"), Repeat(RandomRepeats)]
        public void VariantArrayFromBuiltInType(BuiltInType builtInType, bool useBoundaryValues)
        {
            SetRepeatedRandomSeed();
            object randomData = DataGenerator.GetRandomArray(builtInType, useBoundaryValues, 100, false);
            Variant variant1 = new Variant(randomData);
            Variant variant2 = new Variant(randomData, new TypeInfo(builtInType, ValueRanks.OneDimension));
        }

        /// <summary>
        /// Initialize Variant with Enum array.
        /// </summary>
        [Test]
        [Category("BuiltInType")]
        public void VariantFromEnumArray()
        {
            // Enum Scalar
            Variant variant0 = new Variant(DayOfWeek.Monday);
            Variant variant1 = new Variant(DayOfWeek.Monday, new TypeInfo(BuiltInType.Enumeration, ValueRanks.Scalar));

            // Enum array
            DayOfWeek[] days = new DayOfWeek[] { DayOfWeek.Monday, DayOfWeek.Tuesday };
            Variant variant2 = new Variant(days, new TypeInfo(BuiltInType.Enumeration, ValueRanks.OneDimension));
            Variant variant3 = new Variant(days);

            // Enum 2-dim Array
            DayOfWeek[,] daysdays = new DayOfWeek[,] { { DayOfWeek.Monday, DayOfWeek.Tuesday }, { DayOfWeek.Monday, DayOfWeek.Tuesday } };
            Variant variant4 = new Variant(daysdays, new TypeInfo(BuiltInType.Enumeration, ValueRanks.TwoDimensions));
            // not supported
            // Variant variant5 = new Variant(daysdays);
        }
        #endregion
    }

}
