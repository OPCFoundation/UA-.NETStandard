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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Configuration;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Exercises the default hosted server through real reverse-connect discovery and session channels.
    /// </summary>
    [TestFixture]
    [Category("Hosting")]
    [Category("ReverseConnect")]
    [NonParallelizable]
    public sealed class HostedReverseConnectIntegrationTests
    {
        [TestCase("options")]
        [TestCase("file")]
        [TestCase("stream")]
        public async Task DefaultHostedServerSupportsReverseSessionsWithDiHooksAsync(string configurationSource)
        {
            string root = Path.Combine(Path.GetTempPath(), "opc4410", Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(isServer: true);
            var client = new ClientFixture(telemetry);
            Stream configurationStream = null;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await client.LoadClientConfigurationAsync(Path.Combine(root, "client")).ConfigureAwait(false);
                client.Config.ClientConfiguration!.ReverseConnect = new ReverseConnectClientConfiguration
                {
                    HoldTime = 5000
                };
                var clientUrl = new Uri(
                    "opc.tcp://localhost:" +
                    ServerFixtureUtils.GetNextFreeIPPort().ToString(CultureInfo.InvariantCulture));
                client.ReverseConnectManager.AddEndpoint(clientUrl, client.Config);
                await client.ReverseConnectManager.StartServiceAsync(client.Config).ConfigureAwait(false);

                string serverUrl = "opc.tcp://localhost:" +
                    ServerFixtureUtils.GetNextFreeIPPort().ToString(CultureInfo.InvariantCulture) + "/HostedReverse";
                var started = new TaskCompletionSource<IServerContext>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton(telemetry);
                IOpcUaBuilder builder = services.AddOpcUa();
                IOpcUaServerBuilder serverBuilder;
                if (configurationSource == "options")
                {
                    serverBuilder = builder.AddServer(options =>
                    {
                        options.ApplicationName = kApplicationName;
                        options.ApplicationUri = kApplicationUri;
                        options.ProductUri = kProductUri;
                        options.PkiRoot = Path.Combine(root, "server");
                        options.EndpointUrls.Add(serverUrl);
                        options.IncludeSignAndEncryptPolicies = false;
                        options.IncludeUnsecurePolicyNone = true;
                        options.ReverseConnect = new ServerReverseConnectOptions
                        {
                            ConnectIntervalMs = 100,
                            ConnectTimeoutMs = 10000
                        };
                        options.ReverseConnect.Clients.Add(new ServerReverseConnectClientOptions
                        {
                            EndpointUrl = clientUrl.AbsoluteUri,
                            Timeout = 10000,
                            MaxSessionCount = 1
                        });
                    });
                }
                else
                {
                    string path = await WriteConfigurationAsync(
                        root, serverUrl, clientUrl, telemetry, cancellation.Token).ConfigureAwait(false);
                    if (configurationSource == "file")
                    {
                        serverBuilder = builder.AddServer(path);
                    }
                    else
                    {
                        configurationStream = File.OpenRead(path);
                        serverBuilder = builder.AddServer(configurationStream);
                    }
                }

                serverBuilder
                    .ConfigureRoles(_ => { })
                    .ConfigureServerProperties(properties => properties.ManufacturerName = "Hosting integration test")
                    .AddStartupTask((_, context, _) =>
                    {
                        started.TrySetResult(context);
                        return default;
                    });
                ServiceProvider provider = services.BuildServiceProvider();
                var hosted = (BackgroundService)provider.GetServices<IHostedService>().Single();
                try
                {
                    await hosted.StartAsync(cancellation.Token).ConfigureAwait(false);
                    Task completed = await Task.WhenAny(
                        started.Task,
                        hosted.ExecuteTask!,
                        Task.Delay(Timeout.Infinite, cancellation.Token)).ConfigureAwait(false);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (completed == hosted.ExecuteTask)
                    {
                        await hosted.ExecuteTask.ConfigureAwait(false);
                        Assert.Fail("The hosted server stopped before completing startup.");
                    }
                    IServerContext context = await started.Task.ConfigureAwait(false);
                    Assert.That(context.CurrentState, Is.EqualTo(ServerState.Running));
                    Assert.That(context.FindNodeManagers<IDiagnosticsNodeManager>().Count(), Is.EqualTo(1));

                    var expectedEndpoint = new Uri(Utils.ReplaceLocalhost(serverUrl));
                    ITransportWaitingConnection discoveryConnection =
                        await client.ReverseConnectManager.WaitForConnectionAsync(
                            expectedEndpoint, null, cancellation.Token).ConfigureAwait(false);
                    EndpointDescription description = await CoreClientUtils.SelectEndpointAsync(
                        client.Config, discoveryConnection, false, 10000, telemetry).ConfigureAwait(false);
                    var endpoint = new ConfiguredEndpoint(
                        null, description, EndpointConfiguration.Create(client.Config));
                    ITransportWaitingConnection sessionConnection =
                        await client.ReverseConnectManager.WaitForConnectionAsync(
                            expectedEndpoint, null, cancellation.Token).ConfigureAwait(false);
                    using Client.ISession session = await client.SessionFactory.CreateAsync(
                        client.Config,
                        sessionConnection,
                        endpoint,
                        false,
                        false,
                        "Hosted reverse-connect integration",
                        30000,
                        new UserIdentity(),
                        default,
                        cancellation.Token).ConfigureAwait(false);
                    try
                    {
                        DataValue value = await session.ReadValueAsync(
                            VariableIds.Server_ServerStatus_BuildInfo_ProductName,
                            cancellation.Token).ConfigureAwait(false);
                        Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(value.WrappedValue.TryGetValue(out string productName), Is.True);
                        Assert.That(productName, Is.EqualTo(kApplicationName));
                    }
                    finally
                    {
                        await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    cancellation.Cancel();
                    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    try
                    {
                        await hosted.StopAsync(shutdown.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        await provider.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                cancellation.Cancel();
                await client.DisposeAsync().ConfigureAwait(false);
                configurationStream?.Dispose();
                Directory.Delete(root, recursive: true);
            }
        }

        private static async Task<string> WriteConfigurationAsync(
            string root,
            string serverUrl,
            Uri clientUrl,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            var application = new ApplicationInstance(telemetry) { ApplicationName = kApplicationName };
            try
            {
                string pkiRoot = Path.Combine(root, "server");
                ApplicationConfiguration configuration = await application
                    .Build(kApplicationUri, kProductUri)
                    .AsServer([serverUrl])
                    .AddUnsecurePolicyNone()
                    .SetReverseConnect(new ReverseConnectServerConfiguration
                    {
                        ConnectInterval = 100,
                        ConnectTimeout = 10000,
                        Clients =
                        [
                            new ReverseConnectClient
                            {
                                EndpointUrl = clientUrl.AbsoluteUri,
                                Timeout = 10000,
                                MaxSessionCount = 1,
                                Enabled = true
                            }
                        ]
                    })
                    .AddSecurityConfiguration(
                        ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                            "CN=" + kApplicationName, CertificateStoreType.Directory, pkiRoot),
                        pkiRoot)
                    .CreateAsync(ct).ConfigureAwait(false);
                string path = Path.Combine(root, "server.xml");
                configuration.SaveToFile(path);
                return path;
            }
            finally
            {
                await application.DisposeAsync().ConfigureAwait(false);
            }
        }

        private const string kApplicationName = "HostedReverse";
        private const string kApplicationUri = "urn:opcfoundation:test:hosted-reverse";
        private const string kProductUri = "urn:opcfoundation:test:hosted-reverse:product";
    }
}
