/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies actual managed-server channel identity and owned-session cleanup.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class SessionDeviceRegistrarManagedApplicationChannelVerifierTests
    {
        /// <summary>
        /// Creates the fixture's installed application credential.
        /// </summary>
        [OneTimeSetUp]
        public void SetUp()
        {
            m_certificate = CertificateBuilder.Create("CN=Managed Application")
                .AddExtension(new X509SubjectAltNameExtension(ApplicationUri, ["localhost", Utils.GetHostName()]))
                .SetRSAKeySize(2048).CreateForRSA();
        }

        /// <summary>
        /// Releases the fixture-owned key.
        /// </summary>
        [OneTimeTearDown]
        public void TearDown()
        {
            m_certificate.Dispose();
        }

        /// <summary>
        /// Fluent guards preserve parameter names and validate services before the factory.
        /// </summary>
        [TestCase(true, true, "services")]
        [TestCase(true, false, "configurationFactory")]
        [TestCase(false, true, "services")]
        [TestCase(false, false, "factory")]
        public void ManagedRegistrationNullGuardsPreserveParameterNames(
            bool channelVerifier, bool nullServices, string parameterName)
        {
            IServiceCollection services = nullServices ? null! : new ServiceCollection();
            ArgumentNullException? error = Assert.Throws<ArgumentNullException>(() =>
            {
                if (channelVerifier)
                {
                    services.AddDeviceRegistrarManagedApplicationChannelVerifier(null!);
                }
                else
                {
                    services.AddDeviceRegistrarManagedApplicationProvider(null!);
                }
            });

            Assert.That(error!.ParamName, Is.EqualTo(parameterName));
        }

        /// <summary>
        /// Constructor and snapshot guards retain their public argument names.
        /// </summary>
        [TestCase(true, "configuration")]
        [TestCase(false, "endpoint")]
        public void ManagedVerifierSynchronousNullGuardsPreserveParameterNames(bool constructor, string parameterName)
        {
            var factory = Factory(null);
            ArgumentNullException? error = Assert.Throws<ArgumentNullException>(() =>
            {
                if (constructor)
                {
                    _ = new SessionDeviceRegistrarManagedApplicationChannelVerifier(null!, factory.Object);
                }
                else
                {
                    _ = SessionDeviceRegistrarManagedApplicationChannelVerifier.SnapshotEndpoint(null!);
                }
            });

            Assert.That(error!.ParamName, Is.EqualTo(parameterName));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Verification validates application before target without invoking the session factory.
        /// </summary>
        [TestCase(true, "application")]
        [TestCase(false, "target")]
        public async Task ManagedVerifierAsyncNullGuardsPreserveParameterNames(
            bool nullApplication, string parameterName)
        {
            var factory = Factory(null);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);
            ApplicationDescription application = nullApplication ? null! : Application();

            ArgumentNullException? error = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await verifier.VerifyAsync(application, null!, default).ConfigureAwait(false));

            Assert.That(error!.ParamName, Is.EqualTo(parameterName));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Unsupported HTTPS Web API and WSS profiles fail before the injected session factory receives credentials.
        /// </summary>
        [TestCase(Profiles.HttpsOpenApiTransport, "opc.https+webapi://managed:4840")]
        [TestCase(Profiles.UaWssTransport, "opc.wss://managed:4840")]
        [TestCase(Profiles.WssOpenApiTransport, "opc.wss+openapi://managed:4840")]
        public async Task ManagedVerifierRejectsTransportsWithoutPreActivationPeerBinding(
            string transportProfile,
            string endpointUrl)
        {
            var factory = Factory(null);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);
            var endpoint = new EndpointDescription
            {
                EndpointUrl = endpointUrl,
                Server = Application(),
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                TransportProfileUri = transportProfile,
                ServerCertificate = ByteString.From(m_certificate.RawData.AsSpan())
            };
            DeviceRegistrarManagedApplicationTarget target = CreateTarget(
                endpoint, ByteString.From(m_certificate.RawData.AsSpan()));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await verifier.VerifyAsync(
                    Application(), target, ByteString.From(m_certificate.RawData.AsSpan()))
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The verifier rejects HTTPS channels without an enforceable per-handshake TLS policy.
        /// </summary>
        [TestCase(Profiles.HttpsBinaryTransport, "https://managed:4840")]
        [TestCase(Profiles.HttpsJsonTransport, "opc.https://managed:4840")]
        public async Task HttpsEndpointRequiresPerHandshakeTlsCapabilityBeforeSessionCreation(
            string transportProfile,
            string endpointUrl)
        {
            ByteString applicationCertificate = ByteString.From(m_certificate.RawData.AsSpan());
            var endpoint = new EndpointDescription
            {
                EndpointUrl = endpointUrl,
                Server = Application(),
                SecurityMode = MessageSecurityMode.Sign,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                TransportProfileUri = transportProfile,
                ServerCertificate = applicationCertificate
            };
            using var channel = new Mock<ITransportChannel>().Object;
            Mock.Get(channel).SetupGet(value => value.EndpointDescription).Returns(endpoint);
            var factory = Factory(channel);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await verifier.VerifyAsync(
                    Application(), CreateTarget(endpoint, applicationCertificate), applicationCertificate)
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            factory.Verify(value => value.CreateChannelAsync(
                It.IsAny<ApplicationConfiguration>(), It.IsAny<ITransportWaitingConnection>(),
                It.IsAny<ConfiguredEndpoint>(), false, true, It.IsAny<CancellationToken>()), Times.Once);
            factory.Verify(value => value.Create(
                It.IsAny<ITransportChannel>(), It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ConfiguredEndpoint>(), null, null, It.IsAny<ArrayOf<EndpointDescription>>(),
                It.IsAny<ArrayOf<string>>()), Times.Never);
            m_lastCreatedSession!.Verify(value => value.OpenAsync(
                It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// HTTPS selects TLS trust or a separate TLS pin before the URI-bound session can receive credentials.
        /// </summary>
        [TestCase(Profiles.HttpsBinaryTransport, "https://localhost:4840", true)]
        [TestCase(Profiles.HttpsBinaryTransport, "https://localhost:4840", false)]
        [TestCase(Profiles.HttpsJsonTransport, "opc.https://localhost:4840", true)]
        [TestCase(Profiles.HttpsJsonTransport, "opc.https://localhost:4840", false)]
        public async Task HttpsSelectsIndependentTlsPolicyBeforeOpeningPinnedApplicationSession(
            string transportProfile,
            string endpointUrl,
            bool pinTls)
        {
            ByteString applicationCertificate = ByteString.From(m_certificate.RawData.AsSpan());
            using Certificate tlsCertificate = CertificateBuilder.Create("CN=TLS Terminator")
                .AddExtension(new X509SubjectAltNameExtension("urn:test:tls", ["localhost"]))
                .CreateForRSA();
            ByteString tlsPin = pinTls ? ByteString.From(tlsCertificate.RawData.AsSpan()) : default;
            EndpointDescription endpoint = HttpsEndpoint(applicationCertificate);
            endpoint.TransportProfileUri = transportProfile;
            endpoint.EndpointUrl = endpointUrl;
            var channel = new Mock<ITransportChannel>(MockBehavior.Strict);
            bool tlsPolicySelected = false;
            channel.As<IHttpsServerCertificateValidation>()
                .Setup(value => value.RequireServerCertificateValidation(tlsPin))
                .Callback(() => tlsPolicySelected = true);
            channel.SetupGet(value => value.EndpointDescription).Returns(endpoint);
            channel.Setup(value => value.Dispose());
            var factory = Factory(channel.Object);
            m_lastCreatedSession!.Setup(value => value.OpenAsync(
                It.IsAny<string>(), 60000, It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(),
                true, true, It.IsAny<CancellationToken>()))
                .Callback(() => Assert.That(tlsPolicySelected, Is.True))
                .Returns(Task.CompletedTask);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            await verifier.VerifyAsync(
                Application(),
                CreateTarget(endpoint, applicationCertificate) with { HttpsServerCertificate = tlsPin },
                applicationCertificate).ConfigureAwait(false);

            channel.As<IHttpsServerCertificateValidation>().Verify(
                value => value.RequireServerCertificateValidation(tlsPin), Times.Once);
            m_lastCreatedSession.As<IExpectedServerApplicationUriSession>().Verify(
                value => value.RequireServerApplicationUri(ApplicationUri), Times.Once);
            m_lastCreatedSession.Verify(value => value.CloseAsync(
                5000, true, It.IsAny<CancellationToken>()), Times.Once);
            channel.VerifyGet(value => value.ServerChannelCertificate, Times.Never);
        }

        /// <summary>
        /// Non-None HTTPS requires Sign rather than UA-TCP's SignAndEncrypt or an unsecured session.
        /// </summary>
        [TestCase(MessageSecurityMode.None, SecurityPolicies.None)]
        [TestCase(MessageSecurityMode.Sign, SecurityPolicies.None)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256)]
        public async Task HttpsRejectsUnsecuredOrInvalidSecurityModeBeforeChannelCreation(
            MessageSecurityMode mode,
            string policy)
        {
            ByteString applicationCertificate = ByteString.From(m_certificate.RawData.AsSpan());
            EndpointDescription endpoint = HttpsEndpoint(applicationCertificate);
            endpoint.SecurityMode = mode;
            endpoint.SecurityPolicyUri = policy;
            var factory = Factory(null);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await verifier.VerifyAsync(
                    Application(), CreateTarget(endpoint, applicationCertificate), applicationCertificate)
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Real session proof validates the application independently of TLS before sending username credentials.
        /// </summary>
        [TestCase("valid")]
        [TestCase("certificate")]
        [TestCase("endpointUri")]
        [TestCase("certificateUri")]
        [TestCase("missingSignature")]
        [TestCase("wrongSignature")]
        [TestCase("certificateChain")]
        [TestCase("wrongCertificateChain")]
        public async Task HttpsRealSessionRequiresPinnedApplicationUriAndLegacyProofBeforeCredentials(
            string proof)
        {
            StatusCode expectedStatus = proof switch
            {
                "valid" or "certificateChain" => StatusCodes.Good,
                "certificate" or "wrongCertificateChain" => StatusCodes.BadCertificateInvalid,
                "endpointUri" => StatusCodes.BadSecurityChecksFailed,
                "certificateUri" => StatusCodes.BadCertificateUriInvalid,
                "missingSignature" or "wrongSignature" => StatusCodes.BadApplicationSignatureInvalid,
                _ => throw new ArgumentOutOfRangeException(nameof(proof))
            };
            using Certificate tlsCertificate = CertificateBuilder.Create("CN=TLS Terminator")
                .AddExtension(new X509SubjectAltNameExtension("urn:test:tls", ["localhost"]))
                .CreateForRSA();
            using Certificate wrongCertificate = CertificateBuilder.Create("CN=Other Application")
                .AddExtension(new X509SubjectAltNameExtension(
                    "urn:other:application", ["localhost", Utils.GetHostName()]))
                .SetRSAKeySize(2048).CreateForRSA();
            using Certificate issuer = CertificateBuilder.Create("CN=Application Issuer")
                .SetCAConstraint().CreateForRSA();
            using Certificate issuedCertificate = CertificateBuilder.Create("CN=Issued Application")
                .AddExtension(new X509SubjectAltNameExtension(ApplicationUri, ["localhost", Utils.GetHostName()]))
                .SetIssuer(issuer).CreateForRSA();
            Certificate applicationCertificate = proof switch
            {
                "certificateUri" => wrongCertificate,
                "certificateChain" or "wrongCertificateChain" => issuedCertificate,
                _ => m_certificate
            };
            ByteString applicationPin = ByteString.From(applicationCertificate.RawData.AsSpan());
            EndpointDescription endpoint = HttpsEndpoint(applicationPin);
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            manager.Setup(value => value.ValidateAsync(
                It.IsAny<CertificateCollection>(), It.IsAny<TrustListIdentifier?>(),
                It.IsAny<Security.Certificates.CertificateValidationOptions?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CertificateValidationResult.Success);
            ApplicationConfiguration configuration = Configuration();
            configuration.CertificateManager = manager.Object;
            configuration.SecurityConfiguration.NonceLength = 32;
            var channel = new Mock<ITransportChannel>();
            bool tlsPolicySelected = false;
            channel.As<IHttpsServerCertificateValidation>()
                .Setup(value => value.RequireServerCertificateValidation(It.IsAny<ByteString>()))
                .Callback(() => tlsPolicySelected = true);
            channel.SetupGet(value => value.EndpointDescription).Returns(endpoint);
            channel.SetupGet(value => value.EndpointConfiguration).Returns(EndpointConfiguration.Create());
            channel.SetupGet(value => value.MessageContext)
                .Returns(ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            channel.SetupGet(value => value.ServerChannelCertificate).Returns(tlsCertificate.RawData);
            channel.SetupGet(value => value.ClientChannelCertificate).Returns([]);
            channel.SetupGet(value => value.ChannelThumbprint).Returns([]);
            int createCount = 0;
            int activationCount = 0;
            channel.Setup(value => value.SendRequestAsync(
                It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                .Returns<IServiceRequest, CancellationToken>((request, _) =>
                {
                    IServiceResponse response;
                    switch (request)
                    {
                        case CreateSessionRequest create:
                            createCount++;
                            Assert.That(tlsPolicySelected, Is.True);
                            byte[] serverNonce = Nonce.CreateRandomNonceData(32);
                            byte[] signatureData = create.ClientCertificate.ToArray()
                                .Concat(create.ClientNonce.ToArray()).ToArray();
                            SignatureData signature = proof == "missingSignature"
                                ? new SignatureData()
                                : SecurityPolicies.Default.CreateSignatureData(
                                    SecurityPolicies.Basic256Sha256,
                                    proof == "wrongSignature" ? tlsCertificate : applicationCertificate,
                                    signatureData);
                            EndpointDescription authenticatedEndpoint = CoreUtils.Clone(endpoint)!;
                            if (proof == "endpointUri")
                            {
                                authenticatedEndpoint.Server.ApplicationUri = "urn:other:application";
                            }
                            response = new CreateSessionResponse
                            {
                                SessionId = NodeId.Parse("s=https-proof-session"),
                                AuthenticationToken = NodeId.Parse("s=https-proof-token"),
                                RevisedSessionTimeout = 60000,
                                ServerNonce = ByteString.From(serverNonce),
                                ServerCertificate = proof switch
                                {
                                    "certificate" => ByteString.From(wrongCertificate.RawData.AsSpan()),
                                    "certificateChain" => ByteString.From(
                                        issuedCertificate.RawData.Concat(issuer.RawData).ToArray()),
                                    "wrongCertificateChain" => ByteString.From(
                                        wrongCertificate.RawData.Concat(issuer.RawData).ToArray()),
                                    _ => applicationPin
                                },
                                ServerEndpoints = [authenticatedEndpoint],
                                ServerSignature = signature
                            };
                            break;
                        case ActivateSessionRequest activate:
                            activationCount++;
                            Assert.That(activate.UserIdentityToken.TryGetValue(
                                out UserNameIdentityToken? token), Is.True);
                            Assert.That(token!.UserName, Is.EqualTo("target-user"));
                            Assert.That(token.Password.IsEmpty, Is.False);
                            Assert.That(token.Password, Is.Not.EqualTo(ByteString.From("secret"u8)));
                            response = new ActivateSessionResponse
                            {
                                ServerNonce = ByteString.From(Nonce.CreateRandomNonceData(32))
                            };
                            break;
                        case ReadRequest read:
                            response = new ReadResponse
                            {
                                Results = read.NodesToRead.ConvertAll(ReadHttpsProofValue)
                            };
                            break;
                        default:
                            response = new CloseSessionResponse();
                            break;
                    }
                    return new ValueTask<IServiceResponse>(response);
                });
            var factory = PinnedFactory();
            factory.Setup(value => value.CreateChannelAsync(
                It.IsAny<ApplicationConfiguration>(), It.IsAny<ITransportWaitingConnection>(),
                It.IsAny<ConfiguredEndpoint>(), false, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(channel.Object);
            factory.Setup(value => value.Create(
                It.IsAny<ITransportChannel>(), It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ConfiguredEndpoint>(), null, null, It.IsAny<ArrayOf<EndpointDescription>>(),
                It.IsAny<ArrayOf<string>>()))
                .Returns<ITransportChannel, ApplicationConfiguration, ConfiguredEndpoint, Certificate?,
                    CertificateCollection?, ArrayOf<EndpointDescription>, ArrayOf<string>>(
                    (transport, selectedConfiguration, selectedEndpoint, _, _, _, _) =>
                        new Session(transport, selectedConfiguration, selectedEndpoint, m_certificate.AddRef()));
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(configuration, factory.Object);
            DeviceRegistrarManagedApplicationTarget target = CreateTarget(endpoint, applicationPin) with
            {
                UserIdentity = new UserIdentity("target-user", "secret"u8),
                HttpsServerCertificate = ByteString.From(tlsCertificate.RawData.AsSpan())
            };

            if (expectedStatus == StatusCodes.Good)
            {
                await verifier.VerifyAsync(Application(), target, applicationPin).ConfigureAwait(false);
            }
            else
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await verifier.VerifyAsync(Application(), target, applicationPin).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(expectedStatus));
            }

            Assert.That(createCount, Is.EqualTo(1));
            Assert.That(activationCount, Is.EqualTo(expectedStatus == StatusCodes.Good ? 1 : 0));
            Assert.That(applicationPin, Is.Not.EqualTo(ByteString.From(tlsCertificate.RawData.AsSpan())));
        }

        /// <summary>
        /// The verifier rejects factories that do not promise pre-activation application-certificate validation.
        /// </summary>
        [Test]
        public void ConstructorRejectsSessionFactoryWithoutPinnedCertificateCapability()
        {
            var factory = new Mock<ISessionFactory>(MockBehavior.Strict);
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                new SessionDeviceRegistrarManagedApplicationChannelVerifier(Configuration(), factory.Object));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A derived default factory must opt in directly and cannot inherit onboarding capability by accident.
        /// </summary>
        [Test]
        public void ConstructorRejectsDerivedDefaultFactoryBeforeItCanReceiveCredentials()
        {
            var factory = new CredentialReceivingDerivedSessionFactory();
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                new SessionDeviceRegistrarManagedApplicationChannelVerifier(Configuration(), factory));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(factory, Is.Not.InstanceOf<IPinnedServerCertificateSessionFactory>());
            Assert.That(factory.CreateCalls, Is.Zero);
            Assert.That(factory.ReceivedIdentity, Is.Null);
        }

        /// <summary>
        /// The verifier accepts only the actual pinned peer and always retires its session.
        /// </summary>
        [Test]
        public async Task ActualPeerCertificateIsPinnedBeforeOpeningSession()
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            context.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            context.Channel.Object.EndpointDescription.Server.ApplicationUri = ApplicationUri;
            var factory = Factory(context.Channel.Object);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            await verifier.VerifyAsync(
                Application(), Target(context), ByteString.From(m_certificate.RawData.AsSpan()))
                .ConfigureAwait(false);

            factory.Verify(value => value.CreateChannelAsync(
                It.IsAny<ApplicationConfiguration>(), It.IsAny<ITransportWaitingConnection>(),
                It.IsAny<ConfiguredEndpoint>(), false, true, It.IsAny<CancellationToken>()), Times.Once);
            factory.Verify(value => value.Create(
                It.IsAny<ITransportChannel>(), It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ConfiguredEndpoint>(), null, null, It.IsAny<ArrayOf<EndpointDescription>>(),
                It.IsAny<ArrayOf<string>>()), Times.Once);
            m_lastCreatedSession!.Verify(value => value.OpenAsync(
                It.IsAny<string>(), 60000, It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(),
                true, true, It.IsAny<CancellationToken>()), Times.Once);
            m_lastCreatedSession.As<IExpectedServerApplicationUriSession>().Verify(
                value => value.RequireServerApplicationUri(ApplicationUri),
                Times.Once);
            m_lastCreatedSession.Verify(value => value.CloseAsync(
                5000, true, It.IsAny<CancellationToken>()), Times.Once);
            m_lastCreatedSession.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// The verifier rejects sessions that cannot enforce the URI check before activating credentials.
        /// </summary>
        [Test]
        public async Task SessionWithoutApplicationUriValidationIsRejectedBeforeOpeningAsync()
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            context.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            context.Channel.Object.EndpointDescription.Server.ApplicationUri = ApplicationUri;
            var factory = Factory(context.Channel.Object, supportsApplicationUriValidation: false);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await verifier.VerifyAsync(
                    Application(), Target(context), ByteString.From(m_certificate.RawData.AsSpan()))
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            m_lastCreatedSession!.Verify(value => value.OpenAsync(
                It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A peer certificate that differs from the selected certificate is rejected.
        /// </summary>
        [Test]
        public async Task UnexpectedActualPeerCertificateIsRejectedBeforeSessionCreation()
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            using Certificate wrongPeerCertificate = CertificateBuilder.Create("CN=Wrong Managed Application")
                .AddExtension(new X509SubjectAltNameExtension("urn:wrong-managed-application", ["localhost"]))
                .SetRSAKeySize(2048).CreateForRSA();
            context.Channel.SetupGet(channel => channel.ServerChannelCertificate)
                .Returns(wrongPeerCertificate.RawData);
            context.Channel.Object.EndpointDescription.Server.ApplicationUri = ApplicationUri;
            context.Channel.Object.EndpointDescription.ServerCertificate =
                ByteString.From(m_certificate.RawData.AsSpan());
            var factory = Factory(context.Channel.Object);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await verifier.VerifyAsync(
                    Application(), Target(context), ByteString.From(m_certificate.RawData.AsSpan()))
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
            factory.Verify(value => value.CreateChannelAsync(
                It.IsAny<ApplicationConfiguration>(), It.IsAny<ITransportWaitingConnection>(),
                It.IsAny<ConfiguredEndpoint>(), false, true, It.IsAny<CancellationToken>()), Times.Once);
            factory.Verify(value => value.Create(
                It.IsAny<ITransportChannel>(), It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ConfiguredEndpoint>(), null, null, It.IsAny<ArrayOf<EndpointDescription>>(),
                It.IsAny<ArrayOf<string>>()), Times.Never);
            m_lastCreatedSession!.Verify(value => value.OpenAsync(
                It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            context.Channel.Verify(channel => channel.Dispose(), Times.Once);
        }

        /// <summary>
        /// Failed session and channel cleanup retain every failure and still attempt channel disposal.
        /// </summary>
        [Test]
        public async Task CleanupFailuresPreserveOpenAndBothCleanupErrors()
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            context.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            context.Channel.Object.EndpointDescription.Server.ApplicationUri = ApplicationUri;
            context.Channel.Object.EndpointDescription.ServerCertificate =
                ByteString.From(m_certificate.RawData.AsSpan());
            DeviceRegistrarManagedApplicationTarget target = Target(context);
            var openError = new InvalidOperationException("session open failed");
            var sessionCleanupError = new InvalidOperationException("session cleanup failed");
            var channelCleanupError = new InvalidOperationException("channel cleanup failed");
            int disposeCalls = 0;
            context.Channel.Setup(channel => channel.Dispose())
                .Callback(() =>
                {
                    if (++disposeCalls == 1)
                    {
                        throw channelCleanupError;
                    }
                });
            var factory = PinnedFactory();
            Mock<Opc.Ua.Client.ISession> session = CreateSessionMock(
                context.Channel.Object, () => target.Endpoint);
            m_lastCreatedSession = session;
            session.Setup(value => value.OpenAsync(
                It.IsAny<string>(),
                It.IsAny<uint>(),
                It.IsAny<IUserIdentity>(),
                It.IsAny<ArrayOf<string>>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(openError);
            session.Setup(value => value.DisposeAsync())
                .Returns(new ValueTask(Task.FromException(sessionCleanupError)));
            factory.Setup(value => value.CreateChannelAsync(
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ITransportWaitingConnection>(),
                It.IsAny<ConfiguredEndpoint>(),
                false,
                true,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(context.Channel.Object);
            factory.Setup(value => value.Create(
                It.IsAny<ITransportChannel>(),
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ConfiguredEndpoint>(),
                null,
                null,
                It.IsAny<ArrayOf<EndpointDescription>>(),
                It.IsAny<ArrayOf<string>>()))
                .Returns(session.Object);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await verifier.VerifyAsync(
                    Application(), target, ByteString.From(m_certificate.RawData.AsSpan()))
                    .ConfigureAwait(false));

            Assert.That(error!.Flatten().InnerExceptions, Does.Contain(openError));
            Assert.That(error.Flatten().InnerExceptions, Does.Contain(sessionCleanupError));
            Assert.That(error.Flatten().InnerExceptions, Does.Contain(channelCleanupError));
            session.Verify(value => value.DisposeAsync(), Times.Once);
            context.Channel.Verify(channel => channel.Dispose(), Times.Once);
        }

        /// <summary>
        /// The verifier safely retries disposal when a session disposed its channel before failing cleanup.
        /// </summary>
        [Test]
        public async Task CleanupFailureRetriesIdempotentChannelDisposal()
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            context.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            context.Channel.Object.EndpointDescription.Server.ApplicationUri = ApplicationUri;
            context.Channel.Object.EndpointDescription.ServerCertificate =
                ByteString.From(m_certificate.RawData.AsSpan());
            DeviceRegistrarManagedApplicationTarget target = Target(context);
            int disposeCalls = 0;
            bool channelDisposed = false;
            context.Channel.Setup(channel => channel.Dispose())
                .Callback(() =>
                {
                    disposeCalls++;
                    channelDisposed = true;
                });
            var openError = new InvalidOperationException("session open failed");
            var sessionCleanupError = new InvalidOperationException("session cleanup failed");
            var factory = PinnedFactory();
            Mock<Opc.Ua.Client.ISession> session = CreateSessionMock(
                context.Channel.Object, () => target.Endpoint);
            m_lastCreatedSession = session;
            session.Setup(value => value.OpenAsync(
                It.IsAny<string>(),
                It.IsAny<uint>(),
                It.IsAny<IUserIdentity>(),
                It.IsAny<ArrayOf<string>>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(openError);
            session.Setup(value => value.DisposeAsync())
                .Callback(() => context.Channel.Object.Dispose())
                .Returns(new ValueTask(Task.FromException(sessionCleanupError)));
            factory.Setup(value => value.CreateChannelAsync(
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ITransportWaitingConnection>(),
                It.IsAny<ConfiguredEndpoint>(),
                false,
                true,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(context.Channel.Object);
            factory.Setup(value => value.Create(
                It.IsAny<ITransportChannel>(),
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ConfiguredEndpoint>(),
                null,
                null,
                It.IsAny<ArrayOf<EndpointDescription>>(),
                It.IsAny<ArrayOf<string>>()))
                .Returns(session.Object);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await verifier.VerifyAsync(
                    Application(), target, ByteString.From(m_certificate.RawData.AsSpan()))
                    .ConfigureAwait(false));

            Assert.That(error!.Flatten().InnerExceptions, Does.Contain(openError));
            Assert.That(error.Flatten().InnerExceptions, Does.Contain(sessionCleanupError));
            Assert.That(channelDisposed, Is.True);
            Assert.That(disposeCalls, Is.EqualTo(2));
            context.Channel.Verify(channel => channel.Dispose(), Times.Exactly(2));
        }

        /// <summary>
        /// A channel sharing a factory-mutated endpoint cannot replace the endpoint values selected by the host.
        /// </summary>
        [Test]
        public async Task MutatedSharedChannelEndpointIsRejectedBeforeSessionCreation()
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            context.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            context.Channel.Object.EndpointDescription.Server.ApplicationUri = ApplicationUri;
            var factory = PinnedFactory();
            factory.Setup(value => value.CreateChannelAsync(
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ITransportWaitingConnection>(),
                It.IsAny<ConfiguredEndpoint>(),
                false,
                true,
                It.IsAny<CancellationToken>()))
                .Callback<ApplicationConfiguration, ITransportWaitingConnection, ConfiguredEndpoint,
                    bool, bool, CancellationToken>((_, _, endpoint, _, _, _) =>
                    {
                        endpoint.Description.EndpointUrl = "opc.tcp://mutated-peer:4840";
                        context.Channel.SetupGet(channel => channel.EndpointDescription)
                            .Returns(endpoint.Description);
                    })
                .ReturnsAsync(context.Channel.Object);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await verifier.VerifyAsync(
                    Application(), Target(context), ByteString.From(m_certificate.RawData.AsSpan()))
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            factory.Verify(value => value.Create(
                It.IsAny<ITransportChannel>(), It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ConfiguredEndpoint>(), null, null, It.IsAny<ArrayOf<EndpointDescription>>(),
                It.IsAny<ArrayOf<string>>()), Times.Never);
            context.Channel.Verify(channel => channel.Dispose(), Times.Once);
        }

        /// <summary>
        /// A factory cannot return a session attached to a different channel or transfer channel cleanup.
        /// </summary>
        [Test]
        public async Task SessionUsingDifferentChannelIsRejectedAndBothChannelsAreDisposed()
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            context.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            context.Channel.Object.EndpointDescription.Server.ApplicationUri = ApplicationUri;
            DeviceRegistrarManagedApplicationTarget target = Target(context);
            var otherChannel = new Mock<ITransportChannel>();
            otherChannel.SetupGet(channel => channel.EndpointDescription)
                .Returns(CoreUtils.Clone(context.Channel.Object.EndpointDescription)!);
            otherChannel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            Mock<Opc.Ua.Client.ISession> otherSession = CreateSessionMock(
                otherChannel.Object, () => target.Endpoint);
            otherSession.Setup(value => value.DisposeAsync())
                .Callback(() => otherChannel.Object.Dispose())
                .Returns(new ValueTask());
            var factory = PinnedFactory();
            factory.Setup(value => value.CreateChannelAsync(
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ITransportWaitingConnection>(),
                It.IsAny<ConfiguredEndpoint>(),
                false,
                true,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(context.Channel.Object);
            factory.Setup(value => value.Create(
                It.IsAny<ITransportChannel>(),
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ConfiguredEndpoint>(),
                null,
                null,
                It.IsAny<ArrayOf<EndpointDescription>>(),
                It.IsAny<ArrayOf<string>>()))
                .Returns(otherSession.Object);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await verifier.VerifyAsync(
                    Application(), target, ByteString.From(m_certificate.RawData.AsSpan()))
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            otherSession.Verify(value => value.OpenAsync(
                It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            otherSession.Verify(value => value.DisposeAsync(), Times.Once);
            context.Channel.Verify(channel => channel.Dispose(), Times.Once);
            otherChannel.Verify(channel => channel.Dispose(), Times.Once);
        }

        /// <summary>
        /// A factory cannot bind the verified channel to different session endpoint metadata.
        /// </summary>
        [Test]
        public async Task SessionUsingDifferentEndpointOnVerifiedChannelIsRejectedBeforeOpen()
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            context.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            context.Channel.Object.EndpointDescription.Server.ApplicationUri = ApplicationUri;
            DeviceRegistrarManagedApplicationTarget target = Target(context);
            var factory = Factory(context.Channel.Object);
            Mock<Opc.Ua.Client.ISession> session = m_lastCreatedSession!;
            EndpointDescription differentDescription = CoreUtils.Clone(target.Endpoint.Description)!;
            differentDescription.Server.ApplicationUri = "urn:other-managed-application";
            session.SetupGet(value => value.ConfiguredEndpoint).Returns(
                new ConfiguredEndpoint(null, differentDescription, EndpointConfiguration.Create()));
            session.Setup(value => value.DisposeAsync())
                .Callback(() => context.Channel.Object.Dispose())
                .Returns(new ValueTask());
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await verifier.VerifyAsync(
                    Application(), target, ByteString.From(m_certificate.RawData.AsSpan()))
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            session.Verify(value => value.OpenAsync(
                It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.DisposeAsync(), Times.Once);
            context.Channel.Verify(channel => channel.Dispose(), Times.Once);
        }

        /// <summary>
        /// Real secure sessions accept the initial pin and retained rotated pin, then reject the previous peer.
        /// </summary>
        [Test]
        public async Task RealSecureSessionsProveInitialAndRotatedCertificatesAndRejectPreviousPeer()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "ManagedApplicationProof-" + Guid.NewGuid().ToString("N"));
            string clientRoot = Path.Combine(root, "client");
            var client = new ClientFixture(NUnitTelemetryContext.Create());
            var initialServer = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                AutoAccept = true
            };
            ServerFixture<ReferenceServer>? rotatedServer = null;
            Exception? testFailure = null;
            try
            {
                await initialServer.LoadConfigurationAsync(Path.Combine(root, "initial-server"))
                    .ConfigureAwait(false);
                await initialServer.StartAsync().ConfigureAwait(false);
                await client.LoadClientConfigurationAsync(clientRoot, "ManagedApplicationProofClient")
                    .ConfigureAwait(false);
                client.Config.SecurityConfiguration.AutoAcceptUntrustedCertificates = false;
                client.Config.SecurityConfiguration.UseValidatedCertificates = true;
                client.Config.CertificateManager ??= CertificateManagerFactory.Create(
                    client.Config.SecurityConfiguration, NUnitTelemetryContext.Create());

                (EndpointDescription initialEndpoint, ByteString initialCertificate) =
                    await DiscoverSecureEndpointAsync(initialServer).ConfigureAwait(false);
                await TrustPeerAsync(client.Config, initialEndpoint.ServerCertificate).ConfigureAwait(false);
                var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                    client.Config, client.SessionFactory);
                ApplicationDescription application = CoreUtils.Clone(initialEndpoint.Server)!;
                DeviceRegistrarManagedApplicationTarget initialTarget =
                    CreateTarget(initialEndpoint, initialCertificate);
                Assert.That(initialTarget.DiscoveryUrls.ToList(), Does.Not.Contain(initialEndpoint.EndpointUrl));

                await verifier.VerifyAsync(application, initialTarget, initialCertificate).ConfigureAwait(false);

                await initialTarget.Endpoint.UpdateFromServerAsync(NUnitTelemetryContext.Create())
                    .ConfigureAwait(false);
                Assert.That(initialTarget.Endpoint.DiscoveryEndpoints.IsEmpty, Is.False);
                ArrayOf<EndpointDescription> retained = initialTarget.Endpoint.DiscoveryEndpoints;
                string? retainedUrl = retained[0].EndpointUrl;
                byte retainedSecurityLevel = retained[0].SecurityLevel;
                var snapshotFactory = PinnedFactory();
                snapshotFactory.Setup(value => value.CreateChannelAsync(
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<ConfiguredEndpoint>(),
                    false,
                    true,
                    It.IsAny<CancellationToken>()))
                    .Returns<ApplicationConfiguration, ITransportWaitingConnection, ConfiguredEndpoint,
                        bool, bool, CancellationToken>(
                        async (configuration, connection, captured, update, checkDomain, ct) =>
                        {
                            await Task.Yield();
                            retained[0].EndpointUrl = "opc.tcp://mutated-provider:4840";
                            retained[0].SecurityLevel ^= 0xff;
                            Assert.That(captured.DiscoveryEndpoints.Count, Is.EqualTo(retained.Count));
                            for (int index = 0; index < retained.Count; index++)
                            {
                                Assert.That(captured.DiscoveryEndpoints[index], Is.Not.SameAs(retained[index]));
                                Assert.That(captured.DiscoveryEndpoints[index].Server,
                                    Is.Not.SameAs(retained[index].Server));
                                for (int token = 0; token < retained[index].UserIdentityTokens.Count; token++)
                                {
                                    Assert.That(captured.DiscoveryEndpoints[index].UserIdentityTokens[token],
                                        Is.Not.SameAs(retained[index].UserIdentityTokens[token]));
                                }
                            }
                            Assert.That(captured.DiscoveryEndpoints[0].EndpointUrl, Is.EqualTo(retainedUrl));
                            Assert.That(captured.DiscoveryEndpoints[0].SecurityLevel,
                                Is.EqualTo(retainedSecurityLevel));
                            string? retainedApplicationUri = retained[0].Server.ApplicationUri;
                            retained[0].Server.ApplicationUri = "urn:mutated:discovery-application";
                            Assert.That(captured.DiscoveryEndpoints[0].Server.ApplicationUri,
                                Is.EqualTo(retainedApplicationUri));
                            Assert.That(retained[0].UserIdentityTokens.Count, Is.GreaterThan(0));
                            string? retainedPolicyId = retained[0].UserIdentityTokens[0].PolicyId;
                            retained[0].UserIdentityTokens[0].PolicyId = "mutated-discovery-policy";
                            Assert.That(captured.DiscoveryEndpoints[0].UserIdentityTokens[0].PolicyId,
                                Is.EqualTo(retainedPolicyId));
                            return await client.SessionFactory.CreateChannelAsync(
                                configuration, connection, captured, update, checkDomain, ct)
                                .ConfigureAwait(false);
                        });
                snapshotFactory.Setup(value => value.Create(
                    It.IsAny<ITransportChannel>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<ConfiguredEndpoint>(),
                    null,
                    null,
                    It.IsAny<ArrayOf<EndpointDescription>>(),
                    It.IsAny<ArrayOf<string>>()))
                    .Returns<ITransportChannel, ApplicationConfiguration, ConfiguredEndpoint, Certificate?,
                        CertificateCollection?, ArrayOf<EndpointDescription>, ArrayOf<string>>(
                        (channel, configuration, captured, certificate, chain, endpoints, profiles) =>
                            client.SessionFactory.Create(
                                channel, configuration, captured, certificate, chain, endpoints, profiles));
                var retainedVerifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                    client.Config, snapshotFactory.Object);
                await retainedVerifier.VerifyAsync(application, initialTarget, initialCertificate)
                    .ConfigureAwait(false);
                snapshotFactory.Verify(value => value.CreateChannelAsync(
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<ConfiguredEndpoint>(), false, true, It.IsAny<CancellationToken>()), Times.Once);

                ServiceResultException? discoveryMismatch = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await verifier.VerifyAsync(application, initialTarget, initialCertificate).ConfigureAwait(false));
                Assert.That(discoveryMismatch!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));

                await initialServer.StopAsync().ConfigureAwait(false);
                rotatedServer = new ServerFixture<ReferenceServer>(
                    telemetry => new ReferenceServer(telemetry))
                {
                    AutoAccept = true
                };
                await rotatedServer.LoadConfigurationAsync(Path.Combine(root, "rotated-server"))
                    .ConfigureAwait(false);
                await rotatedServer.StartAsync().ConfigureAwait(false);

                (EndpointDescription rotatedEndpoint, ByteString rotatedCertificate) =
                    await DiscoverSecureEndpointAsync(rotatedServer).ConfigureAwait(false);
                Assert.That(rotatedCertificate, Is.Not.EqualTo(initialCertificate));
                await TrustPeerAsync(client.Config, rotatedEndpoint.ServerCertificate).ConfigureAwait(false);
                DeviceRegistrarManagedApplicationTarget rotatedTarget =
                    CreateTarget(rotatedEndpoint, initialCertificate);
                Assert.That(rotatedTarget.DiscoveryUrls.ToList(), Does.Not.Contain(rotatedEndpoint.EndpointUrl));

                await verifier.VerifyAsync(application, rotatedTarget, rotatedCertificate).ConfigureAwait(false);

                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await verifier.VerifyAsync(application, rotatedTarget, initialCertificate).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed),
                    "The rotated server rejects the stale receiver certificate during OpenSecureChannel.");
            }
            catch (Exception error)
            {
                testFailure = error;
            }
            var cleanupErrors = new List<Exception>();
            await CleanupAsync(cleanupErrors, () => initialServer.StopAsync()).ConfigureAwait(false);
            if (rotatedServer != null)
            {
                await CleanupAsync(cleanupErrors, () => rotatedServer.StopAsync()).ConfigureAwait(false);
            }
            await CleanupAsync(cleanupErrors, () => client.DisposeAsync().AsTask()).ConfigureAwait(false);
            await CleanupAsync(cleanupErrors, () =>
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            ThrowCleanupFailures(testFailure, cleanupErrors, "Managed application proof cleanup failed.");
        }

        /// <summary>
        /// Application identity stays pinned while the manager applies its current trust policy.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RealUntrustedPinnedPeerFollowsCurrentManagerTrustPolicy(bool changeAutoAccept)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "ManagedApplicationTrust-" + Guid.NewGuid().ToString("N"));
            ITelemetryContext clientTelemetry = NUnitTelemetryContext.Create();
            using var records = new RecordingLoggerProvider();
            if (!changeAutoAccept)
            {
                clientTelemetry.LoggerFactory.AddProvider(records);
            }
            var client = new ClientFixture(clientTelemetry);
            var server = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                AutoAccept = true
            };
            Exception? testFailure = null;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await server.LoadConfigurationAsync(Path.Combine(root, "server")).ConfigureAwait(false);
                await server.StartAsync().ConfigureAwait(false);
                await client.LoadClientConfigurationAsync(Path.Combine(root, "client"), "ManagedApplicationTrustClient")
                    .ConfigureAwait(false);
                client.Config.SecurityConfiguration.AutoAcceptUntrustedCertificates = false;
                client.Config.SecurityConfiguration.UseValidatedCertificates = false;
                await client.Config.CertificateManager.UpdateAsync(client.Config.SecurityConfiguration)
                    .ConfigureAwait(false);
                Assert.That(client.Config.CertificateManager, Is.TypeOf<CertificateManager>());
                var manager = (CertificateManager)client.Config.CertificateManager;
                Assert.That(manager.AutoAcceptUntrustedCertificates, Is.False);

                (EndpointDescription endpoint, ByteString pin) =
                    await DiscoverSecureEndpointAsync(server).ConfigureAwait(false);
                using CertificateCollection chain = DefaultCertificateFactory.Instance.ParseChainBlob(pin.ToArray());
                CertificateValidationResult untrusted = await manager.ValidateAsync(
                    chain, ct: cancellation.Token).ConfigureAwait(false);
                Assert.That(untrusted.IsValid, Is.False);
                Assert.That(untrusted.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));

                DeviceRegistrarManagedApplicationTarget target = CreateTarget(endpoint, pin);
                EndpointConfiguration endpointConfiguration = target.Endpoint.Configuration ??
                    throw new InvalidOperationException("The test target has no endpoint configuration.");
                endpointConfiguration.OperationTimeout = 15000;
                await TrustPeerAsync(client.Config, pin).ConfigureAwait(false);
                var trustedVerifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                    client.Config, client.SessionFactory);
                await trustedVerifier.VerifyAsync(endpoint.Server, target, pin, cancellation.Token)
                    .ConfigureAwait(false);
                using (ICertificateStore trusted = manager.OpenTrustedStore(TrustListIdentifier.Peers))
                {
                    Assert.That(await trusted.DeleteAsync(chain[0].Thumbprint, cancellation.Token)
                        .ConfigureAwait(false), Is.True);
                }
                manager.NotifyTrustListChanged(
                    TrustListIdentifier.Peers, trustChanged: true, crlChanged: false);
                CertificateValidationResult removedTrust = await manager.ValidateAsync(
                    chain, ct: cancellation.Token).ConfigureAwait(false);
                Assert.That(removedTrust.IsValid, Is.False);
                Assert.That(removedTrust.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
                Assert.That(manager.AutoAcceptUntrustedCertificates, Is.False);

                var observedFactory = changeAutoAccept
                    ? null
                    : new ChannelRejectionRecordingSessionFactory(clientTelemetry, records);
                ISessionFactory realFactory = observedFactory ?? client.SessionFactory;
                var forwardingFactory = PinnedFactory();
                forwardingFactory.Setup(value => value.CreateChannelAsync(
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<ConfiguredEndpoint>(),
                    false,
                    true,
                    It.IsAny<CancellationToken>()))
                    .Returns<ApplicationConfiguration, ITransportWaitingConnection, ConfiguredEndpoint,
                        bool, bool, CancellationToken>(
                        async (configuration, connection, captured, update, checkDomain, ct) =>
                        {
                            Assert.That(configuration.CertificateManager, Is.SameAs(manager));
                            Assert.That(captured.Description.ServerCertificate, Is.EqualTo(pin));
                            Assert.That(manager.AutoAcceptUntrustedCertificates, Is.False);
                            if (changeAutoAccept)
                            {
                                manager.AutoAcceptUntrustedCertificates = true;
                            }
                            return await realFactory.CreateChannelAsync(
                                configuration, connection, captured, update, checkDomain, ct)
                                .ConfigureAwait(false);
                        });
                forwardingFactory.Setup(value => value.Create(
                    It.IsAny<ITransportChannel>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<ConfiguredEndpoint>(),
                    null,
                    null,
                    It.IsAny<ArrayOf<EndpointDescription>>(),
                    It.IsAny<ArrayOf<string>>()))
                    .Returns<ITransportChannel, ApplicationConfiguration, ConfiguredEndpoint, Certificate?,
                        CertificateCollection?, ArrayOf<EndpointDescription>, ArrayOf<string>>(
                        (channel, configuration, captured, certificate, chain, endpoints, profiles) =>
                            realFactory.Create(
                                channel, configuration, captured, certificate, chain, endpoints, profiles));
                var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                    client.Config, forwardingFactory.Object);
                if (changeAutoAccept)
                {
                    await verifier.VerifyAsync(endpoint.Server, target, pin, cancellation.Token)
                        .ConfigureAwait(false);
                    Assert.That(manager.AutoAcceptUntrustedCertificates, Is.True);
                }
                else
                {
                    ChannelRejectionRecordingSessionFactory rejectedFactory = observedFactory ??
                        throw new InvalidOperationException("The rejection case requires a recording session factory.");
                    ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await verifier.VerifyAsync(endpoint.Server, target, pin, cancellation.Token)
                            .ConfigureAwait(false));
                    Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
                    Assert.That(rejectedFactory.ChannelCreationFailed, Is.True);
                    Assert.That(rejectedFactory.SessionConstructionCount, Is.Zero);
                    Assert.That(rejectedFactory.RejectionMessages.ToList(), Has.Some.Matches<string>(message =>
                        message != null &&
                        message.Contains("validation failed with suppressible errors but was rejected",
                            StringComparison.Ordinal)));
                }
                forwardingFactory.Verify(value => value.CreateChannelAsync(
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<ConfiguredEndpoint>(), false, true, It.IsAny<CancellationToken>()), Times.Once);
            }
            catch (Exception error)
            {
                testFailure = error;
            }
            var cleanupErrors = new List<Exception>();
            await CleanupAsync(cleanupErrors, () =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            await CleanupAsync(cleanupErrors, () => server.StopAsync()).ConfigureAwait(false);
            await CleanupAsync(cleanupErrors, () => client.DisposeAsync().AsTask()).ConfigureAwait(false);
            await CleanupAsync(cleanupErrors, () =>
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            ThrowCleanupFailures(testFailure, cleanupErrors, "Managed application trust cleanup failed.");
        }

        /// <summary>
        /// Distinct discovery and connection URLs preserve explicit endpoint selection and actual peer proof.
        /// </summary>
        [Test]
        public async Task DistinctDiscoveryAndConnectionUrlsUseSelectedPinnedEndpoint()
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            context.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            context.Channel.Object.EndpointDescription.Server.ApplicationUri = ApplicationUri;
            var factory = Factory(context.Channel.Object);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);
            DeviceRegistrarManagedApplicationTarget target = Target(context) with
            {
                DiscoveryUrls = ["https://discovery.example.test/managed"]
            };

            await verifier.VerifyAsync(Application(), target, ByteString.From(m_certificate.RawData.AsSpan()))
                .ConfigureAwait(false);

            Assert.That(target.DiscoveryUrls.ToList(), Does.Not.Contain(target.Endpoint.Description.EndpointUrl));
            factory.Verify(value => value.CreateChannelAsync(
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ITransportWaitingConnection>(),
                It.Is<ConfiguredEndpoint>(endpoint =>
                    endpoint.Description.EndpointUrl == target.Endpoint.Description.EndpointUrl &&
                    endpoint.Description.ServerCertificate == target.InstalledCertificate),
                false, true, It.IsAny<CancellationToken>()), Times.Once);
            m_lastCreatedSession!.Verify(value => value.CloseAsync(
                5000, true, It.IsAny<CancellationToken>()), Times.Once);
            m_lastCreatedSession.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Provider Clone overrides cannot retain endpoint, nested metadata, quota or certificate state across awaits.
        /// </summary>
        [Test]
        public async Task ProviderMetadataMutationAfterCaptureCannotChangePinnedVerification()
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            context.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            context.Channel.Object.EndpointDescription.Server.ApplicationUri = ApplicationUri;
            context.Channel.Object.EndpointDescription.ServerCertificate =
                ByteString.From(m_certificate.RawData.AsSpan());
            string endpointUrl = context.Channel.Object.EndpointDescription.EndpointUrl!;
            byte[] certificate = m_certificate.RawData;
            string[] discoveryUrls = ["https://discovery.example.test/managed"];
            var application = new SelfCloningApplicationDescription
            {
                ApplicationUri = ApplicationUri,
                ApplicationName = new LocalizedText("en", "captured"),
                DiscoveryUrls = discoveryUrls.ToArrayOf()
            };
            var policy = new SelfCloningUserTokenPolicy
            {
                PolicyId = "captured",
                TokenType = UserTokenType.Anonymous,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            var description = new SelfCloningEndpointDescription
            {
                EndpointUrl = endpointUrl,
                Server = application,
                UserIdentityTokens = [policy],
                ServerCertificate = ByteString.From(certificate),
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            var quotas = new SelfCloningEndpointConfiguration { OperationTimeout = 12345 };
            var target = new DeviceRegistrarManagedApplicationTarget(
                discoveryUrls.ToArrayOf(), ByteString.From(certificate),
                new ConfiguredEndpoint(null, description, quotas), new UserIdentity(), ["DA"]);
            EndpointConfiguration selectedQuotas = target.Endpoint.Configuration ??
                throw new InvalidOperationException("The selected endpoint configuration is missing.");
            selectedQuotas.UseBinaryEncoding = false;
            const string selectedExtensionXml = "<settings selected=\"captured\"> <option /> </settings>";
            XmlElement[] extensions = [XmlElement.From(selectedExtensionXml)];
            target.Endpoint.SelectedUserTokenPolicyIndex = -1;
            target.Endpoint.BinaryEncodingSupport = BinaryEncodingSupport.Required;
            target.Endpoint.ReverseConnect = new ReverseConnectEndpoint
            {
                Enabled = false,
                ServerUri = ApplicationUri,
                Thumbprint = "captured"
            };
            target.Endpoint.Extensions = extensions.ToArrayOf();
            ApplicationDescription expectedApplication = Application();
            var replacementFormattingArgument = new Mock<IFormattable>(MockBehavior.Strict);
            var factory = PinnedFactory();
            ConfiguredEndpoint? createdEndpoint = null;
            Mock<Opc.Ua.Client.ISession> createdSession = CreateSessionMock(
                context.Channel.Object,
                () => createdEndpoint ?? throw new InvalidOperationException(
                    "The session endpoint has not been supplied by the factory."));
            m_lastCreatedSession = createdSession;
            factory.Setup(value => value.CreateChannelAsync(
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ITransportWaitingConnection>(),
                It.IsAny<ConfiguredEndpoint>(),
                false,
                true,
                It.IsAny<CancellationToken>()))
                .Returns<ApplicationConfiguration, ITransportWaitingConnection, ConfiguredEndpoint,
                    bool, bool, CancellationToken>(
                    async (_, _, captured, _, _, _) =>
                    {
                        await Task.Yield();
                        description.EndpointUrl = "opc.tcp://wrong-peer:4840";
                        description.SecurityMode = MessageSecurityMode.None;
                        application.ApplicationUri = "urn:wrong:application";
                        policy.PolicyId = "mutated";
                        policy.SecurityPolicyUri = SecurityPolicies.None;
                        application.ApplicationName = new LocalizedText(
                            new TranslationInfo("replacement", "en", "{0}", replacementFormattingArgument.Object));
                        discoveryUrls[0] = "relative";
                        quotas.OperationTimeout = 1;
                        selectedQuotas.UseBinaryEncoding = true;
                        certificate[0] ^= 0xff;
                        expectedApplication.ApplicationUri = "urn:wrong:argument";
                        target.Endpoint.SelectedUserTokenPolicyIndex = 0;
                        target.Endpoint.BinaryEncodingSupport = BinaryEncodingSupport.None;
                        target.Endpoint.ReverseConnect!.ServerUri = "urn:wrong:reverse";
                        extensions[0] = XmlElement.From("<settings selected=\"mutated\" />");
                        target.Endpoint.Extensions = ArrayOf<XmlElement>.Empty;
                        application.DiscoveryUrls = ArrayOf<string>.Empty;
                        description.UserIdentityTokens = ArrayOf<UserTokenPolicy>.Empty;
                        EndpointConfiguration capturedQuotas = captured.Configuration ??
                            throw new InvalidOperationException("The captured endpoint configuration is missing.");

                        Assert.That(captured.Description, Is.Not.SameAs(description));
                        Assert.That(captured.Description.Server, Is.Not.SameAs(application));
                        Assert.That(captured.Description.UserIdentityTokens[0], Is.Not.SameAs(policy));
                        Assert.That(capturedQuotas, Is.Not.SameAs(quotas));
                        Assert.That(captured.Description.EndpointUrl, Is.EqualTo(endpointUrl));
                        Assert.That(captured.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                        Assert.That(captured.Description.Server.ApplicationUri, Is.EqualTo(ApplicationUri));
                        Assert.That(captured.Description.Server.ApplicationName.Text, Is.EqualTo("captured"));
                        Assert.That(captured.Description.Server.DiscoveryUrls[0],
                            Is.EqualTo("https://discovery.example.test/managed"));
                        Assert.That(captured.Description.UserIdentityTokens[0].PolicyId, Is.EqualTo("captured"));
                        Assert.That(captured.Description.UserIdentityTokens[0].SecurityPolicyUri,
                            Is.EqualTo(SecurityPolicies.Basic256Sha256));
                        Assert.That(capturedQuotas.OperationTimeout, Is.EqualTo(12345));
                        Assert.That(capturedQuotas.UseBinaryEncoding, Is.False);
                        Assert.That(captured.SelectedUserTokenPolicyIndex, Is.EqualTo(-1));
                        Assert.That(captured.BinaryEncodingSupport, Is.EqualTo(BinaryEncodingSupport.Required));
                        Assert.That(captured.UpdateBeforeConnect, Is.False);
                        ReverseConnectEndpoint capturedReverse = captured.ReverseConnect ??
                            throw new InvalidOperationException("The reverse-connect snapshot is missing.");
                        Assert.That(capturedReverse, Is.Not.SameAs(target.Endpoint.ReverseConnect));
                        Assert.That(capturedReverse.Enabled, Is.False);
                        Assert.That(capturedReverse.ServerUri, Is.EqualTo(ApplicationUri));
                        Assert.That(capturedReverse.Thumbprint, Is.EqualTo("captured"));
                        Assert.That(captured.Extensions[0].OuterXml, Is.EqualTo(selectedExtensionXml));
                        Assert.That(captured.Description.ServerCertificate,
                            Is.EqualTo(ByteString.From(m_certificate.RawData.AsSpan())));
                        return context.Channel.Object;
                    });
            factory.Setup(value => value.Create(
                It.IsAny<ITransportChannel>(),
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<ConfiguredEndpoint>(),
                null,
                null,
                It.IsAny<ArrayOf<EndpointDescription>>(),
                It.IsAny<ArrayOf<string>>()))
                .Returns<ITransportChannel, ApplicationConfiguration, ConfiguredEndpoint, Certificate?,
                    CertificateCollection?, ArrayOf<EndpointDescription>, ArrayOf<string>>(
                    (_, _, endpoint, _, _, _, _) =>
                    {
                        createdEndpoint = endpoint;
                        return createdSession.Object;
                    });
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            await verifier.VerifyAsync(expectedApplication, target, target.InstalledCertificate).ConfigureAwait(false);

            replacementFormattingArgument.VerifyNoOtherCalls();
            m_lastCreatedSession.Verify(value => value.OpenAsync(
                It.IsAny<string>(), 60000, It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(),
                true, true, It.IsAny<CancellationToken>()), Times.Once);
            m_lastCreatedSession.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// An enabled reverse target must not fall through to a forward-only session-factory overload.
        /// </summary>
        [Test]
        public async Task EnabledReverseEndpointIsRejectedBeforeSessionCreation()
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            DeviceRegistrarManagedApplicationTarget target = Target(context);
            target.Endpoint.ReverseConnect = new ReverseConnectEndpoint
            {
                Enabled = true,
                ServerUri = ApplicationUri,
                Thumbprint = "captured"
            };
            ConfiguredEndpoint captured =
                SessionDeviceRegistrarManagedApplicationChannelVerifier.SnapshotEndpoint(target.Endpoint);
            ReverseConnectEndpoint capturedReverse = captured.ReverseConnect ??
                throw new InvalidOperationException("The reverse-connect snapshot is missing.");
            Assert.That(capturedReverse, Is.Not.SameAs(target.Endpoint.ReverseConnect));
            Assert.That(capturedReverse.Enabled, Is.True);
            Assert.That(capturedReverse.ServerUri, Is.EqualTo(ApplicationUri));
            Assert.That(capturedReverse.Thumbprint, Is.EqualTo("captured"));
            var factory = Factory(null);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await verifier.VerifyAsync(Application(), target, target.InstalledCertificate).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Certificate snapshots preserve null versus empty byte strings.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void EndpointSnapshotPreservesNullAndEmptyCertificate(bool nullCertificate)
        {
            var endpoint = new ConfiguredEndpoint(null,
                new EndpointDescription
                {
                    Server = new ApplicationDescription(),
                    ServerCertificate = nullCertificate ? default : ByteString.Empty
                }, EndpointConfiguration.Create());

            ConfiguredEndpoint captured =
                SessionDeviceRegistrarManagedApplicationChannelVerifier.SnapshotEndpoint(endpoint);

            Assert.That(captured.Description.ServerCertificate.IsNull, Is.EqualTo(nullCertificate));
            Assert.That(captured.Description.ServerCertificate.Length, Is.Zero);
        }

        /// <summary>
        /// Proxy snapshots preserve null, relative and absolute URLs independently of later property replacement.
        /// </summary>
        [TestCase(null)]
        [TestCase("proxy/path")]
        [TestCase("opc.tcp://proxy.example.test:4840/path")]
        public void EndpointSnapshotPreservesProxyUrl(string? proxyUrl)
        {
            Uri? selectedProxy = proxyUrl == null ? null : new Uri(proxyUrl, UriKind.RelativeOrAbsolute);
            var endpoint = new ConfiguredEndpoint(null,
                new EndpointDescription
                {
                    Server = new ApplicationDescription(),
                    ProxyUrl = selectedProxy
                }, EndpointConfiguration.Create());

            ConfiguredEndpoint captured =
                SessionDeviceRegistrarManagedApplicationChannelVerifier.SnapshotEndpoint(endpoint);
            endpoint.Description.ProxyUrl = null;

            if (selectedProxy == null)
            {
                Assert.That(captured.Description.ProxyUrl, Is.Null);
            }
            else
            {
                Uri capturedProxy = captured.Description.ProxyUrl ??
                    throw new InvalidOperationException("The proxy snapshot is missing.");
                Assert.That(capturedProxy, Is.Not.SameAs(selectedProxy));
                Assert.That(capturedProxy.OriginalString, Is.EqualTo(proxyUrl));
                Assert.That(capturedProxy.IsAbsoluteUri, Is.EqualTo(selectedProxy.IsAbsoluteUri));
            }
        }

        /// <summary>
        /// Snapshotting preserves null and empty extension, token-policy and discovery-URL arrays.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void EndpointSnapshotPreservesNullAndEmptyExtensions(bool nullArray)
        {
            var endpoint = new ConfiguredEndpoint(null,
                new EndpointDescription
                {
                    Server = new ApplicationDescription
                    {
                        DiscoveryUrls = nullArray ? default : ArrayOf<string>.Empty
                    },
                    UserIdentityTokens = nullArray ? default : ArrayOf<UserTokenPolicy>.Empty
                }, EndpointConfiguration.Create())
            {
                Extensions = nullArray ? default : ArrayOf<XmlElement>.Empty
            };
            endpoint.Description.Server.DiscoveryUrls = nullArray ? default : ArrayOf<string>.Empty;

            ConfiguredEndpoint captured =
                SessionDeviceRegistrarManagedApplicationChannelVerifier.SnapshotEndpoint(endpoint);

            Assert.That(captured.Extensions.IsNull, Is.EqualTo(nullArray));
            Assert.That(captured.Extensions.Count, Is.Zero);
            Assert.That(captured.DiscoveryEndpoints.IsNull, Is.True);
            Assert.That(captured.Description.Server.DiscoveryUrls.IsNull,
                Is.EqualTo(endpoint.Description.Server.DiscoveryUrls.IsNull));
            Assert.That(captured.Description.Server.DiscoveryUrls.Count, Is.Zero);
            Assert.That(captured.Description.UserIdentityTokens.IsNull,
                Is.EqualTo(endpoint.Description.UserIdentityTokens.IsNull));
            Assert.That(captured.Description.UserIdentityTokens.Count, Is.Zero);
        }

        /// <summary>
        /// Null XML values remain null rather than becoming invalid DOM objects during snapshotting.
        /// </summary>
        [Test]
        public void EndpointSnapshotPreservesNullExtensionValue()
        {
            var endpoint = new ConfiguredEndpoint(null,
                new EndpointDescription { Server = new ApplicationDescription() }, EndpointConfiguration.Create())
            {
                Extensions = [XmlElement.Empty]
            };

            ConfiguredEndpoint captured =
                SessionDeviceRegistrarManagedApplicationChannelVerifier.SnapshotEndpoint(endpoint);

            Assert.That(captured.Extensions.IsNull, Is.False);
            Assert.That(captured.Extensions.Count, Is.EqualTo(1));
            Assert.That(captured.Extensions[0].IsNull, Is.True);
        }

        /// <summary>
        /// Parameterless endpoint construction can leave configuration absent until deserialization supplies it.
        /// </summary>
        [Test]
        public async Task MissingEndpointConfigurationIsRejectedBeforeSessionCreation()
        {
            var endpoint = new ConfiguredEndpoint();
            endpoint.Description.EndpointUrl = "opc.tcp://managed:4840";
            endpoint.Description.Server = new ApplicationDescription
            {
                ApplicationUri = ApplicationUri
            };
            endpoint.Description.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            endpoint.Description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            var target = new DeviceRegistrarManagedApplicationTarget(
                ["https://discovery.example.test/managed"], ByteString.From(m_certificate.RawData.AsSpan()),
                endpoint, new UserIdentity(), ["DA"]);
            var factory = Factory(null);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await verifier.VerifyAsync(Application(), target, target.InstalledCertificate).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Deferred name metadata is rejected until the shared safe-copy contract is available.
        /// </summary>
        [Test]
        public async Task DeferredEndpointNameIsRejectedBeforeSessionCreation()
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            DeviceRegistrarManagedApplicationTarget target = Target(context);
            var formattingArgument = new Mock<IFormattable>(MockBehavior.Strict);
            target.Endpoint.Description.Server.ApplicationName =
                new LocalizedText(new TranslationInfo("name", "en", "{0}", formattingArgument.Object));
            var factory = Factory(null);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await verifier.VerifyAsync(Application(), target, target.InstalledCertificate).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            factory.VerifyNoOtherCalls();
            formattingArgument.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A second endpoint identity is rejected rather than silently discarded in favor of the explicit target.
        /// </summary>
        [Test]
        public async Task EmbeddedEndpointIdentityIsRejectedBeforeSessionCreation()
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            DeviceRegistrarManagedApplicationTarget target = Target(context);
            target.Endpoint.UserIdentity = new AnonymousIdentityToken();
            var factory = Factory(null);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await verifier.VerifyAsync(Application(), target, target.InstalledCertificate).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// An invalid selected connection URL is rejected before session creation.
        /// </summary>
        [Test]
        public async Task InvalidSelectedEndpointUrlIsRejectedBeforeSessionCreation()
        {
            var factory = Factory(null);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "not-an-absolute-url",
                Server = new ApplicationDescription { ApplicationUri = ApplicationUri },
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            var target = new DeviceRegistrarManagedApplicationTarget(
                ["opc.tcp://managed:4840"],
                ByteString.From(m_certificate.RawData.AsSpan()),
                new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create()),
                new UserIdentity(),
                ["DA"]);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await verifier.VerifyAsync(
                    Application(), target, ByteString.From(m_certificate.RawData.AsSpan())).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The verifier rejects a legacy SHA-1 policy before creating a session.
        /// </summary>
        [Test]
        public async Task LegacySecurityPolicyIsRejectedBeforeSessionCreation()
        {
            var factory = Factory(null);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                Configuration(), factory.Object);
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://managed:4840",
                Server = new ApplicationDescription { ApplicationUri = ApplicationUri },
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256
            };
            var target = new DeviceRegistrarManagedApplicationTarget(
                ["opc.tcp://managed:4840"],
                ByteString.From(m_certificate.RawData.AsSpan()),
                new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create()),
                new UserIdentity(),
                ["DA"]);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await verifier.VerifyAsync(
                    Application(), target, ByteString.From(m_certificate.RawData.AsSpan())).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Fluent registration creates the verifier lazily and keeps the selected factory host-owned.
        /// </summary>
        [Test]
        public void FluentRegistrationDefersConfigurationResolution()
        {
            var services = new ServiceCollection();
            var factory = PinnedFactory();
            services.AddSingleton(factory.Object);
            int calls = 0;
            services.AddDeviceRegistrarManagedApplicationChannelVerifier(provider =>
            {
                calls++;
                return provider.GetRequiredService<ApplicationConfiguration>();
            });
            Assert.Throws<InvalidOperationException>(() =>
                services.AddDeviceRegistrarManagedApplicationChannelVerifier(
                    provider => provider.GetRequiredService<ApplicationConfiguration>()));
            services.AddSingleton(Configuration());

            using ServiceProvider container = services.BuildServiceProvider();
            IDeviceRegistrarManagedApplicationChannelVerifier first =
                container.GetRequiredService<IDeviceRegistrarManagedApplicationChannelVerifier>();
            IDeviceRegistrarManagedApplicationChannelVerifier repeated =
                container.GetRequiredService<IDeviceRegistrarManagedApplicationChannelVerifier>();

            Assert.That(first, Is.TypeOf<SessionDeviceRegistrarManagedApplicationChannelVerifier>());
            Assert.That(repeated, Is.SameAs(first));
            Assert.That(calls, Is.EqualTo(1));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The verifier leaves the configured manager's current trust policy unchanged.
        /// </summary>
        [Test]
        public void ConstructorPreservesCertificateManagerAutoAcceptPolicy()
        {
            ApplicationConfiguration configuration = Configuration();
            using var manager = CertificateManagerFactory.Create(
                configuration.SecurityConfiguration!, NUnitTelemetryContext.Create());
            manager.AutoAcceptUntrustedCertificates = true;
            configuration.CertificateManager = manager;

            _ = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                configuration, Factory(null).Object);

            Assert.That(manager.AutoAcceptUntrustedCertificates, Is.True);
            Assert.That(configuration.SecurityConfiguration!.AutoAcceptUntrustedCertificates, Is.False);
        }

        /// <summary>
        /// The verifier uses the shared manager without changing a policy updated after construction.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task VerificationPreservesLaterAutoAcceptChange(bool changeManager)
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            context.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            context.Channel.Object.EndpointDescription.Server.ApplicationUri = ApplicationUri;
            ApplicationConfiguration configuration = Configuration();
            using var manager = CertificateManagerFactory.Create(
                configuration.SecurityConfiguration!, NUnitTelemetryContext.Create());
            configuration.CertificateManager = manager;
            var factory = Factory(context.Channel.Object);
            var verifier = new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                configuration, factory.Object);

            if (changeManager)
            {
                manager.AutoAcceptUntrustedCertificates = true;
            }
            else
            {
                configuration.SecurityConfiguration!.AutoAcceptUntrustedCertificates = true;
            }

            await verifier.VerifyAsync(
                Application(), Target(context), ByteString.From(m_certificate.RawData.AsSpan()))
                .ConfigureAwait(false);

            Assert.That(manager.AutoAcceptUntrustedCertificates, Is.EqualTo(changeManager));
            Assert.That(
                configuration.SecurityConfiguration!.AutoAcceptUntrustedCertificates,
                Is.EqualTo(!changeManager));
            factory.Verify(value => value.CreateChannelAsync(
                It.IsAny<ApplicationConfiguration>(), It.IsAny<ITransportWaitingConnection>(),
                It.IsAny<ConfiguredEndpoint>(), false, true, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// One explicit host provider owns managed-server endpoint metadata and authentication policy.
        /// </summary>
        [Test]
        public void ManagedApplicationProviderRegistrationRejectsAmbiguousOwnership()
        {
            var services = new ServiceCollection();
            var provider = new Mock<IDeviceRegistrarManagedApplicationProvider>(MockBehavior.Strict);
            services.AddDeviceRegistrarManagedApplicationProvider(_ => provider.Object);
            using ServiceProvider container = services.BuildServiceProvider();

            Assert.That(container.GetRequiredService<IDeviceRegistrarManagedApplicationProvider>(),
                Is.SameAs(provider.Object));
            Assert.Throws<InvalidOperationException>(() =>
                services.AddDeviceRegistrarManagedApplicationProvider(_ => provider.Object));
        }

        /// <summary>
        /// Runs one cleanup action and retains its failure while later cleanup continues.
        /// </summary>
        private static async Task CleanupAsync(List<Exception> errors, Func<Task> cleanup)
        {
            try
            {
                await cleanup().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        /// <summary>
        /// Rethrows the test failure or reports it together with independent cleanup failures.
        /// </summary>
        private static void ThrowCleanupFailures(
            Exception? testFailure,
            List<Exception> cleanupErrors,
            string message)
        {
            if (testFailure != null)
            {
                cleanupErrors.Insert(0, testFailure);
            }
            if (cleanupErrors.Count == 1)
            {
                ExceptionDispatchInfo.Capture(cleanupErrors[0]).Throw();
            }
            if (cleanupErrors.Count > 1)
            {
                throw new AggregateException(message, cleanupErrors);
            }
        }

        /// <summary>
        /// Returns a host-selected signed HTTPS endpoint with an independent application certificate.
        /// </summary>
        private static EndpointDescription HttpsEndpoint(ByteString applicationCertificate)
        {
            return new EndpointDescription
            {
                EndpointUrl = "https://localhost:4840",
                Server = Application(),
                SecurityMode = MessageSecurityMode.Sign,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                TransportProfileUri = Profiles.HttpsBinaryTransport,
                ServerCertificate = applicationCertificate,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "username",
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    }
                ]
            };
        }

        /// <summary>
        /// Supplies session metadata without unrelated server capability behavior.
        /// </summary>
        private static DataValue ReadHttpsProofValue(ReadValueId value)
        {
            if (value.NodeId == global::Opc.Ua.VariableIds.Server_NamespaceArray)
            {
                return new DataValue(ArrayOf.Create([Namespaces.OpcUa]));
            }
            if (value.NodeId == global::Opc.Ua.VariableIds.Server_ServerArray)
            {
                return new DataValue(ArrayOf.Create([ApplicationUri]));
            }
            if (value.NodeId == global::Opc.Ua.VariableIds.Server_ServerStatus_State)
            {
                return new DataValue(new Variant((int)ServerState.Running));
            }
            return new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown);
        }

        /// <summary>
        /// Returns a fresh session fixture from the existing stack factory seam.
        /// </summary>
        private Mock<ISessionFactory> Factory(
            ITransportChannel? channel,
            bool supportsApplicationUriValidation = true)
        {
            var factory = PinnedFactory();
            if (channel != null)
            {
                ConfiguredEndpoint? selectedEndpoint = null;
                Mock<Opc.Ua.Client.ISession> createdSession = CreateSessionMock(
                    channel,
                    () => selectedEndpoint ?? throw new InvalidOperationException(
                        "The session endpoint has not been supplied by the factory."),
                    supportsApplicationUriValidation);
                m_lastCreatedSession = createdSession;
                factory.Setup(value => value.CreateChannelAsync(
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<ConfiguredEndpoint>(),
                    false,
                    true,
                    It.IsAny<CancellationToken>()))
                    .ReturnsAsync(channel);
                factory.Setup(value => value.Create(
                    It.IsAny<ITransportChannel>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<ConfiguredEndpoint>(),
                    null,
                    null,
                    It.IsAny<ArrayOf<EndpointDescription>>(),
                    It.IsAny<ArrayOf<string>>()))
                    .Returns<ITransportChannel, ApplicationConfiguration, ConfiguredEndpoint, Certificate?,
                        CertificateCollection?, ArrayOf<EndpointDescription>, ArrayOf<string>>(
                        (_, _, endpoint, _, _, _, _) =>
                        {
                            selectedEndpoint = endpoint;
                            return createdSession.Object;
                        });
            }
            return factory;
        }

        /// <summary>
        /// Creates a session double bound to the exact channel supplied by the verifier.
        /// </summary>
        private static Mock<Opc.Ua.Client.ISession> CreateSessionMock(
            ITransportChannel channel,
            Func<ConfiguredEndpoint> configuredEndpoint,
            bool supportsApplicationUriValidation = true)
        {
            var session = new Mock<Opc.Ua.Client.ISession>(MockBehavior.Strict);
            if (supportsApplicationUriValidation)
            {
                session.As<IExpectedServerApplicationUriSession>()
                    .Setup(value => value.RequireServerApplicationUri(It.IsAny<string>()));
            }
            session.SetupGet(value => value.ConfiguredEndpoint).Returns(configuredEndpoint);
            session.SetupGet(value => value.TransportChannel).Returns(channel);
            session.Setup(value => value.OpenAsync(
                It.IsAny<string>(),
                It.IsAny<uint>(),
                It.IsAny<IUserIdentity>(),
                It.IsAny<ArrayOf<string>>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            session.Setup(value => value.CloseAsync(
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(StatusCodes.Good);
            session.Setup(value => value.DisposeAsync()).Returns(new ValueTask());
            return session;
        }

        /// <summary>
        /// Creates a test factory that declares the required pre-activation certificate-binding contract.
        /// </summary>
        private static Mock<ISessionFactory> PinnedFactory()
        {
            var factory = new Mock<ISessionFactory>(MockBehavior.Strict);
            factory.As<IPinnedServerCertificateSessionFactory>();
            return factory;
        }

        /// <summary>
        /// Creates an explicitly trusted client configuration for the verifier.
        /// </summary>
        private static ApplicationConfiguration Configuration()
        {
            return new ApplicationConfiguration(NUnitTelemetryContext.Create())
            {
                ApplicationName = "Managed application proof tests",
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration { AutoAcceptUntrustedCertificates = false }
            };
        }

        /// <summary>
        /// Discovers the server's SHA-256 encrypted endpoint and extracts the actual leaf certificate.
        /// </summary>
        private static async Task<(EndpointDescription Endpoint, ByteString Certificate)> DiscoverSecureEndpointAsync(
            ServerFixture<ReferenceServer> server)
        {
            string url = $"opc.tcp://localhost:{server.Port.ToString(CultureInfo.InvariantCulture)}";
            using DiscoveryClient discovery = await DiscoveryClient.CreateAsync(
                new Uri(url),
                EndpointConfiguration.Create(server.Config),
                NUnitTelemetryContext.Create(),
                ct: CancellationToken.None).ConfigureAwait(false);
            ArrayOf<EndpointDescription> endpoints = await discovery.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);
            for (int index = 0; index < endpoints.Count; index++)
            {
                EndpointDescription endpoint = endpoints[index];
                if (endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                    endpoint.SecurityPolicyUri != SecurityPolicies.Basic256Sha256 ||
                    endpoint.ServerCertificate.IsEmpty)
                {
                    continue;
                }
                using CertificateCollection chain = DefaultCertificateFactory.Instance.ParseChainBlob(
                    endpoint.ServerCertificate.ToArray().AsMemory());
                if (chain.Count == 0)
                {
                    continue;
                }
                return (CoreUtils.Clone(endpoint)!, ByteString.From(chain[0].RawData.AsSpan()));
            }
            throw new InvalidOperationException("The test server did not advertise a trusted SHA-256 endpoint.");
        }

        /// <summary>
        /// Adds one explicitly trusted endpoint chain to the client certificate manager.
        /// </summary>
        private static async Task TrustPeerAsync(
            ApplicationConfiguration configuration,
            ByteString encodedChain)
        {
            using CertificateCollection chain = DefaultCertificateFactory.Instance.ParseChainBlob(
                encodedChain.ToArray().AsMemory());
            if (chain.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            using ICertificateStore trusted = configuration.CertificateManager!
                .OpenTrustedStore(TrustListIdentifier.Peers);
            await trusted.AddAsync(chain[0]).ConfigureAwait(false);
            configuration.CertificateManager.NotifyTrustListChanged(
                TrustListIdentifier.Peers, trustChanged: true, crlChanged: false);
        }

        /// <summary>
        /// Creates explicit endpoint metadata while retaining the operator's previous pin across rotation.
        /// </summary>
        private static DeviceRegistrarManagedApplicationTarget CreateTarget(
            EndpointDescription endpoint,
            ByteString operatorPin)
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            string endpointUrl = endpoint.EndpointUrl ??
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            return new DeviceRegistrarManagedApplicationTarget(
                [new Uri(endpointUrl).GetLeftPart(UriPartial.Authority)],
                operatorPin,
                new ConfiguredEndpoint(null, endpoint, endpointConfiguration),
                new UserIdentity(),
                ["DA"]);
        }

        /// <summary>
        /// Creates the managed application identity used by the endpoint verification.
        /// </summary>
        private static ApplicationDescription Application()
        {
            return new ApplicationDescription
            {
                ApplicationUri = ApplicationUri,
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://managed:4840"]
            };
        }

        /// <summary>
        /// Creates host-selected endpoint metadata tied to the fixture's actual peer certificate.
        /// </summary>
        private DeviceRegistrarManagedApplicationTarget Target(SessionRequestContextTestContext context)
        {
            context.Channel.Object.EndpointDescription.ServerCertificate =
                ByteString.From(m_certificate.RawData.AsSpan());
            string endpointUrl = context.Channel.Object.EndpointDescription.EndpointUrl ??
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            return new DeviceRegistrarManagedApplicationTarget(
                [endpointUrl],
                ByteString.From(m_certificate.RawData.AsSpan()),
                new ConfiguredEndpoint(
                    null, CoreUtils.Clone(context.Channel.Object.EndpointDescription)!, EndpointConfiguration.Create()),
                new UserIdentity(),
                ["DA"]);
        }

        /// <summary>
        /// Records synchronous rejection-log text during real channel creation, before Session construction.
        /// </summary>
        private sealed class ChannelRejectionRecordingSessionFactory :
            DefaultSessionFactory,
            IPinnedServerCertificateSessionFactory
        {
            /// <summary>
            /// Creates a factory using the isolated client's synchronous log recorder.
            /// </summary>
            public ChannelRejectionRecordingSessionFactory(
                ITelemetryContext telemetry,
                RecordingLoggerProvider records)
                : base(telemetry)
            {
                m_records = records;
            }

            /// <summary>
            /// Whether channel creation propagated a failure before constructing a Session.
            /// </summary>
            public bool ChannelCreationFailed { get; private set; }

            /// <summary>
            /// Number of Session construction calls after channel creation.
            /// </summary>
            public int SessionConstructionCount { get; private set; }

            /// <summary>
            /// Immutable formatted Log11 evidence captured only within the channel-creation interval.
            /// </summary>
            public ArrayOf<string> RejectionMessages { get; private set; }

            /// <inheritdoc/>
            public override async Task<ITransportChannel> CreateChannelAsync(
                ApplicationConfiguration configuration,
                ITransportWaitingConnection? connection,
                ConfiguredEndpoint endpoint,
                bool updateBeforeConnect,
                bool checkDomain,
                CancellationToken ct = default)
            {
                int firstRecord = m_records.Records.Count;
                try
                {
                    return await base.CreateChannelAsync(
                        configuration, connection, endpoint, updateBeforeConnect, checkDomain, ct)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    ChannelCreationFailed = true;
                    throw;
                }
                finally
                {
                    RejectionMessages = m_records.Records.Skip(firstRecord)
                        .Where(record => record.CategoryName == "Opc.Ua.CertificateValidationCore" &&
                            record.EventId.Name == "CertificateValidationLog11" &&
                            record.LogLevel == LogLevel.Error)
                        .Select(record => record.Message)
                        .ToArray()
                        .ToArrayOf();
                }
            }

            /// <inheritdoc/>
            public override ISession Create(
                ITransportChannel channel,
                ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint,
                Certificate? clientCertificate = null,
                CertificateCollection? clientCertificateChain = null,
                ArrayOf<EndpointDescription> availableEndpoints = default,
                ArrayOf<string> discoveryProfileUris = default)
            {
                SessionConstructionCount++;
                return base.Create(
                    channel, configuration, endpoint, clientCertificate, clientCertificateChain,
                    availableEndpoints, discoveryProfileUris);
            }

            /// <summary>The synchronous recorder owned by the test.</summary>
            private readonly RecordingLoggerProvider m_records;
        }

        /// <summary>
        /// Records whether an inherited default-factory override receives an onboarding identity.
        /// </summary>
        private sealed class CredentialReceivingDerivedSessionFactory : DefaultSessionFactory
        {
            /// <summary>
            /// Creates the factory with test telemetry.
            /// </summary>
            public CredentialReceivingDerivedSessionFactory()
                : base(NUnitTelemetryContext.Create())
            {
            }

            /// <summary>
            /// Number of credential-bearing session creation calls.
            /// </summary>
            public int CreateCalls { get; private set; }

            /// <summary>
            /// Identity supplied to the credential-bearing session creation call.
            /// </summary>
            public IUserIdentity? ReceivedIdentity { get; private set; }

            /// <inheritdoc/>
            public override Task<ISession> CreateAsync(
                ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint,
                bool updateBeforeConnect,
                bool checkDomain,
                string sessionName,
                uint sessionTimeout,
                IUserIdentity? identity,
                ArrayOf<string> preferredLocales,
                CancellationToken ct = default)
            {
                CreateCalls++;
                ReceivedIdentity = identity;
                throw new InvalidOperationException("The verifier must reject this factory before session creation.");
            }
        }

        /// <summary>The application URI shared by the test endpoint and its actual certificate.</summary>
        private const string ApplicationUri = "urn:managed:application";

        /// <summary>The fixture-owned leaf certificate.</summary>
        private Certificate m_certificate = null!;

        /// <summary>The session double returned by the latest factory fixture.</summary>
        private Mock<Opc.Ua.Client.ISession>? m_lastCreatedSession;
    }
}
