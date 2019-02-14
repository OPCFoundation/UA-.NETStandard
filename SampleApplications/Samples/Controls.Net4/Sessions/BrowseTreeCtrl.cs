/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class BrowseTreeCtrl : Opc.Ua.Client.Controls.BaseTreeCtrl
    {
        #region Contructors
        public BrowseTreeCtrl()
        {
            InitializeComponent();
            m_references = new ReferenceDescriptionCollection();
            m_BrowserMoreReferences = new BrowserEventHandler(Browser_MoreReferences);
        }
        #endregion

        #region Private Fields
        private Browser m_browser;
        private NodeId m_rootId;
        private AttributeListCtrl m_AttributesCtrl;
        private bool m_allowPick;
        private bool m_showReferences;
        private TreeNode m_nodeToBrowse;
        private ReferenceDescription m_parent;
        private ReferenceDescriptionCollection m_references;
        private event NodesSelectedEventHandler m_ItemsSelected;
        private event MethodCalledEventHandler m_MethodCalled;
        private BrowserEventHandler m_BrowserMoreReferences;
        private SessionTreeCtrl m_SessionTreeCtrl;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// The control used to display the address space for a session.
        /// </summary>
        public SessionTreeCtrl SessionTreeCtrl
        {
            get { return m_SessionTreeCtrl;  }
            set { m_SessionTreeCtrl = value; }
        }

        /// <summary>
        /// Whether items can be picked in the control.
        /// </summary>
        [DefaultValue(false)]
        public bool AllowPick
        {
            get { return m_allowPick;  }
            set { m_allowPick = value; }
        }
        
        /// <summary>
        /// Whether references should be displayed in the control.
        /// </summary>
        [DefaultValue(false)]
        public bool ShowReferences
        {
            get { return m_showReferences;  }
            set { m_showReferences = value; }
        }
        
        /// <summary>
        /// Whether references should be displayed in the control.
        /// </summary>
        public ReferenceDescriptionCollection SelectedReferences
        {
            get 
            {     
                return m_references;
            }

            set 
            { 
                m_references = value;

                if (m_references == null)
                {
                    m_references = new ReferenceDescriptionCollection();
                }
            }
        }

        /// <summary>
        /// The control used to display the attributes for the currently selected node.
        /// </summary>
        public AttributeListCtrl AttributesCtrl
        {
            get { return m_AttributesCtrl;  }
            set { m_AttributesCtrl = value; }
        }
        
        /// <summary>
        /// Raised when nodes are selected in the control.
        /// </summary>
        public event NodesSelectedEventHandler ItemsSelected
        {
            add    { m_ItemsSelected += value; }
            remove { m_ItemsSelected -= value; }
        }

        /// <summary>
        /// Raised when a method is called.
        /// </summary>
        public event MethodCalledEventHandler MethodCalled
        {
            add    { m_MethodCalled += value; }
            remove { m_MethodCalled -= value; }
        }

        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            if (m_browser != null)
            {
                m_browser.MoreReferences -= m_BrowserMoreReferences;
            }

            NodesTV.Nodes.Clear();
        }

        /// <summary>
        /// Sets the root node for the control.
        /// </summary>
        public void SetRoot(Browser browser, NodeId rootId)
        {     
            Clear();
            
            ShowReferencesMI.Checked = m_showReferences;
            
            m_rootId  = rootId;
            m_browser = browser;

            if (m_browser != null)
            {
                m_browser.MoreReferences += m_BrowserMoreReferences;
            }

            // check if session is connected.
            if (m_browser == null || !m_browser.Session.Connected)
            {
                return;
            }

            if (NodeId.IsNull(rootId))
            {
                m_rootId = Objects.RootFolder;
            }

            if (m_browser != null)
            {
                INode node = m_browser.Session.NodeCache.Find(m_rootId);

                if (node == null)
                {
                    return;
                }

                ReferenceDescription reference = new ReferenceDescription();
                
                reference.ReferenceTypeId = ReferenceTypeIds.References;
                reference.IsForward       = true;
                reference.NodeId          = node.NodeId;
                reference.NodeClass       = (NodeClass)node.NodeClass;
                reference.BrowseName      = node.BrowseName;
                reference.DisplayName     = node.DisplayName;
                reference.TypeDefinition  = null;
                                
                string text = GetTargetText(reference);
                string icon = GuiUtils2.GetTargetIcon(m_browser.Session, reference);

                TreeNode root = AddNode(null, reference, text, icon);
                root.Nodes.Add(new TreeNode());
                root.Expand();
            }
        }
                
        /// <summary>
        /// Sets the root node for the control.
        /// </summary>
        public void SetRoot(Session session, NodeId rootId)
        {
            SetRoot(new Browser(session), rootId);
        }

        /// <summary>
        /// Sets the view for the control.
        /// </summary>
        public void SetView(Session session, BrowseViewType viewType, NodeId viewId)
        {            
            Clear();

            // check if session is connected.
            if (session == null || !session.Connected)
            {
                return;
            }

            Browser browser = new Browser(session);

            browser.BrowseDirection   = BrowseDirection.Forward;
            browser.ReferenceTypeId   = null;
            browser.IncludeSubtypes   = true;
            browser.NodeClassMask     = 0;
            browser.ContinueUntilDone = false;
            
            NodeId rootId = Objects.RootFolder;
            ShowReferences = false;

            switch (viewType)
            {
                case BrowseViewType.All:
                {
                    ShowReferences = true;
                    break;
                }

                case BrowseViewType.Objects:
                {
                    rootId = Objects.ObjectsFolder;
                    browser.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;
                    break;
                }

                case BrowseViewType.Types:
                {
                    rootId = Objects.TypesFolder;
                    browser.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;
                    break;
                }

                case BrowseViewType.ObjectTypes:
                {
                    rootId = ObjectTypes.BaseObjectType;
                    browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                    break;
                }

                case BrowseViewType.EventTypes:
                {
                    rootId = ObjectTypes.BaseEventType;
                    browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                    break;
                }

                case BrowseViewType.DataTypes:
                {
                    rootId = DataTypeIds.BaseDataType;
                    browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                    break;
                }

                case BrowseViewType.ReferenceTypes:
                {
                    rootId = ReferenceTypeIds.References;
                    browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                    break;
                }

                case BrowseViewType.ServerDefinedView:
                {
                    rootId = viewId;
                    browser.View = new ViewDescription();
                    browser.View.ViewId = viewId;
                    ShowReferences = true;
                    break;
                }
            }
            
            SetRoot(browser, rootId);
        }

        /// <summary>
        /// The root node where browsing should start.
        /// </summary>
        public NodeId RootId
        {
            get { return m_rootId; }
        }
        #endregion
        
        #region Overridden Members
        /// <see cref="BaseTreeCtrl.BeforeExpand" />
        protected override bool BeforeExpand(TreeNode clickedNode)
        {
            // check if a placeholder child is present.
            if (clickedNode.Nodes.Count == 1 && clickedNode.Nodes[0].Text == String.Empty)
            {
                // clear dummy children.
                clickedNode.Nodes.Clear();
                
                // do nothing if an error is detected.
                if (m_browser.Session.KeepAliveStopped)
                {
                    return false;
                }

                // browse.
                return Browse(clickedNode);
            }

            // do not cancel expand.
            return false;
        }

        /// <see cref="BaseTreeCtrl.EnableMenuItems" />
        protected override void EnableMenuItems(TreeNode clickedNode)
        {
            BrowseOptionsMI.Enabled   = true;
            ShowReferencesMI.Enabled  = true;    
            SelectMI.Visible          = m_allowPick;
            SelectSeparatorMI.Visible = m_allowPick;

            if (clickedNode != null)
            {
                // do nothing if an error is detected.
                if (m_browser.Session.KeepAliveStopped)
                {
                    return;
                }

                SelectMI.Enabled         = true;
                SelectItemMI.Enabled     = true;
                SelectChildrenMI.Enabled = clickedNode.Nodes.Count > 0;
                BrowseRefreshMI.Enabled  = true;

                ReferenceDescription reference = clickedNode.Tag as ReferenceDescription;

                if (reference != null)
                {
                    BrowseMI.Enabled         = (reference.NodeId != null && !reference.NodeId.IsAbsolute);
                    ViewAttributesMI.Enabled = true;

                    NodeId nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_browser.Session.NamespaceUris);

                    INode node = m_browser.Session.ReadNode(nodeId);
    
                    byte accessLevel = 0;
                    byte eventNotifier = 0;
                    bool executable = false;

                    VariableNode variableNode = node as VariableNode;

                    if (variableNode != null)
                    {
                        accessLevel = variableNode.UserAccessLevel;
                    }
                    
                    ObjectNode objectNode = node as ObjectNode;

                    if (objectNode != null)
                    {
                        eventNotifier = objectNode.EventNotifier;
                    }
                    
                    ViewNode viewNode = node as ViewNode;

                    if (viewNode != null)
                    {
                        eventNotifier = viewNode.EventNotifier;
                    }
                    
                    MethodNode methodNode = node as MethodNode;

                    if (methodNode != null)
                    {
                        executable = methodNode.UserExecutable;
                    }
                    
                    ReadMI.Visible         = false;
                    HistoryReadMI.Visible   = false;
                    WriteMI.Visible         = false;
                    HistoryUpdateMI.Visible = false;
                    EncodingsMI.Visible     = false;
                    SubscribeMI.Visible     = false;
                    CallMI.Visible          = false;

                    if (accessLevel != 0)
                    {
                        ReadMI.Visible          = true;
                        HistoryReadMI.Visible   = true;
                        WriteMI.Visible         = true;
                        HistoryUpdateMI.Visible = true;
                        EncodingsMI.Visible     = true;
                        SubscribeMI.Visible     = m_SessionTreeCtrl != null;

                        if ((accessLevel & (byte)AccessLevels.CurrentRead) != 0)
                        {
                            ReadMI.Enabled         = true;
                            EncodingsMI.Enabled    = true;
                            SubscribeMI.Enabled    = true;
                            SubscribeNewMI.Enabled = true;
                        }

                        if ((accessLevel & (byte)AccessLevels.CurrentWrite) != 0)
                        {
                            WriteMI.Enabled     = true;
                            EncodingsMI.Enabled = true;
                        }

                        if ((accessLevel & (byte)AccessLevels.HistoryRead) != 0)
                        {
                            HistoryReadMI.Enabled = true;
                        }

                        if ((accessLevel & (byte)AccessLevels.HistoryWrite) != 0)
                        {
                            HistoryUpdateMI.Enabled = true;
                        }
                    }
                    
                    if (eventNotifier != 0)
                    {
                        HistoryReadMI.Visible   = true;
                        HistoryUpdateMI.Visible = true;
                        SubscribeMI.Visible     = true;
                        
                        if ((eventNotifier & (byte)EventNotifiers.HistoryRead) != 0)
                        {
                            HistoryReadMI.Enabled = true;
                        }

                        if ((eventNotifier & (byte)EventNotifiers.HistoryWrite) != 0)
                        {
                            HistoryUpdateMI.Enabled = true;
                        }

                        SubscribeMI.Enabled    = (eventNotifier & (byte)EventNotifiers.SubscribeToEvents) != 0;
                        SubscribeNewMI.Enabled = SubscribeMI.Enabled;
                    }
                    
                    if (methodNode != null)
                    {
                        CallMI.Visible = true;
                        CallMI.Enabled = executable;
                    }                   
                    
                    if (variableNode != null && EncodingsMI.Enabled)
                    {
                        ReferenceDescriptionCollection encodings = m_browser.Session.ReadAvailableEncodings(variableNode.NodeId);

                        if (encodings.Count == 0)
                        {
                            EncodingsMI.Visible = false;
                        }
                    }

                    if (SubscribeMI.Enabled)
                    {
                        while (SubscribeMI.DropDown.Items.Count > 1)
                        {
                            SubscribeMI.DropDown.Items.RemoveAt(SubscribeMI.DropDown.Items.Count - 1);
                        }

                        foreach (Subscription subscription in m_browser.Session.Subscriptions)
                        {
                            if (subscription.Created)
                            {
                                ToolStripItem item = SubscribeMI.DropDown.Items.Add(subscription.DisplayName);
                                item.Click += new EventHandler(Subscription_Click);
                                item.Tag = subscription;
                            }
                        }
                    }
                }
            }
        }

        /// <see cref="BaseTreeCtrl.SelectNode" />
        protected override void SelectNode()
        {
            base.SelectNode();
            
            // check if node is selected.
            if (NodesTV.SelectedNode == null)
            {
                return;
            }
            
            m_parent = GetParentOfSelected();

            ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

            // update the attributes control.
            if (m_AttributesCtrl != null)
            {              
                if (reference != null)
                {
                    m_AttributesCtrl.Initialize(m_browser.Session, reference.NodeId);
                }
                else
                {
                    m_AttributesCtrl.Clear();
                }
            }
                        
            // check for single reference.
            if (reference != null)
            {
                m_references = new ReferenceDescription[] { reference };
                return;
            }
            
            // check if reference type folder is selected.
            NodeId referenceTypeId = NodesTV.SelectedNode.Tag as NodeId;

            if (referenceTypeId != null)
            {
                m_references = new ReferenceDescriptionCollection();

                foreach (TreeNode child in NodesTV.SelectedNode.Nodes)
                {                        
                    reference = child.Tag as ReferenceDescription;

                    if (reference != null)
                    {
                        m_references.Add(reference);
                    }
                }
            }      
        }
        #endregion
        
        #region Private Members
        /// <summary>
        /// Returns the parent node of the selected reference.
        /// </summary>
        private ReferenceDescription GetParentOfSelected()
        {
            if (NodesTV.SelectedNode.Parent != null)
            {
                if (NodesTV.SelectedNode.Parent.Tag is NodeId)
                {
                    if (NodesTV.SelectedNode.Parent.Parent != null)
                    {
                        return NodesTV.SelectedNode.Parent.Tag as ReferenceDescription;
                    }
                }
                else
                {
                    return NodesTV.SelectedNode.Parent.Tag as ReferenceDescription;
                }
            }

            return null;
        }

        /// <summary>
        /// Adds a item to a subscription.
        /// </summary>
        private void Subscribe(Subscription subscription, ReferenceDescription reference)
        {    
            MonitoredItem monitoredItem = new MonitoredItem(subscription.DefaultItem);

            monitoredItem.DisplayName      = subscription.Session.NodeCache.GetDisplayText(reference);
            monitoredItem.StartNodeId      = (NodeId)reference.NodeId;
            monitoredItem.NodeClass        = (NodeClass)reference.NodeClass;
            monitoredItem.AttributeId      = Attributes.Value;
            monitoredItem.SamplingInterval = 0;
            monitoredItem.QueueSize        = 1;

            // add condition fields to any event filter.
            EventFilter filter = monitoredItem.Filter as EventFilter;

            if (filter != null)
            {
                monitoredItem.AttributeId = Attributes.EventNotifier;
				monitoredItem.QueueSize = 0;
            }
            
            subscription.AddItem(monitoredItem);
            subscription.ApplyChanges();
        }

        /// <summary>
        /// Browses the server address space and adds the targets to the tree.
        /// </summary>
        private bool Browse(TreeNode node)
        {      
            // save node being browsed.
            m_nodeToBrowse = node;

            // find node to browse.
            ReferenceDescription reference = node.Tag as ReferenceDescription;

            if (reference == null || reference.NodeId == null || reference.NodeId.IsAbsolute)
            {
                return false;
            }
            
            // fetch references.
            ReferenceDescriptionCollection references = null;

            if (reference != null)
            {
                references = m_browser.Browse((NodeId)reference.NodeId);
            }
            else
            {
                references = m_browser.Browse(m_rootId);
            }
            
            // add nodes to tree.
            AddReferences(m_nodeToBrowse, references);

            return false;
        }

        /// <summary>
        /// Adds a target to the tree control.
        /// </summary>
        private void AddReferences(TreeNode parent, ReferenceDescriptionCollection references)
        {
            foreach (ReferenceDescription reference in references)
            {
                if (reference.ReferenceTypeId.IsNullNodeId)
                {
                    Utils.Trace("Reference {0} has null reference type id", reference.DisplayName);
                    continue;
                }

                ReferenceTypeNode typeNode = m_browser.Session.NodeCache.Find(reference.ReferenceTypeId) as ReferenceTypeNode;
                if (typeNode == null)
                {
                    Utils.Trace("Reference {0} has invalid reference type id.", reference.DisplayName);
                    continue;
                }

                if (m_browser.BrowseDirection == BrowseDirection.Forward && !reference.IsForward
                    || m_browser.BrowseDirection == BrowseDirection.Inverse && reference.IsForward)
                {
                    Utils.Trace("Reference's IsForward value is: {0}, but the browse direction is: {1}; for reference {2}", reference.IsForward, m_browser.BrowseDirection, reference.DisplayName);
                    continue;
                }

                if (reference.NodeId == null || reference.NodeId.IsNull)
                {
                    Utils.Trace("The node id of the reference {0} is NULL.", reference.DisplayName);
                    continue;
                }

                if (reference.BrowseName == null || reference.BrowseName.Name == null)
                {
                    Utils.Trace("Browse name is empty for reference {0}", reference.DisplayName);
                    continue;
                }

                if (!Enum.IsDefined(typeof(Opc.Ua.NodeClass), reference.NodeClass) || reference.NodeClass == NodeClass.Unspecified)
                {
                    Utils.Trace("Node class is an unknown or unspecified value, for reference {0}", reference.DisplayName);
                    continue;
                }
                
                if (m_browser.NodeClassMask != 0 && m_browser.NodeClassMask != 255)
                {
                    if (reference.TypeDefinition == null || reference.TypeDefinition.IsNull)
                    {
                        Utils.Trace("Type definition is null for reference {0}", reference.DisplayName);
                        continue;
                    }
                }

                // suppress duplicate references.
                if (!m_showReferences)
                {
                    bool exists = false;

                    foreach (TreeNode existingChild in parent.Nodes)
                    {
                        ReferenceDescription existingReference = existingChild.Tag as ReferenceDescription;

                        if (existingReference != null)
                        {
                            if (existingReference.NodeId == reference.NodeId)
                            {
                                exists = true;
                                break;
                            }
                        }
                    }

                    if (exists)
                    {
                        continue;
                    }
                }

                string text = GetTargetText(reference);
                string icon = GuiUtils2.GetTargetIcon(m_browser.Session, reference);

                TreeNode container = parent;

                if (m_showReferences)
                {
                    container = FindReferenceTypeContainer(parent, reference);
                }

                if (container != null)
                {
                    TreeNode child = AddNode(container, reference, text, icon);
                    child.Nodes.Add(new TreeNode());
                }
            }
        }
        
        /// <summary>
        /// Adds a container for the reference type to the tree control.
        /// </summary>
        private TreeNode FindReferenceTypeContainer(TreeNode parent, ReferenceDescription reference)
        {
            if (parent == null)
            {
                return null;
            }

            if (reference.ReferenceTypeId.IsNullNodeId)
            {
                Utils.Trace("NULL reference type id, for reference: {0}", reference.DisplayName);
                return null;
            }

            ReferenceTypeNode typeNode = m_browser.Session.NodeCache.Find(reference.ReferenceTypeId) as ReferenceTypeNode;

            foreach (TreeNode child in parent.Nodes)
            {
                NodeId referenceTypeId = child.Tag as NodeId;

                if (typeNode.NodeId == referenceTypeId)
                {
                    if (typeNode.InverseName == null)
                    {
                        return child;
                    }

                    if (reference.IsForward)
                    {
                        if (child.Text == typeNode.DisplayName.Text)
                        {
                            return child;
                        }
                    }
                    else
                    {
                        if (child.Text == typeNode.InverseName.Text)
                        {
                            return child;
                        }
                    }
                }
            }

            if (typeNode != null)
            {
                string text = typeNode.DisplayName.Text;
                string icon = "ReferenceType";

                if (!reference.IsForward && typeNode.InverseName != null)
                {
                    text = typeNode.InverseName.Text;
                }

                return AddNode(parent, typeNode.NodeId, text, icon);
            }

            Utils.Trace("Reference type id not found for: {0}", reference.ReferenceTypeId);

            return null;
        }
                
        /// <summary>
        /// Returns to display text for the target of a reference.
        /// </summary>
        public string GetTargetText(ReferenceDescription reference)
        {
            if (reference != null)
            {
                if (reference.DisplayName != null && !String.IsNullOrEmpty(reference.DisplayName.Text))
                {
                    return reference.DisplayName.Text;
                }

                if (reference.BrowseName != null)
                {
                    return reference.BrowseName.Name;
                }
            }

            return null;  
        }
        #endregion
        
        private void BrowseOptionsMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (new BrowseOptionsDlg().ShowDialog(m_browser))
                {                    
                    if (NodesTV.SelectedNode != null)
                    {
                        NodesTV.SelectedNode.Nodes.Clear();
                        Browse(NodesTV.SelectedNode);
                    }
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void BrowseRefreshMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode != null)
                {
                    NodesTV.SelectedNode.Nodes.Clear();
                    Browse(NodesTV.SelectedNode);
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void Browser_MoreReferences(Browser sender, BrowserEventArgs e)
        {
            try
            {
                AddReferences(m_nodeToBrowse, e.References);
                e.References.Clear();
                
                if (MessageBox.Show("More references exist. Continue?", "Browse", MessageBoxButtons.YesNo) == DialogResult.No)
                {
                    e.Cancel = true;
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SelectItemMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_ItemsSelected == null || NodesTV.SelectedNode == null)
                {
                    return;
                }

                if (m_references.Count > 0)
                {
                    m_ItemsSelected(this, new NodesSelectedEventArgs(m_parent.NodeId, m_references));
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        
        private void SelectChildrenMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_ItemsSelected == null || NodesTV.SelectedNode == null)
                {
                    return;
                }

                m_parent = GetParentOfSelected();
                m_references = new ReferenceDescriptionCollection();

                foreach (TreeNode child in NodesTV.SelectedNode.Nodes)
                {
                    ReferenceDescription reference = child.Tag as ReferenceDescription;

                    if (reference != null)
                    {
                        m_references.Add(reference);
                    }                    
                }

                if (m_references.Count > 0)
                {
                    m_ItemsSelected(this, new NodesSelectedEventArgs(m_parent.NodeId, m_references));
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void ShowReferencesMI_CheckedChanged(object sender, EventArgs e)
        {
            m_showReferences = ShowReferencesMI.Checked;
            SetRoot(m_browser, m_rootId);
        }

        private void ViewAttributesMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }
                    
                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;
                
                if (reference == null)
                {
                    return;
                }
                
                new NodeAttributesDlg().ShowDialog(m_browser.Session, reference.NodeId);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void CallMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                Session session = m_browser.Session;
                                    
                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;
                
                if (reference == null || reference.NodeClass != NodeClass.Method)
                {
                    return;
                }

                NodeId methodId = (NodeId)reference.NodeId;
                                
                reference = NodesTV.SelectedNode.Parent.Tag as ReferenceDescription;
                
                if (reference == null)
                {
                    reference = NodesTV.SelectedNode.Parent.Parent.Tag as ReferenceDescription;

                    if (reference == null)
                    {
                        return;
                    }
                }
                                
                NodeId objectId = (NodeId)reference.NodeId;

                if (m_MethodCalled != null)
                {
                    MethodCalledEventArgs args = new MethodCalledEventArgs(m_browser.Session, objectId, methodId);
                    m_MethodCalled(this, args);

                    if (args.Handled)
                    {
                        return;
                    }
                }

                new CallMethodDlg().Show(m_browser.Session, objectId, methodId);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void ReadMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }
                                    
                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;
                
                if (reference == null || (reference.NodeClass & (NodeClass.Variable | NodeClass.VariableType)) == 0)
                {
                    return;
                }

                Session session = m_browser.Session;

                // build list of nodes to read.
                ReadValueIdCollection valueIds = new ReadValueIdCollection();

                ReadValueId valueId = new ReadValueId();

                valueId.NodeId       = (NodeId)reference.NodeId;
                valueId.AttributeId  = Attributes.Value;
                valueId.IndexRange   = null;
                valueId.DataEncoding = null;

                valueIds.Add(valueId);

                // show form.
                new ReadDlg().Show(session, valueIds);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void WriteMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }
                                    
                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;
                
                if (reference == null || (reference.NodeClass & (NodeClass.Variable | NodeClass.VariableType)) == 0)
                {
                    return;
                }

                Session session = m_browser.Session;

                // build list of nodes to read.
                WriteValueCollection values = new WriteValueCollection();

                WriteValue value = new WriteValue();

                value.NodeId      = (NodeId)reference.NodeId;
                value.AttributeId = Attributes.Value;
                value.IndexRange  = null;
                value.Value       = null;

                values.Add(value);

                // show form.
                new WriteDlg().Show(session, values);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SubscribeNewMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                if (reference == null)
                {
                    return;
                }

                if (m_SessionTreeCtrl != null)
                {                    
                    Subscription subscription = m_SessionTreeCtrl.CreateSubscription(m_browser.Session);

                    if (subscription != null)
                    {
                        Subscribe(subscription, reference);
                    }
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        void Subscription_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                if (reference == null)
                {
                    return;
                }
                    
                Subscription subscription = ((ToolStripItem)sender).Tag as Subscription;

                if (subscription != null)
                {
                    Subscribe(subscription, reference);
                }               
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void EncodingsMI_Click(object sender, EventArgs e)
        {           
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                if (reference == null || (reference.NodeClass & NodeClass.Variable) == 0)
                {
                    return;
                }
                                
                new DataEncodingDlg().ShowDialog(m_browser.Session, (NodeId)reference.NodeId);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void HistoryReadMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                if (reference == null || reference.NodeId == null || reference.NodeId.IsAbsolute)
                {
                    return;
                }

                new ReadHistoryDlg().ShowDialog(m_browser.Session, (NodeId)reference.NodeId);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void BrowseMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedNode == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedNode.Tag as ReferenceDescription;

                if (reference == null || reference.NodeId == null || reference.NodeId.IsAbsolute)
                {
                    return;
                }

                new BrowseDlg().Show(m_browser.Session, (NodeId)reference.NodeId);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
    }
    
    #region NodesSelectedEventArgs Class
    /// <summary>
    /// The event arguments provided nodes are picked in the dialog.
    /// </summary>
    public class NodesSelectedEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal NodesSelectedEventArgs(ExpandedNodeId sourceId, ReferenceDescriptionCollection references)
        {
            m_sourceId   = sourceId;
            m_references = references;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The source of the references that were picked.
        /// </summary>
        public ExpandedNodeId SourceId
        {
            get { return m_sourceId; }
        }

        /// <summary>
        /// The references that were picked in the dialog.
        /// </summary>
        public IEnumerable<ReferenceDescription> References
        {
            get { return m_references; }
        }
        #endregion
        
        #region Private Fields
        private ExpandedNodeId m_sourceId;
        private ReferenceDescriptionCollection m_references;
        #endregion
    }

    /// <summary>
    /// The delegate used to receive notifications when nodes are picked in the dialog.
    /// </summary>
    public delegate void NodesSelectedEventHandler(object sender, NodesSelectedEventArgs e);
    #endregion   
    
    #region BrowseViewType Enumeration
    /// <summary>
    /// The type views that can be used when browsing the address space. 
    /// </summary>
    public enum BrowseViewType
    {
        /// <summary>
        /// All nodes and references in the address space.
        /// </summary>
        All,

        /// <summary>
        /// The object instance hierarchy.
        /// </summary>
        Objects,

        /// <summary>
        /// The type hierarchies.
        /// </summary>
        Types,

        /// <summary>
        /// The object type hierarchies.
        /// </summary>
        ObjectTypes,

        /// <summary>
        /// The event type hierarchies.
        /// </summary>
        EventTypes,

        /// <summary>
        /// The data type hierarchies.
        /// </summary>
        DataTypes,

        /// <summary>
        /// The reference type hierarchies.
        /// </summary>
        ReferenceTypes,

        /// <summary>
        /// A server defined view.
        /// </summary>
        ServerDefinedView
    }
    #endregion 

    
    #region MethodCalledEventArgs Class
    /// <summary>
    /// The event arguments provided nodes are picked in the dialog.
    /// </summary>
    public class MethodCalledEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal MethodCalledEventArgs(Session session, NodeId objectId, NodeId methodId)
        {
            m_session  = session;
            m_objectId = objectId;
            m_methodId = methodId;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The session 
        /// </summary>
        public Session Session
        {
            get { return m_session; }
        }

        /// <summary>
        /// The NodeId of the Object with a method.
        /// </summary>
        public NodeId ObjectId
        {
            get { return m_objectId; }
        }

        /// <summary>
        /// The NodeId of the Method to call.
        /// </summary>
        public NodeId MethodId
        {
            get { return m_methodId; }
        }

        /// <summary>
        /// Whether the method call was handled.
        /// </summary>
        public bool Handled
        {
            get { return m_handled;  }
            set { m_handled = value; }
        }
        #endregion
        
        #region Private Fields
        private Session m_session;
        private NodeId m_objectId;
        private NodeId m_methodId;
        private bool m_handled;
        #endregion
    }

    /// <summary>
    /// The delegate used to receive notifications when nodes are picked in the dialog.
    /// </summary>
    public delegate void MethodCalledEventHandler(object sender, MethodCalledEventArgs e);
    #endregion   
}
