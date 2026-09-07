/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Certificates must load from paths the file system accepts, including
    /// paths longer than the Windows <c>MAX_PATH</c> limit.
    /// </summary>
    /// <remarks>
    /// A PKI root only has to be moderately deep before the certificate and
    /// private key files underneath it cross 260 characters. Directory
    /// enumeration happily returns those paths, but handing one to the platform
    /// certificate loader reaches CryptoAPI on Windows, which is not long-path
    /// aware and fails with <c>CryptographicException: The system cannot find
    /// the path specified</c>. Reading the file first sidesteps that, and these
    /// tests pin the behaviour so the shortcut is not reintroduced.
    /// </remarks>
    [TestFixture]
    [Category("Certificate")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CertificateLongPathTests
    {
        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(
                Path.GetTempPath(),
                "ualp" + Guid.NewGuid().ToString("N")[..8]);

            // Nest until the directory alone is past MAX_PATH, so any file in
            // it is too.
            string deep = m_root;
            while (deep.Length < 300)
            {
                deep = Path.Combine(deep, new string('d', 40));
            }

            try
            {
                Directory.CreateDirectory(deep);
            }
            catch (Exception ex) when (
                ex is PathTooLongException or DirectoryNotFoundException or IOException)
            {
                Assert.Ignore(
                    "This file system will not host a path past MAX_PATH: " + ex.Message);
            }

            m_deep = deep;
        }

        [TearDown]
        public void TearDown()
        {
            if (m_root != null && Directory.Exists(m_root))
            {
                try
                {
                    Directory.Delete(m_root, recursive: true);
                }
                catch (IOException)
                {
                    // A leftover temp directory must not fail the run.
                }
            }
            m_root = null;
            m_deep = null;
        }

        [Test]
        public void PublicCertificateLoadsFromAPathPastMaxPath()
        {
            using Certificate built = CertificateBuilder
                .Create("CN=LongPathPublic")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            string path = Path.Combine(m_deep!, "certificate.der");
            File.WriteAllBytes(path, built.RawData);

            Assert.That(path, Has.Length.GreaterThan(260));

            using var loaded = new Certificate(path);

            Assert.That(loaded.Thumbprint, Is.EqualTo(built.Thumbprint));
            Assert.That(loaded.HasPrivateKey, Is.False);
        }

        [Test]
        public void PrivateKeyLoadsFromAPathPastMaxPath()
        {
            using Certificate built = CertificateBuilder
                .Create("CN=LongPathPrivate")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            string path = Path.Combine(m_deep!, "certificate.pfx");
            File.WriteAllBytes(path, built.Export(X509ContentType.Pfx, "pw"));

            Assert.That(path, Has.Length.GreaterThan(260));

            using var loaded = new Certificate(
                path,
                "pw".AsSpan(),
                X509KeyStorageFlags.Exportable);

            Assert.That(loaded.Thumbprint, Is.EqualTo(built.Thumbprint));
            Assert.That(loaded.HasPrivateKey, Is.True);
        }

        private string m_root;
        private string m_deep;
    }
}
