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
