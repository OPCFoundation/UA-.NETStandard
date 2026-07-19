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
using NUnit.Framework;
using Opc.Ua.Lds.Server;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Lds.Tests
{
    [TestFixture]
    [Category("DiscoveryServices")]
    [Parallelizable]
    public sealed class RegistrationValidationTests
    {
        private const string ServerUri = "urn:localhost:opcfoundation.org:RegistrationValidation";

        [Test]
        public void RegistrationAcceptsOneExactCertificateApplicationUri()
        {
            using Certificate certificate = CreateCertificate([ServerUri]);
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel(certificate.RawData),
                CreateRegisteredServer(ServerUri));

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void RegistrationRejectsMissingClientCertificate()
        {
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel(null),
                CreateRegisteredServer(ServerUri));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void RegistrationRejectsMalformedClientCertificate()
        {
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel([1, 2, 3, 4]),
                CreateRegisteredServer(ServerUri));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
        }

        [Test]
        public void RegistrationRejectsCertificateWithoutApplicationUri()
        {
            using Certificate certificate = CreateCertificate([]);
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel(certificate.RawData),
                CreateRegisteredServer(ServerUri));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadServerUriInvalid));
        }

        [Test]
        public void RegistrationRejectsSubjectAltNameWithoutApplicationUri()
        {
            using Certificate certificate = CreateCertificate([], includeSubjectAltName: true);
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel(certificate.RawData),
                CreateRegisteredServer(ServerUri));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadServerUriInvalid));
        }

        [Test]
        public void RegistrationAcceptsExactApplicationUriAmongMultipleUris()
        {
            using Certificate certificate = CreateCertificate(["urn:test:other", ServerUri]);
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel(certificate.RawData),
                CreateRegisteredServer(ServerUri));

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void RegistrationRejectsMultipleApplicationUrisWithoutExactMatch()
        {
            using Certificate certificate = CreateCertificate(
                ["urn:test:other", "urn:test:another"]);
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel(certificate.RawData),
                CreateRegisteredServer(ServerUri));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadServerUriInvalid));
        }

        [Test]
        public void RegistrationRequiresOrdinalApplicationUriMatch()
        {
            using Certificate certificate = CreateCertificate([ServerUri]);
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel(certificate.RawData),
                CreateRegisteredServer(ServerUri.ToUpperInvariant()));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadServerUriInvalid));
        }

        private static Certificate CreateCertificate(
            string[] applicationUris,
            bool includeSubjectAltName = false)
        {
            ICertificateBuilder builder = CertificateBuilder
                .Create("CN=RegistrationValidation")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(30));
            if (applicationUris.Length > 0 || includeSubjectAltName)
            {
                builder = builder.AddExtension(
                    new X509SubjectAltNameExtension(applicationUris, ["localhost"]));
            }

            return builder.SetRSAKeySize(2048).CreateForRSA();
        }

        private static SecureChannelContext CreateChannel(byte[]? clientCertificate)
        {
            return new SecureChannelContext(
                "registration-validation",
                new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt
                },
                RequestEncoding.Binary,
                clientCertificate);
        }

        private static RegisteredServer CreateRegisteredServer(string serverUri)
        {
            return new RegisteredServer
            {
                ServerUri = serverUri,
                ProductUri = "urn:test:product",
                ServerNames = [new LocalizedText("en-US", "Registration Validation")],
                ServerType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://localhost:4840"],
                IsOnline = true
            };
        }

        private sealed class TestLdsServer : LdsServer
        {
            public ServiceResult Validate(
                SecureChannelContext secureChannelContext,
                RegisteredServer registeredServer)
            {
                return ValidateRegistration(secureChannelContext, registeredServer);
            }
        }
    }
}
