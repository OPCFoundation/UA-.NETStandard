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
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Planners;
using BindingAffordanceKind = Opc.Ua.WotCon.Bindings.WotAffordanceKind;

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

        [Test]
        public void ThingModelsMatchCanonicalConverterRegeneration()
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
                    $"{document.FileName} is not the canonical converter output.");
            }
        }

        [Test]
        public void PumpThingDescriptionMatchesCanonicalRegeneration()
        {
            byte[] regenerated = WotAggregationDocumentGenerator.GeneratePumpThingDescription(
                DocumentPath("SamplePump.NodeSet2.xml"));
            byte[] checkedIn = File.ReadAllBytes(DocumentPath("SamplePump.td.json"));

            Assert.That(regenerated, Is.EqualTo(checkedIn));
        }

        [Test]
        public void PumpAssetProjectionDocumentsMatchCanonicalRegeneration()
        {
            foreach (string fileName in s_assetProjectionDocuments)
            {
                byte[] regenerated =
                    WotAggregationDocumentGenerator.GeneratePumpAssetProjectionDocument(fileName);
                byte[] checkedIn = File.ReadAllBytes(DocumentPath(fileName));

                Assert.That(
                    regenerated,
                    Is.EqualTo(checkedIn),
                    $"{fileName} is not the canonical projection document.");
            }
        }

        [Test]
        public void CheckedInJsonDocumentsUseCanonicalSerialization()
        {
            IEnumerable<string> paths = s_modelDocuments
                .Select(document => DocumentPath(document.FileName))
                .Append(DocumentPath("SamplePump.td.json"))
                .Concat(s_assetProjectionDocuments.Select(DocumentPath))
                .Append(DocumentPath("documents.json"))
                .Append(StructuredExamplePath);

            foreach (string path in paths)
            {
                byte[] bytes = File.ReadAllBytes(path);
                using var document = WotDocument.Parse(
                    bytes,
                    WotAggregationDocumentGenerator.CreateLargeDocumentOptions());
                Assert.That(
                    document.ToCanonicalUtf8(),
                    Is.EqualTo(bytes),
                    $"{path} is not canonical JSON.");
            }
        }

        [Test]
        public void ManifestOrdersDependenciesBeforeDependents()
        {
            using var manifest = JsonDocument.Parse(
                File.ReadAllBytes(DocumentPath("documents.json")));
            JsonElement.ArrayEnumerator documents = manifest.RootElement.EnumerateArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new List<string>();

            foreach (JsonElement document in documents)
            {
                string resourceId = document.GetProperty("resourceId").GetString()!;
                foreach (JsonElement dependency in document.GetProperty("dependsOn").EnumerateArray())
                {
                    Assert.That(seen, Does.Contain(dependency.GetString()));
                }
                seen.Add(resourceId);
                kinds.Add(document.GetProperty("documentKind").GetString()!);
            }

            Assert.That(
                kinds,
                Is.EqualTo(s_expectedDocumentKinds));
        }

        [Test]
        public async Task PumpAssetProjectionDocumentsResolveToExpectedGroupsAndMembers()
        {
            var resolver = new WotProjectionResolver(
                new FileThingResolver(DocumentPath),
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

                await AssertProjectionMembersAsync(
                    resolver,
                    $"{pumpName}.ProcessData.td.json",
                    "properties",
                    s_processDataMembers).ConfigureAwait(false);
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
            }
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
                NodeSetRoundtripReport report = NodeSetComparer.Roundtrip(
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

        [Test]
        public void PumpNodeSetHasRequiredModelDependenciesAndTypeDefinition()
        {
            UANodeSet nodeSet = ReadPumpNodeSet();
            ModelTableEntry model = nodeSet.Models!.Single();
            string[] requiredModels = [.. model.RequiredModel!.Select(required => required.ModelUri!)];
            UAObject pump = FindNode<UAObject>(nodeSet, "ns=1;s=Pump1");

            Assert.That(model.ModelUri, Is.EqualTo(WotAggregationDocumentGenerator.PumpInstanceNamespace));
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
            UANode pump = FindNode<UANode>(nodeSet, "ns=1;s=Pump1");

            // Without a hierarchical parent the pump imports as an orphan: it
            // exists in the AddressSpace but nothing browses to it, so a Client
            // starting from the root never finds it.
            // Written as the NodeId rather than the "Organizes" alias: this
            // NodeSet round-trips through a Thing Description, whose residue
            // preserves a reference type verbatim, and the converter emits no
            // <Aliases> block - so a bare name would not survive the trip.
            Assert.That(
                pump.References?.Any(r =>
                    string.Equals(r.ReferenceType, "i=35", StringComparison.Ordinal) &&
                    !r.IsForward &&
                    string.Equals(r.Value, "i=85", StringComparison.Ordinal)),
                Is.True,
                "Pump1 must declare an inverse Organizes from the Objects folder.");
        }

        [Test]
        public void PumpNodeSetHasStableRequiredHierarchy()
        {
            UANodeSet nodeSet = ReadPumpNodeSet();

            foreach ((string nodeId, string browseName, string typeDefinition) in s_pumpNodes)
            {
                UANode node = FindNode<UANode>(nodeSet, nodeId);
                Assert.That(LocalName(node.BrowseName), Is.EqualTo(browseName), nodeId);
                Assert.That(TypeDefinition(node), Is.EqualTo(typeDefinition), nodeId);
            }

            AssertChildPath(
                nodeSet,
                "ns=1;s=Pump1",
                "Operational",
                "Measurements",
                "DifferentialPressure");
            AssertChildPath(nodeSet, "ns=1;s=Pump1", "Operational", "Measurements", "FluidTemperature");
            AssertChildPath(nodeSet, "ns=1;s=Pump1", "Operational", "Measurements", "BearingTemperature");
            AssertChildPath(nodeSet, "ns=1;s=Pump1", "Operational", "Measurements", "PumpPowerInput");
            AssertChildPath(nodeSet, "ns=1;s=Pump1", "Operational", "Measurements", "MassFlow");
            AssertChildPath(nodeSet, "ns=1;s=Pump1", "Operational", "Measurements", "PumpEfficiency");
            AssertChildPath(nodeSet, "ns=1;s=Pump1", "Operational", "Measurements", "Level");
            AssertChildPath(nodeSet, "ns=1;s=Pump1", "Operational", "Measurements", "NumberOfStarts");
            AssertChildPath(
                nodeSet,
                "ns=1;s=Pump1",
                "Events",
                "SupervisionProcessFluid",
                "Cavitation");
            AssertChildPath(
                nodeSet,
                "ns=1;s=Pump1",
                "Events",
                "SupervisionPumpOperation",
                "MotorOverheat");
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

        [Test]
        public void PumpAnalogMeasurementsCarryEngineeringUnitMetadata()
        {
            var document = XDocument.Load(DocumentPath("SamplePump.NodeSet2.xml"));
            XNamespace nodeSetNamespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
            XNamespace typesNamespace = "http://opcfoundation.org/UA/2008/02/Types.xsd";

            foreach ((string name, string unit) in s_engineeringUnits)
            {
                string nodeId = $"ns=1;s=Pump1.Operational.Measurements.{name}";
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
            }
        }

        [Test]
        public void PumpMappingsUseBothEndpointPlaceholdersAndPortableOpcUaForms()
        {
            byte[] bytes = File.ReadAllBytes(DocumentPath("SamplePump.td.json"));
            string text = Encoding.UTF8.GetString(bytes);
            using var document = JsonDocument.Parse(bytes);
            JsonElement properties = document.RootElement.GetProperty("properties");
            var placeholders = new HashSet<string>(StringComparer.Ordinal);

            foreach (JsonProperty property in properties.EnumerateObject())
            {
                JsonElement affordance = property.Value;
                JsonElement form = affordance.GetProperty("forms")[0];
                string href = form.GetProperty("href").GetString()!;
                string sourceNodeId = form.GetProperty("uav:id").GetString()!;

                placeholders.Add(href);
                if (affordance.TryGetProperty("uav:mapToNodeId", out JsonElement target))
                {
                    Assert.That(
                        target.GetString(),
                        Does.StartWith($"nsu={WotAggregationDocumentGenerator.PumpInstanceNamespace};s=Pump1."));
                }
                Assert.That(sourceNodeId, Does.StartWith("nsu=urn:opcfoundation.org:UA:WotAggregation:Source"));
            }

            Assert.That(
                placeholders,
                Is.EquivalentTo(s_endpointPlaceholders));
            Assert.That(text.Replace("${SOURCE_A_ENDPOINT}", string.Empty, StringComparison.Ordinal)
                .Replace("${SOURCE_B_ENDPOINT}", string.Empty, StringComparison.Ordinal), Does.Not.Contain("${"));
        }

        [Test]
        public void PumpMappingsCompileAfterEndpointSubstitution()
        {
            string document = File.ReadAllText(DocumentPath("SamplePump.td.json"))
                .Replace("${SOURCE_A_ENDPOINT}", "opc.tcp://source-a:4840", StringComparison.Ordinal)
                .Replace("${SOURCE_B_ENDPOINT}", "opc.tcp://source-b:4840", StringComparison.Ordinal);
            var registry = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "sample-pump",
                WoTDocumentKindEnum.ThingDescription,
                Encoding.UTF8.GetBytes(document)));

            Assert.That(plan.FullySupported, Is.True);
            Assert.That(plan.CompiledForms, Is.Not.Empty);
            Assert.That(
                plan.CompiledForms
                    .Where(form => form.AffordanceKind == BindingAffordanceKind.Property)
                    .Where(form => !form.AffordanceName.StartsWith("Pump2", StringComparison.Ordinal))
                    .All(form => !form.TargetMapping.IsEmpty),
                Is.True);
            Assert.That(
                plan.CompiledForms
                    .Where(form => form.AffordanceKind != BindingAffordanceKind.Property)
                    .All(form => form.TargetMapping.IsEmpty),
                Is.True);
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
            Assert.That(PropertyNames(map), Is.EqualTo(expectedMembers), fileName);
            foreach (JsonProperty member in map.EnumerateObject())
            {
                Assert.That(
                    member.Value.GetProperty("uav:resolvedFrom").GetString(),
                    Does.StartWith(
                        $"{fileName[..fileName.IndexOf('.', StringComparison.Ordinal)].ToLowerInvariant()}-members#"),
                    member.Name);
            }
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
                        string.Equals(reference.ReferenceType, "HasComponent", StringComparison.Ordinal))
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
            return RepositoryPath("samples", "WotCon", "AggregationClient", "Documents", fileName);
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

        private sealed class FileThingResolver(Func<string, string> documentPath) : IWotThingResolver
        {
            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                string fileName = reference.StartsWith("./", StringComparison.Ordinal)
                    ? reference[2..]
                    : reference;
                fileName = fileName switch
                {
                    "sample-pump" => "SamplePump.td.json",
                    "pump1-members" => "Pump1.Members.td.json",
                    "pump1-processdata" => "Pump1.ProcessData.td.json",
                    "pump1-conditiondata" => "Pump1.ConditionData.td.json",
                    "pump1-supervision" => "Pump1.Supervision.td.json",
                    "pump1-management" => "Pump1.Management.td.json",
                    "pump2-members" => "Pump2.Members.td.json",
                    "pump2-processdata" => "Pump2.ProcessData.td.json",
                    "pump2-conditiondata" => "Pump2.ConditionData.td.json",
                    "pump2-supervision" => "Pump2.Supervision.td.json",
                    "pump2-management" => "Pump2.Management.td.json",
                    _ => fileName
                };
                string path = documentPath(fileName);
                if (!File.Exists(path))
                {
                    return new ValueTask<WotResolverResult>(WotResolverResult.NotFound);
                }

                return new ValueTask<WotResolverResult>(
                    WotResolverResult.FromBytes(File.ReadAllBytes(path)));
            }
        }

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
                    "PumpDeviceIntegrationServer",
                    "Model",
                    "Opc.Ua.Machinery.NodeSet2.xml"),
                "OPC UA Machinery",
                MachineryNamespace),
            new(
                "Opc.Ua.Pumps.tm.json",
                Path.Combine(
                    "samples",
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
            "reset",
            "start",
            "stop"
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

        private static readonly (string Name, string Unit)[] s_engineeringUnits =
        [
            ("DifferentialPressure", "Pa"),
            ("FluidTemperature", "K"),
            ("BearingTemperature", "K"),
            ("PumpPowerInput", "W"),
            ("MassFlow", "kg/s"),
            ("PumpEfficiency", "%"),
            ("Level", "m")
        ];

        private static readonly string[] s_expectedDocumentKinds =
        [
            "ThingModel",
            "ThingModel",
            "ThingModel",
            "ThingDescription",
            "ThingDescription",
            "ThingDescription",
            "ThingDescription",
            "ThingDescription",
            "ThingDescription",
            "ThingDescription",
            "ThingDescription",
            "ThingDescription",
            "ThingDescription",
            "ThingDescription",
            "ThingDescription",
            "ThingDescription"
        ];

        private static readonly string[] s_endpointPlaceholders =
        [
            "${SOURCE_A_ENDPOINT}",
            "${SOURCE_B_ENDPOINT}"
        ];
    }
}
