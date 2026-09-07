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
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// The EventType definitions the NodeSet converter emits (WoT Binding
    /// Section 6.1): a Thing Model of the EventType Node carries the portable
    /// identity, the effective <c>data</c> schema and the deterministic field
    /// order a fast path derives its baseline from, and an Object that raises
    /// the event names that document with <c>tm:ref</c>.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotEventTypeDefinitionTests
    {
        [Test]
        public void AnEventAffordanceStatesTheOrderItsFieldsAreDerivedIn()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(BuildNodeSet());

            JsonElement data = document.Events["OverTemperature"].GetProperty("data");
            List<string> order = ReadFieldOrder(data);
            List<string> properties = ReadProperties(data);

            Assert.Multiple(() =>
            {
                Assert.That(
                    order,
                    Is.EqualTo(properties).AsCollection,
                    "Every object with more than one property the derivation walks states " +
                    "uav:fieldOrder listing each of its properties exactly once.");
                Assert.That(order, Does.Contain("EventId"));
                Assert.That(
                    order[order.Count - 1],
                    Is.EqualTo("Temperature"),
                    "The inherited standard fields come first, then the fields the type " +
                    "declares itself in the order its References state them.");
                Assert.That(
                    data.GetProperty("properties").GetProperty("Temperature")
                        .GetProperty("uav:browseName").GetString(),
                    Is.EqualTo("ns1:Temperature"),
                    "A field of another NamespaceUri states its exact QualifiedName; a bare " +
                    "name cannot say which NamespaceUri qualifies it.");
            });
        }

        [Test]
        public void AStateVariableSchemaStatesItsOwnFieldOrder()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(BuildNodeSet());

            JsonElement enabledState = document.Events["OverTemperature"]
                .GetProperty("data")
                .GetProperty("properties")
                .GetProperty("EnabledState");

            Assert.That(
                ReadFieldOrder(enabledState),
                Is.EqualTo(s_enabledStateFieldOrder).AsCollection,
                "A nested object the derivation walks states its order too, so the two " +
                "clauses it yields are derived in a stated order.");
        }

        [Test]
        public void AnEventTypeThingModelCarriesTheDefinitionAFastPathLinksTo()
        {
            WotConversionResult<WotDocumentSet> result =
                WotNodeSetConverter.FromNodeSetDocuments(BuildNodeSet(), "pump");

            Assert.That(result.Value, Is.Not.Null, Describe(result));
            using WotDocumentSet set = result.Value!;
            WotDocument definition = FindEventTypeDocument(set);

            Assert.Multiple(() =>
            {
                Assert.That(
                    definition.TypeTokens,
                    Does.Contain(WotEventSelectClauses.EventTypeAnnotation));
                Assert.That(
                    definition.RootElement.GetProperty("uav:id").GetString(),
                    Is.EqualTo("nsu=urn:test:pump;i=1002"),
                    "The definition's identity is the TypeDefinitionId of every clause taken " +
                    "from it, so a consumer never has to invent one.");
                Assert.That(
                    definition.RootElement.TryGetProperty("data", out JsonElement data),
                    Is.True,
                    "A definition a fast path links to declares the field set it is selected " +
                    "from.");
                Assert.That(ReadFieldOrder(data), Is.Not.Empty);
                Assert.That(
                    definition.RootElement.TryGetProperty(
                        WotEventSelectClauses.Term, out _),
                    Is.False,
                    "A definition states the fields an EventType has and does not select them.");
                Assert.That(
                    definition.RootElement.TryGetProperty("tm:ref", out _),
                    Is.False,
                    "A document that projects the EventType itself does not reference itself.");
            });
        }

        [Test]
        public void AnObjectDocumentNamesTheSiblingEventTypeDefinition()
        {
            WotConversionResult<WotDocumentSet> result =
                WotNodeSetConverter.FromNodeSetDocuments(BuildNodeSet(), "pump");

            Assert.That(result.Value, Is.Not.Null, Describe(result));
            using WotDocumentSet set = result.Value!;
            WotDocument owner = FindOwnerDocument(set);
            WotDocument definition = FindEventTypeDocument(set);
            string definitionHref = set.Entries.ToList()
                .First(e => ReferenceEquals(e.Document, definition)).Href;

            JsonElement affordance = owner.Events.Values.Single();
            Assert.Multiple(() =>
            {
                Assert.That(
                    affordance.GetProperty(
                        WotEventSelectClauses.TypeDefinitionReferenceTerm).GetString(),
                    Is.EqualTo(definitionHref),
                    "The affordance names the sibling document that defines its EventType, " +
                    "so a consumer derives every select clause from that definition.");
                Assert.That(
                    affordance.TryGetProperty("data", out _),
                    Is.True,
                    "The affordance keeps the schema a consumer reads a notification with.");
            });
        }

        [Test]
        public async Task TheLinkedDefinitionResolvesToTheDerivedSelectionAsync()
        {
            WotConversionResult<WotDocumentSet> result =
                WotNodeSetConverter.FromNodeSetDocuments(BuildNodeSet(), "pump");

            Assert.That(result.Value, Is.Not.Null, Describe(result));
            using WotDocumentSet set = result.Value!;
            WotDocument owner = FindOwnerDocument(set);

            var resolver = new WotEventSelectionResolver(new DocumentSetResolver(set));
            WotConversionResult<WotEventSelectionCatalog> resolved = await resolver
                .ResolveAsync(owner)
                .ConfigureAwait(false);

            Assert.That(
                resolved.Value,
                Is.Not.Null,
                string.Join("; ", resolved.Diagnostics.Select(d => d.Message)));
            Assert.That(
                resolved.Value!.TryGetSelection(
                    "OverTemperature", out ArrayOf<WotResolvedEventSelectClause> clauses),
                Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(
                    clauses.ToList().All(c =>
                        c.TypeDefinitionId == "nsu=urn:test:pump;i=1002"),
                    Is.True,
                    "Every derived clause carries the linked definition's uav:id.");
                Assert.That(
                    clauses.ToList().Select(c => c.BrowsePath),
                    Does.Contain("ns1:Temperature"),
                    "The member's uav:browseName supplies the exact QualifiedName.");
                Assert.That(
                    clauses.ToList().Select(c => c.BrowsePath),
                    Does.Contain("EnabledState"),
                    "A state Variable's trailing Name is dropped: the clause naming the " +
                    "Variable itself supplies that object's Name member.");
            });
        }

        /// <summary>
        /// A fast-path affordance declares no <c>data</c> of its own, so its
        /// fields are the linked definition's. The synchronous conversion
        /// resolves nothing, and reports that instead of materializing an
        /// EventType with no field at all.
        /// </summary>
        [Test]
        public void AFastPathAffordanceWithoutAResolverIsReported()
        {
            using WotDocument document = Parse(FastPathDocument());

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                        .Select(d => d.Code),
                    Is.EqualTo(new[] { WotDiagnosticCode.EventSelectionUnresolved }),
                    Describe(result));
                Assert.That(
                    result.Diagnostics
                        .First(d => d.Code == WotDiagnosticCode.EventSelectionUnresolved)
                        .Message,
                    Does.Contain("ToNodeSetResultAsync").And.Contain("IWotThingResolver"),
                    "The report names the way to convert the document.");
            });
        }

        /// <summary>
        /// The throwing synchronous entry point fails rather than returning a
        /// NodeSet whose EventType quietly lost the linked fields.
        /// </summary>
        [Test]
        public void AFastPathAffordanceWithoutAResolverThrows()
        {
            using WotDocument document = Parse(FastPathDocument());

            Assert.That(
                () => WotNodeSetConverter.ToNodeSet(document),
                Throws.TypeOf<FormatException>());
        }

        /// <summary>
        /// An affordance that writes explicit clauses names the EventType each
        /// one is taken from, so it is resolved the same way and reported the
        /// same way where it is not.
        /// </summary>
        [Test]
        public void ExplicitClausesWithoutAResolverAreReported()
        {
            using WotDocument document = Parse(ExplicitClauseDocument());

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Code),
                Is.EqualTo(new[] { WotDiagnosticCode.EventSelectionUnresolved }),
                Describe(result));
        }

        /// <summary>
        /// Given the document the link names, the same conversion materializes
        /// the EventType's fields from the definition's effective schema
        /// (WoT Binding Section 6.1).
        /// </summary>
        [Test]
        public async Task AFastPathAffordanceMaterializesTheLinkedFieldsAsync()
        {
            using WotDocument document = Parse(FastPathDocument());

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(document, null, LinkedDefinition())
                .ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Is.Empty);
            Assert.That(
                result.Value!.Items.OfType<UAVariable>()
                    .Select(v => v.BrowseName),
                Does.Contain("1:Temperature"),
                "The field the linked definition declares and BaseEventType does not is the " +
                "one the projected type adds.");
        }

        /// <summary>
        /// An affordance that states no selection takes the implicit
        /// <c>BaseEventType</c> default, which needs no resolution: the
        /// synchronous conversion keeps converting it unchanged.
        /// </summary>
        [Test]
        public void AnAffordanceStatingNoSelectionStillConvertsSynchronously()
        {
            using WotDocument document = Parse(
                ThingModel(
                    "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\"," +
                    "\"uav:browseName\":\"pump:AlarmType\"," +
                    "\"data\":{\"type\":\"object\",\"properties\":{" +
                    "\"Temperature\":{\"type\":\"number\"}}}}}"));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                        .Select(d => d.Message),
                    Is.Empty);
                Assert.That(
                    result.Value!.Items.OfType<UAVariable>().Select(v => v.BrowseName),
                    Does.Contain("1:Temperature"));
            });
        }

        private static WotDocument Parse(string json)
        {
            return WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(json));
        }

        private static string ThingModel(string members)
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"Pump\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," + members + "}";
        }

        /// <summary>
        /// An affordance that names its EventType definition and declares no
        /// <c>data</c> of its own: without the definition it has no fields at
        /// all.
        /// </summary>
        private static string FastPathDocument()
        {
            return ThingModel(
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\"," +
                "\"uav:browseName\":\"pump:AlarmType\"," +
                "\"tm:ref\":\"./alarm.tm.jsonld\"}}");
        }

        private static string ExplicitClauseDocument()
        {
            return ThingModel(
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\"," +
                "\"uav:browseName\":\"pump:AlarmType\"," +
                "\"uav:eventSelectClauses\":[{\"tm:ref\":\"./alarm.tm.jsonld\"," +
                "\"uav:browsePath\":\"Temperature\"}]}}");
        }

        private static HrefResolver LinkedDefinition()
        {
            return new HrefResolver(
                "./alarm.tm.jsonld",
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:eventType\"]," +
                "\"title\":\"AlarmType\",\"uav:id\":\"nsu=urn:test:pump;i=1002\"," +
                "\"data\":{\"type\":\"object\"," +
                "\"uav:fieldOrder\":[\"EventId\",\"Temperature\"]," +
                "\"properties\":{\"EventId\":{\"type\":\"string\"}," +
                "\"Temperature\":{\"type\":\"number\"}}}}");
        }

        private static WotDocument FindEventTypeDocument(WotDocumentSet set)
        {
            return set.Entries.ToList()
                .Select(e => e.Document)
                .First(d => d.TypeTokens.Contains(WotEventSelectClauses.EventTypeAnnotation));
        }

        private static WotDocument FindOwnerDocument(WotDocumentSet set)
        {
            return set.Entries.ToList()
                .Select(e => e.Document)
                .First(d => d.Events.Count > 0 &&
                    !d.TypeTokens.Contains(WotEventSelectClauses.EventTypeAnnotation));
        }

        private static List<string> ReadFieldOrder(JsonElement schema)
        {
            return schema.TryGetProperty(
                WotEventSelectClauses.FieldOrderTerm, out JsonElement order) &&
                order.ValueKind == JsonValueKind.Array
                ? [.. order.EnumerateArray().Select(e => e.GetString()!)]
                : [];
        }

        private static List<string> ReadProperties(JsonElement schema)
        {
            return schema.TryGetProperty("properties", out JsonElement properties) &&
                properties.ValueKind == JsonValueKind.Object
                ? [.. properties.EnumerateObject().Select(p => p.Name)]
                : [];
        }

        private static string Describe<T>(WotConversionResult<T> result) where T : class
        {
            return string.Join("; ", result.Diagnostics.Select(d => d.Message));
        }

        /// <summary>
        /// An ObjectType that raises an EventType of its own, with one field
        /// the type declares in another NamespaceUri.
        /// </summary>
        private static UANodeSet BuildNodeSet()
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:pump"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:pump" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:PumpType",
                        DisplayName = [new Export.LocalizedText { Value = "PumpType" }],
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype", IsForward = false, Value = "i=58"
                            },
                            new Reference
                            {
                                ReferenceType = "GeneratesEvent",
                                IsForward = true,
                                Value = "ns=1;i=1002"
                            }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1002",
                        BrowseName = "1:OverTemperature",
                        DisplayName = [new Export.LocalizedText { Value = "OverTemperature" }],
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype", IsForward = false, Value = "i=2782"
                            },
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = true,
                                Value = "ns=1;i=6001"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=6001",
                        BrowseName = "1:Temperature",
                        DisplayName = [new Export.LocalizedText { Value = "Temperature" }],
                        ParentNodeId = "ns=1;i=1002",
                        DataType = "Double",
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = false,
                                Value = "ns=1;i=1002"
                            }
                        ]
                    }
                ]
            };
        }

        /// <summary>
        /// Resolves an EventType reference against the document set the
        /// conversion produced, which is the local context of WoT Binding
        /// Section 5.1.5 for a set held together.
        /// </summary>
        private sealed class DocumentSetResolver : IWotThingResolver
        {
            public DocumentSetResolver(WotDocumentSet set)
            {
                m_set = set;
            }

            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<WotResolverResult>(
                    m_set.TryGetDocument(reference, out WotDocument document)
                        ? WotResolverResult.FromBytes(document.Utf8Json.ToArray())
                        : WotResolverResult.NotFound);
            }

            private readonly WotDocumentSet m_set;
        }

        /// <summary>
        /// Serves one document under one href, and nothing else.
        /// </summary>
        private sealed class HrefResolver : IWotThingResolver
        {
            public HrefResolver(string href, string json)
            {
                m_href = href;
                m_json = json;
            }

            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<WotResolverResult>(
                    string.Equals(reference, m_href, System.StringComparison.Ordinal)
                        ? WotResolverResult.FromBytes(
                            System.Text.Encoding.UTF8.GetBytes(m_json))
                        : WotResolverResult.NotFound);
            }

            private readonly string m_href;
            private readonly string m_json;
        }

        /// <summary>
        /// The field order the EnabledState StateVariable schema states, which
        /// is the order its References declare the two members in.
        /// </summary>
        private static readonly string[] s_enabledStateFieldOrder = ["Id", "Name"];
    }
}
