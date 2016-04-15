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
using Opc.Ua.Client.Controls;
using WinRTXamlToolkit.Controls;
using Windows.UI.Popups;
using Windows.UI.Core;
using System.Threading.Tasks;

namespace Opc.Ua.Sample.Controls
{
    public partial class BrowseTreeCtrl : BaseTreeCtrl
    {
        #region Constructors
        public BrowseTreeCtrl()
        {
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
        private TreeItemViewModel m_nodeToBrowse;
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

        internal void Back()
        {
            throw new NotImplementedException();
        }

        internal void Forward()
        {
            throw new NotImplementedException();
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
        new public void Clear()
        {
            if (m_browser != null)
            {
                m_browser.MoreReferences -= m_BrowserMoreReferences;
            }

            base.Clear();
        }

        internal void SetPosition(int selectedIndex)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Sets the root node for the control.
        /// </summary>
        public void SetRoot(Browser browser, NodeId rootId)
        {     
            Clear();
            
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

                TreeItemViewModel root = AddNode(null, reference, text, String.Empty);
                // trigger browse operation on the node
                TreeItemViewModel child = new TreeItemViewModel(NodesTV,root);
                root.Children.Add(child);
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
        protected override async Task BeforeExpand(TreeItemViewModel clickedNode)
        {
            // check if a placeholder child is present.
            if (clickedNode.Children.Count == 1 && String.IsNullOrEmpty(((TreeItemViewModel)clickedNode.Children[0]).Text))
            {
                // do nothing if an error is detected.
                if (!m_browser.Session.KeepAliveStopped)
                {
                    await Dispatcher.RunAsync(
                        CoreDispatcherPriority.Low,
                        () =>
                        {
                            // clear dummy children.
                            clickedNode.Children.Clear();

                            // browse.
                            Browse(clickedNode);

                        });
                }
            }
        }

        /// <see cref="BaseTreeCtrl.EnableMenuItems" />
        protected override void EnableMenuItems(TreeItemViewModel clickedNode)
        {
            // Context menu is currently not implemented
        }

        /// <see cref="BaseTreeCtrl.SelectNode" />
        protected override void SelectNode()
        {
            base.SelectNode();
            
            // check if node is selected.
            if (NodesTV.SelectedItem == null)
            {
                return;
            }
            
            m_parent = GetParentOfSelected();

            ReferenceDescription reference = NodesTV.SelectedItem.Item as ReferenceDescription;

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
            NodeId referenceTypeId = NodesTV.SelectedItem.Item as NodeId;

            if (referenceTypeId != null)
            {
                m_references = new ReferenceDescriptionCollection();

                foreach (TreeItemViewModel child in NodesTV.SelectedItem.Children)
                {                        
                    reference = child.Item as ReferenceDescription;

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
            if (NodesTV.SelectedItem.Parent != null)
            {
                if (NodesTV.SelectedItem.Parent.Item is NodeId)
                {
                    if (NodesTV.SelectedItem.Parent.Parent != null)
                    {
                        return NodesTV.SelectedItem.Parent.Item as ReferenceDescription;
                    }
                }
                else
                {
                    return NodesTV.SelectedItem.Parent.Item as ReferenceDescription;
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
            monitoredItem.SamplingInterval = -1;

            // add condition fields to any event filter.
            EventFilter filter = monitoredItem.Filter as EventFilter;

            if (filter != null)
            {
                monitoredItem.AttributeId = Attributes.EventNotifier;
            }
            
            subscription.AddItem(monitoredItem);
            subscription.ApplyChanges();
        }

        /// <summary>
        /// Browses the server address space and adds the targets to the tree.
        /// </summary>
        private bool Browse(TreeItemViewModel node)
        {      
            // save node being browsed.
            m_nodeToBrowse = node;

            // find node to browse.
            ReferenceDescription reference = node.Item as ReferenceDescription;

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
        private void AddReferences(TreeItemViewModel parent, ReferenceDescriptionCollection references)
        {
            foreach (ReferenceDescription reference in references)
            {
                // supress duplicate references.
                if (!m_showReferences)
                {
                    bool exists = false;

                    foreach (TreeItemViewModel existingChild in parent.Children)
                    {
                        ReferenceDescription existingReference = existingChild.Item as ReferenceDescription;

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

                TreeItemViewModel container = parent;

                if (m_showReferences)
                {
                    container = FindReferenceTypeContainer(parent, reference);
                }

                TreeItemViewModel child = AddNode(container, reference, text, String.Empty);
                child.Children.Add(new TreeItemViewModel(NodesTV,child));
            }
        }
        
        /// <summary>
        /// Adds a container for the reference type to the tree control.
        /// </summary>
        private TreeItemViewModel FindReferenceTypeContainer(TreeItemViewModel parent, ReferenceDescription reference)
        {
            if (parent == null)
            {
                return null;
            }

            ReferenceTypeNode typeNode = m_browser.Session.NodeCache.Find(reference.ReferenceTypeId) as ReferenceTypeNode;

            foreach (TreeItemViewModel child in parent.Children)
            {
                NodeId referenceTypeId = child.Item as NodeId;

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

            string text = typeNode.DisplayName.Text;
            string icon = "ReferenceType";

            if (!reference.IsForward && typeNode.InverseName != null)
            {
                text = typeNode.InverseName.Text;
            }

            return AddNode(parent, typeNode.NodeId, text, icon);
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
                    if (NodesTV.SelectedItem != null)
                    {
                        ((TreeItemViewModel) NodesTV.SelectedItem).Children.Clear();
                        Browse((TreeItemViewModel) NodesTV.SelectedItem);
                    }
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void BrowseRefreshMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedItem != null)
                {
                    ((TreeItemViewModel) NodesTV.SelectedItem).Children.Clear();
                    Browse((TreeItemViewModel) NodesTV.SelectedItem);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private async void Browser_MoreReferences(Browser sender, BrowserEventArgs e)
        {
            try
            {
                AddReferences(m_nodeToBrowse, e.References);
                e.References.Clear();
                MessageDlg dialog = new MessageDlg("More references exist. Continue?", MessageDlgButton.Yes, MessageDlgButton.No);
                MessageDlgButton result = await dialog.ShowAsync();
                if (result != MessageDlgButton.Yes)
                {
                    e.Cancel = true;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void SelectItemMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_ItemsSelected == null || NodesTV.SelectedItem == null)
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
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
        
        private void SelectChildrenMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_ItemsSelected == null || NodesTV.SelectedItem == null)
                {
                    return;
                }

                m_parent = GetParentOfSelected();
                m_references = new ReferenceDescriptionCollection();

                foreach (TreeItemViewModel child in NodesTV.SelectedItem.Children)
                {
                    ReferenceDescription reference = child.Item as ReferenceDescription;

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
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ShowReferencesMI_CheckedChanged(object sender, EventArgs e)
        {
            SetRoot(m_browser, m_rootId);
        }

        private void ViewAttributesMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedItem == null)
                {
                    return;
                }
                    
                ReferenceDescription reference = NodesTV.SelectedItem.Item as ReferenceDescription;
                
                if (reference == null)
                {
                    return;
                }
                
                //new NodeAttributesDlg().ShowDialog(m_browser.Session, reference.NodeId);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void CallMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedItem == null)
                {
                    return;
                }

                Session session = m_browser.Session;
                                    
                ReferenceDescription reference = NodesTV.SelectedItem.Item as ReferenceDescription;
                
                if (reference == null || reference.NodeClass != NodeClass.Method)
                {
                    return;
                }

                NodeId methodId = (NodeId)reference.NodeId;
                                
                reference = NodesTV.SelectedItem.Parent.Item as ReferenceDescription;
                
                if (reference == null)
                {
                    reference =NodesTV.SelectedItem.Parent.Parent.Item as ReferenceDescription;

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

                //new CallMethodDlg().Show(m_browser.Session, objectId, methodId);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ReadMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedItem == null)
                {
                    return;
                }
                                    
                ReferenceDescription reference = NodesTV.SelectedItem.Item as ReferenceDescription;
                
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
                //new ReadDlg().Show(session, valueIds);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void WriteMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedItem == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedItem.Item as ReferenceDescription;
                
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
                //new WriteDlg().Show(session, values);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void SubscribeNewMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedItem == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedItem.Item as ReferenceDescription;

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
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        void Subscription_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedItem == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedItem.Item as ReferenceDescription;

                if (reference == null)
                {
                    return;
                }
                    
                Subscription subscription = sender as Subscription;

                if (subscription != null)
                {
                    Subscribe(subscription, reference);
                }               
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void EncodingsMI_Click(object sender, EventArgs e)
        {           
            try
            {
                if (NodesTV.SelectedItem == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedItem.Item as ReferenceDescription;

                if (reference == null || (reference.NodeClass & NodeClass.Variable) == 0)
                {
                    return;
                }
                                
                //new DataEncodingDlg().ShowDialog(m_browser.Session, (NodeId)reference.NodeId);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void HistoryReadMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedItem == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedItem.Item as ReferenceDescription;

                if (reference == null || reference.NodeId == null || reference.NodeId.IsAbsolute)
                {
                    return;
                }

                new ReadHistoryDlg().ShowDialog(m_browser.Session, (NodeId)reference.NodeId);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void BrowseMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (NodesTV.SelectedItem == null)
                {
                    return;
                }

                ReferenceDescription reference = NodesTV.SelectedItem.Item as ReferenceDescription;

                if (reference == null || reference.NodeId == null || reference.NodeId.IsAbsolute)
                {
                    return;
                }

                new BrowseDlg().Show(m_browser.Session, (NodeId)reference.NodeId);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
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
