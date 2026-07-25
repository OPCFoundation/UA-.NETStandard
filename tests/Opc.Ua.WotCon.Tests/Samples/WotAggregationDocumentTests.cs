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
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Planners;

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
        public void CheckedInJsonDocumentsUseCanonicalSerialization()
        {
            IEnumerable<string> paths = s_modelDocuments
                .Select(document => DocumentPath(document.FileName))
                .Append(DocumentPath("SamplePump.td.json"))
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
                string target = affordance.GetProperty("uav:mapToNodeId").GetString()!;
                JsonElement form = affordance.GetProperty("forms")[0];
                string href = form.GetProperty("href").GetString()!;
                string sourceNodeId = form.GetProperty("uav:id").GetString()!;

                placeholders.Add(href);
                Assert.That(
                    target,
                    Does.StartWith($"nsu={WotAggregationDocumentGenerator.PumpInstanceNamespace};s=Pump1."));
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
            Assert.That(plan.CompiledForms.All(form => !form.TargetMapping.IsEmpty), Is.True);
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

        private static string DocumentPath(string fileName)
        {
            return RepositoryPath("samples", "WotAggregation", "Documents", fileName);
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

        private static readonly (string NodeId, string BrowseName, string TypeDefinition)[] s_pumpNodes =
        [
            ("ns=1;s=Pump1", "Pump #1", "ns=2;i=1052"),
            ("ns=1;s=Pump1.Identification", "Identification", "ns=4;i=1005"),
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
            "ThingDescription"
        ];

        private static readonly string[] s_endpointPlaceholders =
        [
            "${SOURCE_A_ENDPOINT}",
            "${SOURCE_B_ENDPOINT}"
        ];
    }
}
