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
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Additional coverage tests for <see cref="NodeId"/> to close gaps
    /// identified by cobertura analysis.
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
            var inodeId1 = (NodeId)id1;
            Assert.That(inodeId1, Is.EqualTo(nodeId1));

            ByteString id2 = [65, 66, 67, 68, 69];
            var nodeId2 = new NodeId(id2);
            // implicit conversion;
            var inodeId2 = (NodeId)id2;
            Assert.That(inodeId2, Is.EqualTo(nodeId2));

#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(nodeId2 < inodeId2, Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(nodeId2, Is.EqualTo(inodeId2));
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(nodeId2 > inodeId2, Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure

            const string text = "i=123";
            var nodeIdText = NodeId.Parse(text);
            Assert.That(nodeIdText.TryGetIdentifier(out uint t1), Is.True);
            Assert.That(t1, Is.EqualTo(123));
            var inodeIdText = NodeId.Parse(text);
            Assert.That(inodeIdText, Is.EqualTo(nodeIdText));

#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(nodeIdText < inodeIdText, Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(nodeIdText, Is.EqualTo(inodeIdText));
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(nodeIdText > inodeIdText, Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure

            _ = nodeIdText < nodeId2;
            _ = nodeIdText == nodeId2;
            _ = nodeIdText > nodeId2;

            var id = new NodeId(123, 123);
            Assert.That(id.NamespaceIndex, Is.EqualTo(123));
            Assert.That(id.TryGetIdentifier(out uint n2), Is.True);
            Assert.That(n2, Is.EqualTo(123));
            id = new NodeId("Test", 123);
            Assert.That(id.NamespaceIndex, Is.EqualTo(123));
            Assert.That(id.TryGetIdentifier(out string s3), Is.True);
            Assert.That(s3, Is.EqualTo("Test"));
            id = new NodeId(id2, 123);
            Assert.That(id.NamespaceIndex, Is.EqualTo(123));
            Assert.That(id.TryGetIdentifier(out ByteString o1), Is.True);
            Assert.That(o1, Is.EqualTo(id2));
            id = new NodeId(null, 0);
            Assert.That(id.IsNull, Is.True);
            id = new NodeId(string.Empty, 0);
            Assert.That(id.IsNull, Is.True);
            id = new NodeId(id1, 123);
            Assert.That(id.NamespaceIndex, Is.EqualTo(123));
            Assert.That(id.TryGetIdentifier(out Guid g4), Is.True);
            Assert.That(g4, Is.EqualTo(id1));
            var guid = Guid.NewGuid();
            id = new NodeId(guid, 123);
            Assert.That(id.NamespaceIndex, Is.EqualTo(123));
            Assert.That(id.TryGetIdentifier(out Guid g5), Is.True);
            Assert.That(g5, Is.EqualTo(guid));

            ServiceResultException sre = Assert.Throws<ServiceResultException>(() =>
                _ = NodeId.Create(123, "urn:xyz", new NamespaceTable()));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            var opaqueId = (NodeId)ByteString.From([33, 44, 55, 66]);
            var stringId1 = NodeId.Parse("ns=1;s=Test");
            var stringId2 = NodeId.Parse("ns=1;s=Test");
            Assert.That(stringId1, Is.EqualTo(stringId2));
            Assert.Throws<ArgumentException>(() => NodeId.Parse("Test"));
            Assert.Throws<ArgumentException>(() => NodeId.Parse("nsu=urn:xyz;Test"));
            var expandedId1 = ExpandedNodeId.Parse("nsu=urn:xyz;Test");
            Assert.That(expandedId1.IsNull, Is.False);
            var nullId = ExpandedNodeId.ToNodeId(default, new NamespaceTable());
            Assert.That(nullId.IsNull, Is.True);

            // create a nodeId from a guid
            var guid1 = Guid.NewGuid();
            var guid2 = Guid.NewGuid();
            var nodeGuid1 = new NodeId(id1);

            // now to compare the nodeId to the guids
            Assert.That(nodeGuid1, Is.EqualTo(id1));
            Assert.That(nodeGuid1, Is.EqualTo(id1));
            Assert.That(nodeGuid1, Is.EqualTo((NodeId)id1));
            Assert.That(nodeGuid1, Is.EqualTo(id1));
            Assert.That(nodeGuid1, Is.EqualTo(id1));
            Assert.That(nodeGuid1, Is.Not.EqualTo(id2));
            Assert.That(nodeGuid1, Is.Not.EqualTo(id2));

            Assert.Throws<ServiceResultException>(
                () => _ = NodeId.Create(123, "urn:xyz", null));
            Assert.Throws<ServiceResultException>(() => _ = NodeId.Parse("ns="));
            Assert.Throws<ArgumentException>(() => _ = NodeId.Parse("nsu="));
            Assert.Throws<ArgumentException>(() => _ = NodeId.Parse("Test"));
            Assert.Throws<ArgumentException>(() =>
                _ = NodeId.Parse("nsu=http://opcfoundation.org/Tests;s=Test"));
            Assert.That(NodeId.ToExpandedNodeId(default, null).IsNull, Is.True);

            // IsNull
            Assert.That(new ExpandedNodeId(NodeId.Null).IsNull, Is.True);
            Assert.That(new ExpandedNodeId(Guid.Empty).IsNull, Is.True);
            Assert.That(ExpandedNodeId.Null.IsNull, Is.True);
            Assert.That(new ExpandedNodeId(ByteString.Empty).IsNull, Is.True);
            Assert.That(new ExpandedNodeId(string.Empty, 0).IsNull, Is.True);
            Assert.That(new ExpandedNodeId(0).IsNull, Is.True);
            Assert.That(new ExpandedNodeId(1).IsNull, Is.False);

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
                Assert.That(nodeId.ToString(), Is.EqualTo(testString));
            }
        }

        [Test]
        public void NodeIdHashCode()
        {
            // ensure that the hash code is the same for the same node id
            var nodeIds = new List<NodeId>
            {
                // Null NodeIds
                (NodeId)0,
                NodeId.Null,
                new(0),
                new(Guid.Empty),
                new(string.Empty, 0),
                new(ByteString.Empty)
            };

            foreach (NodeId nodeId in nodeIds)
            {
                Assert.That(nodeId.IsNull, Is.True);
            }

            // validate the hash code of null node id compares as equal
            foreach (NodeId nodeId in nodeIds)
            {
                foreach (NodeId nodeIdExpected in nodeIds)
                {
                    Assert.That(
                        nodeId.GetHashCode(),
                        Is.EqualTo(nodeIdExpected.GetHashCode()),
                        $"Expected{nodeIdExpected}!=NodeId={nodeId}");
                }
            }

            int distinctNodes = 1;
            nodeIds.Insert(0, new NodeId(123));
            nodeIds.Insert(0, new NodeId(123, 0));
            distinctNodes++;
            Assert.That(nodeIds[0].GetHashCode(), Is.EqualTo(nodeIds[1].GetHashCode()));
            Assert.That(nodeIds[0], Is.EqualTo(nodeIds[1]));

            nodeIds.Insert(0, new NodeId(123, 1));
            distinctNodes++;
            Assert.That(nodeIds[0].GetHashCode(), Is.EqualTo(nodeIds[1].GetHashCode()));

            nodeIds.Insert(0, new NodeId("Test", 0));
            distinctNodes++;
            Assert.That(nodeIds[0].GetHashCode(), Is.Not.EqualTo(nodeIds[2].GetHashCode()));
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
            Assert.That(distinctNodeIds, Has.Count.EqualTo(distinctNodes));
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

            Assert.That(nodeId.IsNull, Is.True);

            Assert.That(nodeId, Is.EqualTo(NodeId.Null));
            Assert.That(nodeId, Is.EqualTo(new NodeId(0, 0)));
            Assert.That(nodeId, Is.EqualTo(new NodeId(Guid.Empty)));
            Assert.That(nodeId, Is.EqualTo(new NodeId(ByteString.Empty)));
            Assert.That(nodeId, Is.EqualTo(new NodeId((ByteString)null)));
            Assert.That(nodeId, Is.EqualTo(NodeId.Parse(null)));

            Assert.That(nodeId, Is.EqualTo(NodeId.Null));
            Assert.That(nodeId, Is.EqualTo(new NodeId(0, 0)));
            Assert.That(nodeId, Is.EqualTo(new NodeId(Guid.Empty)));
            Assert.That(nodeId, Is.EqualTo(new NodeId(ByteString.Empty)));
            Assert.That(nodeId, Is.EqualTo(new NodeId((ByteString)null)));
            Assert.That(nodeId, Is.EqualTo(NodeId.Parse(null)));

            var nodeIdBasedDataValue = new DataValue(new Variant(nodeId));

            var dataValue = new DataValue(Attributes.NodeClass)
            {
                WrappedValue = new Variant((int)Attributes.NodeClass), // without this cast the second and third asserts evaluate correctly.
                StatusCode = nodeIdBasedDataValue.StatusCode
            };

            bool comparisonResult1b = dataValue.Equals(nodeIdBasedDataValue);
            Assert.That(comparisonResult1b, Is.False); // assert succeeds

            bool comparisonResult1a = nodeIdBasedDataValue.Equals(dataValue);
            Assert.That(comparisonResult1a, Is.False); // assert fails (symmetry for Equals is broken)

            bool comparisonResult1c = EqualityComparer<DataValue>.Default
                .Equals(nodeIdBasedDataValue, dataValue);
            Assert.That(comparisonResult1c, Is.False); // assert fails
            int comparisonResult2 = nodeId.CompareTo(dataValue);
            Assert.That(comparisonResult2, Is.Not.Zero); // assert fails - this is the root cause for the previous assertion failures

            Assert.That(nodeIdBasedDataValue.WrappedValue.GetNodeId(), Is.EqualTo(nodeId));
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
                Assert.Throws<ArgumentException>(() => _ = NodeId.Parse(invalidNodeId),
                    $"Expected ArgumentException for invalid NodeId: {invalidNodeId}");
            }
        }

        [Test]
        public void NodeIdTryParseValidInputs()
        {
            // Test numeric identifiers
            Assert.That(NodeId.TryParse("i=1234", out NodeId result), Is.True);
            Assert.That(result.TryGetIdentifier(out uint n1) ? n1 : 0, Is.EqualTo(1234u));
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result.NamespaceIndex, Is.Zero);

            Assert.That(NodeId.TryParse("ns=2;i=1234", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out uint n2) ? n2 : 0, Is.EqualTo(1234u));
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));

            // Test string identifiers
            Assert.That(NodeId.TryParse("s=HelloWorld", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out string s1) ? s1 : null, Is.EqualTo("HelloWorld"));
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.NamespaceIndex, Is.Zero);

            Assert.That(NodeId.TryParse("ns=2;s=HelloWorld", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out string s2) ? s2 : null, Is.EqualTo("HelloWorld"));
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));

            // Test GUID identifiers
            Assert.That(NodeId.TryParse("g=af469096-f02a-4563-940b-603958363b81", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out Guid g1) ? g1 : Guid.Empty, Is.EqualTo(new Guid("af469096-f02a-4563-940b-603958363b81")));
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));
            Assert.That(result.NamespaceIndex, Is.Zero);

            Assert.That(NodeId.TryParse("ns=2;g=af469096-f02a-4563-940b-603958363b81", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out Guid g2) ? g2 : Guid.Empty, Is.EqualTo(new Guid("af469096-f02a-4563-940b-603958363b81")));
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));

            // Test opaque identifiers (b=01020304 is valid base64 that decodes to specific bytes)
            Assert.That(NodeId.TryParse("b=01020304", out result), Is.True);
            var expectedBytes1 = ByteString.FromBase64("01020304");
            Assert.That(result.TryGetIdentifier(out ByteString b1) ? b1 : default, Is.EqualTo(expectedBytes1));
            Assert.That(result.IdType, Is.EqualTo(IdType.Opaque));
            Assert.That(result.NamespaceIndex, Is.Zero);
            Assert.That(NodeId.TryParse("ns=2;b=04030201", out result), Is.True);
            byte[] expectedBytes2 = Convert.FromBase64String("04030201");
            Assert.That(result.TryGetIdentifier(out ByteString b2) ? b2 : default, Is.EqualTo(expectedBytes2));
            Assert.That(result.IdType, Is.EqualTo(IdType.Opaque));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));

            // Test null and empty
            Assert.That(NodeId.TryParse(null, out result), Is.True);
            Assert.That(result, Is.EqualTo(NodeId.Null));
            Assert.That(NodeId.TryParse(string.Empty, out result), Is.True);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void NodeIdTryParseInvalidInputs()
        {
            // Invalid formats should return false and NodeId.Null
            Assert.That(NodeId.TryParse("HelloWorld", out NodeId result), Is.False);
            Assert.That(result, Is.EqualTo(NodeId.Null));

            Assert.That(NodeId.TryParse("Test", out result), Is.False);
            Assert.That(result, Is.EqualTo(NodeId.Null));
            Assert.That(NodeId.TryParse("nsu=http://opcfoundation.org/UA/;i=1234", out result), Is.False);
            Assert.That(result, Is.EqualTo(NodeId.Null));

            Assert.That(NodeId.TryParse("nsu=urn:xyz;Test", out result), Is.False);
            Assert.That(result, Is.EqualTo(NodeId.Null));
            Assert.That(NodeId.TryParse("ns=", out result), Is.False);
            Assert.That(result, Is.EqualTo(NodeId.Null));

            Assert.That(NodeId.TryParse("nsu=", out result), Is.False);
            Assert.That(result, Is.EqualTo(NodeId.Null));
            // Invalid identifier values
            Assert.That(NodeId.TryParse("i=notanumber", out result), Is.False);
            Assert.That(result, Is.EqualTo(NodeId.Null));

            Assert.That(NodeId.TryParse("g=not-a-valid-guid", out result), Is.False);
            Assert.That(result, Is.EqualTo(NodeId.Null));
            Assert.That(NodeId.TryParse("b=notbase64!@#", out result), Is.False);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void NodeIdTryParseWithContext()
        {
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create(), new EncodeableFactory())
            {
                NamespaceUris = new NamespaceTable()
            };
            context.NamespaceUris.Append("http://opcfoundation.org/UA/");
            context.NamespaceUris.Append("http://test.org/");

            // Test with namespace URI
            Assert.That(NodeId.TryParse(context, "nsu=http://test.org/;i=1234", out NodeId result), Is.True);
            Assert.That(result.TryGetIdentifier(out uint n1) ? n1 : 0, Is.EqualTo(1234u));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));

            // Test with namespace index
            Assert.That(NodeId.TryParse(context, "ns=2;s=Test", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out string s1) ? s1 : null, Is.EqualTo("Test"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));

            // Test with unknown namespace URI (should fail)
            Assert.That(NodeId.TryParse(context, "nsu=http://unknown.org/;i=1234", out result), Is.False);
            Assert.That(result, Is.EqualTo(NodeId.Null));
            // Test null/empty
            Assert.That(NodeId.TryParse(context, null, out result), Is.True);
            Assert.That(result, Is.EqualTo(NodeId.Null));

            Assert.That(NodeId.TryParse(context, string.Empty, out result), Is.True);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void ParseWithContextValidNumeric()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var result = NodeId.Parse(context, "i=42");
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result, Is.EqualTo(new NodeId(42u)));
        }

        [Test]
        public void ParseWithContextValidString()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var result = NodeId.Parse(context, "ns=2;s=TestNode");
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));
        }

        [Test]
        public void ParseWithContextValidGuid()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var guid = Guid.NewGuid();
            var result = NodeId.Parse(context, $"g={guid}");
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));
            Assert.That(result.TryGetIdentifier(out Guid parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(guid));
        }

        [Test]
        public void ParseWithContextValidOpaque()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            byte[] bytes = [1, 2, 3, 4];
            string base64 = Convert.ToBase64String(bytes);
            var result = NodeId.Parse(context, $"b={base64}");
            Assert.That(result.IdType, Is.EqualTo(IdType.Opaque));
        }

        [Test]
        public void ParseWithContextNullOrEmptyReturnsNull()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var result = NodeId.Parse(context, string.Empty);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ParseWithContextInvalidThrows()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            Assert.Throws<ServiceResultException>(() =>
                NodeId.Parse(context, "x=invalid"));
        }

        [Test]
        public void ParseWithContextNamespaceUriValid()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            const string nsUri = "http://test.org/UA/";
            context.NamespaceUris.GetIndexOrAppend(nsUri);
            var result = NodeId.Parse(context, $"nsu={nsUri};i=100");
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result.NamespaceIndex, Is.Not.Zero);
        }

        [Test]
        public void ParseWithContextNamespaceUriMissingSemicolon()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            Assert.Throws<ServiceResultException>(() =>
                NodeId.Parse(context, "nsu=http://test.org"));
        }

        [Test]
        public void ParseWithContextNamespaceUriNotInTable()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            Assert.Throws<ServiceResultException>(() =>
                NodeId.Parse(context, "nsu=http://unknown.org/;i=1"));
        }

        [Test]
        public void ParseWithContextNsIndexMissingSemicolon()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            Assert.Throws<ServiceResultException>(() =>
                NodeId.Parse(context, "ns=2"));
        }

        [Test]
        public void ParseWithContextInvalidIdentifierTypeReturnsError()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "ns=0;x=invalid", out NodeId value);
            Assert.That(result, Is.False);
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void ParseWithContextStringWhitespaceIdentifier()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "s=   ", out NodeId _);
            Assert.That(result, Is.False);
        }

        [Test]
        public void ParseWithContextOpaqueInvalidBase64()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "b=!!!invalid!!!", out NodeId _);
            Assert.That(result, Is.False);
        }

        [Test]
        public void ParseWithContextGuidInvalid()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "g=not-a-guid", out NodeId _);
            Assert.That(result, Is.False);
        }

        [Test]
        public void ParseWithContextNumericInvalid()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "i=abc", out NodeId _);
            Assert.That(result, Is.False);
        }

        [Test]
        public void ParseWithContextShortIdentifier()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "a", out NodeId _);
            Assert.That(result, Is.False);
        }

        [Test]
        public void ParseWithContextUpdateTablesOption()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var options = new NodeIdParsingOptions { UpdateTables = true };
            var result = NodeId.Parse(context, "nsu=http://newuri.com/;i=5", options);
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result.NamespaceIndex, Is.Not.Zero);
        }

        [Test]
        public void ParseWithContextNamespaceMappingsOption()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var options = new NodeIdParsingOptions
            {
                NamespaceMappings = [0, 5, 10]
            };
            // ns=1 should be less than NamespaceMappings.Length (3), so mapping is applied
            bool result = NodeId.TryParse(context, "ns=1;i=42", options, out _);
            Assert.That(result, Is.True);
        }

        [Test]
        public void TryParseWithErrorOutput()
        {
            bool result = NodeId.TryParse("i=42", out NodeId value, out NodeIdParseError error);
            Assert.That(result, Is.True);
            Assert.That(error, Is.EqualTo(NodeIdParseError.None));
            Assert.That(value, Is.EqualTo(new NodeId(42u)));
        }

        [Test]
        public void TryParseWithErrorOutputInvalid()
        {
            bool result = NodeId.TryParse("x=bad", out NodeId _, out NodeIdParseError error);
            Assert.That(result, Is.False);
            Assert.That(error, Is.Not.EqualTo(NodeIdParseError.None));
        }

        [Test]
        public void TryParseWithContextAndOptions()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "i=99", null, out NodeId value);
            Assert.That(result, Is.True);
            Assert.That(value, Is.EqualTo(new NodeId(99u)));
        }

        [Test]
        public void TryParseWithContextAndOptionsAndError()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "i=77", null, out NodeId value, out NodeIdParseError error);
            Assert.That(result, Is.True);
            Assert.That(error, Is.EqualTo(NodeIdParseError.None));
            Assert.That(value, Is.EqualTo(new NodeId(77u)));
        }

        [Test]
        public void TryParseInvalidNamespaceIndex()
        {
            bool result = NodeId.TryParse("ns=abc;i=1", out NodeId _, out NodeIdParseError error);
            Assert.That(result, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.InvalidNamespaceIndex));
        }

        [Test]
        public void TryParseInvalidNumericIdentifier()
        {
            bool result = NodeId.TryParse("i=notanumber", out NodeId _, out NodeIdParseError error);
            Assert.That(result, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.InvalidIdentifier));
        }

        [Test]
        public void TryParseNamespaceUriFormatReturnsFalse()
        {
            bool result = NodeId.TryParse("nsu=http://test.org/;i=1", out NodeId _, out NodeIdParseError error);
            Assert.That(result, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.InvalidNamespaceUri));
        }

        [Test]
        public void ParseIdentifierMissingThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                NodeId.Parse("SomeRandomText"));
        }

        [Test]
        public void ParseInvalidNamespaceUriThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                NodeId.Parse("nsu=http://opcfoundation.org/UA/;i=1234"));
        }

        [Test]
        public void FormatWithContextNullReturnsEmpty()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            NodeId nullId = NodeId.Null;
            string result = nullId.Format(context);
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void FormatWithContextUseNamespaceUri()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            const string nsUri = "http://format.test.org/";
            ushort nsIndex = context.NamespaceUris.GetIndexOrAppend(nsUri);
            var nodeId = new NodeId(42u, nsIndex);
            string result = nodeId.Format(context, useNamespaceUri: true);
            Assert.That(result, Does.Contain("nsu="));
            Assert.That(result, Does.Contain("i=42"));
        }

        [Test]
        public void FormatWithContextUseNamespaceUriNotFound()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            // namespace index 999 won't be in the table, falls back to ns= format
            var nodeId = new NodeId(42u, 999);
            string result = nodeId.Format(context, useNamespaceUri: true);
            Assert.That(result, Does.Contain("ns=999"));
        }

        [Test]
        public void FormatWithContextNoNamespaceUri()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var nodeId = new NodeId(42u, 3);
            string result = nodeId.Format(context, useNamespaceUri: false);
            Assert.That(result, Does.Contain("ns=3"));
            Assert.That(result, Does.Contain("i=42"));
        }

        [Test]
        public void FormatWithContextGuid()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            string result = nodeId.Format(context);
            Assert.That(result, Does.StartWith("g="));
        }

        [Test]
        public void FormatWithContextOpaque()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var nodeId = new NodeId(new ByteString(new byte[] { 0xAA, 0xBB }));
            string result = nodeId.Format(context);
            Assert.That(result, Does.StartWith("b="));
        }

        [Test]
        public void FormatWithContextString()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var nodeId = new NodeId("TestString", 0);
            string result = nodeId.Format(context);
            Assert.That(result, Is.EqualTo("s=TestString"));
        }

        [Test]
        public void FormatStaticWithNodeIdToBuffer()
        {
            var buffer = new StringBuilder();
            var nodeId = new NodeId(42u, 2);
#pragma warning disable CA1305 // Specify IFormatProvider
            NodeId.Format(buffer, nodeId);
#pragma warning restore CA1305 // Specify IFormatProvider
            Assert.That(buffer.ToString(), Is.EqualTo("ns=2;i=42"));
        }

        [Test]
        public void FormatStaticWithProviderAndNodeId()
        {
            var buffer = new StringBuilder();
            var nodeId = new NodeId("Hello", 0);
            NodeId.Format(CultureInfo.InvariantCulture, buffer, nodeId);
            Assert.That(buffer.ToString(), Is.EqualTo("s=Hello"));
        }

        [Test]
        public void FormatStaticWithComponents()
        {
            var buffer = new StringBuilder();
#pragma warning disable CA1305 // Specify IFormatProvider
            NodeId.Format(buffer, "1234", IdType.Numeric, 0);
#pragma warning restore CA1305 // Specify IFormatProvider
            Assert.That(buffer.ToString(), Is.EqualTo("i=1234"));
        }

        [Test]
        public void FormatStaticWithProviderAndComponents()
        {
            var buffer = new StringBuilder();
            NodeId.Format(CultureInfo.InvariantCulture, buffer, "TestValue", IdType.String, 5);
            Assert.That(buffer.ToString(), Is.EqualTo("ns=5;s=TestValue"));
        }

        [Test]
        public void FormatStaticAllIdTypes()
        {
            var guid = Guid.NewGuid();
            string bytes = Convert.ToBase64String([1, 2, 3]);

            var bufferNumeric = new StringBuilder();
            NodeId.Format(CultureInfo.InvariantCulture, bufferNumeric, "42", IdType.Numeric, 0);
            Assert.That(bufferNumeric.ToString(), Does.StartWith("i="));

            var bufferString = new StringBuilder();
            NodeId.Format(CultureInfo.InvariantCulture, bufferString, "hello", IdType.String, 0);
            Assert.That(bufferString.ToString(), Does.StartWith("s="));

            var bufferGuid = new StringBuilder();
            NodeId.Format(CultureInfo.InvariantCulture, bufferGuid, guid.ToString(), IdType.Guid, 0);
            Assert.That(bufferGuid.ToString(), Does.StartWith("g="));

            var bufferOpaque = new StringBuilder();
            NodeId.Format(CultureInfo.InvariantCulture, bufferOpaque, bytes, IdType.Opaque, 0);
            Assert.That(bufferOpaque.ToString(), Does.StartWith("b="));
        }

        [Test]
        public void FormatStaticWithNamespaceIndex()
        {
            var buffer = new StringBuilder();
            NodeId.Format(CultureInfo.InvariantCulture, buffer, "test", IdType.String, 7);
            Assert.That(buffer.ToString(), Does.Contain("ns=7;"));
        }

        [Test]
        public void CreateStringWithNamespaceUri()
        {
            var namespaceTable = new NamespaceTable();
            const string nsUri = "http://create.test.org/";
            namespaceTable.GetIndexOrAppend(nsUri);
            var result = NodeId.Create("TestIdentifier", nsUri, namespaceTable);
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.NamespaceIndex, Is.Not.Zero);
        }

        [Test]
        public void CreateNumericWithNamespaceUri()
        {
            var namespaceTable = new NamespaceTable();
            const string nsUri = "http://create.test.org/";
            namespaceTable.GetIndexOrAppend(nsUri);
            var result = NodeId.Create(42u, nsUri, namespaceTable);
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
        }

        [Test]
        public void CreateByteStringWithNamespaceUri()
        {
            var namespaceTable = new NamespaceTable();
            const string nsUri = "http://create.test.org/";
            namespaceTable.GetIndexOrAppend(nsUri);
            var result = NodeId.Create(new ByteString(new byte[] { 1, 2, 3 }), nsUri, namespaceTable);
            Assert.That(result.IdType, Is.EqualTo(IdType.Opaque));
        }

        [Test]
        public void CreateGuidWithNamespaceUri()
        {
            var namespaceTable = new NamespaceTable();
            const string nsUri = "http://create.test.org/";
            namespaceTable.GetIndexOrAppend(nsUri);
            var guid = Guid.NewGuid();
            var result = NodeId.Create(guid, nsUri, namespaceTable);
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));
        }

        [Test]
        public void CreateWithUnknownNamespaceThrows()
        {
            var namespaceTable = new NamespaceTable();
            Assert.Throws<ServiceResultException>(() =>
                NodeId.Create(42u, "http://unknown.org/", namespaceTable));
        }

        [Test]
        public void CreateWithNullNamespaceTableThrows()
        {
            Assert.Throws<ServiceResultException>(() =>
                NodeId.Create(42u, "http://unknown.org/", null));
        }

#pragma warning disable CS0618 // Type or member is obsolete
        [Test]
        public void CreateObsoleteObjectOverload()
        {
            var namespaceTable = new NamespaceTable();
            const string nsUri = "http://create.test.org/";
            namespaceTable.GetIndexOrAppend(nsUri);
            var result = NodeId.Create((object)42u, nsUri, namespaceTable);
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
        }
#pragma warning restore CS0618
        [Test]
        public void WithIdentifierUint()
        {
            var nodeId = new NodeId(10u, 5);
            NodeId result = nodeId.WithIdentifier(99u);
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result.NamespaceIndex, Is.EqualTo(5));
            Assert.That(result, Is.EqualTo(new NodeId(99u, 5)));
        }

        [Test]
        public void WithIdentifierString()
        {
            var nodeId = new NodeId(10u, 5);
            NodeId result = nodeId.WithIdentifier("NewName");
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.NamespaceIndex, Is.EqualTo(5));
        }

        [Test]
        public void WithIdentifierByteString()
        {
            var nodeId = new NodeId(10u, 5);
            NodeId result = nodeId.WithIdentifier(new ByteString(new byte[] { 0xFF }));
            Assert.That(result.IdType, Is.EqualTo(IdType.Opaque));
            Assert.That(result.NamespaceIndex, Is.EqualTo(5));
        }

        [Test]
        public void WithIdentifierGuid()
        {
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(10u, 5);
            NodeId result = nodeId.WithIdentifier(guid);
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));
            Assert.That(result.NamespaceIndex, Is.EqualTo(5));
        }

        [Test]
        public void WithNamespaceIndexOnNullNodeId()
        {
            NodeId nullId = NodeId.Null;
            NodeId result = nullId.WithNamespaceIndex(5);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void WithNamespaceIndexOnStringNodeId()
        {
            var nodeId = new NodeId("Test", 1);
            NodeId result = nodeId.WithNamespaceIndex(5);
            Assert.That(result.NamespaceIndex, Is.EqualTo(5));
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
        }

        [Test]
        public void WithNamespaceIndexOnGuidNodeId()
        {
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid, 1);
            NodeId result = nodeId.WithNamespaceIndex(5);
            Assert.That(result.NamespaceIndex, Is.EqualTo(5));
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));
        }

        [Test]
        public void WithNamespaceIndexOnOpaqueNodeId()
        {
            var nodeId = new NodeId(new ByteString(new byte[] { 1, 2 }), 1);
            NodeId result = nodeId.WithNamespaceIndex(5);
            Assert.That(result.NamespaceIndex, Is.EqualTo(5));
            Assert.That(result.IdType, Is.EqualTo(IdType.Opaque));
        }

#pragma warning disable CS0618 // Type or member is obsolete
        [Test]
        public void SetNamespaceIndexObsolete()
        {
            var nodeId = new NodeId(42u, 1);
            NodeId result = nodeId.SetNamespaceIndex(9);
            Assert.That(result.NamespaceIndex, Is.EqualTo(9));
        }
#pragma warning restore CS0618
        [Test]
        public void CompareToNodeIdBothNull()
        {
            NodeId a = NodeId.Null;
            NodeId b = NodeId.Null;
            Assert.That(a.CompareTo(b), Is.Zero);
        }

        [Test]
        public void CompareToNodeIdThisNull()
        {
            NodeId a = NodeId.Null;
            var b = new NodeId(42u);
            Assert.That(a.CompareTo(b), Is.EqualTo(1));
        }

        [Test]
        public void CompareToNodeIdDifferentNamespace()
        {
            var a = new NodeId(1u, 5);
            var b = new NodeId(1u, 3);
            Assert.That(a.CompareTo(b), Is.LessThan(0));
            Assert.That(b.CompareTo(a), Is.GreaterThan(0));
        }

        [Test]
        public void CompareToNodeIdDifferentIdType()
        {
            var a = new NodeId("Test", 0);
            var b = new NodeId(Guid.NewGuid(), 0);
            int result = a.CompareTo(b);
            Assert.That(result, Is.Not.Zero);
        }

        [Test]
        public void CompareToNodeIdSameTypeString()
        {
            var a = new NodeId("Alpha", 0);
            var b = new NodeId("Beta", 0);
            Assert.That(a.CompareTo(b), Is.LessThan(0));
        }

        [Test]
        public void CompareToNodeIdSameTypeGuid()
        {
            var guid1 = new Guid("00000000-0000-0000-0000-000000000001");
            var guid2 = new Guid("00000000-0000-0000-0000-000000000002");
            var a = new NodeId(guid1, 0);
            var b = new NodeId(guid2, 0);
            Assert.That(a.CompareTo(b), Is.Not.Zero);
        }

        [Test]
        public void CompareToNodeIdSameTypeOpaque()
        {
            var a = new NodeId(new ByteString(new byte[] { 1 }), 0);
            var b = new NodeId(new ByteString(new byte[] { 2 }), 0);
            Assert.That(a.CompareTo(b), Is.Not.Zero);
        }

        [Test]
        public void CompareToExpandedNodeIdAbsolute()
        {
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(42u, "http://test.org/");
            Assert.That(nodeId.CompareTo(expanded), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToExpandedNodeIdRelative()
        {
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);
            Assert.That(nodeId.CompareTo(expanded), Is.Zero);
        }

        [Test]
        public void CompareToStringNull()
        {
            NodeId nullId = NodeId.Null;
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(nullId.CompareTo((string)null), Is.Zero);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            Assert.That(nullId.CompareTo(string.Empty), Is.Zero);
        }

        [Test]
        public void CompareToStringNullWithValue()
        {
            NodeId nullId = NodeId.Null;
            Assert.That(nullId.CompareTo("something"), Is.EqualTo(1));
        }

        [Test]
        public void CompareToStringMismatchedType()
        {
            var numericId = new NodeId(42u);
            Assert.That(numericId.CompareTo("test"), Is.EqualTo(-1));

            var nsId = new NodeId("hello", 2);
            Assert.That(nsId.CompareTo("hello"), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToStringMatch()
        {
            var nodeId = new NodeId("Alpha", 0);
            Assert.That(nodeId.CompareTo("Alpha"), Is.Zero);
            Assert.That(nodeId.CompareTo("Beta"), Is.LessThan(0));
        }

        [Test]
        public void CompareToUintNull()
        {
            NodeId nullId = NodeId.Null;
            Assert.That(nullId.CompareTo(0u), Is.Zero);
            Assert.That(nullId.CompareTo(5u), Is.EqualTo(1));
        }

        [Test]
        public void CompareToUintMismatchedType()
        {
            var stringId = new NodeId("test", 0);
            Assert.That(stringId.CompareTo(42u), Is.EqualTo(-1));

            var nsId = new NodeId(42u, 2);
            Assert.That(nsId.CompareTo(42u), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToUintMatch()
        {
            var nodeId = new NodeId(10u);
            Assert.That(nodeId.CompareTo(10u), Is.Zero);
            Assert.That(nodeId.CompareTo(20u), Is.LessThan(0));
        }

        [Test]
        public void CompareToGuidNull()
        {
            NodeId nullId = NodeId.Null;
            Assert.That(nullId.CompareTo(Guid.Empty), Is.Zero);
            Assert.That(nullId.CompareTo(Guid.NewGuid()), Is.EqualTo(1));
        }

        [Test]
        public void CompareToGuidMismatchedType()
        {
            var numericId = new NodeId(42u);
            Assert.That(numericId.CompareTo(Guid.NewGuid()), Is.EqualTo(-1));

            var guidId = new NodeId(Guid.NewGuid(), 2);
            Assert.That(guidId.CompareTo(Guid.NewGuid()), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToGuidMatch()
        {
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            Assert.That(nodeId.CompareTo(guid), Is.Zero);
        }

        [Test]
        public void CompareToByteStringNull()
        {
            NodeId nullId = NodeId.Null;
            Assert.That(nullId.CompareTo(ByteString.Empty), Is.Zero);
            Assert.That(nullId.CompareTo(new ByteString(new byte[] { 1 })), Is.EqualTo(1));
        }

        [Test]
        public void CompareToByteStringMismatchedType()
        {
            var numericId = new NodeId(42u);
            Assert.That(numericId.CompareTo(new ByteString(new byte[] { 1 })), Is.EqualTo(-1));

            var opaqueId = new NodeId(new ByteString(new byte[] { 1 }), 2);
            Assert.That(opaqueId.CompareTo(new ByteString(new byte[] { 1 })), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToByteStringMatch()
        {
            var bytes = new ByteString(new byte[] { 1, 2, 3 });
            var nodeId = new NodeId(bytes);
            Assert.That(nodeId.CompareTo(bytes), Is.Zero);
        }

        [Test]
        public void CompareToObjectNull()
        {
            NodeId nullId = NodeId.Null;
            Assert.That(nullId.CompareTo((object)null), Is.Zero);

            // non-null nodeId vs null
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.CompareTo((object)null), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToObjectInt()
        {
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.CompareTo((object)42), Is.Zero);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(nodeId.CompareTo((object)-1), Is.EqualTo(-1));
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void CompareToObjectUint()
        {
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.CompareTo((object)42u), Is.Zero);
        }

        [Test]
        public void CompareToObjectGuid()
        {
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            Assert.That(nodeId.CompareTo((object)guid), Is.Zero);
        }

        [Test]
        public void CompareToObjectUuid()
        {
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            Assert.That(nodeId.CompareTo((object)new Uuid(guid)), Is.Zero);
        }

        [Test]
        public void CompareToObjectString()
        {
            var nodeId = new NodeId("test", 0);
            Assert.That(nodeId.CompareTo((object)"test"), Is.Zero);
        }

        [Test]
        public void CompareToObjectByteString()
        {
            var bytes = new ByteString(new byte[] { 1, 2 });
            var nodeId = new NodeId(bytes);
            Assert.That(nodeId.CompareTo((object)bytes), Is.Zero);
        }

        [Test]
        public void CompareToObjectExpandedNodeId()
        {
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);
            Assert.That(nodeId.CompareTo((object)expanded), Is.Zero);
        }

        [Test]
        public void CompareToObjectNodeId()
        {
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.CompareTo((object)new NodeId(42u)), Is.Zero);
        }

        [Test]
        public void CompareToObjectSerializableNodeId()
        {
            var nodeId = new NodeId(42u);
            var serializable = new SerializableNodeId(nodeId);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(nodeId.CompareTo((object)serializable), Is.Zero);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void CompareToObjectSerializableExpandedNodeId()
        {
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);
            var serializable = new SerializableExpandedNodeId(expanded);
            Assert.That(nodeId.CompareTo((object)serializable), Is.Zero);
        }

        [Test]
        public void CompareToObjectUnknownType()
        {
            var nodeId = new NodeId(42u);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(nodeId.CompareTo((object)3.14), Is.EqualTo(-1));
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }
        [Test]
        public void OperatorGreaterThanOrEqual()
        {
            var a = new NodeId(10u);
            var b = new NodeId(10u);
            var c = new NodeId(20u);
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(a >= b, Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(c >= a, Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
        }

        [Test]
        public void OperatorLessThanOrEqual()
        {
            var a = new NodeId(10u);
            var b = new NodeId(10u);
            var c = new NodeId(20u);
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(a <= b, Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(a <= c, Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
        }
        [Test]
        public void ToStringIFormattableNullFormat()
        {
            var nodeId = new NodeId(42u, 3);
            string result = nodeId.ToString(null, CultureInfo.InvariantCulture);
            Assert.That(result, Is.EqualTo("ns=3;i=42"));
        }

        [Test]
        public void ToStringIFormattableNonNullFormatThrows()
        {
            var nodeId = new NodeId(42u);
            Assert.Throws<FormatException>(() =>
                nodeId.ToString("G", CultureInfo.InvariantCulture));
        }
        [Test]
        public void EqualsObjectNull()
        {
            NodeId nullId = NodeId.Null;
#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            Assert.That(nullId.Equals((object)null));
#pragma warning restore NUnit2010 // Use EqualConstraint for better assertion messages in case of failure

            var nodeId = new NodeId(42u);
#pragma warning disable NUnit4002 // Use Specific constraint
            Assert.That(nodeId, Is.Not.EqualTo((object)null));
#pragma warning restore NUnit4002 // Use Specific constraint
        }

        [Test]
        public void EqualsObjectInt()
        {
            var nodeId = new NodeId(42u);
            Assert.That(nodeId, Is.EqualTo((object)42));
            Assert.That(nodeId, Is.Not.EqualTo((object)-1));
        }

        [Test]
        public void EqualsObjectUint()
        {
            var nodeId = new NodeId(42u);
            Assert.That(nodeId, Is.EqualTo((object)42u));
        }

        [Test]
        public void EqualsObjectGuid()
        {
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            Assert.That(nodeId, Is.EqualTo((object)guid));
        }

        [Test]
        public void EqualsObjectByteString()
        {
            var bytes = new ByteString(new byte[] { 1, 2, 3 });
            var nodeId = new NodeId(bytes);
            Assert.That(nodeId, Is.EqualTo((object)bytes));
        }

        [Test]
        public void EqualsObjectString()
        {
            var nodeId = new NodeId("test", 0);
            Assert.That(nodeId, Is.EqualTo((object)"test"));
        }

        [Test]
        public void EqualsObjectExpandedNodeId()
        {
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);
            Assert.That(nodeId, Is.EqualTo((object)expanded));
        }

        [Test]
        public void EqualsObjectSerializableNodeId()
        {
            var nodeId = new NodeId(42u);
            var serializable = new SerializableNodeId(nodeId);
            Assert.That(nodeId, Is.EqualTo((object)serializable));
        }

        [Test]
        public void EqualsObjectSerializableExpandedNodeId()
        {
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);
            var serializable = new SerializableExpandedNodeId(expanded);
            Assert.That(nodeId, Is.EqualTo((object)serializable));
        }

        [Test]
        public void EqualsObjectUnknownType()
        {
            var nodeId = new NodeId(42u);
            Assert.That(nodeId, Is.Not.EqualTo((object)3.14));
        }

        [Test]
        public void EqualsNodeIdFallsThrough()
        {
            // after type check)
            var a = new NodeId("test", 0);
            var b = new NodeId(42u, 0);
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void EqualsExpandedNodeIdAbsolute()
        {
            var nodeId = new NodeId(42u);
            var absolute = new ExpandedNodeId(42u, "http://test.org/");
            Assert.That(nodeId, Is.Not.EqualTo(absolute));
        }

        [Test]
        public void EqualsExpandedNodeIdRelative()
        {
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);
            Assert.That(nodeId, Is.EqualTo(expanded));
        }

        [Test]
        public void EqualsUintMatch()
        {
            var nodeId = new NodeId(42u);
            Assert.That(nodeId, Is.EqualTo(42u));
            Assert.That(nodeId, Is.Not.EqualTo(99u));
        }

        [Test]
        public void EqualsUintMismatchedType()
        {
            var stringId = new NodeId("test", 0);
            Assert.That(stringId, Is.Not.EqualTo(42u));

            var nsId = new NodeId(42u, 2);
            Assert.That(nsId, Is.Not.EqualTo(42u));
        }

        [Test]
        public void EqualsGuidMatch()
        {
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            Assert.That(nodeId, Is.EqualTo(guid));
            Assert.That(nodeId, Is.Not.EqualTo(Guid.NewGuid()));
        }

        [Test]
        public void EqualsGuidMismatchedType()
        {
            var numericId = new NodeId(42u);
            Assert.That(numericId, Is.Not.EqualTo(Guid.NewGuid()));
        }

        [Test]
        public void EqualsByteStringMatch()
        {
            var bytes = new ByteString(new byte[] { 1, 2, 3 });
            var nodeId = new NodeId(bytes);
            Assert.That(nodeId, Is.EqualTo(bytes));
        }

        [Test]
        public void EqualsByteStringMismatchedType()
        {
            var numericId = new NodeId(42u);
            Assert.That(numericId, Is.Not.EqualTo(new ByteString(new byte[] { 1 })));
        }

        [Test]
        public void EqualsStringMatch()
        {
            var nodeId = new NodeId("hello", 0);
            Assert.That(nodeId, Is.EqualTo("hello"));
            Assert.That(nodeId, Is.Not.EqualTo("world"));
        }

        [Test]
        public void EqualsStringMismatchedType()
        {
            var numericId = new NodeId(42u);
            Assert.That(numericId, Is.Not.EqualTo("test"));

            var nsId = new NodeId("test", 2);
            Assert.That(nsId, Is.Not.EqualTo("test"));
        }

        [Test]
        public void EqualsStringNullOrEmpty()
        {
            var nullId = new NodeId(string.Empty, 0);
#pragma warning disable NUnit4002 // Use Specific constraint
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(nullId, Is.EqualTo((string)null));
#pragma warning restore IDE0004 // Remove Unnecessary Cast
#pragma warning restore NUnit4002 // Use Specific constraint
            Assert.That(nullId, Is.EqualTo(string.Empty));
        }
        [Test]
        public void OperatorEqualsObject()
        {
            var nodeId = new NodeId(42u);
            Assert.That(nodeId, Is.EqualTo((object)42u));
            Assert.That(nodeId, Is.Not.EqualTo((object)99u));
        }

        [Test]
        public void OperatorNotEqualsObject()
        {
            var nodeId = new NodeId(42u);
            Assert.That(nodeId, Is.Not.EqualTo((object)99u));
            Assert.That(nodeId, Is.EqualTo((object)42u));
        }

        [Test]
        public void OperatorEqualsExpandedNodeId()
        {
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);
            Assert.That(nodeId, Is.EqualTo(expanded));
        }

        [Test]
        public void OperatorNotEqualsExpandedNodeId()
        {
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(99u);
            Assert.That(nodeId, Is.Not.EqualTo(expanded));
        }

        [Test]
        public void OperatorEqualsUint()
        {
            var nodeId = new NodeId(42u);
            Assert.That(nodeId, Is.EqualTo(42u));
        }

        [Test]
        public void OperatorNotEqualsUint()
        {
            var nodeId = new NodeId(42u);
            Assert.That(nodeId, Is.Not.EqualTo(99u));
        }

        [Test]
        public void OperatorEqualsByteString()
        {
            var bytes = new ByteString(new byte[] { 1, 2 });
            var nodeId = new NodeId(bytes);
            Assert.That(nodeId, Is.EqualTo(bytes));
        }

        [Test]
        public void OperatorNotEqualsByteString()
        {
            var nodeId = new NodeId(new ByteString(new byte[] { 1 }));
            Assert.That(nodeId, Is.Not.EqualTo(new ByteString(new byte[] { 2 })));
        }

        [Test]
        public void OperatorEqualsGuid()
        {
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            Assert.That(nodeId, Is.EqualTo(guid));
        }

        [Test]
        public void OperatorNotEqualsGuid()
        {
            var nodeId = new NodeId(Guid.NewGuid());
            Assert.That(nodeId, Is.Not.EqualTo(Guid.Empty));
        }

        [Test]
        public void OperatorEqualsString()
        {
            var nodeId = new NodeId("test", 0);
            Assert.That(nodeId, Is.EqualTo("test"));
        }

        [Test]
        public void OperatorNotEqualsString()
        {
            var nodeId = new NodeId("test", 0);
            Assert.That(nodeId, Is.Not.EqualTo("other"));
        }
        [Test]
        public void TryGetIdentifierUintFalseForNonNumeric()
        {
            var nodeId = new NodeId("test", 0);
            Assert.That(nodeId.TryGetIdentifier(out uint _), Is.False);
        }

        [Test]
        public void TryGetIdentifierByteStringFalseForNonOpaque()
        {
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.TryGetIdentifier(out ByteString _), Is.False);
        }

        [Test]
        public void TryGetIdentifierByteStringTrue()
        {
            var bytes = new ByteString(new byte[] { 1, 2, 3 });
            var nodeId = new NodeId(bytes);
            Assert.That(nodeId.TryGetIdentifier(out ByteString result), Is.True);
            Assert.That(result, Is.EqualTo(bytes));
        }

        [Test]
        public void TryGetIdentifierStringFalseForNonString()
        {
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.TryGetIdentifier(out string _), Is.False);
        }

        [Test]
        public void TryGetIdentifierStringTrue()
        {
            var nodeId = new NodeId("hello", 0);
            Assert.That(nodeId.TryGetIdentifier(out string result), Is.True);
            Assert.That(result, Is.EqualTo("hello"));
        }

        [Test]
        public void TryGetIdentifierGuidFalseForNonGuid()
        {
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.TryGetIdentifier(out Guid _), Is.False);
        }

        [Test]
        public void TryGetIdentifierGuidTrue()
        {
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            Assert.That(nodeId.TryGetIdentifier(out Guid result), Is.True);
            Assert.That(result, Is.EqualTo(guid));
        }
#pragma warning disable CS0618 // Type or member is obsolete
        [Test]
        public void IdentifierPropertyString()
        {
            var nodeId = new NodeId("test", 0);
            object id = nodeId.Identifier;
            Assert.That(id, Is.TypeOf<string>());
            Assert.That(id, Is.EqualTo("test"));
        }

        [Test]
        public void IdentifierPropertyGuid()
        {
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            object id = nodeId.Identifier;
            Assert.That(id, Is.TypeOf<Guid>());
            Assert.That(id, Is.EqualTo(guid));
        }

        [Test]
        public void IdentifierPropertyOpaque()
        {
            var bytes = new ByteString(new byte[] { 1, 2, 3 });
            var nodeId = new NodeId(bytes);
            object id = nodeId.Identifier;
            Assert.That(id, Is.TypeOf<ByteString>());
            Assert.That(id, Is.EqualTo(bytes));
        }

        [Test]
        public void IdentifierPropertyNumeric()
        {
            var nodeId = new NodeId(42u);
            object id = nodeId.Identifier;
            Assert.That(id, Is.TypeOf<uint>());
            Assert.That(id, Is.EqualTo(42u));
        }
#pragma warning restore CS0618

        [Test]
        public void IdentifierAsStringOpaque()
        {
            var bytes = new ByteString(new byte[] { 0xAA, 0xBB });
            var nodeId = new NodeId(bytes);
            string str = nodeId.IdentifierAsString;
            Assert.That(str, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void IdentifierAsStringGuid()
        {
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            string str = nodeId.IdentifierAsString;
            Assert.That(str, Is.EqualTo(guid.ToString()));
        }
        [Test]
        public void IsNullWithNonZeroNamespace()
        {
            var nodeId = new NodeId(0u, 5);
            Assert.That(nodeId.IsNull, Is.False);
        }

        [Test]
        public void IsNullEmptyGuid()
        {
            var nodeId = new NodeId(Guid.Empty);
            Assert.That(nodeId.IsNull, Is.True);
        }

        [Test]
        public void IsNullNonEmptyGuid()
        {
            var nodeId = new NodeId(Guid.NewGuid());
            Assert.That(nodeId.IsNull, Is.False);
        }

        [Test]
        public void IsNullEmptyOpaque()
        {
            var nodeId = new NodeId(ByteString.Empty);
            Assert.That(nodeId.IsNull, Is.True);
        }

        [Test]
        public void IsNullNonEmptyOpaque()
        {
            var nodeId = new NodeId(new ByteString(new byte[] { 1 }));
            Assert.That(nodeId.IsNull, Is.False);
        }
        [Test]
        public void ToExpandedNodeIdNull()
        {
            var namespaceTable = new NamespaceTable();
            var result = NodeId.ToExpandedNodeId(NodeId.Null, namespaceTable);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ToExpandedNodeIdWithNamespace()
        {
            var namespaceTable = new NamespaceTable();
            const string nsUri = "http://test.org/";
            namespaceTable.GetIndexOrAppend(nsUri);
            var nodeId = new NodeId(42u, 1);
            var result = NodeId.ToExpandedNodeId(nodeId, namespaceTable);
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.NamespaceUri, Is.EqualTo(nsUri));
        }

        [Test]
        public void ToExpandedNodeIdNamespaceZero()
        {
            var namespaceTable = new NamespaceTable();
            var nodeId = new NodeId(42u, 0);
            var result = NodeId.ToExpandedNodeId(nodeId, namespaceTable);
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.NamespaceUri, Is.Null.Or.Empty);
        }
#pragma warning disable CS0618 // Type or member is obsolete
        [Test]
        public void ObsoleteStringConstructor()
        {
            var nodeId = new NodeId("i=42");
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(nodeId, Is.EqualTo(new NodeId(42u)));
        }

        [Test]
        public void ObsoleteObjectConstructorUint()
        {
            var nodeId = new NodeId((object)42u, 3);
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(3));
        }

        [Test]
        public void ObsoleteObjectConstructorNullString()
        {
            var nodeId = new NodeId((object)null, 0);
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.String));
        }

        [Test]
        public void ObsoleteObjectConstructorString()
        {
            var nodeId = new NodeId((object)"hello", 2);
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.String));
            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(2));
        }

        [Test]
        public void ObsoleteObjectConstructorGuid()
        {
            var guid = Guid.NewGuid();
            var nodeId = new NodeId((object)guid, 1);
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Guid));
            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void ObsoleteObjectConstructorByteString()
        {
            var bytes = new ByteString(new byte[] { 5, 6 });
            var nodeId = new NodeId((object)bytes, 4);
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Opaque));
            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(4));
        }

        [Test]
        public void ObsoleteObjectConstructorUnsupportedTypeThrows()
        {
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.Throws<ArgumentException>(() =>
    new NodeId((object)3.14, 0));
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }
#pragma warning restore CS0618
        [Test]
        public void SerializableNodeIdDefaultConstructor()
        {
            var serializable = new SerializableNodeId();
            Assert.That(serializable.Value.IsNull, Is.True);
        }

        [Test]
        public void SerializableNodeIdConstructorWithValue()
        {
            var nodeId = new NodeId(42u, 2);
            var serializable = new SerializableNodeId(nodeId);
            Assert.That(serializable.Value, Is.EqualTo(nodeId));
        }

        [Test]
        public void SerializableNodeIdGetValue()
        {
            var nodeId = new NodeId(42u);
            var serializable = new SerializableNodeId(nodeId);
            object result = serializable.GetValue();
            Assert.That(result, Is.TypeOf<NodeId>());
            Assert.That((NodeId)result, Is.EqualTo(nodeId));
        }
        [Test]
        public void NodeIdParsingOptionsProperties()
        {
            var options = new NodeIdParsingOptions
            {
                UpdateTables = true,
                NamespaceMappings = [0, 1, 2],
                ServerMappings = [0, 5]
            };
            Assert.That(options.UpdateTables, Is.True);
            Assert.That(options.NamespaceMappings, Has.Length.EqualTo(3));
            Assert.That(options.ServerMappings, Has.Length.EqualTo(2));
        }
        [Test]
        public void ImplicitConversionFromUint()
        {
            var nodeId = (NodeId)42u;
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(nodeId, Is.EqualTo(new NodeId(42u)));
        }

        [Test]
        public void ImplicitConversionFromGuid()
        {
            var guid = Guid.NewGuid();
            var nodeId = (NodeId)guid;
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Guid));
        }

        [Test]
        public void ExplicitConversionFromByteString()
        {
            var bytes = new ByteString(new byte[] { 1, 2 });
            var nodeId = (NodeId)bytes;
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Opaque));
        }
        [Test]
        public void RoundTripNumericWithNamespace()
        {
            var original = new NodeId(12345u, 7);
            string text = original.ToString();
            bool parsed = NodeId.TryParse(text, out NodeId result);
            Assert.That(parsed, Is.True);
            Assert.That(result, Is.EqualTo(original));
        }

        [Test]
        public void RoundTripString()
        {
            var original = new NodeId("MyNode", 3);
            string text = original.ToString();
            bool parsed = NodeId.TryParse(text, out NodeId result);
            Assert.That(parsed, Is.True);
            Assert.That(result, Is.EqualTo(original));
        }

        [Test]
        public void RoundTripGuid()
        {
            var guid = Guid.NewGuid();
            var original = new NodeId(guid, 2);
            string text = original.ToString();
            bool parsed = NodeId.TryParse(text, out NodeId result);
            Assert.That(parsed, Is.True);
            Assert.That(result, Is.EqualTo(original));
        }

        [Test]
        public void RoundTripOpaque()
        {
            var bytes = new ByteString(new byte[] { 0x01, 0x02, 0x03, 0xFF });
            var original = new NodeId(bytes, 1);
            string text = original.ToString();
            bool parsed = NodeId.TryParse(text, out NodeId result);
            Assert.That(parsed, Is.True);
            Assert.That(result, Is.EqualTo(original));
        }

        [Test]
        public void RoundTripContextFormat()
        {
            // Round trip through Format(context) and Parse(context)
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            const string nsUri = "http://roundtrip.org/";
            context.NamespaceUris.GetIndexOrAppend(nsUri);
            var original = new NodeId(42u, 1);

            string formatted = original.Format(context, useNamespaceUri: true);
            var parsed = NodeId.Parse(context, formatted);
            Assert.That(parsed, Is.EqualTo(original));
        }
        [Test]
        public void NullNodeIdToString()
        {
            NodeId nullId = NodeId.Null;
            string result = nullId.ToString();
            Assert.That(result, Is.EqualTo("i=0"));
        }

        [Test]
        public void DefaultNodeIdIsNull()
        {
            NodeId nodeId = default;
            Assert.That(nodeId.IsNull, Is.True);
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(nodeId.NamespaceIndex, Is.Zero);
        }

        [Test]
        public void ParseEmptyStringReturnsNull()
        {
            bool result = NodeId.TryParse(string.Empty, out NodeId value);
            Assert.That(result, Is.True);
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void ParseNullStringReturnsNull()
        {
            bool result = NodeId.TryParse(null, out NodeId value);
            Assert.That(result, Is.True);
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void ParseNsMissingTerminator()
        {
            // ns= without semicolon in simple parser
            bool result = NodeId.TryParse("ns=2", out NodeId _, out NodeIdParseError error);
            Assert.That(result, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.InvalidNamespaceIndex));
        }

        [Test]
        public void ParseInvalidGuidIdentifier()
        {
            bool result = NodeId.TryParse("g=not-a-valid-guid", out NodeId _, out NodeIdParseError error);
            Assert.That(result, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.InvalidIdentifier));
        }

        [Test]
        public void EqualsNodeIdBothNullDifferentIdType()
        {
            // Both are "null" but one is string-null, one is numeric-null
            var numericNull = new NodeId(0u, 0);
            var stringNull = new NodeId(string.Empty, 0);
            // Both IsNull == true, so they should be equal
            Assert.That(numericNull, Is.EqualTo(stringNull));
        }

        [Test]
        public void CompareToNodeIdSameNumericValues()
        {
            var a = new NodeId(42u, 0);
            var b = new NodeId(42u, 0);
            Assert.That(a.CompareTo(b), Is.Zero);
        }

        private const string ParseLongFormKnownNamespace= "http://opcfoundation.org/UA/Test/";
        private const string ParseLongFormUnknownNamespace = "http://opcfoundation.org/UA/Unknown/";

        private static NamespaceTable BuildParseLongFormNamespaces()
        {
            var table = new NamespaceTable();
            table.Append(ParseLongFormKnownNamespace);
            return table;
        }

        private static uint GetParseLongFormUInt(NodeId id)
        {
            Assert.That(id.TryGetIdentifier(out uint v), Is.True);
            return v;
        }

        private static string GetParseLongFormString(NodeId id)
        {
            Assert.That(id.TryGetIdentifier(out string v), Is.True);
            return v;
        }

        private static Guid GetParseLongFormGuid(NodeId id)
        {
            Assert.That(id.TryGetIdentifier(out Guid v), Is.True);
            return v;
        }

        private static ByteString GetParseLongFormBytes(NodeId id)
        {
            Assert.That(id.TryGetIdentifier(out ByteString v), Is.True);
            return v;
        }

        [Test]
        public void ParseLongFormThrowsWhenTableIsNull()
        {
            Assert.That(
                () => NodeId.ParseLongForm("i=42", null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ParseLongFormReturnsNullForNullText()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            NodeId result = NodeId.ParseLongForm(null, table);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void ParseLongFormReturnsNullForEmptyText()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            NodeId result = NodeId.ParseLongForm(string.Empty, table);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void ParseLongFormBareNumericIdentifier()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            NodeId result = NodeId.ParseLongForm("i=42", table);
            Assert.That(result.NamespaceIndex, Is.EqualTo(0));
            Assert.That(GetParseLongFormUInt(result), Is.EqualTo((uint)42));
        }

        [Test]
        public void ParseLongFormResolvesKnownNamespaceUriNumeric()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            NodeId result = NodeId.ParseLongForm($"nsu={ParseLongFormKnownNamespace};i=99", table);
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
            Assert.That(GetParseLongFormUInt(result), Is.EqualTo((uint)99));
        }

        [Test]
        public void ParseLongFormResolvesKnownNamespaceUriString()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            NodeId result = NodeId.ParseLongForm($"nsu={ParseLongFormKnownNamespace};s=Tag1", table);
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
            Assert.That(GetParseLongFormString(result), Is.EqualTo("Tag1"));
        }

        [Test]
        public void ParseLongFormResolvesKnownNamespaceUriGuid()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            var guid = new Guid("12345678-1234-1234-1234-1234567890AB");
            NodeId result = NodeId.ParseLongForm($"nsu={ParseLongFormKnownNamespace};g={guid}", table);
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
            Assert.That(GetParseLongFormGuid(result), Is.EqualTo(guid));
        }

        [Test]
        public void ParseLongFormResolvesKnownNamespaceUriOpaque()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            byte[] bytes = [1, 2, 3, 4];
            string base64 = Convert.ToBase64String(bytes);
            NodeId result = NodeId.ParseLongForm(
                $"nsu={ParseLongFormKnownNamespace};b={base64}", table);
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
            Assert.That(GetParseLongFormBytes(result), Is.EqualTo((ByteString)bytes));
        }

        [Test]
        public void ParseLongFormThrowsForUnresolvedNamespaceUri()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            Assert.That(
                () => NodeId.ParseLongForm($"nsu={ParseLongFormUnknownNamespace};i=1", table),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void ParseLongFormThrowsForMalformedTypedIdentifier()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            Assert.That(
                () => NodeId.ParseLongForm($"nsu={ParseLongFormKnownNamespace};i=notanumber", table),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void ParseFallbackToStringIdentifierRecoversMalformedTypedIdentifier()
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(null);
            NamespaceTable table = BuildParseLongFormNamespaces();
            context.NamespaceUris = table;
            NodeId result = NodeId.Parse(
                context,
                $"nsu={ParseLongFormKnownNamespace};i=notanumber",
                new NodeIdParsingOptions
                {
                    RequireResolvedUris = true,
                    FallbackToStringIdentifier = true
                });
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
            Assert.That(GetParseLongFormString(result), Is.EqualTo("i=notanumber"));
        }

        [Test]
        public void ParseLongFormNamespaceIndexPrefixUsesNamespaceTable()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            NodeId result = NodeId.ParseLongForm("ns=1;i=7", table);
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
            Assert.That(GetParseLongFormUInt(result), Is.EqualTo((uint)7));
        }
    }
}
