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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.AMB.Server;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Hosting;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Registers Device Integration devices, and objects of other managers,
    /// as manageable assets of a hosted server.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class AssetRegistrationTests
    {
        private const string SensorUri = "urn:acme:sensor:4711";

        [TestCase(false, TestName = "DeviceIntegrationRegisteredFirst")]
        [TestCase(true, TestName = "AssetManagementRegisteredFirst")]
        public async Task ADeviceWithItsProductInstanceUriBecomesAnAssetAsync(bool assetManagementFirst)
        {
            IAssetHandle? handle = null;
            NodeId deviceId = NodeId.Null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(ADeviceWithItsProductInstanceUriBecomesAnAssetAsync) + assetManagementFirst,
                builder =>
                {
                    if (assetManagementFirst)
                    {
                        builder.AddAssetManagement().AddOpcUaDi();
                    }
                    else
                    {
                        builder.AddOpcUaDi().AddAssetManagement();
                    }
                    builder.ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> device = await CreateDeviceAsync(context, "Sensor", SensorUri).ConfigureAwait(false);
                        deviceId = device.Device.NodeId;
                        handle = await device.RegisterAsAssetAsync(
                            context.GetRequiredService<IAssetManagement>(),
                            cancellationToken: context.CancellationToken).ConfigureAwait(false);
                    });
                }).ConfigureAwait(false);

            Assert.That(handle, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(handle!.NodeId, Is.EqualTo(deviceId));
                Assert.That(handle.BrowseName.Name, Is.EqualTo("Sensor"));
                Assert.That(handle.ProductInstanceUri, Is.EqualTo(SensorUri));
                Assert.That(handle.AssetId, Is.Null);
                Assert.That(handle.IsAssetIdConfigurable, Is.False);
                Assert.That(handle.IsRegistered, Is.True);
                Assert.That(handle.ToString(), Does.Contain("Sensor"));
                Assert.That(server.AssetManagement.Assets.ToArray(), Is.EqualTo(new[] { handle }));
                Assert.That(server.AssetManagement.TryGetAsset(deviceId, out IAssetHandle? found), Is.True);
                Assert.That(found, Is.SameAs(handle));
                Assert.That(server.AssetManagement.TryGetAsset(new NodeId(1u), out _), Is.False);
            });
        }

        [Test]
        public async Task AnAssetWithoutProductInstanceUriIsRefusedAsync()
        {
            ServiceResultException? refused = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AnAssetWithoutProductInstanceUriIsRefusedAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> device = await CreateDeviceAsync(context, "Anonymous", null).ConfigureAwait(false);
                        try
                        {
                            await device.RegisterAsAssetAsync(context.GetRequiredService<IAssetManagement>()).ConfigureAwait(false);
                        }
                        catch (ServiceResultException ex)
                        {
                            refused = ex;
                        }
                    })).ConfigureAwait(false);

            Assert.That(refused, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(refused!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(refused.Message, Does.Contain("ProductInstanceUri"));
                Assert.That(server.AssetManagement.Assets.Count, Is.Zero);
            });
        }

        [Test]
        public async Task AnAssetWithoutProductInstanceUriIsAcceptedWhenNotRequiredAsync()
        {
            IAssetHandle? handle = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AnAssetWithoutProductInstanceUriIsAcceptedWhenNotRequiredAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement(options => options.RequireProductInstanceUri = false)
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> device = await CreateDeviceAsync(context, "Anonymous", null).ConfigureAwait(false);
                        handle = await device.RegisterAsAssetAsync(context.GetRequiredService<IAssetManagement>()).ConfigureAwait(false);
                    })).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(handle!.ProductInstanceUri, Is.Empty);
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Not.Contain(new QualifiedName("AMB Asset Identification")));
                Assert.That(server.Manager.ServerProfiles.Count, Is.Zero);
            });
        }

        [Test]
        public async Task AnObjectIsRegisteredOnceAsync()
        {
            ServiceResultException? refused = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AnObjectIsRegisteredOnceAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> device = await CreateDeviceAsync(context, "Sensor", SensorUri).ConfigureAwait(false);
                        IAssetManagement assets = context.GetRequiredService<IAssetManagement>();
                        await device.RegisterAsAssetAsync(assets).ConfigureAwait(false);
                        try
                        {
                            await device.RegisterAsAssetAsync(assets).ConfigureAwait(false);
                        }
                        catch (ServiceResultException ex)
                        {
                            refused = ex;
                        }
                    })).ConfigureAwait(false);

            Assert.That(refused?.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
            Assert.That(server.AssetManagement.Assets.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task IdentificationInTheIdentificationGroupIsFoundAsync()
        {
            // A Machinery machine keeps its nameplate as components of the
            // 2:Identification group rather than on the machine itself.
            IAssetHandle? handle = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(IdentificationInTheIdentificationGroupIsFoundAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureAssetManagement(async context =>
                    {
                        INodeBuilder<BaseObjectState> machine = CreateMachine(context, "urn:acme:press:1");
                        handle = await context.Assets.RegisterAssetAsync(machine, null, context.CancellationToken).ConfigureAwait(false);
                    })).ConfigureAwait(false);

            Assert.That(handle!.ProductInstanceUri, Is.EqualTo("urn:acme:press:1"));
        }

        [Test]
        public async Task AnAssetNeedsTheDeviceIntegrationNamespaceAsync()
        {
            ServiceResultException? refused = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AnAssetNeedsTheDeviceIntegrationNamespaceAsync),
                builder => builder
                    .AddAssetManagement()
                    .ConfigureAssetManagement(async context =>
                    {
                        INodeBuilder<BaseObjectState> machine = context.Builder.AddObject(
                            new QualifiedName("Machine", context.Manager.InstanceNamespaceIndex),
                            Ua.ObjectIds.ObjectsFolder);
                        try
                        {
                            await context.Assets.RegisterAssetAsync(machine).ConfigureAwait(false);
                        }
                        catch (ServiceResultException ex)
                        {
                            refused = ex;
                        }
                    })).ConfigureAwait(false);

            Assert.That(refused?.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(refused!.Message, Does.Contain("Device Integration"));
        }

        [Test]
        public async Task UnregisteringRemovesTheAssetAsync()
        {
            IAssetHandle? handle = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(UnregisteringRemovesTheAssetAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> device = await CreateDeviceAsync(context, "Sensor", SensorUri).ConfigureAwait(false);
                        handle = await device.RegisterAsAssetAsync(context.GetRequiredService<IAssetManagement>()).ConfigureAwait(false);
                    })).ConfigureAwait(false);
            Assert.That(server.Manager.ServerProfiles.Count, Is.EqualTo(1));

            await handle!.UnregisterAsync().ConfigureAwait(false);
            await handle.UnregisterAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(handle.IsRegistered, Is.False);
                Assert.That(server.AssetManagement.Assets.Count, Is.Zero);
                Assert.That(server.AssetManagement.TryGetAsset(handle.NodeId, out _), Is.False);
                Assert.That(server.Manager.ConformanceUnits.Count, Is.Zero, "an empty server claims nothing");
                Assert.That(server.Manager.ServerProfiles.Count, Is.Zero);
            });
        }

        internal static async Task<IDeviceBuilder<DeviceState>> CreateDeviceAsync(
            IDiPostSetupContext context,
            string name,
            string? productInstanceUri)
        {
            IDeviceBuilder<DeviceState> device = await context.CreateDeviceAsync(
                new QualifiedName(name, context.Manager.InstanceNamespaceIndex)).ConfigureAwait(false);
            if (productInstanceUri != null)
            {
                device.WithIdentification(identification =>
                    identification.ProductInstanceUri = productInstanceUri);
            }
            return device;
        }

        /// <summary>
        /// Builds an object shaped like a Machinery machine: the nameplate is
        /// a component of its 2:Identification group.
        /// </summary>
        internal static INodeBuilder<BaseObjectState> CreateMachine(
            IAssetManagementSetupContext context,
            string productInstanceUri)
        {
            ushort di = (ushort)context.Manager.Server.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi);
            ushort ns = context.Manager.InstanceNamespaceIndex;
            INodeBuilder<BaseObjectState> machine = context.Builder.AddObject(
                new QualifiedName("Press", ns),
                Ua.ObjectIds.ObjectsFolder);
            var identification = new BaseObjectState(machine.Node)
            {
                BrowseName = new QualifiedName("Identification", di),
                DisplayName = new LocalizedText("Identification"),
                TypeDefinitionId = ExpandedNodeId.ToNodeId(
                    Opc.Ua.Di.ObjectTypeIds.FunctionalGroupType,
                    context.Manager.Server.NamespaceUris),
                ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent
            };
            identification.NodeId = context.Manager.New(context.Manager.SystemContext, identification);
            machine.Node.AddChild(identification);
            PropertyState uri = identification.AddProperty<string, VariantBuilder>(
                "ProductInstanceUri",
                Ua.DataTypeIds.String,
                ValueRanks.Scalar);
            uri.BrowseName = new QualifiedName("ProductInstanceUri", di);
            uri.NodeId = context.Manager.New(context.Manager.SystemContext, uri);
            uri.WrappedValue = Variant.From(productInstanceUri);
            context.Manager.AddNode(identification);
            return machine;
        }
    }
}
