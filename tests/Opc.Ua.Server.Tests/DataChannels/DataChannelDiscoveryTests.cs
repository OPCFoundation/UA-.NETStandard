/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Microsoft.Extensions.Time.Testing;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.DataChannels;
using Opc.Ua.Tests;

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

        [TestCase("opc.tcp")]
        [TestCase("opc.wss")]
        public async Task CanonicalPart30DiscoveryFiltersExactProfilesAndPreservesEndpointMetadata(string scheme)
        {
            using Certificate certificate = CanonicalPart30Certificate();
            await using ServerDataChannelProvider provider = CanonicalPart30Provider();
            Assert.That(provider.GetEndpoints(), Is.Empty);
            Assert.That(provider.GetSupportedTransportProfiles(), Is.Empty);
            EndpointDescription canonical = CanonicalPart30Endpoint(
                certificate, scheme, DataChannelProfiles.PubSubUasc, "/pubsub/uasc");
            using IDisposable first = provider.RegisterEndpoint(canonical, static () => true);
            ArrayOf<EndpointDescription> singleton = provider.GetEndpoints();
            Assert.That(singleton.Count, Is.EqualTo(1));
            CanonicalPart30AssertMetadata(singleton[0], certificate, scheme, DataChannelProfiles.PubSubUasc, "/pubsub/uasc");
            using IDisposable second = provider.RegisterEndpoint(CanonicalPart30Endpoint(
                certificate, scheme, DataChannelProfiles.SharedUasc, "/flow/v1/shared"), static () => true);
            using IDisposable third = provider.RegisterEndpoint(CanonicalPart30Endpoint(
                certificate, scheme, DataChannelProfiles.DedicatedUasc, "/flow/v1/dedicated"), static () => true);

            Assert.Multiple(() =>
            {
                Assert.That(provider.GetEndpoints().ConvertAll(value => value.TransportProfileUri).ToArray(), Is.EquivalentTo(
                    new[] { DataChannelProfiles.PubSubUasc, DataChannelProfiles.SharedUasc, DataChannelProfiles.DedicatedUasc }));
                Assert.That(provider.GetEndpoints([]).Count, Is.EqualTo(3));
                Assert.That(provider.GetEndpoints(["urn:unknown:profile"]), Is.Empty);
                Assert.That(provider.GetEndpoints([DataChannelProfiles.PubSubUasc, DataChannelProfiles.SharedUasc])
                    .ConvertAll(value => value.TransportProfileUri).ToArray(),
                    Is.EquivalentTo(new[] { DataChannelProfiles.PubSubUasc, DataChannelProfiles.SharedUasc }));
            });
            foreach ((string profile, string path) in new[]
            {
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc"),
                (DataChannelProfiles.SharedUasc, "/flow/v1/shared"),
                (DataChannelProfiles.DedicatedUasc, "/flow/v1/dedicated")
            })
            {
                ArrayOf<EndpointDescription> selected = provider.GetEndpoints([profile]);
                Assert.That(selected.Count, Is.EqualTo(1), profile);
                CanonicalPart30AssertMetadata(selected[0], certificate, scheme, profile, path);
            }
        }

        [TestCase("resource")]
        [TestCase("profile-resource")]
        [TestCase("reserved")]
        [TestCase("query")]
        [TestCase("none")]
        [TestCase("sign")]
        [TestCase("policy-none")]
        [TestCase("policy-null")]
        [TestCase("policy-empty")]
        [TestCase("certificate")]
        [TestCase("application")]
        [TestCase("foreign-application")]
        [TestCase("userinfo")]
        [TestCase("fragment")]
        [TestCase("ordinary-profile")]
        [TestCase("discovery")]
        [TestCase("dedicated-session")]
        public async Task CanonicalPart30DiscoveryRegistrationRejectsInvalidCanonicalDescriptions(
            string defect)
        {
            StatusCode status = defect switch
            {
                "resource" or "profile-resource" or "reserved" or "query" => StatusCodes.BadNotSupported,
                "none" or "sign" => StatusCodes.BadSecurityModeRejected,
                "policy-none" or "policy-null" or "policy-empty" => StatusCodes.BadSecurityPolicyRejected,
                _ => StatusCodes.Good
            };
            using Certificate certificate = CanonicalPart30Certificate();
            await using ServerDataChannelProvider provider = CanonicalPart30Provider();
            using IDisposable control = provider.RegisterEndpoint(CanonicalPart30Endpoint(
                certificate, "opc.tcp", DataChannelProfiles.PubSubUasc, "/pubsub/uasc"), static () => true);
            foreach (string scheme in new[] { "opc.tcp", "opc.wss" })
            {
                EndpointDescription endpoint = CanonicalPart30Endpoint(
                    certificate, scheme, DataChannelProfiles.PubSubUasc, "/pubsub/uasc");
                switch (defect)
                {
                    case "resource":
                        endpoint.EndpointUrl = $"{scheme}://localhost:4840/flow/v1/shared";
                        break;
                    case "profile-resource":
                        endpoint.TransportProfileUri = DataChannelProfiles.SharedUasc;
                        break;
                    case "reserved":
                        endpoint.EndpointUrl = $"{scheme}://localhost:4840/pubsub/other";
                        break;
                    case "query":
                        endpoint.EndpointUrl += "?fallback=true";
                        break;
                    case "none":
                        endpoint.SecurityMode = MessageSecurityMode.None;
                        break;
                    case "sign":
                        endpoint.SecurityMode = MessageSecurityMode.Sign;
                        break;
                    case "policy-none":
                        endpoint.SecurityPolicyUri = SecurityPolicies.None;
                        break;
                    case "policy-null":
                        endpoint.SecurityPolicyUri = null;
                        break;
                    case "policy-empty":
                        endpoint.SecurityPolicyUri = string.Empty;
                        break;
                    case "certificate":
                        endpoint.ServerCertificate = default;
                        break;
                    case "application":
                        endpoint.Server.ApplicationUri = null;
                        break;
                    case "foreign-application":
                        endpoint.Server.ApplicationUri = "urn:foreign:server";
                        break;
                    case "userinfo":
                        endpoint.EndpointUrl = $"{scheme}://user@localhost:4840/pubsub/uasc";
                        break;
                    case "fragment":
                        endpoint.EndpointUrl += "#fragment";
                        break;
                    case "ordinary-profile":
                        endpoint.TransportProfileUri = Profiles.UaTcpTransport;
                        break;
                    case "discovery":
                        endpoint.Server.DiscoveryUrls = [endpoint.EndpointUrl!];
                        break;
                    case "dedicated-session":
                        endpoint.TransportProfileUri = DataChannelProfiles.DedicatedUasc;
                        endpoint.EndpointUrl = $"{scheme}://localhost:4840/flow/v1/dedicated";
                        break;
                }
                void Register()
                {
                    using IDisposable unexpected = provider.RegisterEndpoint(endpoint, static () => true);
                }
                if (status == StatusCodes.Good)
                {
                    Assert.Throws<ArgumentException>(Register, scheme);
                }
                else
                {
                    ServiceResultException failure = Assert.Throws<ServiceResultException>(Register, scheme)!;
                    Assert.That(failure.StatusCode, Is.EqualTo(status), scheme);
                }
                ArrayOf<EndpointDescription> remaining = provider.GetEndpoints();
                Assert.That(remaining.Count, Is.EqualTo(1), scheme);
                CanonicalPart30AssertMetadata(
                    remaining[0], certificate, "opc.tcp", DataChannelProfiles.PubSubUasc, "/pubsub/uasc");
                Assert.That(provider.GetSupportedTransportProfiles(),
                    Is.EqualTo(s_canonicalPart30Profile));
            }
        }

        [Test]
        public async Task CanonicalPart30DiscoveryClonesRegistrationAndReturnedDescriptions()
        {
            using Certificate certificate = CanonicalPart30Certificate();
            await using ServerDataChannelProvider provider = CanonicalPart30Provider();
            EndpointDescription original = CanonicalPart30Endpoint(
                certificate, "opc.wss", DataChannelProfiles.PubSubUasc, "/pubsub/uasc");
            using IDisposable registration = provider.RegisterEndpoint(original, static () => true);
            original.EndpointUrl = "opc.wss://changed.invalid/ordinary";
            original.Server.ApplicationUri = "urn:mutated:application";
            original.Server.ApplicationName = new LocalizedText("Changed");
            original.Server.DiscoveryUrls = ["opc.tcp://changed.invalid/ordinary"];
            original.ServerCertificate = default;
            original.UserIdentityTokens[0].PolicyId = "changed";

            EndpointDescription returned = provider.GetEndpoints()[0];
            CanonicalPart30AssertMetadata(
                returned, certificate, "opc.wss", DataChannelProfiles.PubSubUasc, "/pubsub/uasc");
            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.Not.SameAs(original));
                Assert.That(returned.Server, Is.Not.SameAs(original.Server));
                Assert.That(returned.UserIdentityTokens[0], Is.Not.SameAs(original.UserIdentityTokens[0]));
            });
            returned.Server.ApplicationUri = "urn:mutated:returned";
            returned.Server.DiscoveryUrls = [];
            returned.ServerCertificate = default;
            returned.SecurityPolicyUri = SecurityPolicies.None;
            returned.UserIdentityTokens[1].IssuedTokenType = "changed";

            EndpointDescription fresh = provider.GetEndpoints()[0];
            CanonicalPart30AssertMetadata(
                fresh, certificate, "opc.wss", DataChannelProfiles.PubSubUasc, "/pubsub/uasc");
            Assert.Multiple(() =>
            {
                Assert.That(fresh, Is.Not.SameAs(returned));
                Assert.That(fresh.Server, Is.Not.SameAs(returned.Server));
                Assert.That(fresh.UserIdentityTokens[1], Is.Not.SameAs(returned.UserIdentityTokens[1]));
            });
        }

        [Test]
        public async Task CanonicalPart30DiscoveryTracksAvailabilityAndRegistrationDisposal()
        {
            using Certificate certificate = CanonicalPart30Certificate();
            await using ServerDataChannelProvider provider = CanonicalPart30Provider();
            bool canonicalAvailable = false;
            bool legacyAvailable = true;
            using IDisposable canonical = provider.RegisterEndpoint(CanonicalPart30Endpoint(
                certificate, "opc.tcp", DataChannelProfiles.PubSubUasc, "/pubsub/uasc"), () => canonicalAvailable);
            using IDisposable legacy = provider.RegisterEndpoint(CanonicalPart30Endpoint(
                certificate, "opc.wss", DataChannelProfiles.SharedUasc, "/flow/v1/shared"), () => legacyAvailable);
            Assert.That(provider.GetEndpoints().ConvertAll(value => value.EndpointUrl),
                Is.EqualTo(s_canonicalPart30LegacyUrl));
            Assert.That(provider.GetSupportedTransportProfiles(), Is.EqualTo(new[] { DataChannelProfiles.SharedUasc }));

            canonicalAvailable = true;
            Assert.That(provider.GetEndpoints().Count, Is.EqualTo(2));
            Assert.That(provider.GetSupportedTransportProfiles().ToArray(),
                Is.EquivalentTo(new[] { DataChannelProfiles.PubSubUasc, DataChannelProfiles.SharedUasc }));
            canonicalAvailable = false;
            legacyAvailable = false;
            Assert.That(provider.GetEndpoints(), Is.Empty);
            Assert.That(provider.GetSupportedTransportProfiles(), Is.Empty);
            canonicalAvailable = true;
            legacyAvailable = true;
            canonical.Dispose();
            canonical.Dispose();
            Assert.That(provider.GetEndpoints().ConvertAll(value => value.EndpointUrl),
                Is.EqualTo(s_canonicalPart30LegacyUrl));
            Assert.That(provider.GetEndpoints([DataChannelProfiles.PubSubUasc]), Is.Empty);
            legacy.Dispose();
            Assert.That(provider.GetEndpoints(), Is.Empty);
            Assert.That(provider.GetSupportedTransportProfiles(), Is.Empty);
        }

        [Test]
        public async Task CanonicalPart30DiscoverySupportedProfilesKeepsCanonicalAndLegacyDistinct()
        {
            using Certificate certificate = CanonicalPart30Certificate();
            await using ServerDataChannelProvider provider = CanonicalPart30Provider();
            using IDisposable tcp = provider.RegisterEndpoint(CanonicalPart30Endpoint(
                certificate, "opc.tcp", DataChannelProfiles.PubSubUasc, "/pubsub/uasc"), static () => true);
            using IDisposable wss = provider.RegisterEndpoint(CanonicalPart30Endpoint(
                certificate, "opc.wss", DataChannelProfiles.PubSubUasc, "/pubsub/uasc"), static () => true);
            using IDisposable legacy = provider.RegisterEndpoint(CanonicalPart30Endpoint(
                certificate, "opc.tcp", DataChannelProfiles.SharedUasc, "/flow/v1/shared"), static () => true);
            using IDisposable dedicated = provider.RegisterEndpoint(CanonicalPart30Endpoint(
                certificate, "opc.tcp", DataChannelProfiles.DedicatedUasc, "/flow/v1/dedicated"), static () => true);
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetSupportedTransportProfiles().ToArray(), Is.EquivalentTo(s_canonicalPart30Profiles));
                Assert.That(provider.GetEndpoints([DataChannelProfiles.PubSubUasc]).Count, Is.EqualTo(2));
                Assert.That(provider.GetEndpoints([DataChannelProfiles.SharedUasc]).Count, Is.EqualTo(1));
                Assert.That(provider.GetEndpoints([DataChannelProfiles.DedicatedUasc])[0].UserIdentityTokens, Is.Empty);
                Assert.That(provider.GetEndpoints([DataChannelProfiles.PubSubUasc])[0].UserIdentityTokens.Count, Is.EqualTo(2));
            });
            foreach (string recognized in new[]
            {
                DataChannelProfiles.PubSubUasc, DataChannelProfiles.SharedUasc, DataChannelProfiles.DedicatedUasc
            })
            {
                Assert.That(ServerDataChannelProvider.IsDataProfile(recognized), Is.True, recognized);
            }
            foreach (string? unrelated in new[] { null, string.Empty, Profiles.UaTcpTransport, "urn:unknown", DataChannelProfiles.PubSubUasc + "/" })
            {
                Assert.That(ServerDataChannelProvider.IsDataProfile(unrelated), Is.False, unrelated);
            }
        }

        private static ServerDataChannelProvider CanonicalPart30Provider()
        {
            return new ServerDataChannelProvider(
                new DataChannelSourceRegistry(), new DataChannelOptions(), new DataChannelApplicationBudget(),
                new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero)),
                NUnitTelemetryContext.Create());
        }

        private static Certificate CanonicalPart30Certificate()
        {
            return CertificateBuilder.Create("CN=CanonicalPart30Discovery")
                .SetNotBefore(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2036, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .AddExtension(new X509SubjectAltNameExtension("urn:canonical-part30:server", ["localhost"]))
                .CreateForRSA();
        }

        private static EndpointDescription CanonicalPart30Endpoint(
            Certificate certificate, string scheme, string profile, string resource)
        {
            return new EndpointDescription
            {
                EndpointUrl = $"{scheme}://localhost:4840{resource}",
                TransportProfileUri = profile,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                SecurityLevel = 42,
                ServerCertificate = ByteString.From(certificate.RawData),
                Server = new ApplicationDescription
                {
                    ApplicationUri = "urn:canonical-part30:server",
                    ApplicationName = new LocalizedText("Part30 Server"),
                    ProductUri = "urn:canonical-part30:product",
                    ApplicationType = ApplicationType.Server,
                    DiscoveryUrls = ["opc.tcp://localhost:4840/discovery", "opc.wss://localhost:4841/discovery"]
                },
                UserIdentityTokens = profile == DataChannelProfiles.DedicatedUasc ? [] :
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "username", TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    },
                    new UserTokenPolicy
                    {
                        PolicyId = "issued", TokenType = UserTokenType.IssuedToken,
                        IssuedTokenType = "urn:canonical-part30:token", IssuerEndpointUrl = "opc.tcp://issuer.invalid:4840",
                        SecurityPolicyUri = SecurityPolicies.Aes128_Sha256_RsaOaep
                    }
                ]
            };
        }

        private static void CanonicalPart30AssertMetadata(
            EndpointDescription endpoint, Certificate certificate, string scheme, string profile, string resource)
        {
            Assert.Multiple(() =>
            {
                Assert.That(endpoint.EndpointUrl, Is.EqualTo($"{scheme}://localhost:4840{resource}"));
                Assert.That(endpoint.TransportProfileUri, Is.EqualTo(profile));
                Assert.That(endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(endpoint.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
                Assert.That(endpoint.SecurityLevel, Is.EqualTo(42));
                Assert.That(endpoint.ServerCertificate.ToArray(), Is.EqualTo(certificate.RawData));
                Assert.That(endpoint.Server.ApplicationUri, Is.EqualTo("urn:canonical-part30:server"));
                Assert.That(endpoint.Server.ApplicationName.Text, Is.EqualTo("Part30 Server"));
                Assert.That(endpoint.Server.ProductUri, Is.EqualTo("urn:canonical-part30:product"));
                Assert.That(endpoint.Server.ApplicationType, Is.EqualTo(ApplicationType.Server));
                Assert.That(endpoint.Server.DiscoveryUrls, Is.EqualTo(s_canonicalPart30DiscoveryUrls));
                Assert.That(endpoint.UserIdentityTokens.Count,
                    Is.EqualTo(profile == DataChannelProfiles.DedicatedUasc ? 0 : 2));
            });
            if (profile != DataChannelProfiles.DedicatedUasc)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(endpoint.UserIdentityTokens[0].PolicyId, Is.EqualTo("username"));
                    Assert.That(endpoint.UserIdentityTokens[0].TokenType, Is.EqualTo(UserTokenType.UserName));
                    Assert.That(endpoint.UserIdentityTokens[0].SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
                    Assert.That(endpoint.UserIdentityTokens[1].PolicyId, Is.EqualTo("issued"));
                    Assert.That(endpoint.UserIdentityTokens[1].TokenType, Is.EqualTo(UserTokenType.IssuedToken));
                    Assert.That(endpoint.UserIdentityTokens[1].IssuedTokenType, Is.EqualTo("urn:canonical-part30:token"));
                    Assert.That(endpoint.UserIdentityTokens[1].IssuerEndpointUrl, Is.EqualTo("opc.tcp://issuer.invalid:4840"));
                    Assert.That(endpoint.UserIdentityTokens[1].SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Aes128_Sha256_RsaOaep));
                });
            }
        }

        private static readonly string[] s_canonicalPart30Profile =
            ["http://opcfoundation.org/UA-Profile/Transport/pubsub-uasc"];
        private static readonly string[] s_canonicalPart30LegacyUrl = ["opc.wss://localhost:4840/flow/v1/shared"];
        private static readonly string[] s_canonicalPart30Profiles =
        [
            "http://opcfoundation.org/UA-Profile/Transport/pubsub-uasc",
            "http://opcfoundation.org/UA-Profile/Transport/flow-shared-uasc-v1",
            "http://opcfoundation.org/UA-Profile/Transport/flow-uasc-v1"
        ];
        private static readonly string[] s_canonicalPart30DiscoveryUrls =
            ["opc.tcp://localhost:4840/discovery", "opc.wss://localhost:4841/discovery"];
    }
}
