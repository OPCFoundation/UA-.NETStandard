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
using Microsoft.Extensions.Logging;
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
    public class BuiltInTests
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
            var variant2 = new Variant(randomData, TypeInfo.CreateScalar(builtInType));
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
            if (builtInType == BuiltInType.Byte)
            {
                // Without hint, byte array can not be distinguished from bytestring
                Assert.AreEqual(BuiltInType.ByteString, variant1.TypeInfo.BuiltInType);
            }
            else
            {
                Assert.AreEqual(builtInType, variant1.TypeInfo.BuiltInType);
            }
            var variant2 = new Variant(randomData, TypeInfo.CreateArray(builtInType));
            Assert.AreEqual(builtInType, variant2.TypeInfo.BuiltInType);
        }

        /// <summary>
        /// Variant constructor.
        /// </summary>
        [Test]
        public void VariantConstructor()
        {
            var uuid = new Uuid(Guid.NewGuid());
            var variant1 = new Variant(uuid);
            Assert.AreEqual(BuiltInType.Guid, variant1.TypeInfo.BuiltInType);
        }

        /// <summary>
        /// Initialize Variant with Enum array.
        /// </summary>
        [Test]
        public void VariantFromEnumArray()
        {
            // Enum Scalar
            _ = new Variant(DayOfWeek.Monday);

            _ = new Variant(
                DayOfWeek.Monday,
                TypeInfo.Scalars.Enumeration);

            // Enum array
            var days = new DayOfWeek[] { DayOfWeek.Monday, DayOfWeek.Tuesday };

            _ = new Variant(days, TypeInfo.Arrays.Enumeration);

            _ = new Variant(days);

            // Enum 2-dim Array
            var daysdays = new DayOfWeek[,]
            {
                { DayOfWeek.Monday, DayOfWeek.Tuesday },
                { DayOfWeek.Monday, DayOfWeek.Tuesday }
            };

            _ = new Variant(
                daysdays,
                TypeInfo.Create(BuiltInType.Enumeration, ValueRanks.TwoDimensions));

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
            var extensionObject_Default = new ExtensionObject();
            Assert.NotNull(extensionObject_Default);
            Assert.AreEqual(ExpandedNodeId.Null, extensionObject_Default.TypeId);
            Assert.AreEqual(ExtensionObjectEncoding.None, extensionObject_Default.Encoding);
            Assert.Null(extensionObject_Default.Body);
            // Constructor by ExtensionObject
            var extensionObject = new ExtensionObject(ExpandedNodeId.Null);
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
            NUnit.Framework.Assert
                .Throws<ArgumentNullException>(() => new ExtensionObject(extensionObject_null));
            NUnit.Framework.Assert
                .Throws<ServiceResultException>(() => new ExtensionObject(new object()));
            // constructor by object
            byte[] byteArray = [1, 2, 3];
            extensionObject = new ExtensionObject((object)byteArray);
            Assert.NotNull(extensionObject);
            Assert.AreEqual(extensionObject, extensionObject);
            // string extension
            string extensionObjectString = extensionObject.ToString();
            NUnit.Framework.Assert
                .Throws<FormatException>(() => extensionObject.ToString("123", null));
            Assert.NotNull(extensionObjectString);
            // clone
            ExtensionObject clonedExtensionObject = Utils.Clone(extensionObject);
            Assert.AreEqual(extensionObject, clonedExtensionObject);
            // IsEqual operator
            clonedExtensionObject.TypeId = new ExpandedNodeId(333);
            Assert.AreNotEqual(extensionObject, clonedExtensionObject);
            Assert.AreNotEqual(extensionObject, extensionObject_Default);
            Assert.AreNotEqual(extensionObject, new object());
            Assert.AreEqual(clonedExtensionObject, clonedExtensionObject);
            Assert.AreEqual(ExpandedNodeId.Null, extensionObject.TypeId);
            Assert.AreEqual(
                ExpandedNodeId.Null.GetHashCode(),
                extensionObject.TypeId.GetHashCode());
            Assert.AreEqual(ExtensionObjectEncoding.Binary, extensionObject.Encoding);
            Assert.AreEqual(byteArray, extensionObject.Body);
            Assert.AreEqual(byteArray.GetHashCode(), extensionObject.Body.GetHashCode());
            // collection
            var collection = new ExtensionObjectCollection();
            Assert.NotNull(collection);
            collection = new ExtensionObjectCollection(100);
            Assert.NotNull(collection);
            collection = [.. collection];
            Assert.NotNull(collection);
            collection = Utils.Clone(collection);
            // default value is null
            Assert.Null(TypeInfo.GetDefaultValue(BuiltInType.ExtensionObject));
        }

        /// <summary>
        /// Ensure defaults are correct.
        /// </summary>
        [Test]
        public void DiagnosticInfoDefault()
        {
            var diagnosticInfo = new DiagnosticInfo();
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
            var stringTable = new StringTable();
            var serviceResult = new ServiceResult(
                StatusCodes.BadAggregateConfigurationRejected,
                "SymbolicId",
                Namespaces.OpcUa,
                new LocalizedText("The text", "en-us"),
                new Exception("The inner exception."));
            ILogger logger = Telemetry.CreateLogger<BuiltInTests>();
            var diagnosticInfo = new DiagnosticInfo(
                serviceResult,
                DiagnosticsMasks.All,
                true,
                stringTable,
                logger);
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
            diagnosticInfo = new DiagnosticInfo(
                serviceResult,
                DiagnosticsMasks.All,
                true,
                stringTable,
                logger);
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
            int[,,] testArray = new int[,,]
            {
                {
                    { 1, 2, 3 },
                    { 4, 5, 6 },
                    { 7, 8, 9 }
                },
                {
                    { 11, 12, 13 },
                    { 14, 15, 16 },
                    { 17, 18, 19 }
                }
            };
            var matrix = new Matrix(
                testArray,
                TypeInfo.GetBuiltInType(new NodeId((int)BuiltInType.Int32)));
            var toArray = matrix.ToArray();
            Assert.AreEqual(testArray, toArray);
            Assert.True(Utils.IsEqual(testArray, toArray));
        }

        [Test]
        public void NodeIdConstructor()
        {
            var id1 = Guid.NewGuid();
            var nodeId1 = new NodeId(id1);
            // implicit conversion;
            NodeId inodeId1 = id1;
            Assert.AreEqual(nodeId1, inodeId1);

            byte[] id2 = [65, 66, 67, 68, 69];
            var nodeId2 = new NodeId(id2);
            // implicit conversion;
            NodeId inodeId2 = id2;
            Assert.AreEqual(nodeId2, inodeId2);

            Assert.False(nodeId2 < inodeId2);
            Assert.True(nodeId2 == inodeId2);
            Assert.False(nodeId2 > inodeId2);

            const string text = "i=123";
            var nodeIdText = new NodeId(text);
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

            var id = new NodeId((object)(uint)123, 123);
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
            NUnit.Framework.Assert
                .Throws<ArgumentException>(() => _ = new NodeId((long)7777777, 123));

            ServiceResultException sre = NUnit.Framework.Assert.Throws<ServiceResultException>(() =>
                _ = NodeId.Create(123, "urn:xyz", new NamespaceTable()));
            Assert.AreEqual((StatusCode)StatusCodes.BadNodeIdInvalid, (StatusCode)sre.StatusCode);

            NodeId opaqueId = "!,7B"u8.ToArray();
            NodeId stringId1 = "ns=1;s=Test";
            var stringId2 = new NodeId("ns=1;s=Test");
            Assert.AreEqual(stringId1, stringId2);
            NUnit.Framework.Assert.Throws<ArgumentException>(() => new NodeId("Test"));
            NUnit.Framework.Assert.Throws<ArgumentException>(() => new NodeId("nsu=urn:xyz;Test"));
            var expandedId1 = new ExpandedNodeId("nsu=urn:xyz;Test");
            Assert.NotNull(expandedId1);
            var nullId = ExpandedNodeId.ToNodeId(null, new NamespaceTable());
            Assert.IsNull(nullId);

            // create a nodeId from a guid
            var guid1 = Guid.NewGuid();
            var guid2 = Guid.NewGuid();
            var nodeGuid1 = new NodeId(id1);

            // now to compare the nodeId to the guids
            Assert.True(nodeGuid1.Equals(id1));
            Assert.True(nodeGuid1 == id1);
            Assert.True(nodeGuid1 == (NodeId)id1);
            Assert.True(nodeGuid1.Equals((Uuid)id1));
            Assert.True(nodeGuid1 == (Uuid)id1);
            Assert.False(nodeGuid1.Equals(id2));
            Assert.False(nodeGuid1 == id2);

            id.SetIdentifier("Test", IdType.Opaque);

            NUnit.Framework.Assert
                .Throws<ArgumentException>(() => _ = new NodeId((object)123, 123));
            NUnit.Framework.Assert.Throws<ServiceResultException>(
                () => _ = NodeId.Create((uint)123, "urn:xyz", null));
            NUnit.Framework.Assert.Throws<ServiceResultException>(() => _ = NodeId.Parse("ns="));
            NUnit.Framework.Assert.Throws<ArgumentException>(() => _ = NodeId.Parse("nsu="));
            NUnit.Framework.Assert.Throws<ArgumentException>(() => _ = NodeId.Parse("Test"));
            NUnit.Framework.Assert.Throws<ArgumentException>(() =>
            {
                NodeId _ = "Test";
            });
            NUnit.Framework.Assert.Throws<ArgumentException>(() =>
                _ = NodeId.Parse("nsu=http://opcfoundation.org/Tests;s=Test"));
            NUnit.Framework.Assert.Throws<ArgumentException>(() =>
            {
                NodeId _ = "nsu=http://opcfoundation.org/Tests;s=Test";
            });
            Assert.IsNull(NodeId.ToExpandedNodeId(null, null));

            // IsNull
            Assert.True(NodeId.IsNull((ExpandedNodeId)null));
            Assert.True(NodeId.IsNull(new ExpandedNodeId(Guid.Empty)));
            Assert.True(NodeId.IsNull(ExpandedNodeId.Null));
            Assert.True(NodeId.IsNull(new ExpandedNodeId([])));
            Assert.True(NodeId.IsNull(new ExpandedNodeId(string.Empty, 0)));
            Assert.True(NodeId.IsNull(new ExpandedNodeId(0)));
            Assert.False(NodeId.IsNull(new ExpandedNodeId(1)));

            string[] testStrings =
            [
                "i=1234",
                "s=HelloWorld",
                "g=af469096-f02a-4563-940b-603958363b81",
                "b=01020304",
                "ns=2;s=HelloWorld",
                "ns=2;i=1234",
                "ns=2;g=af469096-f02a-4563-940b-603958363b82",
                "ns=2;b=04030201"
            ];

            foreach (string testString in testStrings)
            {
                var nodeId = NodeId.Parse(testString);
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
            var guid1 = Guid.NewGuid();
            var nodeId1 = new ExpandedNodeId(guid1);

            // implicit conversion;
            ExpandedNodeId inodeId1 = guid1;
            Assert.AreEqual(nodeId1, inodeId1);

            // byte[]
            byte[] byteid2 = [65, 66, 67, 68, 69];
            var nodeId2 = new ExpandedNodeId(byteid2);

            // implicit conversion;
            ExpandedNodeId inodeId2 = byteid2;
            Assert.AreEqual(nodeId2, inodeId2);

            Assert.AreEqual(nodeId1.GetHashCode(), inodeId1.GetHashCode());
            Assert.False(nodeId2 < inodeId2);
            Assert.True(nodeId2 == inodeId2);
            Assert.False(nodeId2 > inodeId2);

            // string
            const string text = "i=123";
            var nodeIdText = new ExpandedNodeId(text);
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

            _ = new ExpandedNodeId(123, 123);
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

            const string namespaceUri = "http://opcfoundation.org/Namespace";

            id = new ExpandedNodeId((uint)123, 321, namespaceUri, 2);
            Assert.AreEqual(2, id.ServerIndex);
            Assert.AreEqual(123, (uint)id.Identifier);
            Assert.AreEqual(321, id.NamespaceIndex);
            Assert.AreEqual(namespaceUri, id.NamespaceUri);
            Assert.AreEqual($"svr=2;nsu={namespaceUri};ns=321;i=123", id.ToString());

            id = new ExpandedNodeId("Test", 123, namespaceUri, 1);
            nodeId = new ExpandedNodeId(byteid2, 123, namespaceUri, 0);
            _ = new ExpandedNodeId(null, 123, namespaceUri, 1);
            nodeId2 = new ExpandedNodeId(guid1, 123, namespaceUri, 1);
            Assert.AreNotEqual(nodeId, nodeId2);
            Assert.AreNotEqual(nodeId.GetHashCode(), nodeId2.GetHashCode());

            const string teststring = "nsu=http://opcfoundation.org/Namespace;s=Test";
            nodeId = teststring;
            nodeId2 = ExpandedNodeId.Parse(teststring);
            Assert.AreEqual(nodeId, nodeId2);
            Assert.AreEqual(teststring, nodeId2.ToString());

            NUnit.Framework.Assert
                .Throws<ArgumentException>(() => _ = new ExpandedNodeId(123, 123, namespaceUri, 1));
            NUnit.Framework.Assert
                .Throws<ServiceResultException>(() => _ = ExpandedNodeId.Parse("ns="));
            NUnit.Framework.Assert
                .Throws<ServiceResultException>(() => _ = ExpandedNodeId.Parse("nsu="));
            NUnit.Framework.Assert.Throws<ArgumentException>(() => id = "Test");
            Assert.IsNull(NodeId.ToExpandedNodeId(null, null));

            string[] testStrings =
            [
                "i=1234",
                "s=HelloWorld",
                "g=af469096-f02a-4563-940b-603958363b81",
                "b=01020304",
                "ns=2;s=HelloWorld",
                "ns=2;i=1234",
                "ns=2;g=af469096-f02a-4563-940b-603958363b82",
                "ns=2;b=04030201"
            ];

            foreach (string testString in testStrings)
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
            var nodeIds = new List<NodeId>
            {
                // Null NodeIds
                0,
                string.Empty,
                NodeId.Null,
                new(0),
                new(Guid.Empty),
                new(string.Empty),
                new([])
            };

            foreach (NodeId nodeId in nodeIds)
            {
                Assert.IsTrue(nodeId.IsNullNodeId);
            }

            // validate the hash code of null node id compares as equal
            foreach (NodeId nodeId in nodeIds)
            {
                foreach (NodeId nodeIdExpected in nodeIds)
                {
                    Assert.AreEqual(
                        nodeIdExpected.GetHashCode(),
                        nodeId.GetHashCode(),
                        $"Expected{nodeIdExpected}!=NodeId={nodeId}");
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
            IEnumerable<NodeId> distinctNodeIds = nodeIds.Distinct();
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
        public void NodeIdComparison(IdType idType)
        {
            NodeId nodeId;
            switch (idType)
            {
                case IdType.Numeric:
                    nodeId = new NodeId(
                        DataGenerator.GetRandomUInt16(),
                        DataGenerator.GetRandomByte());
                    break;
                case IdType.String:
                    nodeId = new NodeId(DataGenerator.GetRandomString(), 0);
                    break;
                case IdType.Guid:
                    nodeId = new NodeId(DataGenerator.GetRandomGuid());
                    break;
                case IdType.Opaque:
                    nodeId = new NodeId(Ua.Nonce.CreateRandomNonceData(32));
                    break;
                default:
                    nodeId = DataGenerator.GetRandomNodeId();
                    break;
            }
            var nodeIdClone = (NodeId)nodeId.Clone();
            Assert.AreEqual(nodeId, nodeIdClone);
            Assert.AreEqual(nodeId.GetHashCode(), nodeIdClone.GetHashCode());
            Assert.AreEqual(nodeIdClone.GetHashCode(), nodeIdClone.GetHashCode());
            Assert.AreEqual(nodeId.GetHashCode(), nodeId.GetHashCode());
            Assert.IsTrue(nodeId.Equals(nodeIdClone));
            NodeId id = nodeId;
            Assert.False(id < nodeId);
            Assert.False(id > nodeId);
            Assert.True(id == nodeId);

            var dictionary = new Dictionary<NodeId, string> { { nodeId, "Test" } };
            Assert.IsTrue(dictionary.ContainsKey(nodeId));
            Assert.IsTrue(dictionary.ContainsKey(nodeIdClone));
            Assert.IsTrue(dictionary.ContainsKey((NodeId)nodeIdClone.Clone()));
            Assert.IsTrue(dictionary.TryGetValue(nodeId, out string value));

            NUnit.Framework.Assert
                .Throws<ArgumentException>(() => dictionary.Add(nodeIdClone, "TestClone"));

            NodeId nodeId2;
            switch (idType)
            {
                case IdType.Numeric:
                    nodeId2 = new NodeId(
                        DataGenerator.GetRandomUInt16(),
                        DataGenerator.GetRandomByte());
                    break;
                case IdType.String:
                    nodeId2 = new NodeId(DataGenerator.GetRandomString(), 0);
                    break;
                case IdType.Guid:
                    nodeId2 = new NodeId(DataGenerator.GetRandomGuid());
                    break;
                case IdType.Opaque:
                    nodeId2 = new NodeId(Ua.Nonce.CreateRandomNonceData(32));
                    break;
                default:
                    nodeId2 = DataGenerator.GetRandomNodeId();
                    break;
            }
            dictionary.Add(nodeId2, "TestClone");
            Assert.AreEqual(2, dictionary.Distinct().ToList().Count);
        }

        [Theory]
        [TestCase(-1)]
        [TestCase(100)]
        public void NullIdNodeIdComparison(IdType idType)
        {
            NodeId nodeId;
            switch (idType)
            {
                case IdType.Numeric:
                    nodeId = new NodeId(0, 0);
                    break;
                case IdType.String:
                    nodeId = new NodeId(string.Empty);
                    break;
                case IdType.Guid:
                    nodeId = new NodeId(Guid.Empty);
                    break;
                case IdType.Opaque:
                    nodeId = new NodeId([]);
                    break;
                case (IdType)100:
                    nodeId = new NodeId((byte[])null);
                    break;
                default:
                    nodeId = NodeId.Null;
                    break;
            }

            Assert.IsTrue(nodeId.IsNullNodeId);

            Assert.AreEqual(nodeId, NodeId.Null);
            Assert.AreEqual(nodeId, new NodeId(0, 0));
            Assert.AreEqual(nodeId, new NodeId(Guid.Empty));
            Assert.AreEqual(nodeId, new NodeId([]));
            Assert.AreEqual(nodeId, new NodeId((byte[])null));
            Assert.AreEqual(nodeId, new NodeId((string)null));

            Assert.True(nodeId.Equals(NodeId.Null));
            Assert.True(nodeId.Equals(new NodeId(0, 0)));
            Assert.True(nodeId.Equals(new NodeId(Guid.Empty)));
            Assert.True(nodeId.Equals(new NodeId([])));
            Assert.True(nodeId.Equals(new NodeId((byte[])null)));
            Assert.True(nodeId.Equals(new NodeId((string)null)));

            var nodeIdBasedDataValue = new DataValue(new Variant(nodeId));

            var dataValue = new DataValue(Attributes.NodeClass)
            {
                WrappedValue = new Variant((int)Attributes.NodeClass), // without this cast the second and third asserts evaluate correctly.
                StatusCode = nodeIdBasedDataValue.StatusCode
            };

            bool comparisonResult1b = dataValue.Equals(nodeIdBasedDataValue);
            Assert.IsFalse(comparisonResult1b); // assert succeeds

            bool comparisonResult1a = nodeIdBasedDataValue.Equals(dataValue);
            Assert.IsFalse(comparisonResult1a); // assert fails (symmetry for Equals is broken)

            bool comparisonResult1c = EqualityComparer<DataValue>.Default
                .Equals(nodeIdBasedDataValue, dataValue);
            Assert.IsFalse(comparisonResult1c); // assert fails

            int comparisonResult2 = nodeId.CompareTo(dataValue);
            Assert.IsFalse(comparisonResult2 == 0); // assert fails - this is the root cause for the previous assertion failures

            Assert.AreEqual(nodeIdBasedDataValue.Value, nodeId);
            Assert.AreEqual(nodeIdBasedDataValue.Value.GetHashCode(), nodeId.GetHashCode());
        }

        [Test]
        public void ShouldNotThrow()
        {
            var expandedNodeIds1 = new ExpandedNodeId[] { new(0), new(0) };
            var expandedNodeIds2 = new ExpandedNodeId[] { new((byte[])null), new((byte[])null) };
            var dv1 = new DataValue(new Variant(expandedNodeIds1));
            var dv2 = new DataValue(new Variant(expandedNodeIds2));
            NUnit.Framework.Assert.DoesNotThrow(() => dv1.Equals(dv2));

            var byteArrayNodeId = new ExpandedNodeId((byte[])null);
            var expandedNodeId = new ExpandedNodeId((NodeId)null);
            NUnit.Framework.Assert.DoesNotThrow(() => byteArrayNodeId.Equals(expandedNodeId));
        }

        [Test]
        public void NodeIdParseInvalidWithNamespace()
        {
            // Test cases that should throw an exception because they lack proper identifier prefix
            // These were incorrectly accepted as string identifiers in the bug
            string[] invalidNodeIds =
            [
                "ns=1;some_text_without_prefix",
                "ns=4; some_number_or_text_here",
                "ns=2;12345",
                "ns=3;not_valid",
                "ns=0;x=invalid_prefix",
                "ns=5;",
                "ns=1;just_text"
            ];

            foreach (string invalidNodeId in invalidNodeIds)
            {
                NUnit.Framework.Assert.Throws<ArgumentException>(() => _ = NodeId.Parse(invalidNodeId),
                    $"Expected ArgumentException for invalid NodeId: {invalidNodeId}");
                NUnit.Framework.Assert.Throws<ArgumentException>(() =>
                {
                    NodeId _ = invalidNodeId;
                },
                    $"Expected ArgumentException for invalid NodeId (implicit): {invalidNodeId}");
            }
        }

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
            Assert.AreEqual(
                actualValueRank is ValueRanks.Scalar or ValueRanks.OneDimension or ValueRanks
                    .ScalarOrOneDimension,
                ValueRanks.IsValid(actualValueRank, ValueRanks.ScalarOrOneDimension));
            Assert.AreEqual(
                actualValueRank >= 0,
                ValueRanks.IsValid(actualValueRank, ValueRanks.OneOrMoreDimensions));
            Assert.AreEqual(
                actualValueRank == ValueRanks.TwoDimensions,
                ValueRanks.IsValid(actualValueRank, ValueRanks.TwoDimensions));
            Assert.AreEqual(
                actualValueRank == ValueRanks.OneDimension,
                ValueRanks.IsValid(actualValueRank, ValueRanks.OneDimension));
            Assert.AreEqual(
                actualValueRank >= 0,
                ValueRanks.IsValid(actualValueRank, ValueRanks.OneOrMoreDimensions));
            Assert.AreEqual(
                actualValueRank == ValueRanks.Scalar,
                ValueRanks.IsValid(actualValueRank, ValueRanks.Scalar));
        }

        [Test]
        public void NodeIdTryParseValidInputs()
        {
            // Test numeric identifiers
            Assert.IsTrue(NodeId.TryParse("i=1234", out NodeId result));
            Assert.AreEqual(1234u, result.Identifier);
            Assert.AreEqual(IdType.Numeric, result.IdType);
            Assert.AreEqual(0, result.NamespaceIndex);

            Assert.IsTrue(NodeId.TryParse("ns=2;i=1234", out result));
            Assert.AreEqual(1234u, result.Identifier);
            Assert.AreEqual(IdType.Numeric, result.IdType);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test string identifiers
            Assert.IsTrue(NodeId.TryParse("s=HelloWorld", out result));
            Assert.AreEqual("HelloWorld", result.Identifier);
            Assert.AreEqual(IdType.String, result.IdType);
            Assert.AreEqual(0, result.NamespaceIndex);

            Assert.IsTrue(NodeId.TryParse("ns=2;s=HelloWorld", out result));
            Assert.AreEqual("HelloWorld", result.Identifier);
            Assert.AreEqual(IdType.String, result.IdType);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test GUID identifiers
            Assert.IsTrue(NodeId.TryParse("g=af469096-f02a-4563-940b-603958363b81", out result));
            Assert.AreEqual(new Guid("af469096-f02a-4563-940b-603958363b81"), result.Identifier);
            Assert.AreEqual(IdType.Guid, result.IdType);
            Assert.AreEqual(0, result.NamespaceIndex);

            Assert.IsTrue(NodeId.TryParse("ns=2;g=af469096-f02a-4563-940b-603958363b81", out result));
            Assert.AreEqual(new Guid("af469096-f02a-4563-940b-603958363b81"), result.Identifier);
            Assert.AreEqual(IdType.Guid, result.IdType);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test opaque identifiers (b=01020304 is valid base64 that decodes to specific bytes)
            Assert.IsTrue(NodeId.TryParse("b=01020304", out result));
            byte[] expectedBytes1 = Convert.FromBase64String("01020304");
            Assert.AreEqual(expectedBytes1, result.Identifier);
            Assert.AreEqual(IdType.Opaque, result.IdType);
            Assert.AreEqual(0, result.NamespaceIndex);

            Assert.IsTrue(NodeId.TryParse("ns=2;b=04030201", out result));
            byte[] expectedBytes2 = Convert.FromBase64String("04030201");
            Assert.AreEqual(expectedBytes2, result.Identifier);
            Assert.AreEqual(IdType.Opaque, result.IdType);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test null and empty
            Assert.IsTrue(NodeId.TryParse(null, out result));
            Assert.AreEqual(NodeId.Null, result);

            Assert.IsTrue(NodeId.TryParse(string.Empty, out result));
            Assert.AreEqual(NodeId.Null, result);
        }

        [Test]
        public void NodeIdTryParseInvalidInputs()
        {
            // Invalid formats should return false and NodeId.Null
            Assert.IsFalse(NodeId.TryParse("HelloWorld", out NodeId result));
            Assert.AreEqual(NodeId.Null, result);

            Assert.IsFalse(NodeId.TryParse("Test", out result));
            Assert.AreEqual(NodeId.Null, result);

            Assert.IsFalse(NodeId.TryParse("nsu=http://opcfoundation.org/UA/;i=1234", out result));
            Assert.AreEqual(NodeId.Null, result);

            Assert.IsFalse(NodeId.TryParse("nsu=urn:xyz;Test", out result));
            Assert.AreEqual(NodeId.Null, result);

            Assert.IsFalse(NodeId.TryParse("ns=", out result));
            Assert.AreEqual(NodeId.Null, result);

            Assert.IsFalse(NodeId.TryParse("nsu=", out result));
            Assert.AreEqual(NodeId.Null, result);

            // Invalid identifier values
            Assert.IsFalse(NodeId.TryParse("i=notanumber", out result));
            Assert.AreEqual(NodeId.Null, result);

            Assert.IsFalse(NodeId.TryParse("g=not-a-valid-guid", out result));
            Assert.AreEqual(NodeId.Null, result);

            Assert.IsFalse(NodeId.TryParse("b=notbase64!@#", out result));
            Assert.AreEqual(NodeId.Null, result);
        }

        [Test]
        public void NodeIdTryParseWithContext()
        {
            var context = new ServiceMessageContext(Telemetry)
            {
                NamespaceUris = new NamespaceTable()
            };
            context.NamespaceUris.Append("http://opcfoundation.org/UA/");
            context.NamespaceUris.Append("http://test.org/");

            // Test with namespace URI
            Assert.IsTrue(NodeId.TryParse(context, "nsu=http://test.org/;i=1234", out NodeId result));
            Assert.AreEqual(1234u, result.Identifier);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test with namespace index
            Assert.IsTrue(NodeId.TryParse(context, "ns=2;s=Test", out result));
            Assert.AreEqual("Test", result.Identifier);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test with unknown namespace URI (should fail)
            Assert.IsFalse(NodeId.TryParse(context, "nsu=http://unknown.org/;i=1234", out result));
            Assert.AreEqual(NodeId.Null, result);

            // Test null/empty
            Assert.IsTrue(NodeId.TryParse(context, null, out result));
            Assert.AreEqual(NodeId.Null, result);

            Assert.IsTrue(NodeId.TryParse(context, string.Empty, out result));
            Assert.AreEqual(NodeId.Null, result);
        }

        [Test]
        public void ExpandedNodeIdTryParseValidInputs()
        {
            // Test numeric identifiers
            Assert.IsTrue(ExpandedNodeId.TryParse("i=1234", out ExpandedNodeId result));
            Assert.AreEqual(1234u, result.Identifier);
            Assert.AreEqual(IdType.Numeric, result.IdType);
            Assert.AreEqual(0, result.NamespaceIndex);

            Assert.IsTrue(ExpandedNodeId.TryParse("ns=2;i=1234", out result));
            Assert.AreEqual(1234u, result.Identifier);
            Assert.AreEqual(IdType.Numeric, result.IdType);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test string identifiers
            Assert.IsTrue(ExpandedNodeId.TryParse("s=HelloWorld", out result));
            Assert.AreEqual("HelloWorld", result.Identifier);
            Assert.AreEqual(IdType.String, result.IdType);
            Assert.AreEqual(0, result.NamespaceIndex);

            Assert.IsTrue(ExpandedNodeId.TryParse("ns=2;s=HelloWorld", out result));
            Assert.AreEqual("HelloWorld", result.Identifier);
            Assert.AreEqual(IdType.String, result.IdType);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test with namespace URI
            Assert.IsTrue(ExpandedNodeId.TryParse("nsu=http://opcfoundation.org/UA/;s=Test", out result));
            Assert.AreEqual("Test", result.Identifier);
            Assert.AreEqual(IdType.String, result.IdType);
            Assert.AreEqual("http://opcfoundation.org/UA/", result.NamespaceUri);

            // Test with server index
            Assert.IsTrue(ExpandedNodeId.TryParse("svr=1;i=1234", out result));
            Assert.AreEqual(1234u, result.Identifier);
            Assert.AreEqual(1u, result.ServerIndex);

            // Test with both server index and namespace URI
            Assert.IsTrue(ExpandedNodeId.TryParse("svr=1;nsu=http://test.org/;s=Test", out result));
            Assert.AreEqual("Test", result.Identifier);
            Assert.AreEqual(1u, result.ServerIndex);
            Assert.AreEqual("http://test.org/", result.NamespaceUri);

            // Test GUID identifiers
            Assert.IsTrue(ExpandedNodeId.TryParse("g=af469096-f02a-4563-940b-603958363b81", out result));
            Assert.AreEqual(new Guid("af469096-f02a-4563-940b-603958363b81"), result.Identifier);
            Assert.AreEqual(IdType.Guid, result.IdType);

            // Test opaque identifiers (b=01020304 is valid base64 that decodes to specific bytes)
            Assert.IsTrue(ExpandedNodeId.TryParse("b=01020304", out result));
            byte[] expectedOpaqueBytes = Convert.FromBase64String("01020304");
            Assert.AreEqual(expectedOpaqueBytes, result.Identifier);
            Assert.AreEqual(IdType.Opaque, result.IdType);

            // Test null and empty
            Assert.IsTrue(ExpandedNodeId.TryParse(null, out result));
            Assert.AreEqual(ExpandedNodeId.Null, result);

            Assert.IsTrue(ExpandedNodeId.TryParse(string.Empty, out result));
            Assert.AreEqual(ExpandedNodeId.Null, result);
        }

        [Test]
        public void ExpandedNodeIdTryParseInvalidInputs()
        {
            // Invalid formats should return false and ExpandedNodeId.Null
            Assert.IsFalse(ExpandedNodeId.TryParse("invalid", out ExpandedNodeId result));
            Assert.AreEqual(ExpandedNodeId.Null, result);

            Assert.IsFalse(ExpandedNodeId.TryParse("ns=", out result));
            Assert.AreEqual(ExpandedNodeId.Null, result);

            Assert.IsFalse(ExpandedNodeId.TryParse("nsu=", out result));
            Assert.AreEqual(ExpandedNodeId.Null, result);

            Assert.IsFalse(ExpandedNodeId.TryParse("svr=", out result));
            Assert.AreEqual(ExpandedNodeId.Null, result);

            // Invalid identifier values
            Assert.IsFalse(ExpandedNodeId.TryParse("i=notanumber", out result));
            Assert.AreEqual(ExpandedNodeId.Null, result);

            Assert.IsFalse(ExpandedNodeId.TryParse("g=not-a-valid-guid", out result));
            Assert.AreEqual(ExpandedNodeId.Null, result);

            Assert.IsFalse(ExpandedNodeId.TryParse("b=notbase64!@#", out result));
            Assert.AreEqual(ExpandedNodeId.Null, result);
        }

        [Test]
        public void ExpandedNodeIdTryParseWithContext()
        {
            var context = new ServiceMessageContext(Telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            context.NamespaceUris.Append("http://opcfoundation.org/UA/");
            context.NamespaceUris.Append("http://test.org/");
            context.ServerUris.Append("urn:server1");

            // Test with namespace URI
            Assert.IsTrue(ExpandedNodeId.TryParse(context, "nsu=http://test.org/;i=1234", out ExpandedNodeId result));
            Assert.AreEqual(1234u, result.Identifier);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test with namespace index
            Assert.IsTrue(ExpandedNodeId.TryParse(context, "ns=2;s=Test", out result));
            Assert.AreEqual("Test", result.Identifier);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test with server URI - ServerUris table starts at index 0
            Assert.IsTrue(ExpandedNodeId.TryParse(context, "svu=urn:server1;i=1234", out result));
            Assert.AreEqual(1234u, result.Identifier);
            Assert.AreEqual(0u, result.ServerIndex);  // First item in ServerUris is at index 0

            // Test with unknown namespace URI - ExpandedNodeId can store URIs not in the table
            // So this should succeed and create an ExpandedNodeId with the namespace URI
            Assert.IsTrue(ExpandedNodeId.TryParse(context, "nsu=http://unknown.org/;i=1234", out result));
            Assert.AreEqual(1234u, result.Identifier);
            Assert.AreEqual("http://unknown.org/", result.NamespaceUri);

            // Test with unknown server URI (should fail because ServerIndex must be resolved)
            Assert.IsFalse(ExpandedNodeId.TryParse(context, "svu=urn:unknown;i=1234", out result));
            Assert.AreEqual(ExpandedNodeId.Null, result);

            // Test null/empty
            Assert.IsTrue(ExpandedNodeId.TryParse(context, null, out result));
            Assert.AreEqual(ExpandedNodeId.Null, result);

            Assert.IsTrue(ExpandedNodeId.TryParse(context, string.Empty, out result));
            Assert.AreEqual(ExpandedNodeId.Null, result);
        }
    }
}
