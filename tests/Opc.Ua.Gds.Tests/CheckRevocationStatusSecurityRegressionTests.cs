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
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Drives the actual revocation handler with real certificates and CRLs but no HTTP or UA server.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    public sealed class CheckRevocationStatusSecurityRegressionTests
    {
        /// <summary>
        /// An arbitrary certificate cannot make the handler retrieve its AIA or CRL distribution URLs.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF25UnknownIssuerNeverUsesCallerAiaUrls()
        {
            await using var fixture = new RevocationContext(includeRoot: false);

            CheckRevocationStatusMethodStateResult result = await fixture.CheckAsync().ConfigureAwait(false);

            AssertCertificateStatus(result, StatusCodes.BadCertificateChainIncomplete);
            Assert.That(result.ValidityTime.IsNull, Is.True);
            fixture.Store.Verify(store => store.EnumerateCRLsAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.AssertNoRevocationRetrieval();
        }

        /// <summary>
        /// A caller-supplied trust anchor is not an alternative to configured local issuer authority.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF25UnconfiguredSelfSignedCertificateIsUntrusted()
        {
            await using var fixture = new RevocationContext(includeRoot: false);

            CheckRevocationStatusMethodStateResult result = await fixture.CheckAsync(
                certificate: fixture.Root.RawData.ToByteString()).ConfigureAwait(false);

            AssertCertificateStatus(result, StatusCodes.BadCertificateUntrusted);
            Assert.That(result.ValidityTime.IsNull, Is.True);
            fixture.Store.Verify(store => store.EnumerateCRLsAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A locally known intermediate does not authorize downloading its absent parent from the caller's chain.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF25IncompleteLocalChainFailsClosed()
        {
            await using var fixture = new RevocationContext(includeRoot: false, intermediate: true);

            CheckRevocationStatusMethodStateResult result = await fixture.CheckAsync().ConfigureAwait(false);

            AssertCertificateStatus(result, StatusCodes.BadCertificateChainIncomplete);
            Assert.That(result.ValidityTime.IsNull, Is.True);
            fixture.Store.Verify(store => store.EnumerateCRLsAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.AssertNoRevocationRetrieval();
        }

        /// <summary>
        /// Caller retrieval URLs remain inert even for an otherwise valid chain and its local revocation check.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF25KnownIssuerUsesOnlyVerifiedLocalCrls(bool revoked)
        {
            await using var fixture = new RevocationContext();
            fixture.AddCrl(fixture.Root, revoked ? fixture.Leaf : null);

            CheckRevocationStatusMethodStateResult result = await fixture.CheckAsync().ConfigureAwait(false);

            AssertCertificateStatus(result, revoked ? StatusCodes.BadCertificateRevoked : StatusCodes.Good);
            if (revoked)
            {
                Assert.That(result.ValidityTime.IsNull, Is.True);
            }
            else
            {
                Assert.That(result.ValidityTime, Is.EqualTo((DateTimeUtc)fixture.NextUpdate));
            }
            fixture.Store.Verify(store => store.EnumerateCRLsAsync(It.IsAny<CancellationToken>()), Times.Once);
            fixture.AssertNoRevocationRetrieval();
        }

        /// <summary>
        /// Missing, expired or forged local CRLs cannot trigger online fallback or produce Good.
        /// </summary>
        [TestCase("Missing")]
        [TestCase("Expired")]
        [TestCase("Forged")]
        [Category("SecurityRegression")]
        public async Task SecurityF25UnusableLocalCrlFailsClosed(string crlKind)
        {
            await using var fixture = new RevocationContext();
            if (crlKind == "Expired")
            {
                fixture.AddCrl(fixture.Root, expired: true);
            }
            else if (crlKind == "Forged")
            {
                Certificate otherIssuer = fixture.CreateRoot(fixture.Root.Subject);
                fixture.AddCrl(otherIssuer);
            }

            CheckRevocationStatusMethodStateResult result = await fixture.CheckAsync().ConfigureAwait(false);

            AssertCertificateStatus(result, StatusCodes.BadCertificateRevocationUnknown);
            Assert.That(result.ValidityTime.IsNull, Is.True);
            fixture.AssertNoRevocationRetrieval();
        }

        /// <summary>
        /// A local store without CRL support cannot silently convert an unknown status to Good.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF25UnavailableLocalRevocationFailsClosed()
        {
            await using var fixture = new RevocationContext();
            fixture.Store.SetupGet(store => store.SupportsCRLs).Returns(false);

            CheckRevocationStatusMethodStateResult result = await fixture.CheckAsync().ConfigureAwait(false);

            AssertCertificateStatus(result, StatusCodes.BadCertificateRevocationUnknown);
            Assert.That(result.ValidityTime.IsNull, Is.True);
            fixture.Store.Verify(store => store.EnumerateCRLsAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.AssertNoRevocationRetrieval();
        }

        /// <summary>
        /// A matching issuer name without the actual issuing key cannot complete local verification.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF25InvalidLocalSignatureIsRejected()
        {
            await using var fixture = new RevocationContext(invalidSignature: true);
            fixture.AddCrl(fixture.Root);

            CheckRevocationStatusMethodStateResult result = await fixture.CheckAsync().ConfigureAwait(false);

            AssertCertificateStatus(result, StatusCodes.BadCertificateInvalid);
            Assert.That(result.ValidityTime.IsNull, Is.True);
            fixture.Store.Verify(store => store.EnumerateCRLsAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// The offline path validates the complete chain and checks revocation of intermediate issuers too.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF25CompleteLocalChainChecksIssuerRevocation(bool revokedIssuer)
        {
            await using var fixture = new RevocationContext(intermediate: true);
            fixture.AddCrl(fixture.Root, revokedIssuer ? fixture.Intermediate : null);
            fixture.AddCrl(fixture.Intermediate!);

            CheckRevocationStatusMethodStateResult result = await fixture.CheckAsync().ConfigureAwait(false);

            AssertCertificateStatus(result, revokedIssuer ? StatusCodes.BadCertificateIssuerRevoked : StatusCodes.Good);
            Assert.That(result.ValidityTime.IsNull, Is.EqualTo(revokedIssuer));
            fixture.AssertNoRevocationRetrieval();
        }

        /// <summary>
        /// Offline issuer verification does not bypass CA path-length constraints on any target framework.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF25OfflineChainPreservesPathConstraints()
        {
            await using var fixture = new RevocationContext(intermediate: true, rootPathLength: 0);
            fixture.AddCrl(fixture.Root);
            fixture.AddCrl(fixture.Intermediate!);

            CheckRevocationStatusMethodStateResult result = await fixture.CheckAsync().ConfigureAwait(false);

            AssertCertificateStatus(result, StatusCodes.BadCertificateInvalid);
            Assert.That(result.ValidityTime.IsNull, Is.True);
            fixture.Store.Verify(store => store.EnumerateCRLsAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// The existing authenticated-channel boundary runs before any local certificate-store operation.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF25UnsecuredChannelFailsBeforeStoreAccess()
        {
            await using var fixture = new RevocationContext();

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.CheckAsync(MessageSecurityMode.None).ConfigureAwait(false))
                ?? throw new AssertionException("Expected a service failure.");

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            fixture.Store.Verify(store => store.EnumerateAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.Store.Verify(store => store.EnumerateCRLsAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Unparseable input remains a concrete certificate error rather than revocation or success.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF25MalformedCertificateReturnsInvalid()
        {
            await using var fixture = new RevocationContext();

            CheckRevocationStatusMethodStateResult result = await fixture.CheckAsync(
                certificate: [0x01, 0x02, 0x03]).ConfigureAwait(false);

            AssertCertificateStatus(result, StatusCodes.BadCertificateInvalid);
            Assert.That(result.ValidityTime.IsNull, Is.True);
            fixture.Store.Verify(store => store.EnumerateAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Distinguishes a successfully handled query from its concrete certificate status.
        /// </summary>
        private static void AssertCertificateStatus(CheckRevocationStatusMethodStateResult result, StatusCode status)
        {
            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.CertificateStatus, Is.EqualTo(status));
        }

        /// <summary>
        /// Provides the actual node manager with only local, explicitly configured issuer and CRL snapshots.
        /// </summary>
        private sealed class RevocationContext : IAsyncDisposable
        {
            /// <summary>
            /// Creates a real certificate path and a node manager without starting a server or certificate store.
            /// </summary>
            internal RevocationContext(
                bool includeRoot = true,
                bool intermediate = false,
                bool invalidSignature = false,
                int rootPathLength = -1)
            {
                DateTime now = DateTime.UtcNow;
                Now = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                m_messageContext = ServiceMessageContext.Create(telemetry);
                var server = new Mock<IServerInternal>();
                server.SetupGet(value => value.Telemetry).Returns(telemetry);
                server.SetupGet(value => value.NamespaceUris).Returns(m_messageContext.NamespaceUris);
                server.SetupGet(value => value.ServerUris).Returns(m_messageContext.ServerUris);
                server.SetupGet(value => value.Factory).Returns(m_messageContext.Factory);
                server.SetupGet(value => value.TypeTree).Returns(new TypeTable(m_messageContext.NamespaceUris));
                server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
                Root = CreateRoot("CN=GDS Offline Security Regression CA", rootPathLength);
                if (includeRoot)
                {
                    m_issuers.Add(Root);
                }
                Certificate issuer = Root;
                if (intermediate)
                {
                    Intermediate = Own(CertificateBuilder.Create("CN=GDS Offline Intermediate")
                        .SetNotBefore(Now.AddDays(-1))
                        .SetNotAfter(Now.AddMonths(3))
                        .SetCAConstraint(0)
                        .AddExtension(CreateIssuerUrl())
                        .SetIssuer(Root).CreateForRSA());
                    m_issuers.Add(Intermediate);
                    issuer = Intermediate;
                }
                if (invalidSignature)
                {
                    issuer = CreateRoot(Root.Subject);
                }
                Leaf = Own(CertificateBuilder.Create("CN=Caller Certificate With Retrieval URLs")
                    .SetNotBefore(Now.AddDays(-1))
                    .SetNotAfter(Now.AddMonths(1))
                    .AddExtension(CreateIssuerUrl())
                    .AddExtension(CreateCrlUrl())
                    .SetIssuer(issuer).CreateForRSA());
                Store = new Mock<ICertificateStore>(MockBehavior.Strict);
                Store.SetupGet(store => store.SupportsCRLs).Returns(true);
                Store.Setup(store => store.EnumerateAsync(It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        var snapshot = new CertificateCollection();
                        foreach (Certificate localIssuer in m_issuers)
                        {
                            snapshot.Add(localIssuer);
                        }
                        return Task.FromResult(snapshot);
                    });
                Store.Setup(store => store.EnumerateCRLsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(m_crls);
                Store.Setup(store => store.Dispose());
                var trustList = new Mock<CertificateTrustList>();
                trustList.Setup(list => list.OpenStore(It.IsAny<ITelemetryContext>())).Returns(Store.Object);
                var configuration = new ApplicationConfiguration(telemetry)
                {
                    ApplicationName = "OfflineRevocationSecurityRegression",
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        TrustedIssuerCertificates = trustList.Object
                    }
                };
                var database = new LinqApplicationsDatabase();
                m_nodeManager = new ApplicationsNodeManager(
                    server.Object, configuration, database, database, Mock.Of<ICertificateGroup>());
            }

            /// <summary>
            /// Gets a stable within-test time well away from certificate and CRL validity boundaries.
            /// </summary>
            internal DateTime Now { get; }

            /// <summary>
            /// Gets the expected local revocation-result deadline.
            /// </summary>
            internal DateTime NextUpdate => Now.AddDays(1);

            /// <summary>
            /// Gets the configured local root authority.
            /// </summary>
            internal Certificate Root { get; }

            /// <summary>
            /// Gets the optional local intermediate authority.
            /// </summary>
            internal Certificate? Intermediate { get; }

            /// <summary>
            /// Gets the caller certificate, whose retrieval extensions must remain inert.
            /// </summary>
            internal Certificate Leaf { get; }

            /// <summary>
            /// Gets the strict local-store collaborator; it offers no issuer-download or online-validation path.
            /// </summary>
            internal Mock<ICertificateStore> Store { get; }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                try
                {
                    await m_nodeManager.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    m_issuers.Dispose();
                    foreach (Certificate certificate in m_ownedCertificates)
                    {
                        certificate.Dispose();
                    }
                }
            }

            /// <summary>
            /// Adds an owned CA certificate without installing it into operating-system or application trust.
            /// </summary>
            internal Certificate CreateRoot(string subject, int pathLength = -1)
            {
                return Own(CertificateBuilder.Create(subject)
                    .SetNotBefore(Now.AddDays(-2))
                    .SetNotAfter(Now.AddYears(1))
                    .SetCAConstraint(pathLength)
                    .CreateForRSA());
            }

            /// <summary>
            /// Creates a genuine locally signed CRL, optionally revoked or expired.
            /// </summary>
            internal void AddCrl(Certificate issuer, Certificate? revoked = null, bool expired = false)
            {
                CrlBuilder builder = CrlBuilder.Create(issuer.SubjectName, HashAlgorithmName.SHA256)
                    .SetThisUpdate(Now.AddDays(-2))
                    .SetNextUpdate(expired ? Now.AddDays(-1) : NextUpdate);
                if (revoked != null)
                {
                    builder.AddRevokedCertificate(revoked);
                }
                m_crls.Add(new X509CRL(builder.CreateForRSA(issuer)));
            }

            /// <summary>
            /// Calls the same concrete handler wired to Directory.CheckRevocationStatus.
            /// </summary>
            internal ValueTask<CheckRevocationStatusMethodStateResult> CheckAsync(
                MessageSecurityMode mode = MessageSecurityMode.Sign,
                ByteString certificate = default)
            {
                var channel = new SecureChannelContext(
                    "offline-revocation-channel",
                    new EndpointDescription { SecurityMode = mode },
                    RequestEncoding.Binary);
                var operation = new OperationContext(
                    new RequestHeader(), channel, RequestType.Call, RequestLifetime.None);
                var context = new SystemContext(operation, m_messageContext.Telemetry)
                {
                    NamespaceUris = m_messageContext.NamespaceUris
                };
                return m_nodeManager.OnCheckRevocationStatusAsync(
                    context, new MethodState(null), NodeId.Null,
                    certificate.IsNull ? Leaf.RawData.ToByteString() : certificate, CancellationToken.None);
            }

            /// <summary>
            /// Requires CRL interpretation to use the loaded local snapshot, never a secondary retrieval operation.
            /// </summary>
            internal void AssertNoRevocationRetrieval()
            {
                Store.Verify(store => store.IsRevokedAsync(
                    It.IsAny<Certificate>(), It.IsAny<Certificate>(), It.IsAny<CancellationToken>()), Times.Never);
            }

            /// <summary>
            /// Tracks a certificate handle owned exclusively by this fixture.
            /// </summary>
            private Certificate Own(Certificate certificate)
            {
                m_ownedCertificates.Add(certificate);
                return certificate;
            }

            /// <summary>
            /// Encodes a caIssuers AIA URL without making that URL a retrieval collaborator.
            /// </summary>
            private static X509Extension CreateIssuerUrl()
            {
                var writer = new AsnWriter(AsnEncodingRules.DER);
                writer.PushSequence();
                writer.PushSequence();
                writer.WriteObjectIdentifier("1.3.6.1.5.5.7.48.2");
                writer.WriteCharacterString(UniversalTagNumber.IA5String,
                    "http://127.0.0.1:0/security-f25-issuer.der", new Asn1Tag(TagClass.ContextSpecific, 6));
                writer.PopSequence();
                writer.PopSequence();
                return new X509Extension("1.3.6.1.5.5.7.1.1", writer.Encode(), false);
            }

            /// <summary>
            /// Encodes a caller-chosen CRL URL which must not replace local revocation authority.
            /// </summary>
            private static X509Extension CreateCrlUrl()
            {
                var writer = new AsnWriter(AsnEncodingRules.DER);
                var name = new Asn1Tag(TagClass.ContextSpecific, 0, true);
                writer.PushSequence();
                writer.PushSequence();
                writer.PushSequence(name);
                writer.PushSequence(name);
                writer.WriteCharacterString(UniversalTagNumber.IA5String,
                    "http://127.0.0.1:0/security-f25.crl", new Asn1Tag(TagClass.ContextSpecific, 6));
                writer.PopSequence(name);
                writer.PopSequence(name);
                writer.PopSequence();
                writer.PopSequence();
                return new X509Extension("2.5.29.31", writer.Encode(), false);
            }

            /// <summary>The concrete production node manager under test.</summary>
            private readonly ApplicationsNodeManager m_nodeManager;

            /// <summary>Namespace and telemetry context used by the isolated handler.</summary>
            private readonly ServiceMessageContext m_messageContext;

            /// <summary>Owned caller and issuer certificates, never installed in a persistent store.</summary>
            private readonly List<Certificate> m_ownedCertificates = [];

            /// <summary>Configured local issuer references.</summary>
            private readonly CertificateCollection m_issuers = [];

            /// <summary>Locally available signed CRLs.</summary>
            private readonly X509CRLCollection m_crls = [];
        }
    }
}
