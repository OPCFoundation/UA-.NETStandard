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

        /// <summary>
        /// Validate ExtensionObject special cases and constructors.
        /// </summary>
        [Test]
        [Category("BuiltInType")]
        public void ExtensionObject()
        {
            ExtensionObject extensionObject_null = null;
            // Validate the default constructor
            ExtensionObject extensionObject_Default = new Ua.ExtensionObject();
            Assert.NotNull(extensionObject_Default);
            Assert.AreEqual(ExpandedNodeId.Null, extensionObject_Default.TypeId);
            Assert.AreEqual(ExtensionObjectEncoding.None, extensionObject_Default.Encoding);
            Assert.Null(extensionObject_Default.Body);
            // Constructor by ExtensionObject
            ExtensionObject extensionObject = new ExtensionObject(ExpandedNodeId.Null);
            Assert.NotNull(extensionObject);
            Assert.AreEqual(ExpandedNodeId.Null, extensionObject.TypeId);
            Assert.AreEqual(ExtensionObjectEncoding.None, extensionObject.Encoding);
            Assert.Null(extensionObject.Body);
            // static extensions
            Assert.True(Ua.ExtensionObject.IsNull(extensionObject));
            Assert.Null(Ua.ExtensionObject.ToEncodeable(null));
            Assert.Null(Ua.ExtensionObject.ToArray(null, typeof(object)));
            Assert.Null(Ua.ExtensionObject.ToList<object>(null));
            // constructor by ExpandedNodeId
            extensionObject = new ExtensionObject((ExpandedNodeId) null);
            Assert.AreEqual(0, extensionObject.GetHashCode());
            Assert.Throws<ArgumentNullException>(() => new ExtensionObject(extensionObject_null));
            Assert.Throws<ServiceResultException>(() => new ExtensionObject(new object()));
            // constructor by object
            object byteArray = new byte[] { 1, 2, 3 };
            extensionObject = new ExtensionObject(byteArray);
            Assert.NotNull(extensionObject);
            Assert.AreEqual(extensionObject, extensionObject);
            // string extension
            var extensionObjectString = extensionObject.ToString();
            Assert.Throws<FormatException>(() => extensionObject.ToString("123", null));
            Assert.NotNull(extensionObjectString);
            // clone
            var clonedExtensionObject = (ExtensionObject)Utils.Clone(extensionObject);
            Assert.AreEqual(extensionObject, clonedExtensionObject);
            // IsEqual operator
            clonedExtensionObject.TypeId = new ExpandedNodeId(333);
            Assert.AreNotEqual(extensionObject, clonedExtensionObject);
            Assert.AreNotEqual(extensionObject, extensionObject_Default);
            Assert.AreNotEqual(extensionObject, new object());
            Assert.AreEqual(clonedExtensionObject, clonedExtensionObject);
            Assert.AreEqual(ExpandedNodeId.Null, extensionObject.TypeId);
            Assert.AreEqual(ExpandedNodeId.Null.GetHashCode(), extensionObject.TypeId.GetHashCode());
            Assert.AreEqual(ExtensionObjectEncoding.Binary, extensionObject.Encoding);
            Assert.AreEqual(byteArray, extensionObject.Body);
            Assert.AreEqual(byteArray.GetHashCode(), extensionObject.Body.GetHashCode());
            // collection
            ExtensionObjectCollection collection = new ExtensionObjectCollection();
            Assert.NotNull(collection);
            collection = new ExtensionObjectCollection(100);
            Assert.NotNull(collection);
            collection = new ExtensionObjectCollection(collection);
            Assert.NotNull(collection);
            collection = (ExtensionObjectCollection)collection.MemberwiseClone();
            // default value is null
            Assert.Null(TypeInfo.GetDefaultValue(BuiltInType.ExtensionObject));
        }
        #endregion
    }

}
