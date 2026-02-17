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
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeIdTests
    {
        [Test]
        public void NodeIdConstructor()
        {
            var id1 = Guid.NewGuid();
            var nodeId1 = new NodeId(id1);
            // implicit conversion;
            NodeId inodeId1 = id1;
            Assert.AreEqual(nodeId1, inodeId1);

            ByteString id2 = ByteString.From([65, 66, 67, 68, 69]);
            var nodeId2 = new NodeId(id2);
            // implicit conversion;
            var inodeId2 = (NodeId)id2;
            Assert.AreEqual(nodeId2, inodeId2);

            Assert.False(nodeId2 < inodeId2);
            Assert.True(nodeId2 == inodeId2);
            Assert.False(nodeId2 > inodeId2);

            const string text = "i=123";
            var nodeIdText = NodeId.Parse(text);
            Assert.True(nodeIdText.TryGetIdentifier(out uint t1));
            Assert.AreEqual(123, t1);
            var inodeIdText = NodeId.Parse(text);
            Assert.AreEqual(nodeIdText, inodeIdText);

            Assert.False(nodeIdText < inodeIdText);
            Assert.True(nodeIdText == inodeIdText);
            Assert.False(nodeIdText > inodeIdText);

            _ = nodeIdText < nodeId2;
            _ = nodeIdText == nodeId2;
            _ = nodeIdText > nodeId2;

            var id = new NodeId(123, 123);
            Assert.AreEqual(123, id.NamespaceIndex);
            Assert.True(id.TryGetIdentifier(out uint n2));
            Assert.AreEqual(123, n2);
            id = new NodeId("Test", 123);
            Assert.AreEqual(123, id.NamespaceIndex);
            Assert.True(id.TryGetIdentifier(out string s3));
            Assert.AreEqual("Test", s3);
            id = new NodeId(id2, 123);
            Assert.AreEqual(123, id.NamespaceIndex);
            Assert.True(id.TryGetIdentifier(out ByteString o1));
            Assert.AreEqual(id2, o1);
            id = new NodeId(null, 0);
            Assert.True(id.IsNull);
            id = new NodeId(string.Empty, 0);
            Assert.True(id.IsNull);
            id = new NodeId(id1, 123);
            Assert.AreEqual(123, id.NamespaceIndex);
            Assert.True(id.TryGetIdentifier(out Guid g4));
            Assert.AreEqual(id1, g4);
            var guid = Guid.NewGuid();
            id = new NodeId(guid, 123);
            Assert.AreEqual(123, id.NamespaceIndex);
            Assert.True(id.TryGetIdentifier(out Guid g5));
            Assert.AreEqual(guid, g5);

            ServiceResultException sre = NUnit.Framework.Assert.Throws<ServiceResultException>(() =>
                _ = NodeId.Create(123, "urn:xyz", new NamespaceTable()));
            Assert.AreEqual(StatusCodes.BadNodeIdInvalid, sre.StatusCode);

            var opaqueId = (NodeId)ByteString.From([33, 44, 55, 66]);
            var stringId1 = NodeId.Parse("ns=1;s=Test");
            var stringId2 = NodeId.Parse("ns=1;s=Test");
            Assert.AreEqual(stringId1, stringId2);
            NUnit.Framework.Assert.Throws<ArgumentException>(() => NodeId.Parse("Test"));
            NUnit.Framework.Assert.Throws<ArgumentException>(() => NodeId.Parse("nsu=urn:xyz;Test"));
            var expandedId1 = ExpandedNodeId.Parse("nsu=urn:xyz;Test");
            Assert.NotNull(expandedId1);
            var nullId = ExpandedNodeId.ToNodeId(default, new NamespaceTable());
            Assert.IsTrue(nullId.IsNull);

            // create a nodeId from a guid
            var guid1 = Guid.NewGuid();
            var guid2 = Guid.NewGuid();
            var nodeGuid1 = new NodeId(id1);

            // now to compare the nodeId to the guids
            Assert.True(nodeGuid1.Equals(id1));
            Assert.True(nodeGuid1 == id1);
            Assert.True(nodeGuid1 == (NodeId)id1);
            Assert.True(nodeGuid1.Equals(id1));
            Assert.True(nodeGuid1 == id1);
            Assert.False(nodeGuid1.Equals(id2));
            Assert.False(nodeGuid1 == id2);

            NUnit.Framework.Assert.Throws<ServiceResultException>(
                () => _ = NodeId.Create(123, "urn:xyz", null));
            NUnit.Framework.Assert.Throws<ServiceResultException>(() => _ = NodeId.Parse("ns="));
            NUnit.Framework.Assert.Throws<ArgumentException>(() => _ = NodeId.Parse("nsu="));
            NUnit.Framework.Assert.Throws<ArgumentException>(() => _ = NodeId.Parse("Test"));
            NUnit.Framework.Assert.Throws<ArgumentException>(() =>
                _ = NodeId.Parse("nsu=http://opcfoundation.org/Tests;s=Test"));
            Assert.IsTrue(NodeId.ToExpandedNodeId(default, null).IsNull);

            // IsNull
            Assert.True(new ExpandedNodeId(NodeId.Null).IsNull);
            Assert.True(new ExpandedNodeId(Guid.Empty).IsNull);
            Assert.True(ExpandedNodeId.Null.IsNull);
            Assert.True(new ExpandedNodeId(ByteString.Empty).IsNull);
            Assert.True(new ExpandedNodeId(string.Empty, 0).IsNull);
            Assert.True(new ExpandedNodeId(0).IsNull);
            Assert.False(new ExpandedNodeId(1).IsNull);

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
                NodeId.Null,
                new(0),
                new(Guid.Empty),
                new(string.Empty, 0),
                new(ByteString.Empty)
            };

            foreach (NodeId nodeId in nodeIds)
            {
                Assert.IsTrue(nodeId.IsNull);
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
            Assert.AreEqual(nodeIds[0].GetHashCode(), nodeIds[1].GetHashCode());

            nodeIds.Insert(0, new NodeId("Test", 0));
            distinctNodes++;
            Assert.AreNotEqual(nodeIds[0].GetHashCode(), nodeIds[2].GetHashCode());

            TestContext.Out.WriteLine("NodeIds:");
            foreach (NodeId nodeId in nodeIds)
            {
                TestContext.Out.WriteLine($"NodeId={nodeId}, HashCode={nodeId.GetHashCode():x8}");
            }

            TestContext.Out.WriteLine("Distinct NodeIds:");
            List<NodeId> distinctNodeIds = [.. nodeIds.Distinct()];
            foreach (NodeId nodeId in distinctNodeIds)
            {
                TestContext.Out.WriteLine($"NodeId={nodeId}, HashCode={nodeId.GetHashCode():x8}");
            }
            // all null node ids should be equal and removed
            Assert.AreEqual(distinctNodes, distinctNodeIds.Count);
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
                    nodeId = NodeId.Parse(string.Empty);
                    break;
                case IdType.Guid:
                    nodeId = new NodeId(Guid.Empty);
                    break;
                case IdType.Opaque:
                    nodeId = new NodeId(ByteString.Empty);
                    break;
                case (IdType)100:
                    nodeId = new NodeId((ByteString)null);
                    break;
                default:
                    nodeId = NodeId.Null;
                    break;
            }

            Assert.IsTrue(nodeId.IsNull);

            Assert.AreEqual(nodeId, NodeId.Null);
            Assert.AreEqual(nodeId, new NodeId(0, 0));
            Assert.AreEqual(nodeId, new NodeId(Guid.Empty));
            Assert.AreEqual(nodeId, new NodeId(ByteString.Empty));
            Assert.AreEqual(nodeId, new NodeId((ByteString)null));
            Assert.AreEqual(nodeId, NodeId.Parse(null));

            Assert.True(nodeId.Equals(NodeId.Null));
            Assert.True(nodeId.Equals(new NodeId(0, 0)));
            Assert.True(nodeId.Equals(new NodeId(Guid.Empty)));
            Assert.True(nodeId.Equals(new NodeId(ByteString.Empty)));
            Assert.True(nodeId.Equals(new NodeId((ByteString)null)));
            Assert.True(nodeId.Equals(NodeId.Parse(null)));

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
            }
        }

        [Test]
        public void NodeIdTryParseValidInputs()
        {
            // Test numeric identifiers
            Assert.IsTrue(NodeId.TryParse("i=1234", out NodeId result));
            Assert.AreEqual(1234u, result.TryGetIdentifier(out uint n1) ? n1 : 0);
            Assert.AreEqual(IdType.Numeric, result.IdType);
            Assert.AreEqual(0, result.NamespaceIndex);

            Assert.IsTrue(NodeId.TryParse("ns=2;i=1234", out result));
            Assert.AreEqual(1234u, result.TryGetIdentifier(out uint n2) ? n2 : 0);
            Assert.AreEqual(IdType.Numeric, result.IdType);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test string identifiers
            Assert.IsTrue(NodeId.TryParse("s=HelloWorld", out result));
            Assert.AreEqual("HelloWorld", result.TryGetIdentifier(out string s1) ? s1 : null);
            Assert.AreEqual(IdType.String, result.IdType);
            Assert.AreEqual(0, result.NamespaceIndex);

            Assert.IsTrue(NodeId.TryParse("ns=2;s=HelloWorld", out result));
            Assert.AreEqual("HelloWorld", result.TryGetIdentifier(out string s2) ? s2 : null);
            Assert.AreEqual(IdType.String, result.IdType);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test GUID identifiers
            Assert.IsTrue(NodeId.TryParse("g=af469096-f02a-4563-940b-603958363b81", out result));
            Assert.AreEqual(new Guid("af469096-f02a-4563-940b-603958363b81"), result.TryGetIdentifier(out Guid g1) ? g1 : Guid.Empty);
            Assert.AreEqual(IdType.Guid, result.IdType);
            Assert.AreEqual(0, result.NamespaceIndex);

            Assert.IsTrue(NodeId.TryParse("ns=2;g=af469096-f02a-4563-940b-603958363b81", out result));
            Assert.AreEqual(new Guid("af469096-f02a-4563-940b-603958363b81"), result.TryGetIdentifier(out Guid g2) ? g2 : Guid.Empty);
            Assert.AreEqual(IdType.Guid, result.IdType);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test opaque identifiers (b=01020304 is valid base64 that decodes to specific bytes)
            Assert.IsTrue(NodeId.TryParse("b=01020304", out result));
            ByteString expectedBytes1 = ByteString.FromBase64("01020304");
            Assert.AreEqual(expectedBytes1, result.TryGetIdentifier(out ByteString b1) ? b1 : default);
            Assert.AreEqual(IdType.Opaque, result.IdType);
            Assert.AreEqual(0, result.NamespaceIndex);

            Assert.IsTrue(NodeId.TryParse("ns=2;b=04030201", out result));
            byte[] expectedBytes2 = Convert.FromBase64String("04030201");
            Assert.AreEqual(expectedBytes2, result.TryGetIdentifier(out ByteString b2) ? b2 : default);
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
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable()
            };
            context.NamespaceUris.Append("http://opcfoundation.org/UA/");
            context.NamespaceUris.Append("http://test.org/");

            // Test with namespace URI
            Assert.IsTrue(NodeId.TryParse(context, "nsu=http://test.org/;i=1234", out NodeId result));
            Assert.AreEqual(1234u, result.TryGetIdentifier(out uint n1) ? n1 : 0);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test with namespace index
            Assert.IsTrue(NodeId.TryParse(context, "ns=2;s=Test", out result));
            Assert.AreEqual("Test", result.TryGetIdentifier(out string s1) ? s1 : null);
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
    }
}
