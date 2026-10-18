/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.Server.DataChannels;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.DataChannels
{
    /// <summary>
    /// Direct and DI hosting preserve opt-in policy, resource bounds and listener ownership.
    /// </summary>
    [TestFixture]
    [Category("DataChannelsControl")]
    public sealed class DataChannelHostingTests
    {
        /// <summary>
        /// Optional model and Session control do not enable an outer flow factory on ordinary Service listeners.
        /// </summary>
        [TestCase("opc.tcp", Profiles.UaTcpTransport, false)]
        [TestCase("opc.wss", Profiles.UaTcpTransport, false)]
        [TestCase("opc.quic", Profiles.UaTcpTransport, false)]
        [TestCase("opc.wt", Profiles.UaTcpTransport, false)]
        [TestCase("opc.tcp", DataChannelProfiles.DedicatedUasc, true)]
        [TestCase("opc.tcp", DataChannelProfiles.SharedUasc, true)]
        [TestCase("opc.wss", DataChannelProfiles.DedicatedUasc, true)]
        [TestCase("opc.wss", DataChannelProfiles.SharedUasc, true)]
        [TestCase("opc.quic", DataChannelProfiles.SharedUasc, false)]
        [TestCase("opc.wt", DataChannelProfiles.DedicatedUasc, false)]
        [TestCase("opc.tcp", null, false)]
        public async Task ListenerContextFactoryRequiresAnAdvertisedUascFlow(
            string scheme, string? profile, bool expectedFactory)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var budget = new DataChannelApplicationBudget();
            await using var provider = new ServerDataChannelProvider(
                new DataChannelSourceRegistry(), new DataChannelOptions { AllowIntegrity = true },
                budget, TimeProvider.System, telemetry);
            await using var server = new ListenerSettingsServer(telemetry)
            {
                DataChannelProvider = provider,
                EnableDataChannelModel = true
            };
            var endpoint = new Uri($"{scheme}://localhost:4840/ua");
            string resource = profile switch
            {
                DataChannelProfiles.DedicatedUasc => DataChannelProfiles.DedicatedResource,
                DataChannelProfiles.SharedUasc => DataChannelProfiles.SharedResource,
                _ => "/other"
            };
            var settings = new TransportListenerSettings
            {
                Descriptions =
                [
                    new EndpointDescription
                    {
                        EndpointUrl = endpoint.AbsoluteUri,
                        TransportProfileUri = Profiles.UaTcpTransport
                    },
                    new EndpointDescription
                    {
                        EndpointUrl = $"{scheme}://localhost:4840{resource}",
                        TransportProfileUri = profile
                    }
                ]
            };

            server.ConfigureListener(settings, endpoint);

            Assert.Multiple(() =>
            {
                Assert.That(settings.DataChannels, Is.SameAs(expectedFactory ? provider : null));
                Assert.That(settings.AllowDataChannelIntegrity, Is.EqualTo(expectedFactory));
                Assert.That(server.DataChannelProvider, Is.SameAs(provider));
                Assert.That(server.EnableDataChannelModel, Is.True);
                Assert.That(budget.Usage.Contexts, Is.Zero);
            });
        }

        /// <summary>
        /// Empty or absent endpoint advertisements never enable an implicit flow carrier.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingEndpointAdvertisementsDoNotInjectAFlowFactory(bool absent)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var provider = new ServerDataChannelProvider(
                new DataChannelSourceRegistry(), new DataChannelOptions { AllowIntegrity = true },
                new DataChannelApplicationBudget(), TimeProvider.System, telemetry);
            await using var server = new ListenerSettingsServer(telemetry) { DataChannelProvider = provider };
            var settings = new TransportListenerSettings { Descriptions = absent ? null : [] };

            server.ConfigureListener(settings, new Uri("opc.tcp://localhost:4840"));

            Assert.Multiple(() =>
            {
                Assert.That(settings.DataChannels, Is.Null);
                Assert.That(settings.AllowDataChannelIntegrity, Is.False);
            });
        }

        [Test]
        public async Task ServerRegistrationUsesOneProviderAndHonorsExplicitBoundsAfterCommonRegistration()
        {
            var services = new ServiceCollection();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            services.AddSingleton(telemetry);
            IOpcUaBuilder common = services.AddOpcUa();
            common.AddDataChannels();
            IOpcUaServerBuilder builder = common.AddServer(_ => { });
            var options = new DataChannelOptions { MaxFlows = 3 };
            var limits = new DataChannelApplicationLimits { MaxContexts = 2, MaxFlows = 3 };
            var source = new DataChannelSource(
                "urn:hosting:source", new DataChannelApplicationPolicy(["urn:hosting:peer"]));
            builder.AddDataChannels(enableModel: true, options, limits).AddDataChannelSource(source);
            await using ServiceProvider container = services.BuildServiceProvider();
            ServerDataChannelProvider provider = container.GetRequiredService<ServerDataChannelProvider>();
            await using var server = new DependencyInjectionStandardServer(container, telemetry, TimeProvider.System);
            Assert.Multiple(() =>
            {
                Assert.That(container.GetRequiredService<IDataChannelContextFactory>(), Is.SameAs(provider));
                Assert.That(provider.Options, Is.SameAs(options));
                Assert.That(provider.Options.MaxFlows, Is.EqualTo(3));
                Assert.That(server.DataChannelProvider, Is.SameAs(provider));
                Assert.That(server.EnableDataChannelModel, Is.True);
                Assert.That(provider.Sources.TryGet(source.ResourceUri, out DataChannelSource? found), Is.True);
                Assert.That(found, Is.SameAs(source));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidFlowEndpointDisposesUnopenedListenerAndPreservesOriginalError(bool disposeFails)
        {
            await using var server = new StandardServer(NUnitTelemetryContext.Create());
            var listener = new Mock<ITransportListener>();
            if (disposeFails)
            {
                listener.Setup(value => value.DisposeAsync()).Returns(
                    new ValueTask(Task.FromException(new InvalidOperationException("Cleanup failed."))));
            }
            var endpoints = new List<EndpointDescription>
            {
                new()
                {
                    EndpointUrl = "opc.tcp://localhost:4840/flow/v1/dedicated",
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    TransportProfileUri = DataChannelProfiles.DedicatedUasc
                }
            };
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await server.CreateServiceHostEndpointAsync(
                    new Uri(endpoints[0].EndpointUrl!), endpoints, new EndpointConfiguration(),
                    listener.Object, Mock.Of<ICertificateValidatorEx>()).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            listener.Verify(value => value.DisposeAsync(), Times.Once);
            listener.Verify(value => value.OpenAsync(It.IsAny<Uri>(), It.IsAny<TransportListenerSettings>(),
                It.IsAny<ITransportListenerCallback>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Exercises the production listener-configuration hook without opening a transport.
        /// </summary>
        private sealed class ListenerSettingsServer : StandardServer
        {
            /// <summary>
            /// Creates the server with normal shared telemetry.
            /// </summary>
            internal ListenerSettingsServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            /// <summary>
            /// Configures one listener through the normal StandardServer implementation.
            /// </summary>
            internal void ConfigureListener(TransportListenerSettings settings, Uri endpoint)
            {
                ConfigureTransportListenerSettings(settings, endpoint);
            }
        }
    }
}
