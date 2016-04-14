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
using System.Reflection;
using Windows.UI.Xaml.Controls;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays a drop down list of reference types.
    /// </summary>
    public sealed partial class ReferenceTypeCtrl : UserControl
    {
        private ComboBox ReferenceTypesCB = new ComboBox();

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ReferenceTypeCtrl"/> class.
        /// </summary>
        public ReferenceTypeCtrl()
        {
            InitializeComponent();
            m_baseTypeId = ReferenceTypes.References;
        }
        #endregion

        #region Private Fields
        private Session m_session;
        private NodeId m_baseTypeId;
        private event EventHandler<ReferenceSelectedEventArgs> m_referenceSelectionChanged;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Initializes the control with references starting with the specified based type.
        /// </summary>
        public void Initialize(Session session, NodeId baseTypeId)
        {
            m_session = session;
            m_baseTypeId = baseTypeId;

            if (NodeId.IsNull(m_baseTypeId))
            {
                m_baseTypeId = ReferenceTypes.References;
            }

            ReferenceTypesCB.Items.Clear();

            // recurcively fetch the reference types from the server.
            if (m_session != null)
            {
                AddReferenceTypes(m_baseTypeId, null);
            }
        }
        
        /// <summary>
        /// The currently seleected reference type id.
        /// </summary>
        public NodeId SelectedTypeId
        {
            get
            {
                ReferenceTypeChoice choice = ReferenceTypesCB.SelectedItem as ReferenceTypeChoice;

                if (choice == null)
                {
                    return null;
                }

                return choice.ReferenceType.NodeId;            
            }

            set
            {
                for (int ii = 0; ii < ReferenceTypesCB.Items.Count; ii++)
                {
                    ReferenceTypeChoice choice = ReferenceTypesCB.Items[ii] as ReferenceTypeChoice;
                    
                    if (choice != null && choice.ReferenceType.NodeId == value)
                    {
                        ReferenceTypesCB.SelectedIndex = ii;
                        return;
                    }
                }

                if (ReferenceTypesCB.Items.Count > 0)
                {
                    ReferenceTypesCB.SelectedIndex = 0;
                }
            }
        }

        /// <summary>
        /// Raised when the selected reference is changed.
        /// </summary>
        public event EventHandler<ReferenceSelectedEventArgs> ReferenceSelectionChanged
        {
            add { m_referenceSelectionChanged += value;  }
            remove { m_referenceSelectionChanged -= value;  }
        }

        #region ReferenceSelectedEventArgs Class
        /// <summary>
        /// Specifies the nodes that where selected in the control.
        /// </summary>
        public class ReferenceSelectedEventArgs : EventArgs
        {
            /// <summary>
            /// Constructs a new object.
            /// </summary>
            public ReferenceSelectedEventArgs(NodeId selectedTypeId)
            {
                m_referenceTypeId = selectedTypeId;
            }

            /// <summary>
            /// The reference type that was selected.
            /// </summary>
            public NodeId ReferenceTypeId
            {
                get { return m_referenceTypeId; }
            }

            private NodeId m_referenceTypeId;
        }
        #endregion
        #endregion
        
        #region ReferenceTypeChoice Class
        /// <summary>
        /// A reference type that may be used as a browse filter.
        /// </summary>
        private class ReferenceTypeChoice
        {
            /// <summary>
            /// The text to display in the control.
            /// </summary>
            public override string ToString()
            {
                if (ReferenceType == null)
                {
                    return "<None>";
                }

                StringBuilder text = new StringBuilder();   
             
                GetPrefix(text);

                if (text.Length > 0)
                {
                    text.Append("> ");
                }
                           
                if (ReferenceType != null)
                {
                    text.Append(ReferenceType.ToString());
                }
                
                return text.ToString();
            }
            
            /// <summary>
            /// Adds a prefix for subtypes.
            /// </summary>
            private void GetPrefix(StringBuilder prefix)
            {                
                if (SuperType != null)
                {
                    SuperType.GetPrefix(prefix);
                    prefix.Append("--");                   
                }
            }
            
            public ReferenceTypeNode ReferenceType;
            public ReferenceTypeChoice SuperType;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Adds the reference types to drop down box.
        /// </summary>
        private void AddReferenceTypes(ExpandedNodeId referenceTypeId, ReferenceTypeChoice supertype)
        {
            if (referenceTypeId == null) throw new Exception("referenceTypeId");
                        
            try
            {
                // find reference.
                ReferenceTypeNode node =  m_session.NodeCache.Find(referenceTypeId) as ReferenceTypeNode;

                if (node == null)
                {
                    return;
                }

                // add reference to combobox.
                ReferenceTypeChoice choice = new ReferenceTypeChoice();

                choice.ReferenceType = node;
                choice.SuperType     = supertype;

                ReferenceTypesCB.Items.Add(choice);
                
                // recursively add subtypes.
                IList<INode> subtypes = m_session.NodeCache.FindReferences(node.NodeId, ReferenceTypeIds.HasSubtype, false, true);

                foreach (INode subtype in subtypes)
                {
                    AddReferenceTypes(subtype.NodeId, choice);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Ignoring unknown reference type.");
                return;
            }
        }
        #endregion

        #region Event Handlers
        private void ReferenceTypesCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (m_referenceSelectionChanged != null)
                {
                    NodeId referenceTypeId = SelectedTypeId;

                    if (referenceTypeId != null)
                    {
                        m_referenceSelectionChanged(this, new ReferenceSelectedEventArgs(referenceTypeId));
                    }
                }                
			}
            catch (Exception exception)
            {
				GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
        #endregion
    }
}
