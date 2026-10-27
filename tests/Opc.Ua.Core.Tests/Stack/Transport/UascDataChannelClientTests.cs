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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.DataChannels;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Verifies explicit selection and authenticated evidence at the public client boundary.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    public sealed class UascDataChannelClientTests
    {
        [TestCase("/flow/v1/shared", DataChannelProfiles.DedicatedUasc)]
        [TestCase("/flow/v1/dedicated", DataChannelProfiles.SharedUasc)]
        [TestCase("/ordinary", DataChannelProfiles.SharedUasc)]
        [TestCase("/flow/v1/dedicated", Profiles.UaTcpTransport)]
        [TestCase("/flow/v1/shared/extra", DataChannelProfiles.SharedUasc)]
        [TestCase("/flow/v1/shared?fallback=true", DataChannelProfiles.SharedUasc)]
        public async Task MismatchedProfileAndResourceAreRejectedBeforeTransportCreation(
            string resource,
            string profile)
        {
            var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());
            var endpoint = new Uri("opc.tcp://localhost:4840" + resource);
            TransportChannelSettings settings = CreateSettings(endpoint, profile);

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ConnectAsync(endpoint, settings).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            registry.VerifyNoOtherCalls();
        }

        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        public async Task UnprotectedOrImplicitIntegrityIsRejectedBeforeTransportCreation(MessageSecurityMode mode)
        {
            var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());
            var endpoint = new Uri("opc.tcp://localhost:4840/flow/v1/dedicated");
            TransportChannelSettings settings = CreateSettings(endpoint, DataChannelProfiles.DedicatedUasc);
            settings.Description!.SecurityMode = mode;

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ConnectAsync(endpoint, settings).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
            registry.VerifyNoOtherCalls();
        }

        [Test]
        public async Task MissingContextFactoryCannotOpenAFlowEndpoint()
        {
            var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());
            var endpoint = new Uri("opc.tcp://localhost:4840/flow/v1/shared");
            TransportChannelSettings settings = CreateSettings(endpoint, DataChannelProfiles.SharedUasc);
            settings.DataChannels = null;

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ConnectAsync(endpoint, settings).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            registry.VerifyNoOtherCalls();
        }

        [Test]
        public async Task AnOrdinaryRegisteredBindingIsNotTreatedAsAFlowTransport()
        {
            var registry = new Mock<ITransportBindingRegistry>();
            var ordinary = new Mock<ITransportChannel>();
            registry.Setup(value => value.CreateChannel(Utils.UriSchemeOpcTcp, It.IsAny<ITelemetryContext>()))
                .Returns(ordinary.Object);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());
            var endpoint = new Uri("opc.tcp://localhost:4840/flow/v1/shared");

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ConnectAsync(endpoint, CreateSettings(endpoint, DataChannelProfiles.SharedUasc))
                    .ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            ordinary.Verify(value => value.Dispose(), Times.Once);
        }

        [Test]
        public async Task AnOpenedTransportWithoutAConfirmedContextIsNotReportedAsConnected()
        {
            var registry = new Mock<ITransportBindingRegistry>();
            var transport = new Mock<IDataChannelTransportChannel>();
            transport.Setup(value => value.OpenAsync(
                    It.IsAny<Uri>(), It.IsAny<TransportChannelSettings>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            registry.Setup(value => value.CreateChannel(Utils.UriSchemeOpcTcp, It.IsAny<ITelemetryContext>()))
                .Returns(transport.Object);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());
            var endpoint = new Uri("opc.tcp://localhost:4840/flow/v1/shared");

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ConnectAsync(endpoint, CreateSettings(endpoint, DataChannelProfiles.SharedUasc))
                    .ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            transport.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// Rejects untrusted RHE substitution before creating or claiming a transport.
        /// </summary>
        [TestCase("urn:foreign:server", "opc.tcp://localhost:4840/flow/v1/shared")]
        [TestCase("", "opc.tcp://localhost:4840/flow/v1/shared")]
        [TestCase("urn:selected:server", "opc.tcp://foreign:4840/flow/v1/shared")]
        [TestCase("urn:selected:server", "opc.tcp://localhost:4841/flow/v1/shared")]
        public async Task ReverseHandoffMustMatchSelectedServerAndEndpoint(string serverUri, string url)
        {
            var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());
            var endpoint = new Uri("opc.tcp://localhost:4840/flow/v1/shared");
            TransportChannelSettings settings = CreateSettings(endpoint, DataChannelProfiles.SharedUasc);
            settings.Description!.Server.ApplicationUri = "urn:selected:server";
            var waiting = new Mock<ITransportWaitingConnection>(MockBehavior.Strict);
            waiting.SetupGet(value => value.EndpointUrl).Returns(new Uri(url));
            waiting.SetupGet(value => value.ServerUri).Returns(serverUri);

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ConnectAsync(waiting.Object, settings).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            registry.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A reverse open must confirm the same authenticated context and must not open a new outbound connection.
        /// </summary>
        [Test]
        public async Task ReverseHandoffWithoutConfirmedContextIsDisposedWithoutOutboundFallback()
        {
            var endpoint = new Uri("opc.tcp://localhost:4840/flow/v1/shared");
            TransportChannelSettings settings = CreateSettings(endpoint, DataChannelProfiles.SharedUasc);
            settings.Description!.Server.ApplicationUri = "urn:selected:server";
            var waiting = new Mock<ITransportWaitingConnection>();
            waiting.SetupGet(value => value.EndpointUrl).Returns(endpoint);
            waiting.SetupGet(value => value.ServerUri).Returns("urn:selected:server");
            var transport = new Mock<IDataChannelTransportChannel>(MockBehavior.Strict);
            transport.Setup(value => value.OpenAsync(waiting.Object, settings, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            transport.SetupGet(value => value.DataChannels).Returns((DataChannelContext?)null);
            transport.Setup(value => value.Dispose());
            var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.CreateChannel(Utils.UriSchemeOpcTcp, It.IsAny<ITelemetryContext>()))
                .Returns(transport.Object);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ConnectAsync(waiting.Object, settings).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            transport.Verify(value => value.OpenAsync(
                waiting.Object, settings, It.IsAny<CancellationToken>()), Times.Once);
            transport.VerifyGet(value => value.DataChannels, Times.Once);
            transport.Verify(value => value.Dispose(), Times.Once);
            transport.VerifyNoOtherCalls();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DependencyInjectionKeepsTransportRegistriesIsolatedBetweenHosts(bool reverse)
        {
            var firstRegistry = new Mock<ITransportBindingRegistry>();
            var secondRegistry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
            firstRegistry.Setup(value => value.CreateChannel(Utils.UriSchemeOpcTcp, It.IsAny<ITelemetryContext>()))
                .Returns(Mock.Of<ITransportChannel>());
            var firstServices = new ServiceCollection();
            var secondServices = new ServiceCollection();
            firstServices.AddSingleton(firstRegistry.Object);
            secondServices.AddSingleton(secondRegistry.Object);
            firstServices.AddOpcUa().AddUascDataChannels();
            secondServices.AddOpcUa().AddUascDataChannels();
            using ServiceProvider first = firstServices.BuildServiceProvider();
            using ServiceProvider second = secondServices.BuildServiceProvider();
            IUascDataChannelClient firstClient = first.GetRequiredService<IUascDataChannelClient>();
            IUascDataChannelClient secondClient = second.GetRequiredService<IUascDataChannelClient>();
            var endpoint = new Uri("opc.tcp://localhost:4840/flow/v1/shared");
            TransportChannelSettings settings = CreateSettings(endpoint, DataChannelProfiles.SharedUasc);
            settings.Description!.Server.ApplicationUri = "urn:selected:server";
            var waiting = new Mock<ITransportWaitingConnection>();
            waiting.SetupGet(value => value.EndpointUrl).Returns(endpoint);
            waiting.SetupGet(value => value.ServerUri).Returns("urn:selected:server");

            await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (reverse)
                {
                    await firstClient.ConnectAsync(waiting.Object, settings).ConfigureAwait(false);
                }
                else
                {
                    await firstClient.ConnectAsync(endpoint, settings).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            Assert.That(firstClient, Is.Not.SameAs(secondClient));
            firstRegistry.Verify(value => value.CreateChannel(
                Utils.UriSchemeOpcTcp, It.IsAny<ITelemetryContext>()), Times.Once);
            secondRegistry.VerifyNoOtherCalls();
        }

        [Test]
        public async Task DisposalErrorsFromAnUndisposedTransportAreNotHidden()
        {
            var transport = new Mock<IDataChannelTransportChannel>();
            transport.SetupGet(value => value.IsDisposed).Returns(false);
            transport.Setup(value => value.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(Task.FromException(new ObjectDisposedException("unrelated-component"))));
            await using var connection =
                new UascDataChannelConnection(transport.Object, DataChannelCarrierKind.SharedUasc);

            ObjectDisposedException failure = (await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await connection.CloseAsync().ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(failure.ObjectName, Is.EqualTo("unrelated-component"));
            transport.Verify(value => value.Dispose(), Times.Once);
        }

#if NET6_0_OR_GREATER
        [Test]
        public void KeyEvidenceHashesSubjectPublicKeyInfoRatherThanTheCertificate()
        {
            using Certificate certificate = CertificateBuilder.Create("CN=FlowKeyEvidence").CreateForRSA();
            byte[] expected = SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo());

            ByteString actual = DataChannelCertificateIdentity.GetKeyThumbprint(certificate);

            Assert.Multiple(() =>
            {
                Assert.That(actual.ToArray(), Is.EqualTo(expected));
                Assert.That(actual.ToArray(), Is.Not.EqualTo(SHA256.HashData(certificate.RawData)));
            });
        }
#endif

        private static TransportChannelSettings CreateSettings(Uri endpoint, string profile)
        {
            return new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = endpoint.AbsoluteUri,
                    TransportProfileUri = profile,
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                },
                DataChannels = Mock.Of<IDataChannelContextFactory>()
            };
        }

        [TestCase("/pubsub/uasc", DataChannelProfiles.PubSubUasc, true)]
        [TestCase("/flow/v1/shared", DataChannelProfiles.SharedUasc, true)]
        [TestCase("/flow/v1/dedicated", DataChannelProfiles.DedicatedUasc, true)]
        [TestCase("", null, false)]
        [TestCase("/ordinary", null, false)]
        [TestCase("/pubsubscription", null, false)]
        [TestCase("/pubsub", null, true)]
        [TestCase("/pubsub/", null, true)]
        [TestCase("/pubsub/other", null, true)]
        [TestCase("/pubsub/uasc/", null, true)]
        [TestCase("/pubsub/uasc/extra", null, true)]
        [TestCase("/PubSub/Uasc", null, true)]
        [TestCase("/flow", null, true)]
        [TestCase("/flow/v2/shared", null, true)]
        public void CanonicalPart30ConstantsAndResourceMappingsAreExact(
            string resource, string? profile, bool reserved)
        {
            Assert.Multiple(() =>
            {
                Assert.That(DataChannelProfiles.PubSubUasc,
                    Is.EqualTo("http://opcfoundation.org/UA-Profile/Transport/pubsub-uasc"));
                Assert.That(DataChannelProfiles.PubSubResource, Is.EqualTo("/pubsub/uasc"));
                Assert.That(UascDataChannelEndpoint.GetKind(DataChannelProfiles.PubSubUasc),
                    Is.EqualTo(DataChannelCarrierKind.SharedUasc));
                Assert.That(UascDataChannelEndpoint.GetResourceProfile(resource), Is.EqualTo(profile));
                Assert.That(UascDataChannelEndpoint.IsFlowResource(resource), Is.EqualTo(reserved));
                Assert.That(UascDataChannelEndpoint.GetResourceKind(resource),
                    Is.EqualTo(profile == null ? (DataChannelCarrierKind?)null :
                        profile == DataChannelProfiles.DedicatedUasc
                            ? DataChannelCarrierKind.DedicatedUasc : DataChannelCarrierKind.SharedUasc));
            });
        }

        [TestCase("opc.tcp")]
        [TestCase("opc.wss")]
        public void CanonicalPart30ValidationAcceptsProtectedCanonicalEndpoints(string scheme)
        {
            var url = new Uri($"{scheme}://localhost:4840/pubsub/uasc");
            TransportChannelSettings settings = CreateSettings(url, DataChannelProfiles.PubSubUasc);
            DataChannelCarrierKind? selected = UascDataChannelEndpoint.Validate(
                url, settings.Description, settings.DataChannels, allowIntegrity: false);

            Assert.Multiple(() =>
            {
                Assert.That(selected, Is.EqualTo(DataChannelCarrierKind.SharedUasc));
                Assert.That(UascDataChannelEndpoint.GetResourceProfile(url.AbsolutePath),
                    Is.EqualTo("http://opcfoundation.org/UA-Profile/Transport/pubsub-uasc"));
                Assert.That(settings.Description!.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(settings.Description.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            });
        }

        [TestCaseSource(nameof(CanonicalPart30ResourceMismatches))]
        public async Task CanonicalPart30ClientRejectsActualOrDescribedResourceMismatchBeforeTransportCreationAsync(
            string scheme, bool changeDescription, string profile, string validResource, string invalidResource)
        {
            var valid = new Uri($"{scheme}://localhost:4840{validResource}");
            Uri invalid = invalidResource switch
            {
                "userinfo" => new Uri($"{scheme}://user@localhost:4840/pubsub/uasc"),
                "scheme" => new Uri("https://localhost:4840/pubsub/uasc"),
                "other-uasc-scheme" => new Uri(
                    $"{(scheme == "opc.tcp" ? "opc.wss" : "opc.tcp")}://localhost:4840/pubsub/uasc"),
                _ => new Uri($"{scheme}://localhost:4840{invalidResource}")
            };
            TransportChannelSettings settings = CreateSettings(valid, profile);
            if (profile != Profiles.UaTcpTransport)
            {
                Assert.That(UascDataChannelEndpoint.Validate(
                    valid, settings.Description, settings.DataChannels, false),
                    Is.EqualTo(profile == DataChannelProfiles.DedicatedUasc
                        ? DataChannelCarrierKind.DedicatedUasc : DataChannelCarrierKind.SharedUasc));
            }
            settings.Description!.EndpointUrl = changeDescription ? invalid.AbsoluteUri : valid.AbsoluteUri;

            await CanonicalPart30AssertRefusedAsync(
                changeDescription ? valid : invalid, settings, StatusCodes.BadNotSupported).ConfigureAwait(false);
        }

        [TestCase("opc.tcp")]
        [TestCase("opc.wss")]
        public async Task CanonicalPart30ClientRejectsMissingContextBeforeTransportCreationAsync(string scheme)
        {
            var url = new Uri($"{scheme}://localhost:4840/pubsub/uasc");
            TransportChannelSettings settings = CreateSettings(url, DataChannelProfiles.PubSubUasc);
            settings.DataChannels = null;

            await CanonicalPart30AssertRefusedAsync(url, settings, StatusCodes.BadNotSupported).ConfigureAwait(false);
        }

        [Test]
        public async Task CanonicalPart30ClientRejectsUnprotectedModeBeforeTransportCreationAsync(
            [Values("opc.tcp", "opc.wss")] string scheme,
            [Values(MessageSecurityMode.None, MessageSecurityMode.Sign)] MessageSecurityMode mode)
        {
            var url = new Uri($"{scheme}://localhost:4840/pubsub/uasc");
            TransportChannelSettings settings = CreateSettings(url, DataChannelProfiles.PubSubUasc);
            settings.Description!.SecurityMode = mode;

            await CanonicalPart30AssertRefusedAsync(
                url, settings, StatusCodes.BadSecurityModeRejected).ConfigureAwait(false);
        }

        [Test]
        public async Task CanonicalPart30ClientRejectsMissingAuthenticatedPolicyBeforeTransportCreationAsync(
            [Values("opc.tcp", "opc.wss")] string scheme,
            [Values(null, "", SecurityPolicies.None)] string? policy)
        {
            var url = new Uri($"{scheme}://localhost:4840/pubsub/uasc");
            TransportChannelSettings settings = CreateSettings(url, DataChannelProfiles.PubSubUasc);
            settings.Description!.SecurityPolicyUri = policy;

            await CanonicalPart30AssertRefusedAsync(
                url, settings, StatusCodes.BadSecurityPolicyRejected).ConfigureAwait(false);
        }

        [Test]
        public void CanonicalPart30ExplicitIntegrityOptInPreservesLegacyBehavior(
            [Values(DataChannelProfiles.PubSubUasc, DataChannelProfiles.SharedUasc)] string profile)
        {
            string resource = profile == DataChannelProfiles.PubSubUasc ? "/pubsub/uasc" : "/flow/v1/shared";
            var url = new Uri("opc.tcp://localhost:4840" + resource);
            TransportChannelSettings settings = CreateSettings(url, profile);
            Assert.DoesNotThrow(() => UascDataChannelEndpoint.ValidateProtection(
                MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256, false));
            ServiceResultException failure = Assert.Throws<ServiceResultException>(() =>
                UascDataChannelEndpoint.ValidateProtection(
                    MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256, false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeRejected));

            settings.Description!.SecurityMode = MessageSecurityMode.Sign;
            Assert.DoesNotThrow(() => UascDataChannelEndpoint.ValidateProtection(
                MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256, true));
            Assert.That(UascDataChannelEndpoint.Validate(url, settings.Description, settings.DataChannels, true),
                Is.EqualTo(DataChannelCarrierKind.SharedUasc));
            Assert.That(UascDataChannelEndpoint.GetResourceProfile(resource), Is.EqualTo(profile));
        }

        [TestCase("opc.tcp")]
        [TestCase("opc.wss")]
        public void CanonicalPart30ListenerValidationAcceptsOnlyProtectedExactProfileResourcePairs(string scheme)
        {
            var url = new Uri($"{scheme}://localhost:4840/pubsub/uasc");
            TransportChannelSettings channel = CreateSettings(url, DataChannelProfiles.PubSubUasc);
            EndpointDescription ordinary = CreateSettings(
                new Uri($"{scheme}://localhost:4840/ordinary"), Profiles.UaTcpTransport).Description!;
            EndpointDescription legacy = CreateSettings(
                new Uri($"{scheme}://localhost:4840/flow/v1/shared"), DataChannelProfiles.SharedUasc).Description!;
            var settings = new TransportListenerSettings { DataChannels = channel.DataChannels };
            settings.Descriptions = null;
            Assert.DoesNotThrow(() => UascDataChannelEndpoint.ValidateListener(settings));
            settings.Descriptions = [];
            Assert.DoesNotThrow(() => UascDataChannelEndpoint.ValidateListener(settings));
            settings.Descriptions = [ordinary];
            Assert.DoesNotThrow(() => UascDataChannelEndpoint.ValidateListener(settings));
            Assert.That(UascDataChannelEndpoint.Validate(
                new Uri(ordinary.EndpointUrl!), ordinary, settings.DataChannels, false), Is.Null);
            settings.Descriptions = [channel.Description!];
            Assert.DoesNotThrow(() => UascDataChannelEndpoint.ValidateListener(settings));
            settings.Descriptions = [ordinary, channel.Description!, legacy];
            Assert.DoesNotThrow(() => UascDataChannelEndpoint.ValidateListener(settings));
            Assert.That(settings.Descriptions, Has.Count.EqualTo(3));

            (string Defect, StatusCode Status)[] defects =
            [
                ("factory", StatusCodes.BadNotSupported),
                ("none", StatusCodes.BadSecurityModeRejected),
                ("sign", StatusCodes.BadSecurityModeRejected),
                ("policy", StatusCodes.BadSecurityPolicyRejected),
                ("legacy-profile", StatusCodes.BadNotSupported),
                ("/flow/v1/shared", StatusCodes.BadNotSupported),
                ("/pubsub/other", StatusCodes.BadNotSupported),
                ("/pubsub/uasc?fallback=true", StatusCodes.BadNotSupported),
                ("/pubsub/uasc#fragment", StatusCodes.BadNotSupported),
                ("userinfo", StatusCodes.BadNotSupported)
            ];
            foreach ((string defect, StatusCode status) in defects)
            {
                EndpointDescription candidate = (EndpointDescription)channel.Description!.Clone();
                settings.DataChannels = defect == "factory" ? null : channel.DataChannels;
                switch (defect)
                {
                    case "none":
                        candidate.SecurityMode = MessageSecurityMode.None;
                        break;
                    case "sign":
                        candidate.SecurityMode = MessageSecurityMode.Sign;
                        break;
                    case "policy":
                        candidate.SecurityPolicyUri = SecurityPolicies.None;
                        break;
                    case "legacy-profile":
                        candidate.TransportProfileUri = DataChannelProfiles.SharedUasc;
                        break;
                    case "userinfo":
                        candidate.EndpointUrl = $"{scheme}://user@localhost:4840/pubsub/uasc";
                        break;
                    case "factory":
                        break;
                    default:
                        candidate.EndpointUrl = $"{scheme}://localhost:4840{defect}";
                        break;
                }
                settings.Descriptions = [ordinary, candidate, legacy];
                ServiceResultException error = Assert.Throws<ServiceResultException>(
                    () => UascDataChannelEndpoint.ValidateListener(settings), defect)!;
                Assert.Multiple(() =>
                {
                    Assert.That(error.StatusCode, Is.EqualTo(status), defect);
                    Assert.That(settings.Descriptions, Has.Count.EqualTo(3), defect);
                    Assert.That(settings.Descriptions[0], Is.SameAs(ordinary), defect);
                });
            }
        }

        [TestCase("opc.tcp")]
        [TestCase("opc.wss")]
        public void CanonicalPart30ValidationAllowsDifferentAuthoritiesForExactResources(string scheme)
        {
            var actual = new Uri($"{scheme}://connection.invalid:4841/pubsub/uasc");
            var described = new Uri($"{scheme}://advertisement.invalid:4842/pubsub/uasc");
            TransportChannelSettings settings = CreateSettings(described, DataChannelProfiles.PubSubUasc);

            Assert.Multiple(() =>
            {
                Assert.That(UascDataChannelEndpoint.Validate(
                    actual, settings.Description, settings.DataChannels, false),
                    Is.EqualTo(DataChannelCarrierKind.SharedUasc));
                Assert.That(settings.Description!.EndpointUrl,
                    Is.EqualTo($"{scheme}://advertisement.invalid:4842/pubsub/uasc"));
                Assert.That(UascDataChannelEndpoint.GetResourceProfile(actual.AbsolutePath),
                    Is.EqualTo("http://opcfoundation.org/UA-Profile/Transport/pubsub-uasc"));
            });
        }

        private static System.Collections.Generic.IEnumerable<TestCaseData> CanonicalPart30ResourceMismatches()
        {
            (string Profile, string Valid, string Invalid)[] pairs =
            [
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc", "/pubsub"),
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc", "/pubsub/"),
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc", "/pubsub/other"),
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc", "/pubsub/uasc/"),
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc", "/pubsub/uasc/extra"),
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc", "/PubSub/Uasc"),
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc", "/pubsub/uasc?fallback=true"),
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc", "/pubsub/uasc#fragment"),
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc", "userinfo"),
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc", "scheme"),
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc", "other-uasc-scheme"),
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc", "/flow/v1/shared"),
                (DataChannelProfiles.PubSubUasc, "/pubsub/uasc", "/flow/v1/dedicated"),
                (DataChannelProfiles.SharedUasc, "/flow/v1/shared", "/pubsub/uasc"),
                (DataChannelProfiles.DedicatedUasc, "/flow/v1/dedicated", "/pubsub/uasc"),
                (Profiles.UaTcpTransport, "/ordinary", "/pubsub/uasc"),
                (Profiles.UaTcpTransport, "/ordinary", "/pubsub/other")
            ];
            foreach (string scheme in new[] { "opc.tcp", "opc.wss" })
            {
                foreach (bool description in new[] { false, true })
                {
                    foreach ((string profile, string valid, string invalid) in pairs)
                    {
                        yield return new TestCaseData(scheme, description, profile, valid, invalid);
                    }
                }
            }
        }

        private static async Task CanonicalPart30AssertRefusedAsync(
            Uri url, TransportChannelSettings settings, StatusCode status)
        {
            var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ConnectAsync(url, settings).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(status));
                Assert.That(registry.Invocations, Is.Empty);
            });
            registry.VerifyNoOtherCalls();
        }
    }
}
