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
    public class ExpandedNodeIdTests
    {
        [Test]
        public void ShouldNotThrow()
        {
            var expandedNodeIds1 = new ExpandedNodeId[] { new(0), new(0) };
            var expandedNodeIds2 = new ExpandedNodeId[] { new((byte[])null), new((byte[])null) };
            var dv1 = new DataValue(new Variant(expandedNodeIds1));
            var dv2 = new DataValue(new Variant(expandedNodeIds2));
            NUnit.Framework.Assert.DoesNotThrow(() => dv1.Equals(dv2));

            var byteArrayNodeId = new ExpandedNodeId((byte[])null);
            var expandedNodeId = new ExpandedNodeId(NodeId.Null);
            NUnit.Framework.Assert.DoesNotThrow(() => byteArrayNodeId.Equals(expandedNodeId));
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

            // ByteString
            ByteString byteid2 = ByteString.From([65, 66, 67, 68, 69]);
            var nodeId2 = new ExpandedNodeId(byteid2);

            // implicit conversion;
            var inodeId2 = (ExpandedNodeId)byteid2;
            Assert.AreEqual(nodeId2, inodeId2);

            Assert.AreEqual(nodeId1.GetHashCode(), inodeId1.GetHashCode());
            Assert.False(nodeId2 < inodeId2);
            Assert.True(nodeId2 == inodeId2);
            Assert.False(nodeId2 > inodeId2);

            // string
            const string text = "i=123";
            var nodeIdText = ExpandedNodeId.Parse(text);
            Assert.AreEqual(123, nodeIdText.TryGetIdentifier(out uint n1) ? n1 : 0);

            // explicit conversion;
            var inodeIdText = (ExpandedNodeId)text;
            Assert.AreEqual(nodeIdText, inodeIdText);

            // implicit conversion;
            ExpandedNodeId inodeIdText2 = 123;
            Assert.AreEqual(inodeIdText2, inodeIdText);

            Assert.False(nodeIdText < inodeIdText);
            Assert.True(nodeIdText == inodeIdText);
            Assert.True(nodeIdText == inodeIdText2);
            Assert.False(nodeIdText > inodeIdText);

            Assert.True(nodeIdText > nodeId2);
            Assert.False(nodeIdText == nodeId2);
            Assert.False(nodeIdText < nodeId2);

            _ = new ExpandedNodeId(123, 123);
            _ = new ExpandedNodeId("Test", 123);
            _ = new ExpandedNodeId(byteid2, 123);
            _ = new ExpandedNodeId(0, 123);
            _ = new ExpandedNodeId(guid1, 123);

            id = ExpandedNodeId.Parse("ns=1;s=Test");
            ExpandedNodeId nodeId = NodeId.Parse("ns=1;s=Test");
            Assert.AreEqual(1, nodeId.NamespaceIndex);
            Assert.AreEqual("Test", nodeId.IdentifierAsString);
            Assert.AreEqual("Test", nodeId.TryGetIdentifier(out string s1) ? s1 : null);
            Assert.AreEqual("ns=1;s=Test", nodeId.ToString());
            Assert.AreEqual(nodeId, id);
            Assert.True(nodeId == id);

            id = (ExpandedNodeId)"s=Test";
            nodeId = NodeId.Parse("s=Test");
            Assert.AreEqual(0, nodeId.NamespaceIndex);
            Assert.AreEqual("Test", nodeId.IdentifierAsString);
            Assert.AreEqual("Test", nodeId.TryGetIdentifier(out string s4) ? s4 : null);
            Assert.AreEqual("s=Test", nodeId.ToString());
            Assert.AreEqual(nodeId, id);
            Assert.True(nodeId == id);

            const string namespaceUri = "http://opcfoundation.org/Namespace";

            id = new ExpandedNodeId(123, namespaceUri, 2).WithNamespaceIndex(321);
            Assert.AreEqual(2, id.ServerIndex);
            Assert.AreEqual(123, id.TryGetIdentifier(out uint n2) ? n2 : 0);
            Assert.AreEqual(321, id.NamespaceIndex);
            Assert.IsNull(id.NamespaceUri);
            Assert.AreEqual("svr=2;ns=321;i=123", id.ToString());

            id = new ExpandedNodeId(123, namespaceUri, 2);
            Assert.AreEqual(2, id.ServerIndex);
            Assert.AreEqual(123, id.TryGetIdentifier(out uint n3) ? n3 : 0);
            Assert.AreEqual(0, id.NamespaceIndex);
            Assert.AreEqual(namespaceUri, id.NamespaceUri);
            Assert.AreEqual($"svr=2;nsu={namespaceUri};i=123", id.ToString());

            id = new ExpandedNodeId("Test", namespaceUri, 1);
            nodeId = new ExpandedNodeId(byteid2, namespaceUri, 0);
            nodeId2 = new ExpandedNodeId(guid1, namespaceUri, 1);
            Assert.AreNotEqual(nodeId, nodeId2);
            Assert.AreNotEqual(nodeId.GetHashCode(), nodeId2.GetHashCode());

            const string teststring = "nsu=http://opcfoundation.org/Namespace;s=Test";
            nodeId = (ExpandedNodeId)teststring;
            nodeId2 = ExpandedNodeId.Parse(teststring);
            Assert.AreEqual(nodeId, nodeId2);
            Assert.AreEqual(teststring, nodeId2.ToString());

            NUnit.Framework.Assert
                .Throws<ServiceResultException>(() => _ = ExpandedNodeId.Parse("ns="));
            NUnit.Framework.Assert
                .Throws<ServiceResultException>(() => _ = ExpandedNodeId.Parse("nsu="));
            NUnit.Framework.Assert
                .Throws<ServiceResultException>(() => id = (ExpandedNodeId)"Test");
            Assert.IsTrue(NodeId.ToExpandedNodeId(default, null).IsNull);

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
                id = (ExpandedNodeId)testString;
                Assert.AreEqual(testString, id.ToString());
            }
        }

        [Test]
        public void ExpandedNodeIdTryParseValidInputs()
        {
            // Test numeric identifiers
            Assert.IsTrue(ExpandedNodeId.TryParse("i=1234", out ExpandedNodeId result));
            Assert.AreEqual(1234u, result.TryGetIdentifier(out uint n1) ? n1 : 0);
            Assert.AreEqual(IdType.Numeric, result.IdType);
            Assert.AreEqual(0, result.NamespaceIndex);

            Assert.IsTrue(ExpandedNodeId.TryParse("ns=2;i=1234", out result));
            Assert.AreEqual(1234u, result.TryGetIdentifier(out uint n2) ? n2 : 0);
            Assert.AreEqual(IdType.Numeric, result.IdType);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test string identifiers
            Assert.IsTrue(ExpandedNodeId.TryParse("s=HelloWorld", out result));
            Assert.AreEqual("HelloWorld", result.TryGetIdentifier(out string s1) ? s1 : 0);
            Assert.AreEqual(IdType.String, result.IdType);
            Assert.AreEqual(0, result.NamespaceIndex);

            Assert.IsTrue(ExpandedNodeId.TryParse("ns=2;s=HelloWorld", out result));
            Assert.AreEqual("HelloWorld", result.TryGetIdentifier(out string s2) ? s2 : null);
            Assert.AreEqual(IdType.String, result.IdType);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test with namespace URI
            Assert.IsTrue(ExpandedNodeId.TryParse("nsu=http://opcfoundation.org/UA/;s=Test", out result));
            Assert.AreEqual("Test", result.TryGetIdentifier(out string s3) ? s3 : null);
            Assert.AreEqual(IdType.String, result.IdType);
            Assert.AreEqual("http://opcfoundation.org/UA/", result.NamespaceUri);

            // Test with server index
            Assert.IsTrue(ExpandedNodeId.TryParse("svr=1;i=1234", out result));
            Assert.AreEqual(1234u, result.TryGetIdentifier(out uint n3) ? n3 : 0);
            Assert.AreEqual(1u, result.ServerIndex);

            // Test with both server index and namespace URI
            Assert.IsTrue(ExpandedNodeId.TryParse("svr=1;nsu=http://test.org/;s=Test", out result));
            Assert.AreEqual("Test", result.TryGetIdentifier(out string s6) ? s6 : null);
            Assert.AreEqual(1u, result.ServerIndex);
            Assert.AreEqual("http://test.org/", result.NamespaceUri);

            // Test GUID identifiers
            Assert.IsTrue(ExpandedNodeId.TryParse("g=af469096-f02a-4563-940b-603958363b81", out result));
            Assert.AreEqual(new Guid("af469096-f02a-4563-940b-603958363b81"),
                result.TryGetIdentifier(out Guid g1) ? g1 : 0);
            Assert.AreEqual(IdType.Guid, result.IdType);

            // Test opaque identifiers (b=01020304 is valid base64 that decodes to specific bytes)
            Assert.IsTrue(ExpandedNodeId.TryParse("b=01020304", out result));
            ByteString expectedOpaqueBytes = ByteString.FromBase64("01020304");
            Assert.AreEqual(expectedOpaqueBytes, result.TryGetIdentifier(out ByteString o1) ? o1 : default);
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
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            context.NamespaceUris.Append("http://opcfoundation.org/UA/");
            context.NamespaceUris.Append("http://test.org/");
            context.ServerUris.Append("urn:server1");

            // Test with namespace URI
            Assert.IsTrue(ExpandedNodeId.TryParse(context, "nsu=http://test.org/;i=1234", out ExpandedNodeId result));
            Assert.AreEqual(1234u, result.TryGetIdentifier(out uint n3) ? n3 : 0);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test with namespace index
            Assert.IsTrue(ExpandedNodeId.TryParse(context, "ns=2;s=Test", out result));
            Assert.AreEqual("Test", result.TryGetIdentifier(out string s1) ? s1 : null);
            Assert.AreEqual(2, result.NamespaceIndex);

            // Test with server URI - ServerUris table starts at index 0
            Assert.IsTrue(ExpandedNodeId.TryParse(context, "svu=urn:server1;i=1234", out result));
            Assert.AreEqual(1234u, result.TryGetIdentifier(out uint n1) ? n1 : 0);
            Assert.AreEqual(0u, result.ServerIndex);  // First item in ServerUris is at index 0

            // Test with unknown namespace URI - ExpandedNodeId can store URIs not in the table
            // So this should succeed and create an ExpandedNodeId with the namespace URI
            Assert.IsTrue(ExpandedNodeId.TryParse(context, "nsu=http://unknown.org/;i=1234", out result));
            Assert.AreEqual(1234u, result.TryGetIdentifier(out uint n2) ? n2 : 0);
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
