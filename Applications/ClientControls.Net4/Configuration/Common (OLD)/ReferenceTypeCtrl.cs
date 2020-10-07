/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays a drop down list of reference types.
    /// </summary>
    public partial class ReferenceTypeCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ReferenceTypeCtrl"/> class.
        /// </summary>
        public ReferenceTypeCtrl()
        {
            InitializeComponent();
            m_baseTypeId = Opc.Ua.ReferenceTypeIds.References;
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
                m_baseTypeId = ReferenceTypeIds.References;
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
            if (referenceTypeId == null) throw new ApplicationException("referenceTypeId");
                        
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
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
