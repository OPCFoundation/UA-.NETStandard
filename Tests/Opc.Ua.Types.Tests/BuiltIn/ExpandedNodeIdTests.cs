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
using System.Globalization;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.BuiltIn
{
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
            var expandedNodeIds2 = new ExpandedNodeId[] { new((ByteString)null), new((ByteString)null) };
            var dv1 = new DataValue(Variant.From(expandedNodeIds1));
            var dv2 = new DataValue(Variant.From(expandedNodeIds2));
            Assert.DoesNotThrow(() => dv1.Equals(dv2));

            var byteArrayNodeId = new ExpandedNodeId((ByteString)null);
            var expandedNodeId = new ExpandedNodeId(NodeId.Null);
            Assert.DoesNotThrow(() => byteArrayNodeId.Equals(expandedNodeId));
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
            Assert.That(inodeId1, Is.EqualTo(nodeId1));

            // ByteString
            ByteString byteid2 = [65, 66, 67, 68, 69];
            var nodeId2 = new ExpandedNodeId(byteid2);

            // implicit conversion;
            var inodeId2 = (ExpandedNodeId)byteid2;
            Assert.That(inodeId2, Is.EqualTo(nodeId2));

            Assert.That(inodeId1.GetHashCode(), Is.EqualTo(nodeId1.GetHashCode()));
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(nodeId2 < inodeId2, Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(nodeId2, Is.EqualTo(inodeId2));
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(nodeId2 > inodeId2, Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure

            // string
            const string text = "i=123";
            var nodeIdText = ExpandedNodeId.Parse(text);
            Assert.That(nodeIdText.TryGetIdentifier(out uint n1) ? n1 : 0, Is.EqualTo(123));

            // explicit conversion;
            var inodeIdText = (ExpandedNodeId)text;
            Assert.That(inodeIdText, Is.EqualTo(nodeIdText));
            // implicit conversion;
            ExpandedNodeId inodeIdText2 = 123;
            Assert.That(inodeIdText2, Is.EqualTo(inodeIdText));

#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(nodeIdText < inodeIdText, Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(nodeIdText, Is.EqualTo(inodeIdText));
            Assert.That(nodeIdText, Is.EqualTo(inodeIdText2));
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(nodeIdText > inodeIdText, Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure

            Assert.That(nodeIdText, Is.GreaterThan(nodeId2));
            Assert.That(nodeIdText, Is.Not.EqualTo(nodeId2));
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(nodeIdText < nodeId2, Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            _ = new ExpandedNodeId(123, 123);
            _ = new ExpandedNodeId("Test", 123);
            _ = new ExpandedNodeId(byteid2, 123);
            _ = new ExpandedNodeId(0, 123);
            _ = new ExpandedNodeId(guid1, 123);

            id = ExpandedNodeId.Parse("ns=1;s=Test");
            ExpandedNodeId nodeId = NodeId.Parse("ns=1;s=Test");
            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(1));
            Assert.That(nodeId.IdentifierAsString, Is.EqualTo("Test"));
            Assert.That(nodeId.TryGetIdentifier(out string s1) ? s1 : null, Is.EqualTo("Test"));
            Assert.That(nodeId.ToString(), Is.EqualTo("ns=1;s=Test"));
            Assert.That(nodeId, Is.EqualTo(id));
            Assert.That(nodeId, Is.EqualTo(id));

            id = (ExpandedNodeId)"s=Test";
            nodeId = NodeId.Parse("s=Test");
            Assert.That(nodeId.NamespaceIndex, Is.Zero);
            Assert.That(nodeId.IdentifierAsString, Is.EqualTo("Test"));
            Assert.That(nodeId.TryGetIdentifier(out string s4) ? s4 : null, Is.EqualTo("Test"));
            Assert.That(nodeId.ToString(), Is.EqualTo("s=Test"));
            Assert.That(nodeId, Is.EqualTo(id));
            Assert.That(nodeId, Is.EqualTo(id));
            const string namespaceUri = "http://opcfoundation.org/Namespace";

            id = new ExpandedNodeId(123, namespaceUri, 2).WithNamespaceIndex(321);
            Assert.That(id.ServerIndex, Is.EqualTo(2));
            Assert.That(id.TryGetIdentifier(out uint n2) ? n2 : 0, Is.EqualTo(123));
            Assert.That(id.NamespaceIndex, Is.EqualTo(321));
            Assert.That(id.NamespaceUri, Is.Null);
            Assert.That(id.ToString(), Is.EqualTo("svr=2;ns=321;i=123"));
            id = new ExpandedNodeId(123, namespaceUri, 2);
            Assert.That(id.ServerIndex, Is.EqualTo(2));
            Assert.That(id.TryGetIdentifier(out uint n3) ? n3 : 0, Is.EqualTo(123));
            Assert.That(id.NamespaceIndex, Is.Zero);
            Assert.That(id.NamespaceUri, Is.EqualTo(namespaceUri));
            Assert.That(id.ToString(), Is.EqualTo($"svr=2;nsu={namespaceUri};i=123"));
            id = new ExpandedNodeId("Test", namespaceUri, 1);
            nodeId = new ExpandedNodeId(byteid2, namespaceUri, 0);
            nodeId2 = new ExpandedNodeId(guid1, namespaceUri, 1);
            Assert.That(nodeId, Is.Not.EqualTo(nodeId2));
            Assert.That(nodeId.GetHashCode(), Is.Not.EqualTo(nodeId2.GetHashCode()));

            const string teststring = "nsu=http://opcfoundation.org/Namespace;s=Test";
            nodeId = (ExpandedNodeId)teststring;
            nodeId2 = ExpandedNodeId.Parse(teststring);
            Assert.That(nodeId, Is.EqualTo(nodeId2));
            Assert.That(nodeId2.ToString(), Is.EqualTo(teststring));

            Assert.Throws<ServiceResultException>(() => _ = ExpandedNodeId.Parse("ns="));
            Assert.Throws<ServiceResultException>(() => _ = ExpandedNodeId.Parse("nsu="));
            Assert.Throws<ServiceResultException>(() => id = (ExpandedNodeId)"Test");
            Assert.That(NodeId.ToExpandedNodeId(default, null).IsNull, Is.True);

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
                Assert.That(id.ToString(), Is.EqualTo(testString));
                id = (ExpandedNodeId)testString;
                Assert.That(id.ToString(), Is.EqualTo(testString));
            }
        }

        [Test]
        public void ExpandedNodeIdTryParseValidInputs()
        {
            // Test numeric identifiers
            Assert.That(ExpandedNodeId.TryParse("i=1234", out ExpandedNodeId result), Is.True);
            Assert.That(result.TryGetIdentifier(out uint n1) ? n1 : 0, Is.EqualTo(1234u));
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result.NamespaceIndex, Is.Zero);

            Assert.That(ExpandedNodeId.TryParse("ns=2;i=1234", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out uint n2) ? n2 : 0, Is.EqualTo(1234u));
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));

            // Test string identifiers
            Assert.That(ExpandedNodeId.TryParse("s=HelloWorld", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out string s1) ? s1 : null, Is.EqualTo("HelloWorld"));
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.NamespaceIndex, Is.Zero);

            Assert.That(ExpandedNodeId.TryParse("ns=2;s=HelloWorld", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out string s2) ? s2 : null, Is.EqualTo("HelloWorld"));
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));

            // Test with namespace URI
            Assert.That(ExpandedNodeId.TryParse("nsu=http://opcfoundation.org/UA/;s=Test", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out string s3) ? s3 : null, Is.EqualTo("Test"));
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://opcfoundation.org/UA/"));

            // Test with server index
            Assert.That(ExpandedNodeId.TryParse("svr=1;i=1234", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out uint n3) ? n3 : 0, Is.EqualTo(1234u));
            Assert.That(result.ServerIndex, Is.EqualTo(1u));

            // Test with both server index and namespace URI
            Assert.That(ExpandedNodeId.TryParse("svr=1;nsu=http://test.org/;s=Test", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out string s6) ? s6 : null, Is.EqualTo("Test"));
            Assert.That(result.ServerIndex, Is.EqualTo(1u));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://test.org/"));

            // Test GUID identifiers
            Assert.That(ExpandedNodeId.TryParse("g=af469096-f02a-4563-940b-603958363b81", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out Guid g1) ? g1 : Guid.Empty, Is.EqualTo(new Guid("af469096-f02a-4563-940b-603958363b81")));
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));

            // Test opaque identifiers (b=01020304 is valid base64 that decodes to specific bytes)
            Assert.That(ExpandedNodeId.TryParse("b=01020304", out result), Is.True);
            var expectedOpaqueBytes = ByteString.FromBase64("01020304");
            Assert.That(result.TryGetIdentifier(out ByteString o1) ? o1 : default, Is.EqualTo(expectedOpaqueBytes));
            Assert.That(result.IdType, Is.EqualTo(IdType.Opaque));

            // Test null and empty
            Assert.That(ExpandedNodeId.TryParse(null, out result), Is.True);
            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(ExpandedNodeId.TryParse(string.Empty, out result), Is.True);
            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));
        }

        [Test]
        public void ExpandedNodeIdTryParseInvalidInputs()
        {
            // Invalid formats should return false and ExpandedNodeId.Null
            Assert.That(ExpandedNodeId.TryParse("invalid", out ExpandedNodeId result), Is.False);
            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));

            Assert.That(ExpandedNodeId.TryParse("ns=", out result), Is.False);
            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(ExpandedNodeId.TryParse("nsu=", out result), Is.False);
            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));

            Assert.That(ExpandedNodeId.TryParse("svr=", out result), Is.False);
            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));
            // Invalid identifier values
            Assert.That(ExpandedNodeId.TryParse("i=notanumber", out result), Is.False);
            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));

            Assert.That(ExpandedNodeId.TryParse("g=not-a-valid-guid", out result), Is.False);
            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(ExpandedNodeId.TryParse("b=notbase64!@#", out result), Is.False);
            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));
        }

        [Test]
        public void ExpandedNodeIdTryParseWithContext()
        {
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create(), new EncodeableFactory())
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            context.NamespaceUris.Append("http://opcfoundation.org/UA/");
            context.NamespaceUris.Append("http://test.org/");
            context.ServerUris.Append("urn:server1");

            // Test with namespace URI
            Assert.That(ExpandedNodeId.TryParse(context, "nsu=http://test.org/;i=1234", out ExpandedNodeId result), Is.True);
            Assert.That(result.TryGetIdentifier(out uint n3) ? n3 : 0, Is.EqualTo(1234u));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));

            // Test with namespace index
            Assert.That(ExpandedNodeId.TryParse(context, "ns=2;s=Test", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out string s1) ? s1 : null, Is.EqualTo("Test"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));

            // Test with server URI - ServerUris table starts at index 0
            Assert.That(ExpandedNodeId.TryParse(context, "svu=urn:server1;i=1234", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out uint n1) ? n1 : 0, Is.EqualTo(1234u));
            Assert.That(result.ServerIndex, Is.Zero);  // First item in ServerUris is at index 0

            // Test with unknown namespace URI - ExpandedNodeId can store URIs not in the table
            // So this should succeed and create an ExpandedNodeId with the namespace URI
            Assert.That(ExpandedNodeId.TryParse(context, "nsu=http://unknown.org/;i=1234", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out uint n2) ? n2 : 0, Is.EqualTo(1234u));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://unknown.org/"));

            // Test with unknown server URI (should fail because ServerIndex must be resolved)
            Assert.That(ExpandedNodeId.TryParse(context, "svu=urn:unknown;i=1234", out result), Is.False);
            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));

            // Test null/empty
            Assert.That(ExpandedNodeId.TryParse(context, null, out result), Is.True);
            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));

            Assert.That(ExpandedNodeId.TryParse(context, string.Empty, out result), Is.True);
            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));
        }

        [Test]
        public void ConstructorUintNamespaceIndexWithNamespaceUri()
        {
            // with non-empty namespaceUri branch
            var id = new ExpandedNodeId(42u, 3, "http://test.org/", 5);
            Assert.That(id.ServerIndex, Is.EqualTo(5u));
            Assert.That(id.NamespaceUri, Is.EqualTo("http://test.org/"));
            // When namespaceUri is set, namespaceIndex is forced to 0
            Assert.That(id.NamespaceIndex, Is.Zero);
            Assert.That(id.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
        }

        [Test]
        public void ConstructorUintNamespaceIndexWithoutNamespaceUri()
        {
            // with empty namespaceUri branch
            var id = new ExpandedNodeId(42u, 3, null, 0);
            Assert.That(id.NamespaceUri, Is.Null);
            Assert.That(id.NamespaceIndex, Is.EqualTo(3));
        }

        [Test]
        public void ConstructorStringWithNamespaceUri()
        {
            var id = new ExpandedNodeId("Hello", "http://test.org/", 2);
            Assert.That(id.NamespaceUri, Is.EqualTo("http://test.org/"));
            Assert.That(id.ServerIndex, Is.EqualTo(2u));
            Assert.That(id.TryGetIdentifier(out string s), Is.True);
            Assert.That(s, Is.EqualTo("Hello"));
        }

        [Test]
        public void ConstructorStringWithEmptyNamespaceUri()
        {
            var id = new ExpandedNodeId("Hello", string.Empty, 0);
            Assert.That(id.NamespaceUri, Is.Null);
        }

        [Test]
        public void ConstructorStringNamespaceIndexWithUri()
        {
            // with namespaceUri set
            var id = new ExpandedNodeId("Test", 5, "http://ns.org/", 1);
            Assert.That(id.NamespaceUri, Is.EqualTo("http://ns.org/"));
            Assert.That(id.NamespaceIndex, Is.Zero);
            Assert.That(id.ServerIndex, Is.EqualTo(1u));
        }

        [Test]
        public void ConstructorStringNamespaceIndexWithoutUri()
        {
            // with null namespaceUri
            var id = new ExpandedNodeId("Test", 5, null, 0);
            Assert.That(id.NamespaceUri, Is.Null);
            Assert.That(id.NamespaceIndex, Is.EqualTo(5));
        }

        [Test]
        public void ConstructorGuidNamespaceIndexWithUri()
        {
            // with namespaceUri set
            var guid = Guid.NewGuid();
            var id = new ExpandedNodeId(guid, 3, "http://ns.org/", 2);
            Assert.That(id.NamespaceUri, Is.EqualTo("http://ns.org/"));
            Assert.That(id.NamespaceIndex, Is.Zero);
            Assert.That(id.ServerIndex, Is.EqualTo(2u));
            Assert.That(id.TryGetIdentifier(out Guid g), Is.True);
            Assert.That(g, Is.EqualTo(guid));
        }

        [Test]
        public void ConstructorGuidNamespaceIndexWithoutUri()
        {
            var guid = Guid.NewGuid();
            var id = new ExpandedNodeId(guid, 7, null, 0);
            Assert.That(id.NamespaceUri, Is.Null);
            Assert.That(id.NamespaceIndex, Is.EqualTo(7));
        }

        [Test]
        public void ConstructorByteStringNamespaceIndexWithUri()
        {
            ByteString bs = [1, 2, 3];
            var id = new ExpandedNodeId(bs, 4, "http://ns.org/", 1);
            Assert.That(id.NamespaceUri, Is.EqualTo("http://ns.org/"));
            Assert.That(id.NamespaceIndex, Is.Zero);
        }

        [Test]
        public void ConstructorByteStringNamespaceIndexWithoutUri()
        {
            ByteString bs = [1, 2, 3];
            var id = new ExpandedNodeId(bs, 4, null, 0);
            Assert.That(id.NamespaceUri, Is.Null);
            Assert.That(id.NamespaceIndex, Is.EqualTo(4));
        }

#pragma warning disable CS0618 // Type or member is obsolete
        [Test]
        public void ConstructorFromString()
        {
            var id = new ExpandedNodeId("i=100");
            Assert.That(id.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(100u));
        }

        [Test]
        public void ConstructorObjectIdentifier()
        {
            var id = new ExpandedNodeId((object)42u, 5, "http://ns.org/", 3);
            Assert.That(id.NamespaceUri, Is.EqualTo("http://ns.org/"));
            Assert.That(id.ServerIndex, Is.EqualTo(3u));
            // When namespaceUri is set, ns index forced to 0
            Assert.That(id.NamespaceIndex, Is.Zero);
        }

        [Test]
        public void ConstructorObjectIdentifierWithEmptyUri()
        {
            var id = new ExpandedNodeId((object)42u, 5, null, 0);
            Assert.That(id.NamespaceUri, Is.Null);
            Assert.That(id.NamespaceIndex, Is.EqualTo(5));
        }
#pragma warning restore CS0618 // Type or member is obsolete

        [Test]
        public void IsNullReturnsTrueForDefault()
        {
            Assert.That(ExpandedNodeId.Null.IsNull, Is.True);
            Assert.That(default(ExpandedNodeId).IsNull, Is.True);
        }

        [Test]
        public void IsAbsoluteWithNamespaceUri()
        {
            var id = new ExpandedNodeId(1u, "http://ns.org/");
            Assert.That(id.IsAbsolute, Is.True);
        }

        [Test]
        public void IsAbsoluteWithServerIndex()
        {
            var id = new ExpandedNodeId(1u, null, 1);
            Assert.That(id.IsAbsolute, Is.True);
        }

        [Test]
        public void NamespaceIndexForNull()
        {
            Assert.That(ExpandedNodeId.Null.NamespaceIndex, Is.Zero);
        }

        [Test]
        public void IdTypeForNull()
        {
            Assert.That(ExpandedNodeId.Null.IdType, Is.EqualTo(IdType.Numeric));
        }

        [Test]
        public void IdentifierForNull()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(ExpandedNodeId.Null.Identifier, Is.Null);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Test]
        public void IdentifierForNonNull()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var id = new ExpandedNodeId(42u);
            Assert.That(id.Identifier, Is.EqualTo(42u));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Test]
        public void WithInnerNode()
        {
            var original = new ExpandedNodeId(1u, "http://ns.org/", 2);
            var newInner = new NodeId(99u, 3);
            ExpandedNodeId result = original.WithInnerNode(newInner);

            Assert.That(result.ServerIndex, Is.EqualTo(2u));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org/"));
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(99u));
        }

        [Test]
        public void WithNamespaceUri()
        {
            var original = new ExpandedNodeId(42u, 5);
            ExpandedNodeId result = original.WithNamespaceUri("http://new.org/");
            Assert.That(result.NamespaceUri, Is.EqualTo("http://new.org/"));
            Assert.That(result.NamespaceIndex, Is.Zero);
        }

        [Test]
        public void WithServerIndex()
        {
            var original = new ExpandedNodeId(42u, "http://ns.org/");
            ExpandedNodeId result = original.WithServerIndex(10);
            Assert.That(result.ServerIndex, Is.EqualTo(10u));
        }

        [Test]
        public void CompareToNullWhenIsNull()
        {
            ExpandedNodeId id = ExpandedNodeId.Null;
            Assert.That(id.CompareTo(null), Is.Zero);
        }

        [Test]
        public void CompareToNullWhenNotNull()
        {
            var id = new ExpandedNodeId(42u);
            Assert.That(id.CompareTo(null), Is.LessThan(0));
        }

        [Test]
        public void CompareToNonAbsoluteNodeDelegatesToInner()
        {
            var id1 = new ExpandedNodeId(1u);
            var id2 = new ExpandedNodeId(2u);
            Assert.That(id1.CompareTo(id2), Is.LessThan(0));
            Assert.That(id2.CompareTo(id1), Is.GreaterThan(0));
        }

        [Test]
        public void CompareToExpandedNodeIdBothNull()
        {
            ExpandedNodeId id1 = ExpandedNodeId.Null;
            ExpandedNodeId id2 = ExpandedNodeId.Null;
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(id1.CompareTo((object)id2), Is.Zero);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void CompareToNodeIdBothNull()
        {
            // Make an absolute ExpandedNodeId that is also Null-ish
            ExpandedNodeId id = ExpandedNodeId.Null;
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(id.CompareTo((object)NodeId.Null), Is.Zero);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void CompareToExpandedNodeIdDifferentServerIndex()
        {
            var id1 = new ExpandedNodeId(1u, "http://ns.org/", 1);
            var id2 = new ExpandedNodeId(1u, "http://ns.org/", 2);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(id1.CompareTo((object)id2), Is.LessThan(0));
            Assert.That(id2.CompareTo((object)id1), Is.GreaterThan(0));
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void CompareToExpandedNodeIdDifferentNamespaceUri()
        {
            var id1 = new ExpandedNodeId(1u, "http://aaa.org/", 1);
            var id2 = new ExpandedNodeId(1u, "http://zzz.org/", 1);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(id1.CompareTo((object)id2), Is.LessThan(0));
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void CompareToExpandedNodeIdNullVsNonNullNamespaceUri()
        {
            var id1 = new ExpandedNodeId(1u, null, 1);
            var id2 = new ExpandedNodeId(1u, "http://ns.org/", 1);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(id1.CompareTo((object)id2), Is.LessThan(0));
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void CompareToExpandedNodeIdSameNamespaceComparesInnerNodeId()
        {
            var id1 = new ExpandedNodeId(1u, "http://ns.org/", 1);
            var id2 = new ExpandedNodeId(2u, "http://ns.org/", 1);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(id1.CompareTo((object)id2), Is.LessThan(0));
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void CompareToUnknownObjectType()
        {
            var id = new ExpandedNodeId(1u, "http://ns.org/", 1);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(id.CompareTo((object)"not a node id"), Is.Not.Zero);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void CompareToExpandedNullVsNonNullInnerNode()
        {
            // Create an absolute null expanded node id vs non-null
            var nullId = new ExpandedNodeId(NodeId.Null, null, 1);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var nonNullId = new ExpandedNodeId(42u, (string)null, 1);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(nullId.CompareTo((object)nonNullId), Is.LessThan(0));
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void OperatorGreaterThanOrEqual()
        {
            var id1 = new ExpandedNodeId(1u);
            var id2 = new ExpandedNodeId(2u);
            var id3 = new ExpandedNodeId(1u);
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(id2 >= id1, Is.True);
            Assert.That(id1 >= id3, Is.True);
            Assert.That(id1 >= id2, Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
        }

        [Test]
        public void OperatorLessThanOrEqual()
        {
            var id1 = new ExpandedNodeId(1u);
            var id2 = new ExpandedNodeId(2u);
            var id3 = new ExpandedNodeId(1u);
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(id1 <= id2, Is.True);
            Assert.That(id1 <= id3, Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(id2, Is.GreaterThan(id1));
        }

        [Test]
        public void EqualsObjectNull()
        {
            ExpandedNodeId nullId = ExpandedNodeId.Null;
#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(nullId.Equals((object)null));
#pragma warning restore IDE0004 // Remove Unnecessary Cast
#pragma warning restore NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            var nonNull = new ExpandedNodeId(1u);
#pragma warning disable NUnit4002 // Use Specific constraint
            Assert.That(nonNull, Is.Not.EqualTo((object)null));
#pragma warning restore NUnit4002 // Use Specific constraint
        }

        [Test]
        public void EqualsObjectUnknownType()
        {
            var id = new ExpandedNodeId(1u);
            Assert.That(id, Is.Not.EqualTo((object)"a string"));
        }

        [Test]
        public void EqualsNodeIdWhenAbsolute()
        {
            var id = new ExpandedNodeId(1u, "http://ns.org/");
            var nodeId = new NodeId(1u);
            Assert.That(id, Is.Not.EqualTo(nodeId));
        }

        [Test]
        public void EqualsNodeIdWhenNotAbsolute()
        {
            var id = new ExpandedNodeId(new NodeId(1u, 2));
            var nodeId = new NodeId(1u, 2);
            Assert.That(id, Is.EqualTo(nodeId));
        }

        [Test]
        public void EqualsNodeIdMismatch()
        {
            var id = new ExpandedNodeId(1u);
            var nodeId = new NodeId(2u);
            Assert.That(id, Is.Not.EqualTo(nodeId));
        }

        [Test]
        public void OperatorEqualsObject()
        {
            var id = new ExpandedNodeId(1u);
            Assert.That(id, Is.EqualTo((object)new ExpandedNodeId(1u)));
            Assert.That(id, Is.Not.EqualTo((object)new ExpandedNodeId(2u)));
        }

        [Test]
        public void OperatorNotEqualsObject()
        {
            var id = new ExpandedNodeId(1u);
            Assert.That(id, Is.Not.EqualTo((object)new ExpandedNodeId(2u)));
            Assert.That(id, Is.EqualTo((object)new ExpandedNodeId(1u)));
        }

        [Test]
        public void OperatorEqualsNodeId()
        {
            var eid = new ExpandedNodeId(new NodeId(1u));
            var nid = new NodeId(1u);
            Assert.That(eid, Is.EqualTo(nid));
        }

        [Test]
        public void OperatorNotEqualsNodeId()
        {
            var eid = new ExpandedNodeId(new NodeId(1u));
            var nid = new NodeId(2u);
            Assert.That(eid, Is.Not.EqualTo(nid));
        }

        [Test]
        public void ExplicitCastToNodeIdNull()
        {
            ExpandedNodeId id = ExpandedNodeId.Null;
            var result = (NodeId)id;
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ExplicitCastToNodeIdAbsoluteThrows()
        {
            var id = new ExpandedNodeId(1u, "http://ns.org/");
            Assert.That(() => { var _ = (NodeId)id; },
                Throws.TypeOf<InvalidCastException>());
        }

        [Test]
        public void ExplicitCastToNodeIdNonAbsolute()
        {
            var id = new ExpandedNodeId(new NodeId(42u, 3));
            var result = (NodeId)id;
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
            Assert.That(result.NamespaceIndex, Is.EqualTo(3));
        }

        [Test]
        public void ToStringWithInvalidFormatThrows()
        {
            var id = new ExpandedNodeId(1u);
            Assert.That(() => id.ToString("X", null),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ToStringWithNullFormat()
        {
            var id = new ExpandedNodeId(1u);
            string result = id.ToString(null, null);
            Assert.That(result, Is.EqualTo("i=1"));
        }

        [Test]
        public void FormatWithContextNullNodeId()
        {
            ExpandedNodeId id = ExpandedNodeId.Null;
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            Assert.That(id.Format(ctx), Is.Null);
        }

        [Test]
        public void FormatWithContextServerIndexNoUris()
        {
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var id = new ExpandedNodeId(1u, (string)null, 2);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            string result = id.Format(ctx, useUris: false);
            Assert.That(result, Does.StartWith("svr=2;"));
        }

        [Test]
        public void FormatWithContextServerIndexWithUrisResolved()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            ctx.ServerUris.Append("urn:placeholder");  // index 0
            ctx.ServerUris.Append("urn:server1");       // index 1
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var id = new ExpandedNodeId(1u, (string)null, 1);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            string result = id.Format(ctx, useUris: true);
            Assert.That(result, Does.Contain("svu="));
            Assert.That(result, Does.Contain("urn:server1"));
        }

        [Test]
        public void FormatWithContextServerIndexWithUrisUnresolved()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            // ServerIndex 5 has no matching URI in the table
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var id = new ExpandedNodeId(1u, (string)null, 5);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            string result = id.Format(ctx, useUris: true);
            Assert.That(result, Does.StartWith("svr=5;"));
        }

        [Test]
        public void ParseWithNamespaceTableNonAbsolute()
        {
            var nsTable = new NamespaceTable();
            NodeId result = ExpandedNodeId.Parse("ns=0;i=42", nsTable);
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
        }

        [Test]
        public void ParseWithNamespaceTableAbsoluteResolved()
        {
            var nsTable = new NamespaceTable();
            nsTable.Append("http://test.org/");
            NodeId result = ExpandedNodeId.Parse("nsu=http://test.org/;i=42", nsTable);
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
            Assert.That(result.NamespaceIndex, Is.GreaterThan(0));
        }

        [Test]
        public void ParseWithNamespaceTableAbsoluteUnresolvedThrows()
        {
            var nsTable = new NamespaceTable();
            Assert.That(
                () => ExpandedNodeId.Parse("nsu=http://unknown.org/;i=42", nsTable),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void FormatNullExpandedNodeIdReturnsEmpty()
        {
            Assert.That(ExpandedNodeId.Null.Format(null), Is.EqualTo(string.Empty));
        }

        [Test]
        public void FormatWithServerIndex()
        {
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var id = new ExpandedNodeId(1u, (string)null, 3);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            string result = id.Format(CultureInfo.InvariantCulture);
            Assert.That(result, Does.StartWith("svr=3;"));
        }

        [Test]
        public void FormatWithNamespaceUri()
        {
            var id = new ExpandedNodeId(1u, "http://ns.org/");
            string result = id.Format(CultureInfo.InvariantCulture);
            Assert.That(result, Does.Contain("nsu=http://ns.org/"));
        }

        [Test]
        public void FormatIntoBufferNullNodeId()
        {
            // but NamespaceUri is set — triggers the else branch
            var id = new ExpandedNodeId(NodeId.Null, "http://ns.org/", 1);
            var sb = new StringBuilder();
            id.Format(CultureInfo.InvariantCulture, sb);
            Assert.That(sb.ToString(), Does.Contain("svr=1"));
            Assert.That(sb.ToString(), Does.Contain("nsu=http://ns.org/"));
        }

        [Test]
        public void StaticFormatOverloadWithoutFormatProvider()
        {
            var sb = new StringBuilder();
#pragma warning disable CA1305 // Specify IFormatProvider
            ExpandedNodeId.Format(sb, "Hello", IdType.String, 2, "http://ns.org/", 1);
#pragma warning restore CA1305 // Specify IFormatProvider
            string result = sb.ToString();
            Assert.That(result, Does.Contain("svr=1;"));
            Assert.That(result, Does.Contain("nsu=http://ns.org/;"));
            Assert.That(result, Does.Contain("ns=2;s=Hello"));
        }

        [Test]
        public void ParseWithCurrentAndTargetNamespacesWithNamespaceIndex()
        {
            var current = new NamespaceTable();
            current.Append("http://source.org/");
            var target = new NamespaceTable();
            target.Append("http://source.org/");

            var result = ExpandedNodeId.Parse(
                "ns=1;i=42", current, target);
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void ParseWithCurrentAndTargetNamespacesWithUri()
        {
            var current = new NamespaceTable();
            var target = new NamespaceTable();
            target.Append("http://test.org/");

            var result = ExpandedNodeId.Parse(
                "nsu=http://test.org/;i=42", current, target);
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void ParseWithCurrentAndTargetNamespacesUnresolvedThrows()
        {
            var current = new NamespaceTable();
            var target = new NamespaceTable();
            Assert.That(
                () => ExpandedNodeId.Parse(
                    "nsu=http://unknown.org/;i=42", current, target),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseWithCurrentAndTargetNamespacesWithServerIndex()
        {
            var current = new NamespaceTable();
            var target = new NamespaceTable();
            target.Append("http://ns.org/");

            var result = ExpandedNodeId.Parse(
                "svr=1;nsu=http://ns.org/;i=42", current, target);
            Assert.That(result.ServerIndex, Is.EqualTo(1u));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org/"));
        }

        [Test]
        public void TryParseWithError()
        {
            bool success = ExpandedNodeId.TryParse("svr=;i=1", out _, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.Not.EqualTo(NodeIdParseError.None));
        }

        [Test]
        public void TryParseWithErrorValid()
        {
            bool success = ExpandedNodeId.TryParse("i=42", out ExpandedNodeId value, out NodeIdParseError error);
            Assert.That(success, Is.True);
            Assert.That(error, Is.EqualTo(NodeIdParseError.None));
            Assert.That(value.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
        }

        [Test]
        public void TryParseWithContextAndOptions()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            ctx.NamespaceUris.Append("http://test.org/");
            var options = new NodeIdParsingOptions { UpdateTables = false };
            bool success = ExpandedNodeId.TryParse(ctx, "nsu=http://test.org/;i=1", options, out ExpandedNodeId value);
            Assert.That(success, Is.True);
            Assert.That(value.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void TryParseWithContextOptionsAndError()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var options = new NodeIdParsingOptions { UpdateTables = false };

            bool success = ExpandedNodeId.TryParse(ctx, "i=42", options, out _, out NodeIdParseError error);
            Assert.That(success, Is.True);
            Assert.That(error, Is.EqualTo(NodeIdParseError.None));
        }

        [Test]
        public void TryParseWithContextOptionsAndErrorFailure()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());

            bool success = ExpandedNodeId.TryParse(ctx, "svu=;i=42", null, out _, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.Not.EqualTo(NodeIdParseError.None));
        }

        [Test]
        public void ParseWithContextSuccess()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            ctx.NamespaceUris.Append("http://test.org/");
            var result = ExpandedNodeId.Parse(ctx, "nsu=http://test.org/;i=42");
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
        }

        [Test]
        public void ParseWithContextFailureThrows()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            Assert.That(
                () => ExpandedNodeId.Parse(ctx, "svu=;i=42"),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseWithContextAndUpdateTablesOption()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var options = new NodeIdParsingOptions { UpdateTables = true };
            var result = ExpandedNodeId.Parse(ctx, "nsu=http://newns.org/;i=42", options);
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
            // The namespace should have been added to the table
            Assert.That(ctx.NamespaceUris.GetIndex("http://newns.org/"), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void TryParseInvalidServerIndexNoSemicolon()
        {
            bool success = ExpandedNodeId.TryParse("svr=1", out _);
            Assert.That(success, Is.False);
        }

        [Test]
        public void TryParseInvalidServerIndexNonNumeric()
        {
            bool success = ExpandedNodeId.TryParse("svr=abc;i=1", out _);
            Assert.That(success, Is.False);
        }

        [Test]
        public void TryParseInvalidNamespaceUriNoSemicolon()
        {
            bool success = ExpandedNodeId.TryParse("nsu=http://test.org/", out _);
            Assert.That(success, Is.False);
        }

        [Test]
        public void TryParseNamespaceUriWithEscapedCharacters()
        {
            bool success = ExpandedNodeId.TryParse(
    "nsu=http://test.org/my%3Bpath;i=42", out ExpandedNodeId value);
            Assert.That(success, Is.True);
            Assert.That(value.NamespaceUri, Is.EqualTo("http://test.org/my;path"));
        }

        [Test]
        public void TryParseCatchBlockOnInvalidEscape()
        {
            // A truncated percent-escape at end triggers ServiceResultException, caught → Unexpected
            bool success = ExpandedNodeId.TryParse("nsu=http://test%2;i=1", out _, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.Unexpected));
        }

        [Test]
        public void TryParseWithContextEmptyText()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(ctx, string.Empty, out ExpandedNodeId value);
            Assert.That(success, Is.True);
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void TryParseWithContextSvuNoSemicolon()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(ctx, "svu=urn:server", out _);
            Assert.That(success, Is.False);
        }

        [Test]
        public void TryParseWithContextSvuUnknownServerUri()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(
                ctx, "svu=urn:unknown;i=1", null, out _, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.NoServerUriMapping));
        }

        [Test]
        public void TryParseWithContextSvuUpdateTables()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var options = new NodeIdParsingOptions { UpdateTables = true };
            bool success = ExpandedNodeId.TryParse(
                ctx, "svu=urn:newserver;i=1", options, out _);
            Assert.That(success, Is.True);
            Assert.That(ctx.ServerUris.GetIndex("urn:newserver"), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void TryParseWithContextSvrFormat()
        {
            // Must pass options (even empty) to avoid NullReferenceException at line 1372
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var options = new NodeIdParsingOptions();
            bool success = ExpandedNodeId.TryParse(ctx, "svr=2;i=1", options, out ExpandedNodeId value);
            Assert.That(success, Is.True);
            Assert.That(value.ServerIndex, Is.EqualTo(2u));
        }

        [Test]
        public void TryParseWithContextSvrNoSemicolon()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(
                ctx, "svr=2", null, out _, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.InvalidServerUriFormat));
        }

        [Test]
        public void TryParseWithContextNsuNoSemicolon()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(
                ctx, "nsu=http://test.org/", null, out _, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.InvalidNamespaceFormat));
        }

        [Test]
        public void TryParseWithContextNsuNamespaceResolvedToIndex()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            ctx.NamespaceUris.Append("http://test.org/");
            bool success = ExpandedNodeId.TryParse(
                ctx, "nsu=http://test.org/;i=42", out ExpandedNodeId value);
            Assert.That(success, Is.True);
            Assert.That(value.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void TryParseWithContextNsuNamespaceNotResolved()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(
                ctx, "nsu=http://unknown.org/;i=42", out ExpandedNodeId value);
            Assert.That(success, Is.True);
            Assert.That(value.NamespaceUri, Is.EqualTo("http://unknown.org/"));
        }

        [Test]
        public void TryParseWithContextInvalidNodeIdFails()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(ctx, "i=notanumber", out _);
            Assert.That(success, Is.False);
        }

        [Test]
        public void GetHashCodeForNullReturnsZero()
        {
            Assert.That(ExpandedNodeId.Null.GetHashCode(), Is.Zero);
        }

        [Test]
        public void GetHashCodeForNonAbsolute()
        {
            var id = new ExpandedNodeId(42u);
            Assert.That(id.GetHashCode(), Is.EqualTo(new NodeId(42u).GetHashCode()));
        }

        [Test]
        public void GetHashCodeForAbsoluteWithServerIndex()
        {
            var id = new ExpandedNodeId(42u, "http://ns.org/", 3);
            int hash = id.GetHashCode();
            Assert.That(hash, Is.Not.Zero);
        }

        [Test]
        public void GetHashCodeForAbsoluteWithOnlyServerIndex()
        {
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var id = new ExpandedNodeId(42u, (string)null, 3);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            int hash = id.GetHashCode();
            Assert.That(hash, Is.Not.Zero);
        }

        [Test]
        public void ToNodeIdNullExpandedNodeId()
        {
            var result = ExpandedNodeId.ToNodeId(ExpandedNodeId.Null, null);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ToNodeIdNonAbsoluteReturnsInner()
        {
            var id = new ExpandedNodeId(new NodeId(42u));
            var result = ExpandedNodeId.ToNodeId(id, null);
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
        }

        [Test]
        public void ToNodeIdWithNullNamespaceTable()
        {
            var id = new ExpandedNodeId(42u, "http://ns.org/");
            var result = ExpandedNodeId.ToNodeId(id, null);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ToNodeIdWithNamespaceTableNotFound()
        {
            var nsTable = new NamespaceTable();
            var id = new ExpandedNodeId(42u, "http://unknown.org/");
            var result = ExpandedNodeId.ToNodeId(id, nsTable);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ToNodeIdWithNamespaceTableFound()
        {
            var nsTable = new NamespaceTable();
            nsTable.Append("http://ns.org/");
            var id = new ExpandedNodeId(42u, "http://ns.org/");
            var result = ExpandedNodeId.ToNodeId(id, nsTable);
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.NamespaceIndex, Is.GreaterThan(0));
        }

        [Test]
        public void SerializableDefaultConstructor()
        {
            var s = new SerializableExpandedNodeId();
            Assert.That(s.Value.IsNull, Is.True);
        }

        [Test]
        public void SerializableValueConstructor()
        {
            var eid = new ExpandedNodeId(42u);
            var s = new SerializableExpandedNodeId(eid);
            Assert.That(s.Value, Is.EqualTo(eid));
        }

        [Test]
        public void SerializableEqualsObject()
        {
            var eid = new ExpandedNodeId(42u);
            var s1 = new SerializableExpandedNodeId(eid);
            var s2 = new SerializableExpandedNodeId(eid);

            // SerializableExpandedNodeId case
            Assert.That(s1, Is.EqualTo((object)s2));
            // ExpandedNodeId case
            Assert.That(s1, Is.EqualTo((object)eid));
            // Other case (falls through to Value.Equals)
            Assert.That(s1, Is.Not.EqualTo((object)"not a node"));
        }

        [Test]
        public void SerializableEqualsExpandedNodeId()
        {
            var eid = new ExpandedNodeId(42u);
            var s = new SerializableExpandedNodeId(eid);
            Assert.That(s, Is.EqualTo(eid));
            Assert.That(s, Is.Not.EqualTo(new ExpandedNodeId(99u)));
        }

        [Test]
        public void SerializableEqualsSerializableExpandedNodeId()
        {
            var s1 = new SerializableExpandedNodeId(new ExpandedNodeId(42u));
            var s2 = new SerializableExpandedNodeId(new ExpandedNodeId(42u));
            SerializableExpandedNodeId nullS = null;
            Assert.That(s1, Is.EqualTo(s2));
#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(s1, Is.Not.EqualTo(nullS));
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void SerializableGetHashCode()
        {
            var eid = new ExpandedNodeId(42u);
            var s = new SerializableExpandedNodeId(eid);
            Assert.That(s.GetHashCode(), Is.EqualTo(eid.GetHashCode()));
        }

        [Test]
        public void SerializableOperatorEqualsBothSerializable()
        {
            var s1 = new SerializableExpandedNodeId(new ExpandedNodeId(42u));
            var s2 = new SerializableExpandedNodeId(new ExpandedNodeId(42u));
            SerializableExpandedNodeId nullS = null;
            Assert.That(s1, Is.EqualTo(s2));
#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(nullS, Is.Not.EqualTo(s1));
#pragma warning disable NUnit4002 // Use Specific constraint
            Assert.That(nullS, Is.EqualTo((SerializableExpandedNodeId)null));
#pragma warning restore NUnit4002 // Use Specific constraint
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void SerializableOperatorNotEqualsBothSerializable()
        {
            var s1 = new SerializableExpandedNodeId(new ExpandedNodeId(42u));
            var s2 = new SerializableExpandedNodeId(new ExpandedNodeId(99u));
            Assert.That(s1, Is.Not.EqualTo(s2));
        }

        [Test]
        public void SerializableOperatorEqualsWithExpandedNodeId()
        {
            var eid = new ExpandedNodeId(42u);
            var s = new SerializableExpandedNodeId(eid);
            SerializableExpandedNodeId nullS = null;
            Assert.That(s, Is.EqualTo(eid));
#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(nullS, Is.Not.EqualTo(new ExpandedNodeId(42u)));
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void SerializableOperatorNotEqualsWithExpandedNodeId()
        {
            var s = new SerializableExpandedNodeId(new ExpandedNodeId(42u));
            Assert.That(s, Is.Not.EqualTo(new ExpandedNodeId(99u)));
        }

        [Test]
        public void SerializableImplicitFromExpandedNodeId()
        {
            SerializableExpandedNodeId s = new ExpandedNodeId(42u);
            Assert.That(s.Value, Is.EqualTo(new ExpandedNodeId(42u)));
        }

        [Test]
        public void SerializableImplicitToExpandedNodeId()
        {
            ExpandedNodeId eid = new SerializableExpandedNodeId(new ExpandedNodeId(42u));
            Assert.That(eid, Is.EqualTo(new ExpandedNodeId(42u)));
        }

        [Test]
        public void SerializableExplicitToString()
        {
            var s = new SerializableExpandedNodeId(new ExpandedNodeId(42u));
            string str = (string)s;
            Assert.That(str, Is.EqualTo("i=42"));
        }

        [Test]
        public void SerializableExplicitFromString()
        {
            var s = (SerializableExpandedNodeId)"i=42";
            Assert.That(s.Value.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
        }

        [Test]
        public void ImplicitConversionFromNodeId()
        {
            ExpandedNodeId eid = new NodeId(42u, 3);
            Assert.That(eid.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
            Assert.That(eid.NamespaceIndex, Is.EqualTo(3));
        }

        [Test]
        public void ImplicitConversionFromUint()
        {
            ExpandedNodeId eid = 99u;
            Assert.That(eid.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(99u));
        }

        [Test]
        public void ImplicitConversionFromGuid()
        {
            var guid = Guid.NewGuid();
            ExpandedNodeId eid = guid;
            Assert.That(eid.TryGetIdentifier(out Guid g), Is.True);
            Assert.That(g, Is.EqualTo(guid));
        }

        [Test]
        public void ExplicitConversionFromByteString()
        {
            ByteString bs = [1, 2, 3];
            var eid = (ExpandedNodeId)bs;
            Assert.That(eid.TryGetIdentifier(out ByteString result), Is.True);
            Assert.That(result, Is.EqualTo(bs));
        }

        [Test]
        public void EqualsExpandedNodeIdDifferentServerIndex()
        {
            var id1 = new ExpandedNodeId(1u, "http://ns.org/", 1);
            var id2 = new ExpandedNodeId(1u, "http://ns.org/", 2);
            Assert.That(id1, Is.Not.EqualTo(id2));
        }

        [Test]
        public void EqualsExpandedNodeIdDifferentNamespaceUri()
        {
            var id1 = new ExpandedNodeId(1u, "http://a.org/", 1);
            var id2 = new ExpandedNodeId(1u, "http://b.org/", 1);
            Assert.That(id1, Is.Not.EqualTo(id2));
        }

        [Test]
        public void EqualsExpandedNodeIdDifferentInnerNodeId()
        {
            var id1 = new ExpandedNodeId(1u, "http://ns.org/", 1);
            var id2 = new ExpandedNodeId(2u, "http://ns.org/", 1);
            Assert.That(id1, Is.Not.EqualTo(id2));
        }

        [Test]
        public void EqualsExpandedNodeIdAllSame()
        {
            var id1 = new ExpandedNodeId(1u, "http://ns.org/", 1);
            var id2 = new ExpandedNodeId(1u, "http://ns.org/", 1);
            Assert.That(id1, Is.EqualTo(id2));
        }

        [Test]
        public void EqualsExpandedNodeIdBothNull()
        {
#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            Assert.That(ExpandedNodeId.Null.Equals(ExpandedNodeId.Null), Is.True);
#pragma warning restore NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
        }

        [Test]
        public void FormatAndParseRoundTripWithEscapedNamespaceUri()
        {
            // and parse lines 1277-1280
            var id = new ExpandedNodeId(42u, "http://ns.org/path;with;semicolons");
            string formatted = id.Format(CultureInfo.InvariantCulture);
            Assert.That(formatted, Does.Contain("nsu=http://ns.org/path%3Bwith%3Bsemicolons;"));

            var parsed = ExpandedNodeId.Parse(formatted);
            Assert.That(parsed.NamespaceUri, Is.EqualTo("http://ns.org/path;with;semicolons"));
            Assert.That(parsed.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
        }

        [Test]
        public void FormatAndParseRoundTripWithPercentInUri()
        {
            var id = new ExpandedNodeId(1u, "http://ns.org/100%done");
            string formatted = id.Format(CultureInfo.InvariantCulture);
            Assert.That(formatted, Does.Contain("%25"));

            var parsed = ExpandedNodeId.Parse(formatted);
            Assert.That(parsed.NamespaceUri, Is.EqualTo("http://ns.org/100%done"));
        }

        [Test]
        public void CompareToNodeIdNotNullWhenAbsolute()
        {
            // Falls through to line 503 comparison
            var id = new ExpandedNodeId(10u, "http://ns.org/", 1);
            var nodeId = new NodeId(5u);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            int cmp = id.CompareTo((object)nodeId);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            Assert.That(cmp, Is.Not.Zero);
        }

        [Test]
        public void OperatorNotEqualsExpandedNodeId()
        {
            var id1 = new ExpandedNodeId(1u);
            var id2 = new ExpandedNodeId(2u);
            Assert.That(id1, Is.Not.EqualTo(id2));
            Assert.That(id1, Is.EqualTo(new ExpandedNodeId(1u)));
        }

        [Test]
        public void TryParseInvalidFirstHexDigitInEscape()
        {
            bool success = ExpandedNodeId.TryParse("nsu=http://test%XZ;i=1", out _, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.Unexpected));
        }

        [Test]
        public void TryParseInvalidSecondHexDigitInEscape()
        {
            bool success = ExpandedNodeId.TryParse("nsu=http://test%3X;i=1", out _, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.Unexpected));
        }

        [Test]
        public void TryParseWithContextSvrWithServerMappings()
        {
            var ctx = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            var options = new NodeIdParsingOptions
            {
                ServerMappings = [0, 5, 10],
                NamespaceMappings = [0, 1, 2, 3]
            };
            bool success = ExpandedNodeId.TryParse(ctx, "svr=2;i=42", options, out _);
            Assert.That(success, Is.True);
        }

        [Test]
        public void SerializableGetValue()
        {
            var eid = new ExpandedNodeId(42u);
            var s = new SerializableExpandedNodeId(eid);
            object val = s.GetValue();
            Assert.That(val, Is.EqualTo(eid));
        }

        [Test]
        public void CompareToAbsoluteNullNodeIdWithNonNullNodeId()
        {
            var nullAbsolute = new ExpandedNodeId(NodeId.Null, null, 1);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var other = new ExpandedNodeId(42u, (string)null, 1);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            int cmp = nullAbsolute.CompareTo((object)other);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            Assert.That(cmp, Is.LessThan(0));
        }

        [Test]
        public void CompareToAbsoluteWithNonNullNodeId()
        {
            var id1 = new ExpandedNodeId(10u, "http://ns.org/", 1);
            var id2 = new ExpandedNodeId(20u, "http://ns.org/", 1);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(id1.CompareTo((object)id2), Is.LessThan(0));
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void ParseEmptyStringReturnsNull()
        {
            var result = ExpandedNodeId.Parse(string.Empty);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ParseNullStringReturnsNull()
        {
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var result = ExpandedNodeId.Parse((string)null);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ParseWithServerAndNamespaceUri()
        {
            var result = ExpandedNodeId.Parse("svr=2;nsu=http://test.org/;i=42");
            Assert.That(result.ServerIndex, Is.EqualTo(2u));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://test.org/"));
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
        }

        [Test]
        public void ParseInvalidTextThrows()
        {
            Assert.That(
    () => ExpandedNodeId.Parse("invalid_text"),
    Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ConstructorNodeIdWithNamespaceUri()
        {
            var nodeId = new NodeId(42u, 5);
            var eid = new ExpandedNodeId(nodeId, "http://ns.org/", 0);
            Assert.That(eid.NamespaceUri, Is.EqualTo("http://ns.org/"));
            // NamespaceIndex is forced to 0 when URI is provided
            Assert.That(eid.NamespaceIndex, Is.Zero);
        }

        [Test]
        public void ConstructorNodeIdWithNullNamespaceUri()
        {
            var nodeId = new NodeId(42u, 5);
            var eid = new ExpandedNodeId(nodeId, null, 0);
            Assert.That(eid.NamespaceUri, Is.Null);
            Assert.That(eid.NamespaceIndex, Is.EqualTo(5));
        }

        [Test]
        public void ConstructorGuidWithNamespaceUri()
        {
            var guid = Guid.NewGuid();
            var id = new ExpandedNodeId(guid, "http://ns.org/", 0);
            Assert.That(id.NamespaceUri, Is.EqualTo("http://ns.org/"));
        }

        [Test]
        public void ConstructorByteStringWithNamespaceUri()
        {
            ByteString bs = [1, 2];
            var id = new ExpandedNodeId(bs, "http://ns.org/", 0);
            Assert.That(id.NamespaceUri, Is.EqualTo("http://ns.org/"));
        }

        private const string ParseLongFormKnownNamespace= "http://opcfoundation.org/UA/Test/";
        private const string ParseLongFormUnknownNamespace = "http://opcfoundation.org/UA/Unknown/";
        private const string ParseLongFormKnownServer = "urn:server:known";

        private static NamespaceTable BuildParseLongFormNamespaces()
        {
            var table = new NamespaceTable();
            table.Append(ParseLongFormKnownNamespace);
            return table;
        }

        private static StringTable BuildParseLongFormServers()
        {
            var table = new StringTable();
            table.Append(ParseLongFormKnownServer);
            return table;
        }

        private static uint GetParseLongFormUInt(ExpandedNodeId id)
        {
            Assert.That(id.TryGetIdentifier(out uint v), Is.True);
            return v;
        }

        [Test]
        public void ParseLongFormThrowsWhenTableIsNull()
        {
            Assert.That(
                () => ExpandedNodeId.ParseLongForm("i=1", null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ParseLongFormBareIdentifier()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            ExpandedNodeId result = ExpandedNodeId.ParseLongForm("i=10", table);
            Assert.That(result.NamespaceIndex, Is.EqualTo(0));
            Assert.That(GetParseLongFormUInt(result), Is.EqualTo((uint)10));
            Assert.That(result.IsAbsolute, Is.False);
        }

        [Test]
        public void ParseLongFormResolvesKnownNamespaceUri()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            ExpandedNodeId result = ExpandedNodeId.ParseLongForm(
                $"nsu={ParseLongFormKnownNamespace};i=11", table);
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
            Assert.That(GetParseLongFormUInt(result), Is.EqualTo((uint)11));
            Assert.That(result.IsAbsolute, Is.False);
        }

        [Test]
        public void ParseLongFormThrowsForUnresolvedNamespaceUri()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            Assert.That(
                () => ExpandedNodeId.ParseLongForm(
                    $"nsu={ParseLongFormUnknownNamespace};i=1", table),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void ParseDefaultsAcceptUnresolvedNamespaceUri()
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(null);
            context.NamespaceUris = BuildParseLongFormNamespaces();
            ExpandedNodeId result = ExpandedNodeId.Parse(
                context, $"nsu={ParseLongFormUnknownNamespace};i=1");
            Assert.That(result.IsAbsolute, Is.True);
            Assert.That(result.NamespaceUri, Is.EqualTo(ParseLongFormUnknownNamespace));
            Assert.That(GetParseLongFormUInt(result), Is.EqualTo((uint)1));
        }

        [Test]
        public void ParseLongFormResolvesKnownServerUri()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            StringTable servers = BuildParseLongFormServers();
            ExpandedNodeId result = ExpandedNodeId.ParseLongForm(
                $"svu={ParseLongFormKnownServer};nsu={ParseLongFormKnownNamespace};i=5",
                table,
                servers);
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
            Assert.That(GetParseLongFormUInt(result), Is.EqualTo((uint)5));
            Assert.That(result.ServerIndex, Is.EqualTo((uint)0));
        }

        [Test]
        public void ParseLongFormThrowsForUnresolvedServerUri()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            StringTable servers = BuildParseLongFormServers();
            Assert.That(
                () => ExpandedNodeId.ParseLongForm(
                    $"svu=urn:other;nsu={ParseLongFormKnownNamespace};i=5",
                    table,
                    servers),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void ParseLongFormRejectsServerUriWithoutTable()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            Assert.That(
                () => ExpandedNodeId.ParseLongForm(
                    $"svu={ParseLongFormKnownServer};nsu={ParseLongFormKnownNamespace};i=5",
                    table,
                    serverUris: null),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void ParseLongFormServerIndexPrefix()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            ExpandedNodeId result = ExpandedNodeId.ParseLongForm(
                $"svr=2;nsu={ParseLongFormKnownNamespace};i=8", table);
            Assert.That(result.ServerIndex, Is.EqualTo((uint)2));
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
            Assert.That(GetParseLongFormUInt(result), Is.EqualTo((uint)8));
        }

        [Test]
        public void ParseLongFormRoundTrip()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            StringTable servers = BuildParseLongFormServers();
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(null);
            context.NamespaceUris = table;
            context.ServerUris = servers;

            var original = new ExpandedNodeId((uint)42, ParseLongFormKnownNamespace, 0);
            string formatted = original.Format(context, useUris: true);
            ExpandedNodeId parsed = ExpandedNodeId.ParseLongForm(formatted, table, servers);

            Assert.That(GetParseLongFormUInt(parsed), Is.EqualTo((uint)42));
            Assert.That(parsed.NamespaceIndex, Is.EqualTo(1));
            Assert.That(parsed.ServerIndex, Is.EqualTo((uint)0));
        }
    }
}
