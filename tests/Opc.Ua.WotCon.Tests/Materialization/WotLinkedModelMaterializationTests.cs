// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Tests materialization of resources linked to shared WoT models.
    /// </summary>
    [TestFixture]
    public sealed class WotLinkedModelMaterializationTests
    {
        /// <summary>
        /// Verifies that disjoint documents of one model activate as one source.
        /// </summary>
        [Test]
        public async Task DisjointDocumentsOfOneModelActivateAsOneSource()
        {
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync().ConfigureAwait(false);
            WotRegistryService registry = await runtime.CreateRegistryAsync().ConfigureAwait(false);
            await AddAsync(registry, "first", Partition("First")).ConfigureAwait(false);
            await AddAsync(registry, "second", Partition("Second")).ConfigureAwait(false);
            var host = new FakeWotProjectionHost();
            using var coordinator = new WotMaterializationCoordinator(
                registry, runtime.Observe(host.RecordCommitted), new WotProtocolBinderRegistry([]))
            {
                ServerNamespaceUris = runtime.Namespaces
            };

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
            }).ConfigureAwait(false);

            Assert.That(result.Results.Select(item => item.Outcome), Is.All.EqualTo(WoTOutcomeEnum.Success),
                string.Join("; ", result.Results.Select(item => item.Message)));
            Assert.That(host.AddCount, Is.EqualTo(1));
            WotProjectionDocument projected = host.Operations.Single().Document!;
            Assert.That(projected.Sources, Has.Length.EqualTo(1));
            Assert.That(projected.BindingPlans.Count, Is.EqualTo(2));
            using var xml = new MemoryStream(projected.Sources[0].NodeSetXml, writable: false);
            UANodeSet merged = UANodeSet.Read(xml)!;
            Assert.That(merged.Models, Has.Length.EqualTo(1));
            Assert.That(merged.Models![0].ModelUri, Is.EqualTo(kNamespace));
            Assert.That(merged.Items!.Select(node => node.NodeId), Is.EquivalentTo(s_nodeIds));
        }

        /// <summary>
        /// Verifies that conflicting model partitions never reach the projection host.
        /// </summary>
        /// <param name="conflictingHeader">The conflicting-header flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConflictingModelPartitionsNeverReachTheProjectionHost(bool conflictingHeader)
        {
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync().ConfigureAwait(false);
            WotRegistryService registry = await runtime.CreateRegistryAsync().ConfigureAwait(false);
            await AddAsync(registry, "first", Partition("First")).ConfigureAwait(false);
            UANodeSet second = Partition(conflictingHeader ? "Second" : "First");
            if (conflictingHeader)
            {
                second.Models![0].Version = "2.0.0";
            }
            await AddAsync(registry, "second", second).ConfigureAwait(false);
            var host = new FakeWotProjectionHost();
            using var coordinator = new WotMaterializationCoordinator(
                registry, runtime.Observe(host.RecordCommitted), new WotProtocolBinderRegistry([]))
            {
                ServerNamespaceUris = runtime.Namespaces
            };

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest
            {
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
            }).ConfigureAwait(false);

            Assert.That(result.Results.Any(item => item.Outcome == WoTOutcomeEnum.Failed), Is.True);
            Assert.That(host.AddCount, Is.Zero);
        }

        private static async Task AddAsync(WotRegistryService registry, string resourceId, UANodeSet source)
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source, options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });
            await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingModel,
                Content = ByteString.From(document.Utf8Json.Span)
            }).ConfigureAwait(false);
        }

        private static UANodeSet Partition(string name)
        {
            return new UANodeSet
            {
                NamespaceUris = [kNamespace],
                Models = [new ModelTableEntry { ModelUri = kNamespace, Version = "1.0.0" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;s=" + name,
                        BrowseName = "1:" + name,
                        DisplayName = [new Export.LocalizedText { Value = name }],
                        References =
                        [
                            new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=58" }
                        ]
                    }
                ]
            };
        }

        private const string kNamespace = "urn:linked-model";
        private static readonly string[] s_nodeIds = ["ns=1;s=First", "ns=1;s=Second"];
    }
}
