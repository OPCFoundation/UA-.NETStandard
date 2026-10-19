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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Planners;
using BindingAffordanceKind = Opc.Ua.WotCon.Bindings.WotAffordanceKind;
using SampleDocument = Opc.Ua.WotCon.Tests.Samples.WotAggregationDocumentGenerator.SampleDocument;

namespace Opc.Ua.WotCon.Tests.Samples
{
    /// <summary>
    /// Verifies the deterministic sample documents that will be shared by the
    /// WoT aggregation client and end-to-end tests.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Category("Samples")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class WotAggregationDocumentTests
    {
        private const string DiNamespace = "http://opcfoundation.org/UA/DI/";
        private const string MachineryNamespace = "http://opcfoundation.org/UA/Machinery/";
        private const string PumpsNamespace = "http://opcfoundation.org/UA/Pumps/";
        private const string PumpInstanceNamespace = "urn:opcfoundation.org:UA:WotAggregation:PumpInstance";

        /// <summary>
        /// Verifies that checked-in Thing Models match formatted converter output.
        /// </summary>
        [Test]
        public void ThingModelsMatchFormattedConverterRegeneration()
        {
            foreach (ModelDocument document in s_modelDocuments)
            {
                byte[] regenerated = WotAggregationDocumentGenerator.GenerateThingModel(
                    RepositoryPath(document.SourcePath),
                    document.Title);
                byte[] checkedIn = File.ReadAllBytes(DocumentPath(document.FileName));

                Assert.That(
                    regenerated,
                    Is.EqualTo(checkedIn),
                    $"{document.FileName} is not the formatted converter output.");
            }
        }

        /// <summary>
        /// Rewrites the checked-in single-document Thing Models.
        /// </summary>
        /// <remarks>
        /// Explicit for the same reason <c>WriteCompanionModelSets</c> is: it
        /// rewrites checked-in sample documents. Run it when the converter's
        /// output changes, review the diff, then commit what it produced:
        /// <para>
        ///   dotnet test tests\Opc.Ua.WotCon.Tests --filter "FullyQualifiedName~WriteThingModels"
        /// </para>
        /// </remarks>
        [Test]
        [Explicit("Rewrites the checked-in sample documents.")]
        public void WriteThingModels()
        {
            foreach (ModelDocument document in s_modelDocuments)
            {
                byte[] regenerated = WotAggregationDocumentGenerator.GenerateThingModel(
                    RepositoryPath(document.SourcePath),
                    document.Title);
                File.WriteAllBytes(DocumentPath(document.FileName), regenerated);
                TestContext.Out.WriteLine(
                    $"{document.FileName}: {regenerated.Length} bytes");
            }
        }

        /// <summary>
        /// Verifies that the checked-in pump Thing Description matches formatted regeneration.
        /// </summary>
        [Test]
        public void PumpThingDescriptionMatchesFormattedRegeneration()
        {
            byte[] regenerated = WotAggregationDocumentGenerator.GeneratePumpThingDescription(
                DocumentPath("SamplePump.NodeSet2.xml"));
            byte[] checkedIn = File.ReadAllBytes(DocumentPath("SamplePump.td.json"));

            Assert.That(regenerated, Is.EqualTo(checkedIn));
        }

        /// <summary>
        /// Rewrites the checked-in sample Thing Description.
        /// </summary>
        /// <remarks>
        /// Explicit for the same reason <c>WriteThingModels</c> is: it rewrites
        /// a checked-in sample document. Run it when the converter's output
        /// changes, review the diff, then commit what it produced:
        /// <para>
        ///   dotnet test tests\Opc.Ua.WotCon.Tests --filter "FullyQualifiedName~WritePumpThingDescription"
        /// </para>
        /// </remarks>
        [Test]
        [Explicit("Rewrites the checked-in sample documents.")]
        public void WritePumpThingDescription()
        {
            byte[] regenerated = WotAggregationDocumentGenerator.GeneratePumpThingDescription(
                DocumentPath("SamplePump.NodeSet2.xml"));
            File.WriteAllBytes(DocumentPath("SamplePump.td.json"), regenerated);
            TestContext.Out.WriteLine($"SamplePump.td.json: {regenerated.Length} bytes");
        }

        /// <summary>
        /// Verifies that checked-in pump asset projection documents match formatted regeneration.
        /// </summary>
        [Test]
        public void PumpAssetProjectionDocumentsMatchFormattedRegeneration()
        {
            ArrayOf<SampleDocument> pumpDocuments = ReadPumpDocuments();
            foreach (string fileName in s_assetProjectionDocuments)
            {
                ByteString regenerated =
                    WotAggregationDocumentGenerator.GeneratePumpAssetProjectionDocument(
                        fileName, pumpDocuments);
                byte[] checkedIn = File.ReadAllBytes(DocumentPath(fileName));

                Assert.That(
                    regenerated.ToArray(),
                    Is.EqualTo(checkedIn),
                    $"{fileName} is not the formatted projection document.");
            }
        }

        /// <summary>
        /// Verifies that pump asset projection documents declare current plan headers.
        /// </summary>
        [Test]
        public void PumpAssetProjectionDocumentsDeclareCurrentPlanHeaders()
        {
            ArrayOf<SampleDocument> projections =
                WotAggregationDocumentGenerator.GenerateAssetProjectionDocuments(ReadPumpDocuments());
            Assert.That(projections.Count, Is.EqualTo(s_assetProjectionDocuments.Length));
            foreach (SampleDocument sample in projections)
            {
                using var document = WotDocument.Parse(sample.Json.Memory);
                var diagnostics = new List<WotDiagnostic>();
                WotProjection plan = WotProjection.Parse(document, diagnostics)!;
                Assert.That(plan, Is.Not.Null, sample.Path);
                Assert.That(diagnostics, Is.Empty, sample.Path);
                Assert.That(plan.ResultKind, Is.EqualTo(WotDocumentKind.ThingDescription), sample.Path);
                Assert.That(document.TypeTokens, Does.Not.Contain("Thing").And.Not.Contain("tm:ThingModel"), sample.Path);
                if (!sample.Path.Contains(".Members.", StringComparison.Ordinal))
                {
                    Assert.That(plan.Sources.ToList().Select(source => source.MediaType),
                        Is.All.EqualTo(WotProjection.ContentType), sample.Path);
                }
            }
        }

        /// <summary>
        /// Regenerates the asset projection documents used by the aggregation samples.
        /// </summary>
        [Test]
        [Explicit("Rewrites only the checked-in projection plans from the retained source documents.")]
        public async Task WriteAssetProjectionDocuments()
        {
            ArrayOf<SampleDocument> projections =
                WotAggregationDocumentGenerator.GenerateAssetProjectionDocuments(ReadPumpDocuments());
            for (int index = 0; index < projections.Count; index++)
            {
                SampleDocument document = projections[index];
                await WotAggregationDocumentGenerator.WriteBytesAsync(
                    DocumentPath(document.Path), document.Json).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that manifest documents match regeneration of the complete aggregation document set.
        /// </summary>
        [Test]
        public async Task ManifestDocumentsMatchCompleteAsyncRegeneration()
        {
            ArrayOf<SampleDocument> checkedIn = ReadManifestDocuments();
            ArrayOf<SampleDocument> regenerated = await WotAggregationDocumentGenerator
                .GenerateAggregationDocumentsAsync(RepositoryRoot).ConfigureAwait(false);

            Assert.That(
                regenerated.ToList().Select(document => document.ResourceId),
                Is.EquivalentTo(checkedIn.ToList().Select(document => document.ResourceId)));
            Assert.That(
                regenerated.ToList().Select(document => document.Path),
                Is.EquivalentTo(checkedIn.ToList().Select(document => document.Path)));
            foreach (SampleDocument expected in checkedIn)
            {
                SampleDocument actual = regenerated.ToList().Single(
                    document => document.ResourceId == expected.ResourceId);
                Assert.That(actual.Path, Is.EqualTo(expected.Path), expected.ResourceId);
                Assert.That(actual.DocumentKind, Is.EqualTo(expected.DocumentKind), expected.ResourceId);
                Assert.That(actual.Json.ToArray(), Is.EqualTo(expected.Json.ToArray()), expected.Path);
            }
            Assert.That(
                WotAggregationDocumentGenerator.GenerateManifest(regenerated).ToArray(),
                Is.EqualTo(File.ReadAllBytes(DocumentPath("documents.json"))));
        }

        /// <summary>
        /// Regenerates the complete aggregation document set.
        /// </summary>
        [Test]
        [Explicit("Rewrites the complete sample manifest and its generated documents.")]
        public async Task WriteCompleteAggregationDocumentsAsync()
        {
            ArrayOf<SampleDocument> documents = await WotAggregationDocumentGenerator
                .GenerateAggregationDocumentsAsync(RepositoryRoot).ConfigureAwait(false);
            await WotAggregationDocumentGenerator.WriteDocumentsAsync(
                DocumentPath(string.Empty), documents).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that checked-in JSON sample documents use indented serialization.
        /// </summary>
        [Test]
        public void CheckedInJsonDocumentsUseIndentedSerialization()
        {
            foreach (string path in SampleJsonDocuments())
            {
                Assert.That(Path.GetFileName(path), Does.Not.Contain("--").And.Not.EndWith("-.json"), path);
                byte[] bytes = File.ReadAllBytes(path);
                using var document = WotDocument.Parse(
                    bytes,
                    WotAggregationDocumentGenerator.CreateLargeDocumentOptions());
                Assert.That(
                    WotAggregationDocumentGenerator.FormatJson(document).ToArray(),
                    Is.EqualTo(bytes),
                    $"{path} is not deterministically formatted JSON.");
            }
        }

        /// <summary>
        /// Rewrites every checked-in sample document with sorted members,
        /// two-space indentation, and LF line endings.
        /// </summary>
        /// <remarks>
        /// Explicit for the same reason <c>WriteThingModels</c> is: it rewrites
        /// checked-in sample documents. Formatting changes the JSON layout,
        /// not the document's value. Run it, review the diff, then commit what
        /// it produced:
        /// <para>
        ///   dotnet test tests\Opc.Ua.WotCon.Tests --filter "FullyQualifiedName~RewriteCheckedInJsonDocuments"
        /// </para>
        /// </remarks>
        [Test]
        [Explicit("Rewrites the checked-in sample documents.")]
        public void RewriteCheckedInJsonDocuments()
        {
            foreach (string path in SampleJsonDocuments())
            {
                byte[] bytes = File.ReadAllBytes(path);
                using var document = WotDocument.Parse(
                    bytes,
                    WotAggregationDocumentGenerator.CreateLargeDocumentOptions());
                byte[] formatted = WotAggregationDocumentGenerator.FormatJson(document).ToArray();
                if (formatted.SequenceEqual(bytes))
                {
                    continue;
                }
                File.WriteAllBytes(path, formatted);
                TestContext.Out.WriteLine($"{path}: {formatted.Length} bytes");
            }
        }

        private static IEnumerable<string> SampleJsonDocuments()
        {
            return Directory.EnumerateFiles(DocumentPath(string.Empty), "*.json", SearchOption.AllDirectories)
                .Append(StructuredExamplePath);
        }

        /// <summary>
        /// Verifies that manifest orders dependencies before dependents.
        /// </summary>
        [Test]
        public void ManifestOrdersDependenciesBeforeDependents()
        {
            using var manifest = JsonDocument.Parse(
                File.ReadAllBytes(DocumentPath("documents.json")));
            JsonElement.ArrayEnumerator documents = manifest.RootElement.EnumerateArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            ArrayOf<SampleDocument> selected = ReadManifestDocuments();
            var resources = selected.ToList().ToDictionary(document => document.ResourceId, StringComparer.Ordinal);

            foreach (JsonElement document in documents)
            {
                string resourceId = document.GetProperty("resourceId").GetString()!;
                string path = document.GetProperty("path").GetString()!;
                Assert.That(path, Does.Not.Contain("\\").And.Not.StartWith("/"), resourceId);
                Assert.That(File.Exists(DocumentPath(path)), Is.True, path);
                using var json = JsonDocument.Parse(resources[resourceId].Json.ToArray());
                Assert.That(
                    resources[resourceId].DocumentKind,
                    Is.EqualTo(TypeNames(json.RootElement).Contains("tm:ThingModel")
                        ? WoTDocumentKindEnum.ThingModel
                        : WoTDocumentKindEnum.ThingDescription),
                    resourceId);
                string[] dependencies = [.. document.GetProperty("dependsOn")
                    .EnumerateArray().Select(dependency => dependency.GetString()!)];
                string[] logicalDependencies = [.. LogicalDependencies(
                    json.RootElement, resourceId, resources.Keys)];
                Assert.That(
                    dependencies,
                    Is.SupersetOf(logicalDependencies),
                    $"{resourceId} must retain every logical document dependency.");
                foreach (string dependency in dependencies.Except(logicalDependencies))
                {
                    AssertDependencyHasDeclaredReference(json.RootElement, resources[dependency]);
                }
                foreach (JsonElement dependency in document.GetProperty("dependsOn").EnumerateArray())
                {
                    Assert.That(seen, Does.Contain(dependency.GetString()), resourceId);
                }
                Assert.That(seen.Add(resourceId), Is.True, $"Duplicate resource: {resourceId}");
                Assert.That(
                    document.GetProperty("documentKind").GetString(),
                    Is.EqualTo(resources[resourceId].DocumentKind.ToString()),
                    resourceId);
                Assert.That(
                    document.GetProperty("groupId").GetString(),
                    Is.EqualTo(resources[resourceId].DocumentKind == WoTDocumentKindEnum.ThingModel
                        ? "thingmodels"
                        : "thingdescriptions"),
                    resourceId);
            }

            string[] linkedDirectories = ["opc-ua-di", "opc-ua-machinery", "opc-ua-pumps", "sample-pump"];
            string[] expectedPaths =
            [
                .. linkedDirectories.SelectMany(directory =>
                                Directory.EnumerateFiles(DocumentPath(directory), "*.json", SearchOption.AllDirectories)
                                    .Select(path => path[(DocumentPath(string.Empty).TrimEnd('\\', '/').Length + 1)..]
                                        .Replace('\\', '/')))
,
                .. s_assetProjectionDocuments
            ];
            Assert.That(
                selected.ToList().Select(document => document.Path),
                Is.EquivalentTo(expectedPaths));
            Assert.That(selected.Count, Is.EqualTo(expectedPaths.Length));
            Assert.That(
                selected.ToList().Count(document => s_assetProjectionDocuments.Contains(document.Path)),
                Is.EqualTo(12));
            foreach (string directory in linkedDirectories)
            {
                Assert.That(
                    selected.ToList().Any(document =>
                        document.Path.StartsWith(directory + "/", StringComparison.Ordinal)),
                    Is.True,
                    directory);
            }
        }

        /// <summary>
        /// Verifies that pump asset projection documents resolve to expected groups and members.
        /// </summary>
        [Test]
        public async Task PumpAssetProjectionDocumentsResolveToExpectedGroupsAndMembers()
        {
            var resolver = new WotProjectionResolver(
                new WotAggregationDocumentGenerator.SampleThingResolver(ReadSubstitutedManifestDocuments()),
                WotAggregationDocumentGenerator.CreateLargeDocumentOptions());
            using IDisposable allViewsScope = Assert.EnterMultipleScope();

            foreach (string pumpName in s_pumpAssetNames)
            {
                using var asset = WotDocument.Parse(
                    File.ReadAllBytes(DocumentPath($"{pumpName}.Asset.td.json")),
                    WotAggregationDocumentGenerator.CreateLargeDocumentOptions());

                WotConversionResult<WotDocument> assetResult = await resolver
                    .ResolveAsync(asset).ConfigureAwait(false);

                Assert.That(
                    assetResult.Success,
                    Is.True,
                    pumpName + Environment.NewLine + string.Join(
                        Environment.NewLine,
                        (assetResult.Diagnostics.ToArray() ?? Array.Empty<WotDiagnostic>()).Select(d => d.ToString())));
                using WotDocument assetView = assetResult.Value!;
                JsonElement root = assetView.RootElement;
                Assert.That(TypeNames(root), Does.Not.Contain("uav:projection"));
                Assert.That(
                    PropertyNames(root.GetProperty("properties")),
                    Is.EquivalentTo(s_identityMembers),
                    pumpName);
                Assert.That(
                    root.GetProperty("links")
                        .EnumerateArray()
                        .Select(link => link.GetProperty("uav:refName").GetString()),
                    Is.EqualTo(s_assetGroupNames),
                    pumpName);
                foreach (JsonElement link in root.GetProperty("links").EnumerateArray())
                {
                    string group = link.GetProperty("uav:refName").GetString()!;
                    Assert.That(link.GetProperty("href").GetString(),
                        Is.EqualTo($"{pumpName.ToLowerInvariant()}-{group.ToLowerInvariant()}"), pumpName);
                    Assert.That(link.GetProperty("rel").GetString(), Is.EqualTo("ua:Organizes"), pumpName);
                }

                await AssertProjectionMembersAsync(
                    resolver,
                    $"{pumpName}.Asset.td.json",
                    "properties",
                    s_identityMembers).ConfigureAwait(false);
                await AssertProjectionMembersAsync(
                    resolver,
                    $"{pumpName}.Members.td.json",
                    "properties",
                    [.. s_propertyBindings.Select(binding => binding.Name)]).ConfigureAwait(false);
                await AssertProjectionMembersAsync(
                    resolver,
                    $"{pumpName}.Members.td.json",
                    "actions",
                    s_managementMembers).ConfigureAwait(false);
                await AssertProjectionMembersAsync(
                    resolver,
                    $"{pumpName}.Members.td.json",
                    "events",
                    s_supervisionMembers).ConfigureAwait(false);
                await AssertProjectionMembersAsync(
                    resolver,
                    $"{pumpName}.ProcessData.td.json",
                    "properties",
                    s_processDataMembers).ConfigureAwait(false);
                await AssertProjectionMembersAsync(
                    resolver,
                    $"{pumpName}.Supervision.td.json",
                    "properties",
                    ["Cavitation", "MotorOverheat"]).ConfigureAwait(false);
                await AssertProjectionMembersAsync(
                    resolver,
                    $"{pumpName}.ConditionData.td.json",
                    "properties",
                    s_conditionDataMembers).ConfigureAwait(false);
                await AssertProjectionMembersAsync(
                    resolver,
                    $"{pumpName}.Supervision.td.json",
                    "events",
                    s_supervisionMembers).ConfigureAwait(false);
                await AssertProjectionMembersAsync(
                    resolver,
                    $"{pumpName}.Management.td.json",
                    "actions",
                    s_managementMembers).ConfigureAwait(false);
                await AssertProjectionMembersAsync(
                    resolver,
                    $"{pumpName}.Management.td.json",
                    "properties",
                    ["SourceARunning", "SourceBRunning"]).ConfigureAwait(false);
                await AssertProjectionMembersAsync(
                    resolver,
                    $"{pumpName}.Management.td.json",
                    "events",
                    s_supervisionMembers).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that members select the actual linked declarations by portable identity.
        /// </summary>
        /// <param name="pumpName">The pump name used by this test case.</param>
        /// <param name="rootResource">The root resource used by this test case.</param>
        /// <param name="measurementResource">The measurement resource used by this test case.</param>
        [TestCase("Pump1", "sample-pump", "sample-pump-operational-measurements")]
        [TestCase("Pump2", "sample-pump-pump-2", "sample-pump-pump-2-operational-measurements")]
        public void MembersSelectTheActualLinkedDeclarationsByPortableIdentity(
            string pumpName,
            string rootResource,
            string measurementResource)
        {
            ArrayOf<SampleDocument> documents = ReadPumpDocuments();
            SampleDocument root = FindDocumentByIdentity(documents, LocalNodeId(pumpName, string.Empty));
            SampleDocument measurements = FindDocumentByIdentity(
                documents, LocalNodeId(pumpName, "Operational.Measurements"));
            Assert.That(root.ResourceId, Is.EqualTo(rootResource));
            Assert.That(root.Path, Is.EqualTo("sample-pump/" + rootResource + ".json"));
            Assert.That(measurements.ResourceId, Is.EqualTo(measurementResource));
            Assert.That(measurements.Path, Is.EqualTo("sample-pump/" + measurementResource + ".json"));

            using var members = JsonDocument.Parse(File.ReadAllBytes(DocumentPath($"{pumpName}.Members.td.json")));
            var selectedResources = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string mapName, string[] names) in new[]
            {
                ("properties", [.. s_propertyBindings.Select(binding => binding.Name)]),
                ("actions", s_managementMembers),
                ("events", s_supervisionMembers)
            })
            {
                JsonElement map = members.RootElement.GetProperty(mapName);
                Assert.That(PropertyNames(map), Is.EquivalentTo(names), pumpName + ": " + mapName);
                foreach (JsonProperty member in map.EnumerateObject())
                {
                    string localPath = mapName == "properties"
                        ? s_propertyBindings.Single(binding => binding.Name == member.Name).LocalPath
                        : member.Name;
                    string localId = LocalNodeId(pumpName, localPath);
                    Affordance declaration = FindAffordance(documents, mapName, localId);
                    string reference = member.Value.GetProperty("tm:ref").GetString()!;
                    Assert.That(reference, Is.EqualTo(declaration.ResourceId + "#" + declaration.Pointer), member.Name);
                    Assert.That(ResolveReference(documents, reference).GetProperty("uav:id").GetString(),
                        Is.EqualTo(localId), member.Name);
                    selectedResources.Add(declaration.ResourceId);
                }
            }
            Assert.That(
                members.RootElement.GetProperty("uav:projects").EnumerateArray()
                    .Select(source => source.GetProperty("href").GetString()),
                Is.EquivalentTo(selectedResources));
        }

        /// <summary>
        /// Verifies that management projections compile with their condition events.
        /// </summary>
        /// <param name="pumpName">The pump name used by this test case.</param>
        [TestCase("Pump1")]
        [TestCase("Pump2")]
        public async Task ManagementProjectionsCompileWithTheirConditionEvents(string pumpName)
        {
            ArrayOf<SampleDocument> documents = ReadSubstitutedManifestDocuments();
            var thingResolver = new WotAggregationDocumentGenerator.SampleThingResolver(documents);
            var projectionResolver = new WotProjectionResolver(
                thingResolver, WotAggregationDocumentGenerator.CreateLargeDocumentOptions());
            SampleDocument projection = documents.ToList().Single(
                document => document.Path == $"{pumpName}.Management.td.json");
            using var projectionDocument = WotDocument.Parse(projection.Json.ToArray());
            WotConversionResult<WotDocument> resolved = await projectionResolver
                .ResolveAsync(projectionDocument).ConfigureAwait(false);
            Assert.That(resolved.Success, Is.True, string.Join("; ", resolved.Diagnostics.Select(d => d.Message)));
            using WotDocument view = resolved.Value!;
            JsonElement root = view.RootElement;
            Assert.That(PropertyNames(root.GetProperty("events")), Is.EquivalentTo(s_supervisionMembers));
            foreach ((string alarm, _, _) in s_alarmBindings)
            {
                foreach (string operation in new[] { "Acknowledge", "Confirm" })
                {
                    string actionName = alarm + operation;
                    JsonElement action = root.GetProperty("actions").GetProperty(actionName);
                    string actsOn = action.GetProperty("uav:actsOn").GetString()!;
                    JsonElement alarmEvent = root.GetProperty("events").GetProperty(actsOn);
                    Assert.That(actsOn, Is.EqualTo(alarm + "Alarm"), actionName);
                    Assert.That(action.GetProperty("uav:conditionAction").GetString(),
                        Is.EqualTo(operation), actionName);
                    Assert.That(alarmEvent.GetProperty("uav:id").GetString(),
                        Is.EqualTo(LocalNodeId(pumpName, alarm + "Alarm")), actionName);
                    AssertConditionInput(action.GetProperty("input"), actionName);
                }
            }

            WotBindingPlanRequest request = await WotBindingPlanRequest.FromDocumentAsync(
                projection.ResourceId, projection.DocumentKind, view.ToCanonicalUtf8(), thingResolver)
                .ConfigureAwait(false);
            var registry = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());
            WotBindingPlan plan = registry.Prepare(request);
            Assert.That(plan.FullySupported, Is.True, pumpName + ": " + string.Join("; ", plan.Diagnostics));
            Assert.That(plan.CompiledForms.Count(form => form.AffordanceKind == BindingAffordanceKind.Property),
                Is.EqualTo(4));
            Assert.That(plan.CompiledForms.Count(form => form.AffordanceKind == BindingAffordanceKind.Action),
                Is.EqualTo(10));
            Assert.That(plan.CompiledForms.Count(form => form.AffordanceKind == BindingAffordanceKind.Event),
                Is.EqualTo(2));
            Assert.That(plan.CompiledForms.Where(form => form.AffordanceKind == BindingAffordanceKind.Event)
                .Select(form => form.OpToken), Is.All.EqualTo("subscribeevent"));
            Assert.That(plan.CompiledForms.Where(form => form.AffordanceKind == BindingAffordanceKind.Event)
                .Select(form => form.EventSelection!.Origin), Is.All.EqualTo(WotEventSelectionOrigin.Standard));
        }

        /// <summary>
        /// Verifies that each model document owns the namespaces it declares.
        /// </summary>
        [Test]
        public void ModelDocumentsOwnTheirDeclaredNamespaces()
        {
            foreach (ModelDocument document in s_modelDocuments)
            {
                byte[] bytes = File.ReadAllBytes(DocumentPath(document.FileName));
                UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(
                    bytes,
                    WotAggregationDocumentGenerator.CreateLargeDocumentOptions());

                Assert.That(nodeSet.Models, Is.Not.Null.And.Not.Empty);
                Assert.That(nodeSet.Models![0].ModelUri, Is.EqualTo(document.ModelUri));
                Assert.That(nodeSet.NamespaceUris, Is.Not.Null.And.Not.Empty);
                Assert.That(nodeSet.NamespaceUris![0], Is.EqualTo(document.ModelUri));
                Assert.That(
                    nodeSet.Items!.Where(node => node.NodeId!.StartsWith("ns=1;", StringComparison.Ordinal)),
                    Is.Not.Empty);
            }
        }

        /// <summary>
        /// <i>OPC UA — Devices</i> (OPC 10000-100) §5.5 Table 48 defines
        /// <c>ConnectsTo</c> as a subtype of <c>NonHierarchicalReferences</c>:
        /// "It is NonHierarchical and symmetric, because this is natural for
        /// this Reference." The official DI NodeSet carried
        /// <c>HierarchicalReferences</c> up to and including DI 1.04 and was
        /// corrected in DI 1.05.0, so refreshing the checked-in NodeSet from an
        /// older upstream revision would silently reintroduce a non-compliant
        /// model into every document generated from it.
        /// </summary>
        [Test]
        public void DiConnectsToIsANonHierarchicalReference()
        {
            const string nonHierarchicalReferences = "i=32";
            ModelDocument di = s_modelDocuments.Single(
                document => document.ModelUri == DiNamespace);

            UANodeSet source = WotAggregationDocumentGenerator.ReadNodeSet(
                RepositoryPath(di.SourcePath));
            AssertConnectsToSuperType(source, nonHierarchicalReferences, di.SourcePath);

            UANodeSet generated = WotNodeSetConverter.ToNodeSet(
                File.ReadAllBytes(DocumentPath(di.FileName)),
                WotAggregationDocumentGenerator.CreateLargeDocumentOptions());
            AssertConnectsToSuperType(generated, nonHierarchicalReferences, di.FileName);
        }

        /// <summary>
        /// A link whose authoritative facts live in sibling partitions validates
        /// clean against the context of the partitions that share its namespace
        /// table, and still conflicts when the readable reference is genuinely wrong.
        /// </summary>
        [Test]
        public async Task LinkedNativeReferenceValidatesAgainstSiblingPartitionsAsync()
        {
            WotNodeSetConverterOptions options = WotAggregationDocumentGenerator.CreateLargeDocumentOptions();
            string directory = DocumentPath("opc-ua-di");
            byte[] cpIdentifier = File.ReadAllBytes(Path.Combine(directory, "opc-ua-di-cpidentifier.json"));
            string text = Encoding.UTF8.GetString(cpIdentifier);
            const string target = "\"nsu=http://opcfoundation.org/UA/DI/;i=6247\"";
            Assert.That(text, Does.Contain(target));
            byte[] conflicting = Encoding.UTF8.GetBytes(text.Replace(
                target, "\"nsu=http://opcfoundation.org/UA/DI/;i=6000\"", StringComparison.Ordinal));

            var contexts = new WotNodeSetConverter.ArchiveContextIndex(options);
            foreach (string path in Directory.GetFiles(directory, "*.json"))
            {
                using WotDocument sibling = WotDocument.Parse(File.ReadAllBytes(path), options);
                contexts.Add(sibling);
            }

            using WotDocument document = WotDocument.Parse(cpIdentifier, options);
            using WotDocument wrong = WotDocument.Parse(conflicting, options);
            WotConversionResult<UANodeSet> alone = await WotNodeSetConverter
                .ToNodeSetResultAsync(document, options).ConfigureAwait(false);
            WotConversionResult<UANodeSet> withContext = await WotNodeSetConverter
                .ToNodeSetResultAsync(document, options, new ContextResolver(contexts)).ConfigureAwait(false);
            WotConversionResult<UANodeSet> withContextConflict = await WotNodeSetConverter
                .ToNodeSetResultAsync(wrong, options, new ContextResolver(contexts)).ConfigureAwait(false);

            // Same namespace table, different model (publication date): its
            // symmetric/forward references must not bless this model's claim.
            const string publication = "\"publicationDate\": \"2025-11-15T00:00:00Z\"";
            var foreign = new WotNodeSetConverter.ArchiveContextIndex(options);
            WotDocument? foreignSibling = null;
            foreach (string path in Directory.GetFiles(directory, "*.json"))
            {
                string siblingText = File.ReadAllText(path);
                WotDocument parsed = WotDocument.Parse(Encoding.UTF8.GetBytes(siblingText.Replace(
                    publication, "\"publicationDate\": \"2024-01-01T00:00:00Z\"", StringComparison.Ordinal)), options);
                foreign.Add(parsed);
                if (path.EndsWith("opc-ua-di-networktype.json", StringComparison.Ordinal))
                {
                    foreignSibling = parsed;
                }
                else
                {
                    parsed.Dispose();
                }
            }
            using WotDocument? foreignNetworkType = foreignSibling;

            var duplicated = new WotNodeSetConverter.ArchiveContextIndex(options);
            duplicated.Add(document);
            duplicated.Add(document);

            // The same identity spelled differently is one identity, not two witnesses.
            var respelled = new WotNodeSetConverter.ArchiveContextIndex(options);
            respelled.Add(document);
            using WotDocument variant = WotDocument.Parse(Encoding.UTF8.GetBytes(text.Replace(
                "nsu=http://opcfoundation.org/UA/DI/;i=", "ns=1;i=", StringComparison.Ordinal)), options);
            respelled.Add(variant);

            var incomplete = new WotNodeSetConverter.ArchiveContextIndex(options);
            incomplete.Add(document);
            incomplete.MarkIncomplete("test bound");

            using IDisposable scope = Assert.EnterMultipleScope();
            Assert.That(foreignNetworkType, Is.Not.Null);
            Assert.That(foreign.Find(foreignNetworkType!), Is.Not.Null, "The foreign model must restore on its own.");
            Assert.That(
                (await WotNodeSetConverter.ToNodeSetResultAsync(
                    document, options, new ContextResolver(foreign)).ConfigureAwait(false))
                    .Diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionConflict),
                Is.True,
                "A foreign model sharing the namespace table must not validate this claim.");
            Assert.That(() => duplicated.Find(document), Throws.InvalidOperationException);
            Assert.That(() => respelled.Find(document), Throws.InvalidOperationException);
            Assert.That(() => incomplete.Find(document), Throws.InvalidOperationException);
            Assert.That(
                alone.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionConflict),
                Is.True,
                "A lone partition cannot see the reverse reference or symmetric ReferenceType.");
            Assert.That(
                withContext.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionConflict),
                Is.False);
            Assert.That(withContext.Success, Is.True);
            Assert.That(
                withContextConflict.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NativeProjectionConflict),
                Is.True);
        }

        /// <summary>
        /// Exhausting the resolver document or byte bound while composing the native
        /// context fails the document that needs it explicitly, never with a partial
        /// context, and cancellation propagates.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task ExhaustedNativeContextBoundFailsExplicitlyAsync(bool documentBound)
        {
            WotNodeSetConverterOptions options = WotAggregationDocumentGenerator.CreateLargeDocumentOptions();
            (Opc.Ua.WotCon.Server.Registry.WotRegistrySnapshot snapshot,
                Dictionary<string, ByteString> contents) = await StoreDiPartitionsAsync().ConfigureAwait(false);
            Opc.Ua.WotCon.Server.Registry.WotResource resource = FindResource(snapshot, "opc-ua-di-cpidentifier");
            int selectedLength = contents[resource.DefaultVersion!.DigestHex].Length;
            long aggregate = contents.Values.Sum(c => (long)c.Length);
            Assert.That(contents, Has.Count.GreaterThan(2));
            Assert.That(aggregate, Is.GreaterThan(selectedLength));
            if (documentBound)
            {
                options.MaxResolverDocuments = 2;
            }
            else
            {
                options.MaxResolverTotalBytes = selectedLength;
            }
            var converter = new Opc.Ua.WotCon.Server.Materialization.WotNodeSetDocumentConverter(options);

            Opc.Ua.WotCon.Server.Materialization.WotConversionOutput output = await converter.ConvertAsync(
                resource,
                contents[resource.DefaultVersion!.DigestHex],
                snapshot,
                contents,
                System.Threading.CancellationToken.None).ConfigureAwait(false);
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync().ConfigureAwait(false);

            using IDisposable scope = Assert.EnterMultipleScope();
            Assert.That(output.Succeeded, Is.False);
            Assert.That(output.NodeSet, Is.Null);
            Assert.That(
                string.Join("; ", output.Errors),
                Does.Contain("The native document context is incomplete")
                    .And.Contain("resolver document or byte limit"));
            Assert.That(
                async () => await new Opc.Ua.WotCon.Server.Materialization.WotNodeSetDocumentConverter(options)
                    .ConvertAsync(
                        resource,
                        contents[resource.DefaultVersion!.DigestHex],
                        snapshot,
                        contents,
                        cancelled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        /// <summary>
        /// A malformed sibling is only omitted from the native context. A document of
        /// the model still converts, and the malformed document fails by itself.
        /// </summary>
        [Test]
        public async Task MalformedSiblingIsOmittedFromNativeContextOnlyAsync()
        {
            WotNodeSetConverterOptions options = WotAggregationDocumentGenerator.CreateLargeDocumentOptions();
            (Opc.Ua.WotCon.Server.Registry.WotRegistrySnapshot snapshot,
                Dictionary<string, ByteString> contents) = await StoreDiPartitionsAsync().ConfigureAwait(false);
            Opc.Ua.WotCon.Server.Registry.WotResource broken = FindResource(snapshot, "opc-ua-di-networktype");
            contents[broken.DefaultVersion!.DigestHex] = ByteString.From(Encoding.UTF8.GetBytes("{not json"));
            Opc.Ua.WotCon.Server.Registry.WotResource resource = FindResource(snapshot, "opc-ua-di-cpidentifier");
            var converter = new Opc.Ua.WotCon.Server.Materialization.WotNodeSetDocumentConverter(options);

            Opc.Ua.WotCon.Server.Materialization.WotConversionOutput selected = await converter.ConvertAsync(
                broken,
                contents[broken.DefaultVersion.DigestHex],
                snapshot,
                contents,
                System.Threading.CancellationToken.None).ConfigureAwait(false);
            Opc.Ua.WotCon.Server.Materialization.WotConversionOutput other = await converter.ConvertAsync(
                resource,
                contents[resource.DefaultVersion!.DigestHex],
                snapshot,
                contents,
                System.Threading.CancellationToken.None).ConfigureAwait(false);

            using IDisposable scope = Assert.EnterMultipleScope();
            Assert.That(selected.Succeeded, Is.False);
            Assert.That(selected.Errors, Is.Not.Empty);
            Assert.That(string.Join("; ", other.Errors), Does.Not.Contain("limit"));
        }

        /// <summary>
        /// An archive that declares no model has no authoritative identity, so a set of them supplies
        /// no cross-document context, while the same export with its model does.
        /// </summary>
        [Test]
        public async Task UnmodelledNativeCarriersSupplyNoCrossDocumentAuthorityAsync()
        {
            WotNodeSetConverterOptions options = WotAggregationDocumentGenerator.CreateLargeDocumentOptions();
            options.PreservationMode = WotNodeSetPreservationMode.Always;
            string source = RepositoryPath(Path.Combine(
                "tests", "Opc.Ua.SourceGeneration.Core.Tests", "Resources", "Opc.Ua.Di.NodeSet2.xml"));
            UANodeSet modelled = WotAggregationDocumentGenerator.ReadNodeSet(source);
            UANodeSet unmodelled = WotAggregationDocumentGenerator.ReadNodeSet(source);
            unmodelled.Models = null;
            using WotDocumentSet modelledSet = await ExportSetAsync(modelled, options).ConfigureAwait(false);
            using WotDocumentSet unmodelledSet = await ExportSetAsync(unmodelled, options).ConfigureAwait(false);
            var withModel = new WotNodeSetConverter.ArchiveContextIndex(options);
            var withoutModel = new WotNodeSetConverter.ArchiveContextIndex(options);
            var empty = new WotNodeSetConverter.ArchiveContextIndex(options);
            foreach (WotDocumentSetEntry entry in modelledSet.Entries.ToList())
            {
                withModel.Add(entry.Document);
            }
            foreach (WotDocumentSetEntry entry in unmodelledSet.Entries.ToList())
            {
                withoutModel.Add(entry.Document);
            }

            using IDisposable scope = Assert.EnterMultipleScope();
            Assert.That(
                modelledSet.Entries.ToList().Where(CarriesNative).ToList(),
                Is.Not.Empty,
                "The modelled export must carry preserved native nodes.");
            Assert.That(
                unmodelledSet.Entries.ToList().Where(CarriesNative).ToList(),
                Is.Not.Empty,
                "The unmodelled export must carry preserved native nodes.");
            foreach (WotDocumentSetEntry entry in modelledSet.Entries.ToList().Where(CarriesNative))
            {
                Assert.That(withModel.Find(entry.Document), Is.Not.Null, entry.Href);
            }
            foreach (WotDocumentSetEntry entry in unmodelledSet.Entries.ToList())
            {
                Assert.That(withoutModel.Find(entry.Document), Is.Null, entry.Href);
                WotConversionResult<UANodeSet> alone = await WotNodeSetConverter
                    .ToNodeSetResultAsync(entry.Document, options, new ContextResolver(empty))
                    .ConfigureAwait(false);
                WotConversionResult<UANodeSet> viaIndex = await WotNodeSetConverter
                    .ToNodeSetResultAsync(entry.Document, options, new ContextResolver(withoutModel))
                    .ConfigureAwait(false);
                Assert.That(viaIndex.Success, Is.EqualTo(alone.Success), entry.Href);
                Assert.That(Describe(viaIndex), Is.EqualTo(Describe(alone)), entry.Href);
            }
        }

        private static bool CarriesNative(WotDocumentSetEntry entry)
        {
            return entry.Document.TryGetEnvelope(out _) || entry.Document.TryGetNativeProjection(out _);
        }

        private static string[] Describe(WotConversionResult<UANodeSet> result)
        {
            return [.. result.Diagnostics.Select(d =>
                $"{d.Severity}|{d.Code}|{d.Location?.JsonPointer}|{d.Location?.NodeId}|{d.Message}")];
        }
        private static async Task<WotDocumentSet> ExportSetAsync(UANodeSet source, WotNodeSetConverterOptions options)
        {
            WotConversionResult<WotDocumentSet> result = await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                source, "opc-ua-di", "OPC UA Device Integration", options).ConfigureAwait(false);
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            return result.Value!;
        }

        private static Opc.Ua.WotCon.Server.Registry.WotResource FindResource(
            Opc.Ua.WotCon.Server.Registry.WotRegistrySnapshot snapshot,
            string resourceId)
        {
            return snapshot.AllResources().Single(
                r => string.Equals(r.ResourceId, resourceId, StringComparison.Ordinal));
        }

        private static async Task<(Opc.Ua.WotCon.Server.Registry.WotRegistrySnapshot,
            Dictionary<string, ByteString>)> StoreDiPartitionsAsync()
        {
            using var registry = new Opc.Ua.WotCon.Server.Registry.WotRegistryService();
            var contents = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(DocumentPath("opc-ua-di"), "*.json"))
            {
                byte[] bytes = File.ReadAllBytes(path);
                bool model = Encoding.UTF8.GetString(bytes).Contains("tm:ThingModel", StringComparison.Ordinal);
                ByteString content = ByteString.From(bytes);
                contents[Opc.Ua.WotCon.Server.Registry.WotContentDigest.ToHex(
                    Opc.Ua.WotCon.Server.Registry.WotContentDigest.Compute(content))] = content;
                await registry.UpsertResourceAsync(new Opc.Ua.WotCon.Server.Registry.WotUpsertResourceRequest
                {
                    GroupId = model
                        ? Opc.Ua.WotCon.Server.Registry.WotRegistryGroups.ThingModels
                        : Opc.Ua.WotCon.Server.Registry.WotRegistryGroups.ThingDescriptions,
                    ResourceId = Path.GetFileNameWithoutExtension(path),
                    Kind = model ? WoTDocumentKindEnum.ThingModel : WoTDocumentKindEnum.ThingDescription,
                    Content = content
                }).ConfigureAwait(false);
            }
            return (registry.Current, contents);
        }

        /// <summary>
        /// A resolver that supplies a native archive context and resolves no external references.
        /// </summary>
        private sealed class ContextResolver : IWotThingResolver, IWotArchiveContextProvider
        {
            /// <summary>
            /// Creates the resolver over a context index.
            /// </summary>
            /// <param name="contexts">The index that supplies the native contexts.</param>
            public ContextResolver(WotNodeSetConverter.ArchiveContextIndex contexts)
            {
                m_contexts = contexts;
            }

            /// <inheritdoc/>
            public UANodeSet? GetArchiveContext(WotDocument document)
            {
                return m_contexts.Find(document);
            }

            /// <inheritdoc/>
            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                System.Threading.CancellationToken cancellationToken)
            {
                return new ValueTask<WotResolverResult>(WotResolverResult.NotFound);
            }

            private readonly WotNodeSetConverter.ArchiveContextIndex m_contexts;
        }

        private static void AssertConnectsToSuperType(
            UANodeSet nodeSet,
            string expectedSuperType,
            string origin)
        {
            UAReferenceType? connectsTo = nodeSet.Items!
                .OfType<UAReferenceType>()
                .SingleOrDefault(node => node.BrowseName == "1:ConnectsTo");
            Assert.That(connectsTo, Is.Not.Null, $"{origin} declares no ConnectsTo ReferenceType.");

            string? superType = connectsTo!.References!
                .Where(reference =>
                    reference.ReferenceType == "HasSubtype" && !reference.IsForward)
                .Select(reference => reference.Value)
                .SingleOrDefault();

            Assert.That(
                superType,
                Is.EqualTo(expectedSuperType),
                $"{origin}: ConnectsTo must be a subtype of NonHierarchicalReferences " +
                "(i=32) per OPC 10000-100 section 5.5 Table 48, not HierarchicalReferences " +
                "(i=33) as the DI NodeSet wrongly declared up to DI 1.04.");
            Assert.That(
                connectsTo.Symmetric,
                Is.True,
                $"{origin}: ConnectsTo is symmetric.");
        }

        /// <summary>
        /// Verifies that companion and pump node sets round trip without change.
        /// </summary>
        [Test]
        public void CompanionAndPumpNodeSetsRoundTripWithoutChange()
        {
            IEnumerable<string> sources = s_modelDocuments
                .Select(document => RepositoryPath(document.SourcePath))
                .Append(DocumentPath("SamplePump.NodeSet2.xml"));

            foreach (string source in sources)
            {
                WotNodeSetRoundtripReport report = WotNodeSetRoundtrip.Run(
                    WotAggregationDocumentGenerator.ReadNodeSet(source));

                Assert.That(report.NativeProjectionPreserved, Is.True, source);
                Assert.That(report.Comparison.AreEquivalent, Is.True, source);
                Assert.That(
                    report.Diagnostics.Any(
                        diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error),
                    Is.False,
                    source + ": " + string.Join("; ", report.Diagnostics));
            }
        }

        /// <summary>
        /// Verifies that the checked-in native Pumps partitions restore the complete source Pumps NodeSet.
        /// </summary>
        [Test]
        public async Task NativePumpsPartitionsRestoreTheSourceNodeSetWithoutChangeAsync()
        {
            const int expectedNodeCount = 9624;
            const int expectedPartitionCount = 339;
            string root = RepositoryPath(string.Empty);
            UANodeSet source = WotAggregationDocumentGenerator.ReadNodeSet(RepositoryPath(
                Path.Combine("samples", "DI", "PumpDeviceIntegrationServer", "Model", "Opc.Ua.Pumps.NodeSet2.xml")));
            SampleDocument[] partitions = [.. WotAggregationDocumentGenerator
                .ReadManifestDocuments(DocumentPath(string.Empty).TrimEnd('\\', '/'))
                .ToList()
                .Where(document => document.Path.StartsWith("opc-ua-pumps/", StringComparison.Ordinal))];
            WotNodeSetConverterOptions options = WotAggregationDocumentGenerator.CreateLargeDocumentOptions();
            IWotNodeResolver resolver = WotAggregationDocumentGenerator.CreateCompanionNodeResolver(root);
            var entries = new List<WotDocumentSetEntry>();
            try
            {
                foreach (SampleDocument partition in partitions)
                {
                    entries.Add(new WotDocumentSetEntry(
                        partition.ResourceId, WotDocument.Parse(partition.Json.ToArray(), options)));
                }
                using var set = new WotDocumentSet("opc-ua-pumps", entries.ToArrayOf());
                entries.Clear();

                WotConversionResult<UANodeSet> restored = await WotNodeSetConverter.ToNodeSetAsync(
                    set, options, resolver).ConfigureAwait(false);

                Assert.That(partitions, Has.Length.EqualTo(expectedPartitionCount));
                Assert.That(
                    Directory.EnumerateFiles(
                        DocumentPath("opc-ua-pumps"), "*.json", SearchOption.AllDirectories).Count(),
                    Is.EqualTo(expectedPartitionCount));
                Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics));
                Assert.That(
                    restored.Diagnostics.Any(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error),
                    Is.False,
                    string.Join("; ", restored.Diagnostics));
                Assert.That(source.Items, Has.Length.EqualTo(expectedNodeCount));
                Assert.That(restored.Value!.Items, Has.Length.EqualTo(expectedNodeCount));
                NodeSetComparisonResult comparison = WotNodeSetConverter.CompareDocumentSet(
                    source, restored.Value, options);
                Assert.That(
                    comparison.AreEquivalent,
                    Is.True,
                    string.Join("; ", comparison.Differences.Take(20)));
            }
            finally
            {
                foreach (WotDocumentSetEntry entry in entries)
                {
                    entry.Dispose();
                }
            }
        }

        /// <summary>
        /// Verifies that pump NodeSet has required model dependencies and type definition.
        /// </summary>
        /// <param name="pumpName">The sample pump instance name.</param>
        [TestCase("Pump1")]
        [TestCase("Pump2")]
        public void PumpNodeSetHasRequiredModelDependenciesAndTypeDefinition(string pumpName)
        {
            UANodeSet nodeSet = ReadPumpNodeSet();
            ModelTableEntry model = nodeSet.Models!.Single();
            string[] requiredModels = [.. model.RequiredModel!.Select(required => required.ModelUri!)];
            UAObject pump = FindNode<UAObject>(nodeSet, $"ns=1;s={pumpName}");

            Assert.That(model.ModelUri, Is.EqualTo(PumpInstanceNamespace));
            Assert.That(nodeSet.NamespaceUris![0], Is.EqualTo(PumpInstanceNamespace));
            Assert.That(
                requiredModels,
                Does.Contain(DiNamespace)
                    .And.Contain(MachineryNamespace)
                    .And.Contain(PumpsNamespace));
            // The pump is typed by its own subtype of the companion PumpType,
            // which declares the alarms it raises (OPC 10000-3 §7.15).
            Assert.That(TypeDefinition(pump), Is.EqualTo($"ns=1;s={pumpName}.Type"));
        }

        /// <summary>
        /// Verifies that pump node set roots the pump under the objects folder.
        /// </summary>
        [Test]
        public void PumpNodeSetRootsThePumpUnderTheObjectsFolder()
        {
            UANodeSet nodeSet = ReadPumpNodeSet();
            UANode[] roots = [.. nodeSet.Items!.Where(node => node.References?.Any(reference =>
                reference.ReferenceType == "i=35" && !reference.IsForward && reference.Value == "i=85") ==
                true)];
            Assert.That(
                roots.Select(node => node.NodeId),
                Is.EquivalentTo(s_pumpRootNodeIds));
            foreach (UANode root in roots)
            {
                Assert.That(root, Is.TypeOf<UAObject>(), root.NodeId);
                Assert.That(
                    root.References!.Count(reference => reference.ReferenceType == "i=35" &&
                        !reference.IsForward &&
                        reference.Value == "i=85"),
                    Is.EqualTo(1),
                    root.NodeId);
            }
        }

        /// <summary>
        /// Verifies that pump NodeSet has stable required hierarchy.
        /// </summary>
        /// <param name="pumpName">The sample pump instance name.</param>
        [TestCase("Pump1")]
        [TestCase("Pump2")]
        public void PumpNodeSetHasStableRequiredHierarchy(string pumpName)
        {
            UANodeSet nodeSet = ReadPumpNodeSet();

            foreach ((string nodeId, string browseName, string typeDefinition) in s_pumpNodes)
            {
                string expectedNodeId = nodeId.Replace("Pump1", pumpName, StringComparison.Ordinal);
                UANode node = FindNode<UANode>(nodeSet, expectedNodeId);
                Assert.That(
                    LocalName(node.BrowseName),
                    Is.EqualTo(browseName == "Pump_1"
                        ? pumpName.Replace("Pump", "Pump_", StringComparison.Ordinal)
                        : browseName),
                    expectedNodeId);
                Assert.That(
                    TypeDefinition(node),
                    Is.EqualTo(typeDefinition.Replace("Pump1", pumpName, StringComparison.Ordinal)),
                    expectedNodeId);
            }

            foreach (PropertyExpectation binding in s_propertyBindings)
            {
                AssertChildPath(nodeSet, $"ns=1;s={pumpName}", binding.LocalPath.Split('.'));
            }
            foreach (string action in s_managementMembers)
            {
                AssertChildPath(nodeSet, $"ns=1;s={pumpName}", action);
            }
        }

        /// <summary>
        /// Verifies that cloning a pump preserves its complete graph without introducing references to another pump.
        /// </summary>
        [Test]
        public void PumpNodeSetClonesTheCompleteGraphWithoutCrossPumpReferences()
        {
            var xml = XDocument.Load(DocumentPath("SamplePump.NodeSet2.xml"));
            XElement[] first = PumpXmlNodes(xml, "Pump1");
            XElement[] second = PumpXmlNodes(xml, "Pump2");
            Assert.That(first, Has.Length.GreaterThan(s_pumpNodes.Length));
            Assert.That(
                second.Select(node => ((string)node.Attribute("NodeId")!)["ns=1;s=Pump2".Length..]),
                Is.EquivalentTo(first.Select(node =>
                    ((string)node.Attribute("NodeId")!)["ns=1;s=Pump1".Length..])));

            foreach (XElement node in second)
            {
                string nodeId = (string)node.Attribute("NodeId")!;
                XElement original = first.Single(candidate =>
                    (string?)candidate.Attribute("NodeId") ==
                        nodeId.Replace("Pump2", "Pump1", StringComparison.Ordinal));
                string normalized = node.ToString(SaveOptions.DisableFormatting)
                    .Replace("Pump2", "Pump1", StringComparison.Ordinal)
                    .Replace("Pump_2", "Pump_1", StringComparison.Ordinal)
                    .Replace("Pump #2", "Pump #1", StringComparison.Ordinal)
                    .Replace("SN-002", "SN-001", StringComparison.Ordinal);
                Assert.That(normalized, Is.EqualTo(original.ToString(SaveOptions.DisableFormatting)), nodeId);
                Assert.That(
                    node.Descendants().Where(element => element.Name.LocalName == "Reference")
                        .Select(reference => reference.Value),
                    Has.None.StartsWith("ns=1;s=Pump1"),
                    nodeId);
            }
        }

        /// <summary>
        /// Verifies that pump node set preserves property data types and identity values.
        /// </summary>
        /// <param name="pumpName">The pump name used by this test case.</param>
        /// <param name="serialNumber">The serial number used by this test case.</param>
        [TestCase("Pump1", "SN-001")]
        [TestCase("Pump2", "SN-002")]
        public void PumpNodeSetPreservesPropertyDataTypesAndIdentityValues(string pumpName, string serialNumber)
        {
            UANodeSet nodeSet = ReadPumpNodeSet();
            foreach (PropertyExpectation binding in s_propertyBindings)
            {
                UAVariable variable = FindNode<UAVariable>(
                    nodeSet, $"ns=1;s={pumpName}.{binding.LocalPath}");
                Assert.That(ResolveAlias(nodeSet, variable.DataType), Is.EqualTo(binding.DataType), binding.Name);
                Assert.That(variable.ValueRank, Is.EqualTo(-1), binding.Name);
            }

            var manufacturer = XElement.Parse(
                FindNode<UAVariable>(nodeSet, $"ns=1;s={pumpName}.Identification.Manufacturer").Value!.OuterXml);
            Assert.That(
                manufacturer.Element(XName.Get("Text", Ua.Namespaces.OpcUaXsd))!.Value,
                Is.EqualTo("SimPump Corp"));
            Assert.That(
                FindNode<UAVariable>(nodeSet, $"ns=1;s={pumpName}.Identification.SerialNumber").Value!.InnerText,
                Is.EqualTo(serialNumber));
            Assert.That(
                FindNode<UAVariable>(nodeSet, $"ns=1;s={pumpName}.Identification.ProductInstanceUri").Value!.InnerText,
                Is.EqualTo($"urn:simdevice:SimPump:PumpX-2000:{serialNumber}"));
            foreach (string source in new[] { "SourceA", "SourceB" })
            {
                Assert.That(
                    FindNode<UAVariable>(nodeSet, $"ns=1;s={pumpName}.{source}Running").Value,
                    Is.Null,
                    "The live source supplies the running value, not a constant in the model.");
            }
        }

        /// <summary>
        /// Verifies that pump node set declares owned controls alarm types and condition arguments.
        /// </summary>
        /// <param name="pumpName">The pump name used by this test case.</param>
        [TestCase("Pump1")]
        [TestCase("Pump2")]
        public void PumpNodeSetDeclaresOwnedControlsAlarmTypesAndConditionArguments(string pumpName)
        {
            UANodeSet nodeSet = ReadPumpNodeSet();
            UAObject pump = FindNode<UAObject>(nodeSet, $"ns=1;s={pumpName}");
            Assert.That(pump.EventNotifier, Is.EqualTo(1));
            Assert.That(
                nodeSet.Items!.OfType<UAMethod>().Where(method =>
                    method.NodeId!.StartsWith($"ns=1;s={pumpName}.", StringComparison.Ordinal))
                    .Select(method => LocalName(method.BrowseName)),
                Is.EquivalentTo(s_managementMembers));
            // OPC 10000-3 §7.15: an Object is never the source of
            // GeneratesEvent, so the pump's alarms are declared by its own
            // subtype of the companion PumpType instead.
            Assert.That(
                pump.References!.Any(reference =>
                    ResolveAlias(nodeSet, reference.ReferenceType) == "i=41"),
                Is.False);
            string pumpTypeId = $"ns=1;s={pumpName}.Type";
            Assert.That(
                pump.References!.Single(reference => reference.IsForward &&
                    ResolveAlias(nodeSet, reference.ReferenceType) == "i=40").Value,
                Is.EqualTo(pumpTypeId));
            UAObjectType pumpType = FindNode<UAObjectType>(nodeSet, pumpTypeId);
            Assert.That(
                pumpType.References!.Single(reference => !reference.IsForward &&
                    ResolveAlias(nodeSet, reference.ReferenceType) == "i=45").Value,
                Is.EqualTo("ns=2;i=1052"),
                "The pump's type derives from the companion PumpType.");
            Assert.That(
                pumpType.References!.Where(reference => reference.IsForward &&
                    ResolveAlias(nodeSet, reference.ReferenceType) == "i=41").Select(reference => reference.Value),
                Is.EquivalentTo(s_supervisionMembers.Select(name => $"ns=1;s={pumpName}.{name}")));

            var xml = XDocument.Load(DocumentPath("SamplePump.NodeSet2.xml"));
            XNamespace types = "http://opcfoundation.org/UA/2008/02/Types.xsd";
            foreach (string name in s_managementMembers)
            {
                string nodeId = $"ns=1;s={pumpName}.{name}";
                UAMethod method = FindNode<UAMethod>(nodeSet, nodeId);
                Assert.That(method.ParentNodeId, Is.EqualTo($"ns=1;s={pumpName}"), name);
                Assert.That(method.Executable, Is.True, name);
                Assert.That(method.UserExecutable, Is.True, name);
                Assert.That(
                    method.References!.Any(reference => !reference.IsForward &&
                        ResolveAlias(nodeSet, reference.ReferenceType) == "i=47" &&
                        reference.Value == $"ns=1;s={pumpName}"),
                    Is.True,
                    name);
                bool condition = name.EndsWith("Acknowledge", StringComparison.Ordinal) ||
                    name.EndsWith("Confirm", StringComparison.Ordinal);
                if (!condition)
                {
                    Assert.That(method.MethodDeclarationId, Is.Null.Or.Empty, name);
                    Assert.That(
                        nodeSet.Items!.Any(node => node.NodeId == nodeId + ".InputArguments"),
                        Is.False,
                        name);
                    continue;
                }

                Assert.That(
                    method.MethodDeclarationId,
                    Is.EqualTo(name.EndsWith("Acknowledge", StringComparison.Ordinal) ? "i=9111" : "i=9113"),
                    name);
                UAVariable input = FindNode<UAVariable>(nodeSet, nodeId + ".InputArguments");
                Assert.That(input.ParentNodeId, Is.EqualTo(nodeId), name);
                Assert.That(
                    method.References!.Count(reference => reference.IsForward &&
                        ResolveAlias(nodeSet, reference.ReferenceType) == "i=46" &&
                        reference.Value == nodeId + ".InputArguments"),
                    Is.EqualTo(1),
                    name);
                Assert.That(
                    input.References!.Count(reference => !reference.IsForward &&
                        ResolveAlias(nodeSet, reference.ReferenceType) == "i=46" &&
                        reference.Value == nodeId),
                    Is.EqualTo(1),
                    name);
                Assert.That(ResolveAlias(nodeSet, input.DataType), Is.EqualTo("i=296"), name);
                Assert.That(input.ValueRank, Is.EqualTo(1), name);
                Assert.That(input.ArrayDimensions, Is.EqualTo("2"), name);
                Assert.That(TypeDefinition(input), Is.EqualTo("i=68"), name);
                XElement arguments = xml.Root!.Elements().Single(element =>
                    (string?)element.Attribute("NodeId") == nodeId + ".InputArguments");
                XElement[] entries = [.. arguments.Descendants(types + "Argument")];
                Assert.That(
                    entries.Select(argument => argument.Element(types + "Name")!.Value),
                    Is.EqualTo(s_conditionArgumentNames),
                    name);
                Assert.That(
                    entries.Select(argument => argument.Element(types + "DataType")!
                        .Element(types + "Identifier")!.Value),
                    Is.EqualTo(s_conditionArgumentTypes),
                    name);
                Assert.That(entries.Select(argument => argument.Element(types + "ValueRank")!.Value),
                    Is.All.EqualTo("-1"), name);
            }

            foreach (string name in s_supervisionMembers)
            {
                UAObjectType eventType = FindNode<UAObjectType>(nodeSet, $"ns=1;s={pumpName}.{name}");
                Assert.That(LocalName(eventType.BrowseName), Is.EqualTo(name));
                Assert.That(
                    eventType.References!.Where(reference => !reference.IsForward &&
                        ResolveAlias(nodeSet, reference.ReferenceType) == "i=45").Select(reference => reference.Value),
                    Is.EqualTo(s_alarmSuperTypes),
                    name);
            }
        }

        /// <summary>
        /// Verifies that pump NodeSet uses qualified parent node IDs.
        /// </summary>
        [Test]
        public void PumpNodeSetUsesQualifiedParentNodeIds()
        {
            var document = XDocument.Load(DocumentPath("SamplePump.NodeSet2.xml"));
            foreach (XElement element in document.Descendants())
            {
                XAttribute? parentNodeId = element.Attribute("ParentNodeId");
                if (parentNodeId is not null)
                {
                    Assert.That(
                        NodeId.TryParse(parentNodeId.Value, out _),
                        Is.True,
                        parentNodeId.Value);
                }
            }
        }

        /// <summary>
        /// Verifies that pump analog measurements carry engineering unit metadata.
        /// </summary>
        /// <param name="pumpName">The sample pump instance name.</param>
        [TestCase("Pump1")]
        [TestCase("Pump2")]
        public void PumpAnalogMeasurementsCarryEngineeringUnitMetadata(string pumpName)
        {
            var document = XDocument.Load(DocumentPath("SamplePump.NodeSet2.xml"));
            XNamespace nodeSetNamespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
            XNamespace typesNamespace = "http://opcfoundation.org/UA/2008/02/Types.xsd";

            foreach ((string name, string unit, string low, string high) in s_engineeringUnits)
            {
                string nodeId = $"ns=1;s={pumpName}.Operational.Measurements.{name}";
                XElement variable = document
                    .Descendants(nodeSetNamespace + "UAVariable")
                    .Single(element => (string?)element.Attribute("NodeId") == nodeId);
                string[] references = [.. variable
                    .Descendants(nodeSetNamespace + "Reference")
                    .Where(reference => (string?)reference.Attribute("ReferenceType") == "HasProperty")
                    .Select(reference => reference.Value)];
                XElement engineeringUnits = document
                    .Descendants(nodeSetNamespace + "UAVariable")
                    .Single(element => (string?)element.Attribute("NodeId") == $"{nodeId}.EngineeringUnits");
                XElement euRange = document
                    .Descendants(nodeSetNamespace + "UAVariable")
                    .Single(element => (string?)element.Attribute("NodeId") == $"{nodeId}.EURange");

                Assert.That(references, Does.Contain($"{nodeId}.EngineeringUnits"));
                Assert.That(references, Does.Contain($"{nodeId}.EURange"));
                Assert.That(
                    engineeringUnits.Descendants(typesNamespace + "DisplayName")
                        .Single()
                        .Element(typesNamespace + "Text")!
                        .Value,
                    Is.EqualTo(unit));
                Assert.That(euRange.Descendants(typesNamespace + "Range"), Has.Exactly(1).Items);
                XElement range = euRange.Descendants(typesNamespace + "Range").Single();
                Assert.That(range.Element(typesNamespace + "Low")!.Value, Is.EqualTo(low), name);
                Assert.That(range.Element(typesNamespace + "High")!.Value, Is.EqualTo(high), name);
            }
        }

        /// <summary>
        /// Verifies that pump mappings use both endpoint placeholders and portable OPC UA forms.
        /// </summary>
        [Test]
        public async Task PumpMappingsUseBothEndpointPlaceholdersAndPortableOpcUaForms()
        {
            ArrayOf<SampleDocument> documents = ReadPumpDocuments();
            // The complete linked set validates the readable claims that the partition-local reads below omit.
            UANodeSet linkedSet = await WotAggregationDocumentGenerator
                .ReadPumpDocumentSetAsync(ReadSubstitutedManifestDocuments().Filter(document =>
                    document.Path.StartsWith("sample-pump/", StringComparison.Ordinal))).ConfigureAwait(false);
            Assert.That(linkedSet.Items, Is.Not.Null.And.Not.Empty);
            var placeholders = new HashSet<string>(StringComparer.Ordinal);
            foreach (string pumpName in s_pumpAssetNames)
            {
                foreach (PropertyExpectation binding in s_propertyBindings)
                {
                    string localId = LocalNodeId(pumpName, binding.LocalPath);
                    Affordance property = FindAffordance(documents, "properties", localId);
                    int separator = binding.LocalPath.LastIndexOf('.');
                    string ownerPath = separator < 0 ? string.Empty : binding.LocalPath[..separator];
                    Assert.That(property.Root.GetProperty("uav:id").GetString(),
                        Is.EqualTo(LocalNodeId(pumpName, ownerPath)), binding.Name);
                    JsonElement form = AssertSingleForm(property.Value, binding.Name);
                    Assert.That(
                        property.Value.GetProperty("uav:mapToNodeId").GetString(),
                        Is.EqualTo(localId),
                        binding.Name);
                    Assert.That(
                        property.Value.GetProperty("uav:mapToType").GetString(),
                        Is.EqualTo(binding.DataType),
                        binding.Name);
                    Assert.That(form.GetProperty("href").GetString(), Is.EqualTo(SourceEndpoint(binding.Source)));
                    Assert.That(
                        form.GetProperty("uav:id").GetString(),
                        Is.EqualTo(SourceNodeId(binding.Source, pumpName, binding.SourcePath)),
                        binding.Name);
                    Assert.That(
                        StringValues(form.GetProperty("op")),
                        Is.EqualTo(binding.IsIdentity
                            ? s_readOperations
                            : s_observationOperations),
                        binding.Name);
                    Assert.That(property.Root.TryGetProperty("uav:nodeSet", out _), Is.False, property.ResourceId);
                    AssertPropertyHasNativeOrReadableNode(property);
                    Assert.That(property.Root.TryGetProperty("uav:nodeSet", out _), Is.False, property.ResourceId);
                    placeholders.Add(form.GetProperty("href").GetString()!);
                }
            }

            Assert.That(placeholders, Is.EquivalentTo(s_endpointPlaceholders));
            Assert.That(
                ReadAffordances(documents, "properties").Count(property =>
                    property.Value.TryGetProperty("uav:mapToNodeId", out _)),
                Is.EqualTo(30));
            foreach (SampleDocument document in documents)
            {
                Assert.That(
                    SubstituteEndpoints(Encoding.UTF8.GetString(document.Json.ToArray())),
                    Does.Not.Contain("${"),
                    document.Path);
            }
        }

        /// <summary>
        /// Verifies that linked pump identities and running states retain their source schemas.
        /// </summary>
        /// <param name="pumpName">The pump name used by this test case.</param>
        /// <param name="serialNumber">The serial number used by this test case.</param>
        [TestCase("Pump1", "SN-001")]
        [TestCase("Pump2", "SN-002")]
        public void LinkedPumpIdentitiesAndRunningStatesRetainTheirSourceSchemas(
            string pumpName,
            string serialNumber)
        {
            ArrayOf<SampleDocument> documents = ReadPumpDocuments();
            foreach ((string name, string expected, string dataType) in new[]
            {
                ("Manufacturer", "SimPump Corp", "i=21"),
                ("SerialNumber", serialNumber, "i=12"),
                ("ProductInstanceUri", $"urn:simdevice:SimPump:PumpX-2000:{serialNumber}", "i=12")
            })
            {
                Affordance property = FindAffordance(
                    documents, "properties", LocalNodeId(pumpName, "Identification." + name));
                if (dataType == "i=21")
                {
                    Assert.That(property.Value.TryGetProperty("type", out _), Is.False,
                        "LocalizedText must not be constrained to a plain string that cannot retain its locale.");
                    UAVariable variable = FindNode<UAVariable>(
                        ReadPumpNodeSet(), $"ns=1;s={pumpName}.Identification.{name}");
                    var value = XElement.Parse(variable.Value!.OuterXml);
                    Assert.That(
                        value.Element(XName.Get("Text", Ua.Namespaces.OpcUaXsd))!.Value,
                        Is.EqualTo(expected));
                }
                else
                {
                    Assert.That(property.Value.GetProperty("type").GetString(), Is.EqualTo("string"), name);
                    Assert.That(property.Value.GetProperty("const").GetString(), Is.EqualTo(expected), name);
                }
                Assert.That(property.Value.GetProperty("uav:mapToType").GetString(), Is.EqualTo(dataType), name);
                Assert.That(
                    AssertSingleForm(property.Value, name).GetProperty("uav:id").GetString(),
                    Is.EqualTo(SourceNodeId("SourceA", pumpName, "Identification." + name)),
                    name);
            }
            foreach (string source in new[] { "SourceA", "SourceB" })
            {
                Affordance property = FindAffordance(
                    documents, "properties", LocalNodeId(pumpName, source + "Running"));
                Assert.That(property.Value.GetProperty("type").GetString(), Is.EqualTo("boolean"), source);
                Assert.That(property.Value.TryGetProperty("const", out _), Is.False, source);
                Assert.That(property.Value.GetProperty("uav:mapToType").GetString(), Is.EqualTo("i=1"), source);
                Assert.That(
                    AssertSingleForm(property.Value, source).GetProperty("uav:id").GetString(),
                    Is.EqualTo(SourceNodeId(source, pumpName, "Running")),
                    source);
            }
        }

        /// <summary>
        /// Verifies that live alarm signals are not constrained to their initial value.
        /// </summary>
        /// <param name="pumpName">The sample pump instance name.</param>
        /// <param name="path">The qualified browse path identifying the captured event field.</param>
        [TestCase("Pump1", "Events.SupervisionProcessFluid.Cavitation")]
        [TestCase("Pump1", "Events.SupervisionPumpOperation.MotorOverheat")]
        [TestCase("Pump2", "Events.SupervisionProcessFluid.Cavitation")]
        [TestCase("Pump2", "Events.SupervisionPumpOperation.MotorOverheat")]
        public void LiveAlarmSignalsAreNotConstrainedToTheirInitialValue(string pumpName, string path)
        {
            Affordance property = FindAffordance(
                ReadPumpDocuments(), "properties", LocalNodeId(pumpName, path));
            Assert.That(property.Value.GetProperty("type").GetString(), Is.EqualTo("boolean"));
            Assert.That(property.Value.TryGetProperty("const", out _), Is.False);
            Assert.That(
                StringValues(AssertSingleForm(property.Value, path).GetProperty("op")),
                Does.Contain("readproperty").And.Contain("observeproperty"));
        }

        /// <summary>
        /// Verifies that pump controls have one form owned by exactly one source.
        /// </summary>
        /// <param name="pumpName">The sample pump instance name.</param>
        [TestCase("Pump1")]
        [TestCase("Pump2")]
        public void PumpControlsHaveOneFormOwnedByExactlyOneSource(string pumpName)
        {
            ArrayOf<SampleDocument> documents = ReadPumpDocuments();
            foreach (string source in new[] { "SourceA", "SourceB" })
            {
                foreach (string operation in new[] { "Start", "Stop", "Reset" })
                {
                    string name = source + operation;
                    Affordance action = FindAffordance(documents, "actions", LocalNodeId(pumpName, name));
                    Assert.That(action.Root.GetProperty("uav:id").GetString(),
                        Is.EqualTo(LocalNodeId(pumpName, string.Empty)), name);
                    JsonElement form = AssertSingleForm(action.Value, name);
                    Assert.That(form.GetProperty("href").GetString(), Is.EqualTo(SourceEndpoint(source)), name);
                    Assert.That(
                        form.GetProperty("uav:componentOf").GetString(),
                        Is.EqualTo(SourceNodeId(source, pumpName, string.Empty)),
                        name);
                    Assert.That(form.GetProperty("uav:id").GetString(),
                        Is.EqualTo(SourceNodeId(source, pumpName, operation)), name);
                    Assert.That(StringValues(form.GetProperty("op")), Is.EqualTo(s_invokeOperations), name);
                    Assert.That(action.Value.TryGetProperty("uav:mapToNodeId", out _), Is.False, name);
                    Assert.That(action.Value.TryGetProperty("uav:conditionAction", out _), Is.False, name);
                    Assert.That(action.Value.TryGetProperty("input", out _), Is.False, name);
                }
            }
        }

        /// <summary>
        /// Verifies that pump condition actions use occurrence arguments and their same document event.
        /// </summary>
        /// <param name="pumpName">The sample pump instance name.</param>
        [TestCase("Pump1")]
        [TestCase("Pump2")]
        public void PumpConditionActionsUseOccurrenceArgumentsAndTheirSameDocumentEvent(string pumpName)
        {
            ArrayOf<SampleDocument> documents = ReadPumpDocuments();
            foreach ((string alarm, string source, string path) in s_alarmBindings)
            {
                Affordance alarmEvent = FindAffordance(
                    documents, "events", LocalNodeId(pumpName, alarm + "Alarm"));
                Assert.That(alarmEvent.Root.GetProperty("uav:id").GetString(),
                    Is.EqualTo(LocalNodeId(pumpName, string.Empty)), alarm);
                foreach ((string operation, string methodId) in new[]
                {
                    ("Acknowledge", "i=9111"),
                    ("Confirm", "i=9113")
                })
                {
                    string name = alarm + operation;
                    Affordance action = FindAffordance(documents, "actions", LocalNodeId(pumpName, name));
                    Assert.That(action.ResourceId, Is.EqualTo(alarmEvent.ResourceId), name);
                    Assert.That(action.Value.GetProperty("uav:actsOn").GetString(), Is.EqualTo(alarmEvent.Name), name);
                    Assert.That(action.Value.GetProperty("uav:conditionAction").GetString(),
                        Is.EqualTo(operation), name);
                    Assert.That(action.Value.TryGetProperty("uav:mapToNodeId", out _), Is.False, name);
                    AssertConditionInput(action.Value.GetProperty("input"), name);

                    JsonElement form = AssertSingleForm(action.Value, name);
                    Assert.That(form.GetProperty("href").GetString(), Is.EqualTo(SourceEndpoint(source)), name);
                    Assert.That(form.GetProperty("uav:id").GetString(), Is.EqualTo(methodId), name);
                    Assert.That(form.GetProperty("uav:componentOf").GetString(),
                        Is.EqualTo(SourceNodeId(source, pumpName, path)), name);
                    Assert.That(StringValues(form.GetProperty("op")), Is.EqualTo(s_invokeOperations), name);
                }
            }
            Assert.That(
                ReadAffordances(documents, "actions")
                    .Where(action => action.Value.GetProperty("uav:id").GetString()!
                        .StartsWith(LocalNodeId(pumpName, string.Empty) + ".", StringComparison.Ordinal))
                    .Select(action => action.Value.GetProperty("uav:id").GetString()),
                Is.EquivalentTo(s_managementMembers.Select(name => LocalNodeId(pumpName, name))));
        }

        /// <summary>
        /// Verifies that pump alarms select every declared field against namespace zero.
        /// </summary>
        /// <param name="pumpName">The pump name used by this test case.</param>
        [TestCase("Pump1")]
        [TestCase("Pump2")]
        public async Task PumpAlarmsSelectEveryDeclaredFieldAgainstNamespaceZero(string pumpName)
        {
            ArrayOf<SampleDocument> documents = ReadPumpDocuments();
            var resolver = new WotEventSelectionResolver(
                new WotAggregationDocumentGenerator.SampleThingResolver(documents),
                WotAggregationDocumentGenerator.CreateLargeDocumentOptions());
            foreach ((string alarm, string source, _) in s_alarmBindings)
            {
                Affordance alarmEvent = FindAffordance(
                    documents, "events", LocalNodeId(pumpName, alarm + "Alarm"));
                JsonElement value = alarmEvent.Value;
                JsonElement form = AssertSingleForm(value, alarm);
                Assert.That(form.GetProperty("href").GetString(), Is.EqualTo(SourceEndpoint(source)), alarm);
                Assert.That(form.GetProperty("uav:id").GetString(),
                    Is.EqualTo(SourceNodeId(source, pumpName, string.Empty)), alarm);
                Assert.That(StringValues(form.GetProperty("op")),
                    Is.EqualTo(s_eventOperations), alarm);
                Assert.That(value.TryGetProperty("uav:mapToNodeId", out _), Is.False, alarm);
                Assert.That(value.TryGetProperty("uav:select", out _), Is.False, alarm);
                Assert.That(value.GetProperty("uav:conditionType").GetString(), Is.EqualTo("ua:AlarmConditionType"));
                Assert.That(value.GetProperty("uav:conditionTypeId").GetString(), Is.EqualTo("i=2915"));

                JsonElement selection = value.GetProperty("uav:eventSelectClauses");
                string definitionReference = selection[0].GetProperty("tm:ref").GetString()!;
                Assert.That(definitionReference,
                    Does.StartWith(alarmEvent.ResourceId + "#/schemaDefinitions/"));
                JsonElement definition = ResolveReference(documents, definitionReference);
                Assert.That(TypeNames(definition), Does.Contain("uav:eventType"));
                Assert.That(definition.GetProperty("uav:id").GetString(), Is.EqualTo("i=2915"));
                JsonElement data = definition.GetProperty("data");
                AssertAlarmDataSchema(data, alarm);
                string[] expectedPaths = [.. SchemaLeafPaths(data).Select(ToEventBrowsePath)];
                Assert.That(
                    selection.EnumerateArray().Select(clause => clause.GetProperty("uav:browsePath").GetString()),
                    Is.EquivalentTo(expectedPaths.Select(path => path.Length == 0
                        ? string.Empty
                        : string.Join("/", path.Split('/').Select(element => "ua:" + element)))),
                    alarm);
                Assert.That(
                    selection.EnumerateArray().Select(clause => clause.GetProperty("tm:ref").GetString()),
                    Is.All.EqualTo(definitionReference),
                    alarm);

                using var document = WotDocument.Parse(
                    documents.ToList().Single(
                        candidate => candidate.ResourceId == alarmEvent.ResourceId).Json.ToArray(),
                    WotAggregationDocumentGenerator.CreateLargeDocumentOptions());
                WotConversionResult<WotEventSelectionCatalog> result = await resolver
                    .ResolveAsync(document).ConfigureAwait(false);
                Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
                Assert.That(
                    result.Value!.TryGetSelection(alarmEvent.Name, out ArrayOf<WotResolvedEventSelectClause> clauses),
                    Is.True,
                    alarm);
                Assert.That(clauses.Count, Is.EqualTo(expectedPaths.Length), alarm);
                Assert.That(clauses.ToList().Select(clause => clause.TypeDefinitionId),
                    Is.All.EqualTo("i=2915"), alarm);
                Assert.That(clauses.ToList().Count(clause => clause.IsConditionIdSelection), Is.EqualTo(1), alarm);
                Assert.That(
                    clauses.ToList().Select(clause => string.Join("/", clause.MemberPath.ToList())),
                    Is.EquivalentTo(SchemaLeafPaths(data)),
                    alarm);
            }
        }

        /// <summary>
        /// Verifies that pump mappings compile after endpoint substitution.
        /// </summary>
        [Test]
        public async Task PumpMappingsCompileAfterEndpointSubstitution()
        {
            var documents = ReadPumpDocuments()
                .ToArrayOf(document => document with
                {
                    Json = ByteString.From(Encoding.UTF8.GetBytes(
                        SubstituteEndpoints(Encoding.UTF8.GetString(document.Json.ToArray()))))
                });
            var resolver = new WotAggregationDocumentGenerator.SampleThingResolver(documents);
            var registry = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());
            var compiled = new List<WotCompiledForm>();
            for (int documentIndex = 0; documentIndex < documents.Count; documentIndex++)
            {
                SampleDocument document = documents[documentIndex];
                var diagnostics = new List<WotDiagnostic>();
                WotBindingPlanRequest request = await WotBindingPlanRequest.FromDocumentAsync(
                    document.ResourceId, document.DocumentKind, document.Json.ToArray(), resolver,
                    diagnostics: diagnostics).ConfigureAwait(false);
                Assert.That(
                    diagnostics.Where(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error),
                    Is.Empty,
                    document.Path);
                WotBindingPlan plan = registry.Prepare(request);
                Assert.That(plan.FullySupported, Is.True, document.Path);
                compiled.AddRange(plan.CompiledForms);
                foreach (WotCompiledForm form in plan.CompiledForms)
                {
                    Assert.That(
                        form.TargetMapping.IsEmpty,
                        Is.EqualTo(form.AffordanceKind != BindingAffordanceKind.Property),
                        document.ResourceId + ": " + form.AffordanceName);
                    if (form.AffordanceKind == BindingAffordanceKind.Event)
                    {
                        Affordance declaration = ReadAffordances(documents, "events")
                            .Single(candidate => candidate.ResourceId == document.ResourceId &&
                                candidate.Name == form.AffordanceName);
                        JsonElement authored = declaration.Value.GetProperty("uav:eventSelectClauses");
                        Assert.That(form.EventSelection!.Origin, Is.EqualTo(WotEventSelectionOrigin.Standard));
                        Assert.That(form.EventSelection.Clauses.Count, Is.EqualTo(authored.GetArrayLength()));
                        Assert.That(
                            form.EventSelection.Clauses.ToList().Select(clause => clause.BrowsePath),
                            Is.EqualTo(authored.EnumerateArray().Select(clause =>
                                clause.GetProperty("uav:browsePath").GetString()!
                                    .Replace("ua:", string.Empty, StringComparison.Ordinal))));
                        Assert.That(form.EventSelection.Clauses.ToList().Select(clause => clause.TypeDefinitionId),
                            Is.All.EqualTo("i=2915"));
                    }
                }
            }
            foreach (string pumpName in s_pumpAssetNames)
            {
                WotCompiledForm[] properties = [.. compiled.Where(form =>
                    form.AffordanceKind == BindingAffordanceKind.Property &&
                    form.TargetMapping.TargetNodeId!.StartsWith(
                        LocalNodeId(pumpName, string.Empty) + ".", StringComparison.Ordinal))];
                Assert.That(
                    properties.Where(form => form.OpToken == "readproperty")
                        .Select(form => form.TargetMapping.TargetNodeId),
                    Is.EquivalentTo(s_propertyBindings.Select(binding => LocalNodeId(pumpName, binding.LocalPath))));
                Assert.That(
                    properties.Where(form => form.OpToken == "observeproperty")
                        .Select(form => form.TargetMapping.TargetNodeId),
                    Is.EquivalentTo(s_propertyBindings.Where(binding => !binding.IsIdentity)
                        .Select(binding => LocalNodeId(pumpName, binding.LocalPath))));
                foreach (PropertyExpectation binding in s_propertyBindings)
                {
                    foreach (WotCompiledForm form in properties.Where(form =>
                        form.TargetMapping.TargetNodeId == LocalNodeId(pumpName, binding.LocalPath)))
                    {
                        Assert.That(form.Addressing.Target,
                            Is.EqualTo(SourceNodeId(binding.Source, pumpName, binding.SourcePath)));
                        Assert.That(form.Endpoint.Host,
                            Is.EqualTo(binding.Source == "SourceA" ? "source-a" : "source-b"));
                    }
                }
            }
            Assert.That(compiled.Count(form => form.AffordanceKind == BindingAffordanceKind.Action),
                Is.EqualTo(s_pumpAssetNames.Length * s_managementMembers.Length));
            Assert.That(compiled.Count(form => form.AffordanceKind == BindingAffordanceKind.Event),
                Is.EqualTo(s_pumpAssetNames.Length * s_supervisionMembers.Length));
            Assert.That(compiled.Where(form => form.AffordanceKind == BindingAffordanceKind.Event)
                .Select(form => form.OpToken), Is.All.EqualTo("subscribeevent"));
        }

        /// <summary>
        /// Verifies that structured mapping example is dedicated and compiles after endpoint substitution.
        /// </summary>
        [Test]
        public void StructuredMappingExampleIsDedicatedAndCompilesAfterEndpointSubstitution()
        {
            string document = File.ReadAllText(StructuredExamplePath)
                .Replace("${SOURCE_A_ENDPOINT}", "opc.tcp://source-a:4840", StringComparison.Ordinal);
            var registry = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "structured-pump-example",
                WoTDocumentKindEnum.ThingDescription,
                Encoding.UTF8.GetBytes(document)));

            Assert.That(plan.FullySupported, Is.True);
            Assert.That(plan.CompiledForms, Has.Length.EqualTo(1));
            Assert.That(
                plan.CompiledForms[0].TargetMapping.TargetTypeNodeId,
                Is.EqualTo(
                    "nsu=urn:opcfoundation.org:UA:WotAggregation:StructuredExample;i=3001"));
            Assert.That(
                plan.CompiledForms[0].TargetMapping.FieldPath,
                Is.EqualTo("Process/DifferentialPressure"));
        }

        private static string RepositoryRoot
        {
            get
            {
                string? configured = Environment.GetEnvironmentVariable("OPCUA_TEST_REPOSITORY_ROOT");
                if (!string.IsNullOrEmpty(configured))
                {
                    string root = Path.GetFullPath(configured);
                    return File.Exists(Path.Combine(root, "UA.slnx"))
                        ? root
                        : throw new DirectoryNotFoundException(
                            "OPCUA_TEST_REPOSITORY_ROOT does not identify a repository containing UA.slnx.");
                }
                DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
                while (directory is not null &&
                    !File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
                {
                    directory = directory.Parent;
                }
                return directory?.FullName
                    ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
            }
        }

        private static string StructuredExamplePath => RepositoryPath(
            "tests",
            "Opc.Ua.WotCon.Tests",
            "Resources",
            "WotAggregation",
            "StructuredPumpMappingExample.td.json");

        private static UANodeSet ReadPumpNodeSet()
        {
            return WotAggregationDocumentGenerator.ReadNodeSet(
                DocumentPath("SamplePump.NodeSet2.xml"));
        }

        private static ArrayOf<SampleDocument> ReadManifestDocuments()
        {
            return WotAggregationDocumentGenerator.ReadManifestDocuments(DocumentPath(string.Empty));
        }

        private static ArrayOf<SampleDocument> ReadSubstitutedManifestDocuments()
        {
            return SubstituteEndpoints(ReadManifestDocuments());
        }

        private static ArrayOf<SampleDocument> ReadPumpDocuments()
        {
            return ReadManifestDocuments().Filter(document =>
                document.Path.StartsWith("sample-pump/", StringComparison.Ordinal));
        }

        private static async Task AssertProjectionMembersAsync(
            WotProjectionResolver resolver,
            string fileName,
            string mapName,
            string[] expectedMembers)
        {
            using var document = WotDocument.Parse(
                File.ReadAllBytes(DocumentPath(fileName)),
                WotAggregationDocumentGenerator.CreateLargeDocumentOptions());
            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document)
                .ConfigureAwait(false);

            Assert.That(result.Success, Is.True, fileName);
            using WotDocument view = result.Value!;
            JsonElement map = view.RootElement.GetProperty(mapName);
            string pumpName = fileName[..fileName.IndexOf('.', StringComparison.Ordinal)];
            ArrayOf<SampleDocument> sources = ReadSubstitutedManifestDocuments().Filter(candidate =>
                candidate.Path.StartsWith("sample-pump/", StringComparison.Ordinal));

            // A selected property that declares uav:unitProperty carries its unit property into the view as a
            // supporting dependency, so the declared units are expected alongside the selected members.
            var expectedSupport = new List<(string Id, Affordance Declaration)>();
            if (mapName == "properties")
            {
                foreach (string selected in expectedMembers)
                {
                    string selectedPath = s_propertyBindings.Single(binding => binding.Name == selected).LocalPath;
                    Affordance selectedDeclaration = FindAffordance(
                        sources, mapName, LocalNodeId(pumpName, selectedPath));
                    if (selectedDeclaration.Value.TryGetProperty("uav:unitProperty", out JsonElement unitPointer))
                    {
                        string unitId = LocalNodeId(pumpName, selectedPath + ".EngineeringUnits");
                        Assert.That(unitPointer.GetString(), Does.StartWith("/properties/"), selected);
                        expectedSupport.Add((unitId, FindAffordance(sources, mapName, unitId)));
                    }
                }
            }

            var membersNameById = new Dictionary<string, string>(StringComparer.Ordinal);
            if (expectedSupport.Count > 0 && fileName != $"{pumpName}.Members.td.json")
            {
                using WotDocument membersDocument = WotDocument.Parse(
                    File.ReadAllBytes(DocumentPath($"{pumpName}.Members.td.json")),
                    WotAggregationDocumentGenerator.CreateLargeDocumentOptions());
                WotConversionResult<WotDocument> membersResult = await resolver.ResolveAsync(membersDocument)
                    .ConfigureAwait(false);
                Assert.That(membersResult.Success, Is.True, pumpName + " members");
                using WotDocument membersView = membersResult.Value!;
                foreach (JsonProperty entry in membersView.RootElement.GetProperty("properties").EnumerateObject())
                {
                    string entryId = entry.Value.GetProperty("uav:id").GetString()!;
                    Assert.That(membersNameById.TryAdd(entryId, entry.Name), Is.True,
                        "duplicate members-view identity " + entryId);
                }
            }

            using IDisposable multipleScope = Assert.EnterMultipleScope();
            Assert.That(
                PropertyNames(map).Where(name => expectedMembers.Contains(name)),
                Is.EquivalentTo(expectedMembers),
                fileName);
            Assert.That(
                map.EnumerateObject()
                    .Where(entry => !expectedMembers.Contains(entry.Name))
                    .Select(entry => entry.Value.GetProperty("uav:id").GetString()),
                Is.EquivalentTo(expectedSupport.Select(support => support.Id)),
                fileName + " supporting members");
            Assert.That(TypeNames(view.RootElement), Does.Not.Contain("uav:projection"), fileName);
            foreach ((string supportId, Affordance supportDeclaration) in expectedSupport)
            {
                JsonProperty[] carried = map.EnumerateObject().Where(entry =>
                    entry.Value.TryGetProperty("uav:id", out JsonElement id) && id.GetString() == supportId)
                    .ToArray();
                Assert.That(carried, Has.Length.EqualTo(1), fileName + " carried " + supportId);
                if (carried.Length != 1)
                {
                    continue;
                }
                JsonProperty support = carried[0];
                string supportReference = fileName == $"{pumpName}.Members.td.json"
                    ? supportDeclaration.ResourceId + "#" + supportDeclaration.Pointer
                    : membersNameById.TryGetValue(supportId, out string? membersName)
                        ? $"{pumpName.ToLowerInvariant()}-members#/properties/{EscapePointer(membersName)}"
                        : "<unmatched>";
                Assert.That(
                    support.Value.GetProperty("uav:resolvedFrom").GetString(),
                    Is.EqualTo(supportReference),
                    support.Name);
                Assert.That(
                    support.Value.GetProperty("uav:mapToType").GetString(),
                    Is.EqualTo(supportDeclaration.Value.GetProperty("uav:mapToType").GetString()),
                    support.Name);
                Assert.That(
                    support.Value.GetProperty("uav:browseName").GetString(),
                    Is.EqualTo(supportDeclaration.Value.GetProperty("uav:browseName").GetString()),
                    support.Name);
                AssertJsonEquivalent(
                    support.Value.TryGetProperty("uav:engineeringUnits", out JsonElement carriedUnits)
                        ? carriedUnits
                        : null,
                    supportDeclaration.Value.TryGetProperty(
                        "uav:engineeringUnits", out JsonElement declaredUnits) ? declaredUnits : null,
                    support.Name + " uav:engineeringUnits");
                Assert.That(
                    support.Value.GetProperty("type").GetString(),
                    Is.EqualTo(supportDeclaration.Value.GetProperty("type").GetString()),
                    support.Name);
                AssertJsonEquivalent(
                    support.Value.GetProperty("uav:componentOf"),
                    supportDeclaration.Value.GetProperty("uav:componentOf"),
                    support.Name + " uav:componentOf");
            }
            foreach (JsonProperty member in map.EnumerateObject().Where(entry => expectedMembers.Contains(entry.Name)))
            {
                string localPath = mapName == "properties"
                    ? s_propertyBindings.Single(binding => binding.Name == member.Name).LocalPath
                    : member.Name;
                string localId = LocalNodeId(pumpName, localPath);
                Affordance declaration = FindAffordance(sources, mapName, localId);
                string reference = declaration.ResourceId + "#" + declaration.Pointer;
                Assert.That(
                    member.Value.GetProperty("uav:resolvedFrom").GetString(),
                    Is.EqualTo(reference),
                    member.Name);
                Assert.That(member.Value.GetProperty("uav:id").GetString(), Is.EqualTo(localId), member.Name);
                JsonElement actualForm = AssertSingleForm(member.Value, member.Name);
                JsonElement sourceForm = AssertSingleForm(declaration.Value, member.Name);
                Assert.That(
                    actualForm.GetProperty("uav:id").GetString(),
                    Is.EqualTo(sourceForm.GetProperty("uav:id").GetString()),
                    member.Name);
                Assert.That(actualForm.GetProperty("href").GetString(),
                    Is.EqualTo(sourceForm.GetProperty("href").GetString()), member.Name);
                Assert.That(StringValues(actualForm.GetProperty("op")),
                    Is.EqualTo(StringValues(sourceForm.GetProperty("op"))), member.Name);
                if (sourceForm.TryGetProperty("uav:componentOf", out JsonElement sourceOwner))
                {
                    Assert.That(actualForm.GetProperty("uav:componentOf").GetString(),
                        Is.EqualTo(sourceOwner.GetString()), member.Name);
                }
                if (mapName == "properties")
                {
                    Assert.That(member.Value.GetProperty("uav:mapToNodeId").GetString(),
                        Is.EqualTo(localId), member.Name);
                    Assert.That(member.Value.GetProperty("uav:mapToType").GetString(),
                        Is.EqualTo(declaration.Value.GetProperty("uav:mapToType").GetString()), member.Name);
                    if (declaration.Value.TryGetProperty("uav:unitProperty", out _))
                    {
                        string unitId = LocalNodeId(pumpName, localPath + ".EngineeringUnits");
                        string unitReference = member.Value.GetProperty("uav:unitProperty").GetString()!;
                        Assert.That(unitReference, Does.StartWith("/properties/"), member.Name);
                        Assert.That(
                            map.TryGetProperty(unitReference["/properties/".Length..], out JsonElement unitMember),
                            Is.True,
                            member.Name + " unit reference " + unitReference);
                        Assert.That(
                            unitMember.ValueKind == JsonValueKind.Object &&
                            unitMember.GetProperty("uav:id").GetString() == unitId,
                            Is.True,
                            member.Name + " unit identity");
                    }
                    if (declaration.Value.TryGetProperty("const", out JsonElement constant))
                    {
                        AssertJsonEquivalent(member.Value.GetProperty("const"), constant, member.Name + " const");

                    }
                }
                else
                {
                    Assert.That(member.Value.TryGetProperty("uav:mapToNodeId", out _), Is.False, member.Name);
                }
                if (mapName == "actions" &&
                    (member.Name.EndsWith("Acknowledge", StringComparison.Ordinal) ||
                        member.Name.EndsWith("Confirm", StringComparison.Ordinal)))
                {
                    string eventName = member.Value.GetProperty("uav:actsOn").GetString()!;
                    Assert.That(eventName,
                        Is.EqualTo(declaration.Value.GetProperty("uav:actsOn").GetString()), member.Name);
                    JsonElement target = view.RootElement.GetProperty("events").GetProperty(eventName);
                    Assert.That(
                        target.GetProperty("uav:id").GetString(),
                        Is.EqualTo(LocalNodeId(pumpName, eventName)),
                        member.Name);
                }
            }
        }

        private static void AssertDependencyHasDeclaredReference(JsonElement source, SampleDocument dependency)
        {
            using var parsed = JsonDocument.Parse(dependency.Json.ToArray());
            JsonElement root = parsed.RootElement;
            var mentions = new HashSet<string>(SemanticStrings(source), StringComparer.Ordinal);
            var identities = new List<string>();
            if (root.TryGetProperty("uav:id", out JsonElement nodeId))
            {
                identities.Add(nodeId.GetString()!);
            }
            foreach (string mapName in new[] { "properties", "actions" })
            {
                identities.AddRange(ReadAffordances([dependency], mapName)
                    .Where(affordance => affordance.Value.TryGetProperty("uav:id", out _))
                    .Select(affordance => affordance.Value.GetProperty("uav:id").GetString()!));
            }
            bool found = identities.Any(mentions.Contains);
            if (!found &&
                root.TryGetProperty("uav:browseName", out JsonElement browseName) &&
                root.TryGetProperty("uav:id", out JsonElement typeId) &&
                typeId.GetString() is string portableTypeId &&
                ExpandedNodeId.TryParse(portableTypeId, out ExpandedNodeId expanded))
            {
                var names = new HashSet<string>(StringComparer.Ordinal)
                {
                    LocalName(browseName.GetString())
                };
                if (root.TryGetProperty("uav:inverseName", out JsonElement inverseName))
                {
                    names.Add(inverseName.GetString()!);
                }
                if (source.TryGetProperty("@context", out JsonElement context))
                {
                    JsonElement[] contexts = context.ValueKind == JsonValueKind.Array
                        ? [.. context.EnumerateArray()]
                        : [context];
                    foreach (JsonElement entry in contexts.Where(entry => entry.ValueKind == JsonValueKind.Object))
                    {
                        foreach (JsonProperty prefix in entry.EnumerateObject())
                        {
                            if (prefix.Value.ValueKind == JsonValueKind.String &&
                                prefix.Value.GetString() == expanded.NamespaceUri)
                            {
                                found |= names.Any(name => mentions.Contains(prefix.Name + ":" + name));
                            }
                        }
                    }
                }
            }
            Assert.That(found, Is.True,
                $"{dependency.ResourceId} must own a declared reference, not merely precede this document.");
        }

        private static IEnumerable<string> SemanticStrings(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                yield return value.GetString()!;
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    foreach (string text in SemanticStrings(item))
                    {
                        yield return text;
                    }
                }
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (property.Name is "forms" or "@context" or "const" or "default" or "enum" or "examples")
                    {
                        continue;
                    }
                    foreach (string text in SemanticStrings(property.Value))
                    {
                        yield return text;
                    }
                }
            }
        }

        private static HashSet<string> LogicalDependencies(
            JsonElement root,
            string resourceId,
            IEnumerable<string> resourceIds)
        {
            var known = new HashSet<string>(resourceIds, StringComparer.Ordinal);
            var dependencies = new HashSet<string>(StringComparer.Ordinal);
            foreach (string reference in LogicalReferences(root))
            {
                string href = reference.Split('#')[0];
                if (href.StartsWith("./", StringComparison.Ordinal))
                {
                    href = href[2..];
                }
                if (href.Length == 0 || href == resourceId)
                {
                    continue;
                }
                if (known.Contains(href))
                {
                    dependencies.Add(href);
                }
                else
                {
                    Assert.That(
                        ExpandedNodeId.TryParse(href, out _) || Uri.TryCreate(href, UriKind.Absolute, out _),
                        Is.True,
                        $"{resourceId} has an unresolved logical reference: {reference}");
                }
            }
            return dependencies;
        }

        private static IEnumerable<string> LogicalReferences(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    foreach (string reference in LogicalReferences(item))
                    {
                        yield return reference;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name is "forms" or "@context" or "const" or "default" or "enum" or "examples")
                    {
                        continue;
                    }
                    if (property.Name is "href" or "tm:ref" or "uav:componentOf")
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            yield return property.Value.GetString()!;
                        }
                        else if (property.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement item in property.Value.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    yield return item.GetString()!;
                                }
                            }
                        }
                    }
                    foreach (string reference in LogicalReferences(property.Value))
                    {
                        yield return reference;
                    }
                }
            }
        }

        private static void AssertJsonEquivalent(JsonElement? actual, JsonElement? expected, string message)
        {
            Assert.That(actual.HasValue, Is.EqualTo(expected.HasValue), message + " presence");
            if (actual.HasValue && expected.HasValue)
            {
                Assert.That(
                    JsonNode.DeepEquals(
                        JsonNode.Parse(actual.Value.GetRawText()),
                        JsonNode.Parse(expected.Value.GetRawText())),
                    Is.True,
                    message + Environment.NewLine + actual.Value.GetRawText() + Environment.NewLine +
                    expected.Value.GetRawText());
            }
        }
        private static Affordance FindAffordance(
            ArrayOf<SampleDocument> documents,
            string mapName,
            string nodeId)
        {
            // A pump raises the alarms its own type declares, so the type's
            // Thing Model states the same EventType as the pump's Thing
            // Description; the pump's is the one that is bound.
            Affordance[] matches = [.. ReadAffordances(documents, mapName).Where(affordance =>
                !TypeNames(affordance.Root).Contains("tm:ThingModel") &&
                affordance.Value.TryGetProperty("uav:id", out JsonElement id) &&
                id.GetString() == nodeId)];
            Assert.That(matches, Has.Length.EqualTo(1), $"{mapName}: {nodeId}");
            return matches[0];
        }

        private static IEnumerable<Affordance> ReadAffordances(ArrayOf<SampleDocument> documents, string mapName)
        {
            for (int documentIndex = 0; documentIndex < documents.Count; documentIndex++)
            {
                SampleDocument document = documents[documentIndex];
                using var parsed = JsonDocument.Parse(document.Json.ToArray());
                JsonElement root = parsed.RootElement.Clone();
                if (root.TryGetProperty(mapName, out JsonElement map))
                {
                    foreach (Affordance affordance in ReadMap(map, "/" + mapName))
                    {
                        yield return affordance;
                    }
                }

                IEnumerable<Affordance> ReadMap(JsonElement members, string pointer)
                {
                    foreach (JsonProperty member in members.EnumerateObject())
                    {
                        string memberPointer = pointer + "/" + EscapePointer(member.Name);
                        yield return new Affordance(
                            document.ResourceId, memberPointer, member.Name, root, member.Value);
                        if (mapName == "properties" &&
                            member.Value.TryGetProperty("properties", out JsonElement nested))
                        {
                            foreach (Affordance descendant in ReadMap(nested, memberPointer + "/properties"))
                            {
                                yield return descendant;
                            }
                        }
                    }
                }
            }
        }

        private static SampleDocument FindDocumentByIdentity(ArrayOf<SampleDocument> documents, string nodeId)
        {
            return documents.ToList().Single(document =>
            {
                using var parsed = JsonDocument.Parse(document.Json.ToArray());
                return parsed.RootElement.TryGetProperty("uav:id", out JsonElement id) && id.GetString() == nodeId;
            });
        }

        private static JsonElement ResolveReference(ArrayOf<SampleDocument> documents, string reference)
        {
            int fragment = reference.IndexOf('#', StringComparison.Ordinal);
            Assert.That(fragment, Is.GreaterThan(0), reference);
            string resourceId = reference[..fragment];
            SampleDocument document = documents.ToList().Single(candidate => candidate.ResourceId == resourceId);
            using var parsed = JsonDocument.Parse(document.Json.ToArray());
            JsonElement value = parsed.RootElement;
            string pointer = reference[(fragment + 1)..];
            Assert.That(pointer, Does.StartWith("/"), reference);
            foreach (string token in pointer[1..].Split('/'))
            {
                value = value.GetProperty(token.Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal));
            }
            return value.Clone();
        }

        private static JsonElement AssertSingleForm(JsonElement affordance, string origin)
        {
            JsonElement forms = affordance.GetProperty("forms");
            Assert.That(forms.GetArrayLength(), Is.EqualTo(1), origin);
            return forms[0];
        }

        private static void AssertPropertyHasNativeOrReadableNode(Affordance property)
        {
            if (!property.Root.TryGetProperty("uav:nodes", out _))
            {
                Assert.That(property.Value.GetProperty("@type").GetString(), Is.EqualTo("uav:variable"));
                return;
            }
            using var document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(property.Root.GetRawText()),
                WotAggregationDocumentGenerator.CreateLargeDocumentOptions());
            UANodeSet nodes = WotAggregationDocumentGenerator.ReadOwnedPartition(document, property.ResourceId);
            var namespaces = new NamespaceTable();
            foreach (string uri in nodes.NamespaceUris ?? [])
            {
                namespaces.Append(uri);
            }
            var id = ExpandedNodeId.ToNodeId(
                ExpandedNodeId.Parse(property.Value.GetProperty("uav:id").GetString()!), namespaces);
            Assert.That(
                nodes.Items!.OfType<UAVariable>().Count(node => node.NodeId == id.ToString()),
                Is.EqualTo(1),
                property.ResourceId + "/" + property.Name);
        }

        private static void AssertConditionInput(JsonElement input, string origin)
        {
            Assert.That(input.GetProperty("type").GetString(), Is.EqualTo("object"), origin);
            Assert.That(StringValues(input.GetProperty("uav:fieldOrder")),
                Is.EqualTo(s_conditionArgumentNames), origin);
            Assert.That(StringValues(input.GetProperty("required")), Is.EqualTo(s_requiredConditionInputs), origin);
            JsonElement properties = input.GetProperty("properties");
            Assert.That(PropertyNames(properties), Is.EquivalentTo(s_conditionArgumentNames), origin);
            JsonElement eventId = properties.GetProperty("EventId");
            Assert.That(eventId.GetProperty("type").GetString(), Is.EqualTo("string"), origin);
            Assert.That(eventId.GetProperty("contentEncoding").GetString(), Is.EqualTo("base64"), origin);
            Assert.That(eventId.GetProperty("uav:mapToType").GetString(), Is.EqualTo("i=15"), origin);
            Assert.That(eventId.GetProperty("uav:valueRank").GetInt32(), Is.EqualTo(-1), origin);
            JsonElement comment = properties.GetProperty("Comment");
            Assert.That(comment.TryGetProperty("type", out _), Is.False,
                "The definitive LocalizedText binding retains the locale as well as its text.");
            Assert.That(comment.GetProperty("uav:mapToType").GetString(), Is.EqualTo("i=21"), origin);
            Assert.That(comment.GetProperty("uav:valueRank").GetInt32(), Is.EqualTo(-1), origin);
        }

        private static void AssertAlarmDataSchema(JsonElement data, string origin)
        {
            JsonElement properties = data.GetProperty("properties");
            string[] required = StringValues(data.GetProperty("required"));
            foreach (string name in s_requiredEventMembers)
            {
                Assert.That(PropertyNames(properties), Does.Contain(name), origin);
                Assert.That(required, Does.Contain(name), origin);
            }
            Assert.That(properties.GetProperty("EventId").GetProperty("type").GetString(),
                Is.EqualTo("string"), origin);
            Assert.That(properties.GetProperty("EventId").GetProperty("contentEncoding").GetString(),
                Is.EqualTo("base64"), origin);
            Assert.That(properties.GetProperty("ConditionId").GetProperty("type").GetString(),
                Is.EqualTo("string"), origin);
            Assert.That(properties.GetProperty("BranchId").GetProperty("type").GetString(),
                Is.EqualTo("string"), origin);
            Assert.That(properties.GetProperty("Retain").GetProperty("type").GetString(),
                Is.EqualTo("boolean"), origin);
            foreach (string state in new[] { "EnabledState", "AckedState", "ConfirmedState", "ActiveState" })
            {
                JsonElement schema = properties.GetProperty(state);
                Assert.That(PropertyNames(schema.GetProperty("properties")),
                    Is.EquivalentTo(s_stateMembers), state);
                Assert.That(schema.TryGetProperty("required", out JsonElement stateRequired), Is.True, state);
                Assert.That(StringValues(stateRequired), Is.EquivalentTo(s_stateMembers), state);
                Assert.That(schema.GetProperty("properties").GetProperty("Id").GetProperty("type").GetString(),
                    Is.EqualTo("boolean"), state);
                Assert.That(schema.GetProperty("properties").GetProperty("Name").GetProperty("type").GetString(),
                    Is.EqualTo("string"), state);
            }
        }

        private static IEnumerable<string> SchemaLeafPaths(JsonElement schema, string prefix = "")
        {
            if (schema.TryGetProperty("properties", out JsonElement properties))
            {
                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    string path = prefix.Length == 0 ? property.Name : prefix + "/" + property.Name;
                    foreach (string leaf in SchemaLeafPaths(property.Value, path))
                    {
                        yield return leaf;
                    }
                }
            }
            else
            {
                yield return prefix;
            }
        }

        private static string ToEventBrowsePath(string memberPath)
        {
            if (memberPath == "ConditionId")
            {
                return string.Empty;
            }
            return memberPath.EndsWith("/Name", StringComparison.Ordinal) ? memberPath[..^5] : memberPath;
        }

        private static XElement[] PumpXmlNodes(XDocument document, string pumpName)
        {
            string prefix = "ns=1;s=" + pumpName;
            return [.. document.Root!.Elements().Where(element =>
                (string?)element.Attribute("NodeId") == prefix ||
                ((string?)element.Attribute("NodeId"))?.StartsWith(prefix + ".", StringComparison.Ordinal) == true)];
        }

        private static string? ResolveAlias(UANodeSet nodeSet, string? value)
        {
            return nodeSet.Aliases?.SingleOrDefault(alias => alias.Alias == value)?.Value ?? value;
        }

        private static string LocalNodeId(string pumpName, string path)
        {
            return $"nsu={PumpInstanceNamespace};s={pumpName}" + (path.Length == 0 ? string.Empty : "." + path);
        }

        private static string SourceNodeId(string source, string pumpName, string path)
        {
            return $"nsu=urn:opcfoundation.org:UA:WotAggregation:{source};s={pumpName}" +
                (path.Length == 0 ? string.Empty : "." + path);
        }

        private static string SourceEndpoint(string source)
        {
            return source == "SourceA" ? "${SOURCE_A_ENDPOINT}" : "${SOURCE_B_ENDPOINT}";
        }

        private static ArrayOf<SampleDocument> SubstituteEndpoints(ArrayOf<SampleDocument> documents)
        {
            return documents.ToArrayOf(document => document with
            {
                Json = ByteString.From(Encoding.UTF8.GetBytes(
                    SubstituteEndpoints(Encoding.UTF8.GetString(document.Json.ToArray()))))
            });
        }

        private static string SubstituteEndpoints(string json)
        {
            return json.Replace("${SOURCE_A_ENDPOINT}", "opc.tcp://source-a:4840", StringComparison.Ordinal)
                .Replace("${SOURCE_B_ENDPOINT}", "opc.tcp://source-b:4840", StringComparison.Ordinal);
        }

        private static string EscapePointer(string token)
        {
            return token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
        }

        private static string[] StringValues(JsonElement array)
        {
            return [.. array.EnumerateArray().Select(value => value.GetString()!)];
        }

        private static T FindNode<T>(UANodeSet nodeSet, string nodeId)
            where T : UANode
        {
            return nodeSet.Items!
                .OfType<T>()
                .Single(node => string.Equals(node.NodeId, nodeId, StringComparison.Ordinal));
        }

        private static string TypeDefinition(UANode node)
        {
            return node.References!
                .Single(reference =>
                    reference.IsForward &&
                    string.Equals(reference.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal))
                .Value!;
        }

        private static void AssertChildPath(
            UANodeSet nodeSet,
            string rootNodeId,
            params string[] browsePath)
        {
            UANode current = FindNode<UANode>(nodeSet, rootNodeId);
            foreach (string segment in browsePath)
            {
                string childNodeId = current.References!
                    .Where(reference =>
                        reference.IsForward &&
                        reference.ReferenceType is "HasComponent" or "HasProperty" or "i=47" or "i=46")
                    .Select(reference => reference.Value!)
                    .Single(nodeId => LocalName(FindNode<UANode>(nodeSet, nodeId).BrowseName) == segment);
                current = FindNode<UANode>(nodeSet, childNodeId);
            }
        }

        private static string LocalName(string? browseName)
        {
            int separator = browseName?.IndexOf(':', StringComparison.Ordinal) ?? -1;
            return separator < 0 ? browseName ?? string.Empty : browseName![(separator + 1)..];
        }

        private static string[] PropertyNames(JsonElement map)
        {
            return [.. map.EnumerateObject().Select(property => property.Name)];
        }

        private static string[] TypeNames(JsonElement root)
        {
            JsonElement type = root.GetProperty("@type");
            if (type.ValueKind == JsonValueKind.Array)
            {
                return [.. type.EnumerateArray().Select(item => item.GetString()!)];
            }
            return [type.GetString()!];
        }

        private static string DocumentPath(string fileName)
        {
            return RepositoryPath("samples", "WotCon", "AggregationClient", "Documents",
                fileName.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepositoryPath(params string[] segments)
        {
            string path = RepositoryRoot;
            foreach (string segment in segments)
            {
                path = Path.Combine(path, segment);
            }
            return path;
        }

        private sealed record ModelDocument(
            string FileName,
            string SourcePath,
            string Title,
            string ModelUri);

        private sealed record Affordance(
            string ResourceId,
            string Pointer,
            string Name,
            JsonElement Root,
            JsonElement Value);

        private sealed record PropertyExpectation(
            string Name,
            string LocalPath,
            string Source,
            string SourcePath,
            string DataType,
            bool IsIdentity = false);

        private static readonly ModelDocument[] s_modelDocuments =
        [
            new(
                "Opc.Ua.Di.tm.json",
                Path.Combine(
                    "tests",
                    "Opc.Ua.SourceGeneration.Core.Tests",
                    "Resources",
                    "Opc.Ua.Di.NodeSet2.xml"),
                "OPC UA Device Integration",
                DiNamespace),
            new(
                "Opc.Ua.Machinery.tm.json",
                Path.Combine(
                    "samples",
                    "DI",
                    "PumpDeviceIntegrationServer",
                    "Model",
                    "Opc.Ua.Machinery.NodeSet2.xml"),
                "OPC UA Machinery",
                MachineryNamespace),
            new(
                "Opc.Ua.Pumps.tm.json",
                Path.Combine(
                    "samples",
                    "DI",
                    "PumpDeviceIntegrationServer",
                    "Model",
                    "Opc.Ua.Pumps.NodeSet2.xml"),
                "OPC UA Pumps",
                PumpsNamespace)
        ];

        private static readonly string[] s_assetProjectionDocuments =
        [
            "Pump1.Members.td.json",
            "Pump1.ProcessData.td.json",
            "Pump1.ConditionData.td.json",
            "Pump1.Supervision.td.json",
            "Pump1.Management.td.json",
            "Pump1.Asset.td.json",
            "Pump2.Members.td.json",
            "Pump2.ProcessData.td.json",
            "Pump2.ConditionData.td.json",
            "Pump2.Supervision.td.json",
            "Pump2.Management.td.json",
            "Pump2.Asset.td.json"
        ];

        private static readonly string[] s_pumpAssetNames = ["Pump1", "Pump2"];

        private static readonly string[] s_assetGroupNames =
        [
            "ProcessData",
            "ConditionData",
            "Supervision",
            "Management"
        ];

        private static readonly string[] s_identityMembers =
        [
            "Manufacturer",
            "ProductInstanceUri",
            "SerialNumber"
        ];

        private static readonly string[] s_processDataMembers =
        [
            "DifferentialPressure",
            "FluidTemperature",
            "Level",
            "MassFlow"
        ];

        private static readonly string[] s_conditionDataMembers =
        [
            "BearingTemperature",
            "NumberOfStarts",
            "PumpEfficiency",
            "PumpPowerInput"
        ];

        private static readonly string[] s_supervisionMembers =
        [
            "CavitationAlarm",
            "MotorOverheatAlarm"
        ];

        private static readonly string[] s_managementMembers =
        [
            "SourceAStart",
            "SourceAStop",
            "SourceAReset",
            "SourceBStart",
            "SourceBStop",
            "SourceBReset",
            "CavitationAcknowledge",
            "CavitationConfirm",
            "MotorOverheatAcknowledge",
            "MotorOverheatConfirm"
        ];

        private static readonly PropertyExpectation[] s_propertyBindings =
        [
            new("DifferentialPressure", "Operational.Measurements.DifferentialPressure", "SourceA",
                "Operational.Measurements.DifferentialPressure", "i=11"),
            new("FluidTemperature", "Operational.Measurements.FluidTemperature", "SourceA",
                "Operational.Measurements.FluidTemperature", "i=11"),
            new("MassFlow", "Operational.Measurements.MassFlow", "SourceA",
                "Operational.Measurements.MassFlow", "i=11"),
            new("Level", "Operational.Measurements.Level", "SourceA", "Operational.Measurements.Level", "i=11"),
            new("BearingTemperature", "Operational.Measurements.BearingTemperature", "SourceB",
                "Operational.Measurements.BearingTemperature", "i=11"),
            new("PumpPowerInput", "Operational.Measurements.PumpPowerInput", "SourceB",
                "Operational.Measurements.PumpPowerInput", "i=11"),
            new("PumpEfficiency", "Operational.Measurements.PumpEfficiency", "SourceB",
                "Operational.Measurements.PumpEfficiency", "i=11"),
            new("NumberOfStarts", "Operational.Measurements.NumberOfStarts", "SourceB",
                "Operational.Measurements.NumberOfStarts", "i=7"),
            new("Cavitation", "Events.SupervisionProcessFluid.Cavitation", "SourceA",
                "Events.SupervisionProcessFluid.Cavitation", "i=1"),
            new("MotorOverheat", "Events.SupervisionPumpOperation.MotorOverheat", "SourceB",
                "Events.SupervisionPumpOperation.MotorOverheat", "i=1"),
            new("Manufacturer", "Identification.Manufacturer", "SourceA", "Identification.Manufacturer", "i=21", true),
            new("SerialNumber", "Identification.SerialNumber", "SourceA", "Identification.SerialNumber", "i=12", true),
            new("ProductInstanceUri", "Identification.ProductInstanceUri", "SourceA",
                "Identification.ProductInstanceUri", "i=12", true),
            new("SourceARunning", "SourceARunning", "SourceA", "Running", "i=1"),
            new("SourceBRunning", "SourceBRunning", "SourceB", "Running", "i=1")
        ];

        private static readonly (string Alarm, string Source, string ConditionPath)[] s_alarmBindings =
        [
            ("Cavitation", "SourceA", "Events.SupervisionProcessFluid.Cavitation.Alarm"),
            ("MotorOverheat", "SourceB", "Events.SupervisionPumpOperation.MotorOverheat.Alarm")
        ];

        private static readonly string[] s_requiredEventMembers =
        [
            "EventId", "EventType", "SourceNode", "SourceName", "Time", "ReceiveTime", "Message", "Severity",
            "ConditionId", "ConditionName", "BranchId", "Retain",
            "EnabledState", "AckedState", "ConfirmedState", "ActiveState"
        ];

        private static readonly (string NodeId, string BrowseName, string TypeDefinition)[] s_pumpNodes =
        [
            ("ns=1;s=Pump1", "Pump_1", "ns=1;s=Pump1.Type"),
            ("ns=1;s=Pump1.Identification", "Identification", "ns=2;i=1005"),
            ("ns=1;s=Pump1.Operational", "Operational", "ns=2;i=1053"),
            ("ns=1;s=Pump1.Operational.Measurements", "Measurements", "ns=2;i=1054"),
            (
                "ns=1;s=Pump1.Operational.Measurements.DifferentialPressure",
                "DifferentialPressure",
                "i=15318"),
            (
                "ns=1;s=Pump1.Operational.Measurements.FluidTemperature",
                "FluidTemperature",
                "i=15318"),
            (
                "ns=1;s=Pump1.Operational.Measurements.BearingTemperature",
                "BearingTemperature",
                "i=15318"),
            (
                "ns=1;s=Pump1.Operational.Measurements.PumpPowerInput",
                "PumpPowerInput",
                "i=15318"),
            ("ns=1;s=Pump1.Operational.Measurements.MassFlow", "MassFlow", "i=15318"),
            ("ns=1;s=Pump1.Operational.Measurements.PumpEfficiency", "PumpEfficiency", "i=15318"),
            ("ns=1;s=Pump1.Operational.Measurements.Level", "Level", "i=15318"),
            ("ns=1;s=Pump1.Operational.Measurements.NumberOfStarts", "NumberOfStarts", "i=15318"),
            ("ns=1;s=Pump1.Events", "Events", "ns=2;i=1019"),
            (
                "ns=1;s=Pump1.Events.SupervisionProcessFluid",
                "SupervisionProcessFluid",
                "ns=2;i=1015"),
            (
                "ns=1;s=Pump1.Events.SupervisionProcessFluid.Cavitation",
                "Cavitation",
                "i=2373"),
            (
                "ns=1;s=Pump1.Events.SupervisionPumpOperation",
                "SupervisionPumpOperation",
                "ns=2;i=1016"),
            (
                "ns=1;s=Pump1.Events.SupervisionPumpOperation.MotorOverheat",
                "MotorOverheat",
                "i=2373"),
            ("ns=1;s=Pump1.Maintenance", "Maintenance", "ns=2;i=1011")
        ];

        private static readonly (string Name, string Unit, string Low, string High)[] s_engineeringUnits =
        [
            ("DifferentialPressure", "Pa", "0", "1000000"),
            ("FluidTemperature", "K", "233.15", "473.15"),
            ("BearingTemperature", "K", "233.15", "473.15"),
            ("PumpPowerInput", "W", "0", "50000"),
            ("MassFlow", "kg/s", "0", "1"),
            ("PumpEfficiency", "%", "0", "100"),
            ("Level", "m", "0", "10")
        ];

        private static readonly string[] s_endpointPlaceholders =
        [
            "${SOURCE_A_ENDPOINT}",
            "${SOURCE_B_ENDPOINT}"
        ];

        private static readonly string[] s_pumpRootNodeIds = ["ns=1;s=Pump1", "ns=1;s=Pump2"];
        private static readonly string[] s_conditionArgumentNames = ["EventId", "Comment"];
        private static readonly string[] s_conditionArgumentTypes = ["i=15", "i=21"];
        private static readonly string[] s_requiredConditionInputs = ["EventId"];
        private static readonly string[] s_stateMembers = ["Id", "Name"];
        private static readonly string[] s_alarmSuperTypes = ["i=2915"];
        private static readonly string[] s_readOperations = ["readproperty"];
        private static readonly string[] s_observationOperations = ["readproperty", "observeproperty"];
        private static readonly string[] s_invokeOperations = ["invokeaction"];
        private static readonly string[] s_eventOperations = ["subscribeevent", "unsubscribeevent"];
    }
}
