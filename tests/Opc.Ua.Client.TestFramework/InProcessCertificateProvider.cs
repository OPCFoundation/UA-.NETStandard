/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.TestFramework
{
    /// <summary>
    /// Test-only <see cref="ICertificateProvider"/> that wraps a single
    /// in-memory <see cref="Certificate"/>. Used by tests that previously
    /// constructed <c>new UserIdentity(certificate)</c> directly and
    /// now need to feed a private-key cert into the
    /// <see cref="X509IdentityTokenHandler"/> ctor without persisting it
    /// to a directory store.
    /// </summary>
    public sealed class InProcessCertificateProvider : ICertificateProvider, IDisposable
    {
        private Certificate? m_cert;

        public InProcessCertificateProvider(Certificate cert)
        {
            if (cert == null)
            {
                throw new ArgumentNullException(nameof(cert));
            }
            m_cert = cert.AddRef();
        }

        public Certificate? TryGetPrivateKeyCertificate(string thumbprint)
        {
            Certificate? cert = m_cert;
            return cert != null && string.Equals(cert.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)
                ? cert.AddRef()
                : null;
        }

        public ValueTask<Certificate?> GetPrivateKeyCertificateAsync(
            CertificateIdentifier identifier,
            ICertificatePasswordProvider? passwordProvider = null,
            string? applicationUri = null,
            CancellationToken ct = default)
        {
            Certificate? cert = m_cert;
            if (cert == null)
            {
                return new ValueTask<Certificate?>((Certificate?)null);
            }
            return new ValueTask<Certificate?>(cert.AddRef());
        }

        public void Dispose()
        {
            m_cert?.Dispose();
            m_cert = null;
        }
    }
}
