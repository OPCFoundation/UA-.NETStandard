/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Server.Tests.DataChannels
{
    /// <summary>
    /// Available carrier profiles retain exact security/resource evidence and cannot downgrade.
    /// </summary>
    [TestFixture]
    [Category("DataChannelsControl")]
    public sealed class DataChannelDiscoveryTests
    {
        [Test]
        public async Task NativeAvailabilityAndExactFiltersNeverSubstituteCarrier()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            bool available = false;
            EndpointDescription endpoint = NativeEndpoint(test);
            using IDisposable registration = test.Provider.RegisterEndpoint(endpoint, () => available);
            Assert.That(test.Provider.GetEndpoints(), Is.Empty);
            Assert.That(test.Provider.GetSupportedTransportProfiles(), Is.Empty);
            available = true;
            ArrayOf<EndpointDescription> selected = test.Provider.GetEndpoints([DataChannelProfiles.Quic]);
            Assert.Multiple(() =>
            {
                Assert.That(selected.Count, Is.EqualTo(1));
                Assert.That(selected[0].EndpointUrl, Is.EqualTo(endpoint.EndpointUrl));
                Assert.That(selected[0].SecurityMode, Is.EqualTo(MessageSecurityMode.None));
                Assert.That(selected[0].SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
                Assert.That(selected[0].ServerCertificate, Is.EqualTo(endpoint.ServerCertificate));
                Assert.That(selected[0].UserIdentityTokens, Is.Empty);
                Assert.That(test.Provider.GetEndpoints([DataChannelProfiles.WebTransport]), Is.Empty);
                Assert.That(test.Provider.GetEndpoints([DataChannelProfiles.UadpPayload]), Is.Empty);
            });
            selected[0].ServerCertificate = default;
            Assert.That(test.Provider.GetEndpoints()[0].ServerCertificate.IsEmpty, Is.False);
            registration.Dispose();
            Assert.That(test.Provider.GetEndpoints(), Is.Empty);
        }

        [TestCase("certificate")]
        [TestCase("session")]
        [TestCase("discovery")]
        [TestCase("profile")]
        [TestCase("application")]
        public async Task NativeDescriptionRefusesMissingTlsIdentityOrFalseSessionDiscoveryClaims(string invalid)
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            EndpointDescription endpoint = NativeEndpoint(test);
            switch (invalid)
            {
                case "certificate":
                    endpoint.ServerCertificate = default;
                    break;
                case "session":
                    endpoint.UserIdentityTokens = [new UserTokenPolicy { TokenType = UserTokenType.Anonymous }];
                    break;
                case "discovery":
                    endpoint.Server!.DiscoveryUrls = [endpoint.EndpointUrl!];
                    break;
                case "profile":
                    endpoint.TransportProfileUri = Profiles.UaTcpTransport;
                    break;
                case "application":
                    endpoint.Server!.ApplicationUri = "urn:wrong:application";
                    break;
            }
            Assert.That(() => test.Provider.RegisterEndpoint(endpoint, static () => true), Throws.ArgumentException);
        }

        private static EndpointDescription NativeEndpoint(DataChannelControlTestContext test)
        {
            return new EndpointDescription
            {
                EndpointUrl = "opc.quic://localhost:4845/data",
                TransportProfileUri = DataChannelProfiles.Quic,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                ServerCertificate = ByteString.From(test.ServerCertificate.RawData),
                Server = new ApplicationDescription
                {
                    ApplicationUri = "urn:control-tests:server",
                    DiscoveryUrls = ["opc.tcp://localhost:4840/discovery"]
                },
                UserIdentityTokens = []
            };
        }
    }
}
