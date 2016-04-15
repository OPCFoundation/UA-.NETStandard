/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua.Client;
using Windows.UI.Xaml.Controls;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class BrowseListCtrl : BaseListCtrl
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public BrowseListCtrl()
        {
            m_stack = new List<ItemData>();
            m_position = -1;
        }
		#endregion

        #region Private Fields
        private Session m_session;
        private Browser m_browser;
        private NodeId m_startId;
        private List<ItemData> m_stack;
        private int m_position;
        private event EventHandler m_PositionChanged;
        private event EventHandler m_PositionAdded;

        #endregion

        #region Public Interface
        /// <summary>
        /// Raised when the position was changed.
        /// </summary>
        public event EventHandler PositionChanged
        {
            add { m_PositionChanged += value; }
            remove { m_PositionChanged -= value; }
        }

        /// <summary>
        /// Raised when a new position is added to the control.
        /// </summary>
        public event EventHandler PositionAdded
        {
            add { m_PositionAdded += value; }
            remove { m_PositionAdded -= value; }
        }

        /// <summary>
        /// The current position
        /// </summary>
        [DefaultValue(-1)]
        public int Position
        {
            get { return m_position+1;  }
            set { SetPosition(value-1); }
        }

        /// <summary>
        /// Returns the NodeIds of the positions stored in the control.
        /// </summary>
        public ICollection<NodeId> Positions
        {
            get
            { 
                List<NodeId> positions = new List<NodeId>();

                positions.Add(m_startId);

                foreach (ItemData itemData in m_stack)
                {
                    positions.Add(itemData.Target.NodeId);
                }

                return positions;
            }
        }

        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            ItemsLV.Items.Clear();
        }

        /// <summary>
        /// Initializes the control with the session/subscription indicated.
        /// </summary>
        public void Initialize(Browser browser, NodeId startId)
        {
            m_browser = null;
            m_session = null;
            
            Clear();

            // nothing to do if no browser provided.
            if (browser == null)
            {
                return;
            }                     
            
            m_browser  = browser;
            m_session  = browser.Session;
            m_startId  = startId;
            m_position = -1;
            
            m_stack.Clear();

            Browse(startId);
        }

        /// <summary>
        /// Moves to the previous position.
        /// </summary>
        public void Back()
        {
            SetPosition(m_position);
        }

        /// <summary>
        /// Moves to the next position.
        /// </summary>
        public void Forward()
        {
            SetPosition(m_position+2);
        }

        /// <summary>
        /// Sets the current position.
        /// </summary>
        public void SetPosition(int position)
        {
            position--;

            if (position < 0)
            {
                position = -1;
            }

            if (position >= m_stack.Count)
            {
                position = m_stack.Count-1;
            }

            if (m_position == position)
            {
                return;
            }
            
            m_position = position;
            
            if (m_position == -1)
            {
                Browse(m_startId);
            }                      
            else
            {
                Browse(m_stack[m_position].Target.NodeId);
            }

            if (m_PositionChanged != null)
            {
                m_PositionChanged(this, null);
            }
        }

        /// <summary>
        /// Displays the target of a browse operation in the control.
        /// </summary>
        private void Browse(NodeId startId)
        {
            if (m_browser == null || NodeId.IsNull(startId))
            {                
                Clear();
                return;
            }
            
            List<ItemData> variables = new List<ItemData>();
            
            // browse the references from the node and build list of variables.
            BeginUpdate();

            foreach (ReferenceDescription reference in m_browser.Browse(startId))
            {
                Node target = m_session.NodeCache.Find(reference.NodeId) as Node;

                if (target == null)
                {
                    continue;
                }

                ReferenceTypeNode referenceType = m_session.NodeCache.Find(reference.ReferenceTypeId) as ReferenceTypeNode;

                Node typeDefinition = null;

                if ((target.NodeClass & (NodeClass.Variable | NodeClass.Object)) != 0)
                {
                    typeDefinition = m_session.NodeCache.Find(reference.TypeDefinition) as Node;
                }
                else
                {
                    typeDefinition = m_session.NodeCache.Find(m_session.NodeCache.TypeTree.FindSuperType(target.NodeId)) as Node;
                }

                ItemData item = new ItemData(referenceType, !reference.IsForward, target, typeDefinition);
                AddItem(item, GuiUtils.GetTargetIcon(m_browser.Session, reference), -1);    
        
                if ((target.NodeClass & (NodeClass.Variable | NodeClass.VariableType)) != 0)
                {
                    variables.Add(item);
                }
            }

            EndUpdate();

            // read the current value for any variables.
            if (variables.Count > 0)
            {
                ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
                
                foreach (ItemData item in variables)
                {
                    ReadValueId valueId = new ReadValueId();

                    valueId.NodeId       = item.Target.NodeId;
                    valueId.AttributeId  = Attributes.Value;
                    valueId.IndexRange   = null;
                    valueId.DataEncoding = null;

                    nodesToRead.Add(valueId);
                }
                    
                DataValueCollection values;
                DiagnosticInfoCollection diagnosticInfos;

                m_session.Read(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    out values,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(values, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
                
                for (int ii = 0; ii < variables.Count; ii++)
                {
                    variables[ii].Value = values[ii];
                    
                    foreach (ListViewItem item in ItemsLV.Items)
                    {
                        if (Object.ReferenceEquals(item.Tag, variables[ii]))
                        {
                            UpdateItem(item, variables[ii]);
                            break;
                        }
                    }
                }
            }
        }
        #endregion
        
        #region ItemData Class
        /// <summary>
        /// Stores the data associated with a list view item.
        /// </summary>
        private class ItemData : IComparable
        {
            public ReferenceTypeNode ReferenceType;
            public bool IsInverse;
            public Node Target;
            public Node TypeDefinition;
            public DataValue Value;
            public string SortKey = String.Empty;
            
            public ItemData(ReferenceTypeNode referenceType, bool isInverse, Node target, Node typeDefinition)
            {
                ReferenceType  = referenceType;
                IsInverse      = isInverse;
                Target         = target;
                TypeDefinition = typeDefinition;
            }

            #region IComparable Members
            /// <summary>
            /// Compares the obj.
            /// </summary>
            public int CompareTo(object obj)
            {
                ItemData target = obj as ItemData;

                if (Object.ReferenceEquals(target, null))
                {
                    return -1;
                }

                if (Object.ReferenceEquals(target, this))
                {
                    return 0;
                }

                return this.SortKey.CompareTo(target.SortKey);
            }
            #endregion
        }
        #endregion

        #region Overridden Methods
        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
		{
            // TBD
		}

        /// <summary>
        /// Handles a double click.
        /// </summary>
        protected override void PickItems()
        {
            if (ItemsLV.SelectedItems.Count <= 0)
            {
                return;
            }

            ItemData itemData = ((ListViewItem) ItemsLV.SelectedItems[0]).Tag as ItemData;
            
            if (itemData == null)
            {
                return;
            }

            base.PickItems();
            
            if (m_position >= 0 && m_position < m_stack.Count-1)
            {
                m_stack.RemoveRange(m_position, m_stack.Count-m_position);
            }    
            else if (m_position == -1)
            {
                m_stack.Clear();
            }
            
            m_position++;
            m_stack.Add(itemData);

            if (m_PositionAdded != null)
            {
                m_PositionAdded(this, null);
            }

            Browse(itemData.Target.NodeId);
        }

        /// <see cref="BaseListCtrl.UpdateItem" />
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
            ItemData itemData = item as ItemData;

			if (itemData == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}

            listItem.Tag = item;
        }
		#endregion
    }
}
