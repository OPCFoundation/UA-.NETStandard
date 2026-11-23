// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Persists SecurityGroup key material for an SKS.
    /// </summary>
    public interface IPubSubSecurityKeyStore
    {
        /// <summary>
        /// Gets all known SecurityGroup identifiers.
        /// </summary>
        ValueTask<ArrayOf<string>> GetSecurityGroupIdsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a SecurityGroup, including current and future keys.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<SksSecurityGroup?> GetSecurityGroupAsync(
            string securityGroupId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves a SecurityGroup.
        /// </summary>
        /// <param name="group">SecurityGroup to save.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SaveSecurityGroupAsync(
            SksSecurityGroup group,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves a SecurityGroup using a lease fencing token to reject stale active writers when
        /// the caller has already resolved ownership.
        /// </summary>
        /// <param name="group">SecurityGroup to save.</param>
        /// <param name="fencingToken">Current lease fencing token.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SaveSecurityGroupAsync(
            SksSecurityGroup group,
            long fencingToken,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a SecurityGroup.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<bool> RemoveSecurityGroupAsync(
            string securityGroupId,
            CancellationToken cancellationToken = default);
    }
}
