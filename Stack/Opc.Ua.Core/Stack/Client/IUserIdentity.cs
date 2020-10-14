/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
        NodeIdCollection GrantedRoleIds { get; set; }

        /// <summary>
        /// Returns a UA user identity token containing the user information.
        /// </summary>
        /// <returns>UA user identity token containing the user information.</returns>
        UserIdentityToken GetIdentityToken();
    }
}
