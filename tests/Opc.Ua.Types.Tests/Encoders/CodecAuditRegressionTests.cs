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
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Regression tests for the binary and XML codec defects reported by the
    /// read-only bug audit of Opc.Ua.Types.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CodecAuditRegressionTests
    {
        private const string kNs = Namespaces.OpcUaXsd;

        private static ServiceMessageContext CreateContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.CreateEmpty(telemetry);
            context.Factory.AddEncodeableType(typeof(Sample));
            return context;
        }

        [Test]
        [CancelAfter(30000)]
        public void ReadVariantValueThrowsForTextOnlyContentInsteadOfSpinning()
        {
            // The whitespace skip loop called Read() until an Element appeared,
            // but Read() returns false at EOF with NodeType None - so a Value
            // element that carries text instead of a typed child element made
            // the decoder spin forever.
            ServiceMessageContext context = CreateContext();
            const string xml = "<Value xmlns=\"" + kNs + "\">not an element</Value>";

            using var decoder = new XmlDecoder(
                XmlReader.Create(
                    new StringReader(xml),
                    CoreUtils.DefaultXmlReaderSettings()),
                context);
            decoder.PushNamespace(kNs);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariant("Value"));
            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadDecodingError));
        }

        [Test]
        public void EncodeableMatrixUsesTheFieldNameNotTheLiteralMatrix()
        {
            // WriteEncodeableMatrix hard-coded the element name "Matrix", so a
            // structure field with any other name could not be decoded.
            ServiceMessageContext context = CreateContext();
            Sample[] elements = [new Sample { Value = 1 }, new Sample { Value = 2 }];
            var input = new MatrixOf<Sample>(elements, [1, 2]);

            string xml;
            using (var encoder = new XmlEncoder(context))
            {
                encoder.PushNamespace(kNs);
                encoder.WriteEncodeableMatrix("Samples", input);
                encoder.PopNamespace();
                xml = encoder.CloseAndReturnText()!;
            }

            Assert.That(xml, Does.Contain("Samples"));

            using var decoder = new XmlParser(xml, context);
            decoder.PushNamespace(kNs);
            MatrixOf<Sample> output = decoder.ReadEncodeableMatrix<Sample>("Samples");
            decoder.PopNamespace();

            Assert.Multiple(() =>
            {
                Assert.That(output.Count, Is.EqualTo(2));
                Assert.That(output.Span[0].Value, Is.EqualTo(1));
                Assert.That(output.Span[1].Value, Is.EqualTo(2));
            });
        }

        [Test]
        public void BinaryEncoderAppliesMaxStringLengthToTheEncodedLength()
        {
            // The encoder counted UTF-16 chars while the decoder counts UTF-8
            // bytes, so a non ASCII string could encode and fail to decode.
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 10;
            string value = new('ä', 10); // 10 chars, 20 UTF-8 bytes

            using var encoder = new BinaryEncoder(context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteString("Value", value));
            Assert.That(
                ex.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void BinaryEncoderStillAcceptsAStringWithinTheEncodedLimit()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 10;

            using var encoder = new BinaryEncoder(context);
            Assert.DoesNotThrow(() => encoder.WriteString("Value", new string('a', 10)));
        }

        [Test]
        [TestCase(3)]
        [TestCase(100)]
        [TestCase(20000)]
        public void BinaryEncoderDoesNotApplyMaxByteStringLengthToStrings(int length)
        {
            // Depending on the string size the encoder routed strings through
            // WriteByteString, which applied MaxByteStringLength although the
            // decoder only checks MaxStringLength.
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 0;
            context.MaxByteStringLength = 2;
            string value = new('x', length);

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteString("Value", value);
                buffer = encoder.CloseAndReturnBuffer()!;
            }

            using var decoder = new BinaryDecoder(buffer!, context);
            Assert.That(decoder.ReadString("Value"), Is.EqualTo(value));
        }

        [Test]
        public void EncodeableMatrixWithInconsistentDimensionsIsADecodingError()
        {
            // MatrixOf throws ArgumentException for wire dimensions that do not
            // match the payload; that must surface as BadDecodingError.
            ServiceMessageContext context = CreateContext();
            Sample[] elements = [new Sample { Value = 1 }, new Sample { Value = 2 }];
            var input = new MatrixOf<Sample>(elements, [1, 2]);

            string xml;
            using (var encoder = new XmlEncoder(context))
            {
                encoder.PushNamespace(kNs);
                encoder.WriteEncodeableMatrix("Samples", input);
                encoder.PopNamespace();
                xml = encoder.CloseAndReturnText()!;
            }

            // Claim three columns for a payload that only carries two elements.
            string tampered = xml!.Replace(
                "<Int32>2</Int32>",
                "<Int32>3</Int32>",
                StringComparison.Ordinal);
            Assert.That(tampered, Is.Not.EqualTo(xml));

            using var parser = new XmlParser(tampered, context);
            parser.PushNamespace(kNs);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => parser.ReadEncodeableMatrix<Sample>("Samples"));
            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadDecodingError));
        }

        [Test]
        public void ByteStringArrayLengthIsCheckedAgainstMaxArrayLength()
        {
            // The element count was compared against MaxByteStringLength.
            ServiceMessageContext context = CreateContext();
            context.MaxArrayLength = 1;
            context.MaxByteStringLength = 1024;

            const string xml =
                "<ListOfByteString xmlns=\"" + kNs + "\">" +
                "<ByteString>AQ==</ByteString>" +
                "<ByteString>Ag==</ByteString>" +
                "</ListOfByteString>";

            using var parser = new XmlParser(xml, context);
            parser.PushNamespace(kNs);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => parser.ReadByteStringArray("ListOfByteString"));
            Assert.That(
                ex.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void PreEncodedExtensionObjectBodyIsNotLimitedByMaxByteStringLength()
        {
            // The buffer-writer encoder wrote the pre-encoded body through
            // WriteByteString, applying a limit that neither the seekable
            // stream path nor the decoder applies.
            ServiceMessageContext context = CreateContext();
            context.MaxByteStringLength = 2; // the body is a 4 byte Int32

            using var encoder = new BinaryEncoder(context);
            Assert.DoesNotThrow(
                () => encoder.WriteExtensionObject(
                    "Body",
                    new ExtensionObject(new Sample { Value = 7 })));
        }

        [Test]
        public void PreEncodedExtensionObjectBodyRoundTrips()
        {
            ServiceMessageContext context = CreateContext();

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteExtensionObject(
                    "Body",
                    new ExtensionObject(new Sample { Value = 7 }));
                buffer = encoder.CloseAndReturnBuffer()!;
            }

            using var decoder = new BinaryDecoder(buffer!, context);
            ExtensionObject decoded = decoder.ReadExtensionObject("Body");

            Assert.That(decoded.TryGetValue(out Sample? sample), Is.True);
            Assert.That(sample!.Value, Is.EqualTo(7));
        }

        [Test]
        public void NestedXmlDecoderInheritsTheNamespaceMappings()
        {
            // An ExtensionObject with an XML body was decoded by a fresh
            // XmlDecoder that had no mapping tables, so node ids in the body
            // were not remapped.
            ServiceMessageContext context = CreateContext();
            const string xml = "<NodeId xmlns=\"" + kNs + "\"><Identifier>ns=1;i=5</Identifier></NodeId>";

            using var decoder = new XmlDecoder(
                XmlReader.Create(
                    new StringReader(xml),
                    CoreUtils.DefaultXmlReaderSettings()),
                context);
            decoder.InheritDecodingState([0, 3], null, 0);
            decoder.PushNamespace(kNs);

            NodeId value = decoder.ReadNodeId("NodeId");

            Assert.That(value.NamespaceIndex, Is.EqualTo((ushort)3));
        }

        [Test]
        public void ReadEncodeableRejectsAWireTypeThatIsNotTheRequestedType()
        {
            // The activator result was cast unchecked, producing an
            // InvalidCastException instead of a decoding error.
            ServiceMessageContext context = CreateContext();

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteEncodeable("Value", new Sample { Value = 1 }, Sample.TypeIdStatic);
                buffer = encoder.CloseAndReturnBuffer()!;
            }

            using var decoder = new BinaryDecoder(buffer!, context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeable<ReadValueId>(null, Sample.TypeIdStatic));
            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadDecodingError));
        }

        [Test]
        public void Base64ByteStringIsNotGatedByMaxStringLength()
        {
            // XmlParser read the base64 text through the String limit, so blobs
            // well within MaxByteStringLength failed to load.
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 8;
            context.MaxByteStringLength = 1024;

            byte[] payload = new byte[64];
            for (int ii = 0; ii < payload.Length; ii++)
            {
                payload[ii] = (byte)ii;
            }

            string xml =
                "<ByteString xmlns=\"" + kNs + "\">" +
                Convert.ToBase64String(payload) +
                "</ByteString>";

            using var parser = new XmlParser(xml, context);
            parser.PushNamespace(kNs);

            ByteString value = parser.ReadByteString("ByteString");

            Assert.That(value.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public void WhitespaceOnlyStringIsWrittenOut()
        {
            // The encoder dropped the content of a whitespace only string,
            // turning it into an empty string on the wire.
            ServiceMessageContext context = CreateContext();

            string xml;
            using (var encoder = new XmlEncoder(context))
            {
                encoder.PushNamespace(kNs);
                encoder.WriteString("Value", "   ");
                encoder.PopNamespace();
                xml = encoder.CloseAndReturnText()!;
            }

            Assert.That(xml, Does.Contain("   "));
        }

        [Test]
        [TestCase("  secret ")]
        [TestCase("\tTag\n")]
        [TestCase("x")]
        [TestCase("   ")]
        [TestCase("\n  \t")]
        public void XmlStringKeepsLeadingAndTrailingWhitespace(string value)
        {
            // ReadString trimmed the value although xs:string preserves
            // whitespace (Part 6 5.3.1.5).
            ServiceMessageContext context = CreateContext();

            string xml;
            using (var encoder = new XmlEncoder(context))
            {
                encoder.PushNamespace(kNs);
                encoder.WriteString("Value", value);
                encoder.PopNamespace();
                xml = encoder.CloseAndReturnText()!;
            }

            string textXml;
            using (var encoder = new XmlEncoder(context))
            {
                encoder.PushNamespace(kNs);
                encoder.WriteLocalizedText("Text", new LocalizedText(" en ", value));
                encoder.PopNamespace();
                textXml = encoder.CloseAndReturnText()!;
            }

            using (var decoder = new XmlDecoder(
                XmlReader.Create(new StringReader(xml!), CoreUtils.DefaultXmlReaderSettings()),
                context))
            {
                decoder.PushNamespace(kNs);
                Assert.That(decoder.ReadString("Value"), Is.EqualTo(value));
            }

            using (var decoder = new XmlDecoder(
                XmlReader.Create(new StringReader(textXml!), CoreUtils.DefaultXmlReaderSettings()),
                context))
            {
                decoder.PushNamespace(kNs);
                LocalizedText text = decoder.ReadLocalizedText("Text");
                Assert.That(text.Text, Is.EqualTo(value));
                Assert.That(text.Locale, Is.EqualTo(" en "));
            }

            using var parser = new XmlParser(xml, context);
            parser.PushNamespace(kNs);
            Assert.That(parser.ReadString("Value"), Is.EqualTo(value));
        }

        [Test]
        public void XmlWhitespaceOnlyStringIsEmptyWhenOptedIn()
        {
            // Pretty-printed NodeSets write an empty Locale as layout whitespace;
            // their importers opt in to read such an element as "".
            ServiceMessageContext context = CreateContext();
            const string xml =
                "<Text xmlns=\"" + kNs + "\">\n  <Locale>\n  </Locale>\n  <Text>Site</Text>\n</Text>";

            using (var decoder = new XmlDecoder(
                XmlReader.Create(new StringReader(xml), CoreUtils.DefaultXmlReaderSettings()),
                context))
            {
                decoder.PushNamespace(kNs);
                Assert.That(decoder.ReadLocalizedText("Text").Locale, Is.EqualTo("\n  "));
            }

            using (var decoder = new XmlDecoder(
                XmlReader.Create(new StringReader(xml), CoreUtils.DefaultXmlReaderSettings()),
                context)
            {
                TreatWhitespaceOnlyStringsAsEmpty = true
            })
            {
                decoder.PushNamespace(kNs);
                LocalizedText text = decoder.ReadLocalizedText("Text");
                Assert.That(text.Locale, Is.Null);
                Assert.That(text.Text, Is.EqualTo("Site"));
            }
        }

        [Test]
        [TestCase("\n  i=85\n", "i=85")]
        [TestCase("ns=2;i=5 ", "ns=2;i=5")]
        [TestCase("\n  ns=1;s=Tag \n", "ns=1;s=Tag ")]
        [TestCase(" s= a ", "s= a ")]
        [TestCase("\n  nsu=urn:x;s=b \n", "nsu=urn:x;s=b ")]
        [TestCase("\n  ns=1;s=Tag  \n    ", "ns=1;s=Tag  ")]
        [TestCase("ns=1;s=Tag\t\r\n", "ns=1;s=Tag")]
        [TestCase("ns=1;s=Tag\u0085", "ns=1;s=Tag")]
        public void XmlNodeIdIdentifierIgnoresLayoutWhitespace(string identifier, string expected)
        {
            // ReadString keeps xs:string whitespace, but the NodeId parsers
            // accept none, so a pretty-printed Identifier failed to decode.
            // String ids keep trailing spaces but not control characters
            // (Part 3 8.2.4).
            ServiceMessageContext context = CreateContext();
            string xml =
                "<Node xmlns=\"" + kNs + "\"><Identifier>" + identifier + "</Identifier></Node>";
            bool isAbsolute = expected.StartsWith("nsu=", StringComparison.Ordinal);

            if (!isAbsolute)
            {
                using var decoder = new XmlDecoder(
                    XmlReader.Create(new StringReader(xml), CoreUtils.DefaultXmlReaderSettings()),
                    context);
                decoder.PushNamespace(kNs);
                Assert.That(decoder.ReadNodeId("Node"), Is.EqualTo(NodeId.Parse(expected)));

                using var parser = new XmlParser(xml, context);
                parser.PushNamespace(kNs);
                Assert.That(parser.ReadNodeId("Node"), Is.EqualTo(NodeId.Parse(expected)));
            }

            using (var decoder = new XmlDecoder(
                XmlReader.Create(new StringReader(xml), CoreUtils.DefaultXmlReaderSettings()),
                context))
            {
                decoder.PushNamespace(kNs);
                Assert.That(
                    decoder.ReadExpandedNodeId("Node"),
                    Is.EqualTo(ExpandedNodeId.Parse(expected)));
            }

            using var expandedParser = new XmlParser(xml, context);
            expandedParser.PushNamespace(kNs);
            Assert.That(
                expandedParser.ReadExpandedNodeId("Node"),
                Is.EqualTo(ExpandedNodeId.Parse(expected)));
        }

        [Test]
        public void XmlStringLengthIsMeasuredInBytesNotCharacters()
        {
            // The XML codec counted UTF-16 code units while the binary and JSON
            // codecs count UTF-8 bytes, so one context gave two answers and a
            // string a binary peer rejects survived an XML round trip.
            // MaxStringLength is a byte count per Part 3 5.6.4 and Part 5 6.3.2.
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 4;

            // Three characters, six UTF-8 bytes.
            const string text = "äöü";

            using var encoder = new XmlEncoder(context);
            encoder.PushNamespace(kNs);

            Assert.Multiple(() =>
            {
                Assert.That(
                    Assert.Throws<ServiceResultException>(
                        () => encoder.WriteString("Value", text))!.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));

                Assert.That(
                    Assert.Throws<ServiceResultException>(
                        () => ReadStringFromXml(text, context, useParser: false))!.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(
                    Assert.Throws<ServiceResultException>(
                        () => ReadStringFromXml(text, context, useParser: true))!.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
            });
        }

        [Test]
        public void XmlStringAtExactlyTheByteLimitIsAccepted()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 6;
            const string text = "äöü";

            using var encoder = new XmlEncoder(context);
            encoder.PushNamespace(kNs);

            Assert.Multiple(() =>
            {
                Assert.DoesNotThrow(() => encoder.WriteString("Value", text));
                Assert.That(ReadStringFromXml(text, context, useParser: false), Is.EqualTo(text));
                Assert.That(ReadStringFromXml(text, context, useParser: true), Is.EqualTo(text));
            });
        }

        private static string ReadStringFromXml(
            string text,
            IServiceMessageContext context,
            bool useParser)
        {
            string document =
                $"<Value xmlns=\"{kNs}\">{text}</Value>";

            if (useParser)
            {
                using var parser = new XmlParser(document, context);
                parser.PushNamespace(kNs);
                return parser.ReadString("Value")!;
            }

            using var decoder = new XmlDecoder(
                XmlReader.Create(
                    new StringReader(document),
                    CoreUtils.DefaultXmlReaderSettings()),
                context);
            decoder.PushNamespace(kNs);
            return decoder.ReadString("Value")!;
        }

        [Test]
        public void EqualXmlElementsHashTheSame()
        {
            // Equals compares structurally through XNode.DeepEquals, which
            // ignores the quote character, character entities, insignificant
            // whitespace inside the tag and an XML declaration, while
            // GetHashCode hashed the raw text - so two equal elements landed in
            // different buckets of every hash container.
            var pairs = new[]
            {
                (XmlElement.From("<a x='1'/>"), XmlElement.From("<a x=\"1\"/>")),
                (XmlElement.From("<a>&#65;</a>"), XmlElement.From("<a>A</a>")),
                (XmlElement.From("<a   x=\"1\"  />"), XmlElement.From("<a x=\"1\"/>")),
                (
                    XmlElement.From("<?xml version=\"1.0\"?><a x=\"1\"/>"),
                    XmlElement.From("<a x=\"1\"/>")
                )
            };

            Assert.Multiple(() =>
            {
                foreach ((XmlElement first, XmlElement second) in pairs)
                {
                    Assert.That(first, Is.EqualTo(second), $"{first} vs {second}");
                    Assert.That(
                        first.GetHashCode(),
                        Is.EqualTo(second.GetHashCode()),
                        $"{first} vs {second}");
                }

                // Unequal elements still hash apart, and a malformed document
                // is hashed by its text, matching how Equals compares it.
                Assert.That(
                    XmlElement.From("<a>1</a>").GetHashCode(),
                    Is.Not.EqualTo(XmlElement.From("<a>2</a>").GetHashCode()));
                Assert.That(
                    XmlElement.From("<not well formed").GetHashCode(),
                    Is.EqualTo(XmlElement.From("<not well formed").GetHashCode()));
            });
        }

        [Test]
        public void XmlDeclarationIsNotInjectedIntoTheFieldElement()
        {
            // An XmlElement whose body starts with an XML declaration parses
            // fine, so the validation accepted it, but WriteRaw then put the
            // declaration in the middle of the document - where it is illegal -
            // and produced XML no decoder can read.
            ServiceMessageContext context = CreateContext();

            using var encoder = new XmlEncoder(context);
            encoder.PushNamespace(kNs);
            encoder.WriteXmlElement(
                "Body",
                XmlElement.From("<?xml version=\"1.0\" encoding=\"utf-8\"?><a x=\"1\" />"));
            string xml = encoder.CloseAndReturnText()!;

            Assert.Multiple(() =>
            {
                Assert.That(xml, Does.Not.Contain("<?xml version=\"1.0\" encoding=\"utf-8\"?><a"));
                // the body is in no namespace, not in the default namespace in scope.
                Assert.That(xml, Does.Contain("<a xmlns=\"\" x=\"1\""));

                // and the result is parseable, which is the point of the check.
                Assert.DoesNotThrow(() => XDocument.Parse(xml!));
            });
        }

        [Test]
        public void MalformedXmlElementIsRejectedInsteadOfWrittenRaw()
        {
            // WriteRaw bypasses the writer's checks, so an unparsable body was
            // written verbatim and produced a document no decoder can read.
            ServiceMessageContext context = CreateContext();

            using var encoder = new XmlEncoder(context);
            encoder.PushNamespace(kNs);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteXmlElement("Body", XmlElement.From("<not well formed")));
            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadEncodingError));
        }

        [Test]
        public void CloseThenDisposeDoesNotFlushADisposedWriter()
        {
            // Close() disposed the writer but kept the reference, so Dispose()
            // flushed it again - which throws for every owned stream that is
            // not a MemoryStream.
            ServiceMessageContext context = CreateContext();
            var stream = new ThrowAfterDisposeStream();

            var encoder = new BinaryEncoder(stream, context, leaveOpen: false);
            encoder.WriteInt32("Value", 1);
            encoder.Close();

            Assert.DoesNotThrow(encoder.Dispose);
        }

        /// <summary>
        /// A stream that behaves like a FileStream: flushing it after it has
        /// been disposed throws.
        /// </summary>
        private sealed class ThrowAfterDisposeStream : Stream
        {
            public override bool CanRead => !m_disposed;
            public override bool CanSeek => false;
            public override bool CanWrite => !m_disposed;
            public override long Length => m_length;

            public override long Position
            {
                get => m_length;
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(ThrowAfterDisposeStream));
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(ThrowAfterDisposeStream));
                }
                m_length += count;
            }

            protected override void Dispose(bool disposing)
            {
                m_disposed = true;
                base.Dispose(disposing);
            }

            private long m_length;
            private bool m_disposed;
        }

        private static global::Opc.Ua.Encoders.Union CreateUnion()
        {
            var definition = new StructureDefinition
            {
                StructureType = StructureType.Union,
                Fields = new StructureField[]
                {
                    new() { Name = "A", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar },
                    new() { Name = "B", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }
                }.ToArrayOf()
            };
            return new global::Opc.Ua.Encoders.Union(
                new XmlQualifiedName("TestUnion", kNs),
                new ExpandedNodeId(88821, 0),
                new ExpandedNodeId(88822, 0),
                new ExpandedNodeId(88823, 0),
                definition,
                new System.Collections.Generic.Dictionary<string, BuiltInType>
                {
                    ["A"] = BuiltInType.Int32,
                    ["B"] = BuiltInType.Int32
                });
        }

        [Test]
        [TestCase(3u)]
        [TestCase(0x7FFFFFFFu)]
        [TestCase(0x80000001u)]
        public void UnionWithOutOfRangeSwitchFieldIsADecodingError(uint selector)
        {
            // Decode stored any selector; Value then threw
            // ArgumentOutOfRangeException and Encode wrote an invalid union.
            ServiceMessageContext context = CreateContext();
            byte[] buffer = BitConverter.GetBytes(selector);
            global::Opc.Ua.Encoders.Union union = CreateUnion();

            using var decoder = new BinaryDecoder(buffer, context);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => union.Decode(decoder));
            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadDecodingError));
        }

        [Test]
        public void UnionWithLastSwitchFieldDecodes()
        {
            ServiceMessageContext context = CreateContext();
            byte[] buffer = [2, 0, 0, 0, 42, 0, 0, 0];
            global::Opc.Ua.Encoders.Union union = CreateUnion();

            using var decoder = new BinaryDecoder(buffer, context);
            union.Decode(decoder);

            Assert.That(union.SwitchField, Is.EqualTo(2u));
            Assert.That(union.Value, Is.EqualTo(new Variant(42)));
        }

        private static Nested CreateNestedChain(int depth)
        {
            var root = new Nested();
            Nested current = root;
            for (int ii = 0; ii < depth; ii++)
            {
                var child = new Nested();
                current.Child = new ExtensionObject(child);
                current = child;
            }
            return root;
        }

        [Test]
        public void SeekableBinaryEncoderCountsExtensionObjectBodiesAgainstNestingLimit()
        {
            // The seekable stream path encoded the body without incrementing
            // the nesting level while the decoder counts every body.
            ServiceMessageContext context = CreateContext();
            context.MaxEncodingNestingLevels = 4;
            Nested root = CreateNestedChain(10);

            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, context, true);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteExtensionObject("Body", new ExtensionObject(root)));
            Assert.That(
                ex.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void SeekableBinaryEncoderNestedExtensionObjectsWithinLimitRoundTrip()
        {
            ServiceMessageContext context = CreateContext();
            context.Factory.AddEncodeableType(typeof(Nested));
            context.MaxEncodingNestingLevels = 8;
            Nested root = CreateNestedChain(3);

            byte[] buffer;
            using (var stream = new MemoryStream())
            {
                using (var encoder = new BinaryEncoder(stream, context, true))
                {
                    encoder.WriteExtensionObject("Body", new ExtensionObject(root));
                }
                buffer = stream.ToArray();
            }

            using var decoder = new BinaryDecoder(buffer, context);
            ExtensionObject decoded = decoder.ReadExtensionObject("Body");
            Assert.That(decoded.TryGetValue(out Nested? result), Is.True);
            Assert.That(result!.IsEqual(root), Is.True);
        }

        [Test]
        public void XmlEncoderCountsExtensionObjectBodiesAgainstNestingLimit()
        {
            // WriteExtensionObjectBody encoded the body without incrementing
            // the nesting level while XmlDecoder counts every body.
            ServiceMessageContext context = CreateContext();
            context.MaxEncodingNestingLevels = 4;
            Nested root = CreateNestedChain(10);

            using var encoder = new XmlEncoder(context);
            encoder.PushNamespace(kNs);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteExtensionObject("Body", new ExtensionObject(root)));
            Assert.That(
                ex.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// An encodeable with an ExtensionObject field used to build nested bodies.
        /// </summary>
        public sealed class Nested : IEncodeable
        {
            public ExtensionObject Child { get; set; }

            public ExpandedNodeId TypeId => new(88811, 0);
            public ExpandedNodeId BinaryEncodingId => new(88812, 0);
            public ExpandedNodeId XmlEncodingId => new(88813, 0);

            public void Encode(IEncoder encoder)
            {
                encoder.WriteExtensionObject("Child", Child);
            }

            public void Decode(IDecoder decoder)
            {
                Child = decoder.ReadExtensionObject("Child");
            }

            public bool IsEqual(IEncodeable? encodeable)
            {
                if (encodeable is not Nested other)
                {
                    return false;
                }
                bool hasChild = Child.TryGetValue(out Nested? mine);
                bool otherHasChild = other.Child.TryGetValue(out Nested? theirs);
                if (hasChild != otherHasChild)
                {
                    return false;
                }
                return !hasChild || mine!.IsEqual(theirs!);
            }

            public object Clone()
            {
                return new Nested { Child = Child };
            }
        }

        /// <summary>
        /// A minimal encodeable used as an ExtensionObject body.
        /// </summary>
        public sealed class Sample : IEncodeable
        {
            public static readonly ExpandedNodeId TypeIdStatic = new(88801, 0);

            public int Value { get; set; }

            public ExpandedNodeId TypeId => TypeIdStatic;
            public ExpandedNodeId BinaryEncodingId => new(88802, 0);
            public ExpandedNodeId XmlEncodingId => new(88803, 0);

            public void Encode(IEncoder encoder)
            {
                encoder.WriteInt32("Value", Value);
            }

            public void Decode(IDecoder decoder)
            {
                Value = decoder.ReadInt32("Value");
            }

            public bool IsEqual(IEncodeable? encodeable)
            {
                return encodeable is Sample other && other.Value == Value;
            }

            public object Clone()
            {
                return new Sample { Value = Value };
            }
        }
    }
}
