/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;

namespace Opc.Ua.Core.Tests
{
    /// <summary>
    /// Test helper to create a temporary directory cert store.
    /// </summary>
    public class TemporaryCertValidator : IDisposable
    {
        /// <summary>
        /// Create the cert store in a temp location.
        /// </summary>
        public static TemporaryCertValidator Create()
        {
            return new TemporaryCertValidator();
        }

        /// <summary>
        /// Ctor of the store, creates the random path name in a OS temp folder.
        /// </summary>
        private TemporaryCertValidator()
        {
            // pki directory root for test runs. 
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName() + Path.DirectorySeparatorChar;
            m_issuerStore = new DirectoryCertificateStore();
            m_issuerStore.Open(m_pkiRoot + "issuer");
            m_trustedStore = new DirectoryCertificateStore();
            m_trustedStore.Open(m_pkiRoot + "trusted");
        }

        /// <summary>
        /// Clean up the temporary folder.
        /// </summary>
        ~TemporaryCertValidator()
        {
            Dispose();
        }

        /// <summary>
        /// Dispose the certificates and delete folders used.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref m_disposed, 1, 0) == 0)
            {
                CleanupValidatorAndStores(true);
                m_issuerStore = null;
                m_trustedStore = null;
                var path = Utils.ReplaceSpecialFolderNames(m_pkiRoot);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
        }

        /// <summary>
        /// The certificate validator for the stores.
        /// </summary>
        public ICertificateValidator CertificateValidator => m_certificateValidator;
        /// <summary>
        /// The issuer store, contains certs used for chain validation.
        /// </summary>
        public ICertificateStore IssuerStore => m_issuerStore;
        /// <summary>
        /// The trusted store, used for trusted CA, Sub CA and leaf certificates.
        /// </summary>
        public ICertificateStore TrustedStore => m_trustedStore;

        /// <summary>
        /// Creates the validator using the issuer and trusted store.
        /// </summary>
        public CertificateValidator Update()
        {
            var certValidator = new CertificateValidator();
            var issuerTrustList = new CertificateTrustList {
                StoreType = "Directory",
                StorePath = m_issuerStore.Directory.FullName
            };
            var trustedTrustList = new CertificateTrustList {
                StoreType = "Directory",
                StorePath = m_trustedStore.Directory.FullName
            };
            certValidator.Update(issuerTrustList, trustedTrustList, null);
            m_certificateValidator = certValidator;
            return certValidator;
        }

        /// <summary>
        /// Clean up (delete) the content of the issuer and trusted store.
        /// </summary>
        public void CleanupValidatorAndStores(bool dispose = false)
        {
            TestUtils.CleanupTrustList(m_issuerStore, dispose);
            TestUtils.CleanupTrustList(m_trustedStore, dispose);
        }

        #region Private Fields
        private int m_disposed;
        private CertificateValidator m_certificateValidator;
        private DirectoryCertificateStore m_issuerStore;
        private DirectoryCertificateStore m_trustedStore;
        private string m_pkiRoot;
        #endregion
    };
}
