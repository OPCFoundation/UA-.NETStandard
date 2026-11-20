// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
