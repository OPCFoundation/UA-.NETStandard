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

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Regression tests for the string forms of NodeId, ExpandedNodeId and
    /// QualifiedName (Part 6 5.1.12) found by the codec audit.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeIdStringFormAuditTests
    {
        private static ServiceMessageContext CreateContext()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.NamespaceUris.Append("urn:ns1");
            context.NamespaceUris.Append("urn:ns2");
            return context;
        }

        [TestCase("ns= 2;i=5")]
        [TestCase("ns=+2;i=5")]
        [TestCase("ns=2 ;i=5")]
        [TestCase("i= 5")]
        [TestCase("i=+7")]
        [TestCase("i=-0")]
        [TestCase("i=7 ")]
        [TestCase("g={C496578A-0DFE-4B8F-870A-745238C6AEAE}")]
        [TestCase("g=C496578A0DFE4B8F870A745238C6AEAE")]
        [TestCase("g=(C496578A-0DFE-4B8F-870A-745238C6AEAE)")]
        public void ContextNodeIdParserRejectsNonCanonicalNumbersAndGuids(string text)
        {
            ServiceMessageContext context = CreateContext();

            Assert.That(
                NodeId.TryParse(context, text, out _),
                Is.False,
                text);
            Assert.That(
                ExpandedNodeId.TryParse(context, text, out _),
                Is.False,
                text);
        }

        [TestCase("i=+7")]
        [TestCase("g={C496578A-0DFE-4B8F-870A-745238C6AEAE}")]
        [TestCase("g=C496578A0DFE4B8F870A745238C6AEAE")]
        public void NodeIdParserRejectsNonCanonicalNumbersAndGuids(string text)
        {
            Assert.That(NodeId.TryParse(text, out _), Is.False, text);
            Assert.That(ExpandedNodeId.TryParse(text, out _), Is.False, text);
        }

        [Test]
        public void NodeIdParsersAcceptCanonicalForms()
        {
            ServiceMessageContext context = CreateContext();
            var guid = new Guid("C496578A-0DFE-4B8F-870A-745238C6AEAE");

            Assert.Multiple(() =>
            {
                Assert.That(NodeId.Parse(context, "ns=2;i=5"), Is.EqualTo(new NodeId(5u, 2)));
                Assert.That(NodeId.Parse(context, "g=c496578a-0dfe-4b8f-870a-745238c6aeae"),
                    Is.EqualTo(new NodeId(guid)));
                Assert.That(NodeId.Parse("ns=2;g=C496578A-0DFE-4B8F-870A-745238C6AEAE"),
                    Is.EqualTo(new NodeId(guid, 2)));
            });
        }

        [TestCase(" 2:Name")]
        [TestCase("+2:Name")]
        public void ContextQualifiedNameParserRejectsNonDigitIndex(string text)
        {
            ServiceMessageContext context = CreateContext();

            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => QualifiedName.Parse(context, text, false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void QualifiedNameParserKeepsNonDigitPrefixInTheName()
        {
            QualifiedName name = QualifiedName.Parse(" 2:Name");

            Assert.Multiple(() =>
            {
                Assert.That(name.NamespaceIndex, Is.Zero);
                Assert.That(name.Name, Is.EqualTo(" 2:Name"));
            });
        }

        [Test]
        public void ExpandedNodeIdParsersDecodePercentEscapesAsUtf8()
        {
            // "%C3%A4" is the UTF-8 encoding of U+00E4. The non-context parser
            // used to decode every escape as a Latin-1 character ("Ã¤").
            ServiceMessageContext context = CreateContext();
            const string text = "nsu=urn:%C3%A4;i=1";

            ExpandedNodeId plain = ExpandedNodeId.Parse(text);
            ExpandedNodeId withContext = ExpandedNodeId.Parse(context, text);

            Assert.Multiple(() =>
            {
                Assert.That(plain.NamespaceUri, Is.EqualTo("urn:ä"));
                Assert.That(withContext.NamespaceUri, Is.EqualTo("urn:ä"));
            });
        }

        [TestCase("nsu=urn:%ZZ;i=1")]
        [TestCase("nsu=urn:%C3;i=1")]
        [TestCase("nsu=urn:%4;i=1")]
        [TestCase("svu=urn:%ZZ;i=1")]
        public void ExpandedNodeIdParsersRejectMalformedPercentEscapes(string text)
        {
            // The context parser kept a malformed escape verbatim while the
            // other parser rejected it, so one text named two namespaces.
            ServiceMessageContext context = CreateContext();

            Assert.Multiple(() =>
            {
                Assert.That(ExpandedNodeId.TryParse(context, text, out _), Is.False, text);
                Assert.That(NodeId.TryParse(context, text, out _), Is.False, text);
                if (text.StartsWith("nsu=", StringComparison.Ordinal))
                {
                    Assert.That(ExpandedNodeId.TryParse(text, out _), Is.False, text);
                }
            });
        }

        [Test]
        public void ExpandedNodeIdEscapedUriRoundTripsThroughBothParsers()
        {
            ServiceMessageContext context = CreateContext();
            var id = new ExpandedNodeId(7u, "urn:a;b%cä");

            string text = id.Format(context, useUris: true);

            Assert.Multiple(() =>
            {
                Assert.That(ExpandedNodeId.Parse(text).NamespaceUri, Is.EqualTo("urn:a;b%cä"));
                Assert.That(ExpandedNodeId.Parse(context, text).NamespaceUri, Is.EqualTo("urn:a;b%cä"));
            });
        }

        [Test]
        public void QualifiedNameContextParserDecodesPercentEscapesAsUtf8()
        {
            ServiceMessageContext context = CreateContext();
            context.NamespaceUris.Append("urn:ä");

            QualifiedName name = QualifiedName.Parse(context, "nsu=urn:%C3%A4;Name", false);

            Assert.That(name.NamespaceIndex, Is.EqualTo(3));
        }
    }
}
