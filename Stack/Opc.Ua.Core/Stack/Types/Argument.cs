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

using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
	/// <summary>
	/// The Argument class.
	/// </summary>
	public partial class Argument
    {
        #region Public Properties
        /// <summary>
        /// Initializes an instance of the argument.
        /// </summary>
        public Argument(string name, NodeId dataType, int valueRank, string description)
        {
            this.m_name = name;
            this.m_dataType = dataType;
            this.m_valueRank = valueRank;
            this.m_description = description;
        }
        #endregion

        #region Public Properties
        /// <summary>
		/// The value for the argument.
		/// </summary>
		public object Value
		{
			get { return m_value;  }
			set { m_value = value; }
		}
		#endregion

		#region Private Fields
		private object m_value;
		#endregion
	}
}
