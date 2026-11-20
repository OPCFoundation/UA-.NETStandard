/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.IO;
using System.Threading.Tasks;

namespace Opc.Ua.Core.TestFramework
{
    /// <summary>
    /// Common utilities for tests.
    /// </summary>
    public static class TestUtils
    {
        public static string[] EnumerateTestAssets(string searchPattern)
        {
            string assetsPath = Utils.GetAbsoluteDirectoryPath("Assets", true, true, false)!;
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
                using Security.Certificates.CertificateCollection certs
                    = await store
                    .EnumerateAsync()
                    .ConfigureAwait(false);
                foreach (Security.Certificates.Certificate cert in certs)
                {
                    await store.DeleteAsync(cert.Thumbprint).ConfigureAwait(false);
                }
                if (store.SupportsCRLs)
                {
                    Security.Certificates.X509CRLCollection crls = await store
                        .EnumerateCRLsAsync()
                        .ConfigureAwait(false);
                    foreach (Security.Certificates.X509CRL crl in crls)
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
