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
 *
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

using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotNativeProjectionAffordanceCoverageTests
    {
        [Test]
        public void NativeProjectionReportsReadableAffordanceItDoesNotCover()
        {
            byte[] json = BuildDocumentWithNativeProjection(includeActionNode: false);

            WotConversionResult<UANodeSet> result = Convert(json);

            WotDiagnostic diagnostic = result.Diagnostics.Single(
                d => d.Code == WotDiagnosticCode.NativeProjectionUncoveredAffordance);
            Assert.That(diagnostic.Severity, Is.EqualTo(WotDiagnosticSeverity.Warning));
            Assert.That(diagnostic.Location?.JsonPointer, Is.EqualTo("/actions/Reset"));
        }

        [Test]
        public void NativeProjectionDoesNotReportReadableAffordancesItCovers()
        {
            byte[] json = BuildDocumentWithNativeProjection(includeActionNode: true);

            WotConversionResult<UANodeSet> result = Convert(json);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.NativeProjectionUncoveredAffordance),
                Is.False);
        }

        [Test]
        public void SynthesizedDocumentDoesNotReportNativeCoverageDiagnostic()
        {
            byte[] json = BuildDocumentWithoutNativeProjection();

            WotConversionResult<UANodeSet> result = Convert(json);

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.NativeProjectionUncoveredAffordance),
                Is.False);
        }

        private static WotConversionResult<UANodeSet> Convert(byte[] json)
        {
            using WotDocument document = WotDocument.Parse(json);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }

        private static byte[] BuildDocumentWithNativeProjection(bool includeActionNode)
        {
            UANodeSet nodeSet = CreateProjectedNodeSet(includeActionNode);
            var diagnostics = new System.Collections.Generic.List<WotDiagnostic>();
            byte[] projection = WotNativeProjection.Write(
                nodeSet,
                new WotNodeSetConverterOptions(),
                diagnostics);
            Assert.That(diagnostics, Is.Empty);

            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                WriteReadableDocument(writer);
                writer.WritePropertyName("uav:nodes");
                writer.WriteRawValue(projection);
                writer.WriteEndObject();
            }
            return output.ToArray();
        }

        private static byte[] BuildDocumentWithoutNativeProjection()
        {
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                WriteReadableDocument(writer);
                writer.WriteEndObject();
            }
            return output.ToArray();
        }

        private static void WriteReadableDocument(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("@context");
            writer.WriteStartObject();
            writer.WriteString("uav", WotNodeSetConverter.VocabularyNamespace);
            writer.WriteEndObject();
            writer.WriteString("@type", "uav:object");
            writer.WriteString("title", "Pump");
            writer.WriteString("uav:id", "nsu=urn:test:model;s=Pump");
            writer.WriteString("uav:browseName", "nsu=urn:test:model;Pump");

            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            writer.WritePropertyName("Temperature");
            writer.WriteStartObject();
            writer.WriteString("type", "number");
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WritePropertyName("actions");
            writer.WriteStartObject();
            writer.WritePropertyName("Reset");
            writer.WriteStartObject();
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WritePropertyName("events");
            writer.WriteStartObject();
            writer.WritePropertyName("Alarm");
            writer.WriteStartObject();
            writer.WriteString("uav:browseName", "nsu=urn:test:model;Alarm");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        private static UANodeSet CreateProjectedNodeSet(bool includeActionNode)
        {
            var items = new System.Collections.Generic.List<UANode>
            {
                new UAObject
                {
                    NodeId = "ns=1;s=Pump",
                    BrowseName = "1:Pump",
                    DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "Pump" }],
                    References =
                    [
                        new Reference { ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=58" },
                        new Reference
                        {
                            ReferenceType = "HasComponent",
                            IsForward = true,
                            Value = "ns=1;s=Pump/Temperature"
                        },
                        new Reference
                        {
                            ReferenceType = "GeneratesEvent",
                            IsForward = true,
                            Value = "ns=1;s=Pump/Alarm"
                        }
                    ]
                },
                new UAVariable
                {
                    NodeId = "ns=1;s=Pump/Temperature",
                    BrowseName = "1:Temperature",
                    DataType = "Double",
                    ParentNodeId = "ns=1;s=Pump",
                    References =
                    [
                        new Reference { ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=63" },
                        new Reference
                        {
                            ReferenceType = "HasComponent",
                            IsForward = false,
                            Value = "ns=1;s=Pump"
                        }
                    ]
                },
                new UAObjectType
                {
                    NodeId = "ns=1;s=Pump/Alarm",
                    BrowseName = "1:Alarm",
                    References =
                    [
                        new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=2041" }
                    ]
                }
            };

            if (includeActionNode)
            {
                items[0].References =
                [
                    .. items[0].References!,
                    new Reference { ReferenceType = "HasComponent", IsForward = true, Value = "ns=1;s=Pump/Reset" }
                ];
                items.Add(new UAMethod
                {
                    NodeId = "ns=1;s=Pump/Reset",
                    BrowseName = "1:Reset",
                    ParentNodeId = "ns=1;s=Pump",
                    References =
                    [
                        new Reference
                        {
                            ReferenceType = "HasComponent",
                            IsForward = false,
                            Value = "ns=1;s=Pump"
                        }
                    ]
                });
            }

            return new UANodeSet
            {
                NamespaceUris = ["urn:test:model"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:model" }],
                Items = [.. items]
            };
        }
    }
}
