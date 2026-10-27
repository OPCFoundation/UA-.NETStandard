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

        /// <summary>
        /// Native WT advertises its actual UASC identity/policy, not a TLS-only or Session endpoint.
        /// </summary>
        [Test]
        public async Task WebTransportManagementRegistrationPreservesUascEvidenceAndExactProfileAsync()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            EndpointDescription endpoint = WebTransportEndpoint(test);
            ByteString certificate = endpoint.ServerCertificate;
            bool available = true;
            using IDisposable registration = test.Provider.RegisterEndpoint(endpoint, () => available);
            endpoint.SecurityPolicyUri = SecurityPolicies.None;
            endpoint.ServerCertificate = default;
            ArrayOf<EndpointDescription> selected = test.Provider.GetEndpoints([DataChannelProfiles.WebTransport]);
            ArrayOf<string> profiles = test.Provider.GetSupportedTransportProfiles();
            Assert.Multiple(() =>
            {
                Assert.That(selected.Count, Is.EqualTo(1));
                Assert.That(selected[0].EndpointUrl, Is.EqualTo("opc.wt://localhost:4845/data?resource=pubsub"));
                Assert.That(selected[0].TransportProfileUri, Is.EqualTo(DataChannelProfiles.WebTransport));
                Assert.That(selected[0].SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(selected[0].SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
                Assert.That(selected[0].ServerCertificate, Is.EqualTo(certificate));
                Assert.That(selected[0].Server!.ApplicationUri, Is.EqualTo("urn:control-tests:server"));
                Assert.That(selected[0].UserIdentityTokens, Is.Empty);
                Assert.That(test.Provider.GetEndpoints([DataChannelProfiles.Quic]), Is.Empty);
                Assert.That(test.Provider.GetEndpoints([DataChannelProfiles.UascWebTransport]), Is.Empty);
                Assert.That(profiles.Count, Is.EqualTo(1));
                Assert.That(profiles[0], Is.EqualTo(DataChannelProfiles.WebTransport));
            });
            selected[0].SecurityMode = MessageSecurityMode.None;
            Assert.That(test.Provider.GetEndpoints()[0].SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            available = false;
            Assert.That(test.Provider.GetEndpoints(), Is.Empty);
            Assert.That(test.Provider.GetSupportedTransportProfiles(), Is.Empty);
            available = true;
            registration.Dispose();
            Assert.That(test.Provider.GetEndpoints(), Is.Empty);
        }

        /// <summary>
        /// Legacy HTTPS and None are independently invalid; neither may advertise a native management endpoint.
        /// </summary>
        [TestCase("https://localhost:4845/data", MessageSecurityMode.SignAndEncrypt)]
        [TestCase("opc.wt://localhost:4845/data", MessageSecurityMode.None)]
        [TestCase("https://localhost:4845/data", MessageSecurityMode.None)]
        [TestCase("opc.wt://localhost:4845/data", MessageSecurityMode.Sign)]
        public async Task WebTransportManagementRegistrationRejectsLegacyHttpsOrUnprotectedUascAsync(
            string url, MessageSecurityMode mode)
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            EndpointDescription endpoint = WebTransportEndpoint(test);
            endpoint.EndpointUrl = url;
            endpoint.SecurityMode = mode;
            if (mode == MessageSecurityMode.None)
            {
                endpoint.SecurityPolicyUri = SecurityPolicies.None;
            }
            ServiceResultException failure = Assert.Throws<ServiceResultException>(() =>
                test.Provider.RegisterEndpoint(endpoint, static () => true))!;
            Assert.Multiple(() =>
            {
                Assert.That(failure.StatusCode,
                    Is.EqualTo(url.StartsWith("https:", StringComparison.Ordinal)
                        ? StatusCodes.BadNotSupported : StatusCodes.BadSecurityModeRejected));
                Assert.That(test.Provider.GetEndpoints(), Is.Empty);
                Assert.That(test.Provider.GetSupportedTransportProfiles(), Is.Empty);
            });
        }

        /// <summary>
        /// Native WT requires a participating application's certificate and cannot claim Session authentication.
        /// </summary>
        [TestCase("certificate")]
        [TestCase("application")]
        [TestCase("session")]
        [TestCase("discovery")]
        public async Task WebTransportManagementRegistrationRejectsMissingIdentityOrSessionClaimsAsync(string invalid)
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            EndpointDescription endpoint = WebTransportEndpoint(test);
            switch (invalid)
            {
                case "certificate":
                    endpoint.ServerCertificate = default;
                    break;
                case "application":
                    endpoint.Server!.ApplicationUri = "urn:not-the-certificate-application";
                    break;
                case "session":
                    endpoint.UserIdentityTokens = [new UserTokenPolicy { TokenType = UserTokenType.Anonymous }];
                    break;
                case "discovery":
                    endpoint.Server!.DiscoveryUrls = [endpoint.EndpointUrl!];
                    break;
            }
            Assert.Throws<ArgumentException>(() => test.Provider.RegisterEndpoint(endpoint, static () => true));
            Assert.That(test.Provider.GetEndpoints(), Is.Empty);
            Assert.That(test.Provider.GetSupportedTransportProfiles(), Is.Empty);
        }

        /// <summary>
        /// An encrypted security mode cannot hide a None policy in discovery.
        /// </summary>
        [Test]
        public async Task WebTransportManagementRegistrationRejectsNonePolicyAsync()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            EndpointDescription endpoint = WebTransportEndpoint(test);
            endpoint.SecurityPolicyUri = SecurityPolicies.None;
            ServiceResultException failure = Assert.Throws<ServiceResultException>(() =>
                test.Provider.RegisterEndpoint(endpoint, static () => true))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
            Assert.That(test.Provider.GetEndpoints(), Is.Empty);
        }

        private static EndpointDescription WebTransportEndpoint(DataChannelControlTestContext test)
        {
            EndpointDescription endpoint = NativeEndpoint(test);
            endpoint.EndpointUrl = "opc.wt://localhost:4845/data?resource=pubsub";
            endpoint.TransportProfileUri = DataChannelProfiles.WebTransport;
            endpoint.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            endpoint.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            return endpoint;
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
