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

using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Stack.Types
{
    [TestFixture]
    [Category("X509IdentityTokenHandler")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class X509IdentityTokenHandlerTests
    {
        /// <summary>
        /// Verifies the <see cref="X509IdentityTokenHandler(CertificateIdentifier,
        /// ICertificatePasswordProvider, ICertificateProvider)"/> ctor:
        /// the handler is a POCO (no live cert reference) and
        /// <see cref="X509IdentityTokenHandler.SignAsync"/> resolves
        /// the private-key cert via <see cref="ICertificateProvider"/>
        /// on each call.
        /// </summary>
        [Test]
        public async Task CertificateIdentifierCtorResolvesViaProviderForSignAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string storePath = Path.Combine(
                Path.GetTempPath(),
                "opcua-x509handler-id-" + System.Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(storePath);
            try
            {
                using Certificate cert = CertificateBuilder
                    .Create("CN=X509HandlerCertIdentifier, O=OPC Foundation")
                    .SetRSAKeySize(2048)
                    .CreateForRSA();

                await cert.AddToStoreAsync(
                    CertificateStoreType.Directory,
                    storePath,
                    password: null,
                    telemetry).ConfigureAwait(false);

                var id = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = storePath,
                    Thumbprint = cert.Thumbprint
                };

                using var manager = new CertificateManager(telemetry);
                var passwordProvider = new CertificatePasswordProvider();
                var handler = new X509IdentityTokenHandler(
                    id,
                    passwordProvider,
                    manager.CertificateProvider);

                Assert.That(handler.Token, Is.Not.Null,
                    "Wire-format X509IdentityToken must be populated.");
                Assert.That(((X509IdentityToken)handler.Token).CertificateData.Length,
                    Is.GreaterThan(0));

                SignatureData signature = await handler.SignAsync(
                    [0x01, 0x02, 0x03, 0x04],
                    SecurityPolicies.Basic256Sha256).ConfigureAwait(false);

                Assert.That(signature, Is.Not.Null);
                Assert.That(signature.Signature.Length, Is.GreaterThan(0));
            }
            finally
            {
                if (Directory.Exists(storePath))
                {
                    Directory.Delete(storePath, true);
                }
            }
        }
    }
}
