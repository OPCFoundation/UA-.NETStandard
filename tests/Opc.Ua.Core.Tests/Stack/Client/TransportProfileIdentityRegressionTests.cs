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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Verifies transport-profile identity in managed channel sharing and discovery endpoint translation.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    public sealed class TransportProfileIdentityRegressionTests
    {
        /// <summary>
        /// Verifies distinct HTTPS codecs use separate managed channels while identical profiles share one.
        /// </summary>
        [Test]
        public async Task DifferentHttpsCodecsNeverShareAManagedTransportAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            int opened = 0;
            var bindings = new Mock<ITransportChannelBindings>();
            bindings.Setup(value => value.Create(It.IsAny<string>(), It.IsAny<ITelemetryContext>()))
                .Returns(() =>
                {
                    var channel = new Mock<ITransportChannel>();
                    Mock<ISecureChannel> secure = channel.As<ISecureChannel>();
                    secure.Setup(value => value.OpenAsync(
                            It.IsAny<Uri>(), It.IsAny<TransportChannelSettings>(), It.IsAny<CancellationToken>()))
                        .Callback(() => opened++)
                        .Returns(default(ValueTask));
                    channel.Setup(value => value.CloseAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
                    return channel.Object;
                });
            await using var manager = new ClientChannelManager(
                new ApplicationConfiguration(telemetry), telemetry, bindings.Object);
            var leases = new List<IManagedTransportChannel>();
            try
            {
                foreach (string profile in s_profiles)
                {
                    leases.Add(await manager.GetAsync(NewParticipant(profile)).ConfigureAwait(false));
                }
                leases.Add(await manager.GetAsync(NewParticipant(s_profiles[0])).ConfigureAwait(false));
                Assert.That(opened, Is.EqualTo(3));
                Assert.That(leases.Take(3).Select(lease => lease.Key), Is.Unique);
                Assert.That(leases[3].Key, Is.EqualTo(leases[0].Key));
                Assert.That(leases[3].Key.GetHashCode(), Is.EqualTo(leases[0].Key.GetHashCode()));
                Assert.That(leases.Select(lease => lease.State), Is.All.EqualTo(ChannelState.Ready));
            }
            finally
            {
                foreach (IManagedTransportChannel lease in leases)
                {
                    await lease.CloseAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Verifies omitted profiles and the compatibility constructor resolve to the default binary channel identity.
        /// </summary>
        [Test]
        public void MissingProfileAndLegacyConstructorKeepDefaultBinaryIdentity()
        {
            ConfiguredEndpoint implicitProfile = Endpoint(null);
            ConfiguredEndpoint explicitProfile = Endpoint(Profiles.HttpsBinaryTransport);
            implicitProfile.Configuration.UseBinaryEncoding = explicitProfile.Configuration.UseBinaryEncoding;
            ManagedChannelKey implicitKey = ManagedChannelKey.FromEndpoint(implicitProfile);
            ManagedChannelKey explicitKey = ManagedChannelKey.FromEndpoint(explicitProfile);
            Assert.That(implicitKey, Is.EqualTo(explicitKey));
            var legacy = new ManagedChannelKey(
                implicitKey.EndpointUrl, implicitKey.SecurityPolicyUri, implicitKey.SecurityMode,
                implicitKey.ServerCertificateThumbprint, implicitKey.EndpointConfigurationHash,
                implicitKey.ClientCertificateThumbprint, null);
            Assert.That(legacy, Is.EqualTo(explicitKey));
        }

        /// <summary>
        /// Verifies an omitted profile respects the endpoint's configured JSON encoding.
        /// </summary>
        [Test]
        public void MissingProfileUsesTheConfiguredJsonEncoding()
        {
            ConfiguredEndpoint implicitProfile = Endpoint(null);
            ConfiguredEndpoint explicitProfile = Endpoint(Profiles.HttpsJsonTransport);
            implicitProfile.Configuration.UseBinaryEncoding = false;
            Assert.That(ManagedChannelKey.FromEndpoint(implicitProfile),
                Is.EqualTo(ManagedChannelKey.FromEndpoint(explicitProfile)));
        }

        /// <summary>
        /// Verifies endpoint translation infers the TCP transport profile from the URL scheme.
        /// </summary>
        [Test]
        public void OmittedBaseProfileUsesTheEndpointSchemesDefault()
        {
            using var server = new TranslationServer();
            ArrayOf<EndpointDescription> result = server.TranslateImplicitTcp();
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].EndpointUrl, Is.EqualTo("opc.tcp://localhost:4840/"));
            Assert.That(result[0].TransportProfileUri, Is.EqualTo(Profiles.UaTcpTransport));
        }

        /// <summary>
        /// Verifies exact profile filtering and alternate-address rewriting preserve all distinct supported codecs.
        /// </summary>
        [Test]
        public void DiscoveryTranslationPreservesProfilesAndHonorsExactProfileFilters(
            [Values("all", "binary", "json", "openapi", "pair", "unknown")] string selection,
            [Values(false, true)] bool alternate)
        {
            using var server = new TranslationServer();
            ArrayOf<string> profiles = selection switch
            {
                "binary" => [Profiles.HttpsBinaryTransport],
                "json" => [Profiles.HttpsJsonTransport],
                "openapi" => [Profiles.HttpsOpenApiTransport],
                "pair" => [Profiles.HttpsBinaryTransport, Profiles.HttpsJsonTransport],
                "unknown" => ["urn:unsupported-profile"],
                _ => []
            };
            ArrayOf<EndpointDescription> result = server.Translate(profiles, alternate);
            string[] expected = selection == "all" ? s_profiles : selection == "unknown" ? [] : profiles.ToArray();
            Assert.That(result.ToArray().Select(endpoint => endpoint.TransportProfileUri), Is.EquivalentTo(expected));
            Assert.That(result.Count, Is.EqualTo(expected.Length));
            string expectedUrl = alternate ? "https://public-host:7443/UA/app" : "https://localhost:5443/UA/app";
            Assert.That(result.ToArray().Select(endpoint => endpoint.EndpointUrl), Is.All.EqualTo(expectedUrl));
        }

        /// <summary>
        /// Creates a reconnect participant whose endpoint selects the requested transport profile.
        /// </summary>
        private static IReconnectParticipant NewParticipant(string profile)
        {
            var participant = new Mock<IReconnectParticipant>();
            participant.SetupGet(value => value.Id).Returns(profile);
            participant.SetupGet(value => value.Endpoint).Returns(Endpoint(profile));
            participant.Setup(value => value.OnReconnectAsync(
                    It.IsAny<IManagedTransportChannel>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ParticipantReconnectResult>(ParticipantReconnectResult.Reactivated));
            return participant.Object;
        }

        /// <summary>
        /// Creates an HTTPS endpoint with an explicit or omitted profile and a fixed operation timeout.
        /// </summary>
        private static ConfiguredEndpoint Endpoint(string profile)
        {
            return new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "https://localhost:5443/UA/app",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = profile
            }, new EndpointConfiguration { OperationTimeout = 1000 })
            {
                UpdateBeforeConnect = false
            };
        }

        /// <summary>
        /// Exposes discovery translation with controlled base and alternate addresses.
        /// </summary>
        private sealed class TranslationServer : ServerBase
        {
            /// <summary>
            /// Configures local and public HTTPS addresses for profile-preserving translation.
            /// </summary>
            public TranslationServer()
                : base(NUnitTelemetryContext.Create())
            {
                InitializeBaseAddresses(new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        BaseAddresses = ["https://localhost:5443/UA"],
                        AlternateBaseAddresses = ["https://public-host:7443/UA"]
                    }
                });
            }

            /// <summary>
            /// Translates duplicate codec descriptions through profile filtering and the selected base address.
            /// </summary>
            public ArrayOf<EndpointDescription> Translate(ArrayOf<string> profiles, bool alternate)
            {
                var descriptions = new List<EndpointDescription>();
                foreach (string profile in s_profiles)
                {
                    descriptions.Add(Endpoint(profile).Description);
                    descriptions.Add(Endpoint(profile).Description);
                }
                return TranslateEndpointDescriptions(
                    new Uri(alternate ? "https://public-host:7443/UA" : "https://localhost:5443/UA"),
                    FilterByEndpointUrl(
                        new Uri(alternate ? "https://public-host:7443/UA" : "https://localhost:5443/UA"),
                        FilterByProfile(profiles, BaseAddresses)),
                    descriptions.ToArrayOf(),
                    new ApplicationDescription());
            }

            /// <summary>
            /// Translates a TCP endpoint whose transport profile was not specified.
            /// </summary>
            public ArrayOf<EndpointDescription> TranslateImplicitTcp()
            {
                var url = new Uri("opc.tcp://localhost:4840");
                return TranslateEndpointDescriptions(
                    url, new List<BaseAddress> { new() { Url = url, DiscoveryUrl = url } },
                    [new EndpointDescription
                    {
                        EndpointUrl = url.ToString(),
                        SecurityPolicyUri = SecurityPolicies.None,
                        SecurityMode = MessageSecurityMode.None
                    }],
                    new ApplicationDescription());
            }
        }

        /// <summary>
        /// Lists the binary, JSON, and OpenAPI profiles that must remain distinct.
        /// </summary>
        private static readonly string[] s_profiles =
            [Profiles.HttpsBinaryTransport, Profiles.HttpsJsonTransport, Profiles.HttpsOpenApiTransport];
    }
}
