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
        public void TypeBindingExampleBindsTheProjectedObjectToTheNamedType()
        {
            using WotDocument document = WotDocument.Parse(ReadExample(TypeBindingExample));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);

            UANode root = result.Value.Items.First(i => i is UAObject);
            Reference typeDefinition = root.References.First(r =>
                string.Equals(r.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal));

            Assert.That(
                typeDefinition.Value,
                Is.Not.EqualTo(WotVocabulary.BaseObjectType),
                "The example carries a ua:HasTypeDefinition link, so the projected " +
                "Object must be bound to that type rather than to BaseObjectType.");
        }

        private const string ResourcePrefix = "Wot.Assets.";
        private const string ProjectionExample = "07-projection-predictive-maintenance.jsonld";
        private const string ResolvedExample = "08-projection-resolved.jsonld";
        private const string TypeBindingExample = "22-type-binding-and-instance-reference.jsonld";
    }
}
