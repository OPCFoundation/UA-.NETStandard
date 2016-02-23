/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
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
        /// Returns a UA user identity token containing the user information.
        /// </summary>
        /// <returns>UA user identity token containing the user information.</returns>
        UserIdentityToken GetIdentityToken();
    }
}
