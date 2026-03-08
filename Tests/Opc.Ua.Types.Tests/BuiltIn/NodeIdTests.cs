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
            NodeId inodeId1 = id1;
            Assert.That(inodeId1, Is.EqualTo(nodeId1));

            ByteString id2 = [65, 66, 67, 68, 69];
            var nodeId2 = new NodeId(id2);
            // implicit conversion;
            var inodeId2 = (NodeId)id2;
            Assert.That(inodeId2, Is.EqualTo(nodeId2));

            Assert.That(nodeId2 < inodeId2, Is.False);
            Assert.That(nodeId2 == inodeId2, Is.True);
            Assert.That(nodeId2 > inodeId2, Is.False);

            const string text = "i=123";
            var nodeIdText = NodeId.Parse(text);
            Assert.That(nodeIdText.TryGetIdentifier(out uint t1), Is.True);
            Assert.That(t1, Is.EqualTo(123));
            var inodeIdText = NodeId.Parse(text);
            Assert.That(inodeIdText, Is.EqualTo(nodeIdText));

            Assert.That(nodeIdText < inodeIdText, Is.False);
            Assert.That(nodeIdText == inodeIdText, Is.True);
            Assert.That(nodeIdText > inodeIdText, Is.False);

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

            ServiceResultException sre = NUnit.Framework.Assert.Throws<ServiceResultException>(() =>
                _ = NodeId.Create(123, "urn:xyz", new NamespaceTable()));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            var opaqueId = (NodeId)ByteString.From([33, 44, 55, 66]);
            var stringId1 = NodeId.Parse("ns=1;s=Test");
            var stringId2 = NodeId.Parse("ns=1;s=Test");
            Assert.That(stringId1, Is.EqualTo(stringId2));
            NUnit.Framework.Assert.Throws<ArgumentException>(() => NodeId.Parse("Test"));
            NUnit.Framework.Assert.Throws<ArgumentException>(() => NodeId.Parse("nsu=urn:xyz;Test"));
            var expandedId1 = ExpandedNodeId.Parse("nsu=urn:xyz;Test");
            Assert.That(expandedId1, Is.Not.Null);
            var nullId = ExpandedNodeId.ToNodeId(default, new NamespaceTable());
            Assert.That(nullId.IsNull, Is.True);

            // create a nodeId from a guid
            var guid1 = Guid.NewGuid();
            var guid2 = Guid.NewGuid();
            var nodeGuid1 = new NodeId(id1);

            // now to compare the nodeId to the guids
            Assert.That(nodeGuid1.Equals(id1), Is.True);
            Assert.That(nodeGuid1 == id1, Is.True);
            Assert.That(nodeGuid1 == (NodeId)id1, Is.True);
            Assert.That(nodeGuid1.Equals(id1), Is.True);
            Assert.That(nodeGuid1 == id1, Is.True);
            Assert.That(nodeGuid1.Equals(id2), Is.False);
            Assert.That(nodeGuid1 == id2, Is.False);

            NUnit.Framework.Assert.Throws<ServiceResultException>(
                () => _ = NodeId.Create(123, "urn:xyz", null));
            NUnit.Framework.Assert.Throws<ServiceResultException>(() => _ = NodeId.Parse("ns="));
            NUnit.Framework.Assert.Throws<ArgumentException>(() => _ = NodeId.Parse("nsu="));
            NUnit.Framework.Assert.Throws<ArgumentException>(() => _ = NodeId.Parse("Test"));
            NUnit.Framework.Assert.Throws<ArgumentException>(() =>
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
                0,
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
            Assert.That(distinctNodeIds.Count, Is.EqualTo(distinctNodes));
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

            Assert.That(nodeId.Equals(NodeId.Null), Is.True);
            Assert.That(nodeId.Equals(new NodeId(0, 0)), Is.True);
            Assert.That(nodeId.Equals(new NodeId(Guid.Empty)), Is.True);
            Assert.That(nodeId.Equals(new NodeId(ByteString.Empty)), Is.True);
            Assert.That(nodeId.Equals(new NodeId((ByteString)null)), Is.True);
            Assert.That(nodeId.Equals(NodeId.Parse(null)), Is.True);

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
            Assert.That(comparisonResult2 == 0, Is.False); // assert fails - this is the root cause for the previous assertion failures

            Assert.That(nodeIdBasedDataValue.Value, Is.EqualTo(nodeId));
            Assert.That(nodeIdBasedDataValue.Value.GetHashCode(), Is.EqualTo(nodeId.GetHashCode()));
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
            Assert.That(NodeId.TryParse("i=1234", out NodeId result), Is.True);
            Assert.That(result.TryGetIdentifier(out uint n1) ? n1 : 0, Is.EqualTo(1234u));
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result.NamespaceIndex, Is.EqualTo(0));

            Assert.That(NodeId.TryParse("ns=2;i=1234", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out uint n2) ? n2 : 0, Is.EqualTo(1234u));
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));

            // Test string identifiers
            Assert.That(NodeId.TryParse("s=HelloWorld", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out string s1) ? s1 : null, Is.EqualTo("HelloWorld"));
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.NamespaceIndex, Is.EqualTo(0));

            Assert.That(NodeId.TryParse("ns=2;s=HelloWorld", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out string s2) ? s2 : null, Is.EqualTo("HelloWorld"));
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));

            // Test GUID identifiers
            Assert.That(NodeId.TryParse("g=af469096-f02a-4563-940b-603958363b81", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out Guid g1) ? g1 : Guid.Empty, Is.EqualTo(new Guid("af469096-f02a-4563-940b-603958363b81")));
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));
            Assert.That(result.NamespaceIndex, Is.EqualTo(0));

            Assert.That(NodeId.TryParse("ns=2;g=af469096-f02a-4563-940b-603958363b81", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out Guid g2) ? g2 : Guid.Empty, Is.EqualTo(new Guid("af469096-f02a-4563-940b-603958363b81")));
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));

            // Test opaque identifiers (b=01020304 is valid base64 that decodes to specific bytes)
            Assert.That(NodeId.TryParse("b=01020304", out result), Is.True);
            var expectedBytes1 = ByteString.FromBase64("01020304");
            Assert.That(result.TryGetIdentifier(out ByteString b1) ? b1 : default, Is.EqualTo(expectedBytes1));
            Assert.That(result.IdType, Is.EqualTo(IdType.Opaque));
            Assert.That(result.NamespaceIndex, Is.EqualTo(0));
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
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create())
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

        #region Parse with Context
        [Test]
        public void ParseWithContextValidNumeric()
        {
            // Covers lines 279-298 (Parse with context)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            NodeId result = NodeId.Parse(context, "i=42");
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result, Is.EqualTo(new NodeId(42u)));
        }

        [Test]
        public void ParseWithContextValidString()
        {
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            NodeId result = NodeId.Parse(context, "ns=2;s=TestNode");
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));
        }

        [Test]
        public void ParseWithContextValidGuid()
        {
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var guid = Guid.NewGuid();
            NodeId result = NodeId.Parse(context, $"g={guid}");
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));
            Assert.That(result.TryGetIdentifier(out Guid parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(guid));
        }

        [Test]
        public void ParseWithContextValidOpaque()
        {
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var bytes = new byte[] { 1, 2, 3, 4 };
            string base64 = Convert.ToBase64String(bytes);
            NodeId result = NodeId.Parse(context, $"b={base64}");
            Assert.That(result.IdType, Is.EqualTo(IdType.Opaque));
        }

        [Test]
        public void ParseWithContextNullOrEmptyReturnsNull()
        {
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            NodeId result = NodeId.Parse(context, string.Empty);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ParseWithContextInvalidThrows()
        {
            // Covers lines 283-295 (failure branch with throw)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            Assert.Throws<ServiceResultException>(() =>
                NodeId.Parse(context, "x=invalid"));
        }

        [Test]
        public void ParseWithContextNamespaceUriValid()
        {
            // Covers lines 327-349 (nsu= parsing in InternalTryParseWithContext)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            string nsUri = "http://test.org/UA/";
            context.NamespaceUris.GetIndexOrAppend(nsUri);
            NodeId result = NodeId.Parse(context, $"nsu={nsUri};i=100");
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result.NamespaceIndex, Is.Not.EqualTo(0));
        }

        [Test]
        public void ParseWithContextNamespaceUriMissingSemicolon()
        {
            // Covers lines 332-334 (nsu= without semicolon)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            Assert.Throws<ServiceResultException>(() =>
                NodeId.Parse(context, "nsu=http://test.org"));
        }

        [Test]
        public void ParseWithContextNamespaceUriNotInTable()
        {
            // Covers lines 343-346 (namespace not in table)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            Assert.Throws<ServiceResultException>(() =>
                NodeId.Parse(context, "nsu=http://unknown.org/;i=1"));
        }

        [Test]
        public void ParseWithContextNsIndexMissingSemicolon()
        {
            // Covers lines 356-359 (ns= without semicolon in context parser)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            Assert.Throws<ServiceResultException>(() =>
                NodeId.Parse(context, "ns=2"));
        }

        [Test]
        public void ParseWithContextInvalidIdentifierTypeReturnsError()
        {
            // Covers lines 419-421 (default case in switch for idType)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "ns=0;x=invalid", out NodeId value);
            Assert.That(result, Is.False);
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void ParseWithContextStringWhitespaceIdentifier()
        {
            // Covers lines 392-398 (s= with whitespace-only text)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "s=   ", out NodeId _);
            Assert.That(result, Is.False);
        }

        [Test]
        public void ParseWithContextOpaqueInvalidBase64()
        {
            // Covers lines 400-408 (b= with invalid base64 in context parser)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "b=!!!invalid!!!", out NodeId _);
            Assert.That(result, Is.False);
        }

        [Test]
        public void ParseWithContextGuidInvalid()
        {
            // Covers lines 412-418 (g= with invalid guid in context parser)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "g=not-a-guid", out NodeId _);
            Assert.That(result, Is.False);
        }

        [Test]
        public void ParseWithContextNumericInvalid()
        {
            // Covers line 390 (i= with non-numeric value in context parser)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "i=abc", out NodeId _);
            Assert.That(result, Is.False);
        }

        [Test]
        public void ParseWithContextShortIdentifier()
        {
            // Covers lines 425-426 (text.Length < 2 in context parser)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "a", out NodeId _);
            Assert.That(result, Is.False);
        }

        [Test]
        public void ParseWithContextUpdateTablesOption()
        {
            // Covers lines 339-340 (UpdateTables = true)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var options = new NodeIdParsingOptions { UpdateTables = true };
            NodeId result = NodeId.Parse(context, "nsu=http://newuri.com/;i=5", options);
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result.NamespaceIndex, Is.Not.EqualTo(0));
        }

        [Test]
        public void ParseWithContextNamespaceMappingsOption()
        {
            // Covers lines 366-370 (NamespaceMappings)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var options = new NodeIdParsingOptions
            {
                NamespaceMappings = [0, 5, 10]
            };
            // ns=1 should be less than NamespaceMappings.Length (3), so mapping is applied
            bool result = NodeId.TryParse(context, "ns=1;i=42", options, out NodeId value);
            Assert.That(result, Is.True);
        }
        #endregion

        #region TryParse Overloads
        [Test]
        public void TryParseWithErrorOutput()
        {
            // Covers lines 675-678 (TryParse with error out)
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
            // Covers lines 703-710 (TryParse with context + options, no error out)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "i=99", null, out NodeId value);
            Assert.That(result, Is.True);
            Assert.That(value, Is.EqualTo(new NodeId(99u)));
        }

        [Test]
        public void TryParseWithContextAndOptionsAndError()
        {
            // Covers lines 721-729 (TryParse with context + options + error)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool result = NodeId.TryParse(context, "i=77", null, out NodeId value, out NodeIdParseError error);
            Assert.That(result, Is.True);
            Assert.That(error, Is.EqualTo(NodeIdParseError.None));
            Assert.That(value, Is.EqualTo(new NodeId(77u)));
        }

        [Test]
        public void TryParseInvalidNamespaceIndex()
        {
            // Covers lines 770-777 (ns= with non-numeric namespace index)
            bool result = NodeId.TryParse("ns=abc;i=1", out NodeId _, out NodeIdParseError error);
            Assert.That(result, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.InvalidNamespaceIndex));
        }

        [Test]
        public void TryParseInvalidNumericIdentifier()
        {
            // Covers lines 853-856 (numeric parse failure returning false)
            bool result = NodeId.TryParse("i=notanumber", out NodeId _, out NodeIdParseError error);
            Assert.That(result, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.InvalidIdentifier));
        }

        [Test]
        public void TryParseNamespaceUriFormatReturnsFalse()
        {
            // Covers line 837-838 (nsu= in simple TryParse returns InvalidNamespaceUri)
            bool result = NodeId.TryParse("nsu=http://test.org/;i=1", out NodeId _, out NodeIdParseError error);
            Assert.That(result, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.InvalidNamespaceUri));
        }

        [Test]
        public void ParseIdentifierMissingThrowsArgumentException()
        {
            // Covers lines 627-629 (IdentifierMissing triggers ArgumentException)
            Assert.Throws<ArgumentException>(() =>
                NodeId.Parse("SomeRandomText"));
        }

        [Test]
        public void ParseInvalidNamespaceUriThrowsArgumentException()
        {
            // Covers lines 627-629 (InvalidNamespaceUri triggers ArgumentException)
            Assert.Throws<ArgumentException>(() =>
                NodeId.Parse("nsu=http://opcfoundation.org/UA/;i=1234"));
        }
        #endregion

        #region Format with Context
        [Test]
        public void FormatWithContextNullReturnsEmpty()
        {
            // Covers lines 438-440 (Format context, IsNull)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            NodeId nullId = NodeId.Null;
            string result = nullId.Format(context);
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void FormatWithContextUseNamespaceUri()
        {
            // Covers lines 447-456 (useNamespaceUri = true with valid uri)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            string nsUri = "http://format.test.org/";
            ushort nsIndex = context.NamespaceUris.GetIndexOrAppend(nsUri);
            var nodeId = new NodeId(42u, nsIndex);
            string result = nodeId.Format(context, useNamespaceUri: true);
            Assert.That(result, Does.Contain("nsu="));
            Assert.That(result, Does.Contain("i=42"));
        }

        [Test]
        public void FormatWithContextUseNamespaceUriNotFound()
        {
            // Covers lines 458-462 (useNamespaceUri = true but uri not found)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            // namespace index 999 won't be in the table, falls back to ns= format
            var nodeId = new NodeId(42u, 999);
            string result = nodeId.Format(context, useNamespaceUri: true);
            Assert.That(result, Does.Contain("ns=999"));
        }

        [Test]
        public void FormatWithContextNoNamespaceUri()
        {
            // Covers lines 464-469 (useNamespaceUri = false, ns > 0)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var nodeId = new NodeId(42u, 3);
            string result = nodeId.Format(context, useNamespaceUri: false);
            Assert.That(result, Does.Contain("ns=3"));
            Assert.That(result, Does.Contain("i=42"));
        }

        [Test]
        public void FormatWithContextGuid()
        {
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            string result = nodeId.Format(context);
            Assert.That(result, Does.StartWith("g="));
        }

        [Test]
        public void FormatWithContextOpaque()
        {
            // Covers lines 482-484 (Opaque case in Format context)
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var nodeId = new NodeId(new ByteString(new byte[] { 0xAA, 0xBB }));
            string result = nodeId.Format(context);
            Assert.That(result, Does.StartWith("b="));
        }

        [Test]
        public void FormatWithContextString()
        {
            // Covers lines 486-488
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var nodeId = new NodeId("TestString", 0);
            string result = nodeId.Format(context);
            Assert.That(result, Is.EqualTo("s=TestString"));
        }
        #endregion

        #region Static Format Methods
        [Test]
        public void FormatStaticWithNodeIdToBuffer()
        {
            // Covers lines 892-898 (Format(StringBuilder, NodeId))
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
            // Covers lines 903-914 (Format with IFormatProvider and NodeId)
            var buffer = new StringBuilder();
            var nodeId = new NodeId("Hello", 0);
            NodeId.Format(CultureInfo.InvariantCulture, buffer, nodeId);
            Assert.That(buffer.ToString(), Is.EqualTo("s=Hello"));
        }

        [Test]
        public void FormatStaticWithComponents()
        {
            // Covers lines 919-931 (Format with components, no provider)
            var buffer = new StringBuilder();
#pragma warning disable CA1305 // Specify IFormatProvider
            NodeId.Format(buffer, "1234", IdType.Numeric, 0);
#pragma warning restore CA1305 // Specify IFormatProvider
            Assert.That(buffer.ToString(), Is.EqualTo("i=1234"));
        }

        [Test]
        public void FormatStaticWithProviderAndComponents()
        {
            // Covers lines 937-971 (Format with provider and components)
            var buffer = new StringBuilder();
            NodeId.Format(CultureInfo.InvariantCulture, buffer, "TestValue", IdType.String, 5);
            Assert.That(buffer.ToString(), Is.EqualTo("ns=5;s=TestValue"));
        }

        [Test]
        public void FormatStaticAllIdTypes()
        {
            // Covers all branches in lines 950-967 (switch on identifierType)
            var guid = Guid.NewGuid();
            var bytes = Convert.ToBase64String([1, 2, 3]);

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
            // Covers line 944-946 (namespaceIndex != 0 branch)
            var buffer = new StringBuilder();
            NodeId.Format(CultureInfo.InvariantCulture, buffer, "test", IdType.String, 7);
            Assert.That(buffer.ToString(), Does.Contain("ns=7;"));
        }
        #endregion

        #region Create Methods
        [Test]
        public void CreateStringWithNamespaceUri()
        {
            // Covers lines 524-531 (Create string)
            var namespaceTable = new NamespaceTable();
            string nsUri = "http://create.test.org/";
            namespaceTable.GetIndexOrAppend(nsUri);
            NodeId result = NodeId.Create("TestIdentifier", nsUri, namespaceTable);
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.NamespaceIndex, Is.Not.EqualTo(0));
        }

        [Test]
        public void CreateNumericWithNamespaceUri()
        {
            // Covers lines 541-548 (Create uint)
            var namespaceTable = new NamespaceTable();
            string nsUri = "http://create.test.org/";
            namespaceTable.GetIndexOrAppend(nsUri);
            NodeId result = NodeId.Create(42u, nsUri, namespaceTable);
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
        }

        [Test]
        public void CreateByteStringWithNamespaceUri()
        {
            // Covers lines 558-565 (Create ByteString)
            var namespaceTable = new NamespaceTable();
            string nsUri = "http://create.test.org/";
            namespaceTable.GetIndexOrAppend(nsUri);
            NodeId result = NodeId.Create(new ByteString(new byte[] { 1, 2, 3 }), nsUri, namespaceTable);
            Assert.That(result.IdType, Is.EqualTo(IdType.Opaque));
        }

        [Test]
        public void CreateGuidWithNamespaceUri()
        {
            // Covers lines 575-582 (Create Guid)
            var namespaceTable = new NamespaceTable();
            string nsUri = "http://create.test.org/";
            namespaceTable.GetIndexOrAppend(nsUri);
            var guid = Guid.NewGuid();
            NodeId result = NodeId.Create(guid, nsUri, namespaceTable);
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));
        }

        [Test]
        public void CreateWithUnknownNamespaceThrows()
        {
            // Covers GetNamespaceIndex failure branch
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
            // Covers lines 507-514 (obsolete Create with object)
            var namespaceTable = new NamespaceTable();
            string nsUri = "http://create.test.org/";
            namespaceTable.GetIndexOrAppend(nsUri);
            NodeId result = NodeId.Create((object)42u, nsUri, namespaceTable);
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
        }
#pragma warning restore CS0618
        #endregion

        #region WithIdentifier Methods
        [Test]
        public void WithIdentifierUint()
        {
            // Covers lines 1057-1060
            var nodeId = new NodeId(10u, 5);
            NodeId result = nodeId.WithIdentifier(99u);
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result.NamespaceIndex, Is.EqualTo(5));
            Assert.That(result, Is.EqualTo(new NodeId(99u, 5)));
        }

        [Test]
        public void WithIdentifierString()
        {
            // Covers lines 1066-1069
            var nodeId = new NodeId(10u, 5);
            NodeId result = nodeId.WithIdentifier("NewName");
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.NamespaceIndex, Is.EqualTo(5));
        }

        [Test]
        public void WithIdentifierByteString()
        {
            // Covers lines 1075-1078
            var nodeId = new NodeId(10u, 5);
            NodeId result = nodeId.WithIdentifier(new ByteString(new byte[] { 0xFF }));
            Assert.That(result.IdType, Is.EqualTo(IdType.Opaque));
            Assert.That(result.NamespaceIndex, Is.EqualTo(5));
        }

        [Test]
        public void WithIdentifierGuid()
        {
            // Covers lines 1084-1087
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(10u, 5);
            NodeId result = nodeId.WithIdentifier(guid);
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));
            Assert.That(result.NamespaceIndex, Is.EqualTo(5));
        }

        [Test]
        public void WithNamespaceIndexOnNullNodeId()
        {
            // Covers lines 1028-1031 (null node returns this)
            NodeId nullId = NodeId.Null;
            NodeId result = nullId.WithNamespaceIndex(5);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void WithNamespaceIndexOnStringNodeId()
        {
            // Covers line 1036 (String branch)
            var nodeId = new NodeId("Test", 1);
            NodeId result = nodeId.WithNamespaceIndex(5);
            Assert.That(result.NamespaceIndex, Is.EqualTo(5));
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
        }

        [Test]
        public void WithNamespaceIndexOnGuidNodeId()
        {
            // Covers line 1037 (Guid branch)
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid, 1);
            NodeId result = nodeId.WithNamespaceIndex(5);
            Assert.That(result.NamespaceIndex, Is.EqualTo(5));
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));
        }

        [Test]
        public void WithNamespaceIndexOnOpaqueNodeId()
        {
            // Covers line 1038 (Opaque branch)
            var nodeId = new NodeId(new ByteString(new byte[] { 1, 2 }), 1);
            NodeId result = nodeId.WithNamespaceIndex(5);
            Assert.That(result.NamespaceIndex, Is.EqualTo(5));
            Assert.That(result.IdType, Is.EqualTo(IdType.Opaque));
        }

#pragma warning disable CS0618 // Type or member is obsolete
        [Test]
        public void SetNamespaceIndexObsolete()
        {
            // Covers lines 1048-1051
            var nodeId = new NodeId(42u, 1);
            NodeId result = nodeId.SetNamespaceIndex(9);
            Assert.That(result.NamespaceIndex, Is.EqualTo(9));
        }
#pragma warning restore CS0618
        #endregion

        #region CompareTo Methods
        [Test]
        public void CompareToNodeIdBothNull()
        {
            // Covers lines 1112-1114 (both null)
            NodeId a = NodeId.Null;
            NodeId b = NodeId.Null;
            Assert.That(a.CompareTo(b), Is.EqualTo(0));
        }

        [Test]
        public void CompareToNodeIdThisNull()
        {
            // Covers line 1114 (this is null, other is not)
            NodeId a = NodeId.Null;
            NodeId b = new NodeId(42u);
            Assert.That(a.CompareTo(b), Is.EqualTo(1));
        }

        [Test]
        public void CompareToNodeIdDifferentNamespace()
        {
            // Covers lines 1118-1120
            var a = new NodeId(1u, 5);
            var b = new NodeId(1u, 3);
            Assert.That(a.CompareTo(b), Is.LessThan(0));
            Assert.That(b.CompareTo(a), Is.GreaterThan(0));
        }

        [Test]
        public void CompareToNodeIdDifferentIdType()
        {
            // Covers lines 1124-1126 (different IdType)
            var a = new NodeId("Test", 0);
            var b = new NodeId(Guid.NewGuid(), 0);
            int result = a.CompareTo(b);
            Assert.That(result, Is.Not.EqualTo(0));
        }

        [Test]
        public void CompareToNodeIdSameTypeString()
        {
            // Covers line 1131-1132 (String comparison)
            var a = new NodeId("Alpha", 0);
            var b = new NodeId("Beta", 0);
            Assert.That(a.CompareTo(b), Is.LessThan(0));
        }

        [Test]
        public void CompareToNodeIdSameTypeGuid()
        {
            // Covers lines 1135-1136 (Guid comparison)
            var guid1 = new Guid("00000000-0000-0000-0000-000000000001");
            var guid2 = new Guid("00000000-0000-0000-0000-000000000002");
            var a = new NodeId(guid1, 0);
            var b = new NodeId(guid2, 0);
            Assert.That(a.CompareTo(b), Is.Not.EqualTo(0));
        }

        [Test]
        public void CompareToNodeIdSameTypeOpaque()
        {
            // Covers lines 1137-1138 (Opaque comparison)
            var a = new NodeId(new ByteString(new byte[] { 1 }), 0);
            var b = new NodeId(new ByteString(new byte[] { 2 }), 0);
            Assert.That(a.CompareTo(b), Is.Not.EqualTo(0));
        }

        [Test]
        public void CompareToExpandedNodeIdAbsolute()
        {
            // Covers lines 1146-1148 (absolute ExpandedNodeId)
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(42u, "http://test.org/");
            Assert.That(nodeId.CompareTo(expanded), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToExpandedNodeIdRelative()
        {
            // Covers lines 1144-1151 (non-absolute ExpandedNodeId)
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);
            Assert.That(nodeId.CompareTo(expanded), Is.EqualTo(0));
        }

        [Test]
        public void CompareToStringNull()
        {
            // Covers lines 1154-1158 (this is null, string is null/empty)
            NodeId nullId = NodeId.Null;
            Assert.That(nullId.CompareTo((string)null), Is.EqualTo(0));
            Assert.That(nullId.CompareTo(string.Empty), Is.EqualTo(0));
        }

        [Test]
        public void CompareToStringNullWithValue()
        {
            // Covers line 1158 (this is null, string has value)
            NodeId nullId = NodeId.Null;
            Assert.That(nullId.CompareTo("something"), Is.EqualTo(1));
        }

        [Test]
        public void CompareToStringMismatchedType()
        {
            // Covers lines 1160-1162 (wrong ns or type)
            var numericId = new NodeId(42u);
            Assert.That(numericId.CompareTo("test"), Is.EqualTo(-1));

            var nsId = new NodeId("hello", 2);
            Assert.That(nsId.CompareTo("hello"), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToStringMatch()
        {
            // Covers line 1164 (actual ordinal comparison)
            var nodeId = new NodeId("Alpha", 0);
            Assert.That(nodeId.CompareTo("Alpha"), Is.EqualTo(0));
            Assert.That(nodeId.CompareTo("Beta"), Is.LessThan(0));
        }

        [Test]
        public void CompareToUintNull()
        {
            // Covers lines 1169-1172 (null nodeId vs uint)
            NodeId nullId = NodeId.Null;
            Assert.That(nullId.CompareTo(0u), Is.EqualTo(0));
            Assert.That(nullId.CompareTo(5u), Is.EqualTo(1));
        }

        [Test]
        public void CompareToUintMismatchedType()
        {
            // Covers lines 1174-1176 (wrong ns or type)
            var stringId = new NodeId("test", 0);
            Assert.That(stringId.CompareTo(42u), Is.EqualTo(-1));

            var nsId = new NodeId(42u, 2);
            Assert.That(nsId.CompareTo(42u), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToUintMatch()
        {
            // Covers line 1178 (actual comparison)
            var nodeId = new NodeId(10u);
            Assert.That(nodeId.CompareTo(10u), Is.EqualTo(0));
            Assert.That(nodeId.CompareTo(20u), Is.LessThan(0));
        }

        [Test]
        public void CompareToGuidNull()
        {
            // Covers lines 1183-1186 (null nodeId vs Guid)
            NodeId nullId = NodeId.Null;
            Assert.That(nullId.CompareTo(Guid.Empty), Is.EqualTo(0));
            Assert.That(nullId.CompareTo(Guid.NewGuid()), Is.EqualTo(1));
        }

        [Test]
        public void CompareToGuidMismatchedType()
        {
            // Covers lines 1188-1190 (wrong ns or type)
            var numericId = new NodeId(42u);
            Assert.That(numericId.CompareTo(Guid.NewGuid()), Is.EqualTo(-1));

            var guidId = new NodeId(Guid.NewGuid(), 2);
            Assert.That(guidId.CompareTo(Guid.NewGuid()), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToGuidMatch()
        {
            // Covers line 1192 (actual comparison)
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            Assert.That(nodeId.CompareTo(guid), Is.EqualTo(0));
        }

        [Test]
        public void CompareToByteStringNull()
        {
            // Covers lines 1197-1200 (null nodeId vs ByteString)
            NodeId nullId = NodeId.Null;
            Assert.That(nullId.CompareTo(ByteString.Empty), Is.EqualTo(0));
            Assert.That(nullId.CompareTo(new ByteString(new byte[] { 1 })), Is.EqualTo(1));
        }

        [Test]
        public void CompareToByteStringMismatchedType()
        {
            // Covers lines 1202-1204 (wrong ns or type)
            var numericId = new NodeId(42u);
            Assert.That(numericId.CompareTo(new ByteString(new byte[] { 1 })), Is.EqualTo(-1));

            var opaqueId = new NodeId(new ByteString(new byte[] { 1 }), 2);
            Assert.That(opaqueId.CompareTo(new ByteString(new byte[] { 1 })), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToByteStringMatch()
        {
            // Covers line 1206 (actual comparison)
            var bytes = new ByteString(new byte[] { 1, 2, 3 });
            var nodeId = new NodeId(bytes);
            Assert.That(nodeId.CompareTo(bytes), Is.EqualTo(0));
        }

        [Test]
        public void CompareToObjectNull()
        {
            // Covers line 1215 (null object, null nodeId)
            NodeId nullId = NodeId.Null;
            Assert.That(nullId.CompareTo((object)null), Is.EqualTo(0));

            // non-null nodeId vs null
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.CompareTo((object)null), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToObjectInt()
        {
            // Covers line 1216 (int, positive and negative)
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.CompareTo((object)42), Is.EqualTo(0));
            Assert.That(nodeId.CompareTo((object)(-1)), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToObjectUint()
        {
            // Covers line 1217
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.CompareTo((object)42u), Is.EqualTo(0));
        }

        [Test]
        public void CompareToObjectGuid()
        {
            // Covers line 1218
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            Assert.That(nodeId.CompareTo((object)guid), Is.EqualTo(0));
        }

        [Test]
        public void CompareToObjectUuid()
        {
            // Covers line 1219
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            Assert.That(nodeId.CompareTo((object)new Uuid(guid)), Is.EqualTo(0));
        }

        [Test]
        public void CompareToObjectString()
        {
            // Covers line 1220
            var nodeId = new NodeId("test", 0);
            Assert.That(nodeId.CompareTo((object)"test"), Is.EqualTo(0));
        }

        [Test]
        public void CompareToObjectByteString()
        {
            // Covers line 1221
            var bytes = new ByteString(new byte[] { 1, 2 });
            var nodeId = new NodeId(bytes);
            Assert.That(nodeId.CompareTo((object)bytes), Is.EqualTo(0));
        }

        [Test]
        public void CompareToObjectExpandedNodeId()
        {
            // Covers line 1222
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);
            Assert.That(nodeId.CompareTo((object)expanded), Is.EqualTo(0));
        }

        [Test]
        public void CompareToObjectNodeId()
        {
            // Covers line 1223
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.CompareTo((object)new NodeId(42u)), Is.EqualTo(0));
        }

        [Test]
        public void CompareToObjectSerializableNodeId()
        {
            // Covers line 1224
            var nodeId = new NodeId(42u);
            var serializable = new SerializableNodeId(nodeId);
            Assert.That(nodeId.CompareTo((object)serializable), Is.EqualTo(0));
        }

        [Test]
        public void CompareToObjectSerializableExpandedNodeId()
        {
            // Covers line 1225
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);
            var serializable = new SerializableExpandedNodeId(expanded);
            Assert.That(nodeId.CompareTo((object)serializable), Is.EqualTo(0));
        }

        [Test]
        public void CompareToObjectUnknownType()
        {
            // Covers line 1226 (default => -1)
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.CompareTo((object)3.14), Is.EqualTo(-1));
        }
        #endregion

        #region Comparison Operators
        [Test]
        public void OperatorGreaterThanOrEqual()
        {
            // Covers lines 1243-1246
            var a = new NodeId(10u);
            var b = new NodeId(10u);
            var c = new NodeId(20u);
            Assert.That(a >= b, Is.True);
            Assert.That(c >= a, Is.True);
        }

        [Test]
        public void OperatorLessThanOrEqual()
        {
            // Covers lines 1249-1252
            var a = new NodeId(10u);
            var b = new NodeId(10u);
            var c = new NodeId(20u);
            Assert.That(a <= b, Is.True);
            Assert.That(a <= c, Is.True);
        }
        #endregion

        #region ToString / IFormattable
        [Test]
        public void ToStringIFormattableNullFormat()
        {
            // Covers lines 1255-1259 (format == null)
            var nodeId = new NodeId(42u, 3);
            string result = nodeId.ToString(null, CultureInfo.InvariantCulture);
            Assert.That(result, Is.EqualTo("ns=3;i=42"));
        }

        [Test]
        public void ToStringIFormattableNonNullFormatThrows()
        {
            // Covers lines 1262 (non-null format throws)
            var nodeId = new NodeId(42u);
            Assert.Throws<FormatException>(() =>
                nodeId.ToString("G", CultureInfo.InvariantCulture));
        }
        #endregion

        #region Equals Methods
        [Test]
        public void EqualsObjectNull()
        {
            // Covers line 1270 (obj is null)
            NodeId nullId = NodeId.Null;
            Assert.That(nullId.Equals((object)null), Is.True);

            var nodeId = new NodeId(42u);
            Assert.That(nodeId.Equals((object)null), Is.False);
        }

        [Test]
        public void EqualsObjectInt()
        {
            // Covers line 1271 (obj is int)
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.Equals((object)42), Is.True);
            Assert.That(nodeId.Equals((object)(-1)), Is.False);
        }

        [Test]
        public void EqualsObjectUint()
        {
            // Covers line 1272 (obj is uint)
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.Equals((object)42u), Is.True);
        }

        [Test]
        public void EqualsObjectGuid()
        {
            // Covers line 1273 (obj is Guid)
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            Assert.That(nodeId.Equals((object)guid), Is.True);
        }

        [Test]
        public void EqualsObjectByteString()
        {
            // Covers line 1274 (obj is ByteString)
            var bytes = new ByteString(new byte[] { 1, 2, 3 });
            var nodeId = new NodeId(bytes);
            Assert.That(nodeId.Equals((object)bytes), Is.True);
        }

        [Test]
        public void EqualsObjectString()
        {
            // Covers line 1275 (obj is string)
            var nodeId = new NodeId("test", 0);
            Assert.That(nodeId.Equals((object)"test"), Is.True);
        }

        [Test]
        public void EqualsObjectExpandedNodeId()
        {
            // Covers line 1276 (obj is ExpandedNodeId)
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);
            Assert.That(nodeId.Equals((object)expanded), Is.True);
        }

        [Test]
        public void EqualsObjectSerializableNodeId()
        {
            // Covers line 1278 (obj is SerializableNodeId)
            var nodeId = new NodeId(42u);
            var serializable = new SerializableNodeId(nodeId);
            Assert.That(nodeId.Equals((object)serializable), Is.True);
        }

        [Test]
        public void EqualsObjectSerializableExpandedNodeId()
        {
            // Covers line 1279 (obj is SerializableExpandedNodeId)
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);
            var serializable = new SerializableExpandedNodeId(expanded);
            Assert.That(nodeId.Equals((object)serializable), Is.True);
        }

        [Test]
        public void EqualsObjectUnknownType()
        {
            // Covers line 1280 (base.Equals fallback)
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.Equals((object)3.14), Is.False);
        }

        [Test]
        public void EqualsNodeIdFallsThrough()
        {
            // Covers line 1315 (fallthrough return false for mismatched id types
            // after type check)
            var a = new NodeId("test", 0);
            var b = new NodeId(42u, 0);
            Assert.That(a.Equals(b), Is.False);
        }

        [Test]
        public void EqualsExpandedNodeIdAbsolute()
        {
            // Covers lines 1321-1323 (absolute ExpandedNodeId)
            var nodeId = new NodeId(42u);
            var absolute = new ExpandedNodeId(42u, "http://test.org/");
            Assert.That(nodeId.Equals(absolute), Is.False);
        }

        [Test]
        public void EqualsExpandedNodeIdRelative()
        {
            // Covers line 1325 (non-absolute)
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);
            Assert.That(nodeId.Equals(expanded), Is.True);
        }

        [Test]
        public void EqualsUintMatch()
        {
            // Covers lines 1329-1336
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.Equals(42u), Is.True);
            Assert.That(nodeId.Equals(99u), Is.False);
        }

        [Test]
        public void EqualsUintMismatchedType()
        {
            // Covers lines 1331-1333 (non-numeric or wrong namespace)
            var stringId = new NodeId("test", 0);
            Assert.That(stringId.Equals(42u), Is.False);

            var nsId = new NodeId(42u, 2);
            Assert.That(nsId.Equals(42u), Is.False);
        }

        [Test]
        public void EqualsGuidMatch()
        {
            // Covers lines 1339-1346
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            Assert.That(nodeId.Equals(guid), Is.True);
            Assert.That(nodeId.Equals(Guid.NewGuid()), Is.False);
        }

        [Test]
        public void EqualsGuidMismatchedType()
        {
            // Covers lines 1341-1343
            var numericId = new NodeId(42u);
            Assert.That(numericId.Equals(Guid.NewGuid()), Is.False);
        }

        [Test]
        public void EqualsByteStringMatch()
        {
            // Covers lines 1349-1356
            var bytes = new ByteString(new byte[] { 1, 2, 3 });
            var nodeId = new NodeId(bytes);
            Assert.That(nodeId.Equals(bytes), Is.True);
        }

        [Test]
        public void EqualsByteStringMismatchedType()
        {
            // Covers lines 1351-1353
            var numericId = new NodeId(42u);
            Assert.That(numericId.Equals(new ByteString(new byte[] { 1 })), Is.False);
        }

        [Test]
        public void EqualsStringMatch()
        {
            // Covers lines 1359-1373
            var nodeId = new NodeId("hello", 0);
            Assert.That(nodeId.Equals("hello"), Is.True);
            Assert.That(nodeId.Equals("world"), Is.False);
        }

        [Test]
        public void EqualsStringMismatchedType()
        {
            // Covers lines 1361-1363
            var numericId = new NodeId(42u);
            Assert.That(numericId.Equals("test"), Is.False);

            var nsId = new NodeId("test", 2);
            Assert.That(nsId.Equals("test"), Is.False);
        }

        [Test]
        public void EqualsStringNullOrEmpty()
        {
            // Covers lines 1365-1367 (null/empty string vs null NodeId)
            NodeId nullId = new NodeId(string.Empty, 0);
            Assert.That(nullId.Equals((string)null), Is.True);
            Assert.That(nullId.Equals(string.Empty), Is.True);
        }
        #endregion

        #region Equality and Comparison Operators
        [Test]
        public void OperatorEqualsObject()
        {
            // Covers lines 1416-1419 (== object)
            var nodeId = new NodeId(42u);
            Assert.That(nodeId == (object)42u, Is.True);
            Assert.That(nodeId == (object)99u, Is.False);
        }

        [Test]
        public void OperatorNotEqualsObject()
        {
            // Covers lines 1422-1425 (!= object)
            var nodeId = new NodeId(42u);
            Assert.That(nodeId != (object)99u, Is.True);
            Assert.That(nodeId != (object)42u, Is.False);
        }

        [Test]
        public void OperatorEqualsExpandedNodeId()
        {
            // Covers lines 1440-1443 (== ExpandedNodeId)
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(nodeId);
            Assert.That(nodeId == expanded, Is.True);
        }

        [Test]
        public void OperatorNotEqualsExpandedNodeId()
        {
            // Covers lines 1446-1449 (!= ExpandedNodeId)
            var nodeId = new NodeId(42u);
            var expanded = new ExpandedNodeId(99u);
            Assert.That(nodeId != expanded, Is.True);
        }

        [Test]
        public void OperatorEqualsUint()
        {
            // Covers lines 1452-1455 (== uint)
            var nodeId = new NodeId(42u);
            Assert.That(nodeId == 42u, Is.True);
        }

        [Test]
        public void OperatorNotEqualsUint()
        {
            // Covers lines 1458-1461 (!= uint)
            var nodeId = new NodeId(42u);
            Assert.That(nodeId != 99u, Is.True);
        }

        [Test]
        public void OperatorEqualsByteString()
        {
            // Covers lines 1464-1467 (== ByteString)
            var bytes = new ByteString(new byte[] { 1, 2 });
            var nodeId = new NodeId(bytes);
            Assert.That(nodeId == bytes, Is.True);
        }

        [Test]
        public void OperatorNotEqualsByteString()
        {
            // Covers lines 1470-1473 (!= ByteString)
            var nodeId = new NodeId(new ByteString(new byte[] { 1 }));
            Assert.That(nodeId != new ByteString(new byte[] { 2 }), Is.True);
        }

        [Test]
        public void OperatorEqualsGuid()
        {
            // Covers lines 1476-1479 (== Guid)
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            Assert.That(nodeId == guid, Is.True);
        }

        [Test]
        public void OperatorNotEqualsGuid()
        {
            // Covers lines 1482-1485 (!= Guid)
            var nodeId = new NodeId(Guid.NewGuid());
            Assert.That(nodeId != Guid.Empty, Is.True);
        }

        [Test]
        public void OperatorEqualsString()
        {
            // Covers lines 1488-1491 (== string)
            var nodeId = new NodeId("test", 0);
            Assert.That(nodeId == "test", Is.True);
        }

        [Test]
        public void OperatorNotEqualsString()
        {
            // Covers lines 1494-1497 (!= string)
            var nodeId = new NodeId("test", 0);
            Assert.That(nodeId != "other", Is.True);
        }
        #endregion

        #region TryGetIdentifier
        [Test]
        public void TryGetIdentifierUintFalseForNonNumeric()
        {
            // Covers lines 1566-1567
            var nodeId = new NodeId("test", 0);
            Assert.That(nodeId.TryGetIdentifier(out uint _), Is.False);
        }

        [Test]
        public void TryGetIdentifierByteStringFalseForNonOpaque()
        {
            // Covers lines 1580-1581
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.TryGetIdentifier(out ByteString _), Is.False);
        }

        [Test]
        public void TryGetIdentifierByteStringTrue()
        {
            // Covers lines 1575-1578
            var bytes = new ByteString(new byte[] { 1, 2, 3 });
            var nodeId = new NodeId(bytes);
            Assert.That(nodeId.TryGetIdentifier(out ByteString result), Is.True);
            Assert.That(result, Is.EqualTo(bytes));
        }

        [Test]
        public void TryGetIdentifierStringFalseForNonString()
        {
            // Covers lines 1594-1595
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.TryGetIdentifier(out string _), Is.False);
        }

        [Test]
        public void TryGetIdentifierStringTrue()
        {
            // Covers lines 1589-1592
            var nodeId = new NodeId("hello", 0);
            Assert.That(nodeId.TryGetIdentifier(out string result), Is.True);
            Assert.That(result, Is.EqualTo("hello"));
        }

        [Test]
        public void TryGetIdentifierGuidFalseForNonGuid()
        {
            // Covers lines 1608-1609
            var nodeId = new NodeId(42u);
            Assert.That(nodeId.TryGetIdentifier(out Guid _), Is.False);
        }

        [Test]
        public void TryGetIdentifierGuidTrue()
        {
            // Covers lines 1603-1606
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            Assert.That(nodeId.TryGetIdentifier(out Guid result), Is.True);
            Assert.That(result, Is.EqualTo(guid));
        }
        #endregion

        #region Identifier and IdentifierAsString
#pragma warning disable CS0618 // Type or member is obsolete
        [Test]
        public void IdentifierPropertyString()
        {
            // Covers line 1535 (String branch of Identifier)
            var nodeId = new NodeId("test", 0);
            object id = nodeId.Identifier;
            Assert.That(id, Is.TypeOf<string>());
            Assert.That(id, Is.EqualTo("test"));
        }

        [Test]
        public void IdentifierPropertyGuid()
        {
            // Covers line 1536 (Guid branch)
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            object id = nodeId.Identifier;
            Assert.That(id, Is.TypeOf<Guid>());
            Assert.That(id, Is.EqualTo(guid));
        }

        [Test]
        public void IdentifierPropertyOpaque()
        {
            // Covers line 1537 (Opaque branch)
            var bytes = new ByteString(new byte[] { 1, 2, 3 });
            var nodeId = new NodeId(bytes);
            object id = nodeId.Identifier;
            Assert.That(id, Is.TypeOf<ByteString>());
            Assert.That(id, Is.EqualTo(bytes));
        }

        [Test]
        public void IdentifierPropertyNumeric()
        {
            // Covers line 1534 (Numeric branch)
            var nodeId = new NodeId(42u);
            object id = nodeId.Identifier;
            Assert.That(id, Is.TypeOf<uint>());
            Assert.That(id, Is.EqualTo(42u));
        }
#pragma warning restore CS0618

        [Test]
        public void IdentifierAsStringOpaque()
        {
            // Covers line 1551 (Opaque branch of IdentifierAsString)
            var bytes = new ByteString(new byte[] { 0xAA, 0xBB });
            var nodeId = new NodeId(bytes);
            string str = nodeId.IdentifierAsString;
            Assert.That(str, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void IdentifierAsStringGuid()
        {
            // Covers line 1550 (Guid branch)
            var guid = Guid.NewGuid();
            var nodeId = new NodeId(guid);
            string str = nodeId.IdentifierAsString;
            Assert.That(str, Is.EqualTo(guid.ToString()));
        }
        #endregion

        #region IsNull Property
        [Test]
        public void IsNullWithNonZeroNamespace()
        {
            // Covers line 1619 (NamespaceIndex != 0 => false)
            var nodeId = new NodeId(0u, 5);
            Assert.That(nodeId.IsNull, Is.False);
        }

        [Test]
        public void IsNullEmptyGuid()
        {
            // Covers line 1622 (Guid == Guid.Empty)
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
            // Covers line 1623 (OpaqueIdentifer.Length == 0)
            var nodeId = new NodeId(ByteString.Empty);
            Assert.That(nodeId.IsNull, Is.True);
        }

        [Test]
        public void IsNullNonEmptyOpaque()
        {
            var nodeId = new NodeId(new ByteString(new byte[] { 1 }));
            Assert.That(nodeId.IsNull, Is.False);
        }
        #endregion

        #region ToExpandedNodeId
        [Test]
        public void ToExpandedNodeIdNull()
        {
            // Covers lines 1002-1004
            var namespaceTable = new NamespaceTable();
            ExpandedNodeId result = NodeId.ToExpandedNodeId(NodeId.Null, namespaceTable);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ToExpandedNodeIdWithNamespace()
        {
            // Covers lines 1009-1016
            var namespaceTable = new NamespaceTable();
            string nsUri = "http://test.org/";
            namespaceTable.GetIndexOrAppend(nsUri);
            var nodeId = new NodeId(42u, 1);
            ExpandedNodeId result = NodeId.ToExpandedNodeId(nodeId, namespaceTable);
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.NamespaceUri, Is.EqualTo(nsUri));
        }

        [Test]
        public void ToExpandedNodeIdNamespaceZero()
        {
            // Covers lines 1007, 1019 (ns=0, skip uri lookup)
            var namespaceTable = new NamespaceTable();
            var nodeId = new NodeId(42u, 0);
            ExpandedNodeId result = NodeId.ToExpandedNodeId(nodeId, namespaceTable);
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.NamespaceUri, Is.Null.Or.Empty);
        }
        #endregion

        #region Obsolete Constructor Tests
#pragma warning disable CS0618 // Type or member is obsolete
        [Test]
        public void ObsoleteStringConstructor()
        {
            // Covers lines 232-235 (NodeId(string text))
            var nodeId = new NodeId("i=42");
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(nodeId, Is.EqualTo(new NodeId(42u)));
        }

        [Test]
        public void ObsoleteObjectConstructorUint()
        {
            // Covers lines 246-268 (NodeId(object, ushort))
            var nodeId = new NodeId((object)42u, 3);
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(3));
        }

        [Test]
        public void ObsoleteObjectConstructorNullString()
        {
            // Covers case null or string (line 253-254)
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
            // Covers case Guid (line 256-257)
            var guid = Guid.NewGuid();
            var nodeId = new NodeId((object)guid, 1);
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Guid));
            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void ObsoleteObjectConstructorByteString()
        {
            // Covers case ByteString (line 259-260)
            var bytes = new ByteString(new byte[] { 5, 6 });
            var nodeId = new NodeId((object)bytes, 4);
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Opaque));
            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(4));
        }

        [Test]
        public void ObsoleteObjectConstructorUnsupportedTypeThrows()
        {
            // Covers lines 262-265 (default case throws)
            Assert.Throws<ArgumentException>(() =>
                new NodeId((object)3.14, 0));
        }
#pragma warning restore CS0618
        #endregion

        #region SerializableNodeId
        [Test]
        public void SerializableNodeIdDefaultConstructor()
        {
            // Covers lines 1694-1697
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
        #endregion

        #region NodeIdParsingOptions
        [Test]
        public void NodeIdParsingOptionsProperties()
        {
            // Covers lines 1672, 1677, 1682
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
        #endregion

        #region Implicit/Explicit Conversions
        [Test]
        public void ImplicitConversionFromUint()
        {
            NodeId nodeId = 42u;
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(nodeId, Is.EqualTo(new NodeId(42u)));
        }

        [Test]
        public void ImplicitConversionFromGuid()
        {
            var guid = Guid.NewGuid();
            NodeId nodeId = guid;
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Guid));
        }

        [Test]
        public void ExplicitConversionFromByteString()
        {
            var bytes = new ByteString(new byte[] { 1, 2 });
            NodeId nodeId = (NodeId)bytes;
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.Opaque));
        }
        #endregion

        #region Round-Trip Parsing
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
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create());
            string nsUri = "http://roundtrip.org/";
            context.NamespaceUris.GetIndexOrAppend(nsUri);
            var original = new NodeId(42u, 1);

            string formatted = original.Format(context, useNamespaceUri: true);
            NodeId parsed = NodeId.Parse(context, formatted);
            Assert.That(parsed, Is.EqualTo(original));
        }
        #endregion

        #region Edge Cases
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
            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(0));
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
            Assert.That(numericNull.Equals(stringNull), Is.True);
        }

        [Test]
        public void CompareToNodeIdSameNumericValues()
        {
            var a = new NodeId(42u, 0);
            var b = new NodeId(42u, 0);
            Assert.That(a.CompareTo(b), Is.EqualTo(0));
        }
        #endregion
    }
}
