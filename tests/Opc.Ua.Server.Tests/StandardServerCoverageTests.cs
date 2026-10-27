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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Opc.Ua.DataChannels;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.DataChannels;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Deterministic, offline unit tests for the guard clauses and state helpers
    /// of <see cref="StandardServer"/> that are reachable without starting the server.
    /// </summary>
    [TestFixture]
    [Category("StandardServer")]
    [Parallelizable(ParallelScope.All)]
    public class StandardServerCoverageTests
    {
        private sealed class TestableStandardServer : StandardServer
        {
            public TestableStandardServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            public TestableStandardServer(ITelemetryContext telemetry, TimeProvider timeProvider)
                : base(telemetry, timeProvider)
            {
            }

            public TimeProvider TimeProviderAccessor => TimeProvider;

            public void ValidateRequestPublic(RequestHeader requestHeader)
            {
                ValidateRequest(requestHeader);
            }

            public ValueTask<OperationContext> ValidateRequestInScopePublicAsync()
            {
                return ValidateRequestAsync(
                    null!,
                    new RequestHeader(),
                    RequestType.Read,
                    RequestLifetime.None);
            }

            public void SetServerStatePublic(ServerState state)
            {
                SetServerState(state);
            }

            public void SetServerErrorPublic(ServiceResult error)
            {
                SetServerError(error);
            }

            public void OnApplicationCertificateErrorPublic(ByteString clientCertificate, ServiceResult result)
            {
                OnApplicationCertificateError(clientCertificate, result);
            }

            public async Task InitializeAndDiscardServiceHostsAsync(
                ApplicationConfiguration configuration,
                ITransportBindingRegistry registry)
            {
                await InitializeServiceHostsAsync(configuration, registry).ConfigureAwait(false);
            }

            public void CanonicalPart30ConfigureListener(TransportListenerSettings settings, Uri endpoint)
            {
                ConfigureTransportListenerSettings(settings, endpoint);
            }
        }

        private sealed class TestTimeProvider : TimeProvider;

        private static TestableStandardServer CreateServer()
        {
            return new TestableStandardServer(NUnitTelemetryContext.Create());
        }

        [Test]
        public void ConstructorUsesSystemTimeProviderByDefault()
        {
            using TestableStandardServer server = CreateServer();

            Assert.That(server.TimeProviderAccessor, Is.SameAs(TimeProvider.System));
        }

        [Test]
        public void ConstructorUsesProvidedTimeProvider()
        {
            var custom = new TestTimeProvider();
            using var server = new TestableStandardServer(NUnitTelemetryContext.Create(), custom);

            Assert.That(server.TimeProviderAccessor, Is.SameAs(custom));
        }

        [Test]
        public void DisposeCanBeCalledMoreThanOnce()
        {
            TestableStandardServer server = CreateServer();

            server.Dispose();

            Assert.DoesNotThrow(server.Dispose);
        }

        [Test]
        public void LoadComplexTypesDefaultsToTrue()
        {
            using TestableStandardServer server = CreateServer();

            Assert.That(server.LoadComplexTypes, Is.True);
        }

        [Test]
        public void LoadComplexTypesCanBeDisabled()
        {
            using TestableStandardServer server = CreateServer();

            server.LoadComplexTypes = false;

            Assert.That(server.LoadComplexTypes, Is.False);
        }

        [Test]
        public void CurrentInstanceThrowsWhenNotStarted()
        {
            using TestableStandardServer server = CreateServer();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => _ = server.CurrentInstance);
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadServerHalted));
        }

        [Test]
        public void CurrentStateThrowsWhenNotStarted()
        {
            using TestableStandardServer server = CreateServer();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => _ = server.CurrentState);
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadServerHalted));
        }

        [Test]
        public void GetStatusThrowsWhenNotStarted()
        {
            using TestableStandardServer server = CreateServer();

#pragma warning disable CS0618 // GetStatus is obsolete but still exercised for coverage.
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => server.GetStatus());
#pragma warning restore CS0618
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadServerHalted));
        }

        [Test]
        public void ValidateRequestThrowsWhenNotStarted()
        {
            using TestableStandardServer server = CreateServer();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => server.ValidateRequestPublic(new RequestHeader()));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadServerHalted));
        }

        [Test]
        public async Task ValidateRequestInScopeThrowsWhenNotStartedAsync()
        {
            using TestableStandardServer server = CreateServer();

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await server
                    .ValidateRequestInScopePublicAsync()
                    .ConfigureAwait(false));

            Assert.That(
                exception.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadServerHalted));
        }

        [Test]
        public void SetServerStateThrowsWhenNotStarted()
        {
            using TestableStandardServer server = CreateServer();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => server.SetServerStatePublic(ServerState.Running));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadServerHalted));
        }

        [Test]
        public void SetServerErrorUpdatesServerErrorProperty()
        {
            using TestableStandardServer server = CreateServer();
            var error = new ServiceResult(StatusCodes.BadInternalError);

            server.SetServerErrorPublic(error);

            Assert.That(server.ServerError, Is.EqualTo(error));
        }

        [Test]
        public void ValidateRequestThrowsServerErrorWhenSet()
        {
            using TestableStandardServer server = CreateServer();
            server.SetServerErrorPublic(new ServiceResult(StatusCodes.BadInternalError));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => server.ValidateRequestPublic(new RequestHeader()));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInternalError));
        }

        [Test]
        public void SetServerStateThrowsServerErrorWhenSet()
        {
            using TestableStandardServer server = CreateServer();
            server.SetServerErrorPublic(new ServiceResult(StatusCodes.BadInternalError));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => server.SetServerStatePublic(ServerState.Running));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInternalError));
        }

        [Test]
        public void OnApplicationCertificateErrorMapsToSecurityChecksFailed()
        {
            StatusCode[] certStatusCodes =
            [
                StatusCodes.BadCertificateInvalid,
                StatusCodes.BadCertificateRevoked,
                StatusCodes.BadCertificateUntrusted,
                StatusCodes.BadCertificateIssuerRevoked,
                StatusCodes.BadCertificateRevocationUnknown,
                StatusCodes.BadCertificateChainIncomplete,
                StatusCodes.BadCertificateIssuerRevocationUnknown,
                StatusCodes.BadCertificatePolicyCheckFailed
            ];
            using TestableStandardServer server = CreateServer();

            foreach (StatusCode certStatusCode in certStatusCodes)
            {
                ServiceResultException ex = Assert.Throws<ServiceResultException>(
                    () => server.OnApplicationCertificateErrorPublic(
                        ByteString.Empty,
                        new ServiceResult(certStatusCode)));
                Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadSecurityChecksFailed));
            }
        }

        [Test]
        public void OnApplicationCertificateErrorPassesThroughUnmappedCode()
        {
            using TestableStandardServer server = CreateServer();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => server.OnApplicationCertificateErrorPublic(
                    ByteString.Empty,
                    new ServiceResult(StatusCodes.BadCertificateTimeInvalid)));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadCertificateTimeInvalid));
        }

        [Test]
        public void OnApplicationCertificateErrorMapsNestedRevocationErrorToSecurityChecksFailed()
        {
            // the certificate validator nests every failed check; an expired certificate whose
            // issuer has no revocation list must not reveal the revocation state (Part 4 §6.1.3).
            using TestableStandardServer server = CreateServer();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => server.OnApplicationCertificateErrorPublic(
                    ByteString.Empty,
                    new ServiceResult(
                        StatusCodes.BadCertificateTimeInvalid,
                        new ServiceResult(StatusCodes.BadCertificateIssuerRevocationUnknown))));
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadSecurityChecksFailed));
        }

        [TestCase("opc.https://localhost:62540/TestServer", "AddHttpsTransport")]
        [TestCase("opc.quic://localhost:62540/TestServer", "AddQuicTransport")]
        public async Task InitializeServiceHostsThrowsForUnregisteredScheme(
            string endpointUrl,
            string registrationMethod)
        {
            using TestableStandardServer server = CreateServer();
            ApplicationConfiguration configuration = CreateConfiguration(endpointUrl);
            var registry = new DefaultTransportBindingRegistry();

            InvalidOperationException ex = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await server.InitializeAndDiscardServiceHostsAsync(configuration, registry)
                    .ConfigureAwait(false)))!;

            Assert.That(ex.Message, Does.Contain(new Uri(endpointUrl).Scheme));
            Assert.That(ex.Message, Does.Contain(registrationMethod));
        }

        [Test]
        public async Task InitializeServiceHostsUsesRegisteredFactoryAsync()
        {
            using TestableStandardServer server = CreateServer();
            ApplicationConfiguration configuration = CreateConfiguration(
                "opc.tcp://localhost:62541/TestServer");
            var registry = new DefaultTransportBindingRegistry();
            var factory = new FakeTcpListenerFactory();
            registry.RegisterListenerFactory(factory);

            await server.InitializeAndDiscardServiceHostsAsync(configuration, registry)
                .ConfigureAwait(false);

            Assert.That(factory.CreateServiceHostCallCount, Is.EqualTo(1));
        }

        private static ApplicationConfiguration CreateConfiguration(string endpointUrl)
        {
            return new ApplicationConfiguration
            {
                ApplicationName = "TestServer",
                ApplicationUri = "urn:localhost:UA:TestServer",
                ProductUri = "uri:opcfoundation.org:TestServer",
                ServerConfiguration = new ServerConfiguration
                {
                    BaseAddresses = [endpointUrl]
                }
            };
        }

        private sealed class FakeTcpListenerFactory : ITransportListenerFactory
        {
            public string UriScheme => Utils.UriSchemeOpcTcp;

            public int CreateServiceHostCallCount { get; private set; }

            public ITransportListener Create(ITelemetryContext telemetry)
            {
                throw new NotSupportedException();
            }

            public ValueTask<List<EndpointDescription>> CreateServiceHostAsync(
                ServerBase serverBase,
                IDictionary<string, ServiceHost> hosts,
                ApplicationConfiguration configuration,
                ArrayOf<string> baseAddresses,
                ApplicationDescription serverDescription,
                ArrayOf<ServerSecurityPolicy> securityPolicies,
                ICertificateRegistry serverCertificates,
                ICertificateValidatorEx clientCertificateValidator,
                CancellationToken ct = default)
            {
                CreateServiceHostCallCount++;
                return new ValueTask<List<EndpointDescription>>([]);
            }
        }

        [Test]
        public async Task CanonicalPart30PrepareDataChannelEndpointsSelectsExactProfileAndProtectedDescriptions(
            [Values("opc.tcp", "opc.wss")] string scheme,
            [Values("/pubsub/uasc", "/flow/v1/shared", "/flow/v1/dedicated")] string resource)
        {
            using Certificate certificate = CanonicalPart30Certificate();
            await using ServerDataChannelProvider provider = CanonicalPart30Provider();
            string expectedProfile = resource switch
            {
                "/pubsub/uasc" => "http://opcfoundation.org/UA-Profile/Transport/pubsub-uasc",
                "/flow/v1/shared" => "http://opcfoundation.org/UA-Profile/Transport/flow-shared-uasc-v1",
                _ => "http://opcfoundation.org/UA-Profile/Transport/flow-uasc-v1"
            };
            Assert.That(CanonicalPart30Prepare([], provider), Is.Empty);
            foreach (string layout in new[] { "ineligible", "singleton", "surrounded" })
            {
                EndpointDescription none = CanonicalPart30Endpoint(certificate, scheme, resource, Profiles.UaTcpTransport);
                none.SecurityMode = MessageSecurityMode.None;
                EndpointDescription sign = (EndpointDescription)none.Clone();
                sign.SecurityMode = MessageSecurityMode.Sign;
                EndpointDescription first = CanonicalPart30Endpoint(certificate, scheme, resource, Profiles.UaTcpTransport);
                EndpointDescription second = (EndpointDescription)first.Clone();
                second.SecurityPolicyUri = SecurityPolicies.Aes128_Sha256_RsaOaep;
                EndpointDescription ordinary = CanonicalPart30Endpoint(certificate, scheme, "/ordinary", Profiles.UaTcpTransport);
                List<EndpointDescription> descriptions = layout switch
                {
                    "ineligible" => [none, sign, ordinary],
                    "singleton" => [first, ordinary],
                    _ => [none, first, sign, second, ordinary]
                };
                EndpointDescription[] eligible = layout switch
                {
                    "ineligible" => [],
                    "singleton" => [first],
                    _ => [first, second]
                };

                List<EndpointDescription> prepared = CanonicalPart30Prepare(descriptions, provider);

                Assert.Multiple(() =>
                {
                    Assert.That(prepared, Is.EquivalentTo(eligible), layout);
                    Assert.That(descriptions, Is.EquivalentTo(eligible.Append(ordinary)), layout);
                    Assert.That(ordinary.TransportProfileUri, Is.EqualTo(Profiles.UaTcpTransport), layout);
                    Assert.That(provider.GetEndpoints(), Is.Empty, "Preparation alone does not advertise a listener.");
                });
                foreach (EndpointDescription endpoint in prepared)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(endpoint.EndpointUrl, Is.EqualTo($"{scheme}://localhost:4840{resource}"), layout);
                        Assert.That(endpoint.TransportProfileUri, Is.EqualTo(expectedProfile), layout);
                        Assert.That(endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt), layout);
                        Assert.That(endpoint.SecurityPolicyUri,
                            Is.EqualTo(ReferenceEquals(endpoint, first) ? SecurityPolicies.Basic256Sha256 :
                                SecurityPolicies.Aes128_Sha256_RsaOaep), layout);
                        Assert.That(endpoint.ServerCertificate.ToArray(), Is.EqualTo(certificate.RawData), layout);
                        Assert.That(endpoint.UserIdentityTokens.Count,
                            Is.EqualTo(resource == "/flow/v1/dedicated" ? 0 : 1), layout);
                    });
                    if (resource != "/flow/v1/dedicated")
                    {
                        CanonicalPart30AssertToken(endpoint);
                    }
                }
            }
        }

        [Test]
        public async Task CanonicalPart30PrepareDataChannelEndpointsClonesServerAndFiltersDiscoveryUrls(
            [Values("opc.tcp", "opc.wss")] string scheme, [Values] bool reservedFirst)
        {
            using Certificate certificate = CanonicalPart30Certificate();
            await using ServerDataChannelProvider provider = CanonicalPart30Provider();
            EndpointDescription endpoint = CanonicalPart30Endpoint(
                certificate, scheme, "/pubsub/uasc", Profiles.UaTcpTransport);
            string[] urls =
            [
                "opc.tcp://localhost:4840/pubsub/uasc",
                "opc.tcp://localhost:4840/discovery",
                "opc.wss://localhost:4841/discovery",
                "opc.wss://localhost:4840/pubsub/other",
                "opc.tcp://localhost:4840/flow/v1/shared"
            ];
            if (!reservedFirst)
            {
                Array.Reverse(urls);
            }
            ApplicationDescription original = endpoint.Server;
            original.DiscoveryUrls = [.. urls];
            List<EndpointDescription> descriptions = [endpoint];

            List<EndpointDescription> prepared = CanonicalPart30Prepare(descriptions, provider);

            Assert.Multiple(() =>
            {
                Assert.That(prepared, Has.Count.EqualTo(1));
                Assert.That(prepared[0], Is.SameAs(endpoint));
                Assert.That(endpoint.Server, Is.Not.SameAs(original));
                Assert.That(endpoint.Server.DiscoveryUrls.ToArray(), Is.EquivalentTo(s_canonicalPart30DiscoveryUrls));
                Assert.That(original.DiscoveryUrls, Is.EqualTo(urls));
                Assert.That(endpoint.Server.ApplicationUri, Is.EqualTo("urn:canonical-part30:server"));
                Assert.That(endpoint.Server.ApplicationName.Text, Is.EqualTo("Part30 Server"));
                Assert.That(endpoint.Server.ProductUri, Is.EqualTo("urn:canonical-part30:product"));
                Assert.That(endpoint.Server.ApplicationType, Is.EqualTo(ApplicationType.Server));
            });
            endpoint.Server.ApplicationUri = "urn:changed:server";
            endpoint.Server.DiscoveryUrls = [];
            Assert.That(original.ApplicationUri, Is.EqualTo("urn:canonical-part30:server"));
            Assert.That(original.DiscoveryUrls, Is.EqualTo(urls));
        }

        [TestCase("missing-provider")]
        [TestCase("unsupported-resource")]
        [TestCase("unsupported-scheme")]
        public async Task CanonicalPart30PrepareDataChannelEndpointsRejectsMissingProviderAndUnsupportedResource(string defect)
        {
            using Certificate certificate = CanonicalPart30Certificate();
            await using ServerDataChannelProvider provider = CanonicalPart30Provider();
            string resource = defect == "unsupported-resource" ? "/pubsub/other" : "/pubsub/uasc";
            string scheme = defect == "unsupported-scheme" ? "https" : "opc.tcp";
            EndpointDescription endpoint = CanonicalPart30Endpoint(certificate, scheme, resource, Profiles.UaTcpTransport);
            ApplicationDescription originalServer = endpoint.Server;
            List<EndpointDescription> descriptions = [endpoint];

            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                CanonicalPart30Prepare(descriptions, defect == "missing-provider" ? null : provider))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(descriptions, Has.Count.EqualTo(1));
                Assert.That(descriptions[0], Is.SameAs(endpoint));
                Assert.That(endpoint.EndpointUrl, Is.EqualTo($"{scheme}://localhost:4840{resource}"));
                Assert.That(endpoint.TransportProfileUri, Is.EqualTo(Profiles.UaTcpTransport));
                Assert.That(endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(endpoint.Server, Is.SameAs(originalServer));
                Assert.That(provider.GetEndpoints(), Is.Empty);
            });
        }

        [Test]
        public async Task CanonicalPart30IncludeDataChannelEndpointsExcludesReservedOrdinaryFallback(
            [Values] bool missingProvider, [Values] bool reverseOrder)
        {
            using Certificate certificate = CanonicalPart30Certificate();
            await using ServerDataChannelProvider provider = CanonicalPart30Provider();
            using IDisposable unavailable = provider.RegisterEndpoint(CanonicalPart30Endpoint(
                certificate, "opc.tcp", "/pubsub/uasc", DataChannelProfiles.PubSubUasc), static () => false);
            using TestableStandardServer server = CreateServer();
            server.DataChannelProvider = missingProvider ? null : provider;
            EndpointDescription ordinary = CanonicalPart30Endpoint(
                certificate, "opc.tcp", "/ordinary", Profiles.UaTcpTransport);
            EndpointDescription[] leaks =
            [
                CanonicalPart30Endpoint(certificate, "opc.tcp", "/ordinary", DataChannelProfiles.PubSubUasc),
                CanonicalPart30Endpoint(certificate, "opc.tcp", "/pubsub/uasc", Profiles.UaTcpTransport),
                CanonicalPart30Endpoint(certificate, "opc.wss", "/pubsub/other", Profiles.UaTcpTransport)
            ];
            Assert.That(CanonicalPart30Include(server, [], []), Is.Empty);
            Assert.That(CanonicalPart30Include(server, [ordinary], [])[0], Is.SameAs(ordinary));
            foreach (EndpointDescription leak in leaks)
            {
                Assert.That(CanonicalPart30Include(server, [leak], []), Is.Empty, leak.EndpointUrl);
                Assert.That(leak.ServerCertificate.ToArray(), Is.EqualTo(certificate.RawData));
            }
            ArrayOf<EndpointDescription> input = reverseOrder
                ? [ordinary, leaks[2], leaks[1], leaks[0]] : [leaks[0], leaks[1], leaks[2], ordinary];

            ArrayOf<EndpointDescription> result = CanonicalPart30Include(server, input, []);

            Assert.Multiple(() =>
            {
                Assert.That(result.Count, Is.EqualTo(1));
                Assert.That(result[0], Is.SameAs(ordinary));
                Assert.That(result[0].EndpointUrl, Is.EqualTo("opc.tcp://localhost:4840/ordinary"));
                Assert.That(input.Count, Is.EqualTo(4));
                Assert.That(provider.GetEndpoints(), Is.Empty);
                Assert.That(CanonicalPart30Include(server, input, [DataChannelProfiles.PubSubUasc]), Is.Empty);
            });
        }

        [TestCase("opc.tcp")]
        [TestCase("opc.wss")]
        public async Task CanonicalPart30IncludeDataChannelEndpointsMergesOnlyExactProfileAdvertisements(string scheme)
        {
            using Certificate certificate = CanonicalPart30Certificate();
            await using ServerDataChannelProvider provider = CanonicalPart30Provider();
            using TestableStandardServer server = CreateServer();
            server.DataChannelProvider = provider;
            using IDisposable canonical = provider.RegisterEndpoint(CanonicalPart30Endpoint(
                certificate, scheme, "/pubsub/uasc", DataChannelProfiles.PubSubUasc), static () => true);
            using IDisposable legacy = provider.RegisterEndpoint(CanonicalPart30Endpoint(
                certificate, scheme, "/flow/v1/shared", DataChannelProfiles.SharedUasc), static () => true);
            EndpointDescription ordinary = CanonicalPart30Endpoint(certificate, scheme, "/ordinary", Profiles.UaTcpTransport);
            ArrayOf<EndpointDescription> input = [ordinary];

            ArrayOf<EndpointDescription> all = CanonicalPart30Include(server, input, []);
            Assert.That(all.ConvertAll(value => value.EndpointUrl).ToArray(), Is.EquivalentTo(new[]
            {
                $"{scheme}://localhost:4840/ordinary", $"{scheme}://localhost:4840/pubsub/uasc",
                $"{scheme}://localhost:4840/flow/v1/shared"
            }));
            Assert.That(all.ToList().Single(value => value.TransportProfileUri == Profiles.UaTcpTransport), Is.SameAs(ordinary));
            foreach ((string profile, string path) in new[]
            {
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc"),
                (DataChannelProfiles.SharedUasc, "/flow/v1/shared")
            })
            {
                ArrayOf<EndpointDescription> selected = CanonicalPart30Include(server, input, [profile]);
                Assert.That(selected.Count, Is.EqualTo(1));
                EndpointDescription endpoint = selected[0];
                Assert.Multiple(() =>
                {
                    Assert.That(endpoint.EndpointUrl, Is.EqualTo($"{scheme}://localhost:4840{path}"));
                    Assert.That(endpoint.TransportProfileUri, Is.EqualTo(profile));
                    Assert.That(endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    Assert.That(endpoint.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
                    Assert.That(endpoint.ServerCertificate.ToArray(), Is.EqualTo(certificate.RawData));
                    Assert.That(endpoint.Server.ApplicationUri, Is.EqualTo("urn:canonical-part30:server"));
                    Assert.That(endpoint.Server.DiscoveryUrls, Is.EqualTo(s_canonicalPart30OrdinaryDiscoveryUrl));
                });
                CanonicalPart30AssertToken(endpoint);
            }
            Assert.Multiple(() =>
            {
                Assert.That(CanonicalPart30Include(server, input, ["urn:unknown:profile"]), Is.Empty);
                Assert.That(CanonicalPart30Include(server, input, [Profiles.UaTcpTransport]), Is.EqualTo(new[] { ordinary }));
                Assert.That(CanonicalPart30Include(server, input, [DataChannelProfiles.PubSubUasc, DataChannelProfiles.SharedUasc])
                    .ConvertAll(value => value.TransportProfileUri).ToArray(),
                    Is.EquivalentTo(new[] { DataChannelProfiles.PubSubUasc, DataChannelProfiles.SharedUasc }));
                Assert.That(input.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task CanonicalPart30ConfigureTransportListenerSettingsInjectsFactoryOnlyForValidFlowAdvertisements(
            [Values("opc.tcp", "opc.wss")] string scheme,
            [Values("canonical", "legacy", "ordinary", "empty", "absent")] string advertisement)
        {
            await using ServerDataChannelProvider provider = CanonicalPart30Provider();
            using TestableStandardServer server = CreateServer();
            server.DataChannelProvider = provider;
            bool flow = advertisement is "canonical" or "legacy";
            string profile = advertisement switch
            {
                "canonical" => DataChannelProfiles.PubSubUasc,
                "legacy" => DataChannelProfiles.SharedUasc,
                _ => Profiles.UaTcpTransport
            };
            string resource = advertisement switch
            {
                "canonical" => "/pubsub/uasc",
                "legacy" => "/flow/v1/shared",
                _ => "/ordinary"
            };
            var description = new EndpointDescription
            {
                EndpointUrl = $"{scheme}://localhost:4840{resource}",
                TransportProfileUri = profile,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            var settings = new TransportListenerSettings
            {
                Descriptions = advertisement == "absent" ? null : advertisement == "empty" ? [] : [description],
                MaxChannelCount = 17
            };
            server.CanonicalPart30ConfigureListener(settings, new Uri($"{scheme}://localhost:4840"));

            Assert.Multiple(() =>
            {
                Assert.That(settings.DataChannels, Is.SameAs(flow ? provider : null));
                Assert.That(settings.AllowDataChannelIntegrity, Is.False);
                Assert.That(settings.MaxChannelCount, Is.EqualTo(17));
                Assert.That(description.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(description.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
                Assert.That(description.TransportProfileUri, Is.EqualTo(profile));
                Assert.That(provider.GetEndpoints(), Is.Empty, "Configuration does not establish availability.");
            });
        }

        [Test]
        public async Task CanonicalPart30InvalidHostingDisposesUnopenedListenerAndPreservesErrorAsync(
            [Values("opc.tcp", "opc.wss")] string scheme, [Values] bool disposeFails)
        {
            using TestableStandardServer server = CreateServer();
            var listener = new Mock<ITransportListener>(MockBehavior.Strict);
            listener.Setup(value => value.DisposeAsync()).Returns(disposeFails
                ? new ValueTask(Task.FromException(new InvalidOperationException("Cleanup failed.")))
                : default);
            var endpoint = new EndpointDescription
            {
                EndpointUrl = $"{scheme}://localhost:4840/pubsub/uasc",
                TransportProfileUri = DataChannelProfiles.PubSubUasc,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            List<EndpointDescription> descriptions = [endpoint];

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await server.CreateServiceHostEndpointAsync(
                    new Uri(endpoint.EndpointUrl!), descriptions, new EndpointConfiguration(),
                    listener.Object, Mock.Of<ICertificateValidatorEx>()).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(descriptions, Has.Count.EqualTo(1));
                Assert.That(descriptions[0], Is.SameAs(endpoint));
            });
            listener.Verify(value => value.OpenAsync(It.IsAny<Uri>(), It.IsAny<TransportListenerSettings>(),
                It.IsAny<ITransportListenerCallback>(), It.IsAny<CancellationToken>()), Times.Never);
            listener.Verify(value => value.DisposeAsync(), Times.Once);
            listener.VerifyNoOtherCalls();
        }

        private static List<EndpointDescription> CanonicalPart30Prepare(
            List<EndpointDescription> endpoints, ServerDataChannelProvider? provider)
        {
            MethodInfo? method = typeof(StandardServer).GetMethod(
                "PrepareDataChannelEndpoints", BindingFlags.Static | BindingFlags.NonPublic,
                null, [typeof(List<EndpointDescription>), typeof(ServerDataChannelProvider)], null);
            Assert.That(method, Is.Not.Null);
            try
            {
                return (List<EndpointDescription>)method!.Invoke(null, [endpoints, provider])!;
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private static ArrayOf<EndpointDescription> CanonicalPart30Include(
            StandardServer server, ArrayOf<EndpointDescription> ordinary, ArrayOf<string> profiles)
        {
            MethodInfo? method = typeof(StandardServer).GetMethod(
                "IncludeDataChannelEndpoints", BindingFlags.Instance | BindingFlags.NonPublic,
                null, [typeof(ArrayOf<EndpointDescription>), typeof(ArrayOf<string>)], null);
            Assert.That(method, Is.Not.Null);
            try
            {
                return (ArrayOf<EndpointDescription>)method!.Invoke(server, [ordinary, profiles])!;
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
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
            return CertificateBuilder.Create("CN=CanonicalPart30StandardServer")
                .SetNotBefore(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2036, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .AddExtension(new X509SubjectAltNameExtension("urn:canonical-part30:server", ["localhost"]))
                .CreateForRSA();
        }

        private static EndpointDescription CanonicalPart30Endpoint(
            Certificate certificate, string scheme, string resource, string profile)
        {
            return new EndpointDescription
            {
                EndpointUrl = $"{scheme}://localhost:4840{resource}",
                TransportProfileUri = profile,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                ServerCertificate = ByteString.From(certificate.RawData),
                Server = new ApplicationDescription
                {
                    ApplicationUri = "urn:canonical-part30:server",
                    ApplicationName = new LocalizedText("Part30 Server"),
                    ProductUri = "urn:canonical-part30:product",
                    ApplicationType = ApplicationType.Server,
                    DiscoveryUrls = ["opc.tcp://localhost:4840/discovery"]
                },
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "issued", TokenType = UserTokenType.IssuedToken,
                        IssuedTokenType = "urn:canonical-part30:token", IssuerEndpointUrl = "opc.tcp://issuer.invalid:4840",
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    }
                ]
            };
        }

        private static void CanonicalPart30AssertToken(EndpointDescription endpoint)
        {
            Assert.That(endpoint.UserIdentityTokens.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(endpoint.UserIdentityTokens[0].PolicyId, Is.EqualTo("issued"));
                Assert.That(endpoint.UserIdentityTokens[0].TokenType, Is.EqualTo(UserTokenType.IssuedToken));
                Assert.That(endpoint.UserIdentityTokens[0].IssuedTokenType, Is.EqualTo("urn:canonical-part30:token"));
                Assert.That(endpoint.UserIdentityTokens[0].IssuerEndpointUrl, Is.EqualTo("opc.tcp://issuer.invalid:4840"));
                Assert.That(endpoint.UserIdentityTokens[0].SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            });
        }

        private static readonly string[] s_canonicalPart30DiscoveryUrls =
            ["opc.tcp://localhost:4840/discovery", "opc.wss://localhost:4841/discovery"];
        private static readonly string[] s_canonicalPart30OrdinaryDiscoveryUrl = ["opc.tcp://localhost:4840/discovery"];
    }
}
