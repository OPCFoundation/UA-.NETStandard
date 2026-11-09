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
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Gds.Client;

namespace UaLens.Plugins.GdsPush;

/// <summary>
/// Minimal facade over a GDS-style client that can produce a Certificate
/// Signing Request and apply a freshly-signed certificate back to the
/// server.  Both <see cref="ServerPushConfigurationClient"/> (Push model,
/// OPC 10000-12 §7) and <c>GlobalDiscoveryServerClient</c> (Pull model,
/// OPC 10000-12 §6) can be wrapped to satisfy this surface so the same
/// helper flow can drive either context — Wave 3a (GDS Push) consumes it
/// directly and Wave 3b (GDS Management) will wrap the GDS pull client.
/// </summary>
internal interface IGdsClientLike
{
    /// <summary>
    /// Builds a CSR on the server for the given certificate group/type.
    /// </summary>
    /// <param name="certificateGroupId">DefaultApplicationGroup, HttpsGroup, etc.</param>
    /// <param name="certificateTypeId">Certificate type NodeId (typically
    /// <see cref="ObjectTypeIds.RsaSha256ApplicationCertificateType"/>).</param>
    /// <param name="subjectName">X.500 subject string (CN=, O=, etc.).</param>
    /// <param name="regeneratePrivateKey">When true, the server generates a
    /// fresh private key; when false the existing key is reused.</param>
    /// <param name="nonce">Server-supplied entropy for key derivation.  May
    /// be empty.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The CSR as raw DER bytes (PKCS#10).</returns>
    Task<byte[]> CreateSigningRequestAsync(
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        string subjectName,
        bool regeneratePrivateKey,
        byte[] nonce,
        CancellationToken ct);

    /// <summary>
    /// Applies a signed certificate back to the server.  When
    /// <paramref name="privateKey"/> is non-empty the server is expected to
    /// also replace its private key in the given format.
    /// </summary>
    Task ApplyUpdatedCertificateAsync(
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        byte[] certificate,
        IReadOnlyList<byte[]> issuerCertificates,
        string privateKeyFormat,
        byte[] privateKey,
        CancellationToken ct);
}

/// <summary>
/// <see cref="IGdsClientLike"/> wrapper around the Push-model
/// <see cref="ServerPushConfigurationClient"/>.  Pure delegation — present
/// here so that <see cref="GdsCertRequestHelper"/> can be driven from a
/// Push tab without a hard dependency on the concrete GDS client type.
/// </summary>
internal sealed class PushClientAdapter : IGdsClientLike
{
    private readonly IServerPushConfigurationClient m_client;

    public PushClientAdapter(IServerPushConfigurationClient client)
    {
        m_client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<byte[]> CreateSigningRequestAsync(
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        string subjectName,
        bool regeneratePrivateKey,
        byte[] nonce,
        CancellationToken ct)
    {
        ByteString csr = await m_client.CreateSigningRequestAsync(
            certificateGroupId,
            certificateTypeId,
            subjectName,
            regeneratePrivateKey,
            (nonce ?? Array.Empty<byte>()).ToByteString(),
            ct).ConfigureAwait(false);
        return csr.Memory.ToArray();
    }

    public async Task ApplyUpdatedCertificateAsync(
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        byte[] certificate,
        IReadOnlyList<byte[]> issuerCertificates,
        string privateKeyFormat,
        byte[] privateKey,
        CancellationToken ct)
    {
        var issuerList = new List<ByteString>();
        if (issuerCertificates is not null)
        {
            foreach (byte[] cert in issuerCertificates)
            {
                if (cert is { Length: > 0 })
                {
                    issuerList.Add(cert.ToByteString());
                }
            }
        }
        ArrayOf<ByteString> issuers = issuerList;
        await m_client.UpdateCertificateAsync(
            certificateGroupId,
            certificateTypeId,
            (certificate ?? Array.Empty<byte>()).ToByteString(),
            privateKeyFormat ?? string.Empty,
            (privateKey ?? Array.Empty<byte>()).ToByteString(),
            issuers,
            ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Reusable orchestration of the CSR + apply-cert flow.  Held to a tiny
/// surface so it can be shared by the Push and Pull (GDS Management) tab
/// apps without dragging extra state across them.  All public methods are
/// pure helpers — no internal state, no events.
/// </summary>
internal static class GdsCertRequestHelper
{
    /// <summary>
    /// Asks the server to generate a CSR for the supplied group/type.  The
    /// returned byte array is the raw PKCS#10 DER blob; callers wrap it in
    /// PEM via <see cref="ToPem"/> for display.
    /// </summary>
    public static Task<byte[]> GenerateCsrAsync(
        IGdsClientLike client,
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        string subjectName,
        bool regeneratePrivateKey,
        byte[] nonce,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        // certificateGroupId/certificateTypeId are NodeId — readonly struct
        // (cannot be null); use .IsNull if "empty" needs to be rejected.
        return client.CreateSigningRequestAsync(
            certificateGroupId,
            certificateTypeId,
            subjectName ?? string.Empty,
            regeneratePrivateKey,
            nonce ?? Array.Empty<byte>(),
            ct);
    }

    /// <summary>
    /// Pushes a freshly signed certificate to the server.  No
    /// <c>ApplyChanges</c> is invoked here — that is the caller's choice
    /// because some servers require ApplyChanges to run interactively (the
    /// session is typically dropped immediately after).
    /// </summary>
    public static async Task ApplyUpdatedCertificateAsync(
        IGdsClientLike client,
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        byte[] certificate,
        IReadOnlyList<byte[]> issuerCertificates,
        string privateKeyFormat,
        byte[] privateKey,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        // certificateGroupId/certificateTypeId are NodeId (readonly struct).
        ArgumentNullException.ThrowIfNull(certificate);
        await client.ApplyUpdatedCertificateAsync(
            certificateGroupId,
            certificateTypeId,
            certificate,
            issuerCertificates ?? Array.Empty<byte[]>(),
            privateKeyFormat ?? string.Empty,
            privateKey ?? Array.Empty<byte>(),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Wraps raw DER bytes into a PEM block with the given label (e.g.
    /// "CERTIFICATE REQUEST", "CERTIFICATE").  Line-wraps base64 at 64
    /// chars per RFC 7468.
    /// </summary>
    public static string ToPem(ReadOnlySpan<byte> der, string label)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        if (der.IsEmpty)
        {
            return string.Empty;
        }
        string b64 = Convert.ToBase64String(der);
        var sb = new StringBuilder(b64.Length + 80);
        sb.Append("-----BEGIN ").Append(label).Append("-----\n");
        for (int i = 0; i < b64.Length; i += 64)
        {
            int len = Math.Min(64, b64.Length - i);
            sb.Append(b64, i, len);
            sb.Append('\n');
        }
        sb.Append("-----END ").Append(label).Append("-----\n");
        return sb.ToString();
    }

    /// <summary>
    /// Parses a PEM-encoded block.  Tolerates Windows line endings, leading
    /// whitespace, and extra trailer text after the END marker.  Throws on
    /// malformed input.
    /// </summary>
    public static byte[] FromPem(string pem, string expectedLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pem);
        ArgumentException.ThrowIfNullOrEmpty(expectedLabel);

        string begin = "-----BEGIN " + expectedLabel + "-----";
        string end = "-----END " + expectedLabel + "-----";
        int s = pem.IndexOf(begin, StringComparison.Ordinal);
        if (s < 0)
        {
            throw new FormatException("Missing BEGIN " + expectedLabel + " marker.");
        }
        int e = pem.IndexOf(end, s + begin.Length, StringComparison.Ordinal);
        if (e < 0)
        {
            throw new FormatException("Missing END " + expectedLabel + " marker.");
        }
        ReadOnlySpan<char> body = pem.AsSpan(s + begin.Length, e - (s + begin.Length));
        var stripped = new StringBuilder(body.Length);
        foreach (char ch in body)
        {
            if (!char.IsWhiteSpace(ch))
            {
                stripped.Append(ch);
            }
        }
        return Convert.FromBase64String(stripped.ToString());
    }

    /// <summary>
    /// Tries to interpret an arbitrary byte blob as either a PEM-wrapped or
    /// raw DER X.509 certificate.  Returns the parsed
    /// <see cref="X509Certificate2"/> on success.  Throws on malformed
    /// input.
    /// </summary>
    public static X509Certificate2 ParseCertificate(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
        {
            throw new FormatException("Empty certificate buffer.");
        }
        if (LooksLikePem(bytes))
        {
            string text = Encoding.ASCII.GetString(bytes);
            byte[] der = FromPem(text, "CERTIFICATE");
            return X509CertificateLoader.LoadCertificate(der);
        }
        return X509CertificateLoader.LoadCertificate(bytes);
    }

    /// <summary>
    /// Loads a certificate from disk, auto-detecting DER (.cer/.crt/.der)
    /// vs PEM (.pem/.crt).  Returns the parsed certificate.
    /// </summary>
    public static async Task<X509Certificate2> LoadCertificateFromFileAsync(
        string filePath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        byte[] raw = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
        return ParseCertificate(raw);
    }

    private static bool LooksLikePem(byte[] bytes)
    {
        // PEM blocks always start with "-----BEGIN ".  We only need to
        // look at the first handful of bytes — anything longer would not
        // be a valid PEM header.
        if (bytes.Length < 11)
        {
            return false;
        }
        // Allow leading whitespace before the marker.
        int i = 0;
        while (i < bytes.Length && (bytes[i] == ' ' || bytes[i] == '\t' ||
                                     bytes[i] == '\r' || bytes[i] == '\n'))
        {
            i++;
        }
        if (bytes.Length - i < 11)
        {
            return false;
        }
        return bytes[i] == '-' && bytes[i + 1] == '-' && bytes[i + 2] == '-' &&
               bytes[i + 3] == '-' && bytes[i + 4] == '-' && bytes[i + 5] == 'B' &&
               bytes[i + 6] == 'E' && bytes[i + 7] == 'G' && bytes[i + 8] == 'I' &&
               bytes[i + 9] == 'N' && bytes[i + 10] == ' ';
    }
}
