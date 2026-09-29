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

        private static ByteString CreateSigningRequest(string? applicationUri)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=CsrTest",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            if (applicationUri != null)
            {
                var altNames = new SubjectAlternativeNameBuilder();
                altNames.AddUri(new Uri(applicationUri));
                altNames.AddDnsName("localhost");
                request.CertificateExtensions.Add(altNames.Build());
            }
            return ByteString.From(request.CreateSigningRequest());
        }
    }
}
