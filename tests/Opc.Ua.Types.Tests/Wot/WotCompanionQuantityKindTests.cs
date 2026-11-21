// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Exercises identity-guarded companion enrichment, provenance and source-conflict preservation.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotCompanionQuantityKindTests
    {
        /// <summary>
        /// Verifies declaration-specific kinds and that enrichment does not introduce NodeSet residue.
        /// </summary>
        [TestCase("Pumps", 13133, 4403510, "LinearVelocity")]
        [TestCase("Pumps", 13135, 4403510, "LinearVelocity")]
        [TestCase("Pumps", 13137, 4403510, "LinearVelocity")]
        [TestCase("Pumps", 13153, 4403510, "LinearVelocity")]
        [TestCase("Pumps", 13155, 4403510, "LinearVelocity")]
        [TestCase("Pumps", 13161, 4403510, "LinearVelocity")]
        [TestCase("Pumps", 13163, 4403510, "LinearVelocity")]
        [TestCase("Pumps", 9347, 4405559, "Frequency")]
        [TestCase("Pumps", 8127, 4405559, "Frequency")]
        [TestCase("Pumps", 7703, 4405559, "Frequency")]
        [TestCase("Pumps", 7706, 4405559, "Frequency")]
        [TestCase("Pumps", 7713, 4405559, "Frequency")]
        [TestCase("Pumps", 7725, 4405559, "Frequency")]
        [TestCase("Pumps", 7753, 4405559, "Frequency")]
        [TestCase("Pumps", 7758, 4405559, "Frequency")]
        [TestCase("Pumps", 13145, 4405559, "Frequency")]
        [TestCase("Pumps", 12719, 4405559, "Frequency")]
        [TestCase("Pumps", 8104, 4404789, "DynamicViscosity")]
        [TestCase("Pumps", 8110, 21300, "KinematicViscosity")]
        [TestCase("Pumps", 7745, 12878, "SoundPowerLevel")]
        [TestCase("Pumps", 12713, 12878, "SoundPowerLevel")]
        [TestCase("Pumps", 7747, 12878, "SoundPressureLevel")]
        [TestCase("Pumps", 12717, 12878, "SoundPressureLevel")]
        [TestCase("Pumps", 7757, 4933459, "MassFlowRate")]
        [TestCase("Pumps", 12673, 4933459, "MassFlowRate")]
        [TestCase("Pumps", 9315, 4600375, "MassFlowRate")]
        [TestCase("Machinery/Energy", 6061, 4933459, "MassFlowRate")]
        [TestCase("PNEM", 6081, 4469812, "ReactivePower")]
        [TestCase("ECM", 6081, 4469812, "ReactivePower")]
        [TestCase("PlasticsRubber/TCD", 6245, 4405556, "Frequency")]
        public async Task VerifiedDeclarationsGainQuantityKindsWithoutChangingSourceAsync(
            string model, int id, int unitId, string kind)
        {
            UANodeSet source = CreateSource(model, id, unitId);

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(source);
            using WotDocument? document = result.Value;

            Assert.That(result.Success, Is.True, WotAnalogTestData.Describe(result.Diagnostics));
            Assert.That(document!.Properties["Measurement"].GetProperty("qudt:hasQuantityKind")
                .GetProperty("@id").GetString(), Is.EqualTo(WotUnitMigration.QuantityKindNamespace + kind));
            WotDiagnostic provenance = result.Diagnostics.Single(
                diagnostic => diagnostic.Code == WotDiagnosticCode.QuantityKindEnriched);
            Assert.That(provenance.Message, Does.Contain("928a9f4b7378dd2f6cd1a3c88690d5c03857a7b4"));
            Assert.That(provenance.Location!.NodeId, Is.EqualTo("ns=1;i=" + id));
            WotConversionResult<WotDocumentSet> verified = await WotNodeSetConverter
                .FromNodeSetDocumentsAsync(source, "model").ConfigureAwait(false);
            using WotDocumentSet? documents = verified.Value;

            Assert.That(verified.Success, Is.True, WotAnalogTestData.Describe(verified.Diagnostics));
        }

        /// <summary>
        /// Leaves ambiguous sound, reciprocal-time and throughput declarations without guessed quantity kinds.
        /// </summary>
        [TestCase(7743, 12878)]
        [TestCase(12709, 12878)]
        [TestCase(7748, 4405559)]
        [TestCase(7751, 4405559)]
        [TestCase(8001, 4665393)]
        [TestCase(7717, 4665393)]
        [TestCase(12721, 4665393)]
        public void AmbiguousDeclarationsKeepOnlyTheirUnit(int id, int unitId)
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(CreateSource("Pumps", id, unitId));

            Assert.That(document.Properties["Measurement"].TryGetProperty("qudt:hasUnit", out _), Is.True);
            Assert.That(document.Properties["Measurement"].TryGetProperty("qudt:hasQuantityKind", out _), Is.False);
        }

        /// <summary>
        /// Reports inconsistent official metadata without correcting its unit or inventing a quantity kind.
        /// </summary>
        [TestCase(12649, 4665393, "PA-M3-PER-SEC")]
        [TestCase(12663, 21300, "M2-PER-SEC")]
        public async Task InconsistentDeclarationsArePreservedAndReportedAsync(int id, int unitId, string unit)
        {
            UANodeSet source = CreateSource("Pumps", id, unitId);

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(source);
            using WotDocument? document = result.Value;

            Assert.That(result.Success, Is.True, WotAnalogTestData.Describe(result.Diagnostics));
            JsonElement measurement = document!.Properties["Measurement"];
            Assert.That(measurement.GetProperty("qudt:hasUnit").GetProperty("@id").GetString(),
                Is.EqualTo("http://qudt.org/vocab/unit/" + unit));
            Assert.That(measurement.GetProperty("unit").GetString(), Is.EqualTo("source-unit"));
            Assert.That(measurement.TryGetProperty("qudt:hasQuantityKind", out _), Is.False);
            WotDiagnostic diagnostic = result.Diagnostics.Single(
                value => value.Code == WotDiagnosticCode.EngineeringUnitSemanticConflict);
            Assert.That(diagnostic.Severity, Is.EqualTo(WotDiagnosticSeverity.Warning));
            Assert.That(diagnostic.Message, Does.Contain("Pumps/Opc.Ua.Pumps.NodeSet2.xml"));
            WotConversionResult<WotDocumentSet> verified = await WotNodeSetConverter
                .FromNodeSetDocumentsAsync(source, "model").ConfigureAwait(false);
            using WotDocumentSet? documents = verified.Value;

            Assert.That(verified.Success, Is.True, WotAnalogTestData.Describe(verified.Diagnostics));
        }

        /// <summary>
        /// Retains source-semantic diagnostics when readable documents require partition preservation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SourceDiagnosticsSurvivePartitionPreservationAsync(bool inconsistent)
        {
            UANodeSet source = inconsistent
                ? CreateSource("Pumps", 12649, 4665393)
                : CreateSource("Pumps", 7745, 12878);
            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Always
            };

            WotConversionResult<WotDocumentSet> result = await WotNodeSetConverter
                .FromNodeSetDocumentsAsync(source, "model", options: options).ConfigureAwait(false);
            using WotDocumentSet? documents = result.Value;

            Assert.That(result.Success, Is.True, WotAnalogTestData.Describe(result.Diagnostics));
            WotDiagnostic diagnostic = result.Diagnostics.Single(value => value.Code == (inconsistent
                ? WotDiagnosticCode.EngineeringUnitSemanticConflict
                : WotDiagnosticCode.QuantityKindEnriched));
            Assert.That(diagnostic.Message, Does.Contain("Pumps/Opc.Ua.Pumps.NodeSet2.xml"));
        }

        /// <summary>
        /// Prevents identity collisions, changed units and foreign authorities from selecting companion semantics.
        /// </summary>
        [TestCase("urn:vendor:units", "Pumps", 7745, 12878)]
        [TestCase(WotAnalogTestData.UnitAuthority, "Other", 7745, 12878)]
        [TestCase(WotAnalogTestData.UnitAuthority, "Pumps", 99999, 12878)]
        [TestCase(WotAnalogTestData.UnitAuthority, "Pumps", 7745, 4405559)]
        public void CompanionMappingsRequireDeclarationAndUnitIdentity(
            string authority, string model, int id, int unitId)
        {
            var provider = new WotCompanionQuantityKindMappingProvider();

            Assert.That(provider.TryGetMapping(
                $"nsu=http://opcfoundation.org/UA/{model}/;i={id}", authority, unitId, out _), Is.False);
        }

        /// <summary>
        /// Uses namespace URIs rather than source namespace indexes to select enrichment.
        /// </summary>
        [Test]
        public void NamespaceTableReorderingDoesNotChangeEnrichment()
        {
            UANodeSet source = CreateSource("Pumps", 7745, 12878);
            source.NamespaceUris = ["urn:unrelated", source.NamespaceUris![0]];
            foreach (UANode node in source.Items!)
            {
                node.NodeId = node.NodeId!.Replace("ns=1;", "ns=2;", StringComparison.Ordinal);
                node.BrowseName = node.BrowseName!.Replace("1:", "2:", StringComparison.Ordinal);
                if (node is UAInstance instance && instance.ParentNodeId is { } parent)
                {
                    instance.ParentNodeId = parent.Replace("ns=1;", "ns=2;", StringComparison.Ordinal);
                }
                foreach (Reference reference in node.References ?? [])
                {
                    reference.Value = reference.Value!.Replace("ns=1;", "ns=2;", StringComparison.Ordinal);
                }
            }

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(document.Properties["Measurement"].GetProperty("qudt:hasQuantityKind")
                .GetProperty("@id").GetString(), Does.EndWith("/SoundPowerLevel"));
        }

        /// <summary>
        /// Preserves an explicitly authored quantity kind instead of replacing it with companion enrichment.
        /// </summary>
        [Test]
        public void AuthoredQuantityKindOverridesCompanionEnrichment()
        {
            using WotDocument initial = WotNodeSetConverter.FromNodeSet(CreateSource("Pumps", 7745, 12878));
            JsonNode root = JsonNode.Parse(initial.RootElement.GetRawText())!;
            root["properties"]!["Measurement"]!["qudt:hasQuantityKind"] =
                new JsonObject { ["@id"] = "urn:test:authored-kind" };
            using var authored = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(authored);
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            Assert.That(restored.Properties["Measurement"].GetProperty("qudt:hasQuantityKind")
                .GetProperty("@id").GetString(), Is.EqualTo("urn:test:authored-kind"));
        }

        /// <summary>
        /// Uses the injected provider consistently during export and verified residue reconstruction.
        /// </summary>
        [Test]
        public async Task InjectedQuantityKindProviderParticipatesInVerifiedReconstructionAsync()
        {
            var options = new WotNodeSetConverterOptions
            {
                QuantityKindMappingProvider = new TestQuantityKindMappingProvider()
            };
            UANodeSet source = CreateSource("Pumps", 7745, 12878);
            using WotDocument document = WotNodeSetConverter.FromNodeSet(source, options: options);
            Assert.That(document.Properties["Measurement"].GetProperty("qudt:hasQuantityKind")
                .GetProperty("@id").GetString(), Is.EqualTo("urn:test:kind"));
            WotConversionResult<WotDocumentSet> verified = await WotNodeSetConverter
                .FromNodeSetDocumentsAsync(source, "model", options: options).ConfigureAwait(false);
            using WotDocumentSet? documents = verified.Value;

            Assert.That(verified.Success, Is.True, WotAnalogTestData.Describe(verified.Diagnostics));
        }

        /// <summary>
        /// Allows a caller to disable contextual enrichment without losing verified engineering-unit mappings.
        /// </summary>
        [Test]
        public void EmptyQuantityKindProviderDisablesCompanionEnrichment()
        {
            var options = new WotNodeSetConverterOptions
            {
                QuantityKindMappingProvider = Mock.Of<IWotQuantityKindMappingProvider>()
            };

            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateSource("Pumps", 7745, 12878), options: options);

            Assert.That(document.Properties["Measurement"].TryGetProperty("qudt:hasUnit", out _), Is.True);
            Assert.That(document.Properties["Measurement"].TryGetProperty("qudt:hasQuantityKind", out _), Is.False);
        }

        /// <summary>
        /// Keeps an explicit unit-provider quantity kind authoritative over declaration defaults.
        /// </summary>
        [Test]
        public void UnitProviderQuantityKindTakesPrecedenceOverCompanionEnrichment()
        {
            string unit = "urn:test:unit";
            string? kind = "urn:test:unit-provider-kind";
            var unitProvider = new Mock<IWotUnitMappingProvider>();
            unitProvider.Setup(provider => provider.TryGetMapping(
                WotAnalogTestData.UnitAuthority, 12878, out unit, out kind)).Returns(true);
            var quantityKindProvider = new Mock<IWotQuantityKindMappingProvider>(MockBehavior.Strict);
            var options = new WotNodeSetConverterOptions
            {
                UnitMappingProvider = unitProvider.Object,
                QuantityKindMappingProvider = quantityKindProvider.Object
            };

            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateSource("Pumps", 7745, 12878), options: options);

            Assert.That(document.Properties["Measurement"].GetProperty("qudt:hasQuantityKind")
                .GetProperty("@id").GetString(), Is.EqualTo(kind));
            quantityKindProvider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Builds a synthetic analog graph with a verified declaration identity and arbitrary display text.
        /// </summary>
        private static UANodeSet CreateSource(string model, int id, int unitId)
        {
            UANodeSet source = WotAnalogTestData.CreateAnalogNodeSet();
            source.NamespaceUris = ["http://opcfoundation.org/UA/" + model + "/"];
            source.Models![0].ModelUri = source.NamespaceUris[0];
            string declarationId = "ns=1;i=" + id;
            foreach (UANode node in source.Items!)
            {
                if (node.NodeId == WotAnalogTestData.MeasurementNodeId)
                {
                    node.NodeId = declarationId;
                }
                if (node is UAInstance instance && instance.ParentNodeId == WotAnalogTestData.MeasurementNodeId)
                {
                    instance.ParentNodeId = declarationId;
                }
                foreach (Reference reference in node.References ?? [])
                {
                    if (reference.Value == WotAnalogTestData.MeasurementNodeId)
                    {
                        reference.Value = declarationId;
                    }
                }
            }
            source.Items.OfType<UAVariable>()
                .Single(variable => variable.NodeId == WotAnalogTestData.EngineeringUnitsNodeId).Value =
                WotAnalogTestData.EngineeringUnitsValue(
                    WotAnalogTestData.UnitAuthority, unitId, "source-unit", null, "source description", null);
            return source;
        }

        /// <summary>
        /// Supplies a process-specific quantity kind with provenance.
        /// </summary>
        private sealed class TestQuantityKindMappingProvider : IWotQuantityKindMappingProvider
        {
            /// <inheritdoc/>
            public bool TryGetMapping(
                string declarationId,
                string unitNamespaceUri,
                int unitId,
                [NotNullWhen(true)] out WotQuantityKindMapping? mapping)
            {
                mapping = new WotQuantityKindMapping("urn:test:evidence", "urn:test:kind");
                return true;
            }
        }
    }
}
