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

#if NET5_0_OR_GREATER
using System;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("CertificateManager")]
    public sealed class EccScalarLifetimeRegressionTests
    {
        [TestCase("success")]
        [TestCase("failure")]
        [TestCase("cancellation")]
        public void ImportedEccScalarIsClearedOnEveryExit(string outcome)
        {
            using var cancellation = new CancellationTokenSource();
            byte[] captured = null;
            ECDsa Import(ECParameters parameters)
            {
                captured = parameters.D;
                Assert.That(captured, Is.Not.Null.And.Length.EqualTo(32));
                Assert.That(captured, Is.Not.All.EqualTo(0));
                if (outcome == "failure")
                {
                    throw new CryptographicException("import failed");
                }
                if (outcome == "cancellation")
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                return ECDsa.Create(parameters);
            }
            var generator = new AdditionalEntropyCertificateKeyGenerator(
                DefaultCertificateFactory.Instance, count => new byte[count], Import);
            var request = new PushCertificateKeyGenerationRequest
            {
                CertificateTypeId = ObjectTypeIds.EccNistP256ApplicationCertificateType,
                ApplicationUri = "urn:regression:ecc-scalar",
                ApplicationName = "Scalar lifetime",
                SubjectName = "CN=Scalar Lifetime",
                DomainNames = ["localhost"],
                NotBefore = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                NotAfter = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AdditionalEntropy = ByteString.From(new byte[32])
            };
            if (outcome == "success")
            {
                using Certificate certificate = generator.CreateApplicationCertificate(request, cancellation.Token);
                using ECDsa key = certificate.GetECDsaPrivateKey();
                using ECDsa publicKey = certificate.GetECDsaPublicKey();
                byte[] data = [1, 2, 3];
                byte[] signature = key.SignData(data, HashAlgorithmName.SHA256);
                Assert.That(publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256), Is.True);
            }
            else if (outcome == "failure")
            {
                CryptographicException error = Assert.Throws<CryptographicException>(() =>
                    generator.CreateApplicationCertificate(request, cancellation.Token));
                Assert.That(error.Message, Is.EqualTo("import failed"));
            }
            else
            {
                Assert.Throws<OperationCanceledException>(() =>
                    generator.CreateApplicationCertificate(request, cancellation.Token));
            }
            Assert.That(captured, Is.Not.Null);
            Assert.That(Array.TrueForAll(captured, value => value == 0), Is.True,
                "The temporary private scalar must be cleared after import.");
        }
    }
}
#endif
