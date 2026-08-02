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
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for <see cref="CertificateTypesProvider"/> certificate chain
    /// loading.
    /// </summary>
    [TestFixture]
    [Category("CertificateTypesProvider")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CertificateTypesProviderTests
    {
        /// <summary>
        /// Regression test for issue #3896: when the issuing CA is present in
        /// the issuer store (and not in the trusted store), the provider must
        /// still build the full chain. Previously
        /// <see cref="CertificateTypesProvider.LoadCertificateChainAsync"/>
        /// gated chain assembly on the boolean returned by GetIssuersAsync
        /// (isTrusted), which is false for issuer-store CAs, so the resolved
        /// issuers were dropped and only the leaf was sent.
        /// </summary>
        [Test]
        public async Task LoadCertificateChainResolvesIssuerFromIssuerStoreAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using X509Certificate2 ca = CertificateBuilder
                .Create("CN=ChainProviderRoot, O=OPC Foundation")
                .SetCAConstraint(-1)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using X509Certificate2 leaf = CertificateBuilder
                .Create("CN=ChainProviderLeaf, O=OPC Foundation")
                .SetIssuer(ca)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            string pkiRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string issuerPath = Path.Combine(pkiRoot, "issuer");
            string trustedPath = Path.Combine(pkiRoot, "trusted");
            try
            {
                // Place the CA in the issuer store only (NOT the trusted store).
                using (var issuerStore = new DirectoryCertificateStore(telemetry))
                {
                    issuerStore.Open(issuerPath, noPrivateKeys: true);
                    await issuerStore.AddAsync(ca).ConfigureAwait(false);
                }

                var config = new ApplicationConfiguration
                {
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        TrustedIssuerCertificates = new CertificateTrustList
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = issuerPath
                        },
                        TrustedPeerCertificates = new CertificateTrustList
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = trustedPath
                        },
                        SendCertificateChain = true
                    }
                };

                var provider = new CertificateTypesProvider(config, telemetry);
                await provider.InitializeAsync().ConfigureAwait(false);

                X509Certificate2Collection chain = await provider
                    .LoadCertificateChainAsync(leaf)
                    .ConfigureAwait(false);

                Assert.That(chain, Is.Not.Null);
                Assert.That(
                    chain.Count,
                    Is.EqualTo(2),
                    "The chain must contain the leaf and the issuer-store CA.");
                Assert.That(chain[0].Thumbprint, Is.EqualTo(leaf.Thumbprint));
                Assert.That(chain[1].Thumbprint, Is.EqualTo(ca.Thumbprint));

                // The raw blob must be leaf || CA byte-for-byte.
                byte[] raw = provider.LoadCertificateChainRaw(leaf);
                byte[] expected = new byte[leaf.RawData.Length + ca.RawData.Length];
                Buffer.BlockCopy(leaf.RawData, 0, expected, 0, leaf.RawData.Length);
                Buffer.BlockCopy(ca.RawData, 0, expected, leaf.RawData.Length, ca.RawData.Length);
                Assert.That(raw, Is.EqualTo(expected));
            }
            finally
            {
                if (Directory.Exists(pkiRoot))
                {
                    Directory.Delete(pkiRoot, true);
                }
            }
        }

        /// <summary>
        /// When the issuing CA is not present in any store the chain cannot be
        /// resolved and the provider must fall back to a leaf-only chain
        /// without throwing.
        /// </summary>
        [Test]
        public async Task LoadCertificateChainFallsBackToLeafWhenIssuerMissingAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using X509Certificate2 ca = CertificateBuilder
                .Create("CN=MissingProviderRoot, O=OPC Foundation")
                .SetCAConstraint(-1)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using X509Certificate2 leaf = CertificateBuilder
                .Create("CN=MissingProviderLeaf, O=OPC Foundation")
                .SetIssuer(ca)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            string pkiRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string issuerPath = Path.Combine(pkiRoot, "issuer");
            string trustedPath = Path.Combine(pkiRoot, "trusted");
            try
            {
                // Both stores are configured but empty: the CA is in neither.
                var config = new ApplicationConfiguration
                {
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        TrustedIssuerCertificates = new CertificateTrustList
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = issuerPath
                        },
                        TrustedPeerCertificates = new CertificateTrustList
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = trustedPath
                        },
                        SendCertificateChain = true
                    }
                };

                var provider = new CertificateTypesProvider(config, telemetry);
                await provider.InitializeAsync().ConfigureAwait(false);

                X509Certificate2Collection chain = await provider
                    .LoadCertificateChainAsync(leaf)
                    .ConfigureAwait(false);

                Assert.That(chain, Is.Not.Null);
                Assert.That(
                    chain.Count,
                    Is.EqualTo(1),
                    "Without a resolvable issuer the chain must contain only the leaf.");

                byte[] raw = provider.LoadCertificateChainRaw(leaf);
                Assert.That(raw, Is.EqualTo(leaf.RawData));
            }
            finally
            {
                if (Directory.Exists(pkiRoot))
                {
                    Directory.Delete(pkiRoot, true);
                }
            }
        }
    }
}
