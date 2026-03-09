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
    /// <summary>
    /// Coverage-driven tests for <see cref="ExpandedNodeId"/> and
    /// <see cref="SerializableExpandedNodeId"/>.
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
            Assert.That(nodeId2 < inodeId2, Is.False);
            Assert.That(nodeId2 == inodeId2, Is.True);
            Assert.That(nodeId2 > inodeId2, Is.False);

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

            Assert.That(nodeIdText < inodeIdText, Is.False);
            Assert.That(nodeIdText == inodeIdText, Is.True);
            Assert.That(nodeIdText == inodeIdText2, Is.True);
            Assert.That(nodeIdText > inodeIdText, Is.False);

            Assert.That(nodeIdText > nodeId2, Is.True);
            Assert.That(nodeIdText == nodeId2, Is.False);
            Assert.That(nodeIdText < nodeId2, Is.False);
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
            Assert.That(nodeId == id, Is.True);

            id = (ExpandedNodeId)"s=Test";
            nodeId = NodeId.Parse("s=Test");
            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(0));
            Assert.That(nodeId.IdentifierAsString, Is.EqualTo("Test"));
            Assert.That(nodeId.TryGetIdentifier(out string s4) ? s4 : null, Is.EqualTo("Test"));
            Assert.That(nodeId.ToString(), Is.EqualTo("s=Test"));
            Assert.That(nodeId, Is.EqualTo(id));
            Assert.That(nodeId == id, Is.True);
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
            Assert.That(id.NamespaceIndex, Is.EqualTo(0));
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
            Assert.That(result.NamespaceIndex, Is.EqualTo(0));

            Assert.That(ExpandedNodeId.TryParse("ns=2;i=1234", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out uint n2) ? n2 : 0, Is.EqualTo(1234u));
            Assert.That(result.IdType, Is.EqualTo(IdType.Numeric));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));

            // Test string identifiers
            Assert.That(ExpandedNodeId.TryParse("s=HelloWorld", out result), Is.True);
            Assert.That(result.TryGetIdentifier(out string s1) ? s1 : null, Is.EqualTo("HelloWorld"));
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.NamespaceIndex, Is.EqualTo(0));

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
            Assert.That(ExpandedNodeId.TryParse("invalid", out ExpandedNodeId result), Is.False );
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
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create())
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
            Assert.That(result.ServerIndex, Is.EqualTo(0u));  // First item in ServerUris is at index 0

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
            // Covers lines 118-128: uint,ushort,namespaceUri,serverIndex ctor
            // with non-empty namespaceUri branch
            var id = new ExpandedNodeId(42u, 3, "http://test.org/", 5);
            Assert.That(id.ServerIndex, Is.EqualTo(5u));
            Assert.That(id.NamespaceUri, Is.EqualTo("http://test.org/"));
            // When namespaceUri is set, namespaceIndex is forced to 0
            Assert.That(id.NamespaceIndex, Is.EqualTo(0));
            Assert.That(id.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
        }

        [Test]
        public void ConstructorUintNamespaceIndexWithoutNamespaceUri()
        {
            // Covers lines 118-128: uint,ushort,namespaceUri,serverIndex ctor
            // with empty namespaceUri branch
            var id = new ExpandedNodeId(42u, 3, null, 0);
            Assert.That(id.NamespaceUri, Is.Null);
            Assert.That(id.NamespaceIndex, Is.EqualTo(3));
        }

        [Test]
        public void ConstructorStringWithNamespaceUri()
        {
            // Covers lines 140-149: string,namespaceUri,serverIndex ctor
            var id = new ExpandedNodeId("Hello", "http://test.org/", 2);
            Assert.That(id.NamespaceUri, Is.EqualTo("http://test.org/"));
            Assert.That(id.ServerIndex, Is.EqualTo(2u));
            Assert.That(id.TryGetIdentifier(out string s), Is.True);
            Assert.That(s, Is.EqualTo("Hello"));
        }

        [Test]
        public void ConstructorStringWithEmptyNamespaceUri()
        {
            // Covers lines 145-146: empty namespaceUri branch
            var id = new ExpandedNodeId("Hello", "", 0);
            Assert.That(id.NamespaceUri, Is.Null);
        }

        [Test]
        public void ConstructorStringNamespaceIndexWithUri()
        {
            // Covers lines 163-179: string,ushort,namespaceUri,serverIndex ctor
            // with namespaceUri set
            var id = new ExpandedNodeId("Test", 5, "http://ns.org/", 1);
            Assert.That(id.NamespaceUri, Is.EqualTo("http://ns.org/"));
            Assert.That(id.NamespaceIndex, Is.EqualTo(0));
            Assert.That(id.ServerIndex, Is.EqualTo(1u));
        }

        [Test]
        public void ConstructorStringNamespaceIndexWithoutUri()
        {
            // Covers lines 163-179: string,ushort,namespaceUri,serverIndex ctor
            // with null namespaceUri
            var id = new ExpandedNodeId("Test", 5, null, 0);
            Assert.That(id.NamespaceUri, Is.Null);
            Assert.That(id.NamespaceIndex, Is.EqualTo(5));
        }

        [Test]
        public void ConstructorGuidNamespaceIndexWithUri()
        {
            // Covers lines 212-229: Guid,ushort,namespaceUri,serverIndex ctor
            // with namespaceUri set
            var guid = Guid.NewGuid();
            var id = new ExpandedNodeId(guid, 3, "http://ns.org/", 2);
            Assert.That(id.NamespaceUri, Is.EqualTo("http://ns.org/"));
            Assert.That(id.NamespaceIndex, Is.EqualTo(0));
            Assert.That(id.ServerIndex, Is.EqualTo(2u));
            Assert.That(id.TryGetIdentifier(out Guid g), Is.True);
            Assert.That(g, Is.EqualTo(guid));
        }

        [Test]
        public void ConstructorGuidNamespaceIndexWithoutUri()
        {
            // Covers lines 219-222: Guid,ushort ctor with null uri
            var guid = Guid.NewGuid();
            var id = new ExpandedNodeId(guid, 7, null, 0);
            Assert.That(id.NamespaceUri, Is.Null);
            Assert.That(id.NamespaceIndex, Is.EqualTo(7));
        }

        [Test]
        public void ConstructorByteStringNamespaceIndexWithUri()
        {
            // Covers lines 260-277: ByteString,ushort,namespaceUri,serverIndex ctor
            ByteString bs = [1, 2, 3];
            var id = new ExpandedNodeId(bs, 4, "http://ns.org/", 1);
            Assert.That(id.NamespaceUri, Is.EqualTo("http://ns.org/"));
            Assert.That(id.NamespaceIndex, Is.EqualTo(0));
        }

        [Test]
        public void ConstructorByteStringNamespaceIndexWithoutUri()
        {
            // Covers lines 267-270: ByteString,ushort ctor with null uri
            ByteString bs = [1, 2, 3];
            var id = new ExpandedNodeId(bs, 4, null, 0);
            Assert.That(id.NamespaceUri, Is.Null);
            Assert.That(id.NamespaceIndex, Is.EqualTo(4));
        }

#pragma warning disable CS0618 // Type or member is obsolete
        [Test]
        public void ConstructorFromString()
        {
            // Covers lines 286-288: obsolete string ctor calling Parse
            var id = new ExpandedNodeId("i=100");
            Assert.That(id.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(100u));
        }

        [Test]
        public void ConstructorObjectIdentifier()
        {
            // Covers lines 300-311: obsolete object identifier ctor
            var id = new ExpandedNodeId((object)42u, 5, "http://ns.org/", 3);
            Assert.That(id.NamespaceUri, Is.EqualTo("http://ns.org/"));
            Assert.That(id.ServerIndex, Is.EqualTo(3u));
            // When namespaceUri is set, ns index forced to 0
            Assert.That(id.NamespaceIndex, Is.EqualTo(0));
        }

        [Test]
        public void ConstructorObjectIdentifierWithEmptyUri()
        {
            // Covers lines 306-310: null namespaceUri branch
            var id = new ExpandedNodeId((object)42u, 5, null, 0);
            Assert.That(id.NamespaceUri, Is.Null);
            Assert.That(id.NamespaceIndex, Is.EqualTo(5));
        }
#pragma warning restore CS0618 // Type or member is obsolete

        [Test]
        public void IsNullReturnsTrueForDefault()
        {
            // Covers line 326: IsNull property
            Assert.That(ExpandedNodeId.Null.IsNull, Is.True);
            Assert.That(default(ExpandedNodeId).IsNull, Is.True);
        }

        [Test]
        public void IsAbsoluteWithNamespaceUri()
        {
            // Covers line 333: IsAbsolute with namespaceUri
            var id = new ExpandedNodeId(1u, "http://ns.org/");
            Assert.That(id.IsAbsolute, Is.True);
        }

        [Test]
        public void IsAbsoluteWithServerIndex()
        {
            // Covers line 333: IsAbsolute with serverIndex > 0
            var id = new ExpandedNodeId(1u, null, 1);
            Assert.That(id.IsAbsolute, Is.True);
        }

        [Test]
        public void NamespaceIndexForNull()
        {
            // Covers line 339 partial branch: null nodeId returns 0
            Assert.That(ExpandedNodeId.Null.NamespaceIndex, Is.EqualTo(0));
        }

        [Test]
        public void IdTypeForNull()
        {
            // Covers line 345 partial branch: null nodeId returns Numeric
            Assert.That(ExpandedNodeId.Null.IdType, Is.EqualTo(IdType.Numeric));
        }

        [Test]
        public void IdentifierForNull()
        {
            // Covers line 357: Identifier property null branch
#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(ExpandedNodeId.Null.Identifier, Is.Null);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Test]
        public void IdentifierForNonNull()
        {
            // Covers line 357: Identifier property non-null branch
#pragma warning disable CS0618 // Type or member is obsolete
            var id = new ExpandedNodeId(42u);
            Assert.That(id.Identifier, Is.EqualTo(42u));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Test]
        public void WithInnerNode()
        {
            // Covers lines 444-449: WithInnerNode method
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
            // Covers lines 419-425: WithNamespaceUri method
            var original = new ExpandedNodeId(42u, 5);
            ExpandedNodeId result = original.WithNamespaceUri("http://new.org/");
            Assert.That(result.NamespaceUri, Is.EqualTo("http://new.org/"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(0));
        }

        [Test]
        public void WithServerIndex()
        {
            // Covers lines 431-437: WithServerIndex method
            var original = new ExpandedNodeId(42u, "http://ns.org/");
            ExpandedNodeId result = original.WithServerIndex(10);
            Assert.That(result.ServerIndex, Is.EqualTo(10u));
        }

        [Test]
        public void CompareToNullWhenIsNull()
        {
            // Covers lines 455-457: CompareTo(null) when IsNull → return 0
            ExpandedNodeId id = ExpandedNodeId.Null;
            Assert.That(id.CompareTo(null), Is.EqualTo(0));
        }

        [Test]
        public void CompareToNullWhenNotNull()
        {
            // Covers lines 455-457: CompareTo(null) when not IsNull → return -1
            var id = new ExpandedNodeId(42u);
            Assert.That(id.CompareTo(null), Is.LessThan(0));
        }

        [Test]
        public void CompareToNonAbsoluteNodeDelegatesToInner()
        {
            // Covers lines 461-463: !IsAbsolute && !m_nodeId.IsNull → m_nodeId.CompareTo
            var id1 = new ExpandedNodeId(1u);
            var id2 = new ExpandedNodeId(2u);
            Assert.That(id1.CompareTo(id2), Is.LessThan(0));
            Assert.That(id2.CompareTo(id1), Is.GreaterThan(0));
        }

        [Test]
        public void CompareToExpandedNodeIdBothNull()
        {
            // Covers lines 473-477: both null ExpandedNodeIds → return 0
            ExpandedNodeId id1 = ExpandedNodeId.Null;
            ExpandedNodeId id2 = ExpandedNodeId.Null;
            Assert.That(id1.CompareTo((object)id2), Is.EqualTo(0));
        }

        [Test]
        public void CompareToNodeIdBothNull()
        {
            // Covers lines 466-471: obj is NodeId, both IsNull → return 0
            // Make an absolute ExpandedNodeId that is also Null-ish
            ExpandedNodeId id = ExpandedNodeId.Null;
            Assert.That(id.CompareTo((object)NodeId.Null), Is.EqualTo(0));
        }

        [Test]
        public void CompareToExpandedNodeIdDifferentServerIndex()
        {
            // Covers lines 480-482: different ServerIndex
            var id1 = new ExpandedNodeId(1u, "http://ns.org/", 1);
            var id2 = new ExpandedNodeId(1u, "http://ns.org/", 2);
            Assert.That(id1.CompareTo((object)id2), Is.LessThan(0));
            Assert.That(id2.CompareTo((object)id1), Is.GreaterThan(0));
        }

        [Test]
        public void CompareToExpandedNodeIdDifferentNamespaceUri()
        {
            // Covers lines 485-492: different NamespaceUri
            var id1 = new ExpandedNodeId(1u, "http://aaa.org/", 1);
            var id2 = new ExpandedNodeId(1u, "http://zzz.org/", 1);
            Assert.That(id1.CompareTo((object)id2), Is.LessThan(0));
        }

        [Test]
        public void CompareToExpandedNodeIdNullVsNonNullNamespaceUri()
        {
            // Covers lines 487-492: NamespaceUri is null on one side → return -1
            var id1 = new ExpandedNodeId(1u, null, 1);
            var id2 = new ExpandedNodeId(1u, "http://ns.org/", 1);
            Assert.That(id1.CompareTo((object)id2), Is.LessThan(0));
        }

        [Test]
        public void CompareToExpandedNodeIdSameNamespaceComparesInnerNodeId()
        {
            // Covers line 495 + 503-505: same namespace → compare inner node ids
            var id1 = new ExpandedNodeId(1u, "http://ns.org/", 1);
            var id2 = new ExpandedNodeId(2u, "http://ns.org/", 1);
            Assert.That(id1.CompareTo((object)id2), Is.LessThan(0));
        }

        [Test]
        public void CompareToUnknownObjectType()
        {
            // Covers lines 498-500: obj is neither NodeId nor ExpandedNodeId
            var id = new ExpandedNodeId(1u, "http://ns.org/", 1);
            Assert.That(id.CompareTo((object)"not a node id"), Is.Not.EqualTo(0));
        }

        [Test]
        public void CompareToExpandedNullVsNonNullInnerNode()
        {
            // Covers line 509: m_nodeId.IsNull, nodeId.IsNull → 0 vs -1
            // Create an absolute null expanded node id vs non-null
            var nullId = new ExpandedNodeId(NodeId.Null, null, 1);
            var nonNullId = new ExpandedNodeId(42u, (string)null, 1);
            Assert.That(nullId.CompareTo((object)nonNullId), Is.LessThan(0));
        }

        [Test]
        public void OperatorGreaterThanOrEqual()
        {
            // Covers lines 526-528: >= operator
            var id1 = new ExpandedNodeId(1u);
            var id2 = new ExpandedNodeId(2u);
            var id3 = new ExpandedNodeId(1u);
            Assert.That(id2 >= id1, Is.True);
            Assert.That(id1 >= id3, Is.True);
            Assert.That(id1 >= id2, Is.False);
        }

        [Test]
        public void OperatorLessThanOrEqual()
        {
            // Covers lines 532-534: <= operator
            var id1 = new ExpandedNodeId(1u);
            var id2 = new ExpandedNodeId(2u);
            var id3 = new ExpandedNodeId(1u);
            Assert.That(id1 <= id2, Is.True);
            Assert.That(id1 <= id3, Is.True);
            Assert.That(id2 <= id1, Is.False);
        }

        [Test]
        public void EqualsObjectNull()
        {
            // Covers line 541: Equals(object null) → IsNull
            ExpandedNodeId nullId = ExpandedNodeId.Null;
            Assert.That(nullId.Equals((object)null), Is.True);
            var nonNull = new ExpandedNodeId(1u);
            Assert.That(nonNull.Equals((object)null), Is.False);
        }

        [Test]
        public void EqualsObjectUnknownType()
        {
            // Covers line 543: Equals(non-NodeId, non-ExpandedNodeId) → false
            var id = new ExpandedNodeId(1u);
            Assert.That(id.Equals((object)"a string"), Is.False);
        }

        [Test]
        public void EqualsNodeIdWhenAbsolute()
        {
            // Covers lines 571-574: Equals(NodeId) when IsAbsolute → false
            var id = new ExpandedNodeId(1u, "http://ns.org/");
            var nodeId = new NodeId(1u);
            Assert.That(id.Equals(nodeId), Is.False);
        }

        [Test]
        public void EqualsNodeIdWhenNotAbsolute()
        {
            // Covers lines 576-577: Equals(NodeId) when not absolute → delegate
            var id = new ExpandedNodeId(new NodeId(1u, 2));
            var nodeId = new NodeId(1u, 2);
            Assert.That(id.Equals(nodeId), Is.True);
        }

        [Test]
        public void EqualsNodeIdMismatch()
        {
            // Covers line 576: Equals(NodeId) when not matching
            var id = new ExpandedNodeId(1u);
            var nodeId = new NodeId(2u);
            Assert.That(id.Equals(nodeId), Is.False);
        }

        [Test]
        public void OperatorEqualsObject()
        {
            // Covers lines 612-614: == (ExpandedNodeId, object)
            var id = new ExpandedNodeId(1u);
            Assert.That(id == (object)new ExpandedNodeId(1u), Is.True);
            Assert.That(id == (object)new ExpandedNodeId(2u), Is.False);
        }

        [Test]
        public void OperatorNotEqualsObject()
        {
            // Covers lines 618-620: != (ExpandedNodeId, object)
            var id = new ExpandedNodeId(1u);
            Assert.That(id != (object)new ExpandedNodeId(2u), Is.True);
            Assert.That(id != (object)new ExpandedNodeId(1u), Is.False);
        }

        [Test]
        public void OperatorEqualsNodeId()
        {
            // Covers lines 636-638: == (ExpandedNodeId, NodeId)
            var eid = new ExpandedNodeId(new NodeId(1u));
            var nid = new NodeId(1u);
            Assert.That(eid == nid, Is.True);
        }

        [Test]
        public void OperatorNotEqualsNodeId()
        {
            // Covers lines 642-644: != (ExpandedNodeId, NodeId)
            var eid = new ExpandedNodeId(new NodeId(1u));
            var nid = new NodeId(2u);
            Assert.That(eid != nid, Is.True);
        }

        [Test]
        public void ExplicitCastToNodeIdNull()
        {
            // Covers lines 652-654: explicit cast null → NodeId.Null
            ExpandedNodeId id = ExpandedNodeId.Null;
            NodeId result = (NodeId)id;
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ExplicitCastToNodeIdAbsoluteThrows()
        {
            // Covers lines 656-659: explicit cast absolute → InvalidCastException
            var id = new ExpandedNodeId(1u, "http://ns.org/");
            Assert.That(() => { NodeId _ = (NodeId)id; },
                Throws.TypeOf<InvalidCastException>());
        }

        [Test]
        public void ExplicitCastToNodeIdNonAbsolute()
        {
            // Covers line 661: explicit cast non-absolute → inner node id
            var id = new ExpandedNodeId(new NodeId(42u, 3));
            NodeId result = (NodeId)id;
            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
            Assert.That(result.NamespaceIndex, Is.EqualTo(3));
        }

        [Test]
        public void ToStringWithInvalidFormatThrows()
        {
            // Covers lines 712-713: ToString(format, formatProvider) with non-null format
            var id = new ExpandedNodeId(1u);
            Assert.That(() => id.ToString("X", null),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ToStringWithNullFormat()
        {
            // Covers lines 707-709: ToString(null, null) → Format
            var id = new ExpandedNodeId(1u);
            string result = id.ToString(null, null);
            Assert.That(result, Is.EqualTo("i=1"));
        }

        [Test]
        public void FormatWithContextNullNodeId()
        {
            // Covers lines 774-776: Format(context) when nodeId is null
            ExpandedNodeId id = ExpandedNodeId.Null;
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            Assert.That(id.Format(ctx), Is.Null);
        }

        [Test]
        public void FormatWithContextServerIndexNoUris()
        {
            // Covers lines 800-805: ServerIndex > 0, useUris=false → svr=N;
            var id = new ExpandedNodeId(1u, (string)null, 2);
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            string result = id.Format(ctx, useUris: false);
            Assert.That(result, Does.StartWith("svr=2;"));
        }

        [Test]
        public void FormatWithContextServerIndexWithUrisResolved()
        {
            // Covers lines 783-792: useUris=true with server URI resolved
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            ctx.ServerUris.Append("urn:placeholder");  // index 0
            ctx.ServerUris.Append("urn:server1");       // index 1
            var id = new ExpandedNodeId(1u, (string)null, 1);
            string result = id.Format(ctx, useUris: true);
            Assert.That(result, Does.Contain("svu="));
            Assert.That(result, Does.Contain("urn:server1"));
        }

        [Test]
        public void FormatWithContextServerIndexWithUrisUnresolved()
        {
            // Covers lines 794-798: useUris=true but server URI not found → falls back to svr=N
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            // ServerIndex 5 has no matching URI in the table
            var id = new ExpandedNodeId(1u, (string)null, 5);
            string result = id.Format(ctx, useUris: true);
            Assert.That(result, Does.StartWith("svr=5;"));
        }

        [Test]
        public void ParseWithNamespaceTableNonAbsolute()
        {
            // Covers lines 829-834: non-absolute id → returns inner node id
            var nsTable = new NamespaceTable();
            NodeId result = ExpandedNodeId.Parse("ns=0;i=42", nsTable);
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
        }

        [Test]
        public void ParseWithNamespaceTableAbsoluteResolved()
        {
            // Covers lines 837-845: absolute id with URI in table → resolved
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
            // Covers lines 838-843: absolute id with URI not in table → throws
            var nsTable = new NamespaceTable();
            Assert.That(
                () => ExpandedNodeId.Parse("nsu=http://unknown.org/;i=42", nsTable),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void FormatNullExpandedNodeIdReturnsEmpty()
        {
            // Covers lines 874-876: IsNull → string.Empty
            Assert.That(ExpandedNodeId.Null.Format(null), Is.EqualTo(string.Empty));
        }

        [Test]
        public void FormatWithServerIndex()
        {
            // Covers lines 945-948: serverIndex != 0
            var id = new ExpandedNodeId(1u, (string)null, 3);
            string result = id.Format(CultureInfo.InvariantCulture);
            Assert.That(result, Does.StartWith("svr=3;"));
        }

        [Test]
        public void FormatWithNamespaceUri()
        {
            // Covers lines 950-955: namespaceUri != null
            var id = new ExpandedNodeId(1u, "http://ns.org/");
            string result = id.Format(CultureInfo.InvariantCulture);
            Assert.That(result, Does.Contain("nsu=http://ns.org/"));
        }

        [Test]
        public void FormatIntoBufferNullNodeId()
        {
            // Covers lines 899-909: Format(formatProvider, buffer) when nodeId is null
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
            // Covers lines 922-931: static Format(buffer, ...) without formatProvider
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
            // Covers lines 973-1011: parse with namespace index mapping
            var current = new NamespaceTable();
            current.Append("http://source.org/");
            var target = new NamespaceTable();
            target.Append("http://source.org/");

            ExpandedNodeId result = ExpandedNodeId.Parse(
                "ns=1;i=42", current, target);
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void ParseWithCurrentAndTargetNamespacesWithUri()
        {
            // Covers lines 978,989-1001: parse with namespaceUri
            var current = new NamespaceTable();
            var target = new NamespaceTable();
            target.Append("http://test.org/");

            ExpandedNodeId result = ExpandedNodeId.Parse(
                "nsu=http://test.org/;i=42", current, target);
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void ParseWithCurrentAndTargetNamespacesUnresolvedThrows()
        {
            // Covers lines 993-998: URI not in target table → throws
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
            // Covers lines 1005-1007: ServerIndex != 0 → returns with uri
            var current = new NamespaceTable();
            var target = new NamespaceTable();
            target.Append("http://ns.org/");

            ExpandedNodeId result = ExpandedNodeId.Parse(
                "svr=1;nsu=http://ns.org/;i=42", current, target);
            Assert.That(result.ServerIndex, Is.EqualTo(1u));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org/"));
        }

        [Test]
        public void TryParseWithError()
        {
            // Covers lines 1062-1064: TryParse(text, out value, out error)
            bool success = ExpandedNodeId.TryParse("svr=;i=1", out ExpandedNodeId value, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.Not.EqualTo(NodeIdParseError.None));
        }

        [Test]
        public void TryParseWithErrorValid()
        {
            // Covers lines 1062-1064: valid parse returns None error
            bool success = ExpandedNodeId.TryParse("i=42", out ExpandedNodeId value, out NodeIdParseError error);
            Assert.That(success, Is.True);
            Assert.That(error, Is.EqualTo(NodeIdParseError.None));
            Assert.That(value.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
        }

        [Test]
        public void TryParseWithContextAndOptions()
        {
            // Covers lines 1096-1098: TryParse(context, text, options, out value)
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            ctx.NamespaceUris.Append("http://test.org/");
            var options = new NodeIdParsingOptions { UpdateTables = false };
            bool success = ExpandedNodeId.TryParse(ctx, "nsu=http://test.org/;i=1", options, out ExpandedNodeId value);
            Assert.That(success, Is.True);
            Assert.That(value.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void TryParseWithContextOptionsAndError()
        {
            // Covers lines 1117-1119: TryParse(context, text, options, out value, out error)
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var options = new NodeIdParsingOptions { UpdateTables = false };
            bool success = ExpandedNodeId.TryParse(ctx, "i=42", options, out ExpandedNodeId value, out NodeIdParseError error);
            Assert.That(success, Is.True);
            Assert.That(error, Is.EqualTo(NodeIdParseError.None));
        }

        [Test]
        public void TryParseWithContextOptionsAndErrorFailure()
        {
            // Covers lines 1117-1119: failure with error
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(ctx, "svu=;i=42", null, out ExpandedNodeId value, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.Not.EqualTo(NodeIdParseError.None));
        }

        [Test]
        public void ParseWithContextSuccess()
        {
            // Covers lines 1134-1148: Parse(context, text, options) success
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            ctx.NamespaceUris.Append("http://test.org/");
            var result = ExpandedNodeId.Parse(ctx, "nsu=http://test.org/;i=42");
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
        }

        [Test]
        public void ParseWithContextFailureThrows()
        {
            // Covers lines 1141-1146: Parse(context, text, options) failure → throws
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            Assert.That(
                () => ExpandedNodeId.Parse(ctx, "svu=;i=42"),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseWithContextAndUpdateTablesOption()
        {
            // Covers lines 1395-1398: options.UpdateTables == true → GetIndexOrAppend
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
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
            // Covers lines 1247-1250: svr= without semicolon
            bool success = ExpandedNodeId.TryParse("svr=1", out ExpandedNodeId value);
            Assert.That(success, Is.False);
        }

        [Test]
        public void TryParseInvalidServerIndexNonNumeric()
        {
            // Covers lines 1253-1260: svr= with non-numeric value
            bool success = ExpandedNodeId.TryParse("svr=abc;i=1", out ExpandedNodeId value);
            Assert.That(success, Is.False);
        }

        [Test]
        public void TryParseInvalidNamespaceUriNoSemicolon()
        {
            // Covers lines 1271-1274: nsu= without semicolon
            bool success = ExpandedNodeId.TryParse("nsu=http://test.org/", out ExpandedNodeId value);
            Assert.That(success, Is.False);
        }

        [Test]
        public void TryParseNamespaceUriWithEscapedCharacters()
        {
            // Covers lines 1277-1280: nsu= with escaped URI parsed via UnescapeUri
            bool success = ExpandedNodeId.TryParse(
                "nsu=http://test.org/my%3Bpath;i=42", out ExpandedNodeId value);
            Assert.That(success, Is.True);
            Assert.That(value.NamespaceUri, Is.EqualTo("http://test.org/my;path"));
        }

        [Test]
        public void TryParseCatchBlockOnInvalidEscape()
        {
            // Covers lines 1283-1286: catch block for unexpected exceptions in URI parsing
            // A truncated percent-escape at end triggers ServiceResultException, caught → Unexpected
            bool success = ExpandedNodeId.TryParse("nsu=http://test%2;i=1", out _, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.Unexpected));
        }

        [Test]
        public void TryParseWithContextEmptyText()
        {
            // Covers lines 1325-1328: empty text → Null
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(ctx, "", out ExpandedNodeId value);
            Assert.That(success, Is.True);
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void TryParseWithContextSvuNoSemicolon()
        {
            // Covers lines 1337-1340: svu= without semicolon → error
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(ctx, "svu=urn:server", out ExpandedNodeId value);
            Assert.That(success, Is.False);
        }

        [Test]
        public void TryParseWithContextSvuUnknownServerUri()
        {
            // Covers lines 1349-1352: svu= with unknown server → NoServerUriMapping
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(
                ctx, "svu=urn:unknown;i=1", null, out ExpandedNodeId value, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.NoServerUriMapping));
        }

        [Test]
        public void TryParseWithContextSvuUpdateTables()
        {
            // Covers lines 1344-1346: svu= with UpdateTables → GetIndexOrAppend
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var options = new NodeIdParsingOptions { UpdateTables = true };
            bool success = ExpandedNodeId.TryParse(
                ctx, "svu=urn:newserver;i=1", options, out ExpandedNodeId value);
            Assert.That(success, Is.True);
            Assert.That(ctx.ServerUris.GetIndex("urn:newserver"), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void TryParseWithContextSvrFormat()
        {
            // Covers lines 1358-1379: svr= parsing in context parse
            // Must pass options (even empty) to avoid NullReferenceException at line 1372
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var options = new NodeIdParsingOptions();
            bool success = ExpandedNodeId.TryParse(ctx, "svr=2;i=1", options, out ExpandedNodeId value);
            Assert.That(success, Is.True);
            Assert.That(value.ServerIndex, Is.EqualTo(2u));
        }

        [Test]
        public void TryParseWithContextSvrNoSemicolon()
        {
            // Covers lines 1362-1365: svr= without semicolon in context parse
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(
                ctx, "svr=2", null, out ExpandedNodeId value, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.InvalidServerUriFormat));
        }

        [Test]
        public void TryParseWithContextNsuNoSemicolon()
        {
            // Covers lines 1389-1391: nsu= without semicolon in context parse
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(
                ctx, "nsu=http://test.org/", null, out ExpandedNodeId value, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.InvalidNamespaceFormat));
        }

        [Test]
        public void TryParseWithContextNsuNamespaceResolvedToIndex()
        {
            // Covers lines 1394-1420: nsu= resolved to index > 0
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            ctx.NamespaceUris.Append("http://test.org/");
            bool success = ExpandedNodeId.TryParse(
                ctx, "nsu=http://test.org/;i=42", out ExpandedNodeId value);
            Assert.That(success, Is.True);
            Assert.That(value.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void TryParseWithContextNsuNamespaceNotResolved()
        {
            // Covers lines 1418-1420: nsu= not resolved (namespaceIndex stays 0) → stores URI
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(
                ctx, "nsu=http://unknown.org/;i=42", out ExpandedNodeId value);
            Assert.That(success, Is.True);
            Assert.That(value.NamespaceUri, Is.EqualTo("http://unknown.org/"));
        }

        [Test]
        public void TryParseWithContextInvalidNodeIdFails()
        {
            // Covers lines 1403-1405: inner NodeId parse failure
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            bool success = ExpandedNodeId.TryParse(ctx, "i=notanumber", out ExpandedNodeId value);
            Assert.That(success, Is.False);
        }

        [Test]
        public void GetHashCodeForNullReturnsZero()
        {
            // Covers lines 582-584
            Assert.That(ExpandedNodeId.Null.GetHashCode(), Is.EqualTo(0));
        }

        [Test]
        public void GetHashCodeForNonAbsolute()
        {
            // Covers lines 588-590: non-absolute → inner nodeId hash
            var id = new ExpandedNodeId(42u);
            Assert.That(id.GetHashCode(), Is.EqualTo(new NodeId(42u).GetHashCode()));
        }

        [Test]
        public void GetHashCodeForAbsoluteWithServerIndex()
        {
            // Covers lines 593-607: absolute with server index and namespace uri
            var id = new ExpandedNodeId(42u, "http://ns.org/", 3);
            int hash = id.GetHashCode();
            Assert.That(hash, Is.Not.EqualTo(0));
        }

        [Test]
        public void GetHashCodeForAbsoluteWithOnlyServerIndex()
        {
            // Covers lines 595-598: ServerIndex != 0, NamespaceUri == null
            var id = new ExpandedNodeId(42u, (string)null, 3);
            int hash = id.GetHashCode();
            Assert.That(hash, Is.Not.EqualTo(0));
        }

        [Test]
        public void ToNodeIdNullExpandedNodeId()
        {
            // Covers lines 735-737
            var result = ExpandedNodeId.ToNodeId(ExpandedNodeId.Null, null);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ToNodeIdNonAbsoluteReturnsInner()
        {
            // Covers lines 741-744: non-absolute → returns inner
            var id = new ExpandedNodeId(new NodeId(42u));
            var result = ExpandedNodeId.ToNodeId(id, null);
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
        }

        [Test]
        public void ToNodeIdWithNullNamespaceTable()
        {
            // Covers lines 752-760: namespaceTable is null → index stays -1 → NodeId.Null
            var id = new ExpandedNodeId(42u, "http://ns.org/");
            var result = ExpandedNodeId.ToNodeId(id, null);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ToNodeIdWithNamespaceTableNotFound()
        {
            // Covers lines 757-760: namespace not in table → NodeId.Null
            var nsTable = new NamespaceTable();
            var id = new ExpandedNodeId(42u, "http://unknown.org/");
            var result = ExpandedNodeId.ToNodeId(id, nsTable);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ToNodeIdWithNamespaceTableFound()
        {
            // Covers line 763: namespace found → return with mapped index
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
            // Covers lines 1462-1465: default ctor
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
            Assert.That(s1.Equals((object)s2), Is.True);
            // ExpandedNodeId case
            Assert.That(s1.Equals((object)eid), Is.True);
            // Other case (falls through to Value.Equals)
            Assert.That(s1.Equals((object)"not a node"), Is.False);
        }

        [Test]
        public void SerializableEqualsExpandedNodeId()
        {
            // Covers lines 1505-1507
            var eid = new ExpandedNodeId(42u);
            var s = new SerializableExpandedNodeId(eid);
            Assert.That(s.Equals(eid), Is.True);
            Assert.That(s.Equals(new ExpandedNodeId(99u)), Is.False);
        }

        [Test]
        public void SerializableEqualsSerializableExpandedNodeId()
        {
            // Covers lines 1511-1513
            var s1 = new SerializableExpandedNodeId(new ExpandedNodeId(42u));
            var s2 = new SerializableExpandedNodeId(new ExpandedNodeId(42u));
            SerializableExpandedNodeId nullS = null;
            Assert.That(s1.Equals(s2), Is.True);
#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(s1.Equals(nullS), Is.False);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void SerializableGetHashCode()
        {
            // Covers lines 1517-1519
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
            Assert.That(s1 == s2, Is.True);
#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(nullS == s1, Is.False);
            Assert.That(nullS == (SerializableExpandedNodeId)null, Is.True);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void SerializableOperatorNotEqualsBothSerializable()
        {
            // Covers lines 1533-1535
            var s1 = new SerializableExpandedNodeId(new ExpandedNodeId(42u));
            var s2 = new SerializableExpandedNodeId(new ExpandedNodeId(99u));
            Assert.That(s1 != s2, Is.True);
        }

        [Test]
        public void SerializableOperatorEqualsWithExpandedNodeId()
        {
            // Covers lines 1541-1543
            var eid = new ExpandedNodeId(42u);
            var s = new SerializableExpandedNodeId(eid);
            SerializableExpandedNodeId nullS = null;
            Assert.That(s == eid, Is.True);
#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(nullS == ExpandedNodeId.Null, Is.True);
            Assert.That(nullS == new ExpandedNodeId(42u), Is.False);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void SerializableOperatorNotEqualsWithExpandedNodeId()
        {
            var s = new SerializableExpandedNodeId(new ExpandedNodeId(42u));
            Assert.That(s != new ExpandedNodeId(99u), Is.True);
        }

        [Test]
        public void SerializableImplicitFromExpandedNodeId()
        {
            // Covers lines 1556-1558
            SerializableExpandedNodeId s = new ExpandedNodeId(42u);
            Assert.That(s.Value, Is.EqualTo(new ExpandedNodeId(42u)));
        }

        [Test]
        public void SerializableImplicitToExpandedNodeId()
        {
            var s = new SerializableExpandedNodeId(new ExpandedNodeId(42u));
            ExpandedNodeId eid = s;
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
            // Covers lines 667-670
            var nodeId = new NodeId(42u, 3);
            ExpandedNodeId eid = nodeId;
            Assert.That(eid.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
            Assert.That(eid.NamespaceIndex, Is.EqualTo(3));
        }

        [Test]
        public void ImplicitConversionFromUint()
        {
            // Covers lines 675-678
            ExpandedNodeId eid = 99u;
            Assert.That(eid.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(99u));
        }

        [Test]
        public void ImplicitConversionFromGuid()
        {
            // Covers lines 683-686
            var guid = Guid.NewGuid();
            ExpandedNodeId eid = guid;
            Assert.That(eid.TryGetIdentifier(out Guid g), Is.True);
            Assert.That(g, Is.EqualTo(guid));
        }

        [Test]
        public void ExplicitConversionFromByteString()
        {
            // Covers lines 691-694
            ByteString bs = [1, 2, 3];
            var eid = (ExpandedNodeId)bs;
            Assert.That(eid.TryGetIdentifier(out ByteString result), Is.True);
            Assert.That(result, Is.EqualTo(bs));
        }

        [Test]
        public void EqualsExpandedNodeIdDifferentServerIndex()
        {
            // Covers lines 554-556
            var id1 = new ExpandedNodeId(1u, "http://ns.org/", 1);
            var id2 = new ExpandedNodeId(1u, "http://ns.org/", 2);
            Assert.That(id1.Equals(id2), Is.False);
        }

        [Test]
        public void EqualsExpandedNodeIdDifferentNamespaceUri()
        {
            // Covers lines 558-560
            var id1 = new ExpandedNodeId(1u, "http://a.org/", 1);
            var id2 = new ExpandedNodeId(1u, "http://b.org/", 1);
            Assert.That(id1.Equals(id2), Is.False);
        }

        [Test]
        public void EqualsExpandedNodeIdDifferentInnerNodeId()
        {
            // Covers lines 562-564
            var id1 = new ExpandedNodeId(1u, "http://ns.org/", 1);
            var id2 = new ExpandedNodeId(2u, "http://ns.org/", 1);
            Assert.That(id1.Equals(id2), Is.False);
        }

        [Test]
        public void EqualsExpandedNodeIdAllSame()
        {
            // Covers line 566
            var id1 = new ExpandedNodeId(1u, "http://ns.org/", 1);
            var id2 = new ExpandedNodeId(1u, "http://ns.org/", 1);
            Assert.That(id1.Equals(id2), Is.True);
        }

        [Test]
        public void EqualsExpandedNodeIdBothNull()
        {
            // Covers lines 550-552
            Assert.That(ExpandedNodeId.Null.Equals(ExpandedNodeId.Null), Is.True);
        }

        [Test]
        public void FormatAndParseRoundTripWithEscapedNamespaceUri()
        {
            // Covers EscapeUri / UnescapeUri round-trip in Format lines 950-955
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
            // Covers percent-encoding in EscapeUri and UnescapeUri lines 1167-1207
            var id = new ExpandedNodeId(1u, "http://ns.org/100%done");
            string formatted = id.Format(CultureInfo.InvariantCulture);
            Assert.That(formatted, Does.Contain("%25"));

            var parsed = ExpandedNodeId.Parse(formatted);
            Assert.That(parsed.NamespaceUri, Is.EqualTo("http://ns.org/100%done"));
        }

        [Test]
        public void CompareToNodeIdNotNullWhenAbsolute()
        {
            // Covers line 472: obj is NodeId (non-null) but ExpandedNodeId is absolute
            // Falls through to line 503 comparison
            var id = new ExpandedNodeId(10u, "http://ns.org/", 1);
            var nodeId = new NodeId(5u);
            int cmp = id.CompareTo((object)nodeId);
            Assert.That(cmp, Is.Not.EqualTo(0));
        }

        [Test]
        public void OperatorNotEqualsExpandedNodeId()
        {
            // Covers lines 630-632: != (ExpandedNodeId, ExpandedNodeId)
            var id1 = new ExpandedNodeId(1u);
            var id2 = new ExpandedNodeId(2u);
            Assert.That(id1 != id2, Is.True);
            Assert.That(id1 != new ExpandedNodeId(1u), Is.False);
        }

        [Test]
        public void TryParseInvalidFirstHexDigitInEscape()
        {
            // Covers lines 1181-1185: UnescapeUri first hex digit invalid
            bool success = ExpandedNodeId.TryParse("nsu=http://test%XZ;i=1", out _, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.Unexpected));
        }

        [Test]
        public void TryParseInvalidSecondHexDigitInEscape()
        {
            // Covers lines 1195-1199: UnescapeUri second hex digit invalid
            bool success = ExpandedNodeId.TryParse("nsu=http://test%3X;i=1", out _, out NodeIdParseError error);
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo(NodeIdParseError.Unexpected));
        }

        [Test]
        public void TryParseWithContextSvrWithServerMappings()
        {
            // Covers lines 1372-1375: ServerMappings != null path
            var ctx = new ServiceMessageContext(NUnitTelemetryContext.Create());
            var options = new NodeIdParsingOptions
            {
                ServerMappings = [0, 5, 10],
                NamespaceMappings = [0, 1, 2, 3]
            };
            bool success = ExpandedNodeId.TryParse(
                ctx, "svr=2;i=42", options, out ExpandedNodeId value);
            Assert.That(success, Is.True);
        }

        [Test]
        public void SerializableGetValue()
        {
            // Covers lines 1477-1480: GetValue() method
            var eid = new ExpandedNodeId(42u);
            var s = new SerializableExpandedNodeId(eid);
            object val = s.GetValue();
            Assert.That(val, Is.EqualTo(eid));
        }

        [Test]
        public void CompareToAbsoluteNullNodeIdWithNonNullNodeId()
        {
            // Covers line 503-505 and 509: absolute with null inner vs non-null nodeId
            var nullAbsolute = new ExpandedNodeId(NodeId.Null, null, 1);
            var other = new ExpandedNodeId(42u, (string)null, 1);
            int cmp = nullAbsolute.CompareTo((object)other);
            Assert.That(cmp, Is.LessThan(0));
        }

        [Test]
        public void CompareToAbsoluteWithNonNullNodeId()
        {
            // Covers lines 503-505: m_nodeId is not null in absolute context → m_nodeId.CompareTo
            var id1 = new ExpandedNodeId(10u, "http://ns.org/", 1);
            var id2 = new ExpandedNodeId(20u, "http://ns.org/", 1);
            Assert.That(id1.CompareTo((object)id2), Is.LessThan(0));
        }

        [Test]
        public void ParseEmptyStringReturnsNull()
        {
            // Covers lines 1231-1234: InternalTryParse with empty string
            var result = ExpandedNodeId.Parse("");
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ParseNullStringReturnsNull()
        {
            // Covers lines 1231-1234: InternalTryParse with null
            var result = ExpandedNodeId.Parse((string)null);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ParseWithServerAndNamespaceUri()
        {
            // Covers combined svr= and nsu= in InternalTryParse lines 1243-1280
            var result = ExpandedNodeId.Parse("svr=2;nsu=http://test.org/;i=42");
            Assert.That(result.ServerIndex, Is.EqualTo(2u));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://test.org/"));
            Assert.That(result.TryGetIdentifier(out uint n), Is.True);
            Assert.That(n, Is.EqualTo(42u));
        }

        [Test]
        public void ParseInvalidTextThrows()
        {
            // Covers lines 1027-1031: Parse throws on invalid text
            Assert.That(
                () => ExpandedNodeId.Parse("invalid_text"),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ConstructorNodeIdWithNamespaceUri()
        {
            // Covers lines 70-74: NodeId ctor with non-empty namespaceUri
            var nodeId = new NodeId(42u, 5);
            var eid = new ExpandedNodeId(nodeId, "http://ns.org/", 0);
            Assert.That(eid.NamespaceUri, Is.EqualTo("http://ns.org/"));
            // NamespaceIndex is forced to 0 when URI is provided
            Assert.That(eid.NamespaceIndex, Is.EqualTo(0));
        }

        [Test]
        public void ConstructorNodeIdWithNullNamespaceUri()
        {
            // Covers lines 76-79: NodeId ctor with null namespaceUri
            var nodeId = new NodeId(42u, 5);
            var eid = new ExpandedNodeId(nodeId, null, 0);
            Assert.That(eid.NamespaceUri, Is.Null);
            Assert.That(eid.NamespaceIndex, Is.EqualTo(5));
        }

        [Test]
        public void ConstructorGuidWithNamespaceUri()
        {
            // Covers lines 196-199: Guid ctor with namespaceUri
            var guid = Guid.NewGuid();
            var id = new ExpandedNodeId(guid, "http://ns.org/", 0);
            Assert.That(id.NamespaceUri, Is.EqualTo("http://ns.org/"));
        }

        [Test]
        public void ConstructorByteStringWithNamespaceUri()
        {
            // Covers lines 244-247: ByteString ctor with namespaceUri
            ByteString bs = [1, 2];
            var id = new ExpandedNodeId(bs, "http://ns.org/", 0);
            Assert.That(id.NamespaceUri, Is.EqualTo("http://ns.org/"));
        }
    }
}
