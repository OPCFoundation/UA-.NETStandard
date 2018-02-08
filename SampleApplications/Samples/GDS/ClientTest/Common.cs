/* ========================================================================
 * Copyright (c) 2005-2017 The OPC Foundation, Inc. All rights reserved.
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

using NUnit.Framework;
using Opc.Ua.Configuration;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;


namespace Opc.Ua.Gds.Test
{
    public class ApplicationMessageDlg : IApplicationMessageDlg
    {
        private string message = string.Empty;
        private bool ask = false;

        public override void Message(string text, bool ask)
        {
            this.message = text;
            this.ask = ask;
        }

        public override async Task<bool> ShowAsync()
        {
            if (ask)
            {
                message += " (y/n, default y): ";
                Console.Write(message);
            }
            else
            {
                Console.WriteLine(message);
            }
            if (ask)
            {
                try
                {
                    ConsoleKeyInfo result = Console.ReadKey();
                    Console.WriteLine();
                    return await Task.FromResult((result.KeyChar == 'y') || (result.KeyChar == 'Y') || (result.KeyChar == '\r'));
                }
                catch
                {
                    // intentionally fall through
                }
            }
            return await Task.FromResult(true);
        }
    }

    public class TestUtils
    {
        public static void CleanupTrustList(ICertificateStore _store)
        {
            using (var store = _store)
            {
                var certs = store.Enumerate().Result;
                foreach (var cert in certs)
                {
                    store.Delete(cert.Thumbprint);
                }
                var crls = store.EnumerateCRLs();
                foreach (var crl in crls)
                {
                    store.DeleteCRL(crl);
                }
            }
        }

        public static void DeleteDirectory(string storePath)
        {
            string fullStorePath = Utils.ReplaceSpecialFolderNames(storePath);
            if (Directory.Exists(fullStorePath))
            {
                Directory.Delete(fullStorePath, true);
            }
        }

        const int MaxPort = 64000;
        const int MinPort = Opc.Ua.Utils.UaTcpDefaultPort;
        public static void PatchBaseAddressesPorts(ApplicationConfiguration config, int basePort)
        {
            if (basePort >= MinPort && basePort <= MaxPort)
            {
                StringCollection newBaseAddresses = new StringCollection();
                foreach (var baseAddress in config.ServerConfiguration.BaseAddresses)
                {
                    UriBuilder baseAddressUri = new UriBuilder(baseAddress);
                    baseAddressUri.Port = basePort++;
                    newBaseAddresses.Add(baseAddressUri.Uri.AbsoluteUri);
                }
                config.ServerConfiguration.BaseAddresses = newBaseAddresses;
            }
        }

        public static string PatchOnlyGDSEndpointUrlPort(string url, int port)
        {
            if (port >= MinPort && port <= MaxPort)
            {
                UriBuilder newUrl = new UriBuilder(url);
                if (newUrl.Path.Contains("GlobalDiscoveryTestServer"))
                {
                    newUrl.Port = port;
                    return newUrl.Uri.AbsoluteUri;
                }
            }
            return url;
        }

        public static void VerifyApplicationCertIntegrity(byte[] certificate, byte[] privateKey, string privateKeyPassword, string privateKeyFormat, byte[][] issuerCertificates)
        {
            X509Certificate2 newCert = new X509Certificate2(certificate);
            Assert.IsNotNull(newCert);
            X509Certificate2 newPrivateKeyCert = null;
            if (privateKeyFormat == "PFX")
            {
                newPrivateKeyCert = CertificateFactory.CreateCertificateFromPKCS12(privateKey, privateKeyPassword);
            }
            else if (privateKeyFormat == "PEM")
            {
                newPrivateKeyCert = CertificateFactory.CreateCertificateWithPEMPrivateKey(newCert, privateKey, privateKeyPassword);
            }
            else
            {
                Assert.Fail("Invalid private key format");
            }
            Assert.IsNotNull(newPrivateKeyCert);
            // verify the public cert matches the private key
            Assert.IsTrue(CertificateFactory.VerifyRSAKeyPair(newCert, newPrivateKeyCert, true));
            Assert.IsTrue(CertificateFactory.VerifyRSAKeyPair(newPrivateKeyCert, newPrivateKeyCert, true));
            CertificateIdentifierCollection issuerCertIdCollection = new CertificateIdentifierCollection();
            foreach (var issuer in issuerCertificates)
            {
                var issuerCert = new X509Certificate2(issuer);
                Assert.IsNotNull(issuerCert);
                issuerCertIdCollection.Add(new CertificateIdentifier(issuerCert));
            }

            // verify cert with issuer chain
            CertificateValidator certValidator = new CertificateValidator();
            CertificateTrustList issuerStore = new CertificateTrustList();
            CertificateTrustList trustedStore = new CertificateTrustList();
            trustedStore.TrustedCertificates = issuerCertIdCollection;
            certValidator.Update(trustedStore, issuerStore, null);
            Assert.That(() =>
            {
                certValidator.Validate(newCert);
            }, Throws.Exception);
            issuerStore.TrustedCertificates = issuerCertIdCollection;
            certValidator.Update(issuerStore, trustedStore, null);
            certValidator.Validate(newCert);
        }

    }

}
