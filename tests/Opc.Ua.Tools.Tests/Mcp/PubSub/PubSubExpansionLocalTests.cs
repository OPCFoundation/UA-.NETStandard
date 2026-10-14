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

#if NET10_0
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.PubSub.Udp.Dtls;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Bounded loopback UDP/DTLS and injected Ethernet integration through the production MCP runtime facade.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    [Category("PubSubLocal")]
    [NonParallelizable]
    public sealed class PubSubExpansionLocalTests
    {
        [Test]
        public async Task LoopbackUdpRuntimeRoundTripsTypedFieldsWithExactInterfaceAndPeerRulesAsync()
        {
            await RoundTripAsync(dtls: false).ConfigureAwait(false);
        }

        [Test]
        public async Task LoopbackDtlsRuntimeRoundTripsThroughCertificateProviderAndPinnedValidatorAsync()
        {
            await RoundTripAsync(dtls: true).ConfigureAwait(false);
        }

        [Test]
        public async Task InjectedEthernetBackendCarriesUadpWithoutClaimingHardwareInteropAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            const string endpoint = "opc.eth://01-1B-19-00-00-01?vid=7&pcp=3";
            var options = new McpPubSubOptions
            {
                EthernetRules =
                [
                    new McpPubSubEthernetRule
                    {
                        Endpoint = endpoint, NetworkInterface = "pubsub-in-memory",
                        LocalMac = "02-00-00-00-00-01", SourceMacs = ["02-00-00-00-00-01"],
                        Operations = ["publish", "subscribe"]
                    }
                ]
            };
            IMcpOperationPolicy authority = fixture.Services.GetRequiredService<IMcpOperationPolicy>();
            var transport = new McpPubSubTransportProvider(authority, new McpNetworkPolicy(authority, []),
                options, TimeProvider.System, ethernetFactory: new InMemoryEthernetFrameChannelFactory());
            var manager = new McpPubSubRuntimeManager(authority, fixture.Registry, options, transport, fixture.Secrets,
                fixture.Sessions.Telemetry, TimeProvider.System,
                fixture.Services.GetRequiredService<McpPubSubKeyReferences>());
            var connection = new PubSubConnectionInput
            {
                Transport = PubSubTransportKind.Ethernet,
                Endpoint = endpoint,
                NetworkInterface = "pubsub-in-memory"
            };

            await ExchangeAsync(fixture, manager, connection).ConfigureAwait(false);
            JsonObject capability = transport.Capabilities().OfType<JsonObject>()
                .Single(value => value["transport"]!.GetValue<string>() == "Ethernet");
            Assert.That(capability["detail"]!.GetValue<string>(), Does.Contain("not proof of hardware interop"));
        }

        private static async Task RoundTripAsync(bool dtls)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            NetworkInterface loopback = NetworkInterface.GetAllNetworkInterfaces().First(network =>
                network.GetIPProperties().UnicastAddresses.Any(address => address.Address.Equals(IPAddress.Loopback)));
            int port;
            using (var reservation = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                reservation.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                port = ((IPEndPoint)reservation.LocalEndPoint!).Port;
            }
            string endpoint = $"{(dtls ? "opc.dtls" : "opc.udp")}://127.0.0.1:{port}";
            var options = new McpPubSubOptions
            {
                DatagramRules =
                [
                    new McpPubSubDatagramRule
                    {
                        Endpoint = endpoint, NetworkInterface = loopback.Name, LocalAddress = "127.0.0.1",
                        Operations = ["publish", "subscribe"],
                        AllowReplies = dtls,
                        Peers = [new McpPubSubPeerRule
                        {
                            Address = "127.0.0.1", FirstPort = 1, LastPort = 65535
                        }]
                    }
                ]
            };
            IMcpOperationPolicy authority = fixture.Services.GetRequiredService<IMcpOperationPolicy>();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                "CN=pubsub-loopback-test", key, HashAlgorithmName.SHA256);
            using var certificate = Certificate.From(request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10)));
            var validator = new Mock<ICertificateValidatorEx>(MockBehavior.Strict);
            validator.Setup(value => value.ValidateAsync(
                It.IsAny<Certificate>(), It.IsAny<TrustListIdentifier?>(), It.IsAny<CancellationToken>()))
                .Returns((Certificate peer, TrustListIdentifier? _, CancellationToken _) =>
                    Task.FromResult(new CertificateValidationResult(
                        isValid: peer.Thumbprint == certificate.Thumbprint,
                        statusCode: peer.Thumbprint == certificate.Thumbprint
                            ? StatusCodes.Good : StatusCodes.BadCertificateUntrusted,
                        errors: [], isSuppressible: false)));
            var certificates = new Mock<ICertificateProvider>(MockBehavior.Strict);
            certificates.Setup(value => value.GetPrivateKeyCertificateAsync(
                    It.Is<CertificateIdentifier>(id => id.Thumbprint == certificate.Thumbprint),
                    null, null, It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<Certificate?>(certificate.AddRef()));
            var dtlsOptions = new DtlsTransportOptions { PeerCertificateValidator = validator.Object };
            dtlsOptions.LocalCertificateIdentifiers.Add(
                new CertificateIdentifier { Thumbprint = certificate.Thumbprint });
            var profiles = new DtlsProfileRegistry();
            var contexts = new DefaultDtlsContextFactory(
                Options.Create(dtlsOptions), profiles, validator.Object, certificates.Object);
            var transport = new McpPubSubTransportProvider(
                authority, new McpNetworkPolicy(authority, []), options, TimeProvider.System,
                dtlsOptions: dtls ? Options.Create(dtlsOptions) : null,
                dtlsProfiles: dtls ? profiles : null, dtlsFactory: dtls ? contexts : null);
            var manager = new McpPubSubRuntimeManager(authority, fixture.Registry, options, transport, fixture.Secrets,
                fixture.Sessions.Telemetry, TimeProvider.System,
                fixture.Services.GetRequiredService<McpPubSubKeyReferences>());

            await ExchangeAsync(fixture, manager, new PubSubConnectionInput
            {
                Transport = dtls ? PubSubTransportKind.Dtls : PubSubTransportKind.Udp,
                Endpoint = endpoint,
                NetworkInterface = loopback.Name
            }).ConfigureAwait(false);
            if (dtls)
            {
                validator.Verify(value => value.ValidateAsync(
                    It.IsAny<Certificate>(), It.IsAny<TrustListIdentifier?>(), It.IsAny<CancellationToken>()),
                    Times.AtLeastOnce);
                certificates.Verify(value => value.GetPrivateKeyCertificateAsync(
                    It.IsAny<CertificateIdentifier>(), null, null, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            }
        }

        private static async Task ExchangeAsync(
            PubSubExpansionTestContext fixture, McpPubSubRuntimeManager manager, PubSubConnectionInput connection)
        {
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            PubSubApplicationInput sender = PubSubExpansionTestContext.Input("sender") with { Connection = connection };
            PubSubApplicationInput receiver = sender with { Name = "receiver", Mode = "Subscriber" };
            string publisher = (await manager.CreateAsync(sender, lifetime.Token).ConfigureAwait(false))
                ["handle"]!.GetValue<string>();
            string subscriber = (await manager.CreateAsync(receiver, lifetime.Token).ConfigureAwait(false))
                ["handle"]!.GetValue<string>();
            try
            {
                Task<JsonObject> receiveStart = manager.StartAsync(subscriber, lifetime.Token).AsTask();
                Task<JsonObject> sendStart = manager.StartAsync(publisher, lifetime.Token).AsTask();
                await Task.WhenAll(receiveStart, sendStart).ConfigureAwait(false);
                JsonObject received;
                do
                {
                    received = await manager.ReadAsync(subscriber, ct: lifetime.Token).ConfigureAwait(false);
                    if (received["dataSets"]!.AsArray().Count == 0)
                    {
                        await Task.Delay(10, lifetime.Token).ConfigureAwait(false);
                    }
                }
                while (received["dataSets"]!.AsArray().Count == 0);

                using var decoder = new JsonDecoder(
                    received["dataSets"]![0]!["fields"]!.ToJsonString(), fixture.Remote.Context);
                DataValue count = decoder.ReadDataValue("Count");
                DataValue enabled = decoder.ReadDataValue("Enabled");
                Assert.That(count.WrappedValue.TryGetValue(out int number), Is.True);
                Assert.That(number, Is.EqualTo(7));
                Assert.That(enabled.WrappedValue.TryGetValue(out bool flag), Is.True);
                Assert.That(flag, Is.False);
                Assert.That(received["running"]!.GetValue<bool>(), Is.True);
                Assert.That(received["bufferedBytes"]!.GetValue<int>(), Is.GreaterThan(0));
            }
            finally
            {
                _ = await manager.RemoveAsync(subscriber).ConfigureAwait(false);
                _ = await manager.RemoveAsync(publisher).ConfigureAwait(false);
            }
        }
    }
}
#endif
