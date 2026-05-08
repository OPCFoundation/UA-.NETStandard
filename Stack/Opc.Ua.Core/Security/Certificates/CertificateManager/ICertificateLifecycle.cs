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

        /// <summary>
        /// Reloads the application certificate registry from the supplied
        /// security configuration. Replaces all entries in the manager's
        /// application certificate snapshot with freshly loaded ones,
        /// disposing the previous entries.
        /// </summary>
        /// <remarks>
        /// This is the integration point for hot certificate-update flows:
        /// after a push or rotation mutates the
        /// <see cref="SecurityConfiguration.ApplicationCertificates"/>,
        /// callers can invoke this method to bring the registry's snapshot
        /// in sync. (Historically known as the
        /// <c>CertificateValidator.UpdateCertificateAsync</c> path; that
        /// API has been removed.)
        /// </remarks>
        /// <param name="securityConfiguration">
        /// The (post-update) security configuration to load from.
        /// </param>
        /// <param name="applicationUri">
        /// Optional application URI used for matching certificates.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A task that completes when the reload is finished.</returns>
        Task ReloadApplicationCertificatesAsync(
            SecurityConfiguration securityConfiguration,
            string? applicationUri = null,
            CancellationToken ct = default);

        /// <summary>
        /// Re-applies a <see cref="SecurityConfiguration"/> at runtime: refreshes
        /// trust-list paths, validation flags, and the application certificate
        /// snapshot. Cached per-trust-list validators are invalidated so the
        /// next validation picks up any changes.
        /// </summary>
        /// <remarks>
        /// Re-applies a <see cref="SecurityConfiguration"/> at runtime so
        /// running components pick up trust-list / certificate / flag
        /// changes without a full restart. Used by
        /// <c>ServerInternalData.OnUpdateConfigurationAsync</c>. Unlike
        /// <see cref="ReloadApplicationCertificatesAsync"/>, this method
        /// also re-maps trust-list paths and re-snapshots validation flags.
        /// </remarks>
        /// <param name="securityConfiguration">The new security configuration.</param>
        /// <param name="applicationUri">
        /// Optional application URI used for matching certificates.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A task that completes when the update is finished.</returns>
        Task UpdateAsync(
            SecurityConfiguration securityConfiguration,
            string? applicationUri = null,
            CancellationToken ct = default);

        /// <summary>
        /// Returns a task that completes when the most recently enqueued
        /// rejected-certificate write has been processed (or immediately when
        /// the queue is idle).
        /// </summary>
        /// <remarks>
        /// Primarily a test affordance that lets assertions observe the
        /// rejected-store contents synchronously after enqueueing a
        /// rejection.
        /// </remarks>
        Task FlushRejectedAsync(CancellationToken ct = default);
    }
}
