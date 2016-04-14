/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
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
using System.Text;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The table of all reference types known to the server.
    /// </summary>
    /// <remarks>This class is thread safe.</remarks>
    public class ContinuationPoint : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public ContinuationPoint()
        {
        }
        #endregion
        
        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {   
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing) 
            {
                Utils.SilentDispose(m_data);
            }
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// A unique identifier for the continuation point.
        /// </summary>
        public Guid Id
        {
            get { return m_id;  }
            set { m_id = value; }
        }
        
        /// <summary>
        /// The node manager that created the continuation point.
        /// </summary>
        public INodeManager Manager
        {
            get { return m_manager;  }
            set { m_manager = value; }
        }
                
        /// <summary>
        /// The view being browsed.
        /// </summary>
        public ViewDescription View
        {
            get { return m_view;  }
            set { m_view = value; }
        }
        
        /// <summary>
        /// The node being browsed.
        /// </summary>
        public object NodeToBrowse
        {
            get { return m_nodeToBrowse;  }
            set { m_nodeToBrowse = value; }
        }
                
        /// <summary>
        /// The maximum number of results to return.
        /// </summary>
        public uint MaxResultsToReturn
        {
            get { return m_maxResultsToReturn;  }
            set { m_maxResultsToReturn = value; }
        }
                
        /// <summary>
        /// What direction to follow the references.
        /// </summary>                
        public BrowseDirection BrowseDirection
        {
            get { return m_browseDirection;  }
            set { m_browseDirection = value; }
        }
                
        /// <summary>
        /// The reference type of the references to return.
        /// </summary>                
        public NodeId ReferenceTypeId
        {
            get { return m_referenceTypeId;  }
            set { m_referenceTypeId = value; }
        }
                                
        /// <summary>
        /// Whether subtypes of the reference type should be return as well.
        /// </summary>        
        public bool IncludeSubtypes
        {
            get { return m_includeSubtypes;  }
            set { m_includeSubtypes = value; }
        }
                                                
        /// <summary>
        /// The node class of the target nodes for the references to return.
        /// </summary>  
        public uint NodeClassMask
        {
            get { return m_nodeClassMask;  }
            set { m_nodeClassMask = value; }
        }
                                                
        /// <summary>
        /// The values to return.
        /// </summary>  
        public BrowseResultMask ResultMask
        {
            get { return m_resultMask;  }
            set { m_resultMask = value; }
        }

        /// <summary>
        /// The index where browsing halted.
        /// </summary>
        public int Index
        {
            get { return m_index;  }
            set { m_index = value; }
        }
        
        /// <summary>
        /// Node manager specific data that is necessary to continue the browse.
        /// </summary>
        /// <remarks>
        /// A node manager needs to hold onto unmanaged resources to continue the browse.
        /// If this is the case then the object stored here must implement the Idispose 
        /// interface. This will ensure the unmanaged resources are freed if the continuation
        /// point expires.
        /// </remarks>
        public object Data
        {
            get { return m_data;  }
            set { m_data = value; }
        }        

        /// <summary>
        /// Whether the ReferenceTypeId should be returned in the result.
        /// </summary>
        public bool ReferenceTypeIdRequired
        {
            get { return (m_resultMask & BrowseResultMask.ReferenceTypeId) != 0; }
        }
        
        /// <summary>
        /// Whether the IsForward flag should be returned in the result.
        /// </summary>
        public bool IsForwardRequired
        {
            get { return (m_resultMask & BrowseResultMask.IsForward) != 0; }
        }
        
        /// <summary>
        /// Whether the NodeClass should be returned in the result.
        /// </summary>
        public bool NodeClassRequired
        {
            get { return (m_resultMask & BrowseResultMask.NodeClass) != 0; }
        }
        
        /// <summary>
        /// Whether the BrowseName should be returned in the result.
        /// </summary>
        public bool BrowseNameRequired
        {
            get { return (m_resultMask & BrowseResultMask.BrowseName) != 0; }
        }
        
        /// <summary>
        /// Whether the DisplayName should be returned in the result.
        /// </summary>
        public bool DisplayNameRequired
        {
            get { return (m_resultMask & BrowseResultMask.DisplayName) != 0; }
        }
        
        /// <summary>
        /// Whether the TypeDefinition should be returned in the result.
        /// </summary>
        public bool TypeDefinitionRequired
        {
            get { return (m_resultMask & BrowseResultMask.TypeDefinition) != 0; }
        }
        
        /// <summary>
        /// False if it is not necessary to read the attributes a target node.
        /// </summary>
        /// <remarks>
        /// This flag is true if the NodeClass filter is set or the target node attributes are returned in the result.
        /// </remarks>
        public bool TargetAttributesRequired
        {
            get 
            { 
                if (m_nodeClassMask != 0)
                {
                    return true;
                }

                return (m_resultMask & (BrowseResultMask.NodeClass | BrowseResultMask.BrowseName | BrowseResultMask.DisplayName | BrowseResultMask.TypeDefinition)) != 0; 
            }
        }
        #endregion

        #region Private Fields
        private Guid m_id;
        private INodeManager m_manager;
        private ViewDescription m_view;
        private object m_nodeToBrowse;
        private uint m_maxResultsToReturn;
        private BrowseDirection m_browseDirection;
        private NodeId m_referenceTypeId;
        private bool m_includeSubtypes;
        private uint m_nodeClassMask;
        private BrowseResultMask m_resultMask;
        private int m_index;
        private object m_data;
        #endregion
    }
}
