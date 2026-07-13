/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Client;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT smoke tests for the unified <c>services.AddOpcUa()</c> DI surface
    /// exposed by Opc.Ua.Core, Opc.Ua.Server and Opc.Ua.Client.
    /// </summary>
    /// <remarks>
    /// These tests must only exercise the <c>Action&lt;TOptions&gt;</c>
    /// overloads of <see cref="OpcUaServerBuilderExtensions"/> and
    /// <see cref="OpcUaClientBuilderExtensions"/>. The
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
    /// overloads are decorated with <c>RequiresUnreferencedCode</c> /
    /// <c>RequiresDynamicCode</c> and are not AOT safe; if a future
    /// refactor causes any of those overloads to leak into this test
    /// the AOT publish will produce IL2026/IL3050 warnings attributable
    /// to this file.
    /// </remarks>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class HostingAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task AddOpcUaCanRegisterRootServicesWithoutAotWarningAsync()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            await Assert.That(builder).IsNotNull();
            await Assert.That(builder.Services).IsSameReferenceAs(services);

            using ServiceProvider sp = services.BuildServiceProvider();

            ITelemetryContext telemetry = sp.GetService<ITelemetryContext>();
            await Assert.That(telemetry).IsNotNull();
        }

        [Test]
        public async Task AddServerActionOverloadIsAotSafeAsync()
        {
            const string expectedName = "AotSmokeServer";
            const string expectedUri = "urn:localhost:OPCFoundation:AotSmokeServer";

            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = expectedName;
                    o.ApplicationUri = expectedUri;
                    o.AutoAcceptUntrustedCertificates = true;
                });

            using ServiceProvider sp = services.BuildServiceProvider();

            IOptions<OpcUaServerOptions> options =
                sp.GetService<IOptions<OpcUaServerOptions>>();
            await Assert.That(options).IsNotNull();

            OpcUaServerOptions resolved = options.Value;
            await Assert.That(resolved).IsNotNull();
            await Assert.That(resolved.ApplicationName).IsEqualTo(expectedName);
            await Assert.That(resolved.ApplicationUri).IsEqualTo(expectedUri);
            await Assert.That(resolved.AutoAcceptUntrustedCertificates).IsTrue();

            // The hosted service is registered via the AOT-safe
            // AddHostedService<T>() overload. Resolve it via the
            // ServiceDescriptor collection rather than constructing it,
            // because the production constructor requires an
            // IApplicationInstanceFactory which is contributed by a
            // separate Opc.Ua.Configuration registration that callers
            // wire up explicitly.
            int hostedCount = services.Count(s =>
                s.ServiceType == typeof(IHostedService) &&
                s.ImplementationType?.Name == "OpcUaServerHostedService");
            await Assert.That(hostedCount).IsEqualTo(1);
        }

        [Test]
        public async Task AddClientActionOverloadIsAotSafeAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                b => b.SetMinimumLevel(LogLevel.Warning));
            ApplicationConfiguration configuration = CreateMinimalClientConfiguration(telemetry);

            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(o => o.Configuration = configuration);

            using ServiceProvider sp = services.BuildServiceProvider();

            OpcUaClientOptions options = sp.GetService<OpcUaClientOptions>();
            await Assert.That(options).IsNotNull();
            await Assert.That(options.Configuration).IsSameReferenceAs(configuration);

            ISessionFactory sessionFactory = sp.GetService<ISessionFactory>();
            await Assert.That(sessionFactory).IsNotNull();

            ManagedSessionFactory managedFactory = sp.GetService<ManagedSessionFactory>();
            await Assert.That(managedFactory).IsNotNull();

            Func<CancellationToken, Task<ManagedSession>> sessionAccessor =
                sp.GetService<Func<CancellationToken, Task<ManagedSession>>>();
            await Assert.That(sessionAccessor).IsNotNull();
        }

        [Test]
        public async Task CombinedServerAndClientRegistrationIsAotSafeAsync()
        {
            const string serverName = "AotSmokeCombinedServer";

            ITelemetryContext telemetry = DefaultTelemetry.Create(
                b => b.SetMinimumLevel(LogLevel.Warning));
            ApplicationConfiguration clientConfiguration =
                CreateMinimalClientConfiguration(telemetry);

            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = serverName;
                    o.AutoAcceptUntrustedCertificates = true;
                })
                .Services.AddOpcUa()
                .AddClient(o => o.Configuration = clientConfiguration);

            using ServiceProvider sp = services.BuildServiceProvider();

            OpcUaServerOptions serverOptions =
                sp.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
            await Assert.That(serverOptions.ApplicationName).IsEqualTo(serverName);

            OpcUaClientOptions clientOptions = sp.GetService<OpcUaClientOptions>();
            await Assert.That(clientOptions).IsNotNull();
            await Assert.That(clientOptions.Configuration)
                .IsSameReferenceAs(clientConfiguration);

            int hostedCount = services.Count(s =>
                s.ServiceType == typeof(IHostedService) &&
                s.ImplementationType?.Name == "OpcUaServerHostedService");
            await Assert.That(hostedCount).IsEqualTo(1);

            Func<CancellationToken, Task<ManagedSession>> sessionAccessor =
                sp.GetService<Func<CancellationToken, Task<ManagedSession>>>();
            await Assert.That(sessionAccessor).IsNotNull();
        }

        [Test]
        public async Task AddRuntimeNodeSetStreamRegistrationIsAotSafeAsync()
        {
            string namespaceUri = fixture.ServerFixture.Server.CurrentInstance
                .NamespaceUris.GetString(1);
            string nodeSetXml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">" +
                "<NamespaceUris><Uri>" + namespaceUri + "</Uri></NamespaceUris>" +
                "<Models><Model ModelUri=\"" + namespaceUri + "\" /></Models>" +
                "<UAObject NodeId=\"ns=1;i=1\" BrowseName=\"1:Root\">" +
                "<DisplayName>Root</DisplayName></UAObject>" +
                "</UANodeSet>";

            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = "AotRuntimeNodeSetServer";
                    o.AutoAcceptUntrustedCertificates = true;
                })
                .AddRuntimeNodeSet(options =>
                {
                    options.Sources =
                    [
                        RuntimeNodeSetSource.FromStream(
                            "AOT runtime NodeSet",
                            _ => new ValueTask<Stream>(
                                new MemoryStream(Encoding.UTF8.GetBytes(nodeSetXml))),
                            [namespaceUri])
                    ];
                });

            using ServiceProvider sp = services.BuildServiceProvider();
            IAsyncNodeManagerFactory factory = sp.GetService<IAsyncNodeManagerFactory>();

            await Assert.That(factory).IsNotNull();
            await Assert.That(factory.NamespacesUris.Count).IsEqualTo(1);
            await Assert.That(factory.NamespacesUris[0]).IsEqualTo(namespaceUri);

            IAsyncNodeManager manager = await factory.CreateAsync(
                fixture.ServerFixture.Server.CurrentInstance,
                fixture.ServerFixture.Config,
                CancellationToken.None).ConfigureAwait(false);

            try
            {
                await manager.CreateAddressSpaceAsync(
                    new Dictionary<NodeId, IList<IReference>>(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                (manager as IDisposable)?.Dispose();
            }
        }

        private static ApplicationConfiguration CreateMinimalClientConfiguration(
            ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "AotSmokeClient",
                ApplicationUri = "urn:localhost:OPCFoundation:AotSmokeClient",
                ApplicationType = ApplicationType.Client,
                ClientConfiguration = new ClientConfiguration()
            };
        }
    }
}
