/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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

using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the CertificateFactory class.
    /// </summary>
    [TestFixture, Category("CertificateStore")]
    [SetCulture("en-us")]
    public class CertificateStoreTest
    {
        #region DataPointSources
        public const string X509StoreSubject = "CN=Opc.Ua.Core.Tests, O=OPC Foundation, OU=X509Store, C=US";
        #endregion

        #region Test Setup
        /// <summary>
        /// Clean up the test cert store folder
        /// </summary>
        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            using (var x509Store = new X509CertificateStore())
            {
                x509Store.Open("CurrentUser\\UA_MachineDefault");
                var collection = x509Store.Enumerate().Result;
                foreach (var cert in collection)
                {
                    if (X509Utils.CompareDistinguishedName(X509StoreSubject, cert.Subject))
                    {
                        x509Store.Delete(cert.Thumbprint).Wait();
                    }
                }
            }

            using (var x509Store = new X509CertificateStore())
            {
                x509Store.Open("CurrentUser\\My");
                var collection = x509Store.Enumerate().Result;
                foreach (var cert in collection)
                {
                    if (X509Utils.CompareDistinguishedName(X509StoreSubject, cert.Subject))
                    {
                        x509Store.Delete(cert.Thumbprint).Wait();
                    }
                }
            }
        }
        #endregion

        #region Test Methods
        /// <summary>
        /// Verify new app certificate is stored in X509Store.
        /// </summary>
        [Test]
        [TestCase("CurrentUser\\UA_MachineDefault")]
        [TestCase("CurrentUser\\My")]
        public async Task VerifyAppCertX509Store(string storePath)
        {
            X509Certificate2 publicKey;
            using (var appCertificate = CertificateFactory.CreateCertificate(X509StoreSubject)
                .CreateForRSA()
                .AddToStore(
                    CertificateStoreType.X509Store,
                    storePath
                ))
            {
                Assert.NotNull(appCertificate);
                Assert.True(appCertificate.HasPrivateKey);
                publicKey = new X509Certificate2(appCertificate.RawData);
                Assert.NotNull(publicKey);
                Assert.False(publicKey.HasPrivateKey);
            }

            var id = new CertificateIdentifier() {
                Thumbprint = publicKey.Thumbprint,
                StorePath = storePath,
                StoreType = CertificateStoreType.X509Store
            };
            var privateKey = await id.LoadPrivateKey(null);
            Assert.NotNull(privateKey);
            Assert.True(privateKey.HasPrivateKey);

            X509Utils.VerifyRSAKeyPair(publicKey, privateKey, true);

            using (var x509Store = new X509CertificateStore())
            {
                x509Store.Open(storePath);
                await x509Store.Delete(publicKey.Thumbprint);
            }
        }
        #endregion
    }

}
