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
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens;

/// <summary>
/// Headless validator for the certificate-trust-store integration:
/// constructs a temporary <see cref="ApplicationConfiguration"/> pointing
/// at scratch directory PKI stores, synthesizes a self-signed
/// <see cref="X509Certificate2"/>, and asserts that
/// <see cref="CertificateStoreService"/>'s <c>AddAsync</c>, <c>ListAsync</c>,
/// <c>DeleteAsync</c>, <c>TrustRejectedAsync</c>, and
/// <c>DeleteExpiredAsync</c> behave correctly across the three
/// <c>TrustChoice</c> code paths exposed by the Connect wizard.
/// </summary>
internal static class CertTrustProbe
{
    public static async Task<int> RunAsync()
    {
        Console.WriteLine("== Cert trust probe ==");
        int rc = 0;
        string root = Path.Combine(Path.GetTempPath(), "subex-cert-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var telemetry = new ProbeTelemetry();
            ApplicationConfiguration cfg = BuildScratchConfig(root);
            var svc = new CertificateStoreService(cfg, telemetry);

            using X509Certificate2 cert = MakeSelfSigned("CN=Probe Test");

            // -- TrustPermanently: must round-trip through AddAsync.
            bool added = await svc.AddAsync(CertStoreKind.Trusted, cert).ConfigureAwait(false);
            rc |= AssertEqual("AddAsync to Trusted", true, added);

            IReadOnlyList<X509Certificate2> trusted =
                await svc.ListAsync(CertStoreKind.Trusted).ConfigureAwait(false);
            bool found = false;
            foreach (X509Certificate2 c in trusted)
            {
                if (c.Thumbprint == cert.Thumbprint)
                { found = true; break; }
            }
            rc |= AssertEqual("Trusted contains cert after AddAsync", true, found);

            // -- DeleteAsync removes it.
            bool deleted = await svc.DeleteAsync(CertStoreKind.Trusted, cert.Thumbprint).ConfigureAwait(false);
            rc |= AssertEqual("DeleteAsync from Trusted", true, deleted);
            IReadOnlyList<X509Certificate2> trustedAfter =
                await svc.ListAsync(CertStoreKind.Trusted).ConfigureAwait(false);
            rc |= AssertEqual("Trusted empty after delete", 0, trustedAfter.Count);

            // -- TrustRejectedAsync: cert in Rejected → Trusted.
            await svc.AddAsync(CertStoreKind.Rejected, cert).ConfigureAwait(false);
            bool moved = await svc.TrustRejectedAsync(cert.Thumbprint).ConfigureAwait(false);
            rc |= AssertEqual("TrustRejectedAsync returns true", true, moved);
            IReadOnlyList<X509Certificate2> trustedAfterMove =
                await svc.ListAsync(CertStoreKind.Trusted).ConfigureAwait(false);
            rc |= AssertEqual("Trusted contains cert after TrustRejected", 1, trustedAfterMove.Count);
            IReadOnlyList<X509Certificate2> rejectedAfterMove =
                await svc.ListAsync(CertStoreKind.Rejected).ConfigureAwait(false);
            rc |= AssertEqual("Rejected empty after TrustRejected", 0, rejectedAfterMove.Count);

            // -- DeleteExpiredAsync: with a not-yet-expired cert, count = 0.
            int expired = await svc.DeleteExpiredAsync(CertStoreKind.Trusted).ConfigureAwait(false);
            rc |= AssertEqual("DeleteExpired with fresh cert deletes 0", 0, expired);

            Console.WriteLine(rc == 0 ? "CERT TRUST PROBE PASS" : "CERT TRUST PROBE FAIL");
            return rc;
        }
        finally
        {
            try
            { Directory.Delete(root, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private static ApplicationConfiguration BuildScratchConfig(string root)
    {
        return new ApplicationConfiguration
        {
            ApplicationName = "CertTrustProbe",
            ApplicationUri = "urn:CertTrustProbe",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = "Directory",
                    StorePath = Path.Combine(root, "own"),
                    SubjectName = "CN=CertTrustProbe"
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = Path.Combine(root, "trusted")
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = Path.Combine(root, "issuer")
                },
                RejectedCertificateStore = new CertificateStoreIdentifier
                {
                    StoreType = "Directory",
                    StorePath = Path.Combine(root, "rejected")
                }
            }
        };
    }

    private static X509Certificate2 MakeSelfSigned(string subject)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        // Mark as CA-like so it has a basic constraint extension (optional but realistic).
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset notAfter = DateTimeOffset.UtcNow.AddDays(30);
        using X509Certificate2 withKey = req.CreateSelfSigned(notBefore, notAfter);
        // Strip the private key — the store only needs the public cert.
        byte[] der = withKey.Export(X509ContentType.Cert);
        return X509CertificateLoader.LoadCertificate(der);
    }

    private static int AssertEqual<T>(string what, T expected, T actual)
    {
        bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
        Console.WriteLine(ok ? $"  PASS  {what}" : $"  FAIL  {what}: expected={expected}, actual={actual}");
        return ok ? 0 : 1;
    }

    private sealed class ProbeTelemetry : ITelemetryContext
    {
        public ILoggerFactory LoggerFactory { get; } = NullLoggerFactory.Instance;
        public Meter CreateMeter() => new("UaLens.CertTrustProbe");
        public ActivitySource ActivitySource { get; } = new("UaLens.CertTrustProbe");
    }
}
