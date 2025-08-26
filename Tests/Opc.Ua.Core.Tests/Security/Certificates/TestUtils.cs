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

using System.IO;
using System.Threading.Tasks;

namespace Opc.Ua.Core.Tests
{
    /// <summary>
    /// Common utilities for tests.
    /// </summary>
    public static class TestUtils
    {
        public static string[] EnumerateTestAssets(string searchPattern)
        {
            string assetsPath = Utils.GetAbsoluteDirectoryPath("Assets", true, true, false);
            if (assetsPath != null)
            {
                return [.. Directory.EnumerateFiles(assetsPath, searchPattern)];
            }
            return [];
        }

        /// <summary>
        /// A common method to clean up the test trust list.
        /// </summary>
        public static async Task CleanupTrustListAsync(ICertificateStore store, bool dispose = true)
        {
            if (store != null)
            {
                System.Security.Cryptography.X509Certificates.X509Certificate2Collection certs
                    = await store
                    .EnumerateAsync()
                    .ConfigureAwait(false);
                foreach (System.Security.Cryptography.X509Certificates.X509Certificate2 cert in certs)
                {
                    await store.DeleteAsync(cert.Thumbprint).ConfigureAwait(false);
                }
                if (store.SupportsCRLs)
                {
                    Ua.Security.Certificates.X509CRLCollection crls = await store
                        .EnumerateCRLsAsync()
                        .ConfigureAwait(false);
                    foreach (Ua.Security.Certificates.X509CRL crl in crls)
                    {
                        await store.DeleteCRLAsync(crl).ConfigureAwait(false);
                    }
                }
                if (dispose)
                {
                    store.Dispose();
                }
            }
        }
    }
}
