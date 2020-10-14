/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// The supported roles in a GDS server.
    /// </summary>
    public enum GdsRole
    {
        /// <summary>
        /// The GDS application Administrator.
        /// </summary>
        ApplicationAdmin,

        /// <summary>
        /// The GDS application user.
        /// </summary>
        ApplicationUser
    }

    /// <summary>
    /// The role based identity for a GDS server.
    /// </summary>
    public class RoleBasedIdentity : IUserIdentity
    {
        private IUserIdentity m_identity;
        private GdsRole m_role;

        /// <summary>
        /// Initialize the role based identity.
        /// </summary>
        public RoleBasedIdentity(IUserIdentity identity, GdsRole role)
        {
            m_identity = identity;
            m_role = role;
        }

        /// <inheritdoc/>
        public NodeIdCollection GrantedRoleIds
        {
            get { return m_identity.GrantedRoleIds; }
            set { m_identity.GrantedRoleIds = value; }
        }

        /// <summary>
        /// The role in the context of a Gds.
        /// </summary>
        public GdsRole Role
        {
            get { return m_role; }
        }

        /// <inheritdoc/>
        public string DisplayName
        {
            get { return m_identity.DisplayName; }
        }

        /// <inheritdoc/>
        public string PolicyId
        {
            get { return m_identity.PolicyId; }
        }

        /// <inheritdoc/>
        public UserTokenType TokenType
        {
            get { return m_identity.TokenType; }
        }

        /// <inheritdoc/>
        public XmlQualifiedName IssuedTokenType
        {
            get { return m_identity.IssuedTokenType; }
        }

        /// <inheritdoc/>
        public bool SupportsSignatures
        {
            get { return m_identity.SupportsSignatures; }
        }

        /// <inheritdoc/>
        public UserIdentityToken GetIdentityToken()
        {
            return m_identity.GetIdentityToken();
        }
    }
}
