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
        NodeIdCollection GrantedRoleIds { get; }

        /// <summary>
        /// <para>Returns a UA user identity token containing the user information and
        /// any secrets.</para>
        /// <para>
        /// IMPORTANT: the returned token should not be disposed by the caller as its
        /// lifetime is owned by the user identity. Because of this fact this is an
        /// unsafe operation and might be removed in future versions.
        /// </para>
        /// </summary>
        /// <returns>UA user identity token containing the user information.</returns>
        UserIdentityToken GetIdentityToken();
    }
}
