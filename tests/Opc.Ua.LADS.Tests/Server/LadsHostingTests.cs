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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.LADS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests how LADS composes with the Machinery and Asset Management Basics owners.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [NonParallelizable]
    public sealed class LadsHostingTests
    {
        [Test]
        public async Task StandaloneHostingPublishesTheDeviceAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(StandaloneHostingPublishesTheDeviceAsync)).ConfigureAwait(false);

            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);

            Assert.That(device.Name, Is.EqualTo(LadsIncubator.DeviceName));
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Operate));
            Assert.That(device.OperationMode, Is.EqualTo(MachineryOperationModeValue.Processing));
            Assert.That(device.FunctionalUnits.Count, Is.EqualTo(1));
            Assert.That(server.Registry.Devices.Count, Is.EqualTo(1));
            Assert.That(server.Registry.TryGetDevice(LadsIncubator.DeviceName, out ILadsDevice? found), Is.True);
            Assert.That(found, Is.SameAs(device));
            Assert.That(server.Registry.TryGetDevice("Missing", out _), Is.False);
            Assert.That(
                server.Server.NamespaceUris.GetIndex(Namespaces.LADS),
                Is.GreaterThan(0));
            Assert.That(
                server.Server.NamespaceUris.GetIndex("urn:opcua-netstandard:lads:instances"),
                Is.GreaterThan(0));

            DataValue value = await server.ReadAsync(device.NodeId, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out QualifiedName name), Is.True);
            Assert.That(name.Name, Is.EqualTo(LadsIncubator.DeviceName));
        }

        [Test]
        public async Task CustomInstanceNamespaceIsUsedAsync()
        {
            const string uri = "urn:example:lads:custom";
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(CustomInstanceNamespaceIsUsedAsync),
                options: options => options.InstanceNamespaceUri = uri).ConfigureAwait(false);

            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);

            Assert.That(server.Server.NamespaceUris.GetString(device.NodeId.NamespaceIndex), Is.EqualTo(uri));
        }

        [Test]
        public async Task ComposesIntoMachineryRegisteredFirstAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(ComposesIntoMachineryRegisteredFirstAsync),
                builder => builder
                    .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
                    .AddAssetManagement()
                    .AddLadsToMachinery()
                    .ConfigureLads(LadsIncubator.Configure)).ConfigureAwait(false);

            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);

            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Operate));
            Assert.That(
                server.Provider.GetServices<IMachineryModelProvider>().OfType<LadsModelProvider>().Count(),
                Is.EqualTo(1));
        }

        [Test]
        public async Task ComposesWithAssetManagementRegisteredBeforeMachineryAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(ComposesWithAssetManagementRegisteredBeforeMachineryAsync),
                builder => builder
                    .AddAssetManagement()
                    .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
                    .AddLadsToMachinery()
                    .ConfigureLads(LadsIncubator.Configure)).ConfigureAwait(false);

            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);

            Assert.That(device.FunctionalUnits.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task AsyncConfigurationAddsSeveralDevicesAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(AsyncConfigurationAddsSeveralDevicesAsync),
                builder => builder
                    .AddLads()
                    .ConfigureLads(async (context, ct) =>
                    {
                        LadsIncubator.Configure(context);
                        await context.AddDeviceAsync(
                            "Reader",
                            device => device.FunctionalUnit(
                                "Optics",
                                unit => unit.AnalogScalarSensor("Absorbance")),
                            ct).ConfigureAwait(false);
                    })).ConfigureAwait(false);

            ILadsDevice incubator = await server.DeviceAsync().ConfigureAwait(false);
            ILadsDevice reader = await server.DeviceAsync("Reader").ConfigureAwait(false);

            Assert.That(incubator.NodeId, Is.Not.EqualTo(reader.NodeId));
            Assert.That(
                reader.GetFunctionalUnit("Optics").GetFunction<IAnalogScalarSensorFunction>("Absorbance"),
                Is.Not.Null);
        }

        [Test]
        public async Task DuplicateDeviceNameIsRejectedAsync()
        {
            Exception? duplicate = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(DuplicateDeviceNameIsRejectedAsync),
                builder => builder
                    .AddLads()
                    .ConfigureLads(context =>
                    {
                        LadsIncubator.Configure(context);
                        try
                        {
                            context.AddDevice(LadsIncubator.DeviceName, _ => { });
                        }
                        catch (Exception ex)
                        {
                            duplicate = ex;
                        }
                    })).ConfigureAwait(false);

            await server.DeviceAsync().ConfigureAwait(false);

            Assert.That(duplicate, Is.InstanceOf<ServiceResultException>());
            Assert.That(
                ((ServiceResultException)duplicate!).StatusCode,
                Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public void DuplicateAddLadsIsRejected()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer<Opc.Ua.Server.StandardServer>(_ => { });
            builder.AddLads();

            Assert.That(() => builder.AddLads(), Throws.InvalidOperationException);
            Assert.That(() => builder.AddLadsToMachinery(), Throws.InvalidOperationException);
        }

        [Test]
        public void AddLadsToMachineryRequiresMachinery()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer<Opc.Ua.Server.StandardServer>(_ => { });

            Assert.That(() => builder.AddLadsToMachinery(), Throws.InvalidOperationException);
        }

        [Test]
        public void InvalidOptionsAreRejectedAtRegistration()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer<Opc.Ua.Server.StandardServer>(_ => { });

            Assert.That(
                () => builder.AddLads(options => options.MaxRetainedResults = 0),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer<Opc.Ua.Server.StandardServer>(_ => { });

            Assert.That(() => ((IOpcUaServerBuilder)null!).AddLads(), Throws.ArgumentNullException);
            Assert.That(() => ((IOpcUaServerBuilder)null!).AddLadsToMachinery(), Throws.ArgumentNullException);
            Assert.That(
                () => builder.ConfigureLads((Action<ILadsSetupContext>)null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.ConfigureLads(
                    (Func<ILadsSetupContext, System.Threading.CancellationToken, ValueTask>)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ProvidersAreRegisteredWithInMemoryDefaults()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer<Opc.Ua.Server.StandardServer>(_ => { }).AddLads();

            Assert.That(Implementation<ILadsDeviceProvider>(services), Is.EqualTo(typeof(InMemoryLadsDeviceProvider)));
            Assert.That(
                Implementation<ILadsFunctionProvider>(services),
                Is.EqualTo(typeof(InMemoryLadsFunctionProvider)));
            Assert.That(
                Implementation<ILadsProgramExecutor>(services),
                Is.EqualTo(typeof(InMemoryLadsProgramExecutor)));
        }

        [Test]
        public void CustomProvidersRegisteredFirstAreKept()
        {
            var services = new ServiceCollection();
            var executor = new DelegatingExecutor();
            services.AddSingleton<ILadsProgramExecutor>(executor);
            services.AddOpcUa().AddServer<Opc.Ua.Server.StandardServer>(_ => { }).AddLads();

            Assert.That(
                services.Single(service => service.ServiceType == typeof(ILadsProgramExecutor)).ImplementationInstance,
                Is.SameAs(executor));
        }

        private static Type? Implementation<T>(IServiceCollection services)
        {
            return services.Single(service => service.ServiceType == typeof(T)).ImplementationType;
        }

        private sealed class DelegatingExecutor : ILadsProgramExecutor
        {
            public ValueTask ExecuteAsync(LadsProgramRun run, System.Threading.CancellationToken cancellationToken)
            {
                return default;
            }
        }
    }
}
