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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.AMB.Server;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Configuration;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Tests the writable, persistent <c>AssetId</c> ("AMB Configurable
    /// Asset Identification") through the server's Write service.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class ConfigurableAssetIdTests
    {
        private const string SensorUri = "urn:acme:sensor:4711";

        [Test]
        public async Task ADeviceGetsTheAssetIdItsTypeDeclaresAsync()
        {
            var store = new MemoryAssetConfigurationStore();
            DeviceState? device = null;
            IAssetHandle? handle = null;
            await using AmbHostedServer server = await StartWithDeviceAsync(
                nameof(ADeviceGetsTheAssetIdItsTypeDeclaresAsync),
                store,
                (builder, assets) =>
                {
                    device = builder.Device;
                    return builder.RegisterAsAssetAsync(
                        assets,
                        asset => asset.WithConfigurableAssetId("Sensor-1"));
                },
                h => handle = h).ConfigureAwait(false);

            Assert.That(device!.AssetId, Is.Not.Null, "the slot DeviceType declares is used");
            NodeId assetId = device.AssetId!.NodeId;
            Assert.Multiple(() =>
            {
                Assert.That(device.AssetId.Value, Is.EqualTo("Sensor-1"));
                Assert.That(device.AssetId.AccessLevel & AccessLevels.CurrentWrite, Is.Not.Zero);
                Assert.That(device.AssetId.UserAccessLevel & AccessLevels.CurrentWrite, Is.Not.Zero);
                Assert.That(
                    device.ReferenceExists(
                        Ua.ReferenceTypeIds.HasInterface,
                        false,
                        server.ToNodeId(Opc.Ua.Di.ObjectTypeIds.ITagNameplateType)),
                    Is.False,
                    "DeviceType implements ITagNameplateType already");
                Assert.That(handle!.IsAssetIdConfigurable, Is.True);
                Assert.That(handle.AssetId, Is.EqualTo("Sensor-1"));
            });

            StatusCode written = await server.WriteAsync(assetId, Variant.From("Pump-7")).ConfigureAwait(false);
            DataValue read = await server.ReadAsync(assetId).ConfigureAwait(false);
            string? persisted = await store.GetValueAsync(SensorUri, AssetConfigurationNames.AssetId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(written), Is.True, written.ToString());
                Assert.That(read.WrappedValue.TryGetValue(out string value), Is.True);
                Assert.That(value, Is.EqualTo("Pump-7"));
                Assert.That(persisted, Is.EqualTo("Pump-7"));
                Assert.That(handle!.AssetId, Is.EqualTo("Pump-7"));
            });
        }

        [Test]
        public async Task TheAssetIdOfTheSlotIsAPropertyClientsCanBrowseToAsync()
        {
            DeviceState? device = null;
            await using AmbHostedServer server = await StartWithDeviceAsync(
                nameof(TheAssetIdOfTheSlotIsAPropertyClientsCanBrowseToAsync),
                new MemoryAssetConfigurationStore(),
                (builder, assets) =>
                {
                    device = builder.Device;
                    return builder.RegisterAsAssetAsync(
                        assets,
                        asset => asset.WithConfigurableAssetId("Sensor-1"));
                },
                _ => { }).ConfigureAwait(false);

            NodeId assetId = device!.AssetId!.NodeId;
            IReadOnlyList<ReferenceDescription> properties = await server
                .BrowseAsync(device.NodeId, Ua.ReferenceTypeIds.HasProperty)
                .ConfigureAwait(false);
            DataValue dataType = await server.ReadAsync(assetId, Attributes.DataType).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    properties.Select(reference => reference.NodeId),
                    Does.Contain((ExpandedNodeId)assetId),
                    "browsing the device lists the AssetId");
                Assert.That(
                    properties.Single(reference => reference.NodeId == (ExpandedNodeId)assetId).TypeDefinition,
                    Is.EqualTo((ExpandedNodeId)Ua.VariableTypeIds.PropertyType));
                Assert.That(
                    dataType.WrappedValue.TryGetValue(out NodeId type) ? type : NodeId.Null,
                    Is.EqualTo(Ua.DataTypeIds.String));
            });
        }

        [Test]
        public async Task TheAssetIdIsLimitedInLengthAndTypeAsync()
        {
            var store = new MemoryAssetConfigurationStore();
            DeviceState? device = null;
            await using AmbHostedServer server = await StartWithDeviceAsync(
                nameof(TheAssetIdIsLimitedInLengthAndTypeAsync),
                store,
                (builder, assets) =>
                {
                    device = builder.Device;
                    return builder.RegisterAsAssetAsync(assets, asset => asset.WithConfigurableAssetId());
                },
                configure: options => options.MaxAssetIdLength = 40).ConfigureAwait(false);
            NodeId assetId = device!.AssetId!.NodeId;

            StatusCode longest = await server.WriteAsync(assetId, Variant.From(new string('x', 40)))
                .ConfigureAwait(false);
            StatusCode tooLong = await server.WriteAsync(assetId, Variant.From(new string('y', 41)))
                .ConfigureAwait(false);
            StatusCode wrongType = await server.WriteAsync(assetId, Variant.From(42)).ConfigureAwait(false);
            string? persisted = await store.GetValueAsync(SensorUri, AssetConfigurationNames.AssetId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(longest), Is.True, longest.ToString());
                Assert.That(tooLong.Code, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(wrongType.Code, Is.EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(persisted, Is.EqualTo(new string('x', 40)));
                Assert.That(device.AssetId.Value, Is.EqualTo(new string('x', 40)));
            });
        }

        [Test]
        public async Task APersistedValueWinsOverTheDefaultAsync()
        {
            var store = new MemoryAssetConfigurationStore();
            await store.SetValueAsync(SensorUri, AssetConfigurationNames.AssetId, "Persisted").ConfigureAwait(false);
            DeviceState? device = null;
            await using AmbHostedServer server = await StartWithDeviceAsync(
                nameof(APersistedValueWinsOverTheDefaultAsync),
                store,
                (builder, assets) =>
                {
                    device = builder.Device;
                    return builder.RegisterAsAssetAsync(
                        assets,
                        asset => asset.WithConfigurableAssetId("Default"));
                }).ConfigureAwait(false);

            Assert.That(device!.AssetId!.Value, Is.EqualTo("Persisted"));
        }

        [Test]
        public async Task TheConfiguredValueSurvivesARestartAsync()
        {
            string directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(TheConfiguredValueSurvivesARestartAsync),
                Guid.NewGuid().ToString("N"));

            DeviceState? first = null;
            await using (AmbHostedServer server = await StartWithDeviceAsync(
                nameof(TheConfiguredValueSurvivesARestartAsync) + "1",
                store: null,
                (builder, assets) =>
                {
                    first = builder.Device;
                    return builder.RegisterAsAssetAsync(assets, asset => asset.WithConfigurableAssetId("Initial"));
                },
                configure: options => options.UseFileSystemStores(directory)).ConfigureAwait(false))
            {
                StatusCode written = await server.WriteAsync(first!.AssetId!.NodeId, Variant.From("Configured"))
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(written), Is.True, written.ToString());
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("AMB Configurable Asset Identification")));
            }

            DeviceState? second = null;
            await using (AmbHostedServer server = await StartWithDeviceAsync(
                nameof(TheConfiguredValueSurvivesARestartAsync) + "2",
                store: null,
                (builder, assets) =>
                {
                    second = builder.Device;
                    return builder.RegisterAsAssetAsync(assets, asset => asset.WithConfigurableAssetId("Initial"));
                },
                configure: options => options.UseFileSystemStores(directory)).ConfigureAwait(false))
            {
                Assert.That(second!.AssetId!.Value, Is.EqualTo("Configured"));
            }
        }

        [Test]
        public async Task ExistingWriteHandlersKeepRunningAsync()
        {
            var store = new MemoryAssetConfigurationStore();
            DeviceState? device = null;
            await using AmbHostedServer server = await StartWithDeviceAsync(
                nameof(ExistingWriteHandlersKeepRunningAsync),
                store,
                (builder, assets) =>
                {
                    device = builder.Device;
                    device.AddAssetId(builder.Context);
                    builder.Manager.AddNode(device.AssetId!);
                    device.AssetId!.OnSimpleWriteValue = (ISystemContext _, NodeState _, ref Variant value) =>
                        value.TryGetValue(out string text) && text == "forbidden"
                            ? StatusCodes.BadUserAccessDenied
                            : ServiceResult.Good;
                    return builder.RegisterAsAssetAsync(assets, asset => asset.WithConfigurableAssetId());
                }).ConfigureAwait(false);
            NodeId assetId = device!.AssetId!.NodeId;

            StatusCode refused = await server.WriteAsync(assetId, Variant.From("forbidden")).ConfigureAwait(false);
            string? afterRefusal = await store.GetValueAsync(SensorUri, AssetConfigurationNames.AssetId)
                .ConfigureAwait(false);
            StatusCode accepted = await server.WriteAsync(assetId, Variant.From("allowed")).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(refused.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(afterRefusal, Is.Null, "a refused value is not persisted");
                Assert.That(StatusCode.IsGood(accepted), Is.True, accepted.ToString());
            });
        }

        [Test]
        public async Task AValueThatCannotBePersistedIsRefusedAsync()
        {
            DeviceState? device = null;
            await using AmbHostedServer server = await StartWithDeviceAsync(
                nameof(AValueThatCannotBePersistedIsRefusedAsync),
                new FailingStore(),
                (builder, assets) =>
                {
                    device = builder.Device;
                    return builder.RegisterAsAssetAsync(assets, asset => asset.WithConfigurableAssetId("Kept"));
                }).ConfigureAwait(false);

            StatusCode refused = await server.WriteAsync(device!.AssetId!.NodeId, Variant.From("Lost"))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(refused.Code, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                Assert.That(device.AssetId.Value, Is.EqualTo("Kept"));
            });
        }

        [Test]
        public async Task UnregisteringRestoresTheWriteHandlersAsync()
        {
            DeviceState? device = null;
            IAssetHandle? handle = null;
            await using AmbHostedServer server = await StartWithDeviceAsync(
                nameof(UnregisteringRestoresTheWriteHandlersAsync),
                new MemoryAssetConfigurationStore(),
                (builder, assets) =>
                {
                    device = builder.Device;
                    return builder.RegisterAsAssetAsync(assets, asset => asset.WithConfigurableAssetId());
                },
                h => handle = h).ConfigureAwait(false);
            Assert.That(device!.AssetId!.OnSimpleWriteValueAsync, Is.Not.Null);

            await handle!.UnregisterAsync().ConfigureAwait(false);

            Assert.That(device.AssetId.OnSimpleWriteValueAsync, Is.Null);
        }

        [Test]
        public async Task ADefaultLongerThanTheMaximumIsRefusedAsync()
        {
            ServiceResultException? refused = null;
            await using AmbHostedServer server = await StartWithDeviceAsync(
                nameof(ADefaultLongerThanTheMaximumIsRefusedAsync),
                new MemoryAssetConfigurationStore(),
                async (builder, assets) =>
                {
                    try
                    {
                        return await builder.RegisterAsAssetAsync(
                            assets,
                            asset => asset.WithConfigurableAssetId(new string('z', 41))).ConfigureAwait(false);
                    }
                    catch (ServiceResultException ex)
                    {
                        refused = ex;
                        return null!;
                    }
                },
                configure: options => options.MaxAssetIdLength = 40).ConfigureAwait(false);

            Assert.That(refused?.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task AnObjectWithoutADeclaredAssetIdGetsTheInterfaceAsync()
        {
            // An object whose type is not a DI component has no AssetId slot:
            // the property is added and ITagNameplateType applied to the
            // instance (OPC 10000-110 §7).
            IAssetHandle? handle = null;
            BaseObjectState? machine = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AnObjectWithoutADeclaredAssetIdGetsTheInterfaceAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureAssetManagement(async context =>
                    {
                        INodeBuilder<BaseObjectState> press = AssetRegistrationTests.CreateMachine(
                            context,
                            "urn:acme:press:1");
                        machine = press.Node;
                        handle = await context.Assets.RegisterAssetAsync(
                            press,
                            asset => asset.WithConfigurableAssetId("Press-1"),
                            context.CancellationToken).ConfigureAwait(false);
                    })).ConfigureAwait(false);

            ushort di = (ushort)server.Server.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi);
            BaseInstanceState? property = machine!.FindChildWithQualifiedName(
                server.Manager.SystemContext,
                new QualifiedName("AssetId", di));
            Assert.That(property, Is.InstanceOf<BaseVariableState>());

            StatusCode written = await server.WriteAsync(property!.NodeId, Variant.From("Press-2"))
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> interfaces = await server.BrowseAsync(
                machine.NodeId,
                Ua.ReferenceTypeIds.HasInterface).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(written), Is.True, written.ToString());
                Assert.That(handle!.AssetId, Is.EqualTo("Press-2"));
                Assert.That(
                    interfaces.Select(reference => server.ToNodeId(reference.NodeId)),
                    Does.Contain(server.ToNodeId(Opc.Ua.Di.ObjectTypeIds.ITagNameplateType)));
            });
        }

        [Test]
        public async Task WritesOfTheAssetIdAreAppliedInTheOrderTheyArriveAsync()
        {
            var store = new GatedStore("RACE-A");
            IAssetHandle? handle = null;
            DeviceState? sensor = null;
            await using AmbHostedServer server = await StartWithDeviceAsync(
                nameof(WritesOfTheAssetIdAreAppliedInTheOrderTheyArriveAsync),
                store,
                (device, assets) =>
                {
                    sensor = device.Device;
                    return device.RegisterAsAssetAsync(assets, asset => asset.WithConfigurableAssetId("RACE-0"));
                },
                registered => handle = registered).ConfigureAwait(false);
            NodeId assetId = sensor!.AssetId!.NodeId;

            Task<StatusCode> first = server.WriteAsync(assetId, Variant.From("RACE-A"));
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Task<StatusCode> second = server.WriteAsync(assetId, Variant.From("RACE-B"));
            await Task.Delay(300).ConfigureAwait(false);
            bool secondWaited = !second.IsCompleted;
            store.Open();
            StatusCode firstResult = await first.ConfigureAwait(false);
            StatusCode secondResult = await second.ConfigureAwait(false);

            ArrayOf<AliasNameDataType> byLast = await server
                .FindAliasAsync(ObjectIds.AssetsByAssetId, "RACE-B").ConfigureAwait(false);
            ArrayOf<AliasNameDataType> byFirst = await server
                .FindAliasAsync(ObjectIds.AssetsByAssetId, "RACE-A").ConfigureAwait(false);
            string? stored = await store.GetValueAsync(SensorUri, AssetConfigurationNames.AssetId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(firstResult), Is.True, firstResult.ToString());
                Assert.That(StatusCode.IsGood(secondResult), Is.True, secondResult.ToString());
                Assert.That(secondWaited, Is.True, "the second write waits for the first");
                Assert.That(handle!.AssetId, Is.EqualTo("RACE-B"));
                Assert.That(stored, Is.EqualTo("RACE-B"));
                Assert.That(byLast.Count, Is.EqualTo(1));
                Assert.That(byFirst.Count, Is.Zero);
            });
        }

        [Test]
        public async Task TheLengthOfAnAssetIdIsCountedInUnicodeCharactersAsync()
        {
            DeviceState? sensor = null;
            await using AmbHostedServer server = await StartWithDeviceAsync(
                nameof(TheLengthOfAnAssetIdIsCountedInUnicodeCharactersAsync),
                null,
                (device, assets) =>
                {
                    sensor = device.Device;
                    return device.RegisterAsAssetAsync(assets, asset => asset.WithConfigurableAssetId());
                },
                configure: options => options.MaxAssetIdLength = AmbServerOptions.MinimumAssetIdLength)
                .ConfigureAwait(false);
            NodeId assetId = sensor!.AssetId!.NodeId;
            string fits = string.Concat(Enumerable.Repeat("\U0001F527", AmbServerOptions.MinimumAssetIdLength));

            StatusCode accepted = await server.WriteAsync(assetId, Variant.From(fits)).ConfigureAwait(false);
            StatusCode refused = await server.WriteAsync(assetId, Variant.From(fits + "x")).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(fits, Has.Length.EqualTo(2 * AmbServerOptions.MinimumAssetIdLength));
                Assert.That(StatusCode.IsGood(accepted), Is.True, "40 characters outside the BMP: " + accepted);
                Assert.That(refused.Code, Is.EqualTo(StatusCodes.BadOutOfRange));
            });
        }

        private static Task<AmbHostedServer> StartWithDeviceAsync(
            string name,
            IAssetConfigurationStore? store,
            Func<IDeviceBuilder<DeviceState>, IAssetManagement, ValueTask<IAssetHandle>> register,
            Action<IAssetHandle>? registered = null,
            Action<AmbServerOptions>? configure = null)
        {
            return AmbHostedServer.StartAsync(name, builder =>
            {
                if (store != null)
                {
                    builder.Services.AddSingleton(store);
                }
                builder
                    .AddOpcUaDi()
                    .AddAssetManagement(configure)
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> device = await AssetRegistrationTests.CreateDeviceAsync(
                            context,
                            "Sensor",
                            SensorUri).ConfigureAwait(false);
                        IAssetHandle handle = await register(
                            device,
                            context.GetRequiredService<IAssetManagement>()).ConfigureAwait(false);
                        registered?.Invoke(handle);
                    });
            });
        }

        /// <summary>
        /// A store in memory that holds back the write of one value until it is
        /// opened.
        /// </summary>
        private sealed class GatedStore : IAssetConfigurationStore
        {
            public GatedStore(string heldValue)
            {
                m_heldValue = heldValue;
            }

            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool IsPersistent => true;

            public void Open()
            {
                m_open.TrySetResult(true);
            }

            public ValueTask<string?> GetValueAsync(
                string productInstanceUri,
                string name,
                CancellationToken cancellationToken = default)
            {
                return m_inner.GetValueAsync(productInstanceUri, name, cancellationToken);
            }

            public async ValueTask SetValueAsync(
                string productInstanceUri,
                string name,
                string? value,
                CancellationToken cancellationToken = default)
            {
                if (string.Equals(value, m_heldValue, StringComparison.Ordinal))
                {
                    Entered.TrySetResult(true);
                    await m_open.Task.ConfigureAwait(false);
                }
                await m_inner.SetValueAsync(productInstanceUri, name, value, cancellationToken).ConfigureAwait(false);
            }

            private readonly string m_heldValue;

            private readonly TaskCompletionSource<bool> m_open =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly MemoryAssetConfigurationStore m_inner = new();
        }

        /// <summary>
        /// A store whose writes fail.
        /// </summary>
        private sealed class FailingStore : IAssetConfigurationStore
        {
            public bool IsPersistent => true;

            public ValueTask<string?> GetValueAsync(
                string productInstanceUri,
                string name,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<string?>((string?)null);
            }

            public ValueTask SetValueAsync(
                string productInstanceUri,
                string name,
                string? value,
                CancellationToken cancellationToken = default)
            {
                throw new IOException("disk full");
            }
        }
    }
}
