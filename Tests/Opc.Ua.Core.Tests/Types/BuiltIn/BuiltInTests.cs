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
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Test;

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture, Category("BuiltInType")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class BuiltInTests
    {
        protected const int kRandomStart = 4840;
        protected const int kRandomRepeats = 100;
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
            RandomSource = new RandomSource(kRandomStart);
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
            int randomSeed = TestContext.CurrentContext.Random.Next() + kRandomStart;
            RandomSource = new RandomSource(randomSeed);
            DataGenerator = new DataGenerator(RandomSource);
        }

        /// <summary>
        /// Ensure tests are reproducible with same seed.
        /// </summary>
        protected void SetRandomSeed(int randomSeed)
        {
            RandomSource = new RandomSource(randomSeed + kRandomStart);
            DataGenerator = new DataGenerator(RandomSource);
        }
        #endregion

        #region DataPointSources
        [DatapointSource]
        public static readonly BuiltInType[] BuiltInTypes = ((BuiltInType[])Enum.GetValues(typeof(BuiltInType)))
            .ToList().Where(b => (b > BuiltInType.Null) && (b < BuiltInType.DataValue)).ToArray();
        #endregion

        #region Test Methods
        /// <summary>
        /// Initialize Variant with BuiltInType Scalar.
        /// </summary>
        [Theory]
        [Repeat(kRandomRepeats)]
        public void VariantScalarFromBuiltInType(BuiltInType builtInType)
        {
            SetRepeatedRandomSeed();
            object randomData = DataGenerator.GetRandom(builtInType);
            Variant variant1 = new Variant(randomData);
            Assert.AreEqual(builtInType, variant1.TypeInfo.BuiltInType);
            Variant variant2 = new Variant(randomData, new TypeInfo(builtInType, ValueRanks.Scalar));
            Assert.AreEqual(builtInType, variant2.TypeInfo.BuiltInType);
            Variant variant3 = new Variant(variant2);
            Assert.AreEqual(builtInType, variant3.TypeInfo.BuiltInType);
            // implicit
            Variant variant4 = variant1;
        }

        /// <summary>
        /// Initialize Variant with BuiltInType Array.
        /// </summary>
        [Theory]
        [Repeat(kRandomRepeats)]
        public void VariantArrayFromBuiltInType(BuiltInType builtInType, bool useBoundaryValues)
        {
            SetRepeatedRandomSeed();
            object randomData = DataGenerator.GetRandomArray(builtInType, useBoundaryValues, 100, false);
            Variant variant1 = new Variant(randomData);
            if (builtInType == BuiltInType.Byte)
            {
                // Without hint, byte array can not be distinguished from bytestring
                Assert.AreEqual(BuiltInType.ByteString, variant1.TypeInfo.BuiltInType);
            }
            else
            {
                Assert.AreEqual(builtInType, variant1.TypeInfo.BuiltInType);
            }
            Variant variant2 = new Variant(randomData, new TypeInfo(builtInType, ValueRanks.OneDimension));
            Assert.AreEqual(builtInType, variant2.TypeInfo.BuiltInType);
        }

        /// <summary>
        /// Variant constructor.
        /// </summary>
        [Test]
        public void VariantConstructor()
        {
            Uuid uuid = new Uuid(Guid.NewGuid());
            Variant variant1 = new Variant(uuid);
            Assert.AreEqual(BuiltInType.Guid, variant1.TypeInfo.BuiltInType);
        }

        /// <summary>
        /// Initialize Variant with Enum array.
        /// </summary>
        [Test]
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
            Variant variant5 = new Variant(daysdays, new TypeInfo(BuiltInType.Enumeration, ValueRanks.TwoDimensions));

            // not supported
            // Variant variant6 = new Variant(daysdays);
        }

        /// <summary>
        /// Validate ExtensionObject special cases and constructors.
        /// </summary>
        [Test]
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
            extensionObject = new ExtensionObject((ExpandedNodeId)null);
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

        /// <summary>
        /// Ensure the matrix dimension and order is identical
        /// after constructor and ToArray is called.
        /// </summary>
        [Test]
        public void MatrixFlatToArray()
        {
            var testArray = new int[,,] {
                { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } },
                { { 11, 12, 13 }, { 14, 15, 16 }, { 17, 18, 19 } } };
            var matrix = new Matrix(testArray, TypeInfo.GetBuiltInType(new NodeId((int)BuiltInType.Int32)));
            var toArray = matrix.ToArray();
            Assert.AreEqual(testArray, toArray);
            Assert.True(Utils.IsEqual(testArray, toArray));
        }
        #endregion

        #region NodeId utilities
        [Test]
        public void NodeIdConstructor()
        {
            Guid id1 = Guid.NewGuid();
            NodeId nodeId1 = new NodeId(id1);
            // implicit conversion;
            NodeId inodeId1 = id1;
            Assert.AreEqual(nodeId1, inodeId1);

            byte[] id2 = new byte[] { 65, 66, 67, 68, 69 };
            NodeId nodeId2 = new NodeId(id2);
            // implicit conversion;
            NodeId inodeId2 = id2;
            Assert.AreEqual(nodeId2, inodeId2);

            _ = nodeId2 < inodeId2;
            _ = nodeId2 == inodeId2;
            _ = nodeId2 > inodeId2;

            string text = "i=123";
            NodeId nodeIdText = new NodeId(text);
            Assert.AreEqual(123, nodeIdText.Identifier);
            // implicit conversion;
            NodeId inodeIdText = text;
            Assert.AreEqual(nodeIdText, inodeIdText);

            _ = nodeIdText < inodeIdText;
            _ = nodeIdText == inodeIdText;
            _ = nodeIdText > inodeIdText;

            _ = nodeIdText < nodeId2;
            _ = nodeIdText == nodeId2;
            _ = nodeIdText > nodeId2;

            _ = new NodeId((object)(uint)123, 123);
            _ = new NodeId((object)"Test", 123);
            _ = new NodeId((object)id2, 123);
            _ = new NodeId((object)null, 123);
            _ = new NodeId((object)id1, 123);

            Assert.Throws<ArgumentException>(() => _ = new NodeId((object)(int)123, 123));
            Assert.Throws<ServiceResultException>(() => _ = NodeId.Create((uint)123, "urn:xyz", null));
            Assert.Throws<ServiceResultException>(() => _ = NodeId.Parse("ns="));
            Assert.IsNull(NodeId.ToExpandedNodeId(null, null));
        }

        [Theory]
        [TestCase(-1)]
        public void NullIdNodeIdComparison(Opc.Ua.IdType idType)
        {
            NodeId nodeId = NodeId.Null;
            switch (idType)
            {
                case Opc.Ua.IdType.Numeric: nodeId = new NodeId(0, 0); break;
                case Opc.Ua.IdType.String: nodeId = new NodeId(""); break;
                case Opc.Ua.IdType.Guid: nodeId = new NodeId(Guid.Empty); break;
                case Opc.Ua.IdType.Opaque: nodeId = new NodeId((byte)0); break;
            }

            Assert.IsTrue(nodeId.IsNullNodeId);

            DataValue nodeIdBasedDataValue = new DataValue(nodeId);

            DataValue dataValue = new DataValue(Attributes.NodeClass);
            dataValue.Value = (int)Attributes.NodeClass; // without this cast the second and third asserts evaluate correctly.
            dataValue.StatusCode = nodeIdBasedDataValue.StatusCode;

            bool comparisonResult1b = dataValue.Equals(nodeIdBasedDataValue);
            Assert.IsFalse(comparisonResult1b); // assert succeeds

            bool comparisonResult1a = nodeIdBasedDataValue.Equals(dataValue);
            Assert.IsFalse(comparisonResult1a); // assert fails (symmetry for Equals is broken)

            bool comparisonResult1c = EqualityComparer<DataValue>.Default.Equals(nodeIdBasedDataValue, dataValue);
            Assert.IsFalse(comparisonResult1c); // assert fails

            int comparisonResult2 = nodeId.CompareTo(dataValue);
            Assert.IsFalse(comparisonResult2 == 0); // assert fails - this is the root cause for the previous assertion failures
        }
        #endregion

        #region ValueRanks
        [Test]
        [TestCase(ValueRanks.ScalarOrOneDimension)]
        [TestCase(ValueRanks.Scalar)]
        [TestCase(ValueRanks.OneOrMoreDimensions)]
        [TestCase(ValueRanks.OneDimension)]
        [TestCase(ValueRanks.TwoDimensions)]

        public void ValueRanksTests(int actualValueRank)
        {
            Assert.IsTrue(ValueRanks.IsValid(actualValueRank, actualValueRank));
            Assert.IsTrue(ValueRanks.IsValid(actualValueRank, ValueRanks.Any));
            Assert.AreEqual(actualValueRank == ValueRanks.Scalar || actualValueRank == ValueRanks.OneDimension || actualValueRank == ValueRanks.ScalarOrOneDimension, ValueRanks.IsValid(actualValueRank, ValueRanks.ScalarOrOneDimension));
            Assert.AreEqual(actualValueRank >= 0, ValueRanks.IsValid(actualValueRank, ValueRanks.OneOrMoreDimensions));
            Assert.AreEqual(actualValueRank == ValueRanks.TwoDimensions, ValueRanks.IsValid(actualValueRank, ValueRanks.TwoDimensions));
            Assert.AreEqual(actualValueRank == ValueRanks.OneDimension, ValueRanks.IsValid(actualValueRank, ValueRanks.OneDimension));
            Assert.AreEqual(actualValueRank >= 0, ValueRanks.IsValid(actualValueRank, ValueRanks.OneOrMoreDimensions));
            Assert.AreEqual(actualValueRank == ValueRanks.Scalar, ValueRanks.IsValid(actualValueRank, ValueRanks.Scalar));
        }
        #endregion
    }

}
