/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Input arguments for a single
    /// <c>PubSubKeyServiceType.GetSecurityKeys</c> call.
    /// </summary>
    /// <remarks>
    /// Implements the input-argument set defined by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3.2">
    /// Part 14 §8.3.2 GetSecurityKeys</see>. The struct is value-typed
    /// so that callers can build it without allocating.
    /// </remarks>
    /// <param name="SecurityGroupId">
    /// SecurityGroupId of the group whose keys are requested. Must be
    /// non-empty; the SKS rejects empty identifiers with
    /// <c>BadInvalidArgument</c>.
    /// </param>
    /// <param name="StartingTokenId">
    /// SKS-assigned token id from which to start the response. A value
    /// of <c>0</c> means "the current token id"; any other value
    /// requests history starting at that explicit token id.
    /// </param>
    /// <param name="RequestedKeyCount">
    /// Number of keys requested. <c>1</c> returns only the current
    /// (or specified) key; larger values return up to that many
    /// future keys.
    /// </param>
    public readonly record struct SksKeyRequest(
        string SecurityGroupId,
        uint StartingTokenId,
        uint RequestedKeyCount);
}
