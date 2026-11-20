/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// An interface to an object that describes an operation in the server tied to a request.
    /// </summary>
    public interface ISessionOperationContext : IOperationContext
    {
        /// <summary>
        /// The identifier for the session (null if multiple sessions are associated with the operation).
        /// </summary>
        /// <value>The session identifier.</value>
        NodeId SessionId { get; }

        /// <summary>
        /// The identity of the user.
        /// </summary>
        /// <value>The user identity.</value>
        IUserIdentity UserIdentity { get; }

        /// <summary>
        /// The ClientUserId of the user (OPC 10000-5 6.4.3): the user name of a
        /// UserName token, the subject of an X.509 token, the issuer and subject of
        /// an issued token, or <c>null</c> for an anonymous user.
        /// </summary>
        /// <value>The ClientUserId.</value>
        string? ClientUserId { get; }

        /// <summary>
        /// The lifetime of the request
        /// </summary>
        RequestLifetime RequestLifetime { get; }
    }
}
