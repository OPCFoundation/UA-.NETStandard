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
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Hosting;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Server.ThingDescriptions;
using Opc.Ua.WotCon.Tests.Providers;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests.Hosting
{
    [TestFixture]
    [Category("WotCon")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class WotConfiguredOptionsTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConnectionTestEnforcesConfiguredEndpointPolicy(bool useDependencyInjection)
        {
            await using ConfiguredServer server = await ConfiguredServer.StartAsync(
                useDependencyInjection,
                options =>
                {
                    options.AssetEndpointPolicy.AllowedSchemes.Add("sim");
                    options.AssetEndpointPolicy.AllowedHosts.Add("opcua.test");
                }).ConfigureAwait(false);

            Assert.That(
                () => server.Client.ConnectionTestAsync("https://unlisted.example/device").AsTask(),
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(
                () => server.Client.CreateAssetForEndpointAsync("Blocked", "https://unlisted.example/device").AsTask(),
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));

            (bool success, string status) = await server.Client
                .ConnectionTestAsync(SimulatedWotDiscoveryProvider.CannedEndpoint)
                .ConfigureAwait(false);

            Assert.That(success, Is.True);
            Assert.That(status, Is.EqualTo("Healthy"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CreatedAssetIsUsableThroughTheGeneratedManagementClient(bool useDependencyInjection)
        {
            await using ConfiguredServer server = await ConfiguredServer
                .StartAsync(useDependencyInjection, _ => { })
                .ConfigureAwait(false);

            WotAssetClient asset = await server.Client.CreateAssetAsync("Pump01").ConfigureAwait(false);

            Assert.That(asset.Name, Is.EqualTo("Pump01"));
            Assert.That(asset.File.ObjectId.IsNull, Is.False);
            Assert.That(
                server.Client.Session.NamespaceUris.GetString(asset.File.ObjectId.NamespaceIndex),
                Is.EqualTo(WotConnectivityServerOptions.DefaultAssetNamespaceUri));

            await server.Client.DeleteAssetAsync(asset.AssetId).ConfigureAwait(false);

            var remaining = new List<WotAssetEntry>();
            await foreach (WotAssetEntry entry in server.Client.EnumerateAssetsAsync().ConfigureAwait(false))
            {
                remaining.Add(entry);
            }
            Assert.That(remaining, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GeneratedDiscoveryMethodReachesTheConfiguredProvider(bool useDependencyInjection)
        {
            await using ConfiguredServer server = await ConfiguredServer.StartAsync(
                useDependencyInjection,
                options => options.AssetEndpointPolicy.AllowedSchemes.Add("sim")).ConfigureAwait(false);

            IReadOnlyList<string> endpoints = await server.Client.DiscoverAssetsAsync().ConfigureAwait(false);

            Assert.That(endpoints, Has.Count.EqualTo(1));
            Assert.That(endpoints[0], Is.EqualTo(SimulatedWotDiscoveryProvider.CannedEndpoint));
        }

        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        public async Task StartupEnforcesConfiguredFileCount(bool useDependencyInjection, int limit)
        {
            await using ConfiguredServer server = await ConfiguredServer.StartAsync(
                useDependencyInjection,
                options => options.MaxPersistedThingDescriptionFiles = limit,
                async folder =>
                {
                    await WriteDescriptionAsync(folder, "First").ConfigureAwait(false);
                    await WriteDescriptionAsync(folder, "Second").ConfigureAwait(false);
                }).ConfigureAwait(false);

            var assets = new List<WotAssetEntry>();
            await foreach (WotAssetEntry asset in server.Client.EnumerateAssetsAsync().ConfigureAwait(false))
            {
                assets.Add(asset);
            }

            Assert.That(assets, Has.Count.EqualTo(limit));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StartupEnforcesConfiguredJsonDepth(bool useDependencyInjection)
        {
            await using ConfiguredServer server = await ConfiguredServer.StartAsync(
                useDependencyInjection,
                options => options.MaxThingDescriptionJsonDepth = 8,
                async folder =>
                {
                    await WriteDescriptionAsync(folder, "Shallow").ConfigureAwait(false);
                    await WriteDescriptionAsync(folder, "Deep", depth: 16).ConfigureAwait(false);
                }).ConfigureAwait(false);

            var names = new List<string>();
            await foreach (WotAssetEntry asset in server.Client.EnumerateAssetsAsync().ConfigureAwait(false))
            {
                names.Add(asset.Name);
            }

            Assert.That(names, Has.Count.EqualTo(1));
            Assert.That(names[0], Is.EqualTo("Shallow"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RestoredAssetFileRetainsCanonicalDocumentBytes(bool includeByteOrderMark)
        {
            ByteString expected = default;
            await using ConfiguredServer server = await ConfiguredServer.StartAsync(
                useDependencyInjection: true,
                _ => { },
                async folder =>
                {
                    expected = await WriteDescriptionAsync(
                        folder, "Restored", includeByteOrderMark: includeByteOrderMark).ConfigureAwait(false);
                }).ConfigureAwait(false);

            var assets = new List<WotAssetEntry>();
            await foreach (WotAssetEntry entry in server.Client.EnumerateAssetsAsync().ConfigureAwait(false))
            {
                assets.Add(entry);
            }
            Assert.That(assets, Has.Count.EqualTo(1));

            WotAssetClient asset = await server.Client.OpenAssetAsync(assets[0].AssetId).ConfigureAwait(false);
            byte[] actual = await asset.File.DownloadAllAsync().ConfigureAwait(false);

            Assert.That(expected.IsEmpty, Is.False);
            Assert.That(ByteString.From(actual), Is.EqualTo(expected));
        }

        [Test]
        public async Task DiscoveredAssetFileContainsTheGeneratedDescription()
        {
            await using ConfiguredServer server = await ConfiguredServer.StartAsync(
                useDependencyInjection: true,
                options => options.AssetEndpointPolicy.AllowedSchemes.Add("sim")).ConfigureAwait(false);

            WotAssetClient asset = await server.Client.CreateAssetForEndpointAsync(
                "Discovered",
                SimulatedWotDiscoveryProvider.CannedEndpoint).ConfigureAwait(false);
            byte[] actual = await asset.File.DownloadAllAsync().ConfigureAwait(false);

            Assert.That(actual, Is.Not.Empty);
            using JsonDocument document = JsonDocument.Parse(actual);
            Assert.That(document.RootElement.GetProperty("name").GetString(), Is.EqualTo("Discovered"));
            Assert.That(
                document.RootElement.GetProperty("base").GetString(),
                Is.EqualTo(SimulatedWotDiscoveryProvider.CannedEndpoint));
            Assert.That(
                document.RootElement.GetProperty("properties").GetProperty("Voltage")
                    .GetProperty("type").GetString(),
                Is.EqualTo("number"));
        }

        [Test]
        public async Task DeletingMixedCaseAssetRemovesAssignedRegistryResource()
        {
            using var registry = new WotRegistryService();
            await registry.GetOrCreateGroupAsync(
                WotRegistryGroups.ThingDescriptions,
                WoTDocumentKindEnum.ThingDescription).ConfigureAwait(false);
            await using ConfiguredServer server = await ConfiguredServer.StartAsync(
                useDependencyInjection: true,
                options => options.RegistryBridge = registry).ConfigureAwait(false);

            WotAssetClient asset = await server.Client.CreateAssetAsync("Pump01").ConfigureAwait(false);
            await asset.UploadThingDescriptionAsync(CreateDescriptionBytes("Pump01")).ConfigureAwait(false);
            WotResourceGroup group = registry.Current.Groups[WotRegistryGroups.ThingDescriptions];
            Assert.That(group.Resources, Has.Count.EqualTo(1));
            WotResource mirrored = group.Resources.Values.Single();

            await server.Client.DeleteAssetAsync(asset.AssetId).ConfigureAwait(false);

            Assert.That(registry.Current.FindResource(group.GroupId, mirrored.ResourceId), Is.Null);
        }

        private static async Task<ByteString> WriteDescriptionAsync(
            string folder,
            string name,
            int depth = 0,
            bool includeByteOrderMark = false)
        {
            byte[] bytes = CreateDescriptionBytes(name, depth, includeByteOrderMark);
            using var stream = new FileStream(
                Path.Combine(folder, name + ".jsonld"),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);
#if NETSTANDARD2_1_OR_GREATER || NET
            await stream.WriteAsync(bytes.AsMemory()).ConfigureAwait(false);
#else
            await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
#endif
            await stream.FlushAsync().ConfigureAwait(false);
            return ByteString.From(bytes);
        }

        private static byte[] CreateDescriptionBytes(
            string name,
            int depth = 0,
            bool includeByteOrderMark = false)
        {
            string value = "0";
            for (int i = 0; i < depth; i++)
            {
                value = "{\"nested\":" + value + "}";
            }
            string json = $$"""
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "@type": "Thing",
                  "id": "urn:configured-options:{{name}}",
                  "name": "{{name}}",
                  "title": "{{name}}",
                  "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
                  "security": [ "nosec_sc" ],
                  "properties": {
                    "Value": {
                      "type": "number",
                      "readOnly": true,
                      "forms": [ { "href": "https://device.example/value", "op": "readproperty" } ]
                    }
                  },
                  "urn:configured-options:metadata": {{value}}
                }
                """;
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            if (includeByteOrderMark)
            {
                bytes = [0xEF, 0xBB, 0xBF, .. bytes];
            }
            return bytes;
        }

        private sealed class ConfiguredServer : IAsyncDisposable
        {
            private ConfiguredServer()
            {
                m_directory = Path.Combine(Path.GetTempPath(), "wot-options-" + Guid.NewGuid().ToString("N"));
                m_telemetry = NUnitTelemetryContext.Create();
                m_fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
                {
                    UriScheme = Utils.UriSchemeOpcTcp,
                    SecurityNone = true,
                    AutoAccept = true
                };
            }

            public WotConnectivityClient Client { get; private set; } = null!;

            public static async Task<ConfiguredServer> StartAsync(
                bool useDependencyInjection,
                Action<WotConnectivityServerOptions> configure,
                Func<string, Task>? prepareDocuments = null)
            {
                var result = new ConfiguredServer();
                bool started = false;
                try
                {
                    if (prepareDocuments is not null)
                    {
                        string folder = Path.Combine(result.m_directory, "documents");
                        Directory.CreateDirectory(folder);
                        await prepareDocuments(folder).ConfigureAwait(false);
                    }
                    await result.InitializeAsync(useDependencyInjection, configure).ConfigureAwait(false);
                    started = true;
                    return result;
                }
                finally
                {
                    if (!started)
                    {
                        await result.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    if (m_session is not null)
                    {
                        await m_session.CloseAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_session?.Dispose();
                    m_clientFixture?.Dispose();
                    try
                    {
                        await m_fixture.StopAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        m_server?.Dispose();
                        m_services?.Dispose();
                        if (Directory.Exists(m_directory))
                        {
                            Directory.Delete(m_directory, recursive: true);
                        }
                    }
                }
            }

            private async Task InitializeAsync(
                bool useDependencyInjection,
                Action<WotConnectivityServerOptions> configure)
            {
                WotConnectivityNodeManagerFactory factory;
                if (useDependencyInjection)
                {
                    var services = new ServiceCollection();
                    services.AddLogging();
                    services.AddOpcUa().AddWotConServer(options =>
                    {
                        Configure(options);
                        configure(options);
                    });
                    m_services = services.BuildServiceProvider();
                    factory = m_services.GetRequiredService<WotConnectivityNodeManagerFactory>();
                }
                else
                {
                    var options = new WotConnectivityServerOptions();
                    Configure(options);
                    configure(options);
                    factory = new WotConnectivityNodeManagerFactory(options);
                }

                m_server = await m_fixture.StartAsync(m_directory).ConfigureAwait(false);
                await m_server.NodeManagerLifecycle.AddAsync(factory, callerContext: null).ConfigureAwait(false);
                m_clientFixture = new ClientFixture(false, false, m_telemetry);
                await m_clientFixture.LoadClientConfigurationAsync(m_directory).ConfigureAwait(false);
                var endpoint = new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}");
                m_session = await m_clientFixture.ConnectAsync(endpoint, SecurityPolicies.None).ConfigureAwait(false);
                Client = await WotConnectivityClient.ForServerAsync(m_session, m_telemetry).ConfigureAwait(false);
            }

            private void Configure(WotConnectivityServerOptions options)
            {
                options.ThingDescriptionStorageFolder = Path.Combine(m_directory, "documents");
                options.Discovery = new SimulatedWotDiscoveryProvider();
                var factory = new Mock<IWotAssetProviderFactory>(MockBehavior.Strict);
                factory.SetupGet(provider => provider.SupportedBindings).Returns(s_supportedBindings);
                factory.Setup(provider => provider.CanHandle(It.IsAny<ThingDescription>())).Returns(true);
                factory.Setup(provider => provider.ConnectAsync(
                        It.IsAny<ThingDescription>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((ThingDescription document, CancellationToken _) =>
                        new ValueTask<IWotAssetProvider>(new SimulatedWotAssetProvider(document)));
                options.Bindings.Add(factory.Object);
                options.ManagementAccess = new WotManagementAccessPolicy
                {
                    MinimumSecurityMode = MessageSecurityMode.None,
                    AllowAnonymous = true,
                    RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                };
            }

            private readonly string m_directory;
            private readonly ITelemetryContext m_telemetry;
            private readonly ServerFixture<ReferenceServer> m_fixture;
            private ServiceProvider? m_services;
            private ReferenceServer? m_server;
            private ClientFixture? m_clientFixture;
            private ISession? m_session;
            private static readonly string[] s_supportedBindings = ["sim"];
        }
    }
}
