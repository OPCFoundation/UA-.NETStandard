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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Client.Discovery;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    [TestFixture]
    [Category("Client")]
    [Category("ClientBuilder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ClientInfrastructureCoverageTests
    {
        [Test]
        public void HttpsTransportChannelBindingsRejectsNullDependencies()
        {
            var inner = new Mock<ITransportChannelBindings>();
            var httpClientFactory = new Mock<IOpcUaHttpClientFactory>();

            Assert.That(
                () => new HttpsTransportChannelBindings(null!, httpClientFactory.Object),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("inner"));
            Assert.That(
                () => new HttpsTransportChannelBindings(inner.Object, null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("httpClientFactory"));
        }

        [Test]
        public void HttpsTransportChannelBindingsRoutesKnownSchemesAndDelegatesUnknown()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fallbackChannel = new Mock<ITransportChannel>();
            var inner = new Mock<ITransportChannelBindings>();
            inner
                .Setup(b => b.Create("opc.tcp", telemetry))
                .Returns(fallbackChannel.Object);
            var httpClientFactory = new Mock<IOpcUaHttpClientFactory>();
            httpClientFactory
                .Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(() => new HttpClient());
            var bindings = new HttpsTransportChannelBindings(inner.Object, httpClientFactory.Object);

            ITransportChannel? https = bindings.Create(Utils.UriSchemeHttps, telemetry);
            ITransportChannel? opcHttps = bindings.Create(Utils.UriSchemeOpcHttps, telemetry);
            ITransportChannel? fallback = bindings.Create("opc.tcp", telemetry);

            Assert.That(https, Is.Not.Null);
            Assert.That(opcHttps, Is.Not.Null);
            Assert.That(fallback, Is.SameAs(fallbackChannel.Object));
            inner.Verify(b => b.Create("opc.tcp", telemetry), Times.Once);
        }

        [Test]
        public void OpcUaDiscoveryServiceRejectsNullConstructorArguments()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            Assert.That(
                () => new OpcUaDiscoveryService(null!, telemetry),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("options"));
            Assert.That(
                () => new OpcUaDiscoveryService(new OpcUaClientOptions(), null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("telemetry"));
        }

        [Test]
        public void OpcUaDiscoveryServiceValidatesArgumentsBeforeCreatingClient()
        {
            var service = new OpcUaDiscoveryService(new OpcUaClientOptions(), NUnitTelemetryContext.Create());

            Assert.That(
                async () => await service.FindServersAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("discoveryUrl"));
            Assert.That(
                async () => await service.GetEndpointsAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("discoveryUrl"));
            Assert.That(
                async () => await service.FindServersAsync("opc.tcp://localhost:4840").ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.EqualTo("OpcUaClientOptions.Configuration is required."));
            Assert.That(
                async () => await service.GetEndpointsAsync("opc.tcp://localhost:4840").ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.EqualTo("OpcUaClientOptions.Configuration is required."));
        }

        [Test]
        public void SubClientFactoriesRejectNullTelemetryAndCreateRootedClients()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ISession session = CreateSession(telemetry);

            Assert.That(
                () => new FileTransferClientFactory(null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("telemetry"));
            Assert.That(
                () => new AliasNameClientFactory(null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("telemetry"));

            var fileFactory = new FileTransferClientFactory(telemetry);
            var aliasFactory = new AliasNameClientFactory(telemetry);
            FileSystemClient fileSystem = fileFactory.CreateFileSystem(session, ObjectIds.FileSystem);
            AliasNameClient aliasClient = aliasFactory.Create(session, ObjectIds.Aliases);

            Assert.That(fileFactory.Telemetry, Is.SameAs(telemetry));
            Assert.That(aliasFactory.Telemetry, Is.SameAs(telemetry));
            Assert.That(fileSystem.Session, Is.SameAs(session));
            Assert.That(aliasClient.Session, Is.SameAs(session));
        }

        [Test]
        public async Task ManagedSessionPoolValidatesKeysAndReportsMissingRemovalAsync()
        {
            var factory = new Mock<IManagedSessionFactory>();
            var pool = new ManagedSessionPool(factory.Object);

            Assert.That(
                () => pool.GetOrConnectAsync(string.Empty, CreateEndpoint()),
                Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("key"));
            Assert.That(
                () => pool.GetOrConnectAsync("primary", null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("endpoint"));
            Assert.That(
                () => pool.GetOrConnectAsync("primary", CreateEndpoint(), null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("configure"));
            Assert.That(
                async () => await pool.RemoveAsync(" ").ConfigureAwait(false),
                Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("key"));

            bool removed = await pool.RemoveAsync("missing").ConfigureAwait(false);

            Assert.That(removed, Is.False);
        }

        [Test]
        public void DefaultManagedSessionFactoryValidatesInputs()
        {
            using ServiceProvider provider = CreateManagedSessionServices().BuildServiceProvider();
            IManagedSessionFactory factory = provider.GetRequiredService<IManagedSessionFactory>();

            Assert.That(
                () => factory.ConnectAsync(null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("endpoint"));
            Assert.That(
                () => factory.ConnectAsync(CreateEndpoint(), null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("configure"));
            Assert.That(
                () => factory.ConnectReverseAsync(null!, new Uri("urn:test:server"), CreateEndpoint()),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("manager"));
            Assert.That(
                () => factory.ConnectReverseAsync(
                    new ReverseConnectManager(NUnitTelemetryContext.Create()),
                    null!,
                    CreateEndpoint()),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("serverUri"));
            Assert.That(
                () => factory.ConnectReverseAsync(
                    new ReverseConnectManager(NUnitTelemetryContext.Create()),
                    new Uri("urn:test:server"),
                    CreateEndpoint(),
                    null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("configure"));
        }

        [Test]
        public void DefaultManagedSessionFactoryConnectReverseAppliesReverseConnectConfiguration()
        {
            var connector = new InvokesConfigureConnector();
            using ServiceProvider provider = CreateManagedSessionServices(connector).BuildServiceProvider();
            IManagedSessionFactory factory = provider.GetRequiredService<IManagedSessionFactory>();
            bool configured = false;

            Assert.That(
                () => factory.ConnectReverseAsync(
                    new ReverseConnectManager(NUnitTelemetryContext.Create()),
                    new Uri("urn:test:server"),
                    CreateEndpoint(),
                    _ => configured = true),
                Throws.InvalidOperationException.With.Message.EqualTo("Configured builder."));

            Assert.That(configured, Is.True);
            Assert.That(connector.ConfigureWasInvoked, Is.True);
        }

        private static ServiceCollection CreateManagedSessionServices(IManagedSessionConnector? connector = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddSingleton(new OpcUaClientOptions
            {
                Configuration = new ApplicationConfiguration(NUnitTelemetryContext.Create())
                {
                    ApplicationName = "coverage",
                    ApplicationUri = "urn:test:coverage",
                    ClientConfiguration = new ClientConfiguration()
                }
            });
            services.AddSingleton(connector ?? new NeverConnectsConnector());
            services.AddSingleton<IManagedSessionFactory, DefaultManagedSessionFactory>();
            return services;
        }

        private static ConfiguredEndpoint CreateEndpoint()
        {
            return new ConfiguredEndpoint(
                null,
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                },
                null);
        }

        private static ISession CreateSession(ITelemetryContext telemetry)
        {
            var session = new Mock<ISession>(MockBehavior.Loose);
            session.SetupGet(s => s.MessageContext).Returns(ServiceMessageContext.Create(telemetry));
            session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            return session.Object;
        }

        private sealed class NeverConnectsConnector : IManagedSessionConnector
        {
            public Task<Opc.Ua.Client.ManagedSession> ConnectAsync(
                IServiceProvider serviceProvider,
                ManagedSessionOptions sessionOptions,
                Action<ManagedSessionBuilder> configure,
                CancellationToken ct)
            {
                throw new InvalidOperationException("The coverage tests only validate argument guards.");
            }
        }

        private sealed class InvokesConfigureConnector : IManagedSessionConnector
        {
            public bool ConfigureWasInvoked { get; private set; }

            public Task<Opc.Ua.Client.ManagedSession> ConnectAsync(
                IServiceProvider serviceProvider,
                ManagedSessionOptions sessionOptions,
                Action<ManagedSessionBuilder> configure,
                CancellationToken ct)
            {
                var builder = new ManagedSessionBuilder(
                    serviceProvider.GetRequiredService<OpcUaClientOptions>().Configuration!,
                    NUnitTelemetryContext.Create());
                configure(builder);
                ConfigureWasInvoked = true;
                throw new InvalidOperationException("Configured builder.");
            }
        }
    }
}
