/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// An interface to an object with stores the identity of a user.
    /// </summary>
    public interface IUserIdentity
    {
        /// <summary>
        /// A display name that identifies the user.
        /// </summary>
        /// <value>The display name.</value>
        string DisplayName { get; }

        /// <summary>
        /// The user token policy.
        /// </summary>
        /// <value>The user token policy.</value>
        string PolicyId { get; }

        /// <summary>
        /// The type of identity token used.
        /// </summary>
        /// <value>The type of the token.</value>
        UserTokenType TokenType { get; }

        /// <summary>
        /// The type of issued token.
        /// </summary>
        /// <value>The type of the issued token.</value>
        XmlQualifiedName IssuedTokenType { get; }

        /// <summary>
        /// Whether the object can create signatures to prove possession of the user's credentials.
        /// </summary>
        /// <value><c>true</c> if signatures are supported; otherwise, <c>false</c>.</value>
        bool SupportsSignatures { get; }

        /// <summary>
        /// Get or sets the list of granted role ids associated to the UserIdentity.
        /// </summary>
        ArrayOf<NodeId> GrantedRoleIds { get; }

        /// <summary>
        /// <para>
        /// Returns a UA user identity token handler containing the user information
        /// and any secrets.
        /// </para>
        /// <para>
        /// IMPORTANT: the returned handler should not be disposed by the caller as its
        /// lifetime is owned by the user identity. Because of this fact this is an
        /// unsafe operation and might be removed in future versions.
        /// </para>
        /// </summary>
        /// <returns>UA user identity token containing the user information.</returns>
        IUserIdentityTokenHandler TokenHandler { get; }
    }
}
