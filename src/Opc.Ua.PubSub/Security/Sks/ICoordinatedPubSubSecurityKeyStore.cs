// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Provides linearizable SecurityGroup reads and conditional writes across SKS replicas.
    /// </summary>
    /// <remarks>
    /// Operations must not use a replica-local cache of shared authorization or keys.
    /// </remarks>
    public interface ICoordinatedPubSubSecurityKeyStore : IPubSubSecurityKeyStore
    {
        /// <summary>
        /// Whether the store is process-local and its operations complete synchronously.
        /// </summary>
        bool IsProcessLocal { get; }

        /// <summary>
        /// Reads the current group, including authorization and keys, and its opaque revision.
        /// An absent group has a null revision. Key material is owned by the caller, not the store.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<(SksSecurityGroup? Group, ByteString Revision)> ReadSecurityGroupAsync(
            string securityGroupId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits the entire group only if its revision still matches. A null revision
        /// requires that the group does not exist. The store must not retain references
        /// to the supplied plaintext key material after this operation returns.
        /// </summary>
        /// <param name="group">Group to commit.</param>
        /// <param name="expectedRevision">Revision read by the caller, or null to create the group.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Whether the revision matched and the group was committed.</returns>
        ValueTask<bool> TryCommitSecurityGroupAsync(
            SksSecurityGroup group,
            ByteString expectedRevision,
            CancellationToken cancellationToken = default);
    }
}
