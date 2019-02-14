/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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

using System;
using System.Collections.Generic;
using System.Xml;
using System.ServiceModel;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Stores a block of encypted data.
    /// </summary>
    public class EncryptedData
    {
        #region Private Members
        /// <summary>
        /// The algorithm used to encrypt the data.
        /// </summary>
        public string Algorithm
        {
            get { return m_algorithm; }
            set { m_algorithm = value; }
        }

        /// <summary>
        /// The encrypted data.
        /// </summary>
        public byte[] Data
        {
            get { return m_data; }
            set { m_data = value; }
        }
        #endregion

        #region Private Members
        private string m_algorithm;
        private byte[] m_data;
        #endregion
    }
}
