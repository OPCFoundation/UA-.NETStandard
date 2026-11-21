/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;

namespace OnboardingDevice
{
    /// <summary>
    /// Accepts only the identity already selected by the device security owner.
    /// </summary>
    internal sealed class DevicePullActiveIdentity : IPullDcaIdentityProvider
    {
        /// <summary>
        /// Borrows the certificate manager that owns the active device identity.
        /// </summary>
        public DevicePullActiveIdentity(ICertificateManager certificates)
        {
            m_certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
        }

        /// <inheritdoc/>
        public ValueTask ActivateAsync(ByteString selectedIdentity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using CertificateEntry? active = m_certificates.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            if (selectedIdentity.IsEmpty || active == null || !active.Certificate.HasPrivateKey ||
                !active.Certificate.RawData.AsSpan().SequenceEqual(selectedIdentity.Span))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The selected identity is not the active device identity.");
            }
            return default;
        }

        /// <summary>The device's certificate manager.</summary>
        private readonly ICertificateManager m_certificates;
    }
}
