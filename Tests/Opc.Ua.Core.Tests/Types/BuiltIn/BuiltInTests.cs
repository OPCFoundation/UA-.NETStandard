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
            int randomSeed = TestContext.CurrentContext.CurrentRepeatCount + kRandomStart;
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
            collection = (ExtensionObjectCollection)Utils.Clone(collection);
            // default value is null
            Assert.Null(TypeInfo.GetDefaultValue(BuiltInType.ExtensionObject));
        }

        /// <summary>
        /// Ensure defaults are correct.
        /// </summary>
        [Test]
        public void DiagnosticInfoDefault()
        {
            DiagnosticInfo diagnosticInfo = new DiagnosticInfo();
            Assert.NotNull(diagnosticInfo);
            Assert.AreEqual(-1, diagnosticInfo.SymbolicId);
            Assert.AreEqual(-1, diagnosticInfo.NamespaceUri);
            Assert.AreEqual(-1, diagnosticInfo.Locale);
            Assert.AreEqual(-1, diagnosticInfo.LocalizedText);
            Assert.AreEqual(null, diagnosticInfo.AdditionalInfo);
            Assert.AreEqual(ServiceResult.Good.StatusCode, diagnosticInfo.InnerStatusCode);
            Assert.AreEqual(null, diagnosticInfo.InnerDiagnosticInfo);

            Assert.IsTrue(diagnosticInfo.Equals(null));
            Assert.IsTrue(diagnosticInfo.IsNullDiagnosticInfo);
        }

        /// <summary>
        /// Ensure nested service result is truncated.
        /// </summary>
        [Test]
        public void DiagnosticInfoInnerDiagnostics()
        {
            StringTable stringTable = new StringTable();
            ServiceResult serviceResult = new ServiceResult(StatusCodes.BadAggregateConfigurationRejected, "SymbolicId", Namespaces.OpcUa, new LocalizedText("The text", "en-us"), new Exception("The inner exception."));
            DiagnosticInfo diagnosticInfo = new DiagnosticInfo(serviceResult, DiagnosticsMasks.All, true, stringTable);
            Assert.NotNull(diagnosticInfo);
            Assert.AreEqual(0, diagnosticInfo.SymbolicId);
            Assert.AreEqual(1, diagnosticInfo.NamespaceUri);
            Assert.AreEqual(2, diagnosticInfo.Locale);
            Assert.AreEqual(3, diagnosticInfo.LocalizedText);

            // recursive inner diagnostics, ensure its truncated
            for (int ii = 0; ii < DiagnosticInfo.MaxInnerDepth + 1; ii++)
            {
                serviceResult = new ServiceResult(serviceResult, serviceResult);
            }
            diagnosticInfo = new DiagnosticInfo(serviceResult, DiagnosticsMasks.All, true, stringTable);
            Assert.NotNull(diagnosticInfo);
            int depth = 0;
            DiagnosticInfo innerDiagnosticInfo = diagnosticInfo;
            Assert.NotNull(innerDiagnosticInfo);
            while (innerDiagnosticInfo != null)
            {
                depth++;
                innerDiagnosticInfo = innerDiagnosticInfo.InnerDiagnosticInfo;
                if (depth > DiagnosticInfo.MaxInnerDepth)
                {
                    Assert.Null(innerDiagnosticInfo);
                    break;
                }
            }
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

            Assert.False(nodeId2 < inodeId2);
            Assert.True(nodeId2 == inodeId2);
            Assert.False(nodeId2 > inodeId2);

            string text = "i=123";
            NodeId nodeIdText = new NodeId(text);
            Assert.AreEqual(123, nodeIdText.Identifier);
            // implicit conversion;
            NodeId inodeIdText = text;
            Assert.AreEqual(nodeIdText, inodeIdText);

            Assert.False(nodeIdText < inodeIdText);
            Assert.True(nodeIdText == inodeIdText);
            Assert.False(nodeIdText > inodeIdText);

            _ = nodeIdText < nodeId2;
            _ = nodeIdText == nodeId2;
            _ = nodeIdText > nodeId2;

            NodeId id;
            id = new NodeId((object)(uint)123, 123);
            Assert.AreEqual(123, id.NamespaceIndex);
            Assert.AreEqual(123, id.Identifier);
            id = new NodeId((object)"Test", 123);
            Assert.AreEqual(123, id.NamespaceIndex);
            Assert.AreEqual("Test", id.Identifier);
            id = new NodeId((object)id2, 123);
            Assert.AreEqual(123, id.NamespaceIndex);
            Assert.AreEqual(id2, id.Identifier);
            id = new NodeId((object)null, 123);
            Assert.AreEqual(123, id.NamespaceIndex);
            Assert.AreEqual(null, id.Identifier);
            id = new NodeId((object)id1, 123);
            Assert.AreEqual(123, id.NamespaceIndex);
            Assert.AreEqual(id1, id.Identifier);
            var guid = Guid.NewGuid();
            id = new NodeId((object)guid, 123);
            Assert.AreEqual(123, id.NamespaceIndex);
            Assert.AreEqual(guid, id.Identifier);
            Assert.Throws<ArgumentException>(() => _ = new NodeId((object)(long)7777777, 123));

            var sre = Assert.Throws<ServiceResultException>(() => _ = NodeId.Create(123, "urn:xyz", new NamespaceTable()));
            Assert.AreEqual(StatusCodes.BadNodeIdInvalid, sre.StatusCode);

            NodeId opaqueId = new byte[] { 33, 44, 55, 66 };
            NodeId stringId1 = "ns=1;s=Test";
            NodeId stringId2 = new NodeId("ns=1;s=Test");
            Assert.AreEqual(stringId1, stringId2);
            Assert.Throws<ArgumentException>(() => new NodeId("Test"));
            Assert.Throws<ArgumentException>(() => new NodeId("nsu=urn:xyz;Test"));
            ExpandedNodeId expandedId1 = new ExpandedNodeId("nsu=urn:xyz;Test");
            Assert.NotNull(expandedId1);
            NodeId nullId = ExpandedNodeId.ToNodeId(null, new NamespaceTable());
            Assert.IsNull(nullId);

            // create a nodeId from a guid
            Guid guid1 = Guid.NewGuid(), guid2 = Guid.NewGuid();
            NodeId nodeGuid1 = new NodeId(id1);

            // now to compare the nodeId to the guids
            Assert.True(nodeGuid1.Equals(id1));
            Assert.True(nodeGuid1 == id1);
            Assert.True(nodeGuid1 == (NodeId)id1);
            Assert.True(nodeGuid1.Equals((Uuid)id1));
            Assert.True(nodeGuid1 == (Uuid)id1);
            Assert.False(nodeGuid1.Equals(id2));
            Assert.False(nodeGuid1 == id2);

            id.SetIdentifier("Test", IdType.Opaque);

            Assert.Throws<ArgumentException>(() => _ = new NodeId((object)(int)123, 123));
            Assert.Throws<ServiceResultException>(() => _ = NodeId.Create((uint)123, "urn:xyz", null));
            Assert.Throws<ServiceResultException>(() => _ = NodeId.Parse("ns="));
            Assert.Throws<ArgumentException>(() => _ = NodeId.Parse("nsu="));
            Assert.Throws<ArgumentException>(() => _ = NodeId.Parse("Test"));
            Assert.Throws<ArgumentException>(() => { NodeId _ = "Test"; });
            Assert.Throws<ArgumentException>(() => _ = NodeId.Parse("nsu=http://opcfoundation.org/Tests;s=Test"));
            Assert.Throws<ArgumentException>(() => { NodeId _ = "nsu=http://opcfoundation.org/Tests;s=Test"; });
            Assert.IsNull(NodeId.ToExpandedNodeId(null, null));

            // IsNull
            Assert.True(NodeId.IsNull((ExpandedNodeId)null));
            Assert.True(NodeId.IsNull(new ExpandedNodeId(Guid.Empty)));
            Assert.True(NodeId.IsNull(ExpandedNodeId.Null));
            Assert.True(NodeId.IsNull(new ExpandedNodeId(new byte[0])));
            Assert.True(NodeId.IsNull(new ExpandedNodeId("", 0)));
            Assert.True(NodeId.IsNull(new ExpandedNodeId(0)));
            Assert.False(NodeId.IsNull(new ExpandedNodeId(1)));

            string[] testStrings = new string[] {
                "i=1234", "s=HelloWorld", "g=af469096-f02a-4563-940b-603958363b81", "b=01020304",
                "ns=2;s=HelloWorld", "ns=2;i=1234", "ns=2;g=af469096-f02a-4563-940b-603958363b82", "ns=2;b=04030201"
            };

            foreach (var testString in testStrings)
            {
                NodeId nodeId = NodeId.Parse(testString);
                Assert.AreEqual(testString, nodeId.ToString());
                nodeId = testString;
                Assert.AreEqual(testString, nodeId.ToString());
            }
        }

        [Test]
        public void ExpandedNodeIdConstructor()
        {
            ExpandedNodeId id;

            // Guid
            Guid guid1 = Guid.NewGuid();
            ExpandedNodeId nodeId1 = new ExpandedNodeId(guid1);

            // implicit conversion;
            ExpandedNodeId inodeId1 = guid1;
            Assert.AreEqual(nodeId1, inodeId1);

            // byte[]
            byte[] byteid2 = new byte[] { 65, 66, 67, 68, 69 };
            ExpandedNodeId nodeId2 = new ExpandedNodeId(byteid2);

            // implicit conversion;
            ExpandedNodeId inodeId2 = byteid2;
            Assert.AreEqual(nodeId2, inodeId2);

            Assert.AreEqual(nodeId1.GetHashCode(), inodeId1.GetHashCode());
            Assert.False(nodeId2 < inodeId2);
            Assert.True(nodeId2 == inodeId2);
            Assert.False(nodeId2 > inodeId2);

            // string
            string text = "i=123";
            ExpandedNodeId nodeIdText = new ExpandedNodeId(text);
            Assert.AreEqual(123, nodeIdText.Identifier);

            // implicit conversion;
            ExpandedNodeId inodeIdText = text;
            Assert.AreEqual(nodeIdText, inodeIdText);

            // implicit conversion;
            ExpandedNodeId inodeIdText2 = 123;
            Assert.AreEqual(inodeIdText2, inodeIdText);

            Assert.False(nodeIdText < inodeIdText);
            Assert.True(nodeIdText == inodeIdText);
            Assert.True(nodeIdText == inodeIdText2);
            Assert.False(nodeIdText > inodeIdText);

            Assert.True(nodeIdText < nodeId2);
            Assert.False(nodeIdText == nodeId2);
            Assert.False(nodeIdText > nodeId2);

            _ = new ExpandedNodeId((uint)123, 123);
            _ = new ExpandedNodeId("Test", 123);
            _ = new ExpandedNodeId(byteid2, 123);
            _ = new ExpandedNodeId(0, 123);
            _ = new ExpandedNodeId(guid1, 123);

            id = "ns=1;s=Test";
            ExpandedNodeId nodeId = NodeId.Parse("ns=1;s=Test");
            Assert.AreEqual(1, nodeId.NamespaceIndex);
            Assert.AreEqual("Test", nodeId.Identifier);
            Assert.AreEqual("ns=1;s=Test", nodeId.ToString());
            Assert.AreEqual(nodeId, id);
            Assert.True(nodeId == id);

            id = "s=Test";
            nodeId = NodeId.Parse("s=Test");
            Assert.AreEqual(0, nodeId.NamespaceIndex);
            Assert.AreEqual("Test", nodeId.Identifier);
            Assert.AreEqual("s=Test", nodeId.ToString());
            Assert.AreEqual(nodeId, id);
            Assert.True(nodeId == id);

            string namespaceUri = "http://opcfoundation.org/Namespace";

            id = new ExpandedNodeId((object)(uint)123, 321, namespaceUri, 2);
            Assert.AreEqual(2, id.ServerIndex);
            Assert.AreEqual(123, (uint)id.Identifier);
            Assert.AreEqual(321, id.NamespaceIndex);
            Assert.AreEqual(namespaceUri, id.NamespaceUri);
            Assert.AreEqual($"svr=2;nsu={namespaceUri};ns=321;i=123", id.ToString());

            id = new ExpandedNodeId((object)"Test", 123, namespaceUri, 1);
            nodeId = new ExpandedNodeId((object)byteid2, 123, namespaceUri, 0);
            _ = new ExpandedNodeId((object)null, 123, namespaceUri, 1);
            nodeId2 = new ExpandedNodeId((object)guid1, 123, namespaceUri, 1);
            Assert.AreNotEqual(nodeId, nodeId2);
            Assert.AreNotEqual(nodeId.GetHashCode(), nodeId2.GetHashCode());

            var teststring = "nsu=http://opcfoundation.org/Namespace;s=Test";
            nodeId = teststring;
            nodeId2 = ExpandedNodeId.Parse(teststring);
            Assert.AreEqual(nodeId, nodeId2);
            Assert.AreEqual(teststring, nodeId2.ToString());

            Assert.Throws<ArgumentException>(() => _ = new ExpandedNodeId((object)(int)123, 123, namespaceUri, 1));
            Assert.Throws<ServiceResultException>(() => _ = ExpandedNodeId.Parse("ns="));
            Assert.Throws<ServiceResultException>(() => _ = ExpandedNodeId.Parse("nsu="));
            Assert.Throws<ArgumentException>(() => id = "Test");
            Assert.IsNull(NodeId.ToExpandedNodeId(null, null));

            string[] testStrings = new string[] {
                "i=1234", "s=HelloWorld", "g=af469096-f02a-4563-940b-603958363b81", "b=01020304",
                "ns=2;s=HelloWorld", "ns=2;i=1234", "ns=2;g=af469096-f02a-4563-940b-603958363b82", "ns=2;b=04030201"
            };

            foreach (var testString in testStrings)
            {
                id = ExpandedNodeId.Parse(testString);
                Assert.AreEqual(testString, id.ToString());
                id = testString;
                Assert.AreEqual(testString, id.ToString());
            }
        }

        [Test]
        public void NodeIdHashCode()
        {
            // ensure that the hash code is the same for the same node id
            IList<NodeId> nodeIds = new List<NodeId>();

            // Null NodeIds
            nodeIds.Add(0);
            nodeIds.Add("");
            nodeIds.Add(NodeId.Null);
            nodeIds.Add(new NodeId(0));
            nodeIds.Add(new NodeId(Guid.Empty));
            nodeIds.Add(new NodeId(""));
            nodeIds.Add(new NodeId(new byte[0]));

            foreach (NodeId nodeId in nodeIds)
            {
                Assert.IsTrue(nodeId.IsNullNodeId);
            }

            // validate the hash code of null node id compares as equal
            foreach (NodeId nodeId in nodeIds)
            {
                foreach (NodeId nodeIdExpected in nodeIds)
                {
                    Assert.AreEqual(nodeIdExpected.GetHashCode(), nodeId.GetHashCode(), $"Expected{nodeIdExpected}!=NodeId={nodeId}");
                }
            }

            int distinctNodes = 1;
            nodeIds.Insert(0, new NodeId(123));
            nodeIds.Insert(0, new NodeId(123, 0));
            distinctNodes++;
            Assert.AreEqual(nodeIds[0].GetHashCode(), nodeIds[1].GetHashCode());
            Assert.AreEqual(nodeIds[0], nodeIds[1]);

            nodeIds.Insert(0, new NodeId(123, 1));
            distinctNodes++;
            Assert.AreNotEqual(nodeIds[0].GetHashCode(), nodeIds[1].GetHashCode());

            nodeIds.Insert(0, new NodeId("Test", 0));
            distinctNodes++;
            Assert.AreNotEqual(nodeIds[0].GetHashCode(), nodeIds[2].GetHashCode());

            TestContext.Out.WriteLine("NodeIds:");
            foreach (NodeId nodeId in nodeIds)
            {
                TestContext.Out.WriteLine($"NodeId={nodeId}, HashCode={nodeId.GetHashCode():x8}");
            }

            TestContext.Out.WriteLine("Distinct NodeIds:");
            var distinctNodeIds = nodeIds.Distinct();
            foreach (NodeId nodeId in distinctNodeIds)
            {
                TestContext.Out.WriteLine($"NodeId={nodeId}, HashCode={nodeId.GetHashCode():x8}");
            }
            // all null node ids should be equal and removed
            Assert.AreEqual(distinctNodes, distinctNodeIds.Count());
        }

        [Theory]
        [TestCase(-1)]
        [Repeat(100)]
        public void NodeIdComparison(Opc.Ua.IdType idType)
        {
            NodeId nodeId = DataGenerator.GetRandomNodeId();
            switch (idType)
            {
                case Opc.Ua.IdType.Numeric: nodeId = new NodeId(DataGenerator.GetRandomUInt16(), DataGenerator.GetRandomByte()); break;
                case Opc.Ua.IdType.String: nodeId = new NodeId(DataGenerator.GetRandomString(), 0); break;
                case Opc.Ua.IdType.Guid: nodeId = new NodeId(DataGenerator.GetRandomGuid()); break;
                case Opc.Ua.IdType.Opaque: nodeId = new NodeId(Utils.Nonce.CreateNonce(32)); break;
            }
            NodeId nodeIdClone = (NodeId)nodeId.Clone();
            Assert.AreEqual(nodeId, nodeIdClone);
            Assert.AreEqual(nodeId.GetHashCode(), nodeIdClone.GetHashCode());
            Assert.AreEqual(nodeIdClone.GetHashCode(), nodeIdClone.GetHashCode());
            Assert.AreEqual(nodeId.GetHashCode(), nodeId.GetHashCode());
            Assert.IsTrue(nodeId.Equals(nodeIdClone));
            NodeId id = nodeId;
            Assert.False(id < nodeId);
            Assert.False(id > nodeId);
            Assert.True(id == nodeId);

            var dictionary = new Dictionary<NodeId, string>();
            dictionary.Add(nodeId, "Test");
            Assert.IsTrue(dictionary.ContainsKey(nodeId));
            Assert.IsTrue(dictionary.ContainsKey(nodeIdClone));
            Assert.IsTrue(dictionary.ContainsKey((NodeId)nodeIdClone.Clone()));
            Assert.IsTrue(dictionary.TryGetValue(nodeId, out string value));

            Assert.Throws<ArgumentException>(() => dictionary.Add(nodeIdClone, "TestClone"));

            NodeId nodeId2 = DataGenerator.GetRandomNodeId();
            switch (idType)
            {
                case Opc.Ua.IdType.Numeric: nodeId2 = new NodeId(DataGenerator.GetRandomUInt16(), DataGenerator.GetRandomByte()); break;
                case Opc.Ua.IdType.String: nodeId2 = new NodeId(DataGenerator.GetRandomString(), 0); break;
                case Opc.Ua.IdType.Guid: nodeId2 = new NodeId(DataGenerator.GetRandomGuid()); break;
                case Opc.Ua.IdType.Opaque: nodeId2 = new NodeId(Utils.Nonce.CreateNonce(32)); break;
            }
            dictionary.Add(nodeId2, "TestClone");
            Assert.AreEqual(2, dictionary.Distinct().ToList().Count);
        }

        [Theory]
        [TestCase(-1)]
        [TestCase(100)]
        public void NullIdNodeIdComparison(Opc.Ua.IdType idType)
        {
            NodeId nodeId = NodeId.Null;
            switch (idType)
            {
                case Opc.Ua.IdType.Numeric: nodeId = new NodeId(0, 0); break;
                case Opc.Ua.IdType.String: nodeId = new NodeId(""); break;
                case Opc.Ua.IdType.Guid: nodeId = new NodeId(Guid.Empty); break;
                case Opc.Ua.IdType.Opaque: nodeId = new NodeId(new byte[0]); break;
                case (Opc.Ua.IdType)100: nodeId = new NodeId((byte[])null); break;
            }

            Assert.IsTrue(nodeId.IsNullNodeId);

            Assert.AreEqual(nodeId, NodeId.Null);
            Assert.AreEqual(nodeId, new NodeId(0, 0));
            Assert.AreEqual(nodeId, new NodeId(Guid.Empty));
            Assert.AreEqual(nodeId, new NodeId(new byte[0]));
            Assert.AreEqual(nodeId, new NodeId((byte[])null));
            Assert.AreEqual(nodeId, new NodeId((string)null));

            Assert.True(nodeId.Equals(NodeId.Null));
            Assert.True(nodeId.Equals(new NodeId(0, 0)));
            Assert.True(nodeId.Equals(new NodeId(Guid.Empty)));
            Assert.True(nodeId.Equals(new NodeId(new byte[0])));
            Assert.True(nodeId.Equals(new NodeId((byte[])null)));
            Assert.True(nodeId.Equals(new NodeId((string)null)));

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

            Assert.AreEqual(nodeIdBasedDataValue.Value, nodeId);
            Assert.AreEqual(nodeIdBasedDataValue.Value.GetHashCode(), nodeId.GetHashCode());
        }

        [Test]
        public void ShouldNotThrow()
        {
            ExpandedNodeId[] expandedNodeIds1 = new ExpandedNodeId[] { new ExpandedNodeId(0), new ExpandedNodeId(0) };
            ExpandedNodeId[] expandedNodeIds2 = new ExpandedNodeId[] { new ExpandedNodeId((byte[])null), new ExpandedNodeId((byte[])null) };
            DataValue dv1 = new DataValue(expandedNodeIds1);
            DataValue dv2 = new DataValue(expandedNodeIds2);
            Assert.DoesNotThrow(() => dv1.Equals(dv2));

            ExpandedNodeId byteArrayNodeId = new ExpandedNodeId((byte[])null);
            ExpandedNodeId expandedNodeId = new ExpandedNodeId((NodeId)null);
            Assert.DoesNotThrow(() => byteArrayNodeId.Equals(expandedNodeId));
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
