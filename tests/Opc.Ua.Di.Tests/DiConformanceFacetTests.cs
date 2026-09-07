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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Di.Server.SoftwareUpdate;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Di.Tests
{
    [TestFixture]
    [Category("DI")]
    [Category("Compliance")]
    [NonParallelizable]
    public sealed class DiConformanceFacetTests
    {
        private const string StandardUa2017 =
            "http://opcfoundation.org/UA-Profile/Server/StandardUA2017";

        private const string DeviceIntegrationHost =
            "http://opcfoundation.org/UA-Profile/DI/Server/DeviceIntegrationHost";

        private const string Locking =
            "http://opcfoundation.org/UA-Profile/DI/Server/Locking";

        private const string SoftwareUpdateBase =
            "http://opcfoundation.org/UA-Profile/DI/Server/SoftwareUpdateBase";

        private const string FileSystemLoading =
            "http://opcfoundation.org/UA-Profile/DI/Server/FileSystemLoading";

        [Test]
        public async Task PlainDiServerDeclaresDeviceIntegrationHostAsync()
        {
            IReadOnlyList<string> profiles = await ReadProfilesAsync(null)
                .ConfigureAwait(false);

            Assert.That(profiles, Does.Contain(StandardUa2017));
            Assert.That(profiles, Does.Contain(DeviceIntegrationHost));
            Assert.That(profiles, Does.Not.Contain(Locking));
            Assert.That(profiles, Does.Not.Contain(SoftwareUpdateBase));
            Assert.That(profiles, Does.Not.Contain(FileSystemLoading));
        }

        [Test]
        public async Task LockingFacetIsDeclaredOnlyWhenLockServiceIsWiredAsync()
        {
            IReadOnlyList<string> withoutLock = await ReadProfilesAsync(null)
                .ConfigureAwait(false);
            IReadOnlyList<string> withLock = await ReadProfilesAsync(WireLockServiceAsync)
                .ConfigureAwait(false);

            Assert.That(withoutLock, Does.Not.Contain(Locking));
            Assert.That(withLock, Does.Contain(Locking));
        }

        [Test]
        public async Task SoftwareUpdateFacetIsDeclaredOnlyWhenPackageStoreIsWiredAsync()
        {
            IReadOnlyList<string> withoutSoftwareUpdate = await ReadProfilesAsync(null)
                .ConfigureAwait(false);
            IReadOnlyList<string> withSoftwareUpdate = await ReadProfilesAsync(WireSoftwareUpdateAsync)
                .ConfigureAwait(false);

            Assert.That(withoutSoftwareUpdate, Does.Not.Contain(SoftwareUpdateBase));
            Assert.That(withoutSoftwareUpdate, Does.Not.Contain(FileSystemLoading));
            Assert.That(withSoftwareUpdate, Does.Contain(SoftwareUpdateBase));
            Assert.That(withSoftwareUpdate, Does.Contain(FileSystemLoading));
        }

        [Test]
        public async Task ServerProfileArrayMergesStandardProfileWithDiFacetsAsync()
        {
            IReadOnlyList<string> profiles = await ReadProfilesAsync(WireSoftwareUpdateAsync)
                .ConfigureAwait(false);

            Assert.That(profiles, Does.Contain(StandardUa2017));
            Assert.That(profiles, Does.Contain(DeviceIntegrationHost));
            Assert.That(profiles, Does.Contain(SoftwareUpdateBase));
            Assert.That(profiles, Does.Contain(FileSystemLoading));
            Assert.That(profiles, Does.Not.Contain(Locking));
        }

        private static async ValueTask WireSoftwareUpdateAsync(IDiPostSetupContext context)
        {
            IDeviceBuilder<DeviceState> device = await context.Manager.CreateDeviceAsync(
                new QualifiedName(
                    "SoftwareUpdateDevice",
                    context.Manager.InstanceNamespaceIndex),
                cancellationToken: context.CancellationToken).ConfigureAwait(false);

            device.WithSoftwareUpdate(new MemoryPackageStore(), su => su.UsePackageLoading());
        }

        private static async ValueTask WireLockServiceAsync(IDiPostSetupContext context)
        {
            IDeviceBuilder<DeviceState> device = await context.Manager.CreateDeviceAsync(
                new QualifiedName("LockingDevice", context.Manager.InstanceNamespaceIndex),
                cancellationToken: context.CancellationToken).ConfigureAwait(false);

            LockingServicesState lockState = CreateLockingServicesState(context.Manager, device.Device);
            lockState.BindToLockService(device.Device.NodeId, new DefaultLockService());
            device.Device.AddChild(lockState);
            await context.Manager.AddPredefinedNodeAsync(lockState, context.CancellationToken)
                .ConfigureAwait(false);
        }

        private static LockingServicesState CreateLockingServicesState(
            DiNodeManager manager,
            DeviceState device)
        {
            var browseName = new QualifiedName("Lock", manager.DiNamespaceIndex);
            var lockState = new LockingServicesState(device)
            {
                SymbolicName = "Lock",
                BrowseName = browseName,
                DisplayName = new LocalizedText("Lock"),
                ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent
            };
            lockState.NodeId = manager.SystemContext.NodeIdFactory.New(
                manager.SystemContext,
                lockState);
            lockState.InitLock = CreateInitLockMethod(manager, lockState);
            lockState.RenewLock = CreateRenewLockMethod(manager, lockState);
            lockState.ExitLock = CreateExitLockMethod(manager, lockState);
            lockState.BreakLock = CreateBreakLockMethod(manager, lockState);
            return lockState;
        }

        private static InitLockMethodState CreateInitLockMethod(
            DiNodeManager manager,
            LockingServicesState parent)
        {
            var method = new InitLockMethodState(parent);
            FinalizeMethod(manager, method, "InitLock");
            return method;
        }

        private static RenewLockMethodState CreateRenewLockMethod(
            DiNodeManager manager,
            LockingServicesState parent)
        {
            var method = new RenewLockMethodState(parent);
            FinalizeMethod(manager, method, "RenewLock");
            return method;
        }

        private static ExitLockMethodState CreateExitLockMethod(
            DiNodeManager manager,
            LockingServicesState parent)
        {
            var method = new ExitLockMethodState(parent);
            FinalizeMethod(manager, method, "ExitLock");
            return method;
        }

        private static BreakLockMethodState CreateBreakLockMethod(
            DiNodeManager manager,
            LockingServicesState parent)
        {
            var method = new BreakLockMethodState(parent);
            FinalizeMethod(manager, method, "BreakLock");
            return method;
        }

        private static void FinalizeMethod(
            DiNodeManager manager,
            MethodState method,
            string name)
        {
            method.SymbolicName = name;
            method.BrowseName = new QualifiedName(name, manager.DiNamespaceIndex);
            method.DisplayName = new LocalizedText(name);
            method.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            method.NodeId = manager.SystemContext.NodeIdFactory.New(manager.SystemContext, method);
        }

        private static async Task<IReadOnlyList<string>> ReadProfilesAsync(
            Func<IDiPostSetupContext, ValueTask>? configure)
        {
            CaptureServer.Reset();
            var services = new ServiceCollection();
            services.AddLogging();
            IOpcUaServerBuilder serverBuilder = services.AddOpcUa()
                .AddServer<CaptureServer>(ConfigureServer)
                .AddOpcUaDi();
            if (configure != null)
            {
                serverBuilder.ConfigureDevicesFor<DiNodeManager>(configure);
            }

            await using ServiceProvider provider = services.BuildServiceProvider();
            IHostedService hostedService = provider.GetServices<IHostedService>().Single();
            await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.That(
                    await WaitForAsync(
                        () => CaptureServer.StartedInstance != null,
                        TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                    Is.True,
                    "The server did not start.");

                return await ReadServerProfileArrayAsync(CaptureServer.StartedInstance!)
                    .ConfigureAwait(false);
            }
            finally
            {
                await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static async Task<IReadOnlyList<string>> ReadServerProfileArrayAsync(
            IServerInternal server)
        {
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId
                {
                    NodeId = Opc.Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray,
                    AttributeId = Attributes.Value
                }
            ];

            using var context = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.Read,
                RequestLifetime.None);

            (ArrayOf<DataValue> values, _) = await server.NodeManager.ReadAsync(
                context,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                CancellationToken.None).ConfigureAwait(false);

            return values[0].GetValue<string[]>([]).ToList();
        }

        private static void ConfigureServer(OpcUaServerOptions options)
        {
            string applicationName = nameof(DiConformanceFacetTests);
            string testRoot = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                applicationName,
                Guid.NewGuid().ToString("N"));
            options.ApplicationName = applicationName;
            options.ApplicationUri = "urn:localhost:" + applicationName;
            options.ProductUri = "urn:localhost:" + applicationName + ":product";
            options.PkiRoot = System.IO.Path.Combine(testRoot, "pki");
            options.AutoAcceptUntrustedCertificates = true;
            options.IncludeUnsecurePolicyNone = true;
            options.EndpointUrls.Clear();
            options.EndpointUrls.Add(
                "opc.tcp://localhost:" +
                GetAvailablePort().ToString(CultureInfo.InvariantCulture) +
                "/" +
                applicationName);
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            return condition();
        }

        public sealed class CaptureServer : DependencyInjectionStandardServer
        {
            public CaptureServer(
                IServiceProvider services,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base(services, telemetry, timeProvider)
            {
            }

            public static IServerInternal? StartedInstance => Volatile.Read(ref s_startedInstance);

            public static void Reset()
            {
                Volatile.Write(ref s_startedInstance, null);
            }

            protected override void OnServerStarted(IServerInternal server)
            {
                Volatile.Write(ref s_startedInstance, server);
                base.OnServerStarted(server);
            }

            private static IServerInternal? s_startedInstance;
        }
    }
}
