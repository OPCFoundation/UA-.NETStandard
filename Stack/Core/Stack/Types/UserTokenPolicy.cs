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

using System;
using System.Text;
using System.ServiceModel.Security;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Defines constants for key user token policies.
    /// </summary>
    public partial class UserTokenPolicy : IFormattable
    {
        #region Constructors
        /// <summary>
        /// Creates an empty token policy with the specified token type.
        /// </summary>
        public UserTokenPolicy(UserTokenType tokenType)
        {
            Initialize();
            m_tokenType = tokenType;
        }
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Returns the object formatted as a string.
        /// </summary>
        public override string ToString()
        {
            return m_tokenType.ToString();
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return String.Format(formatProvider, "{0}", ToString());
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion
    }
}
