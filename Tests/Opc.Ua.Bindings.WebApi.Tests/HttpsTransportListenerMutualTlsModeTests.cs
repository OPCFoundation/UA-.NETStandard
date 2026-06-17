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

#nullable enable

using System.IO;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using NUnit.Framework;

namespace Opc.Ua.Bindings.WebApi.Tests
{
    /// <summary>
    /// Source-level regression test for the Kestrel mTLS adapter
    /// configuration in <see cref="HttpsTransportListener"/>. Pins the
    /// behaviour fixed by alert <c>sec-8-mtls-require-certificate</c>:
    /// when mTLS is enabled the listener used to configure
    /// <see cref="ClientCertificateMode.AllowCertificate"/>, which let
    /// cert-less clients reach the dispatcher anonymously despite the
    /// <see cref="ClientCertificateMode.RequireCertificate"/>
    /// contract documented on
    /// <see cref="Opc.Ua.TransportListenerSettings.HttpsMutualTls"/>.
    /// </summary>
    /// <remarks>
    /// Spinning up a real Kestrel host with mTLS to test the runtime
    /// behaviour pulls in TLS-cert generation + per-OS trust-store
    /// quirks that are flaky in CI. A source-level grep is a tight,
    /// hermetic guard against the regression: it fails if either
    /// call-site re-introduces <c>AllowCertificate</c> when
    /// <c>m_mutualTlsEnabled</c> is true.
    /// </remarks>
    [TestFixture]
    [Category("WebApiSecurity")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class HttpsTransportListenerMutualTlsModeTests
    {
        [Test]
        public void KestrelMutualTlsAdapterRequiresClientCertificate()
        {
            string source = LocateHttpsTransportListenerSource();
            string content = File.ReadAllText(source);

            Assert.That(content.Contains("ClientCertificateMode.RequireCertificate"), Is.True,
                "HttpsTransportListener must configure RequireCertificate when " +
                "m_mutualTlsEnabled is true so cert-less clients are rejected at the " +
                "TLS handshake, not the application layer (sec-8 fix).");
            Assert.That(content.Contains("ClientCertificateMode.AllowCertificate"), Is.False,
                "AllowCertificate must not be used — it permits cert-less connections " +
                "to reach the dispatcher anonymously, defeating the mTLS contract.");
        }

        private static string LocateHttpsTransportListenerSource()
        {
            // Walk up from the test assembly to find the repo root,
            // then locate the listener source. Works whether the test
            // runs from bin/Debug or from a flat publish output.
            string? path = Path.GetDirectoryName(typeof(HttpsTransportListenerMutualTlsModeTests).Assembly.Location);
            for (int i = 0; i < 10 && path != null; i++)
            {
                string candidate = Path.Combine(
                    path,
                    "Stack", "Opc.Ua.Bindings.Https", "Https", "HttpsTransportListener.cs");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                path = Path.GetDirectoryName(path);
            }
            Assert.Fail("Could not locate HttpsTransportListener.cs from test assembly path.");
            return string.Empty;
        }
    }
}
