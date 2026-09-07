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
                string[] dependencies = document.GetProperty("dependsOn")
                    .EnumerateArray().Select(dependency => dependency.GetString()!).ToArray();
                string[] logicalDependencies = LogicalDependencies(
                    json.RootElement, resourceId, resources.Keys).ToArray();
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
            string[] expectedPaths = linkedDirectories.SelectMany(directory =>
                Directory.EnumerateFiles(DocumentPath(directory), "*.json", SearchOption.AllDirectories)
                    .Select(path => path[(DocumentPath(string.Empty).TrimEnd('\\', '/').Length + 1)..]
                        .Replace('\\', '/')))
                .Concat(s_assetProjectionDocuments)
                .ToArray();
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

        [Test]
        public async Task PumpAssetProjectionDocumentsResolveToExpectedGroupsAndMembers()
        {
            var resolver = new WotProjectionResolver(
                new WotAggregationDocumentGenerator.SampleThingResolver(ReadManifestDocuments()),
                WotAggregationDocumentGenerator.CreateLargeDocumentOptions());

            foreach (string pumpName in s_pumpAssetNames)
            {
                using WotDocument asset = WotDocument.Parse(
                    File.ReadAllBytes(DocumentPath($"{pumpName}.Asset.td.json")),
                    WotAggregationDocumentGenerator.CreateLargeDocumentOptions());

                WotConversionResult<WotDocument> assetResult = await resolver
                    .ResolveAsync(asset).ConfigureAwait(false);

                Assert.That(assetResult.Success, Is.True, pumpName);
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
                    s_propertyBindings.Select(binding => binding.Name).ToArray()).ConfigureAwait(false);
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
                ("properties", s_propertyBindings.Select(binding => binding.Name).ToArray()),
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

        [TestCase("Pump1")]
        [TestCase("Pump2")]
        public async Task ManagementProjectionsCompileWithTheirConditionEvents(string pumpName)
        {
            ArrayOf<SampleDocument> documents = ReadManifestDocuments().ToArrayOf(document => document with
            {
                Json = ByteString.From(Encoding.UTF8.GetBytes(
                    SubstituteEndpoints(Encoding.UTF8.GetString(document.Json.ToArray()))))
            });
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
            Assert.That(plan.FullySupported, Is.True, pumpName);
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
                    source);
            }
        }

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
            Assert.That(TypeDefinition(pump), Is.EqualTo("ns=2;i=1052"));
        }

        [Test]
        public void PumpNodeSetRootsThePumpUnderTheObjectsFolder()
        {
            UANodeSet nodeSet = ReadPumpNodeSet();
            UANode[] roots = nodeSet.Items!.Where(node => node.References?.Any(reference =>
                reference.ReferenceType == "i=35" && !reference.IsForward && reference.Value == "i=85")
                == true).ToArray();
            Assert.That(
                roots.Select(node => node.NodeId),
                Is.EquivalentTo(s_pumpRootNodeIds));
            foreach (UANode root in roots)
            {
                Assert.That(root, Is.TypeOf<UAObject>(), root.NodeId);
                Assert.That(
                    root.References!.Count(reference => reference.ReferenceType == "i=35" &&
                        !reference.IsForward && reference.Value == "i=85"),
                    Is.EqualTo(1),
                    root.NodeId);
            }
        }

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
                Assert.That(TypeDefinition(node), Is.EqualTo(typeDefinition), expectedNodeId);
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

            XElement manufacturer = XElement.Parse(
                FindNode<UAVariable>(nodeSet, $"ns=1;s={pumpName}.Identification.Manufacturer").Value!.OuterXml);
            Assert.That(
                manufacturer.Element(XName.Get("Text", global::Opc.Ua.Namespaces.OpcUaXsd))!.Value,
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
            Assert.That(
                pump.References!.Where(reference => reference.IsForward &&
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
                        ResolveAlias(nodeSet, reference.ReferenceType) == "i=46" && reference.Value == nodeId),
                    Is.EqualTo(1),
                    name);
                Assert.That(ResolveAlias(nodeSet, input.DataType), Is.EqualTo("i=296"), name);
                Assert.That(input.ValueRank, Is.EqualTo(1), name);
                Assert.That(input.ArrayDimensions, Is.EqualTo("2"), name);
                Assert.That(TypeDefinition(input), Is.EqualTo("i=68"), name);
                XElement arguments = xml.Root!.Elements().Single(element =>
                    (string?)element.Attribute("NodeId") == nodeId + ".InputArguments");
                XElement[] entries = arguments.Descendants(types + "Argument").ToArray();
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

        [Test]
        public void PumpMappingsUseBothEndpointPlaceholdersAndPortableOpcUaForms()
        {
            ArrayOf<SampleDocument> documents = ReadPumpDocuments();
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
                    XElement value = XElement.Parse(variable.Value!.OuterXml);
                    Assert.That(
                        value.Element(XName.Get("Text", global::Opc.Ua.Namespaces.OpcUaXsd))!.Value,
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
                string[] expectedPaths = SchemaLeafPaths(data).Select(ToEventBrowsePath).ToArray();
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

        [Test]
        public async Task PumpMappingsCompileAfterEndpointSubstitution()
        {
            ArrayOf<SampleDocument> documents = ReadPumpDocuments()
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
                WotCompiledForm[] properties = compiled.Where(form =>
                    form.AffordanceKind == BindingAffordanceKind.Property &&
                    form.TargetMapping.TargetNodeId!.StartsWith(
                        LocalNodeId(pumpName, string.Empty) + ".", StringComparison.Ordinal)).ToArray();
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
            using WotDocument document = WotDocument.Parse(
                File.ReadAllBytes(DocumentPath(fileName)),
                WotAggregationDocumentGenerator.CreateLargeDocumentOptions());
            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document)
                .ConfigureAwait(false);

            Assert.That(result.Success, Is.True, fileName);
            using WotDocument view = result.Value!;
            JsonElement map = view.RootElement.GetProperty(mapName);
            Assert.That(PropertyNames(map), Is.EquivalentTo(expectedMembers), fileName);
            Assert.That(TypeNames(view.RootElement), Does.Not.Contain("uav:projection"), fileName);
            string pumpName = fileName[..fileName.IndexOf('.', StringComparison.Ordinal)];
            ArrayOf<SampleDocument> sources = ReadPumpDocuments();
            foreach (JsonProperty member in map.EnumerateObject())
            {
                string localPath = mapName == "properties"
                    ? s_propertyBindings.Single(binding => binding.Name == member.Name).LocalPath
                    : member.Name;
                string localId = LocalNodeId(pumpName, localPath);
                Affordance declaration = FindAffordance(sources, mapName, localId);
                string reference = fileName == $"{pumpName}.Members.td.json"
                    ? declaration.ResourceId + "#" + declaration.Pointer
                    : $"{pumpName.ToLowerInvariant()}-members#/{mapName}/{EscapePointer(member.Name)}";
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
                    if (declaration.Value.TryGetProperty("const", out JsonElement constant))
                    {
                        Assert.That(member.Value.GetProperty("const").GetRawText(),
                            Is.EqualTo(constant.GetRawText()), member.Name);
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
            if (!found && root.TryGetProperty("uav:browseName", out JsonElement browseName) &&
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
                        ? context.EnumerateArray().ToArray()
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

        private static Affordance FindAffordance(
            ArrayOf<SampleDocument> documents,
            string mapName,
            string nodeId)
        {
            Affordance[] matches = ReadAffordances(documents, mapName).Where(affordance =>
                affordance.Value.TryGetProperty("uav:id", out JsonElement id) &&
                id.GetString() == nodeId).ToArray();
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
            UANodeSet nodes = WotNodeSetConverter.ToNodeSet(
                document, WotAggregationDocumentGenerator.CreateLargeDocumentOptions());
            var namespaces = new NamespaceTable();
            foreach (string uri in nodes.NamespaceUris ?? [])
            {
                namespaces.Append(uri);
            }
            NodeId id = ExpandedNodeId.ToNodeId(
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
            return document.Root!.Elements().Where(element =>
                (string?)element.Attribute("NodeId") == prefix ||
                ((string?)element.Attribute("NodeId"))?.StartsWith(prefix + ".", StringComparison.Ordinal) == true)
                .ToArray();
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
            return array.EnumerateArray().Select(value => value.GetString()!).ToArray();
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
            ("ns=1;s=Pump1", "Pump_1", "ns=2;i=1052"),
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
