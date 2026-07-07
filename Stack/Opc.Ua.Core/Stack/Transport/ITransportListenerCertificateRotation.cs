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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Optional capability interface for <see cref="ITransportListener"/>
    /// implementations that support targeted force-renegotiation of
    /// SecureChannels after a server application certificate has been
    /// rotated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OPC UA Part 12 §7.10.9 (ApplyChanges) requires that once the
    /// <c>ApplyChanges</c> method response has been delivered, the server
    /// <i>shall</i> force existing SecureChannels affected by the change
    /// to renegotiate and use the new ServerCertificate. The spec also
    /// notes that servers <i>may</i> close SecureChannels without
    /// discarding any Sessions or Subscriptions — the client's reconnect
    /// logic transfers the Session over a fresh SecureChannel.
    /// </para>
    /// <para>
    /// This capability surfaces the per-listener channel-cut step. The
    /// soft fan-out of the new certificate (endpoint description blobs,
    /// validator/registry references) is handled by the existing
    /// <see cref="ITransportListener.CertificateUpdate"/> method and is
    /// idempotent.
    /// </para>
    /// </remarks>
    public interface ITransportListenerCertificateRotation
    {
        /// <summary>
        /// Forces SecureChannels that were negotiated against
        /// <paramref name="oldCertificate"/> to renegotiate. Channels
        /// whose negotiated server certificate has a different thumbprint
        /// are left untouched. The listener socket remains bound so
        /// clients can immediately reconnect with the new certificate.
        /// </summary>
        /// <param name="oldCertificate">
        /// The previously-active application certificate. Channels whose
        /// <c>ServerCertificate.Thumbprint</c> matches this certificate
        /// are cut.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// The global channel ids of the SecureChannels that were
        /// closed by this call, useful for diagnostics and tests.
        /// Returns an empty list when no channels matched.
        /// </returns>
        ValueTask<IReadOnlyList<string>> CloseChannelsForCertificateAsync(
            Certificate oldCertificate,
            CancellationToken ct = default);
    }
}
