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
