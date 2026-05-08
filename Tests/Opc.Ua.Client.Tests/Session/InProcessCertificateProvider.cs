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
 *
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

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Test-only <see cref="ICertificateProvider"/> that wraps a single
    /// in-memory <see cref="Certificate"/>. Used by tests that previously
    /// constructed <c>new UserIdentity(certificate)</c> directly and
    /// now need to feed a private-key cert into the
    /// <see cref="X509IdentityTokenHandler"/> ctor without persisting it
    /// to a directory store.
    /// </summary>
    internal sealed class InProcessCertificateProvider : ICertificateProvider, IDisposable
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
