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

using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// OPC 10000-3 §7.15 lets only an ObjectType, a VariableType or a Method
    /// be the SourceNode of a <c>GeneratesEvent</c> Reference. A Thing
    /// Description projects an instance, so the events it declares are stated
    /// by a type synthesized for it rather than by the instance, and the
    /// reverse conversion folds that type back into the document.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotInstanceEventSourceTests
    {
        private const string ObjectDescription =
            "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
            "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
            "\"@type\":\"uav:object\",\"title\":\"Pump01\"," +
            "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;Pump\"," +
            "\"properties\":{\"speed\":{\"@type\":\"uav:variable\"," +
            "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;Speed\"," +
            "\"type\":\"number\",\"readOnly\":true}}," +
            "\"events\":{\"overTemp\":{\"title\":\"OverTemp\"," +
            "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;OverTemp\"}}}";

        private const string ThingModel =
            "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
            "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
            "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
            "\"title\":\"PumpType\"," +
            "\"uav:browseName\":\"nsu=http://example.com/demo/pump;PumpType\"," +
            "\"events\":{\"overTemp\":{" +
            "\"uav:browseName\":\"nsu=http://example.com/demo/pump;OverTemp\"}}}";

        [Test]
        public void ThingDescriptionEventIsGeneratedByASynthesizedTypeNotTheInstance()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(WotTestData.Utf8(ObjectDescription));

            UAObject root = nodeSet.Items!.OfType<UAObject>().Single();
            UAObjectType eventType = EventTypeOf(nodeSet);
            UAObjectType carrier = nodeSet.Items!.OfType<UAObjectType>()
                .Single(type => type.BrowseName == "1:PumpType");

            Assert.Multiple(() =>
            {
                Assert.That(
                    root.References!.Any(reference => reference.ReferenceType == "GeneratesEvent"),
                    Is.False,
                    "An Object instance must not be the source of GeneratesEvent (OPC 10000-3 §7.15).");
                Assert.That(TypeDefinitionOf(root), Is.EqualTo(carrier.NodeId));
                Assert.That(carrier.IsAbstract, Is.False);
                Assert.That(
                    carrier.References!.Select(reference =>
                        (reference.ReferenceType, reference.IsForward, reference.Value)),
                    Is.EquivalentTo(new[]
                    {
                        ("HasSubtype", false, "i=58"),
                        ("GeneratesEvent", true, eventType.NodeId)
                    }));
            });
        }

        [Test]
        public void VariableThingDescriptionEventIsGeneratedByASynthesizedVariableType()
        {
            const string description =
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":\"uav:variable\",\"title\":\"Level\"," +
                "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;Level\"," +
                "\"events\":{\"overflow\":{\"title\":\"Overflow\"," +
                "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;Overflow\"}}}";

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(WotTestData.Utf8(description));

            UAVariable root = nodeSet.Items!.OfType<UAVariable>().Single();
            UAVariableType carrier = nodeSet.Items!.OfType<UAVariableType>().Single();

            Assert.Multiple(() =>
            {
                Assert.That(
                    root.References!.Any(reference => reference.ReferenceType == "GeneratesEvent"),
                    Is.False,
                    "A Variable instance must not be the source of GeneratesEvent (OPC 10000-3 §7.15).");
                Assert.That(TypeDefinitionOf(root), Is.EqualTo(carrier.NodeId));
                Assert.That(carrier.BrowseName, Is.EqualTo("1:LevelType"));
                Assert.That(carrier.DataType, Is.EqualTo(root.DataType));
                Assert.That(carrier.ValueRank, Is.EqualTo(root.ValueRank));
                Assert.That(
                    carrier.References!.Select(reference =>
                        (reference.ReferenceType, reference.IsForward, reference.Value)),
                    Is.EquivalentTo(new[]
                    {
                        ("HasSubtype", false, "i=63"),
                        ("GeneratesEvent", true, EventTypeOf(nodeSet).NodeId)
                    }));
            });
        }

        [Test]
        public void AuthoredAlwaysGeneratesEventLinkIsMovedOffTheInstance()
        {
            const string description =
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"}]," +
                "\"@type\":\"uav:object\",\"title\":\"Pump01\"," +
                "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;Pump\"," +
                "\"links\":[{\"rel\":\"ua:AlwaysGeneratesEvent\",\"href\":\"i=2041\",\"uav:refId\":\"i=3065\"}]}";

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(WotTestData.Utf8(description));

            UAObject root = nodeSet.Items!.OfType<UAObject>().Single();
            UAObjectType carrier = nodeSet.Items!.OfType<UAObjectType>().Single();

            Assert.Multiple(() =>
            {
                Assert.That(
                    root.References!.Any(reference => reference.Value == "i=2041"),
                    Is.False,
                    "AlwaysGeneratesEvent is a GeneratesEvent subtype, so an instance cannot be its source.");
                Assert.That(TypeDefinitionOf(root), Is.EqualTo(carrier.NodeId));
                Assert.That(
                    carrier.References!.Any(reference => reference.IsForward && reference.Value == "i=2041"),
                    Is.True);
            });
        }

        [Test]
        public void EventsAreFoundThroughDeclaredAliases()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test:aliases"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:aliases" }],
                Aliases =
                [
                    new NodeIdAlias { Alias = "TypeOf", Value = "i=40" },
                    new NodeIdAlias { Alias = "SubtypeOf", Value = "i=45" },
                    new NodeIdAlias { Alias = "Raises", Value = "i=3065" },
                    new NodeIdAlias { Alias = "PumpEvents", Value = "ns=1;i=2000" },
                    new NodeIdAlias { Alias = "OverTempEvent", Value = "ns=1;i=1002" }
                ],
                Items =
                [
                    new UAObject
                    {
                        NodeId = "ns=1;i=5001",
                        BrowseName = "1:Pump",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "Pump" }],
                        References =
                        [
                            new Reference { ReferenceType = "TypeOf", IsForward = true, Value = "PumpEvents" }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=2000",
                        BrowseName = "1:PumpType",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "PumpType" }],
                        References =
                        [
                            new Reference { ReferenceType = "SubtypeOf", IsForward = false, Value = "i=58" },
                            new Reference { ReferenceType = "Raises", IsForward = true, Value = "OverTempEvent" }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1002",
                        BrowseName = "1:OverTemp",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "OverTemp" }],
                        References =
                        [
                            new Reference { ReferenceType = "SubtypeOf", IsForward = false, Value = "i=2041" }
                        ]
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(nodeSet);
            Assert.That(result.Success, Is.True);
            using WotDocument document = result.Value!;

            Assert.That(document.RootElement.GetProperty("@type").GetString(), Is.EqualTo("uav:object"),
                "A type that only carries the instance's events is not the document root.");
            Assert.That(document.Events.Keys, Is.EquivalentTo(s_overTemp));
        }

        [Test]
        public void ThingModelKeepsGeneratesEventOnTheProjectedType()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(WotTestData.Utf8(ThingModel));

            UAObjectType root = nodeSet.Items!.OfType<UAObjectType>()
                .Single(type => type.References!.Any(
                    reference => !reference.IsForward && reference.Value == "i=58"));

            Assert.That(
                root.References!.Any(reference =>
                    reference.ReferenceType == "GeneratesEvent" && reference.IsForward),
                Is.True);
            Assert.That(nodeSet.Items!.OfType<UAObjectType>().Count(), Is.EqualTo(2),
                "A Thing Model is a type itself, so no type is synthesized for its events.");
        }

        [Test]
        public void ThingDescriptionWithoutEventsGetsNoSynthesizedType()
        {
            const string description =
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":\"uav:object\",\"title\":\"Pump01\"," +
                "\"uav:browseName\":\"nsu=urn:opcua:wot:synthesized;Pump\"}";

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(WotTestData.Utf8(description));

            Assert.That(nodeSet.Items!.OfType<UAObjectType>(), Is.Empty);
            Assert.That(TypeDefinitionOf(nodeSet.Items!.OfType<UAObject>().Single()), Is.EqualTo("i=58"));
        }

        [Test]
        public void SynthesizedEventTypeIsFoldedBackIntoTheThingDescription()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(WotTestData.Utf8(ObjectDescription));

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(nodeSet);
            Assert.That(result.Success, Is.True);
            using WotDocument document = result.Value!;
            JsonElement root = document.RootElement;

            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("@type").GetString(), Is.EqualTo("uav:object"),
                    "The instance, not the type that carries its events, roots the document.");
                Assert.That(document.Events.Keys, Is.EquivalentTo(s_overTemp));
                Assert.That(
                    document.Links.Any(link =>
                        link.GetProperty("rel").GetString() == "ua:HasTypeDefinition"),
                    Is.False,
                    "The synthesized type derives from BaseObjectType, which an unbound document projects.");
                Assert.That(root.TryGetProperty("uav:nodes", out _), Is.False,
                    "The readable mapping reproduces the synthesized type, so no native projection is needed.");
                Assert.That(root.TryGetProperty("uav:nodeSet", out _), Is.False);
            });

            UANodeSet again = WotNodeSetConverter.ToNodeSet(document);
            UAObject instance = again.Items!.OfType<UAObject>().Single();
            Assert.That(
                instance.References!.Any(reference => reference.ReferenceType == "GeneratesEvent"),
                Is.False);
            Assert.That(
                TypeDefinitionOf(instance),
                Is.EqualTo(nodeSet.Items!.OfType<UAObjectType>()
                    .Single(type => type.BrowseName == "1:PumpType").NodeId));
        }

        [Test]
        public void ArchivedNodeSetAcceptsAnEventItsRootTypeGenerates()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(WotTestData.Utf8(ObjectDescription));
            using WotDocument archived = WotNodeSetConverter.FromNodeSet(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });
            Assert.That(archived.RootElement.TryGetProperty("uav:nodeSet", out _), Is.True);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(archived);

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty,
                "The archive states the event on the root's type, which is where §7.15 puts it.");
            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void InstanceLevelGeneratesEventIsStillReadAsAnEventAffordance()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test:legacy"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:legacy" }],
                Items =
                [
                    new UAObject
                    {
                        NodeId = "ns=1;i=5001",
                        BrowseName = "1:Pump",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "Pump" }],
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=58" },
                            new Reference { ReferenceType = "GeneratesEvent", IsForward = true, Value = "ns=1;i=1002" }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1002",
                        BrowseName = "1:OverTemp",
                        DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "OverTemp" }],
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=2041" }
                        ]
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(nodeSet);
            Assert.That(result.Success, Is.True);
            using WotDocument document = result.Value!;

            Assert.That(document.RootElement.GetProperty("@type").GetString(), Is.EqualTo("uav:object"));
            Assert.That(document.Events.Keys, Is.EquivalentTo(s_overTemp));
        }

        [Test]
        public async Task DocumentSetBindsTheInstanceToTheTypeThatCarriesItsEventsAsync()
        {
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(WotTestData.Utf8(ObjectDescription));

            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(nodeSet, "pump").ConfigureAwait(false);
            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
            using WotDocumentSet set = result.Value!;
            WotDocument instance = set.Entries.ToList()
                .Select(entry => entry.Document)
                .Single(document => document.RootElement.GetProperty("@type").ValueKind == JsonValueKind.String &&
                    document.RootElement.GetProperty("@type").GetString() == "uav:object");

            Assert.Multiple(() =>
            {
                Assert.That(instance.Events.Keys, Is.EquivalentTo(s_overTemp),
                    "The instance raises the events its type definition declares.");
                Assert.That(
                    instance.Links.Single(link =>
                        link.GetProperty("rel").GetString() == "ua:HasTypeDefinition")
                        .GetProperty("href").GetString(),
                    Does.EndWith("PumpType"),
                    "In a document set the carrying type is a Thing Model of its own.");
                Assert.That(
                    result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionIncomplete),
                    Is.False,
                    "The document set reproduces the source NodeSet readably.");
            });
        }

        private static readonly string[] s_overTemp = ["OverTemp"];

        private static UAObjectType EventTypeOf(UANodeSet nodeSet)
        {
            return nodeSet.Items!.OfType<UAObjectType>()
                .Single(type => type.References!.Any(
                    reference => !reference.IsForward && reference.Value == "i=2041"));
        }

        private static string? TypeDefinitionOf(UANode node)
        {
            return node.References!
                .SingleOrDefault(reference =>
                    reference.IsForward && reference.ReferenceType == "HasTypeDefinition")
                ?.Value;
        }
    }
}
