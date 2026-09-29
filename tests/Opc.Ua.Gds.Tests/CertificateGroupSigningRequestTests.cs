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
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Tests for the CSR checks of <see cref="CertificateGroup"/>.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [Parallelizable]
    public sealed class CertificateGroupSigningRequestTests
    {
        private const string ApplicationUri = "urn:localhost:test:csr";

        /// <summary>
        /// OPC 10000-12 §7.9.3: the ApplicationUri shall be specified in the CSR.
        /// </summary>
        [Test]
        public void CsrWithoutApplicationUriIsRejected()
        {
            using var certificateGroup = new CertificateGroup(NUnitTelemetryContext.Create());
            var application = new ApplicationRecordDataType { ApplicationUri = ApplicationUri };

            Assert.That(
                async () => await certificateGroup
                    .VerifySigningRequestAsync(application, CreateSigningRequest(null))
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadCertificateUriInvalid));
            Assert.That(
                async () => await certificateGroup
                    .VerifySigningRequestAsync(application, CreateSigningRequest("urn:other"))
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadCertificateUriInvalid));
            Assert.That(
                async () => await certificateGroup
                    .VerifySigningRequestAsync(application, CreateSigningRequest(ApplicationUri))
                    .ConfigureAwait(false),
                Throws.Nothing);
        }

        /// <summary>
        /// OPC 10000-12 §7.9.3: a CSR whose key does not fit the requested
        /// CertificateTypeId is rejected by StartSigningRequest with
        /// Bad_InvalidArgument, not accepted and failed in FinishRequest.
        /// </summary>
        [Test]
        public void RsaCsrForEccCertificateTypeIsRejected()
        {
            using var certificateGroup = new CertificateGroup(NUnitTelemetryContext.Create());
            var application = new ApplicationRecordDataType { ApplicationUri = ApplicationUri };

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await certificateGroup
                    .VerifySigningRequestAsync(
                        application,
                        Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType,
                        CreateSigningRequest(ApplicationUri))
                    .ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(sre.Message, Does.Contain("RSA").And.Contain("ECC"));

            Assert.That(
                async () => await certificateGroup
                    .VerifySigningRequestAsync(
                        application,
                        Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        CreateSigningRequest(ApplicationUri))
                    .ConfigureAwait(false),
                Throws.Nothing);
        }

        [Test]
        public void EccCsrForRsaCertificateTypeIsRejected()
        {
            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    CreateEccSigningRequest(ECCurve.NamedCurves.nistP256)));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(sre.Message, Does.Contain("nistP256").And.Contain("RSA"));
        }

        [Test]
        public void EccCsrWithOtherCurveIsRejected()
        {
            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType,
                    CreateEccSigningRequest(ECCurve.NamedCurves.nistP384)));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(sre.Message, Does.Contain("nistP384").And.Contain("nistP256"));
        }

        /// <summary>
        /// OPC 10000-12 §7.8.4.8 / §7.8.4.9 key sizes; §7.9.3 returns
        /// Bad_NotSupported for an unsupported key size.
        /// </summary>
        [Test]
        public void RsaKeySizeOutsideCertificateTypeRangeIsNotSupported()
        {
            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    CreateSigningRequest(ApplicationUri, 1024)));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(sre.Message, Does.Contain("1024"));

            sre = Assert.Throws<ServiceResultException>(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.RsaMinApplicationCertificateType,
                    CreateSigningRequest(ApplicationUri, 3072)));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(sre.Message, Does.Contain("3072"));
        }

        [Test]
        public void UnsupportedEccCertificateTypeIsNotSupported()
        {
            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.EccCurve25519ApplicationCertificateType,
                    CreateEccSigningRequest(ECCurve.NamedCurves.nistP256)));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public void MatchingCsrKeysAreAccepted()
        {
            ByteString rsa2048 = CreateSigningRequest(ApplicationUri);
            ByteString nistP256 = CreateEccSigningRequest(ECCurve.NamedCurves.nistP256);

            Assert.That(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, rsa2048),
                Throws.Nothing);
            Assert.That(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.RsaMinApplicationCertificateType, rsa2048),
                Throws.Nothing);
            Assert.That(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.ApplicationCertificateType, rsa2048),
                Throws.Nothing);
            Assert.That(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType, nistP256),
                Throws.Nothing);
            Assert.That(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.EccApplicationCertificateType, nistP256),
                Throws.Nothing);
        }

        [Test]
        public void UndecodableCsrIsRejected()
        {
            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => CertificateGroup.VerifySigningRequestKey(
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    ByteString.From([0x30, 0x03, 0x02, 0x01, 0x00])));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        private static ByteString CreateSigningRequest(string? applicationUri, int keySize = 2048)
        {
            using var rsa = RSA.Create(keySize);
            var request = new CertificateRequest(
                "CN=CsrTest",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            AddApplicationUri(request, applicationUri);
            return ByteString.From(request.CreateSigningRequest());
        }

        private static ByteString CreateEccSigningRequest(ECCurve curve)
        {
            using var ecdsa = ECDsa.Create(curve);
            var request = new CertificateRequest("CN=CsrTest", ecdsa, HashAlgorithmName.SHA256);
            AddApplicationUri(request, ApplicationUri);
            return ByteString.From(request.CreateSigningRequest());
        }

        private static void AddApplicationUri(CertificateRequest request, string? applicationUri)
        {
            if (applicationUri != null)
            {
                var altNames = new SubjectAlternativeNameBuilder();
                altNames.AddUri(new Uri(applicationUri));
                altNames.AddDnsName("localhost");
                request.CertificateExtensions.Add(altNames.Build());
            }
        }
    }
}
