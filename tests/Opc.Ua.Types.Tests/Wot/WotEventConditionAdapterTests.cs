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
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    public sealed class WotEventConditionAdapterTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task CanonicalExportedEventRootsSupportConditionsAndOccurrencesAsync(bool selection)
        {
            WotConversionResult<WotDocumentSet> exported = WotNodeSetConverter.FromNodeSetDocuments(
                NativeGraph(), "model");
            Assert.That(exported.Success, Is.True);
            using WotDocumentSet documents = exported.Value;
            var inputs = new List<WotDocument>();
            foreach (WotDocumentSetEntry entry in documents.Entries)
            {
                inputs.Add(entry.Document);
            }
            WotDocument custom = inputs.Single(document =>
                document.RootElement.GetProperty("uav:id").GetString() == QueryId);
            Assert.That(custom.TypeTokens, Does.Contain("uav:eventType"));
            var context = new WotDocumentNodeResolver(inputs);
            using WotDocument consumer = Consumer(QueryId, selection);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, QueryResolver(QueryId), null, context).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Errors(result));
            Assert.That((await context.ResolveByNodeIdAsync(QueryId).ConfigureAwait(false)).Value.NodeClass,
                Is.EqualTo(WotExpectedNodeClass.ObjectType));
            Assert.That(result.Value.Items.OfType<UAObjectType>().Single().References
                .Single(reference => reference.ReferenceType == "HasSubtype").Value, Is.EqualTo("ns=2;i=7001"));
            WotConversionResult<WotEventSelectionCatalog> selected = await new WotEventSelectionResolver(
                QueryResolver(QueryId)).ResolveAsync(consumer).ConfigureAwait(false);
            if (selection)
            {
                Assert.That(selected.Value.TryGetSelection(
                    "alarm", out ArrayOf<WotResolvedEventSelectClause> clauses), Is.True);
                Assert.That(clauses.ToArray().Single().TypeDefinitionId, Is.EqualTo(QueryId));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DocumentAncestryRejectsConflictingParentOrderAsync(bool reverse)
        {
            using WotDocument baseType = TypeDocument("i=2041", []);
            using WotDocument condition = TypeDocument("i=2782", ["i=2041"]);
            using WotDocument other = TypeDocument(OtherId, ["i=58"]);
            using WotDocument custom = TypeDocument(
                QueryId, reverse ? [OtherId, "i=2782"] : ["i=2782", OtherId]);
            using WotDocument consumer = Consumer(QueryId);
            var context = new WotDocumentNodeResolver([baseType, condition, other, custom]);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, null, null, context).ConfigureAwait(false);

            AssertConditionRejected(result);
        }

        [TestCase("cycle", false)]
        [TestCase("cycle", true)]
        [TestCase("wrongParent", false)]
        [TestCase("wrongParent", true)]
        [TestCase("multipleParents", false)]
        [TestCase("multipleParents", true)]
        public async Task DocumentAncestryHonorsSuppliedStandardFactsAsync(string fact, bool standardPin)
        {
            using WotDocument baseType = TypeDocument("i=2041", []);
            using WotDocument custom = TypeDocument(QueryId, ["i=2782"]);
            using WotDocument other = TypeDocument(OtherId, ["i=58"]);
            using WotDocument condition = TypeDocument("i=2782", fact switch
            {
                "cycle" => [QueryId],
                "wrongParent" => ["i=58"],
                _ => ["i=2041", OtherId]
            });
            using WotDocument consumer = Consumer(standardPin ? "i=2782" : QueryId);
            var context = new WotDocumentNodeResolver([baseType, condition, custom, other]);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, null, null, context).ConfigureAwait(false);

            AssertConditionRejected(result);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InheritedSummariesDoNotHideConflictingParentsAsync(bool includeInherited)
        {
            using WotDocument baseType = TypeDocument("i=2041", []);
            using WotDocument condition = TypeDocument("i=2782", ["i=2041"]);
            using WotDocument other = TypeDocument(OtherId, ["i=58"]);
            using WotDocument custom = TypeDocument(QueryId, ["i=2782", OtherId], includeInherited);
            using WotDocument consumer = Consumer(QueryId);
            var context = new WotDocumentNodeResolver([baseType, condition, other, custom]);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, null, null, context).ConfigureAwait(false);

            AssertConditionRejected(result);
        }

        [TestCase("i=15", -1)]
        [TestCase("i=12", -1)]
        [TestCase("i=15", 1)]
        public async Task NativeContextReportsActualForwardOnlyEventIdDeclarationAsync(string dataType, int rank)
        {
            UANodeSet source = NativeGraph(dataType, rank);
            UAVariable field = source.Items.OfType<UAVariable>().Single();
            Assert.That(field.ParentNodeId, Is.Null);
            Assert.That(field.References.Any(reference => !reference.IsForward), Is.False);
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source, options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });
            var context = new WotDocumentNodeResolver([document]);

            WotTypeDeclarationSet declarations = await context.ResolveDeclarationsAsync(
                "i=2041", WotDeclarationScope.Effective).ConfigureAwait(false);

            Assert.That(declarations, Is.Not.Null);
            Assert.That(declarations.IsComplete, Is.True, declarations.Detail);
            WotTypeDeclaration actual = declarations.Declarations.ToArray().Single(value => value.NodeId == "i=2042");
            Assert.That(actual.DeclaringTypeNodeId, Is.EqualTo("i=2041"));
            Assert.That(actual.NamespaceUri, Is.EqualTo(Namespaces.OpcUa));
            Assert.That(actual.BrowseName, Is.EqualTo("EventId"));
            Assert.That(actual.Kind, Is.EqualTo(WotDeclarationKind.Variable));
            Assert.That(actual.ReferenceTypeName, Is.EqualTo("HasProperty"));
            Assert.That(actual.DataType, Is.EqualTo(dataType));
            Assert.That(actual.ValueRank, Is.EqualTo(rank));
        }

        [TestCase("i=15", -1, true, "i=2041")]
        [TestCase("i=12", -1, false, "i=2041")]
        [TestCase("i=15", 1, false, "i=2041")]
        [TestCase("i=15", -1, true, QueryId)]
        [TestCase("i=12", -1, false, QueryId)]
        [TestCase("i=15", 1, false, QueryId)]
        public async Task NativeOccurrenceSelectionsUseActualDeclarationsAsync(
            string dataType, int rank, bool accepted, string query)
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                NativeGraph(dataType, rank),
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });
            var context = new WotDocumentNodeResolver([document]);
            using WotDocument consumer = Consumer("i=2782", selection: true);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, QueryResolver(query), null, context).ConfigureAwait(false);

            Assert.That(result.Success, Is.EqualTo(accepted), Errors(result));
            if (!accepted)
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ConditionEventIdMissing &&
                    diagnostic.Location?.JsonPointer == "/events/alarm/uav:eventSelectClauses"), Is.True);
            }
        }

        [Test]
        public void CounterfeitNativeEventIdOwnerIsRejected()
        {
            UANodeSet source = NativeGraph();
            source.Items.Single(node => node.NodeId == "i=2041").References = [];
            UANode custom = source.Items.Single(node => node.NodeId == "ns=2;i=7001");
            custom.References =
            [
                .. custom.References,
                new Reference { ReferenceType = "HasProperty", IsForward = true, Value = "i=2042" }
            ];
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source, options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });

            Assert.That(() => new WotDocumentNodeResolver([document]), Throws.TypeOf<FormatException>());
        }

        [TestCase(false, "i=2782", true)]
        [TestCase(true, "i=2782", true)]
        [TestCase(false, QueryId, true)]
        [TestCase(true, QueryId, true)]
        [TestCase(false, "i=2041", false)]
        [TestCase(true, "i=2041", false)]
        [TestCase(false, "i=58", false)]
        [TestCase(true, "i=58", false)]
        public void RestoredPinsMustNameConditionAncestors(bool archive, string pin, bool accepted)
        {
            using WotDocument document = NativeDocument(NativeGraph(), archive, pin);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.EqualTo(accepted), Errors(result));
            Assert.That(result.Value.Items, Has.Length.EqualTo(6));
            Assert.That(result.Value.Items.Single(node => node.NodeId == "ns=1;i=5002")
                .References.Single().Value, Is.EqualTo("ns=2;i=7001"));
            if (!accepted)
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    diagnostic.Location?.JsonPointer == "/events/alarm/uav:conditionTypeId"), Is.True);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RestoredConditionsRejectSuppliedStandardCycles(bool archive, bool forward)
        {
            UANodeSet source = NativeGraph();
            UANode condition = source.Items.Single(node => node.NodeId == "i=2782");
            condition.References = forward
                ? []
                : [new Reference
                {
                    ReferenceType = "HasSubtype", IsForward = false, Value = "ns=2;i=7001"
                }];
            if (forward)
            {
                UANode custom = source.Items.Single(node => node.NodeId == "ns=2;i=7001");
                custom.References =
                [
                    .. custom.References,
                    new Reference { ReferenceType = "HasSubtype", IsForward = true, Value = "i=2782" }
                ];
            }
            using WotDocument document = NativeDocument(source, archive, QueryId);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                diagnostic.Location?.JsonPointer == "/events/alarm/uav:conditionTypeId"), Is.True);
            Assert.That(result.Value.Items, Has.Length.EqualTo(6));
        }

        [Test]
        public async Task ForwardNativeSupertypeReferencesPreserveConditionIdentityAsync()
        {
            UANodeSet source = NativeGraph();
            source.Items.Single(node => node.NodeId == "ns=2;i=7001").References = null;
            UANode condition = source.Items.Single(node => node.NodeId == "i=2782");
            condition.References =
            [
                .. condition.References,
                new Reference { ReferenceType = "HasSubtype", IsForward = true, Value = "ns=2;i=7001" }
            ];
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source, options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });
            using WotDocument consumer = Consumer(QueryId);
            var context = new WotDocumentNodeResolver([document]);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, null, null, context).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Errors(result));
            Assert.That(result.Value.Items.OfType<UAObjectType>().Single().References
                .Single(reference => reference.ReferenceType == "HasSubtype").Value, Is.EqualTo("ns=2;i=7001"));
        }

        [Test]
        public void IncompatibleForwardOnlyTypeCarriagesAreRejected()
        {
            UANodeSet source = NativeGraph();
            source.Items.Single(node => node.NodeId == "ns=2;i=7001").References = [];
            UANode condition = source.Items.Single(node => node.NodeId == "i=2782");
            condition.References =
            [
                .. condition.References,
                new Reference { ReferenceType = "HasSubtype", IsForward = true, Value = "ns=2;i=7001" }
            ];
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source, options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });

            Assert.That(document.TryGetNativeProjection(out _), Is.True);
            Assert.That(document.TryGetEnvelope(out _), Is.True);
            Assert.That(() => new WotDocumentNodeResolver([document]), Throws.TypeOf<FormatException>());
        }

        [TestCase(false, "i=2041")]
        [TestCase(true, "i=2041")]
        [TestCase(false, QueryId)]
        [TestCase(true, QueryId)]
        public async Task NativeOccurrenceSelectionsRejectNonRootCounterfeitOwnersAsync(bool archive, string query)
        {
            UANodeSet source = NativeGraph();
            source.Items.Single(node => node.NodeId == "i=2041").References = null;
            UANode custom = source.Items.Single(node => node.NodeId == "ns=2;i=7001");
            custom.References =
            [
                .. custom.References,
                new Reference { ReferenceType = "HasProperty", IsForward = true, Value = "i=2042" }
            ];
            using WotDocument projected = WotNodeSetConverter.FromNodeSet(
                source, options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });
            JsonObject root = JsonNode.Parse(projected.Utf8Json.Span).AsObject();
            root.Remove(archive ? "uav:nodes" : "uav:nodeSet");
            using WotDocument document = Parse(root);
            var context = new WotDocumentNodeResolver([document]);
            WotTypeDeclarationSet declarations = await context.ResolveDeclarationsAsync(
                QueryId, WotDeclarationScope.Effective).ConfigureAwait(false);
            Assert.That(declarations, Is.Not.Null);
            Assert.That(declarations.IsComplete, Is.True, declarations.Detail);
            WotTypeDeclaration field = declarations.Declarations.ToArray().Single(value => value.NodeId == "i=2042");
            Assert.That(field.DeclaringTypeNodeId, Is.EqualTo(QueryId));
            Assert.That(field.DataType, Is.EqualTo("i=15"));
            Assert.That(field.ValueRank, Is.EqualTo(-1));
            using WotDocument consumer = Consumer("i=2782", selection: true);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, QueryResolver(query), null, context).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ConditionEventIdMissing &&
                diagnostic.Location?.JsonPointer == "/events/alarm/uav:eventSelectClauses"), Is.True, Errors(result));
        }

        [TestCase(98, true)]
        [TestCase(99, true)]
        [TestCase(100, false)]
        public async Task ActualDocumentDeclarationClosureHonorsItsDepthBoundAsync(int customTypes, bool accepted)
        {
            var documents = new List<WotDocument>
            {
                TypeDocument("i=2041", []),
                TypeDocument("i=2782", ["i=2041"])
            };
            try
            {
                for (int index = 0; index < customTypes; index++)
                {
                    string identity = index == 0 ? QueryId : ChainId(index);
                    string parent = index == customTypes - 1 ? "i=2782" : ChainId(index + 1);
                    documents.Add(TypeDocument(identity, [parent]));
                }
                using WotDocument consumer = Consumer(QueryId);
                var context = new WotDocumentNodeResolver(documents);

                WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                    consumer, null, null, null, context).ConfigureAwait(false);

                Assert.That(result.Success, Is.EqualTo(accepted), Errors(result));
                if (!accepted)
                {
                    AssertConditionRejected(result);
                }
            }
            finally
            {
                foreach (WotDocument document in documents)
                {
                    document.Dispose();
                }
            }
        }

        [Test]
        public async Task CancelledConversionWithAnActualDocumentContextStopsAsync()
        {
            using WotDocument custom = TypeDocument(QueryId, ["i=2782"]);
            using WotDocument consumer = Consumer(QueryId);
            var context = new WotDocumentNodeResolver([custom]);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThatAsync(async () =>
                await WotNodeSetConverter.ToNodeSetResultAsync(
                    consumer, null, null, null, context, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        }

        [Test]
        public async Task ResidualNativeStandardIdentityPreservesItsNodeClassAsync(
            [Values(false, true)] bool archive,
            [Values("object", "objectType", "absent", "none")] string sourceKind)
        {
            UANodeSet source = NativeGraph();
            if (sourceKind == "object")
            {
                source.Items = [.. source.Items.Select(node => node.NodeId == "i=2782"
                    ? new UAObject { NodeId = "i=2782", BrowseName = "ConditionType" } : node)];
            }
            else if (sourceKind == "absent")
            {
                source.Items = [.. source.Items.Where(node => node.NodeId != "i=2782")];
            }
            using WotDocument document = ResidualNativeContext(source, archive, "urn:residual:class");
            byte[] original = document.Utf8Json.ToArray();
            var resolver = new WotDocumentNodeResolver(sourceKind == "none" ? [] : [document]);
            using WotDocument consumer = Consumer("i=2782");

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, null, null, resolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.EqualTo(sourceKind != "object"), Errors(result));
            WotResolvedNode? nodeResult = await resolver.ResolveByNodeIdAsync("i=2782").ConfigureAwait(false);
            WotTypeDeclarationSet declarations = await resolver.ResolveDeclarationsAsync(
                "i=2782", WotDeclarationScope.Effective).ConfigureAwait(false);
            if (sourceKind is "none" or "absent")
            {
                Assert.That(nodeResult, Is.Null);
                Assert.That(declarations, Is.Null);
            }
            else
            {
                Assert.That(nodeResult, Is.Not.Null);
                Assert.That(nodeResult.Value.NodeId, Is.EqualTo("i=2782"));
                Assert.That(nodeResult.Value.NodeClass, Is.EqualTo(sourceKind == "object"
                    ? WotExpectedNodeClass.Any : WotExpectedNodeClass.ObjectType));
                Assert.That(declarations, Is.Not.Null);
                Assert.That(declarations.IsComplete, Is.EqualTo(sourceKind == "objectType"));
                if (sourceKind == "object")
                {
                    Assert.That(declarations.Detail, Is.Not.Empty);
                    AssertConditionRejected(result);
                }
            }
            Assert.That(document.Utf8Json.ToArray(), Is.EqualTo(original));
        }

        [Test]
        public async Task ResidualNativeStandardIdentityRejectsConflictingClassesInEitherOrderAsync(
            [Values(false, true)] bool archive,
            [Values(false, true)] bool reverse)
        {
            UANodeSet wrongClass = NativeGraph();
            wrongClass.Items = [.. wrongClass.Items.Select(node => node.NodeId == "i=2782"
                ? new UAObject { NodeId = "i=2782", BrowseName = "ConditionType" } : node)];
            using WotDocument valid = ResidualNativeContext(NativeGraph(), archive, "urn:residual:valid");
            using WotDocument wrong = ResidualNativeContext(wrongClass, archive, "urn:residual:wrong");
            var resolver = new WotDocumentNodeResolver(reverse ? [wrong, valid] : [valid, wrong]);
            using WotDocument consumer = Consumer("i=2782");

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, null, null, resolver).ConfigureAwait(false);

            AssertConditionRejected(result);
            WotTypeDeclarationSet declarations = await resolver.ResolveDeclarationsAsync(
                "i=2782", WotDeclarationScope.Effective).ConfigureAwait(false);
            Assert.That(declarations, Is.Not.Null);
            Assert.That(declarations.IsComplete, Is.False);
            Assert.That(declarations.Detail, Does.Contain("Conflicting native"));
        }

        [Test]
        public async Task ResidualNativeDeclarationsRejectConflictsInEitherOrderAsync(
            [Values(false, true)] bool archive,
            [Values(false, true)] bool reverse,
            [Values("dataType", "rank", "consistent")] string fact,
            [Values("i=2041", QueryId)] string query)
        {
            UANodeSet other = NativeGraph(fact == "dataType" ? "i=12" : "i=15", fact == "rank" ? 1 : -1);
            other.Items = [other.Items[0], .. other.Items.Skip(1).Reverse()];
            using WotDocument first = ResidualNativeContext(NativeGraph(), archive, "urn:residual:first");
            using WotDocument second = ResidualNativeContext(other, archive, "urn:residual:second");
            byte[] firstBytes = first.Utf8Json.ToArray();
            byte[] secondBytes = second.Utf8Json.ToArray();
            var resolver = new WotDocumentNodeResolver(reverse ? [second, first] : [first, second]);
            using WotDocument consumer = Consumer("i=2782", selection: true);
            bool consistent = fact == "consistent";

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, QueryResolver(query), null, resolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.EqualTo(consistent), Errors(result));
            WotTypeDeclarationSet declarations = await resolver.ResolveDeclarationsAsync(
                "i=2041", WotDeclarationScope.Effective).ConfigureAwait(false);
            Assert.That(declarations, Is.Not.Null);
            Assert.That(declarations.TypeNodeId, Is.EqualTo("i=2041"));
            Assert.That(declarations.IsComplete, Is.EqualTo(consistent));
            if (consistent)
            {
                WotTypeDeclaration field = declarations.Declarations.ToArray()
                    .Single(value => value.NodeId == "i=2042");
                Assert.That(field.DeclaringTypeNodeId, Is.EqualTo("i=2041"));
                Assert.That(field.DataType, Is.EqualTo("i=15"));
                Assert.That(field.ValueRank, Is.EqualTo(-1));
            }
            else
            {
                Assert.That(declarations.Detail, Does.Contain("Conflicting native"));
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == WotDiagnosticSeverity.Error &&
                    diagnostic.Location?.JsonPointer == "/events/alarm/uav:eventSelectClauses"), Is.True);
            }
            Assert.That(first.Utf8Json.ToArray(), Is.EqualTo(firstBytes));
            Assert.That(second.Utf8Json.ToArray(), Is.EqualTo(secondBytes));
        }

        private static WotDocument ResidualNativeContext(UANodeSet source, bool archive, string id)
        {
            using WotDocument exported = WotNodeSetConverter.FromNodeSet(
                source, options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });
            JsonObject root = JsonNode.Parse(exported.Utf8Json.Span).AsObject();
            root.Remove(archive ? "uav:nodes" : "uav:nodeSet");
            root["@id"] = id;
            return Parse(root);
        }

        private static void AssertConditionRejected(WotConversionResult<UANodeSet> result)
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Location?.JsonPointer == "/events/alarm/uav:conditionTypeId"), Is.True, Errors(result));
        }

        private static WotDocument TypeDocument(
            string identity, ArrayOf<string> parents, bool includeInherited = false)
        {
            var links = new JsonArray();
            foreach (string parent in parents)
            {
                links.Add(new JsonObject { ["rel"] = "tm:extends", ["href"] = parent });
            }
            return Parse(new JsonObject
            {
                ["@context"] = Context(),
                ["@type"] = new JsonArray("tm:ThingModel", "uav:objectType"),
                ["uav:id"] = identity,
                ["uav:browseName"] = identity == "i=2041" ? "ua:BaseEventType"
                    : identity == "i=2782" ? "ua:ConditionType" : "v:CustomAlarm",
                ["uav:includeInherited"] = includeInherited,
                ["links"] = links
            });
        }

        private static WotDocument Consumer(string pin, bool selection = false)
        {
            var affordance = new JsonObject
            {
                ["@type"] = "uav:eventType",
                ["uav:conditionTypeId"] = pin,
                ["data"] = Data()
            };
            if (selection)
            {
                affordance["uav:eventSelectClauses"] = new JsonArray(new JsonObject
                {
                    ["tm:ref"] = DefinitionIri,
                    ["uav:browsePath"] = "EventId"
                });
            }
            return Parse(new JsonObject
            {
                ["@context"] = Context(),
                ["@type"] = "uav:object",
                ["uav:id"] = "nsu=urn:review:device;i=5001",
                ["uav:browseName"] = "nsu=urn:review:device;Device",
                ["events"] = new JsonObject { ["alarm"] = affordance }
            });
        }

        private static IWotThingResolver QueryResolver(string identity)
        {
            var definition = new JsonObject
            {
                ["@context"] = Context(),
                ["@id"] = DefinitionIri,
                ["@type"] = new JsonArray("tm:ThingModel", "uav:eventType"),
                ["uav:id"] = identity,
                ["data"] = Data()
            };
            var resolver = new Mock<IWotThingResolver>();
            resolver.Setup(value => value.ResolveThingAsync(
                    DefinitionIri, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(WotTestData.Utf8(definition.ToJsonString())));
            return resolver.Object;
        }

        private static UANodeSet NativeGraph(string dataType = "i=15", int rank = -1)
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:review:device", "urn:review:types"],
                Models = [new ModelTableEntry { ModelUri = "urn:review:device" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=5001", BrowseName = "1:Device",
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=58" },
                            new Reference
                            {
                                ReferenceType = "GeneratesEvent", IsForward = true, Value = "ns=1;i=5002"
                            }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=5002", BrowseName = "1:Alarm",
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype", IsForward = false, Value = "ns=2;i=7001"
                            }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=2;i=7001", BrowseName = "2:CustomAlarm",
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=2782" }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "i=2782", BrowseName = "ConditionType",
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=2041" }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "i=2041", BrowseName = "BaseEventType",
                        References =
                        [
                            new Reference { ReferenceType = "HasProperty", IsForward = true, Value = "i=2042" }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "i=2042", BrowseName = "EventId", DataType = dataType, ValueRank = rank,
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=68" }
                        ]
                    }
                ]
            };
        }

        private static WotDocument NativeDocument(UANodeSet source, bool archive, string pin)
        {
            using WotDocument projected = WotNodeSetConverter.FromNodeSet(
                source, options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });
            JsonObject root = JsonNode.Parse(projected.Utf8Json.Span).AsObject();
            root.Remove(archive ? "uav:nodes" : "uav:nodeSet");
            root["events"] = new JsonObject
            {
                ["alarm"] = new JsonObject
                {
                    ["@type"] = "uav:eventType",
                    ["uav:id"] = "nsu=urn:review:device;i=5002",
                    ["uav:conditionTypeId"] = pin
                }
            };
            return Parse(root);
        }

        private static JsonObject Context()
        {
            return new JsonObject
            {
                ["ua"] = Namespaces.OpcUa,
                ["tm"] = "https://www.w3.org/2019/wot/tm#",
                ["v"] = "urn:review:types"
            };
        }

        private static JsonObject Data()
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["EventId"] = new JsonObject { ["type"] = "string", ["contentEncoding"] = "base64" }
                }
            };
        }

        private static WotDocument Parse(JsonObject root)
        {
            return WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));
        }

        private static string ChainId(int index)
        {
            return "nsu=urn:review:types;i=" + (8000 + index).ToString(CultureInfo.InvariantCulture);
        }

        private static string Errors(WotConversionResult<UANodeSet> result)
        {
            return string.Join("; ", result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.Message));
        }

        private const string QueryId = "nsu=urn:review:types;i=7001";
        private const string OtherId = "nsu=urn:review:types;i=7002";
        private const string DefinitionIri = "urn:review:query";
    }
}
