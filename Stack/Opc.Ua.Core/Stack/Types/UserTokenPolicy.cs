/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Text;
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
