// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Optional probe interface for an authenticated <see cref="IUserIdentity"/>
    /// whose credentials stop being valid at a known time, for example an
    /// identity created from an IssuedIdentityToken with an expiration claim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implemented alongside <see cref="IUserIdentity"/> (not inherited) and
    /// probed via <c>identity as IIdentityExpiration</c>. An
    /// <see cref="IIdentityAugmenter"/> that wraps an identity should forward
    /// this interface so the expiration is not lost.
    /// </para>
    /// <para>
    /// OPC 10000-4 7.40.6: once the token has expired the Server invalidates
    /// the credentials of the Session; the Client must call ActivateSession
    /// with a new token. The server rejects other requests of the Session with
    /// <see cref="StatusCodes.BadIdentityTokenInvalid"/> until then.
    /// </para>
    /// </remarks>
    public interface IIdentityExpiration
    {
        /// <summary>
        /// The UTC time after which the credentials are no longer valid,
        /// including any clock skew tolerance the authenticator grants, or
        /// <see langword="null"/> when they do not expire.
        /// </summary>
        DateTime? ExpiresAt { get; }
    }
}
