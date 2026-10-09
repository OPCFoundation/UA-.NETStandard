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
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Interop.Tests
{
    /// <summary>
    /// PKI folders and certificate helpers for the interop tests.
    /// </summary>
    /// <remarks>
    /// Both stacks keep trusted, issuer and rejected stores below a PKI
    /// root, each with certs and crl folders. The own store (certs and
    /// private folders) is "own" for the RSA-only certificate of the 1.5
    /// peer and the PKI root itself for the default RSA and ECC set both
    /// stacks create otherwise. Certificates are written as DER files named
    /// "&lt;CN&gt; [&lt;thumbprint&gt;].der", private keys as PFX files without
    /// password in the private folder of the own store, CRLs as DER files in
    /// the crl folder.
    /// </remarks>
    internal static class InteropPki
    {
        public const string Own = "own";
        public const string Trusted = "trusted";
        public const string Issuer = "issuer";
        public const string Rejected = "rejected";

        /// <summary>
        /// Creates a short root below the temp folder. 1.5.378 cannot reload
        /// a certificate it has just created when the store path is long, so
        /// the root deliberately avoids deep folders.
        /// </summary>
        public static string CreateRoot()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "uaio-" + Path.GetFileNameWithoutExtension(Path.GetRandomFileName()));
            Directory.CreateDirectory(root);
            return root;
        }

        public static string ServerPki(string root)
        {
            return Path.Combine(root, "s");
        }

        public static string ClientPki(string root)
        {
            return Path.Combine(root, "c");
        }

        public static void Delete(string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                return;
            }
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
                // Best effort; the temp folder is cleaned up eventually.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort.
            }
        }

        /// <summary>
        /// Stops a peer server, if one was started, and deletes the PKI root
        /// even when stopping fails.
        /// </summary>
        public static async Task StopAndDeleteAsync(LegacyPeerProcess server, string root)
        {
            try
            {
                if (server != null)
                {
                    await server.StopAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    server.Dispose();
                }
            }
            finally
            {
                Delete(root);
            }
        }

        /// <summary>
        /// Waits until a store of a PKI root holds a certificate. Both stacks
        /// write rejected certificates in the background after they reject a
        /// connection, so the client can see the error before the file exists.
        /// </summary>
        public static async Task<string[]> WaitForCertificatesAsync(string pkiRoot, string store, TimeSpan timeout)
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            string[] certificates = Certificates(pkiRoot, store);
            while (certificates.Length == 0 && elapsed.Elapsed < timeout)
            {
                await Task.Delay(100).ConfigureAwait(false);
                certificates = Certificates(pkiRoot, store);
            }
            return certificates;
        }

        /// <summary>
        /// The certificates (DER files) in a store of a PKI root.
        /// </summary>
        public static string[] Certificates(string pkiRoot, string store)
        {
            string folder = Path.Combine(StorePath(pkiRoot, store), "certs");
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.der") : [];
        }

        /// <summary>
        /// The folder of a store of a PKI root.
        /// </summary>
        public static string StorePath(string pkiRoot, string store)
        {
            if (store == Own && !Directory.Exists(Path.Combine(pkiRoot, Own, "certs")))
            {
                return pkiRoot;
            }
            return Path.Combine(pkiRoot, store);
        }

        /// <summary>
        /// Makes the application certificates of one PKI root trusted by
        /// another one.
        /// </summary>
        public static void Trust(string trustingPkiRoot, string trustedPkiRoot)
        {
            string[] certificates = Certificates(trustedPkiRoot, Own);
            if (certificates.Length == 0)
            {
                throw new InvalidOperationException($"No application certificate in {trustedPkiRoot}.");
            }
            string folder = Path.Combine(trustingPkiRoot, Trusted, "certs");
            Directory.CreateDirectory(folder);
            foreach (string certificate in certificates)
            {
                File.Copy(certificate, Path.Combine(folder, Path.GetFileName(certificate)), true);
            }
            // 1.5 reloads a cached directory store only when the folder's
            // LastWriteTimeUtc is not older than its last check; a copy within
            // the same file system clock tick would keep the stale trust list.
            Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow.AddSeconds(2));
        }

        /// <summary>
        /// Creates a self-signed RSA CA.
        /// </summary>
        public static Certificate CreateCa(string subjectName)
        {
            return CertificateBuilder.Create(subjectName)
                .SetCAConstraint(0)
                .SetLifeTime(TimeSpan.FromDays(365))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// Replaces the RSA application certificate of a PKI root with one
        /// issued by the CA, with the same subject and alternative names, so
        /// the application finds and keeps it on its next start.
        /// </summary>
        public static Certificate ReissueApplicationCertificate(string pkiRoot, Certificate ca)
        {
            string certs = Path.Combine(StorePath(pkiRoot, Own), "certs");
            string keys = Path.Combine(StorePath(pkiRoot, Own), "private");
            foreach (string file in Directory.GetFiles(certs, "*.der"))
            {
                X509Certificate2 existing = LoadDer(file);
                try
                {
                    if (existing.GetRSAPublicKey() == null)
                    {
                        continue;
                    }
                    X509Extension alternativeNames = existing.Extensions
                        .Cast<X509Extension>()
                        .First(e => e.Oid!.Value == "2.5.29.17");

                    Certificate issued = CertificateBuilder.Create(existing.SubjectName)
                        .SetLifeTime(TimeSpan.FromDays(180))
                        .AddExtension(alternativeNames)
                        .SetIssuer(ca)
                        .SetRSAKeySize(2048)
                        .CreateForRSA();

                    File.Delete(file);
                    File.Delete(Path.Combine(keys, Path.ChangeExtension(Path.GetFileName(file), ".pfx")));

                    string baseName =
                        $"{existing.GetNameInfo(X509NameType.SimpleName, false)} [{issued.Thumbprint}]";
                    File.WriteAllBytes(Path.Combine(certs, baseName + ".der"), issued.RawData);
                    // The caller owns the copy, which holds the private key.
                    using (X509Certificate2 x509 = issued.AsX509Certificate2())
                    {
                        File.WriteAllBytes(Path.Combine(keys, baseName + ".pfx"), x509.Export(X509ContentType.Pfx));
                    }
                    return issued;
                }
                finally
                {
                    existing.Dispose();
                }
            }
            throw new InvalidOperationException($"No RSA application certificate in {pkiRoot}.");
        }

        /// <summary>
        /// Trusts the CA in a PKI root, together with its CRL.
        /// </summary>
        public static void TrustCa(string pkiRoot, Certificate ca, X509CRL crl)
        {
            string certs = Path.Combine(pkiRoot, Trusted, "certs");
            string crls = Path.Combine(pkiRoot, Trusted, "crl");
            Directory.CreateDirectory(certs);
            Directory.CreateDirectory(crls);
            File.WriteAllBytes(Path.Combine(certs, $"InteropCA [{ca.Thumbprint}].der"), ca.RawData);
            File.WriteAllBytes(Path.Combine(crls, "InteropCA.crl"), crl.RawData);
        }

        /// <summary>
        /// A CRL of the CA revoking the given certificates (an empty CRL when
        /// none are given).
        /// </summary>
        public static X509CRL CreateCrl(Certificate ca, params Certificate[] revoked)
        {
            // The collection adds a reference to each certificate; dispose it
            // so the certificates are released (the assembly checks for leaks).
            using var revokedCertificates = new CertificateCollection();
            foreach (Certificate certificate in revoked)
            {
                revokedCertificates.Add(certificate);
            }
#pragma warning disable CS0618 // the static helper is the simplest CRL builder for a test
            return CertificateFactory.RevokeCertificate(ca, null!, revokedCertificates);
#pragma warning restore CS0618
        }

        private static X509Certificate2 LoadDer(string file)
        {
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadCertificateFromFile(file);
#else
            return new X509Certificate2(File.ReadAllBytes(file));
#endif
        }
    }
}
