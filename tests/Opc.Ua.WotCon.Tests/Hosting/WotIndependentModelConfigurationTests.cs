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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Tests;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;

namespace Opc.Ua.WotCon.Tests.Hosting
{
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable]
    public sealed class WotIndependentModelConfigurationTests
    {
        [Test]
        public void DefaultRegistryOptionsRetainPartitionReconstruction()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddWotRegistryServer();
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetRequiredService<WotRegistryServerOptions>().DocumentSetMode,
                Is.EqualTo(WotDocumentSetMode.PartitionReconstruction));
            Assert.That(provider.GetRequiredService<WotNodeSetConverterOptions>().DocumentSetMode,
                Is.EqualTo(WotDocumentSetMode.PartitionReconstruction));
        }

        [TestCase("fluent")]
        [TestCase("configuration")]
        [TestCase("section")]
        public async Task ConfiguredIndependentModeReachesTheRegistryMergeAsync(string route)
        {
            var services = new ServiceCollection();
            var host = new FakeWotProjectionHost();
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            services.AddSingleton<IServiceMessageContext>(context);
            services.AddSingleton<IWotProjectionHost>(host);
            services.AddSingleton<IWotViewProjectionHost>(new InMemoryWotViewProjectionHost());
            IOpcUaBuilder builder = services.AddOpcUa();
            if (route == "fluent")
            {
                builder.AddWotRegistryServer(options =>
                    options.DocumentSetMode = WotDocumentSetMode.IndependentReadableModels);
            }
            else
            {
                IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [OpcUaWotRegistryServerBuilderExtensions.DefaultConfigurationSection + ":DocumentSetMode"] =
                            "IndependentReadableModels"
                    }).Build();
                if (route == "configuration")
                {
                    builder.AddWotRegistryServer(configuration);
                }
                else
                {
                    builder.AddWotRegistryServer(configuration.GetSection(
                        OpcUaWotRegistryServerBuilderExtensions.DefaultConfigurationSection));
                }
            }
            using ServiceProvider provider = services.BuildServiceProvider();
            WotNodeSetConverterOptions converterOptions = provider.GetRequiredService<WotNodeSetConverterOptions>();
            Assert.That(converterOptions.DocumentSetMode, Is.EqualTo(WotDocumentSetMode.IndependentReadableModels));
            Assert.That(converterOptions.ValueEncodingContext, Is.SameAs(context));
            IWotRegistryService registry = provider.GetRequiredService<IWotRegistryService>();
            await AddModelAsync(registry, "first", 1).ConfigureAwait(false);
            await AddModelAsync(registry, "second", 2).ConfigureAwait(false);

            WotRefreshResult result = await provider.GetRequiredService<WotMaterializationCoordinator>()
                .RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(result.Results.Select(item => item.Outcome), Is.All.EqualTo(WoTOutcomeEnum.Success),
                string.Join("; ", result.Results.Select(item => item.Message)));
            Assert.That(host.AddCount, Is.EqualTo(1));
            WotProjectionDocument projected = host.Operations.Single().Document!;
            Assert.That(projected.Sources, Has.Length.EqualTo(1));
            using var xml = new MemoryStream(projected.Sources[0].NodeSetXml, writable: false);
            UANodeSet nodes = UANodeSet.Read(xml)!;
            Assert.That(nodes.NamespaceUris, Is.EqualTo(s_mergedNamespaces));
            Assert.That(nodes.Items!.Select(node => node.NodeId), Is.EquivalentTo(s_mergedNodeIds));
        }

        [Test]
        public void UndefinedRegistryModeFailsDuringComposition()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddWotRegistryServer(options => options.DocumentSetMode = (WotDocumentSetMode)99);
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(() => provider.GetRequiredService<WotNodeSetConverterOptions>(),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task ChangingTheModeInvalidatesTheRegistryInputIdentityAsync()
        {
            using var registry = new WotRegistryService();
            await AddModelAsync(registry, "first", 1).ConfigureAwait(false);
            var host = new FakeWotProjectionHost();
            var options = new WotNodeSetConverterOptions();
            using var coordinator = new WotMaterializationCoordinator(registry, host, converterOptions: options);
            WotRefreshResult initial = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            Assert.That(initial.Results.Single().Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            WotRefreshResult unchanged = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            Assert.That(unchanged.Results.Single().Outcome, Is.EqualTo(WoTOutcomeEnum.Unchanged));

            options.DocumentSetMode = WotDocumentSetMode.IndependentReadableModels;
            WotRefreshResult independent =
                await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(independent.Results.Single().Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(host.ShadowCount, Is.EqualTo(1));
            using (var xml = new MemoryStream(host.Operations[^1].Document!.Sources[0].NodeSetXml, writable: false))
            {
                UANodeSet nodes = UANodeSet.Read(xml)!;
                Assert.That(nodes.NamespaceUris, Is.EqualTo(s_singleNamespaces));
                Assert.That(nodes.Items![0].NodeId, Is.EqualTo("ns=2;i=1"));
            }
            WotRefreshResult stable = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            Assert.That(stable.Results.Single().Outcome, Is.EqualTo(WoTOutcomeEnum.Unchanged));
            options.DocumentSetMode = WotDocumentSetMode.PartitionReconstruction;
            WotRefreshResult reconstructed =
                await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            Assert.That(reconstructed.Results.Single().Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(host.ShadowCount, Is.EqualTo(2));
        }

        [Test]
        public async Task IndependentReviewRegistryFailureDoesNotPublishAModeFingerprintAsync()
        {
            var services = new ServiceCollection();
            var host = new FakeWotProjectionHost();
            services.AddSingleton<IWotProjectionHost>(host);
            services.AddSingleton<IWotViewProjectionHost>(new InMemoryWotViewProjectionHost());
            services.AddOpcUa().AddWotRegistryServer(options =>
                options.DocumentSetMode = WotDocumentSetMode.IndependentReadableModels);
            using ServiceProvider provider = services.BuildServiceProvider();
            IWotRegistryService registry = provider.GetRequiredService<IWotRegistryService>();
            await AddModelAsync(registry, "first", 1).ConfigureAwait(false);
            await AddModelAsync(registry, "second", 2).ConfigureAwait(false);
            WotMaterializationCoordinator coordinator = provider.GetRequiredService<WotMaterializationCoordinator>();
            WotRefreshResult initial = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            Assert.That(initial.Results, Has.Length.EqualTo(2));
            Assert.That(initial.Results.Select(result => result.Outcome), Is.All.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(host.AddCount, Is.EqualTo(1));
            WotProjectionDocument active = host.Operations.Single().Document!;
            Assert.That(active.Sources, Has.Length.EqualTo(1));
            using var originalSource = new MemoryStream(active.Sources[0].NodeSetXml, writable: false);
            byte[] sourceBytes = originalSource.ToArray();
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable(["urn:test:local"]),
                EncodeableFactory = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()).Factory
            };
            var nodes = new NodeStateCollection();
            UANodeSet.Read(originalSource)!.Import(context, nodes);
            BaseObjectTypeState[] roots = nodes.OfType<BaseObjectTypeState>().ToArray();
            Assert.That(roots, Has.Length.EqualTo(2));
            Assert.That(roots.Select(node => context.NamespaceUris.GetString(node.NodeId.NamespaceIndex)),
                Is.All.EqualTo("urn:test:shared"));
            BaseObjectTypeState first = roots.Single(node => node.BrowseName.Name == "first");
            BaseObjectTypeState second = roots.Single(node => node.BrowseName.Name == "second");
            Assert.That(second.SuperTypeId, Is.EqualTo(first.NodeId));

            WotRefreshResult stable = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            Assert.That(stable.Results, Has.Length.EqualTo(2));
            Assert.That(stable.Results.Select(result => result.Outcome), Is.All.EqualTo(WoTOutcomeEnum.Unchanged));
            WotNodeSetConverterOptions options = provider.GetRequiredService<WotNodeSetConverterOptions>();
            options.DocumentSetMode = WotDocumentSetMode.PartitionReconstruction;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                WotRefreshResult failed = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
                Assert.That(failed.Results, Has.Length.EqualTo(2));
                Assert.That(failed.Results.Select(result => result.Outcome), Is.All.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(failed.Results.Any(result =>
                    result.Message?.Contains("namespace table", StringComparison.Ordinal) == true), Is.True);
                Assert.That(host.AddCount, Is.EqualTo(1));
                Assert.That(host.ShadowCount, Is.Zero);
                Assert.That(host.Operations, Has.Count.EqualTo(1));
                using var retained = new MemoryStream(host.Operations.Single().Document!.Sources[0].NodeSetXml,
                    writable: false);
                Assert.That(retained.ToArray(), Is.EqualTo(sourceBytes));
            }
        }

        [Test]
        public void DirectConsumersRejectUndefinedModes()
        {
            var options = new WotNodeSetConverterOptions { DocumentSetMode = (WotDocumentSetMode)99 };
            using var registry = new WotRegistryService();

            Assert.That(() => new WotNodeSetDocumentConverter(options), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() =>
            {
                using var coordinator = new WotMaterializationCoordinator(
                    registry, new FakeWotProjectionHost(), converterOptions: options);
            }, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static async Task AddModelAsync(IWotRegistryService registry, string name, uint identifier)
        {
            var root = new JsonObject
            {
                ["@context"] = new JsonObject
                {
                    ["uav"] = WotNodeSetConverter.VocabularyNamespace,
                    ["ns1"] = "urn:test:shared",
                    ["ns2"] = "urn:test:extra:" + name
                },
                ["@type"] = "tm:ThingModel",
                ["id"] = "urn:test:document:" + name,
                ["title"] = name,
                ["uav:id"] = "nsu=urn:test:shared;i=" + identifier.ToString(CultureInfo.InvariantCulture),
                ["uav:browseName"] = "ns1:" + name
            };
            if (identifier == 2)
            {
                root["links"] = new JsonArray(new JsonObject
                {
                    ["rel"] = "tm:extends",
                    ["href"] = "urn:test:document:first"
                });
            }
            WotRegistryMutationResult result = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = name,
                Kind = WoTDocumentKindEnum.ThingModel,
                Content = ByteString.From(Encoding.UTF8.GetBytes(root.ToJsonString()))
            }).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), result.Message);
        }

        private static readonly string[] s_mergedNamespaces =
            ["urn:test:extra:first", "urn:test:extra:second", "urn:test:shared"];
        private static readonly string[] s_mergedNodeIds = ["ns=3;i=1", "ns=3;i=2"];
        private static readonly string[] s_singleNamespaces = ["urn:test:extra:first", "urn:test:shared"];
    }
}
