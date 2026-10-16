/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Machinery.Result;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Exercises GMS workspace polymorphism and Machinery Result outputs through registered wire codecs.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    public sealed class ModelEncodingTests
    {
        /// <summary>
        /// A base-typed workspace preserves its concrete dimensions and encoding identity across all wire encodings.
        /// </summary>
        [TestCase("Binary", false)]
        [TestCase("Binary", true)]
        [TestCase("Json", false)]
        [TestCase("Json", true)]
        [TestCase("Xml", false)]
        [TestCase("Xml", true)]
        public void WorkspaceSubtypesRoundTripPolymorphically(string encoding, bool cylindrical)
        {
            WorkspaceType expected = CreateWorkspace(cylindrical);
            WorkspaceType actual = RoundTrip(expected, encoding);
            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.Not.SameAs(expected));
                Assert.That(actual.IsEqual(expected), Is.True);
                Assert.That(actual.TypeId, Is.EqualTo(new ExpandedNodeId(
                    cylindrical ? 3008u : 3007u, GmsModel.NamespaceUri)));
                Assert.That(actual.BinaryEncodingId, Is.EqualTo(new ExpandedNodeId(
                    cylindrical ? 5013u : 5007u, GmsModel.NamespaceUri)));
            });
            if (cylindrical)
            {
                Assert.That(actual, Is.TypeOf<CylindricalWorkspaceType>());
                var workspace = (CylindricalWorkspaceType)actual;
                Assert.That(workspace.Length, Is.EqualTo(450.25));
                Assert.That(workspace.Radius, Is.EqualTo(70.125));
            }
            else
            {
                Assert.That(actual, Is.TypeOf<CartesianWorkspaceType>());
                var workspace = (CartesianWorkspaceType)actual;
                Assert.That(workspace.Length, Is.EqualTo(450.25));
                Assert.That(workspace.Width, Is.EqualTo(125.5));
                Assert.That(workspace.Height, Is.EqualTo(80.75));
            }
        }

        /// <summary>
        /// The wire field order follows the XML definition rather than just a symmetric encoder/decoder implementation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void WorkspaceBinaryFieldOrderMatchesReleasedDefinition(bool cylindrical)
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext();
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteEncodeable("Workspace", CreateWorkspace(cylindrical));
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            Assert.That(decoder.ReadDouble(null), Is.EqualTo(450.25));
            if (cylindrical)
            {
                Assert.That(decoder.ReadDouble(null), Is.EqualTo(70.125));
            }
            else
            {
                Assert.That(decoder.ReadDouble(null), Is.EqualTo(125.5));
                Assert.That(decoder.ReadDouble(null), Is.EqualTo(80.75));
            }
            Assert.That(stream.Position, Is.EqualTo(stream.Length));
        }

        /// <summary>
        /// Both workspace encodings resolve through GMS registration inside the inherited result envelope.
        /// </summary>
        [TestCase("Binary")]
        [TestCase("Json")]
        [TestCase("Xml")]
        public void MachineryResultEnvelopePreservesGmsPayloadsAndMetadata(string encoding)
        {
            var expected = new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType
                {
                    ResultId = "measurement-42",
                    JobId = "inspection-batch-7",
                    CreationTime = new DateTimeUtc(2023, 7, 1, 12, 34, 56),
                    EncodingMask = (uint)(ResultMetaDataTypeFields.JobId | ResultMetaDataTypeFields.CreationTime)
                },
                ResultContent =
                [
                    Variant.FromStructure(CreateWorkspace(false)),
                    Variant.FromStructure(CreateWorkspace(true)),
                    new Variant(12.875),
                    new Variant("diameter")
                ]
            };
            ResultDataType actual = RoundTrip(expected, encoding);
            Assert.Multiple(() =>
            {
                Assert.That(actual.IsEqual(expected), Is.True);
                Assert.That(actual.ResultMetaData!.ResultId, Is.EqualTo("measurement-42"));
                Assert.That(actual.ResultMetaData.JobId, Is.EqualTo("inspection-batch-7"));
                Assert.That(actual.ResultMetaData.CreationTime, Is.EqualTo(new DateTimeUtc(2023, 7, 1, 12, 34, 56)));
                Assert.That(actual.ResultContent.Count, Is.EqualTo(4));
            });
            Assert.That(actual.ResultContent[0].TryGetValue(out ExtensionObject cartesian), Is.True);
            Assert.That(cartesian.TryGetValue(out CartesianWorkspaceType? first), Is.True);
            Assert.That(first!.Height, Is.EqualTo(80.75));
            Assert.That(actual.ResultContent[1].TryGetValue(out ExtensionObject cylindrical), Is.True);
            Assert.That(cylindrical.TryGetValue(out CylindricalWorkspaceType? second), Is.True);
            Assert.That(second!.Radius, Is.EqualTo(70.125));
            Assert.That(actual.ResultContent[2].TryGetValue(out double measured), Is.True);
            Assert.That(measured, Is.EqualTo(12.875));
        }

        /// <summary>
        /// A binary result-method response preserves its handle, typed result and domain error independently of status.
        /// </summary>
        [TestCase(0)]
        [TestCase(-17)]
        public void ResultMethodOutputsRetainTypedValuesAndDomainErrors(int error)
        {
            var expected = new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments =
                [
                    new Variant(42u),
                    Variant.FromStructure(new ResultDataType
                    {
                        ResultMetaData = new ResultMetaDataType { ResultId = "result-17" },
                        ResultContent = [Variant.FromStructure(CreateWorkspace(false))]
                    }),
                    new Variant(error)
                ]
            };
            CallMethodResult actual = RoundTrip(expected, "Binary");
            Assert.That(actual.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(actual.OutputArguments, Has.Count.EqualTo(3));
            Assert.That(actual.OutputArguments[0].TryGetValue(out uint handle), Is.True);
            Assert.That(handle, Is.EqualTo(42));
            Assert.That(actual.OutputArguments[1].TryGetValue(out ExtensionObject output), Is.True);
            Assert.That(output.TryGetValue(out ResultDataType? result), Is.True);
            Assert.That(result!.ResultMetaData!.ResultId, Is.EqualTo("result-17"));
            Assert.That(actual.OutputArguments[2].TryGetValue(out int actualError), Is.True);
            Assert.That(actualError, Is.EqualTo(error));
        }

        /// <summary>
        /// Unregistered GMS binary bodies remain opaque instead of being decoded through unrelated assembly metadata.
        /// </summary>
        [Test]
        public void CoreOnlyFactoryLeavesGmsWorkspaceOpaque()
        {
            ServiceMessageContext writer = ModelTestContext.CreateMessageContext();
            ServiceMessageContext reader = ModelTestContext.CreateMessageContext(registerModel: false);
            reader.NamespaceUris = writer.NamespaceUris;
            WorkspaceType expected = CreateWorkspace(false);
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, writer, true))
            {
                encoder.WriteExtensionObject("Workspace", new ExtensionObject(expected));
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, reader, true);
            ExtensionObject actual = decoder.ReadExtensionObject("Workspace");
            Assert.Multiple(() =>
            {
                Assert.That(actual.TypeId, Is.EqualTo(expected.BinaryEncodingId));
                Assert.That(actual.Encoding, Is.EqualTo(ExtensionObjectEncoding.Binary));
                Assert.That(actual.TryGetValue(out CartesianWorkspaceType? _), Is.False);
            });
        }

        /// <summary>
        /// Generated enumeration values use the published Int32 wire values, independent of CLR symbol spelling.
        /// </summary>
        [Test]
        public void EnumerationsEncodeTheirPublishedNumericValues()
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext();
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteEnumerated("Reason", MeasurementReasonEnum.MaxMastering);
                encoder.WriteEnumerated("Tolerance", ToleranceLimitEnum.NaturalLimit);
                encoder.WriteEnumerated("Alignment", ToolAlignmentState.Indexed);
                encoder.WriteEnumerated("Qualification", ToolIsQualifiedStatus.NotQualified);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            Assert.Multiple(() =>
            {
                Assert.That(decoder.ReadInt32(null), Is.EqualTo(5));
                Assert.That(decoder.ReadInt32(null), Is.EqualTo(2));
                Assert.That(decoder.ReadInt32(null), Is.EqualTo(1));
                Assert.That(decoder.ReadInt32(null), Is.EqualTo(2));
                Assert.That(stream.Position, Is.EqualTo(stream.Length));
            });
        }

        private static WorkspaceType CreateWorkspace(bool cylindrical)
        {
            return cylindrical
                ? new CylindricalWorkspaceType { Length = 450.25, Radius = 70.125 }
                : new CartesianWorkspaceType { Length = 450.25, Width = 125.5, Height = 80.75 };
        }

        private static T RoundTrip<T>(T value, string encoding)
            where T : class, IEncodeable
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext(reverseNamespaces: true);
            ExtensionObject decoded;
            switch (encoding)
            {
                case "Binary":
                    using (var stream = new MemoryStream())
                    {
                        using (var encoder = new BinaryEncoder(stream, context, true))
                        {
                            encoder.WriteExtensionObject("Value", new ExtensionObject(value));
                        }
                        stream.Position = 0;
                        using var decoder = new BinaryDecoder(stream, context, true);
                        decoded = decoder.ReadExtensionObject("Value");
                    }
                    break;
                case "Json":
                    string json;
                    using (var encoder = new JsonEncoder(context))
                    {
                        encoder.WriteExtensionObject("Value", new ExtensionObject(value));
                        json = encoder.CloseAndReturnText();
                    }
                    using (var decoder = new JsonDecoder(json, context))
                    {
                        decoded = decoder.ReadExtensionObject("Value");
                    }
                    break;
                case "Xml":
                    string xml;
                    using (var encoder = new XmlEncoder(context))
                    {
                        encoder.PushNamespace(Opc.Ua.Namespaces.OpcUaXsd);
                        encoder.WriteExtensionObject("Value", new ExtensionObject(value));
                        encoder.PopNamespace();
                        xml = encoder.CloseAndReturnText()!;
                    }
                    using (var text = new StringReader(xml))
                    using (var reader = XmlReader.Create(text, CoreUtils.DefaultXmlReaderSettings()))
                    using (var decoder = new XmlDecoder(reader, context))
                    {
                        decoder.PushNamespace(Opc.Ua.Namespaces.OpcUaXsd);
                        decoded = decoder.ReadExtensionObject("Value");
                        decoder.PopNamespace();
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(encoding));
            }
            Assert.That(decoded.TryGetValue(out T? result), Is.True);
            return result!;
        }
    }
}
