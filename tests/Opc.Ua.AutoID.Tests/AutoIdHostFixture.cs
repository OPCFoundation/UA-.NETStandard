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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// A hosted OPC 30010 server with one reader of each kind, backed by the simulated providers,
    /// reachable over opc.tcp on a port the operating system assigned.
    /// </summary>
    internal sealed class AutoIdHostFixture : IAsyncDisposable
    {
        public const string RfidName = "Gate1";
        public const string OcrName = "Ocr1";
        public const string OpticalName = "Optical1";
        public const string VerifierName = "Verifier1";
        public const string RtlsName = "Rtls1";

        public InMemoryRfidReaderProvider Rfid { get; } = new(TimeSpan.FromMilliseconds(5));

        public InMemoryScanReaderProvider Ocr { get; } =
            new(AutoIdReaderKind.Ocr, TimeSpan.FromMilliseconds(5));

        public InMemoryScanReaderProvider Optical { get; } =
            new(AutoIdReaderKind.Optical, TimeSpan.FromMilliseconds(5));

        public InMemoryOpticalVerifierProvider Verifier { get; } = new(TimeSpan.FromMilliseconds(5));

        public InMemoryRtlsProvider Rtls { get; } = new(cycleInterval: TimeSpan.FromMilliseconds(5));

        public AutoIdReader RfidReader { get; private set; } = null!;

        public int SynchronousConfiguratorSawReaders { get; private set; } = -1;

        public AutoIdRuntime Runtime { get; private set; } = null!;

        public AutoIdNodeManager Manager { get; private set; } = null!;

        public string EndpointUrl { get; private set; } = string.Empty;

        public static async Task<AutoIdHostFixture> StartAsync(Action<AutoIdServerOptions>? configure = null)
        {
            var fixture = new AutoIdHostFixture();
            await fixture.StartCoreAsync(configure).ConfigureAwait(false);
            return fixture;
        }

        /// <summary>
        /// Opens a managed session to the server.
        /// </summary>
        public async Task<ManagedSession> ConnectAsync(string name)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var clientFixture = new ClientFixture(telemetry);
            await clientFixture.LoadClientConfigurationAsync(
                System.IO.Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "autoid-client",
                    Guid.NewGuid().ToString("N")[..8]),
                name).ConfigureAwait(false);
            ApplicationConfiguration configuration = clientFixture.Config;
            EndpointDescription? endpoint = await CoreClientUtils.SelectEndpointAsync(
                configuration,
                EndpointUrl,
                useSecurity: false,
                discoverTimeout: 15000,
                telemetry).ConfigureAwait(false);
            Assert.That(endpoint, Is.Not.Null);
            return await new ManagedSessionBuilder(configuration, telemetry)
                .UseEndpoint(new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(configuration)))
                .WithSessionName(name)
                .ConnectAsync().ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (m_host != null)
            {
                await m_host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            if (m_provider != null)
            {
                await m_provider.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async Task StartCoreAsync(Action<AutoIdServerOptions>? configure)
        {
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer<StandardServer>(ConfigureServer)
                .AddAutoId(options =>
                {
                    options.MaximumScanDuration = TimeSpan.FromSeconds(5);
                    configure?.Invoke(options);
                })
                .ConfigureAutoId(async context =>
                {
                    Manager = context.Manager;
                    Runtime = context.Runtime;
                    RfidReader = await Runtime.Reader(Name(RfidName), Rfid)
                        .WithDeviceInfo("Dock door reader")
                        .WithDeviceLocationName("Gate 1")
                        .WithDeviceLocation(new Location
                        {
                            SwitchField = LocationFields.Name,
                            Name = "Dock 1"
                        })
                        .WithAntennaNames([new AntennaNameIdPair { AntennaId = 1, AntennaName = "North" }])
                        .WithIdentification(identification =>
                        {
                            identification.Manufacturer = new LocalizedText("Acme");
                            identification.Model = new LocalizedText("R-100");
                            identification.SerialNumber = "SN-1";
                        })
                        .BuildAsync(context.CancellationToken)
                        .ConfigureAwait(false);
                    await Runtime.CreateReaderAsync(Name(OcrName), Ocr, context.CancellationToken).ConfigureAwait(false);
                    await Runtime.CreateReaderAsync(Name(OpticalName), Optical, context.CancellationToken).ConfigureAwait(false);
                    await Runtime.CreateReaderAsync(Name(VerifierName), Verifier, context.CancellationToken).ConfigureAwait(false);
                    await Runtime.CreateReaderAsync(Name(RtlsName), Rtls, context.CancellationToken).ConfigureAwait(false);
                    configured.TrySetResult(true);
                })
                .ConfigureAutoId(context =>
                    // The synchronous overload runs after the asynchronous one, in registration order.
                    SynchronousConfiguratorSawReaders = context.Manager.Readers.Count);

            m_provider = services.BuildServiceProvider();
            m_host = m_provider.GetServices<IHostedService>().Single();
            await m_host.StartAsync(CancellationToken.None).ConfigureAwait(false);
            for (int ii = 0; ii < 300 && !configured.Task.IsCompleted; ii++)
            {
                if (m_host is BackgroundService { ExecuteTask: { IsFaulted: true } startup })
                {
                    await startup.ConfigureAwait(false);
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            Assert.That(configured.Task.IsCompleted, Is.True, "the AutoID configurator completed");
        }

        private QualifiedName Name(string name)
        {
            return new QualifiedName(name, Manager.InstanceNamespaceIndex);
        }

        private void ConfigureServer(OpcUaServerOptions options)
        {
            const string applicationName = "AutoIdHost";
            string testRoot = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "autoid",
                Guid.NewGuid().ToString("N")[..8]);
            options.ApplicationName = applicationName;
            options.ApplicationUri = "urn:localhost:" + applicationName;
            options.ProductUri = "urn:localhost:" + applicationName + ":product";
            options.PkiRoot = System.IO.Path.Combine(testRoot, "pki");
            options.AutoAcceptUntrustedCertificates = true;
            options.IncludeUnsecurePolicyNone = true;
            options.EndpointUrls.Clear();
            EndpointUrl = "opc.tcp://localhost:" +
                GetAvailablePort().ToString(CultureInfo.InvariantCulture) +
                "/" +
                applicationName;
            options.EndpointUrls.Add(EndpointUrl);
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

        private ServiceProvider? m_provider;
        private IHostedService? m_host;
    }
}
