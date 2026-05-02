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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Manages the lifecycle of application certificates, including
    /// updates, rejections, and change notifications.
    /// </summary>
    public interface ICertificateLifecycle
    {
        /// <summary>
        /// Gets an observable stream of certificate change events.
        /// </summary>
        IObservable<CertificateChangeEvent> CertificateChanges { get; }

        /// <summary>
        /// Updates the application certificate for the specified certificate type.
        /// </summary>
        /// <param name="certificateType">
        /// The OPC UA certificate type node identifier.
        /// </param>
        /// <param name="newCertificate">The new certificate to install.</param>
        /// <param name="issuerChain">
        /// An optional issuer chain to store alongside the certificate.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A task that completes when the update is finished.</returns>
        Task UpdateApplicationCertificateAsync(
            NodeId certificateType,
            Certificate newCertificate,
            CertificateCollection? issuerChain = null,
            CancellationToken ct = default);

        /// <summary>
        /// Rejects a certificate chain, typically adding it to the
        /// rejected certificates store.
        /// </summary>
        /// <param name="chain">The certificate chain to reject.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A task that completes when the rejection is recorded.</returns>
        Task RejectCertificateAsync(
            CertificateCollection chain,
            CancellationToken ct = default);
    }
}
