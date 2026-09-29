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

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Regression tests for the JSON decoder findings of the codec audit.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class JsonDecoderCodecAuditTests
    {
        private static ServiceMessageContext CreateContext()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris.Append("urn:ns1");
            context.NamespaceUris.Append("urn:ns2");
            return context;
        }

        private static JsonDecoder Field(ServiceMessageContext context, string jsonValue)
        {
            return new JsonDecoder("{\"F\":" + jsonValue + "}", context);
        }

        private static void AssertStatus(StatusCode expected, Action action)
        {
            ServiceResultException sre = Assert.Throws<ServiceResultException>(action);
            Assert.That(sre.StatusCode, Is.EqualTo(expected), sre.Message);
        }

        [TestCase("\"s=ABCDEFGHIJK\"")]
        [TestCase("\"ns=1;s=ABCDEFGHIJK\"")]
        [TestCase("\"nsu=urn:unknown;i=1\"")]
        public void NodeIdStringIdentifierIsCheckedAgainstMaxStringLength(string json)
        {
            // String NodeIds bypassed MaxStringLength, UA Binary enforces it.
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 10;
            using JsonDecoder decoder = Field(context, json);

            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, () => decoder.ReadNodeId("F"));
        }

        [Test]
        public void NodeIdStringIdentifierAtMaxStringLengthIsAccepted()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 10;
            using JsonDecoder decoder = Field(context, "\"ns=1;s=ABCDEFGHIJ\"");

            Assert.That(decoder.ReadNodeId("F"), Is.EqualTo(new NodeId("ABCDEFGHIJ", 1)));
        }

        [Test]
        public void OpaqueNodeIdIsCheckedAgainstMaxByteStringLength()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxByteStringLength = 4;
            string base64 = Convert.ToBase64String(new byte[5]);

            using JsonDecoder decoder = Field(context, "\"b=" + base64 + "\"");
            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, () => decoder.ReadNodeId("F"));

            using JsonDecoder expanded = Field(context, "\"b=" + base64 + "\"");
            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, () => expanded.ReadExpandedNodeId("F"));
        }

        [TestCase("\"s=ABCDEFGHIJK\"")]
        [TestCase("\"nsu=urn:ABCDEFGHIJK;i=1\"")]
        [TestCase("\"svu=urn:unknown;i=1\"")]
        public void ExpandedNodeIdStringsAreCheckedAgainstMaxStringLength(string json)
        {
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 10;
            using JsonDecoder decoder = Field(context, json);

            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, () => decoder.ReadExpandedNodeId("F"));
        }

        [TestCase("\"ABCDEFGHIJK\"")]
        [TestCase("\"1:ABCDEFGHIJK\"")]
        [TestCase("\"nsu=urn:unknown;Name\"")]
        public void QualifiedNameIsCheckedAgainstMaxStringLength(string json)
        {
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 10;
            using JsonDecoder decoder = Field(context, json);

            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, () => decoder.ReadQualifiedName("F"));
        }

        [TestCase("Hello:World", 0, "Hello:World")]
        [TestCase(":abc", 0, ":abc")]
        [TestCase("a1:b", 0, "a1:b")]
        [TestCase("0:Hello:World", 0, "Hello:World")]
        [TestCase("2:Name", 2, "Name")]
        [TestCase("nsu=urn:ns1;Name", 1, "Name")]
        [TestCase("nsu=urn:unknown;Name", 0, "nsu=urn:unknown;Name")]
        [TestCase("nsu=urn:%ZZ;Name", 0, "nsu=urn:%ZZ;Name")]
        public void QualifiedNameUsesTheJsonStringForms(string text, int namespaceIndex, string name)
        {
            // Part 6 5.1.12 Table 7 only reserves a digit run followed by ':'.
            // Part 6 5.4.2.14 keeps an unmapped NamespaceUri as the raw name.
            ServiceMessageContext context = CreateContext();
            using JsonDecoder decoder = Field(context, "\"" + text + "\"");

            QualifiedName value = decoder.ReadQualifiedName("F");

            Assert.Multiple(() =>
            {
                Assert.That(value.NamespaceIndex, Is.EqualTo(namespaceIndex));
                Assert.That(value.Name, Is.EqualTo(name));
            });
        }

        [TestCase("70000:Name")]
        [TestCase("nsu=urn:ns1")]
        public void QualifiedNameWithInvalidIndexOrSeparatorIsRejected(string text)
        {
            ServiceMessageContext context = CreateContext();
            using JsonDecoder decoder = Field(context, "\"" + text + "\"");

            AssertStatus(StatusCodes.BadDecodingError, () => decoder.ReadQualifiedName("F"));
        }

        [TestCase("{\"UaTypeId\":\"i=631\",\"UaTypeId\":\"i=527\",\"UaBody\":{}}")]
        [TestCase("{\"UaTypeId\":\"i=631\",\"UaBody\":{\"MaxAge\":1,\"MaxAge\":2}}")]
        [TestCase("{\"A\":[{\"B\":1},{\"C\":1,\"C\":2}]}")]
        public void DuplicateMemberNamesAreRejected(string json)
        {
            // Part 6 5.4.2.16: decoders shall report a decoding error if a
            // JSON object has multiple fields with the same name.
            ServiceMessageContext context = CreateContext();

            AssertStatus(StatusCodes.BadDecodingError, () => _ = new JsonDecoder(json, context));
            AssertStatus(
                StatusCodes.BadDecodingError,
                () => JsonDecoder.DecodeMessage<IEncodeable>(
                    System.Text.Encoding.UTF8.GetBytes(json),
                    context));
        }

        [Test]
        public void SameMemberNameInSiblingObjectsIsAccepted()
        {
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder("{\"A\":{\"X\":1},\"B\":{\"X\":2}}", context);

            Assert.That(decoder.Root.GetProperty("B").GetProperty("X").GetInt32(), Is.EqualTo(2));
        }

        [Test]
        public void QualifiedNameIndexIsMappedLikeTheNodeIdIndex()
        {
            // After SetMappingTables a NodeId "ns=1" was remapped but the
            // QualifiedName "1:" was not, so the two disagreed.
            ServiceMessageContext context = CreateContext();
            var messageTable = new NamespaceTable();
            messageTable.Append("urn:ns2");
            using JsonDecoder decoder = new(
                "{\"N\":\"ns=1;i=5\",\"Q\":\"1:Name\"}",
                context);
            decoder.SetMappingTables(messageTable, null);

            NodeId nodeId = decoder.ReadNodeId("N");
            QualifiedName name = decoder.ReadQualifiedName("Q");

            Assert.Multiple(() =>
            {
                Assert.That(nodeId.NamespaceIndex, Is.EqualTo(2));
                Assert.That(name.NamespaceIndex, Is.EqualTo(2));
            });
        }
    }
}
