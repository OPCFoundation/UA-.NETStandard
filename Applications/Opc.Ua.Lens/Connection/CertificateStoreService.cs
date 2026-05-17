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
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Connection;

/// <summary>
/// Identifies one of the three certificate stores managed by the
/// CertificateStoreDialog.
/// </summary>
internal enum CertStoreKind
{
    Trusted,
    Issuer,
    Rejected
}

/// <summary>
/// Lightweight wrapper around the SDK's <see cref="ICertificateStore"/>
/// API that the certificate-store-management dialog consumes.  Each call
/// opens its store, performs the operation, and closes the store again
/// so concurrent dialog operations don't fight over a shared handle.
/// </summary>
internal sealed class CertificateStoreService
{
    private readonly ApplicationConfiguration m_config;
    private readonly ITelemetryContext m_telemetry;
    private readonly ILogger m_log;

    public CertificateStoreService(ApplicationConfiguration config, ITelemetryContext telemetry)
    {
        m_config = config;
        m_telemetry = telemetry;
        m_log = telemetry.CreateLogger("CertificateStore");
    }

    /// <summary>Enumerate every certificate currently in the store.</summary>
    public async Task<IReadOnlyList<X509Certificate2>> ListAsync(CertStoreKind kind, CancellationToken ct = default)
    {
        ICertificateStore? store = OpenStore(kind);
        if (store is null)
        {
            return Array.Empty<X509Certificate2>();
        }
        try
        {
            Opc.Ua.Security.Certificates.CertificateCollection coll = await store.EnumerateAsync(ct).ConfigureAwait(false);
            var list = new List<X509Certificate2>(coll.Count);
            foreach (Opc.Ua.Security.Certificates.Certificate cert in coll)
            {
                list.Add(cert.AsX509Certificate2());
            }
            return list;
        }
        finally
        {
            store.Close();
            store.Dispose();
        }
    }

    /// <summary>Delete one certificate from a store by thumbprint. Returns true when the cert was deleted.</summary>
    public async Task<bool> DeleteAsync(CertStoreKind kind, string thumbprint, CancellationToken ct = default)
    {
        ICertificateStore? store = OpenStore(kind);
        if (store is null)
        {
            return false;
        }

        try
        {
            return await store.DeleteAsync(thumbprint, ct).ConfigureAwait(false);
        }
        finally
        {
            store.Close();
            store.Dispose();
        }
    }

    /// <summary>
    /// Add a certificate to a store (Trusted or Issuer).  Returns true on
    /// success.  Used by the "Add from file" flow in the cert dialog.
    /// </summary>
    public async Task<bool> AddAsync(CertStoreKind kind, X509Certificate2 cert, CancellationToken ct = default)
    {
        ICertificateStore? store = OpenStore(kind);
        if (store is null)
        {
            return false;
        }

        try
        {
            using Opc.Ua.Security.Certificates.Certificate wrapper = Opc.Ua.Security.Certificates.Certificate.From(cert);
            await store.AddAsync(wrapper, password: null, ct).ConfigureAwait(false);
            m_log.LogInformation("Added certificate {Thumbprint} ({Subject}) to {Kind}.",
                cert.Thumbprint, cert.Subject, kind);
            return true;
        }
        catch (Exception ex)
        {
            m_log.LogError(ex, "Failed to add certificate to {Kind}.", kind);
            return false;
        }
        finally
        {
            store.Close();
            store.Dispose();
        }
    }

    /// <summary>
    /// Move a certificate from the Rejected store into the Trusted Peers store.
    /// Adds the cert to Trusted first, then deletes from Rejected to avoid a
    /// transient empty state if the add fails.
    /// </summary>
    public async Task<bool> TrustRejectedAsync(string thumbprint, CancellationToken ct = default)
    {
        // Find the cert in the rejected store.
        ICertificateStore? rej = OpenStore(CertStoreKind.Rejected);
        if (rej is null)
        {
            return false;
        }

        X509Certificate2? cert = null;
        try
        {
            Opc.Ua.Security.Certificates.CertificateCollection found = await rej.FindByThumbprintAsync(thumbprint, ct).ConfigureAwait(false);
            cert = found.Count > 0 ? found[0].AsX509Certificate2() : null;
        }
        finally
        {
            rej.Close();
            rej.Dispose();
        }
        if (cert is null)
        {
            return false;
        }

        ICertificateStore? trusted = OpenStore(CertStoreKind.Trusted);
        if (trusted is null)
        {
            return false;
        }

        try
        {
            using Opc.Ua.Security.Certificates.Certificate wrapper = Opc.Ua.Security.Certificates.Certificate.From(cert);
            await trusted.AddAsync(wrapper, password: null, ct).ConfigureAwait(false);
        }
        finally
        {
            trusted.Close();
            trusted.Dispose();
        }
        bool ok = await DeleteAsync(CertStoreKind.Rejected, thumbprint, ct).ConfigureAwait(false);
        m_log.LogInformation("Trusted rejected certificate {Thumbprint} (delete from rejected: {Ok}).", thumbprint, ok);
        return true;
    }

    /// <summary>
    /// Delete every certificate in <paramref name="kind"/> whose <c>NotAfter</c>
    /// is in the past.  Returns the count of certificates deleted.
    /// </summary>
    public async Task<int> DeleteExpiredAsync(CertStoreKind kind, CancellationToken ct = default)
    {
        IReadOnlyList<X509Certificate2> all = await ListAsync(kind, ct).ConfigureAwait(false);
        DateTime now = DateTime.UtcNow;
        int deleted = 0;
        foreach (X509Certificate2 cert in all)
        {
            if (cert.NotAfter.ToUniversalTime() < now)
            {
                if (await DeleteAsync(kind, cert.Thumbprint, ct).ConfigureAwait(false))
                {
                    deleted++;
                }
            }
        }
        m_log.LogInformation("DeleteExpired({Kind}): deleted {Count} of {Total} certificates.", kind, deleted, all.Count);
        return deleted;
    }

    private ICertificateStore? OpenStore(CertStoreKind kind)
    {
        CertificateStoreIdentifier? id = kind switch
        {
            CertStoreKind.Trusted => m_config.SecurityConfiguration?.TrustedPeerCertificates,
            CertStoreKind.Issuer => m_config.SecurityConfiguration?.TrustedIssuerCertificates,
            CertStoreKind.Rejected => m_config.SecurityConfiguration?.RejectedCertificateStore,
            _ => null
        };
        if (id is null || string.IsNullOrEmpty(id.StorePath))
        {
            return null;
        }
        return id.OpenStore(m_telemetry);
    }
}
