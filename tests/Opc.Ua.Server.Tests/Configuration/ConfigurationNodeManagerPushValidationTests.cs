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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Direct, fast unit tests of the small argument-validation helpers
    /// <see cref="ConfigurationNodeManager"/> extracts for
    /// <c>CreateSelfSignedCertificate</c> (OPC 10000-12 §7.10.6),
    /// <c>GetCertificates</c> (§7.8.3.4) and <c>UpdateCertificate</c>
    /// (§7.10.5), independent of the address-space / method-call plumbing
    /// exercised by <see cref="ConfigurationNodeManagerPushTests"/>.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [Parallelizable]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class ConfigurationNodeManagerPushValidationTests
    {
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();

        private string m_basePath;

        [SetUp]
        public void SetUp()
        {
            m_basePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "cnmv",
                Guid.NewGuid().ToString("N")[..8]);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_basePath))
            {
                Directory.Delete(m_basePath, true);
            }
        }

        [Test]
        public void CreateDefaultApplicationCertificateSubjectNameUsesApplicationName()
        {
            string subjectName = ConfigurationNodeManager
                .CreateDefaultApplicationCertificateSubjectName("MyTestApplication");

            Assert.That(subjectName, Does.Contain("CN=MyTestApplication"));
            Assert.That(subjectName, Does.Contain("O=OPC Foundation"));
        }

        [Test]
        public void CreateDefaultApplicationCertificateSubjectNameFallsBackWhenApplicationNameIsEmpty()
        {
            Assert.That(
                () => ConfigurationNodeManager.CreateDefaultApplicationCertificateSubjectName(null),
                Throws.Nothing);
            string subjectName = ConfigurationNodeManager
                .CreateDefaultApplicationCertificateSubjectName(string.Empty);

            Assert.That(subjectName, Does.Contain("CN="));
        }

        [Test]
        public void CreateDefaultApplicationCertificateSubjectNameSanitizesSeparatorCharacters()
        {
            string subjectName = ConfigurationNodeManager
                .CreateDefaultApplicationCertificateSubjectName("My/App,Name;Here");

            // The sanitized value must still parse as exactly two RDN
            // fields (CN and O); embedded field separators would
            // otherwise be misinterpreted as additional RDN boundaries.
            List<string> fields = X509Utils.ParseDistinguishedName(subjectName);
            Assert.That(fields, Has.Count.EqualTo(2));
            Assert.That(fields[0], Does.StartWith("CN="));
            Assert.That(fields[0], Does.Not.Contain("/"));
            Assert.That(fields[0], Does.Not.Contain(";"));
        }

        [Test]
        public void SubjectCommonNameMatchesDomainReturnsTrueForExactMatch()
        {
            bool matches = ConfigurationNodeManager.SubjectCommonNameMatchesDomain(
                "CN=my.host.name, O=OPC Foundation",
                ["other.host", "my.host.name"]);

            Assert.That(matches, Is.True);
        }

        [Test]
        public void SubjectCommonNameMatchesDomainIsCaseInsensitive()
        {
            bool matches = ConfigurationNodeManager.SubjectCommonNameMatchesDomain(
                "CN=MY.HOST.NAME",
                ["my.host.name"]);

            Assert.That(matches, Is.True);
        }

        [Test]
        public void SubjectCommonNameMatchesDomainReturnsFalseWhenNoMatch()
        {
            bool matches = ConfigurationNodeManager.SubjectCommonNameMatchesDomain(
                "CN=unrelated.host",
                ["my.host.name", "127.0.0.1"]);

            Assert.That(matches, Is.False);
        }

        [Test]
        public void SubjectCommonNameMatchesDomainReturnsFalseWhenSubjectHasNoCommonName()
        {
            bool matches = ConfigurationNodeManager.SubjectCommonNameMatchesDomain(
                "O=OPC Foundation",
                ["my.host.name"]);

            Assert.That(matches, Is.False);
        }

        [TestCase((ushort)0, true)]
        [TestCase((ushort)1024, true)]
        [TestCase((ushort)2048, true)]
        [TestCase((ushort)3072, true)]
        [TestCase((ushort)4096, true)]
        [TestCase((ushort)1536, false)]
        public void ValidateKeySizeForGenericApplicationCertificateType(ushort keySize, bool supported)
        {
            Action action = () => ConfigurationNodeManager.ValidateKeySizeForCertificateType(
                ObjectTypeIds.ApplicationCertificateType,
                isRsaCertificateType: true,
                keySize);

            AssertSupportedOrBadOutOfRange(action, supported);
        }

        [TestCase((ushort)1024, true)]
        [TestCase((ushort)2048, true)]
        [TestCase((ushort)3072, false)]
        [TestCase((ushort)4096, false)]
        public void ValidateKeySizeForRsaMinApplicationCertificateType(ushort keySize, bool supported)
        {
            Action action = () => ConfigurationNodeManager.ValidateKeySizeForCertificateType(
                ObjectTypeIds.RsaMinApplicationCertificateType,
                isRsaCertificateType: true,
                keySize);

            AssertSupportedOrBadOutOfRange(action, supported);
        }

        [TestCase((ushort)1024, false)]
        [TestCase((ushort)2048, true)]
        [TestCase((ushort)3072, true)]
        [TestCase((ushort)4096, true)]
        public void ValidateKeySizeForRsaSha256ApplicationCertificateType(ushort keySize, bool supported)
        {
            Action action = () => ConfigurationNodeManager.ValidateKeySizeForCertificateType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                isRsaCertificateType: true,
                keySize);

            AssertSupportedOrBadOutOfRange(action, supported);
        }

        [TestCase((ushort)256, true)]
        [TestCase((ushort)384, false)]
        public void ValidateKeySizeForEccNistP256ApplicationCertificateType(ushort keySize, bool supported)
        {
            Action action = () => ConfigurationNodeManager.ValidateKeySizeForCertificateType(
                ObjectTypeIds.EccNistP256ApplicationCertificateType,
                isRsaCertificateType: false,
                keySize);

            AssertSupportedOrBadOutOfRange(action, supported);
        }

        [TestCase((ushort)384, true)]
        [TestCase((ushort)256, false)]
        public void ValidateKeySizeForEccNistP384ApplicationCertificateType(ushort keySize, bool supported)
        {
            Action action = () => ConfigurationNodeManager.ValidateKeySizeForCertificateType(
                ObjectTypeIds.EccNistP384ApplicationCertificateType,
                isRsaCertificateType: false,
                keySize);

            AssertSupportedOrBadOutOfRange(action, supported);
        }

        private static void AssertSupportedOrBadOutOfRange(Action action, bool supported)
        {
            if (supported)
            {
                Assert.DoesNotThrow(action);
                return;
            }

            ServiceResultException exception = Assert.Throws<ServiceResultException>(action);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void SelectOccupiedCertificateSlotsOmitsEmptySlotsAndPreservesOrder()
        {
            NodeId rsaType = ObjectTypeIds.RsaSha256ApplicationCertificateType;
            NodeId eccType = ObjectTypeIds.EccNistP256ApplicationCertificateType;
            NodeId httpsType = ObjectTypeIds.HttpsCertificateType;

            ArrayOf<CertificateIdentifier> applicationCertificates =
            [
                new CertificateIdentifier { CertificateType = rsaType },
                new CertificateIdentifier { CertificateType = eccType },
                new CertificateIdentifier { CertificateType = httpsType }
            ];

            using Certificate rsaCert = CertificateBuilder
                .Create("CN=Occupied Slot " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate httpsCert = CertificateBuilder
                .Create("CN=Occupied Slot Https " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using var emptyIssuerChain = new CertificateCollection();

            using var rsaEntry = new CertificateEntry(rsaCert, emptyIssuerChain, rsaType);
            using var httpsEntry = new CertificateEntry(httpsCert, emptyIssuerChain, httpsType);

            (ArrayOf<NodeId> types, ArrayOf<ByteString> certificates) = ConfigurationNodeManager
                .SelectOccupiedCertificateSlots(
                    applicationCertificates,
                    certificateType => certificateType == rsaType
                        ? rsaEntry.AddRef()
                        : certificateType == httpsType
                            ? httpsEntry.AddRef()
                            : null);

            // eccType is unoccupied and must be omitted; rsaType/httpsType
            // must be reported in their original configured order.
            Assert.That(types.ToList(), Is.EqualTo(new[] { rsaType, httpsType }));
            Assert.That(certificates.Count, Is.EqualTo(2));
            using Certificate roundTrippedRsa = Certificate.FromRawData(certificates[0]);
            Assert.That(roundTrippedRsa.Thumbprint, Is.EqualTo(rsaCert.Thumbprint));
        }

        [Test]
        public void SelectOccupiedCertificateSlotsReturnsEmptyArraysForFullyEmptyGroup()
        {
            ArrayOf<CertificateIdentifier> applicationCertificates =
            [
                new CertificateIdentifier { CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType },
                new CertificateIdentifier { CertificateType = ObjectTypeIds.EccNistP256ApplicationCertificateType }
            ];

            (ArrayOf<NodeId> types, ArrayOf<ByteString> certificates) = ConfigurationNodeManager
                .SelectOccupiedCertificateSlots(applicationCertificates, _ => null);

            Assert.That(types.Count, Is.Zero);
            Assert.That(certificates.Count, Is.Zero);
        }

        [Test]
        public void SelectOccupiedCertificateSlotsThrowsForNullResolver()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ConfigurationNodeManager.SelectOccupiedCertificateSlots(
                    ArrayOf<CertificateIdentifier>.Empty,
                    null));
        }

        [Test]
        public async Task ValidateCertificateAgainstGroupTrustListAsyncAcceptsACertificateDirectlyTrustedAsync()
        {
            (CertificateStoreIdentifier trustedStore, CertificateStoreIdentifier issuerStore) = CreateEmptyStores();
            using Certificate selfSigned = CertificateBuilder
                .Create("CN=Directly Trusted " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using (ICertificateStore store = trustedStore.OpenStore(s_telemetry))
            {
                await store.AddAsync(selfSigned, ct: CancellationToken.None).ConfigureAwait(false);
            }

            Assert.DoesNotThrowAsync(async () =>
                await ConfigurationNodeManager.ValidateCertificateAgainstGroupTrustListAsync(
                        trustedStore,
                        issuerStore,
                        "TestGroup-" + Guid.NewGuid().ToString("N")[..8],
                        selfSigned,
                        new SecurityConfiguration(),
                        s_telemetry,
                        CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task ValidateCertificateAgainstGroupTrustListAsyncAcceptsAnUntrustedSelfSignedCertificateAsync()
        {
            // OPC 10000-12 §7.10.5: "All suppressible errors shall be
            // ignored." BadCertificateUntrusted is suppressible, so a
            // self-signed certificate absent from every store must still
            // be accepted rather than rejected.
            (CertificateStoreIdentifier trustedStore, CertificateStoreIdentifier issuerStore) = CreateEmptyStores();
            using Certificate untrusted = CertificateBuilder
                .Create("CN=Never Trusted " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();

            Assert.DoesNotThrowAsync(async () =>
                await ConfigurationNodeManager.ValidateCertificateAgainstGroupTrustListAsync(
                        trustedStore,
                        issuerStore,
                        "TestGroup-" + Guid.NewGuid().ToString("N")[..8],
                        untrusted,
                        new SecurityConfiguration(),
                        s_telemetry,
                        CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public void ValidateCertificateAgainstGroupTrustListAsyncThrowsForNullTrustedStore()
        {
            using Certificate certificate = CertificateBuilder
                .Create("CN=Guard Clause Test")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await ConfigurationNodeManager.ValidateCertificateAgainstGroupTrustListAsync(
                        null,
                        null,
                        "TestGroup",
                        certificate,
                        new SecurityConfiguration(),
                        s_telemetry,
                        CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public void IsCertificateReferencedByEndpointMatchesTheEndpointServerCertificateThumbprint()
        {
            using Certificate referenced = CertificateBuilder
                .Create("CN=Referenced " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate other = CertificateBuilder
                .Create("CN=Other " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();

            ArrayOf<EndpointDescription> endpoints = ArrayOf.Wrapped(new[]
            {
                new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None
                },
                new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    ServerCertificate = referenced.RawData.ToByteString()
                }
            });

            Assert.That(
                ConfigurationNodeManager.IsCertificateReferencedByEndpoint(
                    referenced.Thumbprint, endpoints, null, s_telemetry),
                Is.True,
                "a certificate presented by an endpoint must be reported as referenced");
            Assert.That(
                ConfigurationNodeManager.IsCertificateReferencedByEndpoint(
                    other.Thumbprint, endpoints, null, s_telemetry),
                Is.False,
                "a certificate not presented by any endpoint must not be reported as referenced");
        }

        [Test]
        public void IsCertificateReferencedByEndpointReturnsFalseForAnEmptyEndpointSet()
        {
            using Certificate certificate = CertificateBuilder
                .Create("CN=Unreferenced " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();

            Assert.That(
                ConfigurationNodeManager.IsCertificateReferencedByEndpoint(
                    certificate.Thumbprint, default, null, s_telemetry),
                Is.False);
        }

        [Test]
        public void IsCertificateReferencedByEndpointResolvesTheCurrentCertificateFromTheRegistry()
        {
            // Regression for the endpoint-reference check using startup-only
            // EndpointDescription.ServerCertificate data (OPC 10000-12 §7.10.7):
            // after a rotation the endpoint's cached ServerCertificate blob is
            // the previous certificate (A) while the live registry maps the
            // endpoint's SecurityPolicyUri to the rotated certificate (B). The
            // check must protect B (the certificate presented right now) and
            // must NOT protect A (no longer presented), resolving the
            // assignment from the registry rather than the stale blob.
            using Certificate previous = CertificateBuilder
                .Create("CN=Rotated Out " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate rotated = CertificateBuilder
                .Create("CN=Currently Presented " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // The endpoint still advertises the stale previous-certificate blob.
            ArrayOf<EndpointDescription> endpoints = ArrayOf.Wrapped(new[]
            {
                new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    ServerCertificate = previous.RawData.ToByteString()
                }
            });

            using var emptyIssuerChain = new CertificateCollection();
            using var liveEntry = new CertificateEntry(
                rotated, emptyIssuerChain, ObjectTypeIds.RsaSha256ApplicationCertificateType);

            var registry = new Mock<ICertificateRegistry>();
            registry
                .Setup(r => r.AcquireApplicationCertificateBySecurityPolicy(It.IsAny<string>()))
                .Returns(() => liveEntry.AddRef());

            Assert.That(
                ConfigurationNodeManager.IsCertificateReferencedByEndpoint(
                    rotated.Thumbprint, endpoints, registry.Object, s_telemetry),
                Is.True,
                "the certificate currently presented (resolved from the registry) must be protected");
            Assert.That(
                ConfigurationNodeManager.IsCertificateReferencedByEndpoint(
                    previous.Thumbprint, endpoints, registry.Object, s_telemetry),
                Is.False,
                "the stale endpoint blob must not keep a rotated-out certificate referenced");
        }

        [Test]
        public void IsCertificateReferencedByEndpointIgnoresUnencryptedEndpointsWhenRegistryPresent()
        {
            // An endpoint that does not require encryption presents no channel
            // certificate, so it must never keep a certificate referenced when
            // the live registry is consulted.
            using Certificate certificate = CertificateBuilder
                .Create("CN=Unencrypted " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();

            ArrayOf<EndpointDescription> endpoints = ArrayOf.Wrapped(new[]
            {
                new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    ServerCertificate = certificate.RawData.ToByteString()
                }
            });

            using var emptyIssuerChain = new CertificateCollection();
            using var liveEntry = new CertificateEntry(
                certificate, emptyIssuerChain, ObjectTypeIds.RsaSha256ApplicationCertificateType);

            var registry = new Mock<ICertificateRegistry>();
            registry
                .Setup(r => r.AcquireApplicationCertificateBySecurityPolicy(It.IsAny<string>()))
                .Returns(() => liveEntry.AddRef());

            Assert.That(
                ConfigurationNodeManager.IsCertificateReferencedByEndpoint(
                    certificate.Thumbprint, endpoints, registry.Object, s_telemetry),
                Is.False);
        }

        private (CertificateStoreIdentifier TrustedStore, CertificateStoreIdentifier IssuerStore) CreateEmptyStores()
        {
            return (
                new CertificateStoreIdentifier(Path.Combine(m_basePath, "trusted")),
                new CertificateStoreIdentifier(Path.Combine(m_basePath, "issuer")));
        }
    }
}
