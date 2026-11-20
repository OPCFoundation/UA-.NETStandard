// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Operational client for one Security Key Service endpoint.
    /// Wraps the OPC UA <c>GetSecurityKeys</c> method call and
    /// surfaces availability transitions so that subscribers can
    /// drive the security subsystem's PubSubState transitions
    /// without leaking transport details.
    /// </summary>
    /// <remarks>
    /// Implements the SKS pull profile defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3.2">
    /// Part 14 §8.3.2 GetSecurityKeys</see>. A single instance
    /// services every SecurityGroup hosted by the same SKS
    /// endpoint; each request carries the SecurityGroupId. The
    /// per-group cache and rotation logic live in
    /// <see cref="PullSecurityKeyProvider"/>, which composes this
    /// abstraction.
    /// </remarks>
    public interface ISecurityKeyService
    {
        /// <summary>
        /// Raised whenever the underlying SKS connectivity changes.
        /// </summary>
        event EventHandler<SksAvailabilityChangedEventArgs>? AvailabilityChanged;

        /// <summary>
        /// Issues a <c>GetSecurityKeys</c> call for the supplied
        /// <paramref name="request"/>.
        /// </summary>
        /// <param name="request">SKS pull request arguments.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The packed key material from the SKS.</returns>
        /// <exception cref="OpcUaSksException">
        /// Thrown when the SKS returns a Bad status, the call cannot
        /// be issued (no session), or the response is malformed.
        /// </exception>
        ValueTask<SksKeyResponse> GetSecurityKeysAsync(
            SksKeyRequest request,
            CancellationToken cancellationToken = default);
    }
}
