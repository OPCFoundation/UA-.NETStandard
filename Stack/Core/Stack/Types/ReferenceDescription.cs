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
    #region ReferenceDescription Class
	/// <summary>
	/// A reference returned in browse operation.
	/// </summary>
    public partial class ReferenceDescription : IFormattable
    {     
        #region IFormattable Members
        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                if (m_displayName != null && !String.IsNullOrEmpty(m_displayName.Text))
                {
                    return m_displayName.Text;
                }

                if (!QualifiedName.IsNull(m_browseName))
                {
                    return m_browseName.Name;
                }

                return Utils.Format("(unknown {0})", ((NodeClass)m_nodeClass).ToString().ToLower());
            }
        
            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        
        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        public override string ToString()
        {
            return ToString(null, null);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Sets the reference type for the reference.
        /// </summary>
        public void SetReferenceType(
            BrowseResultMask resultMask,
            NodeId           referenceTypeId,
            bool             isForward)
        {
            if ((resultMask & BrowseResultMask.ReferenceTypeId) != 0)
            {
                m_referenceTypeId = referenceTypeId;
            }
            else
            {
                m_referenceTypeId = null;
            }

            if ((resultMask & BrowseResultMask.IsForward) != 0)
            {
                m_isForward = isForward;
            }
            else
            {
                m_isForward = false;
            }
        }

        /// <summary>
        /// Sets the target attributes for the reference.
        /// </summary>
        public void SetTargetAttributes(
            BrowseResultMask resultMask,
            NodeClass        nodeClass,
            QualifiedName    browseName,
            LocalizedText    displayName,
            ExpandedNodeId   typeDefinition)
        {
            if ((resultMask & BrowseResultMask.NodeClass) != 0)
            {
                m_nodeClass = nodeClass;
            }
            else
            {
                m_nodeClass = 0;
            }

            if ((resultMask & BrowseResultMask.BrowseName) != 0)
            {
                m_browseName = browseName;
            }
            else
            {
                m_browseName = null;
            }

            if ((resultMask & BrowseResultMask.DisplayName) != 0)
            {
                m_displayName = displayName;
            }
            else
            {
                m_displayName = null;
            }

            if ((resultMask & BrowseResultMask.TypeDefinition) != 0)
            {
                m_typeDefinition = typeDefinition;
            }
            else
            {
                m_typeDefinition = null;
            }
        }
        #endregion

        #region Supporting Properties and Methods
        /// <summary>
        /// True if the reference filter has not been applied.
        /// </summary>
        public bool Unfiltered
        {
            get { return m_unfiltered;  }
            set { m_unfiltered = value; }
        }
        #endregion

        #region Private Fields
        private bool m_unfiltered;
        #endregion
    }
    #endregion
}
