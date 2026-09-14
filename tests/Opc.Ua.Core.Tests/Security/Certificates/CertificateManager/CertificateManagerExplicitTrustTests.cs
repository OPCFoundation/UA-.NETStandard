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
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// End-to-end validation of explicitly configured trust and issuer entries.
    /// </summary>
    [TestFixture]
    [Category("CertificateManager")]
    [NonParallelizable]
    public sealed class CertificateManagerExplicitTrustTests
    {
        /// <summary>
        /// Creates same-subject peers and a root, intermediate, and leaf chain for explicit trust scenarios.
        /// </summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_trusted = CreateCertificate("CN=Explicit Trust Peer");
            m_untrusted = CreateCertificate("CN=Explicit Trust Peer");
            m_root = CertificateBuilder
                .Create("CN=Explicit Trust Root")
                .SetNotBefore(s_validFrom)
                .SetNotAfter(s_validTo)
                .SetCAConstraint(-1)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            m_intermediate = CertificateBuilder
                .Create("CN=Explicit Trust Intermediate")
                .SetNotBefore(s_validFrom)
                .SetNotAfter(s_validTo)
                .SetCAConstraint(0)
                .SetIssuer(m_root)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            m_leaf = CertificateBuilder
                .Create("CN=Explicit Trust Issued Peer")
                .SetNotBefore(s_validFrom)
                .SetNotAfter(s_validTo)
                .SetIssuer(m_intermediate)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// Releases the shared peer certificates, issuer chain, and fixture telemetry.
        /// </summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_trusted.Dispose();
            m_untrusted.Dispose();
            m_leaf.Dispose();
            m_intermediate.Dispose();
            m_root.Dispose();
            (m_telemetry as IDisposable)?.Dispose();
        }

        /// <summary>
        /// Verifies explicit peer, user, and HTTPS trust works without a store path and does not trust same-subject
        /// peers.
        /// </summary>
        [TestCase("Peers", false)]
        [TestCase("Peers", true)]
        [TestCase("Users", false)]
        [TestCase("Https", false)]
        public async Task ExplicitConfiguredTrustIsHonoredWithoutStorePathAsync(string scope, bool useAddTrustedPeer)
        {
            SecurityConfiguration configuration = CreateConfiguration();
            if (useAddTrustedPeer)
            {
                configuration.AddTrustedPeer(m_trusted.RawData);
            }
            else
            {
                GetTrustedList(configuration, scope).TrustedCertificates =
                    [new CertificateIdentifier { RawData = m_trusted.RawData }];
            }
            await using var manager = new CertificateManager(m_telemetry);
            manager.MapFromSecurityConfiguration(configuration);

            CertificateValidationResult trusted = await manager.ValidateAsync(m_trusted, GetScope(scope))
                .ConfigureAwait(false);
            Assert.That(trusted.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(trusted.IsValid, Is.True);

            CertificateValidationResult untrusted = await manager.ValidateAsync(m_untrusted, GetScope(scope))
                .ConfigureAwait(false);
            Assert.That(untrusted.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
            Assert.That(untrusted.IsValid, Is.False);
        }

        /// <summary>
        /// Verifies explicit trusted roots and intermediate issuers validate a leaf without filesystem trust stores.
        /// </summary>
        [TestCase("Peers", false)]
        [TestCase("Peers", true)]
        [TestCase("Users", false)]
        [TestCase("Users", true)]
        [TestCase("Https", false)]
        [TestCase("Https", true)]
        public async Task ExplicitIssuerChainIsHonoredWithoutStorePathAsync(string scope, bool strict)
        {
            SecurityConfiguration configuration = CreateConfiguration();
            configuration.RejectUnknownRevocationStatus = strict;
            GetTrustedList(configuration, scope).TrustedCertificates =
                [new CertificateIdentifier { RawData = m_root.RawData }];
            GetIssuerList(configuration, scope).TrustedCertificates =
                [new CertificateIdentifier { RawData = m_intermediate.RawData }];
            await using var manager = new CertificateManager(m_telemetry);
            manager.MapFromSecurityConfiguration(configuration);

            CertificateValidationResult result = await manager.ValidateAsync(m_leaf, GetScope(scope))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.IsValid, Is.True);
        }

        /// <summary>
        /// Verifies explicit issuers complete the chain of a separately trusted leaf certificate.
        /// </summary>
        [Test]
        public async Task ExplicitIssuerChainCompletesTrustedLeafAsync()
        {
            SecurityConfiguration configuration = CreateConfiguration();
            configuration.AddTrustedPeer(m_leaf.RawData);
            configuration.TrustedIssuerCertificates.TrustedCertificates =
            [
                new CertificateIdentifier { RawData = m_root.RawData },
                new CertificateIdentifier { RawData = m_intermediate.RawData }
            ];
            await using var manager = new CertificateManager(m_telemetry);
            manager.MapFromSecurityConfiguration(configuration);

            CertificateValidationResult result = await manager.ValidateAsync(m_leaf, TrustListIdentifier.Peers)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.IsValid, Is.True);
        }

        /// <summary>
        /// Verifies issuer-only configuration supplies chain material without establishing a trust anchor.
        /// </summary>
        [TestCase("Peers")]
        [TestCase("Users")]
        [TestCase("Https")]
        public async Task ExplicitIssuerChainAloneDoesNotEstablishTrustAsync(string scope)
        {
            SecurityConfiguration configuration = CreateConfiguration();
            GetIssuerList(configuration, scope).TrustedCertificates =
            [
                new CertificateIdentifier { RawData = m_root.RawData },
                new CertificateIdentifier { RawData = m_intermediate.RawData }
            ];
            await using var manager = new CertificateManager(m_telemetry);
            manager.MapFromSecurityConfiguration(configuration);

            CertificateValidationResult result = await manager.ValidateAsync(m_leaf, GetScope(scope))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted),
                "The explicit issuers must complete the chain without making its leaf trusted.");
            Assert.That(result.IsValid, Is.False);
        }

        /// <summary>
        /// Verifies a caller-supplied complete chain remains untrusted when no configured trust source accepts it.
        /// </summary>
        [Test]
        public async Task ProvidedChainAloneDoesNotEstablishTrustAsync()
        {
            await using var manager = new CertificateManager(m_telemetry);
            manager.MapFromSecurityConfiguration(CreateConfiguration());
            using var chain = new CertificateCollection { m_leaf, m_intermediate, m_root };

            CertificateValidationResult result = await manager.ValidateAsync(chain, TrustListIdentifier.Peers)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
            Assert.That(result.IsValid, Is.False);
        }

        /// <summary>
        /// Verifies explicit and stored peer certificates are both trusted while unrelated certificates remain
        /// rejected.
        /// </summary>
        [Test]
        public async Task ExplicitAndStoreTrustSourcesAreCombinedAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), "opcua-explicit-trust-" + Guid.NewGuid().ToString("N"));
            try
            {
                SecurityConfiguration configuration = CreateConfiguration();
                configuration.TrustedPeerCertificates.StorePath = path;
                configuration.TrustedPeerCertificates.StoreType = CertificateStoreType.Directory;
                configuration.AddTrustedPeer(m_trusted.RawData);
                using (ICertificateStore store = configuration.TrustedPeerCertificates.OpenStore(m_telemetry))
                {
                    await store.AddAsync(m_untrusted, null).ConfigureAwait(false);
                }

                await using var manager = new CertificateManager(m_telemetry);
                manager.MapFromSecurityConfiguration(configuration);
                CertificateValidationResult explicitResult =
                    await manager.ValidateAsync(m_trusted, TrustListIdentifier.Peers).ConfigureAwait(false);
                CertificateValidationResult storeResult =
                    await manager.ValidateAsync(m_untrusted, TrustListIdentifier.Peers).ConfigureAwait(false);
                Assert.That(explicitResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(storeResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                CertificateValidationResult unrelated =
                    await manager.ValidateAsync(m_root, TrustListIdentifier.Peers).ConfigureAwait(false);
                Assert.That(unrelated.IsValid, Is.False);
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies mutating configured trust entries affects validation only after an explicit manager update.
        /// </summary>
        [Test]
        public async Task TrustConfigurationIsSnapshottedUntilAnExplicitUpdateAsync()
        {
            SecurityConfiguration configuration = CreateConfiguration();
            var identifier = new CertificateIdentifier { RawData = m_trusted.RawData };
            configuration.TrustedPeerCertificates.TrustedCertificates = [identifier];
            await using var manager = new CertificateManager(m_telemetry);
            manager.MapFromSecurityConfiguration(configuration);
            identifier.RawData = m_untrusted.RawData;
            CertificateValidationResult retained =
                await manager.ValidateAsync(m_trusted, TrustListIdentifier.Peers).ConfigureAwait(false);
            CertificateValidationResult notYetConfigured =
                await manager.ValidateAsync(m_untrusted, TrustListIdentifier.Peers).ConfigureAwait(false);
            Assert.That(retained.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(notYetConfigured.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));

            await manager.UpdateAsync(configuration).ConfigureAwait(false);
            CertificateValidationResult removed =
                await manager.ValidateAsync(m_trusted, TrustListIdentifier.Peers).ConfigureAwait(false);
            CertificateValidationResult replacement =
                await manager.ValidateAsync(m_untrusted, TrustListIdentifier.Peers).ConfigureAwait(false);
            Assert.That(removed.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
            Assert.That(replacement.StatusCode, Is.EqualTo(StatusCodes.Good));

            configuration.TrustedPeerCertificates.TrustedCertificates = default;
            await manager.UpdateAsync(configuration).ConfigureAwait(false);
            CertificateValidationResult emptied =
                await manager.ValidateAsync(m_untrusted, TrustListIdentifier.Peers).ConfigureAwait(false);
            Assert.That(emptied.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
        }

        /// <summary>
        /// Verifies configured issuer mutations do not break a cached chain until the manager refreshes its snapshot.
        /// </summary>
        [Test]
        public async Task IssuerConfigurationIsSnapshottedUntilAnExplicitUpdateAsync()
        {
            SecurityConfiguration configuration = CreateConfiguration();
            configuration.AddTrustedPeer(m_root.RawData);
            var issuer = new CertificateIdentifier { RawData = m_intermediate.RawData };
            configuration.TrustedIssuerCertificates.TrustedCertificates = [issuer];
            await using var manager = new CertificateManager(m_telemetry);
            manager.MapFromSecurityConfiguration(configuration);
            issuer.RawData = m_untrusted.RawData;

            CertificateValidationResult retained =
                await manager.ValidateAsync(m_leaf, TrustListIdentifier.Peers).ConfigureAwait(false);
            Assert.That(retained.StatusCode, Is.EqualTo(StatusCodes.Good));
            await manager.UpdateAsync(configuration).ConfigureAwait(false);
            CertificateValidationResult incomplete =
                await manager.ValidateAsync(m_leaf, TrustListIdentifier.Peers).ConfigureAwait(false);
            Assert.That(incomplete.StatusCode, Is.EqualTo(StatusCodes.BadCertificateChainIncomplete));
        }

        /// <summary>
        /// Verifies explicitly listed issuers still apply revocation lists from their configured store.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitIssuerDoesNotBypassConfiguredRevocationAsync(bool trustedIssuer)
        {
            string path = Path.Combine(Path.GetTempPath(), "opcua-explicit-crl-" + Guid.NewGuid().ToString("N"));
            try
            {
                SecurityConfiguration configuration = CreateConfiguration();
                configuration.AddTrustedPeer(m_root.RawData);
                CertificateTrustList issuerList = trustedIssuer
                    ? configuration.TrustedPeerCertificates
                    : configuration.TrustedIssuerCertificates;
                issuerList.StorePath = path;
                issuerList.StoreType = CertificateStoreType.Directory;
                issuerList.TrustedCertificates += new CertificateIdentifier { RawData = m_intermediate.RawData };
                var crl = new X509CRL(CrlBuilder.Create(m_intermediate.SubjectName)
                    .AddRevokedCertificate(m_leaf).CreateForRSA(m_intermediate));
                using (ICertificateStore store = issuerList.OpenStore(m_telemetry))
                {
                    await store.AddAsync(m_intermediate, null).ConfigureAwait(false);
                    await store.AddCRLAsync(crl).ConfigureAwait(false);
                }
                await using var manager = new CertificateManager(m_telemetry);
                manager.MapFromSecurityConfiguration(configuration);

                CertificateValidationResult result =
                    await manager.ValidateAsync(m_leaf, TrustListIdentifier.Peers).ConfigureAwait(false);
                Assert.That(result.IsValid, Is.False);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateRevoked));
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies an inline issuer with a store obeys the configured policy for unknown revocation status.
        /// </summary>
        [Test]
        public async Task InlineIssuerWithStoreHonorsStrictUnknownRevocationPolicyAsync(
            [Values(false, true)] bool trustedIssuer,
            [Values(false, true)] bool strict)
        {
            string path = Path.Combine(Path.GetTempPath(), "opcua-inline-unknown-" + Guid.NewGuid().ToString("N"));
            try
            {
                using Certificate leaf = CreateInlineIssuerLeaf();
                SecurityConfiguration configuration = CreateConfiguration();
                configuration.RejectUnknownRevocationStatus = strict;
                configuration.AddTrustedPeer(leaf.RawData);
                CertificateTrustList issuerList = trustedIssuer
                    ? configuration.TrustedPeerCertificates
                    : configuration.TrustedIssuerCertificates;
                issuerList.StorePath = path;
                issuerList.TrustedCertificates += new CertificateIdentifier { RawData = m_root.RawData };
                await using var manager = new CertificateManager(m_telemetry);
                manager.MapFromSecurityConfiguration(configuration);

                CertificateValidationResult result = await manager.ValidateAsync(leaf, TrustListIdentifier.Peers)
                    .ConfigureAwait(false);

                Assert.That(result.StatusCode,
                    Is.EqualTo(strict ? StatusCodes.BadCertificateRevocationUnknown : StatusCodes.Good));
                Assert.That(result.IsValid, Is.EqualTo(!strict));
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies a known revoked leaf is rejected even when its inline issuer suppresses unknown revocation status.
        /// </summary>
        [Test]
        public async Task InlineIssuerRevocationIsNeverSuppressedAsync(
            [Values(false, true)] bool trustedIssuer,
            [Values(false, true)] bool strict)
        {
            string path = Path.Combine(Path.GetTempPath(), "opcua-inline-revoked-" + Guid.NewGuid().ToString("N"));
            try
            {
                using Certificate leaf = CreateInlineIssuerLeaf();
                SecurityConfiguration configuration = CreateConfiguration();
                configuration.RejectUnknownRevocationStatus = strict;
                configuration.AddTrustedPeer(leaf.RawData);
                CertificateTrustList issuerList = trustedIssuer
                    ? configuration.TrustedPeerCertificates
                    : configuration.TrustedIssuerCertificates;
                issuerList.StorePath = path;
                issuerList.TrustedCertificates += new CertificateIdentifier
                {
                    RawData = m_root.RawData,
                    ValidationOptions = CertificateValidationOptions.SuppressRevocationStatusUnknown
                };
                var crl = new X509CRL(CrlBuilder.Create(m_root.SubjectName)
                    .AddRevokedCertificate(leaf).CreateForRSA(m_root));
                using (ICertificateStore store = issuerList.OpenStore(m_telemetry))
                {
                    await store.AddAsync(m_root, null).ConfigureAwait(false);
                    await store.AddCRLAsync(crl).ConfigureAwait(false);
                    Assert.That(await store.DeleteAsync(m_root.Thumbprint).ConfigureAwait(false), Is.True);
                    using CertificateCollection certificates = await store.EnumerateAsync().ConfigureAwait(false);
                    Assert.That(certificates, Is.Empty, "The issuer must be resolved from the inline entry.");
                }
                await using var manager = new CertificateManager(m_telemetry);
                manager.MapFromSecurityConfiguration(configuration);

                CertificateValidationResult result = await manager.ValidateAsync(leaf, TrustListIdentifier.Peers)
                    .ConfigureAwait(false);

                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateRevoked));
                Assert.That(result.IsValid, Is.False);
                Assert.That(result.IsSuppressible, Is.False);
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies a store-resolved issuer's strict revocation policy overrides suppression on a duplicate inline
        /// entry.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StoreIssuerPolicyTakesPrecedenceOverInlineUnknownSuppressionAsync(bool trustedIssuer)
        {
            string path = Path.Combine(Path.GetTempPath(), "opcua-store-first-" + Guid.NewGuid().ToString("N"));
            try
            {
                using Certificate leaf = CreateInlineIssuerLeaf();
                SecurityConfiguration configuration = CreateConfiguration();
                configuration.RejectUnknownRevocationStatus = true;
                configuration.AddTrustedPeer(leaf.RawData);
                CertificateTrustList issuerList = trustedIssuer
                    ? configuration.TrustedPeerCertificates
                    : configuration.TrustedIssuerCertificates;
                issuerList.StorePath = path;
                issuerList.TrustedCertificates += new CertificateIdentifier
                {
                    RawData = m_root.RawData,
                    ValidationOptions = CertificateValidationOptions.SuppressRevocationStatusUnknown
                };
                using (ICertificateStore store = issuerList.OpenStore(m_telemetry))
                {
                    await store.AddAsync(m_root).ConfigureAwait(false);
                }
                await using var manager = new CertificateManager(m_telemetry);
                manager.MapFromSecurityConfiguration(configuration);

                CertificateValidationResult result = await manager.ValidateAsync(leaf, TrustListIdentifier.Peers)
                    .ConfigureAwait(false);

                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateRevocationUnknown));
                Assert.That(result.IsValid, Is.False);
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        /// <summary>
        /// Issues a leaf directly from the shared root for inline-issuer revocation scenarios.
        /// </summary>
        private Certificate CreateInlineIssuerLeaf()
        {
            return CertificateBuilder.Create("CN=Inline Issuer Revocation Peer")
                .SetNotBefore(s_validFrom)
                .SetNotAfter(s_validTo)
                .SetIssuer(m_root)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// Creates empty trust scopes with automatic acceptance disabled and a 2048-bit certificate minimum.
        /// </summary>
        private static SecurityConfiguration CreateConfiguration()
        {
            return new SecurityConfiguration
            {
                AutoAcceptUntrustedCertificates = false,
                RejectUnknownRevocationStatus = false,
                MinimumCertificateKeySize = 2048,
                TrustedPeerCertificates = new CertificateTrustList(),
                TrustedIssuerCertificates = new CertificateTrustList(),
                TrustedUserCertificates = new CertificateTrustList(),
                UserIssuerCertificates = new CertificateTrustList(),
                TrustedHttpsCertificates = new CertificateTrustList(),
                HttpsIssuerCertificates = new CertificateTrustList()
            };
        }

        /// <summary>
        /// Selects the configured trusted-certificate list for a peer, user, or HTTPS test scope.
        /// </summary>
        private static CertificateTrustList GetTrustedList(SecurityConfiguration configuration, string scope)
        {
            return scope switch
            {
                "Peers" => configuration.TrustedPeerCertificates,
                "Users" => configuration.TrustedUserCertificates,
                "Https" => configuration.TrustedHttpsCertificates,
                _ => throw new ArgumentOutOfRangeException(nameof(scope))
            };
        }

        /// <summary>
        /// Selects the configured issuer-certificate list corresponding to the validation scope.
        /// </summary>
        private static CertificateTrustList GetIssuerList(SecurityConfiguration configuration, string scope)
        {
            return scope switch
            {
                "Peers" => configuration.TrustedIssuerCertificates,
                "Users" => configuration.UserIssuerCertificates,
                "Https" => configuration.HttpsIssuerCertificates,
                _ => throw new ArgumentOutOfRangeException(nameof(scope))
            };
        }

        /// <summary>
        /// Maps the parameterized scope name to the manager's trust-list identifier.
        /// </summary>
        private static TrustListIdentifier GetScope(string scope)
        {
            return scope switch
            {
                "Peers" => TrustListIdentifier.Peers,
                "Users" => TrustListIdentifier.Users,
                "Https" => TrustListIdentifier.Https,
                _ => throw new ArgumentOutOfRangeException(nameof(scope))
            };
        }

        /// <summary>
        /// Creates a self-signed RSA peer with the shared validity interval and requested subject.
        /// </summary>
        private static Certificate CreateCertificate(string subject)
        {
            return CertificateBuilder
                .Create(subject)
                .SetNotBefore(s_validFrom)
                .SetNotAfter(s_validTo)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// Starts all fixture certificates before the trust-policy checks.
        /// </summary>
        private static readonly DateTime s_validFrom = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Keeps certificate expiry separate from the trust and revocation assertions.
        /// </summary>
        private static readonly DateTime s_validTo = new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Supplies logging for the fixture's certificate managers and stores.
        /// </summary>
        private ITelemetryContext m_telemetry;

        /// <summary>
        /// Holds the peer explicitly accepted by the initial trust configuration.
        /// </summary>
        private Certificate m_trusted;

        /// <summary>
        /// Holds a distinct same-subject peer used to detect unintended trust by subject name.
        /// </summary>
        private Certificate m_untrusted;

        /// <summary>
        /// Holds the root trust anchor and signer for direct-leaf revocation scenarios.
        /// </summary>
        private Certificate m_root;

        /// <summary>
        /// Holds the intermediate issuer used to exercise explicit chain completion.
        /// </summary>
        private Certificate m_intermediate;

        /// <summary>
        /// Holds the leaf whose chain requires the shared intermediate and root.
        /// </summary>
        private Certificate m_leaf;
    }
}
