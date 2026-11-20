// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.DependencyInjection
{
    /// <summary>
    /// Smoke tests for <c>HttpsTransportChannelBindings</c>: an internal
    /// decorator in <c>Opc.Ua.Client</c> that wraps
    /// <see cref="ITransportChannelBindings"/> to route
    /// <c>https://</c> / <c>opc.https://</c>
    /// <para>
    /// channels through an
    /// injected <see cref="IOpcUaHttpClientFactory"/>.
    /// </para>
    /// <para>
    /// Since the class is
    /// </para>
    /// <c>internal sealed</c>, tests exercise it indirectly through the DI pipeline: when
    /// <see cref="OpcUaClientBuilderExtensions.AddClient(IOpcUaBuilder, System.Action{OpcUaClientOptions})"/>
    /// is called and an <see cref="IOpcUaHttpClientFactory"/> is present,
    /// <c>HttpsTransportChannelBindings</c> is created inside the
    /// <see cref="IClientChannelManager"/> factory — resolving that
    /// service drives the constructor code paths.
    /// </summary>
    [TestFixture]
    [Category("DIExtensionsBatch1")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class HttpsTransportChannelBindingsTests
    {
        [Test]
        public async Task ClientChannelManagerCreatedWithHttpsBindingsWhenHttpClientFactoryPresent()
        {
            // Arrange: full client DI stack; AddClient registers IOpcUaHttpClientFactory
            // internally (DefaultOpcUaHttpClientFactory), which causes the
            // IClientChannelManager factory to wrap the registry in
            // HttpsTransportChannelBindings.
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddOpcTcpTransport()
                .AddClient(o => o.Configuration = CreateMinimalConfig());

            ServiceProvider provider = services.BuildServiceProvider();
            try
            {
                // Act: resolving IClientChannelManager triggers HttpsTransportChannelBindings ctor.
                IClientChannelManager channelManager =
                    provider.GetRequiredService<IClientChannelManager>();

                // Assert: the service was created without error.
                Assert.That(channelManager, Is.Not.Null);
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ClientChannelManagerSingletonReturnsSameInstance()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddOpcTcpTransport()
                .AddClient(o => o.Configuration = CreateMinimalConfig());

            ServiceProvider provider = services.BuildServiceProvider();
            try
            {
                IClientChannelManager first = provider.GetRequiredService<IClientChannelManager>();
                IClientChannelManager second = provider.GetRequiredService<IClientChannelManager>();

                Assert.That(second, Is.SameAs(first));
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ClientChannelManagerIsResolvable()
        {
            // Verify the IClientChannelManager can be resolved from a DI container that
            // registers the full client stack (which internally registers IOpcUaHttpClientFactory
            // and wraps the ITransportBindingRegistry in HttpsTransportChannelBindings).
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddOpcTcpTransport()
                .AddClient(o => o.Configuration = CreateMinimalConfig());

            ServiceProvider provider = services.BuildServiceProvider();
            try
            {
                Assert.That(
                    () => provider.GetRequiredService<IClientChannelManager>(),
                    Throws.Nothing);
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static ApplicationConfiguration CreateMinimalConfig()
        {
            return new ApplicationConfiguration
            {
                ApplicationUri = "urn:test:https-bindings",
                ApplicationName = "HttpsBindingsTest",
                ClientConfiguration = new ClientConfiguration()
            };
        }
    }
}
