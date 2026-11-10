/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.AMB;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Configuration;
using Opc.Ua.AMB.Server.Health;
using Opc.Ua.Client;
using Opc.Ua.Di;
using Opc.Ua.LADS.Server;
using Opc.Ua.LADS.Tests.Integration;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;
using LadsProgramTemplate = Opc.Ua.LADS.Server.LadsProgramTemplate;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests opted-in AMB composition and legacy LADS identity compatibility.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class LadsAssetManagementTests
    {
        /// <summary>
        /// A legacy device keeps its fallback key and restores its existing snapshot without AMB registration.
        /// </summary>
        [Test]
        public async Task LegacyDeviceRetainsStoredDataIdentityAsync()
        {
            const string key = "urn:opcua-netstandard:lads:instances#Legacy";
            var store = new InMemoryLadsStore();
            await using (LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(LegacyDeviceRetainsStoredDataIdentityAsync),
                builder => builder.AddLads(options => options.Store = store).ConfigureLads(context =>
                    context.AddDevice("Legacy", device => device.FunctionalUnit(
                        "Unit", unit => unit.Programs(_ => { })))),
                "Legacy").ConfigureAwait(false))
            {
                var device = (LadsDevice)await server.DeviceAsync("Legacy").ConfigureAwait(false);
                Assert.That(device.StoreIdentity, Is.EqualTo(key));
                Assert.That(device.Node.Identification?.ProductInstanceUri?.Value, Is.Null.Or.Empty);
                Assert.That(device.Asset, Is.Null);
                Assert.That(server.Provider.GetRequiredService<IAssetManagement>().Assets.Count, Is.Zero);
                await device.GetFunctionalUnit("Unit").Programs!.AddTemplateAsync(
                    new LadsProgramTemplate("Retained").Step("Stored", TimeSpan.FromSeconds(7)))
                    .ConfigureAwait(false);
                LadsStoredDeviceData? saved = await store.LoadAsync(key).ConfigureAwait(false);
                Assert.That(saved, Is.Not.Null);
                Assert.That(saved!.FunctionalUnits[0].Templates[0].Id, Is.EqualTo("Retained"));
            }

            await using LadsHostedServer restarted = await LadsHostedServer.StartAsync(
                nameof(LegacyDeviceRetainsStoredDataIdentityAsync),
                builder => builder.AddLads(options => options.Store = store).ConfigureLads(context =>
                    context.AddDevice("Legacy", device => device.FunctionalUnit(
                        "Unit", unit => unit.Programs(_ => { })))),
                "Legacy").ConfigureAwait(false);
            var restored = (LadsDevice)await restarted.DeviceAsync("Legacy").ConfigureAwait(false);
            ILadsProgramManager programs = restored.GetFunctionalUnit("Unit").Programs!;
            Assert.That(restored.StoreIdentity, Is.EqualTo(key));
            Assert.That(restored.Asset, Is.Null);
            Assert.That(programs.Templates.Count, Is.EqualTo(1));
            Assert.That(programs.Templates[0].Id, Is.EqualTo("Retained"));
            Assert.That(programs.Templates[0].Steps[0].Duration, Is.EqualTo(TimeSpan.FromSeconds(7)));
            Assert.That(programs.ActiveRun, Is.Null);
            Assert.That(restarted.Provider.GetRequiredService<IAssetManagement>().Assets.Count, Is.Zero);
        }

        /// <summary>
        /// Only opted-in devices need a valid identity; rejected builds do not touch storage or the registry.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("relative/device")]
        public async Task AssetConfigurationRequiresIdentityAndRegistryAsync(string? uri)
        {
            var store = new InMemoryLadsStore();
            var assets = new Mock<IAssetManagement>(MockBehavior.Strict);
            ILadsSetupContext? setup = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(AssetConfigurationRequiresIdentityAndRegistryAsync),
                builder => builder.AddLads(options => options.Store = store).ConfigureLads(context =>
                {
                    setup = context;
                    context.AddDevice("Seed", _ => { });
                }),
                "Seed").ConfigureAwait(false);

            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadConfigurationError, () =>
                setup!.AddDeviceAsync("Candidate", device => device
                    .AssetManagement(assets.Object, asset => asset.WithDeviceHealth())
                    .Identification(id => id.ProductInstanceUri = uri))).ConfigureAwait(false);
            Assert.That(await store.LoadAsync("urn:opcua-netstandard:lads:instances#Candidate").ConfigureAwait(false),
                Is.Null);
            Assert.That(server.Registry.TryGetDevice("Candidate", out _), Is.False);
            assets.VerifyNoOtherCalls();

            ILadsDevice retry = await setup!.AddDeviceAsync("Candidate", _ => { }).ConfigureAwait(false);
            Assert.That(retry.Name, Is.EqualTo("Candidate"));
            Assert.That(retry.Asset, Is.Null);
        }

        /// <summary>
        /// A direct context without IAssetManagement rejects the opt-in rather than constructing a new registry.
        /// </summary>
        [Test]
        public async Task MissingAssetManagementServiceRejectsOnlyOptedInDeviceAsync()
        {
            LadsSetupContext? setup = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(MissingAssetManagementServiceRejectsOnlyOptedInDeviceAsync),
                builder => builder.AddLads().ConfigureLads(context =>
                {
                    setup = (LadsSetupContext)context;
                    context.AddDevice("Seed", _ => { });
                }),
                "Seed").ConfigureAwait(false);
            var buildContext = new Mock<IMachineryBuildContext>(MockBehavior.Strict);
            buildContext.Setup(context => context.GetService<IAssetManagement>()).Returns((IAssetManagement?)null);
            var direct = new LadsSetupContext(setup!.Runtime, buildContext.Object);

            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadConfigurationError, () =>
                direct.AddDeviceAsync("Candidate", device => device
                    .Identification(id => id.ProductInstanceUri = kProductInstanceUri)
                    .AssetManagement(asset => asset.WithDeviceHealth()))).ConfigureAwait(false);
            ILadsDevice legacy = await direct.AddDeviceAsync("Candidate", _ => { }).ConfigureAwait(false);
            Assert.That(legacy.Name, Is.EqualTo("Candidate"));
            Assert.That(legacy.Asset, Is.Null);
            Assert.That(server.Provider.GetRequiredService<IAssetManagement>().Assets.Count, Is.Zero);
            buildContext.Verify(context => context.GetService<IAssetManagement>(), Times.Once);
            buildContext.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Invalid or repeated selection is reported before registration and leaves the name reusable.
        /// </summary>
        [TestCase("callback")]
        [TestCase("explicit-callback")]
        [TestCase("service")]
        [TestCase("duplicate")]
        public async Task AssetManagementBuilderRejectsInvalidConfigurationAsync(string invalid)
        {
            ILadsSetupContext? setup = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(AssetManagementBuilderRejectsInvalidConfigurationAsync),
                builder => builder.AddLads().ConfigureLads(context =>
                {
                    setup = context;
                    context.AddDevice("Seed", _ => { });
                }),
                "Seed").ConfigureAwait(false);
            IAssetManagement assets = server.Provider.GetRequiredService<IAssetManagement>();
            Exception? failure = await Assert.CatchAsync<Exception>(async () =>
                await setup!.AddDeviceAsync("Candidate", device =>
                {
                    switch (invalid)
                    {
                        case "callback":
                            device.AssetManagement((Action<IAssetBuilder>)null!);
                            break;
                        case "explicit-callback":
                            device.AssetManagement(assets, null!);
                            break;
                        case "service":
                            device.AssetManagement(null!, _ => { });
                            break;
                        default:
                            device.AssetManagement(assets, _ => { }).AssetManagement(assets, _ => { });
                            break;
                    }
                }).ConfigureAwait(false));
            if (invalid == "duplicate")
            {
                Assert.That(failure, Is.InstanceOf<ServiceResultException>());
                Assert.That(((ServiceResultException)failure!).StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            }
            else
            {
                Assert.That(failure, Is.InstanceOf<ArgumentNullException>());
                Assert.That(((ArgumentNullException)failure!).ParamName,
                    Is.EqualTo(invalid == "service" ? "assets" : "configure"));
            }
            Assert.That(assets.Assets.Count, Is.Zero);
            ILadsDevice retry = await setup!.AddDeviceAsync("Candidate", _ => { }).ConfigureAwait(false);
            Assert.That(retry.Name, Is.EqualTo("Candidate"));
        }

        /// <summary>
        /// Both configuration paths reuse the same object, registry, persistent wire writes and health alarms.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LadsAssetManagementUsesSharedRegistryAsync(bool explicitService)
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                nameof(LadsAssetManagementUsesSharedRegistryAsync), Guid.NewGuid().ToString("N"));
            using (var store = new FileSystemAssetConfigurationStore(directory))
            {
                await using LadsClientConnection connection = await LadsClientConnection.StartAsync(
                    nameof(LadsAssetManagementUsesSharedRegistryAsync), builder =>
                    {
                        builder.Services.AddSingleton<IAssetConfigurationStore>(store);
                        builder.AddLads().ConfigureLads(context => context.AddDevice("Asset", device =>
                        {
                            if (explicitService)
                            {
                                device.Identification(id => id.ProductInstanceUri = kProductInstanceUri)
                                    .AssetManagement(context.BuildContext.GetRequiredService<IAssetManagement>(),
                                        ConfigureAsset);
                            }
                            else
                            {
                                device.AssetManagement(ConfigureAsset)
                                    .Identification(id => id.ProductInstanceUri = kProductInstanceUri);
                            }
                        }));
                    }, "Asset").ConfigureAwait(false);
                var device = (LadsDevice)await connection.Server.DeviceAsync("Asset").ConfigureAwait(false);
                IAssetManagement assets = connection.Server.Provider.GetRequiredService<IAssetManagement>();
                Assert.That(assets.TryGetAsset(device.NodeId, out IAssetHandle? handle), Is.True);
                Assert.That(handle, Is.SameAs(device.Asset));
                Assert.That(handle!.NodeId, Is.EqualTo(device.NodeId));
                Assert.That(handle.ProductInstanceUri, Is.EqualTo(kProductInstanceUri));
                Assert.That(handle.IsRegistered, Is.True);
                Assert.That(handle.IsAssetIdConfigurable, Is.True);
                Assert.That(handle.AssetId, Is.EqualTo("Lab-1"));
                Assert.That(device.StoreIdentity, Is.EqualTo(kProductInstanceUri));
                Assert.That(assets.Assets.Count, Is.EqualTo(1));
                Assert.That((await FindAliasesAsync(connection.Server, kProductInstanceUri).ConfigureAwait(false))
                    .Count, Is.EqualTo(1));

                NodeId assetId = await connection.Server.ChildAsync(device.NodeId, "Identification", "AssetId")
                    .ConfigureAwait(false);
                NodeId directAssetId = await connection.Server.ChildAsync(device.NodeId, "AssetId")
                    .ConfigureAwait(false);
                Assert.That(assetId, Is.EqualTo(directAssetId));
                WriteResponse response = await connection.Session.WriteAsync(null,
                [
                    new WriteValue
                    {
                        NodeId = assetId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From("Lab-42"))
                    }
                ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results[0], Is.EqualTo(StatusCodes.Good));
                DataValue assigned = await connection.Session.ReadValueAsync(assetId, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(assigned.WrappedValue.TryGetValue(out string assignedValue), Is.True);
                Assert.That(assignedValue, Is.EqualTo("Lab-42"));
                Assert.That(handle.AssetId, Is.EqualTo("Lab-42"));
                Opc.Ua.LADS.Client.LadsIdentification identification = await connection.Client
                    .ReadIdentificationAsync(device.NodeId).ConfigureAwait(false);
                Assert.That(identification.AssetId, Is.EqualTo("Lab-42"));
                Assert.That(await store.GetValueAsync(kProductInstanceUri, AssetConfigurationNames.AssetId)
                    .ConfigureAwait(false), Is.EqualTo("Lab-42"));

                IAssetHealth health = handle.Health!;
                Assert.That(health.DeviceHealth, Is.EqualTo(DeviceHealthEnumeration.NORMAL));
                Assert.That(health.DerivesFromAlarms, Is.True);
                Assert.That(health.TryGetAlarm("Overheat", out IAssetHealthAlarm? alarm), Is.True);
                await alarm!.RaiseAsync(800, new LocalizedText("Chamber temperature is too high."))
                    .ConfigureAwait(false);
                Assert.That(alarm.IsActive, Is.True);
                Assert.That(alarm.Severity, Is.EqualTo(800));
                NodeId healthId = await connection.Server.ChildAsync(device.NodeId, "DeviceHealth")
                    .ConfigureAwait(false);
                DataValue observed = await connection.Session.ReadValueAsync(healthId, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(observed.WrappedValue.TryGetValue(out int observedHealth), Is.True);
                Assert.That(observedHealth, Is.EqualTo((int)DeviceHealthEnumeration.FAILURE));
                Assert.That(health.DeviceHealth, Is.EqualTo(DeviceHealthEnumeration.FAILURE));
                await alarm.ClearAsync().ConfigureAwait(false);
                Assert.That(alarm.IsActive, Is.False);
                Assert.That(health.DeviceHealth, Is.EqualTo(DeviceHealthEnumeration.NORMAL));
            }
            using var reopened = new FileSystemAssetConfigurationStore(directory);
            await using LadsHostedServer restored = await LadsHostedServer.StartAsync(
                nameof(LadsAssetManagementUsesSharedRegistryAsync), builder =>
                {
                    builder.Services.AddSingleton<IAssetConfigurationStore>(reopened);
                    builder.AddLads().ConfigureLads(context => context.AddDevice("Asset", device => device
                        .Identification(id => id.ProductInstanceUri = kProductInstanceUri)
                        .AssetManagement(asset => asset.WithConfigurableAssetId("Different default"))));
                }, "Asset").ConfigureAwait(false);
            ILadsDevice restoredDevice = await restored.DeviceAsync("Asset").ConfigureAwait(false);
            Assert.That(restoredDevice.Asset!.AssetId, Is.EqualTo("Lab-42"));
            Assert.That(restoredDevice.Asset.IsRegistered, Is.True);
            Assert.That(restored.Provider.GetRequiredService<IAssetManagement>().Assets.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// A later LADS publication failure withdraws discovery before deleting the owning nodes.
        /// </summary>
        [Test]
        public async Task LadsAssetRegistrationFailureRemovesDiscoveryAsync()
        {
            ILadsSetupContext? setup = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(LadsAssetRegistrationFailureRemovesDiscoveryAsync),
                builder => builder.AddLads().ConfigureLads(context =>
                {
                    setup = context;
                    context.AddDevice("Seed", _ => { });
                }), "Seed").ConfigureAwait(false);
            IAssetManagement assets = server.Provider.GetRequiredService<IAssetManagement>();
            LadsDevice? failed = null;
            IAssetHandle? registered = null;
            bool visibleDuringStart = false;
            IOException? failure = await Assert.ThrowsAsync<IOException>(async () =>
                await setup!.AddDeviceAsync("Candidate", builder =>
                {
                    builder.Identification(id => id.ProductInstanceUri = kProductInstanceUri)
                        .AssetManagement(asset => asset.WithDeviceHealth());
                    failed = ((LadsDeviceBuilder)builder).Device;
                    failed.DeviceState.StateChangedAsync += (_, _) =>
                    {
                        registered = failed.Asset;
                        visibleDuringStart = assets.TryGetAsset(failed.NodeId, out _);
                        throw new IOException("LADS publication failed after AMB registration.");
                    };
                }).ConfigureAwait(false));
            Assert.That(failure!.Message, Is.EqualTo("LADS publication failed after AMB registration."));
            Assert.That(visibleDuringStart, Is.True);
            Assert.That(registered, Is.Not.Null);
            Assert.That(registered!.IsRegistered, Is.False);
            Assert.That(assets.Assets.Count, Is.Zero);
            Assert.That(assets.TryGetAsset(failed!.NodeId, out _), Is.False);
            Assert.That(failed.Runtime.Manager.FindPredefinedNode(failed.NodeId), Is.Null);
            Assert.That(server.Registry.TryGetDevice("Candidate", out _), Is.False);
            Assert.That((await FindAliasesAsync(server, kProductInstanceUri).ConfigureAwait(false)).Count, Is.Zero);
            ILadsDevice retry = await setup!.AddDeviceAsync("Candidate", device => device
                .Identification(id => id.ProductInstanceUri = kProductInstanceUri)
                .AssetManagement(asset => asset.WithDeviceHealth())).ConfigureAwait(false);
            Assert.That(retry.Asset!.IsRegistered, Is.True);
            Assert.That(assets.Assets.Count, Is.EqualTo(1));
            Assert.That((await FindAliasesAsync(server, kProductInstanceUri).ConfigureAwait(false)).Count,
                Is.EqualTo(1));
        }

        /// <summary>
        /// Device cleanup withdraws asset aliases and registration, and repeated cleanup joins the same outcome.
        /// </summary>
        [Test]
        public async Task LadsAssetDisposalUnregistersAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(LadsAssetDisposalUnregistersAsync),
                builder => builder.AddLads().ConfigureLads(context => context.AddDevice("Asset", device => device
                    .Identification(id => id.ProductInstanceUri = kProductInstanceUri)
                    .AssetManagement(asset => asset.WithDeviceHealth()))),
                "Asset").ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync("Asset").ConfigureAwait(false);
            IAssetManagement assets = server.Provider.GetRequiredService<IAssetManagement>();
            IAssetHandle handle = device.Asset!;
            Assert.That(handle.IsRegistered, Is.True);
            Assert.That((await FindAliasesAsync(server, kProductInstanceUri).ConfigureAwait(false)).Count,
                Is.EqualTo(1));

            Task first = device.DisposeAsync().AsTask();
            Task second = device.DisposeAsync().AsTask();
            Assert.That(second, Is.SameAs(first));
            await Task.WhenAll(first, second).ConfigureAwait(false);
            Assert.That(handle.IsRegistered, Is.False);
            Assert.That(device.Asset, Is.SameAs(handle));
            Assert.That(assets.TryGetAsset(device.NodeId, out _), Is.False);
            Assert.That(assets.Assets.Count, Is.Zero);
            Assert.That((await FindAliasesAsync(server, kProductInstanceUri).ConfigureAwait(false)).Count, Is.Zero);
        }

        /// <summary>
        /// A persistent-store error rejects the wire write and preserves both identification views.
        /// </summary>
        [Test]
        public async Task LadsAssetWriteFailurePreservesIdentificationAsync()
        {
            var store = new Mock<IAssetConfigurationStore>(MockBehavior.Strict);
            store.SetupGet(value => value.IsPersistent).Returns(true);
            store.Setup(value => value.GetValueAsync(
                kProductInstanceUri, AssetConfigurationNames.AssetId, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<string?>((string?)null));
            store.Setup(value => value.SetValueAsync(
                kProductInstanceUri, AssetConfigurationNames.AssetId, "Rejected", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(Task.FromException(new IOException("Asset configuration is unavailable."))));
            await using LadsClientConnection connection = await LadsClientConnection.StartAsync(
                nameof(LadsAssetWriteFailurePreservesIdentificationAsync), builder =>
                {
                    builder.Services.AddSingleton(store.Object);
                    builder.AddLads().ConfigureLads(context => context.AddDevice("Asset", device => device
                        .Identification(id => id.ProductInstanceUri = kProductInstanceUri)
                        .AssetManagement(asset => asset.WithConfigurableAssetId("Kept"))));
                }, "Asset").ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync("Asset").ConfigureAwait(false);
            NodeId assetId = await connection.Server.ChildAsync(device.NodeId, "AssetId").ConfigureAwait(false);
            WriteResponse response = await connection.Session.WriteAsync(null,
            [
                new WriteValue
                {
                    NodeId = assetId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From("Rejected"))
                }
            ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results[0], Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(device.Asset!.AssetId, Is.EqualTo("Kept"));
            Opc.Ua.LADS.Client.LadsIdentification identification = await connection.Client
                .ReadIdentificationAsync(device.NodeId).ConfigureAwait(false);
            Assert.That(identification.AssetId, Is.EqualTo("Kept"));
            Assert.That((await FindAliasesAsync(connection.Server, kProductInstanceUri).ConfigureAwait(false)).Count,
                Is.EqualTo(1));
            store.Verify(value => value.SetValueAsync(
                kProductInstanceUri, AssetConfigurationNames.AssetId, "Rejected", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Failed asset withdrawal preserves every error and prevents reuse of an uncertain name or storage key.
        /// </summary>
        [Test]
        public async Task FailedAssetCleanupRetainsNameAndStorageIdentityAsync()
        {
            ILadsSetupContext? setup = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(FailedAssetCleanupRetainsNameAndStorageIdentityAsync),
                builder => builder.AddLads().ConfigureLads(context =>
                {
                    setup = context;
                    context.AddDevice("Seed", _ => { });
                }), "Seed").ConfigureAwait(false);
            var handle = new Mock<IAssetHandle>(MockBehavior.Strict);
            handle.Setup(value => value.UnregisterAsync(CancellationToken.None))
                .Returns(() => new ValueTask(Task.FromException(new IOException("Asset withdrawal failed."))));
            var assets = new Mock<IAssetManagement>(MockBehavior.Strict);
            assets.Setup(value => value.RegisterAssetAsync(
                It.IsAny<INodeBuilder<BaseObjectState>>(), It.IsAny<Action<IAssetBuilder>>(),
                It.IsAny<CancellationToken>())).Returns(new ValueTask<IAssetHandle>(handle.Object));

            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                await setup!.AddDeviceAsync("Candidate", builder =>
                {
                    builder.Identification(id => id.ProductInstanceUri = kProductInstanceUri)
                        .AssetManagement(assets.Object, _ => { });
                    ((LadsDeviceBuilder)builder).Device.DeviceState.StateChangedAsync += (_, _) =>
                        throw new IOException("Device publication failed.");
                }).ConfigureAwait(false));
            Assert.That(failure!.Flatten().InnerExceptions.Select(error => error.Message),
                Is.EqualTo(s_failedAssetCleanupMessages));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadBrowseNameDuplicated, () =>
                setup!.AddDeviceAsync("Candidate", _ => { })).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadConfigurationError, () =>
                setup!.AddDeviceAsync("Different", device => device.Identification(
                    id => id.ProductInstanceUri = kProductInstanceUri))).ConfigureAwait(false);
            ILadsDevice independent = await setup!.AddDeviceAsync("Different", _ => { }).ConfigureAwait(false);
            Assert.That(independent.Name, Is.EqualTo("Different"));
            Assert.That(server.Registry.TryGetDevice("Candidate", out _), Is.False);
            assets.Verify(value => value.RegisterAssetAsync(
                It.IsAny<INodeBuilder<BaseObjectState>>(), It.IsAny<Action<IAssetBuilder>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            handle.Verify(value => value.UnregisterAsync(CancellationToken.None), Times.Exactly(2));
            assets.VerifyNoOtherCalls();
            handle.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Cancellation after AMB registration withdraws the asset and nodes before permitting a corrected retry.
        /// </summary>
        [Test]
        public async Task AssetRegistrationCancellationDoesNotPublishAndAllowsRetryAsync()
        {
            ILadsSetupContext? setup = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(AssetRegistrationCancellationDoesNotPublishAndAllowsRetryAsync),
                builder => builder.AddLads().ConfigureLads(context =>
                {
                    setup = context;
                    context.AddDevice("Seed", _ => { });
                }), "Seed").ConfigureAwait(false);
            IAssetManagement shared = server.Provider.GetRequiredService<IAssetManagement>();
            using var cancellation = new CancellationTokenSource();
            IAssetHandle? registered = null;
            var assets = new Mock<IAssetManagement>(MockBehavior.Strict);
            assets.Setup(value => value.RegisterAssetAsync(
                It.IsAny<INodeBuilder<BaseObjectState>>(), It.IsAny<Action<IAssetBuilder>>(), cancellation.Token))
                .Returns(async (INodeBuilder<BaseObjectState> node, Action<IAssetBuilder>? configure,
                    CancellationToken ct) =>
                {
                    registered = await shared.RegisterAssetAsync(node, configure, ct).ConfigureAwait(false);
                    cancellation.Cancel();
                    return registered;
                });
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await setup!.AddDeviceAsync("Candidate", device => device
                    .Identification(id => id.ProductInstanceUri = kProductInstanceUri)
                    .AssetManagement(assets.Object, asset => asset.WithDeviceHealth()), cancellation.Token)
                    .ConfigureAwait(false));
            Assert.That(registered, Is.Not.Null);
            Assert.That(registered!.IsRegistered, Is.False);
            Assert.That(shared.Assets.Count, Is.Zero);
            var seed = (LadsDevice)await server.DeviceAsync("Seed").ConfigureAwait(false);
            Assert.That(seed.Runtime.Manager.FindPredefinedNode(registered.NodeId), Is.Null);
            Assert.That(server.Registry.TryGetDevice("Candidate", out _), Is.False);
            Assert.That((await FindAliasesAsync(server, kProductInstanceUri).ConfigureAwait(false)).Count, Is.Zero);
            ILadsDevice retry = await setup!.AddDeviceAsync("Candidate", device => device
                .Identification(id => id.ProductInstanceUri = kProductInstanceUri)
                .AssetManagement(asset => asset.WithDeviceHealth())).ConfigureAwait(false);
            Assert.That(retry.Asset!.IsRegistered, Is.True);
            Assert.That(shared.Assets.Count, Is.EqualTo(1));
            assets.Verify(value => value.RegisterAssetAsync(
                It.IsAny<INodeBuilder<BaseObjectState>>(), It.IsAny<Action<IAssetBuilder>>(), cancellation.Token),
                Times.Once);
            assets.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Composes persistent identification and a health alarm using the existing AMB building blocks.
        /// </summary>
        private static void ConfigureAsset(IAssetBuilder asset)
        {
            asset.WithConfigurableAssetId("Lab-1")
                .WithDeviceHealth(deriveFromAlarms: true)
                .WithHealthAlarm("Overheat", AssetHealthAlarmKind.Failure, AmbConditionClass.OverTemperature);
        }

        /// <summary>
        /// Reads the public AMB discovery category through its FindAlias method.
        /// </summary>
        private static async Task<ArrayOf<AliasNameDataType>> FindAliasesAsync(LadsHostedServer server, string uri)
        {
            NodeId category = server.ToNodeId(Opc.Ua.AMB.ObjectIds.AssetsByProductInstanceUri);
            CallMethodResult result = await server.CallByNameAsync(
                category, "FindAlias", Variant.From(uri), Variant.From(NodeId.Null)).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.OutputArguments[0].TryGetStructure(out ArrayOf<AliasNameDataType> aliases), Is.True);
            return aliases;
        }

        /// <summary>The stable identity used by the opted-in device.</summary>
        private const string kProductInstanceUri = "urn:tests:lads:asset-management";

        /// <summary>Publication and withdrawal failures retained by the cleanup regression.</summary>
        private static readonly string[] s_failedAssetCleanupMessages =
        [
            "Device publication failed.", "Asset withdrawal failed.", "Asset withdrawal failed."
        ];
    }
}
