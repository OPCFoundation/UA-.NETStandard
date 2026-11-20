// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Server-side abstraction over a Security Key Service. The
    /// <see cref="SksMethodHandler"/> binds this interface to the
    /// OPC UA <c>PubSubKeyServiceType</c> Object so a Server can
    /// host the SKS for other Publishers and Subscribers.
    /// </summary>
    /// <remarks>
    /// Implements the SKS server-side surface defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3.1">
    /// Part 14 §8.3.1 PubSubKeyServiceType</see>. The interface
    /// intentionally exposes the SecurityGroup-administration
    /// methods (<c>AddSecurityGroup</c> / <c>RemoveSecurityGroup</c>
    /// / <c>GetSecurityGroup</c>) alongside the operational
    /// <c>GetSecurityKeys</c> entry-point so that host code can
    /// administer the SKS via DI without binding to a concrete
    /// implementation.
    /// </remarks>
    public interface IPubSubKeyServiceServer
    {
        /// <summary>
        /// Snapshot of every currently-registered SecurityGroupId.
        /// </summary>
        ArrayOf<string> SecurityGroupIds { get; }

        /// <summary>
        /// Issues keys for the requested SecurityGroup.
        /// </summary>
        /// <param name="callerIdentity">
        /// Authenticated caller identity. The implementation must reject
        /// empty identities and enforce per-SecurityGroup key access.
        /// </param>
        /// <param name="request">SKS pull request arguments.</param>
        /// <param name="callerRoleIds">RoleIds granted to the caller.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The packed key material.</returns>
        /// <exception cref="OpcUaSksException">
        /// Thrown when the request is rejected (unauthorized caller,
        /// missing identity, exhausted future-key budget...).
        /// </exception>
        ValueTask<SksKeyResponse> GetSecurityKeysAsync(
            string callerIdentity,
            SksKeyRequest request,
            ArrayOf<NodeId> callerRoleIds = default,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a new SecurityGroup. Generates the initial set of
        /// keys for the group when <see cref="SksSecurityGroup.Keys"/>
        /// is empty.
        /// </summary>
        /// <param name="group">SecurityGroup configuration.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="OpcUaSksException">
        /// Thrown when the SecurityGroupId is already registered or
        /// the policy URI is not supported.
        /// </exception>
        ValueTask AddSecurityGroupAsync(
            SksSecurityGroup group,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a SecurityGroup from the SKS.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="OpcUaSksException">
        /// Thrown when the SecurityGroupId is not registered.
        /// </exception>
        ValueTask RemoveSecurityGroupAsync(
            string securityGroupId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Looks up a SecurityGroup by identifier.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// The configured group or <see langword="null"/> when the
        /// identifier is not registered.
        /// </returns>
        ValueTask<SksSecurityGroup?> GetSecurityGroupAsync(
            string securityGroupId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Invalidates the current and future keys for a SecurityGroup.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask InvalidateKeysAsync(
            string securityGroupId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Forces an unplanned key rotation for a SecurityGroup.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask ForceKeyRotationAsync(
            string securityGroupId,
            CancellationToken cancellationToken = default);
    }
}
