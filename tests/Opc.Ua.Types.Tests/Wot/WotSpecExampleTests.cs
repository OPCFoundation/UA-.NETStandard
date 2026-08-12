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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Conformance tests that run the WoT Binding specification's own worked
    /// examples through the implementation.
    /// </summary>
    /// <remarks>
    /// Examples 07 and 08 are a golden pair: 07 is a projection document and 08
    /// is the resolved view the specification says it resolves to. Asserting
    /// against 08 checks the selection order, the bulk naming rule, the security
    /// closure naming and the provenance term against the specification's own
    /// expectation rather than against our reading of it.
    /// </remarks>
    [TestFixture]
    [Category("WotSpecExamples")]
    public sealed class WotSpecExampleTests
    {
        [Test]
        public void EveryPublishedExampleParses()
        {
            IReadOnlyList<string> names = ExampleNames();
            Assert.That(names, Is.Not.Empty, "The example fixtures should be embedded.");

            foreach (string name in names)
            {
                using WotDocument document = WotDocument.Parse(ReadExample(name));
                Assert.That(
                    document.RootElement.ValueKind,
                    Is.EqualTo(JsonValueKind.Object),
                    $"Example '{name}' should parse as a JSON object.");
            }
        }

        [Test]
        public void PredictiveMaintenanceExampleIsRecognisedAsAProjection()
        {
            using WotDocument document = WotDocument.Parse(
                ReadExample(ProjectionExample));

            Assert.That(WotProjection.IsProjection(document), Is.True);

            var diagnostics = new List<WotDiagnostic>();
            WotProjection projection = WotProjection.Parse(document, diagnostics);

            Assert.That(projection, Is.Not.Null);
            Assert.That(
                diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.False,
                "A published example should not break any projection rule.");
            Assert.That(
                projection.Scenario,
                Is.EqualTo("http://example.com/scenario/PredictiveMaintenance"));
            Assert.That(projection.Sources.Count, Is.EqualTo(2));
            Assert.That(projection.References.Count, Is.EqualTo(1));
        }

        [Test]
        public void ResolvedViewExampleIsNotAProjection()
        {
            using WotDocument document = WotDocument.Parse(
                ReadExample(ResolvedExample));

            // The resolved view is an ordinary Thing Description: a consumer
            // needs no projection support to use it.
            Assert.That(WotProjection.IsProjection(document), Is.False);
        }

        [Test]
        public async Task PredictiveMaintenanceProjectionResolvesToThePublishedViewAsync()
        {
            using WotDocument projection = WotDocument.Parse(
                ReadExample(ProjectionExample));
            using WotDocument expected = WotDocument.Parse(
                ReadExample(ResolvedExample));

            var resolver = new WotProjectionResolver(new EmbeddedExampleResolver());
            WotConversionResult<WotDocument> result = await resolver
                .ResolveAsync(projection)
                .ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.False,
                DescribeErrors(result.Diagnostics));
            Assert.That(result.Value, Is.Not.Null);

            using WotDocument actual = result.Value;

            // The specification's expected view names exactly these affordances,
            // which together exercise all three selection forms: an enumerated
            // tm:ref, a predicate over property affordances, and a whole-document
            // selection whose names carry the "device" prefix.
            Assert.That(
                actual.Properties.Keys.OrderBy(k => k, StringComparer.Ordinal),
                Is.EquivalentTo(
                    expected.Properties.Keys.OrderBy(k => k, StringComparer.Ordinal)),
                "The resolved view should name the affordances the specification expects.");

            Assert.That(WotProjection.IsProjection(actual), Is.False,
                "A resolved view carries no uav:projection marker.");
        }

        [Test]
        public async Task ResolvedViewCarriesProvenanceForEverySelectionAsync()
        {
            using WotDocument projection = WotDocument.Parse(
                ReadExample(ProjectionExample));

            var resolver = new WotProjectionResolver(new EmbeddedExampleResolver());
            WotConversionResult<WotDocument> result = await resolver
                .ResolveAsync(projection)
                .ConfigureAwait(false);

            Assert.That(result.Value, Is.Not.Null, DescribeErrors(result.Diagnostics));
            using WotDocument actual = result.Value;

            foreach (KeyValuePair<string, JsonElement> affordance in actual.Properties)
            {
                Assert.That(
                    affordance.Value.TryGetProperty("uav:resolvedFrom", out JsonElement from),
                    Is.True,
                    $"'{affordance.Key}' should name where it was resolved from.");
                Assert.That(
                    from.GetString(),
                    Is.Not.Null.And.Not.Empty,
                    $"'{affordance.Key}' should carry a non-empty provenance reference.");
            }

            // The enumerated selection's provenance is the tm:ref exactly as
            // authored, so a derived artifact names an origin that resolves.
            Assert.That(
                actual.Properties["pumpSpeed"].GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo("./01-opcua-td-pump.jsonld#/properties/pumpSpeed"));
        }

        [Test]
        public async Task BulkSelectionAppliesThePrefixWithAnUpperCasedSourceNameAsync()
        {
            using WotDocument projection = WotDocument.Parse(
                ReadExample(ProjectionExample));

            var resolver = new WotProjectionResolver(new EmbeddedExampleResolver());
            WotConversionResult<WotDocument> result = await resolver
                .ResolveAsync(projection)
                .ConfigureAwait(false);

            Assert.That(result.Value, Is.Not.Null, DescribeErrors(result.Diagnostics));
            using WotDocument actual = result.Value;

            // serialNumber under prefix "device" becomes deviceSerialNumber:
            // the prefix is followed by the source name with its first
            // character upper-cased.
            Assert.That(actual.Properties.ContainsKey("deviceSerialNumber"), Is.True);
            Assert.That(actual.Properties.ContainsKey("devicESerialNumber"), Is.False);
            Assert.That(actual.Properties.ContainsKey("deviceserialNumber"), Is.False);
        }

        [Test]
        public async Task CarriedFormsBringTheirSecurityClosureUnderQualifiedNamesAsync()
        {
            using WotDocument projection = WotDocument.Parse(
                ReadExample(ProjectionExample));
            using WotDocument expected = WotDocument.Parse(
                ReadExample(ResolvedExample));

            var resolver = new WotProjectionResolver(new EmbeddedExampleResolver());
            WotConversionResult<WotDocument> result = await resolver
                .ResolveAsync(projection)
                .ConfigureAwait(false);

            Assert.That(result.Value, Is.Not.Null, DescribeErrors(result.Diagnostics));
            using WotDocument actual = result.Value;

            // Under source routing every scheme in the transitive closure of a
            // carried form's effective security travels with it, named
            // <sourceName>_<scheme name>.
            foreach (string scheme in expected.SecurityDefinitions.Keys)
            {
                Assert.That(
                    actual.SecurityDefinitions.ContainsKey(scheme),
                    Is.True,
                    $"The resolved view should define the security scheme '{scheme}'.");
            }
        }

        private static string DescribeErrors(IReadOnlyList<WotDiagnostic> diagnostics)
        {
            return string.Join(
                Environment.NewLine,
                diagnostics
                    .Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => $"{d.Code}: {d.Message}"));
        }

        private static bool IsSection13Diagnostic(WotDiagnostic diagnostic)
        {
            return diagnostic.Code >= WotDiagnosticCode.ConditionEventIdMissing &&
                diagnostic.Code <= WotDiagnosticCode.UnresolvedConditionType;
        }

        private static IReadOnlyList<string> ExampleNames()
        {
            return [.. typeof(WotSpecExampleTests).Assembly
                .GetManifestResourceNames()
                .Where(n => n.Contains(ResourcePrefix, StringComparison.Ordinal) &&
                    n.EndsWith(".jsonld", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal)];
        }

        private static byte[] ReadExample(string name)
        {
            string resource = ExampleNames()
                .Single(n => n.EndsWith(name, StringComparison.Ordinal));
            using Stream stream = typeof(WotSpecExampleTests).Assembly
                .GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing fixture '{name}'.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        /// <summary>
        /// Serves the embedded specification examples by their relative
        /// reference, so a projection's sources resolve without any I/O.
        /// </summary>
        private sealed class EmbeddedExampleResolver : IWotThingResolver
        {
            public async ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                string name = reference;
                int slash = name.LastIndexOf('/');
                if (slash >= 0)
                {
                    name = name.Substring(slash + 1);
                }
                int fragment = name.IndexOf('#', StringComparison.Ordinal);
                if (fragment >= 0)
                {
                    name = name.Substring(0, fragment);
                }
                if (!name.EndsWith(".jsonld", StringComparison.Ordinal))
                {
                    return WotResolverResult.NotFound;
                }
                try
                {
                    return WotResolverResult.FromBytes(
                        ReadExample(name),
                        "application/td+json");
                }
                catch (InvalidOperationException)
                {
                    return WotResolverResult.NotFound;
                }
            }
        }

        /// <summary>
        /// Example 22 is the specification's own worked example of WoT Binding
        /// Section 5.2.1, so converting it is the end-to-end check that a
        /// document binds to a type that already exists rather than projecting
        /// an untyped node.
        /// </summary>
        [Test]
        public async Task TypeBindingExampleBindsTheProjectedObjectToTheNamedTypeAsync()
        {
            using WotDocument document = WotDocument.Parse(ReadExample(TypeBindingExample));

            // The example binds by both forms of Section 5.2.1, so it only
            // converts against a Section 5.1.5 local context that holds the
            // type it names - which is the point of the example.
            var resolver = new ExampleTypeResolver(
                "http://example.com/demo/pump",
                "TankType",
                "nsu=http://example.com/demo/pump;i=1042");

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(document, null, null, null, resolver)
                .ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
            Assert.That(result.Value, Is.Not.Null);

            UANode root = result.Value.Items.First(i => i is UAObject);
            Reference typeDefinition = root.References.First(r =>
                string.Equals(r.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal));

            // The bound type lives in the pump namespace, not the document's
            // own, so asserting the exact node means resolving the emitted
            // namespace index back through the NodeSet's table rather than
            // hard-coding it.
            var bound = NodeId.Parse(typeDefinition.Value);
            Assert.That(bound.IdentifierAsString, Is.EqualTo("1042"));
            Assert.That(
                result.Value.NamespaceUris[bound.NamespaceIndex - 1],
                Is.EqualTo("http://example.com/demo/pump"));
        }

        /// <summary>
        /// The specification's own Condition example must remain an accepted
        /// shape as validation of the actionable-event rules evolves.
        /// </summary>
        [Test]
        public void ConditionExampleConvertsWithoutSection13Diagnostics()
        {
            using WotDocument document = WotDocument.Parse(ReadExample(ConditionExample));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Count(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Zero);
            Assert.That(result.Diagnostics.Count(IsSection13Diagnostic), Is.Zero);
        }

        /// <summary>
        /// The specification's DataType example states every kind of §6.11
        /// definition, so it materializes the whole clause: a Structure, an
        /// optional-field Structure, two subtyped-value kinds, an Enumeration,
        /// an OptionSet and a SimpleDataType.
        /// </summary>
        [Test]
        public void DataTypeExampleMaterializesEveryDefinition()
        {
            using WotDocument document = WotDocument.Parse(ReadExample(DataTypeExample));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Is.Empty);

            UADataType[] dataTypes = result.Value!.Items!.OfType<UADataType>().ToArray();

            // Nine are stated explicitly; five more are inferred from the
            // Inferred* affordances' own schemas under §6.11.4 and §6.11.5.
            Assert.That(dataTypes, Has.Length.EqualTo(14));

            // A definition without uav:dataTypeId derives a namespace-scoped
            // String NodeId from its name alone (§6.11.1), so the same document
            // read again lands on the same Node.
            UADataType measurement = dataTypes.Single(d => d.BrowseName!.EndsWith(
                ":MeasurementDataType", StringComparison.Ordinal));
            Assert.That(measurement.NodeId, Does.Contain("s=DataTypes/MeasurementDataType"));
            Assert.That(measurement.Definition!.Field, Has.Length.EqualTo(3));
            Assert.That(measurement.Definition.Field![0].Name, Is.EqualTo("Value"));
            Assert.That(measurement.Definition.Field[2].ArrayDimensions, Is.EqualTo("3,3"));

            // A field naming a sibling definition resolves through that
            // definition's NodeId; the JSON-LD @id is never a NodeId (§6.11.3).
            UADataType machineState = dataTypes.Single(d => d.BrowseName!.EndsWith(
                ":MachineStateEnum", StringComparison.Ordinal));
            Assert.That(measurement.Definition.Field[1].DataType, Is.EqualTo(machineState.NodeId));

            // An Enumeration keeps its authored values, including a negative one.
            Assert.That(
                machineState.Definition!.Field!.Select(f => f.Value),
                Is.EqualTo(new[] { 0, 10, -1 }));
            Assert.That(machineState.Definition.IsOptionSet, Is.False);

            // An OptionSet numbers bits and states its integer base (§6.11.2).
            UADataType flags = dataTypes.Single(d => d.BrowseName!.EndsWith(
                ":AccessFlags", StringComparison.Ordinal));
            Assert.That(flags.Definition!.IsOptionSet, Is.True);
            Assert.That(
                flags.References!.Any(r => r.ReferenceType == "HasSubtype" &&
                    !r.IsForward && r.Value == "i=7"),
                Is.True);

            // A subtyped-value kind is not expressible as an IsUnion flag, so
            // the reverse direction has to read it back off the fields. Getting
            // this wrong silently demotes the type to a plain Structure, which
            // the kind checks then reject on the way back in.
            UADataType anyNumber = dataTypes.Single(d => d.BrowseName!.EndsWith(
                ":AnyNumberDataType", StringComparison.Ordinal));
            Assert.That(anyNumber.Definition!.Field![0].AllowSubTypes, Is.True);

            // §6.11.7 (new in the PR): a concrete Structure reached only
            // through other Structures may say it has no default encoding, and
            // then none are generated for it.
            UADataType nested = dataTypes.Single(d => d.BrowseName!.EndsWith(
                ":NestedPayloadDataType", StringComparison.Ordinal));
            Assert.That(nested.IsAbstract, Is.False);
            Assert.That(nested.Definition, Is.Not.Null);
            Assert.That(
                nested.References!.Any(r => r.ReferenceType == "HasEncoding"),
                Is.False,
                "A type with uav:hasDefaultEncoding false exposes no encodings.");

            // A SimpleDataType has no definition attribute and no encodings.
            UADataType counter = dataTypes.Single(d => d.BrowseName!.EndsWith(
                ":PositiveCounterType", StringComparison.Ordinal));
            Assert.That(counter.Definition, Is.Null);
            Assert.That(counter.References!.Any(r => r.ReferenceType == "HasEncoding"), Is.False);

            // Every non-abstract Structure exposes all three encodings, with
            // ids derived from its own identity (§6.11.7).
            Assert.That(
                measurement.References!.Count(r => r.ReferenceType == "HasEncoding" && r.IsForward),
                Is.EqualTo(3));
            Assert.That(
                measurement.References!.Any(r => r.ReferenceType == "HasEncoding" &&
                    r.Value == measurement.NodeId + "/Default Binary"),
                Is.True);
        }

        /// <summary>
        /// A term the converter materializes must not also be carried as
        /// residue. Were it captured, the round trip would re-emit it as a
        /// NodeSet Extension on top of the DataType Nodes it already produced,
        /// so the same fact would be stated twice in two different languages.
        /// </summary>
        [Test]
        public void MaterializedDataTypeDefinitionsAreNotAlsoResidue()
        {
            using WotDocument document = WotDocument.Parse(ReadExample(DataTypeExample));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            string extensions = result.Value!.Extensions is null
                ? string.Empty
                : string.Concat(result.Value.Extensions.Select(e => e.OuterXml ?? string.Empty));
            Assert.That(extensions, Does.Not.Contain("dataTypeDefinitions"));
        }

        /// <summary>
        /// Half of the completeness contract of §6.11.8: a DataType Node
        /// survives NodeSet to document and back through the readable
        /// vocabulary alone, identity, fields and order intact.
        /// </summary>
        /// <remarks>
        /// The example still leaves <c>uav:nodes</c> on the document, but no
        /// longer because of any DataType Node: all fourteen materialize and
        /// return identically. What remains is the canonical-schema
        /// equivalence of §6.11.6. An inferred definition's own DataSchema
        /// terms — <c>uav:fieldOrder</c>, <c>properties</c>, <c>required</c> —
        /// travel as residue rather than being re-derived from the definition,
        /// so the two passes hash differently. Deriving the canonical schema
        /// from the definition is the remaining step, and is tracked as its own
        /// piece of work.
        /// </remarks>
        [Test]
        public void DataTypesSurviveTheRoundTripReadably()
        {
            using WotDocument authored = WotDocument.Parse(ReadExample(DataTypeExample));
            UANodeSet first = WotNodeSetConverter.ToNodeSet(authored);

            using WotDocument document = WotNodeSetConverter.FromNodeSet(first);

            Assert.That(
                document.RootElement.TryGetProperty("uav:dataTypeDefinitions", out JsonElement emitted),
                Is.True,
                "Every DataType the NodeSet defines should be stated readably.");
            Assert.That(emitted.GetArrayLength(), Is.EqualTo(14));

            UANodeSet second = WotNodeSetConverter.ToNodeSet(WithoutNativeProjection(document));

            UADataType[] before = first.Items!.OfType<UADataType>()
                .OrderBy(d => d.NodeId, StringComparer.Ordinal).ToArray();
            UADataType[] after = second.Items!.OfType<UADataType>()
                .OrderBy(d => d.NodeId, StringComparer.Ordinal).ToArray();

            Assert.That(after.Select(d => d.NodeId), Is.EqualTo(before.Select(d => d.NodeId)));
            Assert.That(
                after.Select(d => d.BrowseName),
                Is.EqualTo(before.Select(d => d.BrowseName)));
            Assert.That(
                after.Select(d => d.IsAbstract),
                Is.EqualTo(before.Select(d => d.IsAbstract)));

            for (int ii = 0; ii < before.Length; ii++)
            {
                Assert.That(
                    after[ii].Definition?.Field?.Select(f => f.Name),
                    Is.EqualTo(before[ii].Definition?.Field?.Select(f => f.Name)),
                    $"Fields of '{before[ii].BrowseName}' should survive in order.");
                Assert.That(
                    after[ii].Definition?.Field?.Select(f => f.Value),
                    Is.EqualTo(before[ii].Definition?.Field?.Select(f => f.Value)),
                    $"Field values of '{before[ii].BrowseName}' should survive.");
            }
        }

        /// <summary>
        /// Strips the native projection so a round trip exercises the readable
        /// mapping. Left in place the projection is preferred on the way back,
        /// and the readable terms would never be read at all.
        /// </summary>
        private static WotDocument WithoutNativeProjection(WotDocument document)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (JsonProperty member in document.RootElement.EnumerateObject())
                {
                    if (member.Name is "uav:nodes" or "uav:nodeSet")
                    {
                        continue;
                    }
                    writer.WritePropertyName(member.Name);
                    member.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            return WotDocument.Parse(buffer.ToArray());
        }

        /// <summary>
        /// A local context holding exactly one ObjectType, so the example
        /// resolves against the type it names.
        /// </summary>
        private sealed class ExampleTypeResolver(
            string heldNamespace,
            string browseName,
            string nodeId) : IWotNodeResolver
        {
            public ValueTask<bool> HoldsNamespaceAsync(
                string namespaceUri, CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(
                    string.Equals(namespaceUri, heldNamespace, StringComparison.Ordinal));
            }

            public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
                string namespaceUri,
                string name,
                WotExpectedNodeClass expected,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ArrayOf<WotResolvedNode>>(
                    string.Equals(namespaceUri, heldNamespace, StringComparison.Ordinal) &&
                    string.Equals(name, browseName, StringComparison.Ordinal)
                        ? new ArrayOf<WotResolvedNode>(
                            [new WotResolvedNode(nodeId, WotExpectedNodeClass.ObjectType)])
                        : ArrayOf<WotResolvedNode>.Empty);
            }

            public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
                string expandedNodeId, CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotResolvedNode?>(
                    string.Equals(expandedNodeId, nodeId, StringComparison.Ordinal)
                        ? new WotResolvedNode(nodeId, WotExpectedNodeClass.ObjectType)
                        : null);
            }
        }

        private const string ResourcePrefix = "Wot.Assets.";
        private const string ProjectionExample = "07-projection-predictive-maintenance.jsonld";
        private const string ResolvedExample = "08-projection-resolved.jsonld";
        private const string ConditionExample = "21-condition-limit-alarm.jsonld";
        private const string TypeBindingExample = "22-type-binding-and-instance-reference.jsonld";
        private const string DataTypeExample = "23-datatype-definitions.jsonld";
    }
}
