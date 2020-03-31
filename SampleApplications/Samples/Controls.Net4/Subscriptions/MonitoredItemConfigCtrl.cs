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
    public partial class MonitoredItemConfigCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public MonitoredItemConfigCtrl()
        {
            InitializeComponent();
            SetColumns(m_ColumnNames);
            m_dialogs = new Dictionary<uint,MonitoredItemDlg>();
        }
		#endregion

        #region Private Fields
        private Subscription m_subscription;
        private Dictionary<uint,MonitoredItemDlg> m_dialogs;
        private bool m_batchUpdates;
                
        /// <summary>
		/// The columns to display in the control.
		/// </summary>
		private readonly object[][] m_ColumnNames = new object[][]
		{
			new object[] { "ID",                        HorizontalAlignment.Center, null       },
			new object[] { "Name",                      HorizontalAlignment.Left,   null       },
			new object[] { "Class",                     HorizontalAlignment.Left,   "Variable" },
			new object[] { "Node ID",                   HorizontalAlignment.Left,   null       },
			new object[] { "Path",                      HorizontalAlignment.Left,   ""         }, 
			new object[] { "Attribute",                 HorizontalAlignment.Left,   "Value"    },
			new object[] { "Indexes",                   HorizontalAlignment.Left,   ""         }, 
			new object[] { "Encoding",                  HorizontalAlignment.Left,   ""         }, 
			new object[] { "Mode",                      HorizontalAlignment.Left,   null       },
			new object[] { "Sampling Interval",         HorizontalAlignment.Center, null       }, 
            new object[] { "Revised Sampling Interval", HorizontalAlignment.Center, null       }, 
            new object[] { "Queue Size",                HorizontalAlignment.Center, null       },
            new object[] { "Revised Queue Size",        HorizontalAlignment.Center, null        },
			new object[] { "Discard Oldest",            HorizontalAlignment.Center, "True"     }, 
			new object[] { "Status",                    HorizontalAlignment.Left,   ""         }, 
		};
		#endregion

        #region Public Interface
        /// <summary>
        /// Whether changes to items should be applied immediately.
        /// </summary>
        [DefaultValue(false)]
        public bool BatchUpdates
        {
            get { return m_batchUpdates;  }
            set { m_batchUpdates = value; }  
        }

        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            ItemsLV.Items.Clear();
            AdjustColumns();
        }

        /// <summary>
        /// Displays the items for the specified subscription in the control.
        /// </summary>
        public void Initialize(Subscription subscription)
        {
            // do nothing if same subscription provided.
            if (Object.ReferenceEquals(m_subscription, subscription))
            {
                return;
            }

            m_subscription = subscription;
            
            Clear();
            UpdateItems();
        }

        /// <summary>
        /// Called when the subscription changes.
        /// </summary>
        public void SubscriptionChanged(SubscriptionStateChangedEventArgs e)
        {
            UpdateItems();

            // close any monitoring windows.
            if ((e.Status | SubscriptionChangeMask.ItemsDeleted) != 0)
            {
                List<MonitoredItemDlg> dialogsToClose = new List<MonitoredItemDlg>();

                foreach (KeyValuePair<uint,MonitoredItemDlg> current in m_dialogs)
                {
                    if (m_subscription.FindItemByClientHandle(current.Key) == null)
                    {
                        dialogsToClose.Add(current.Value);
                    }
                }
                
                // this invokes a callback which will remove the dialog from the table.
                foreach (MonitoredItemDlg dialog in dialogsToClose)
                {
                    dialog.Close();
                }
            }
        }

        /// <summary>
        /// Creates a new group item.
        /// </summary>
        public MonitoredItem CreateItem(Subscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException("subscription");

            MonitoredItem monitoredItem = new MonitoredItem(subscription.DefaultItem);
            monitoredItem.QueueSize = 1;

            if (!new MonitoredItemEditDlg().ShowDialog(subscription.Session, monitoredItem))
            {
                return null;
            }

            subscription.AddItem(monitoredItem);
            subscription.ChangesCompleted();

            return monitoredItem;
        }

        /// <summary>
        /// Refreshes the state of all items displayed in the control.
        /// </summary>
        public void UpdateItems()
        {
            if (m_subscription != null)
            {
                BeginUpdate();

                foreach (MonitoredItem monitoredItem in m_subscription.MonitoredItems)
                {
                    AddItem(monitoredItem);
                }

                EndUpdate();

                AdjustColumns();
            }
        }

        /// <summary>
        /// Returns the parent for the node.
        /// </summary>
        private Node FindParent(Node node)
        {
            IList<IReference> parents = node.ReferenceTable.Find(ReferenceTypeIds.Aggregates, true, true, m_subscription.Session.TypeTree);

            if (parents.Count > 0)
            {
                bool followToType = false;
                
                foreach (IReference parentReference in parents)
                {
                    Node parent = m_subscription.Session.NodeCache.Find(parentReference.TargetId) as Node;

                    if (followToType)
                    {
                        if (parent.NodeClass == NodeClass.VariableType || parent.NodeClass == NodeClass.ObjectType)
                        {
                            return parent;
                        }
                    }
                    else
                    {
                        return parent;
                    }
                }
            }

            return null;
        }
        
        /// <summary>
        /// Creates an item from a reference.
        /// </summary>
        public void AddItem(ReferenceDescription reference)
        {
            if (reference == null)
            {
                return;
            }

            Node node = m_subscription.Session.NodeCache.Find(reference.NodeId) as Node;

            if (node == null)
            {
                return;
            }

            Node parent = null;

            // if the NodeId is of type string and contains '.' do not use relative paths
            if (node.NodeId.IdType != IdType.String || (node.NodeId.Identifier.ToString().IndexOf('.') == -1 && node.NodeId.Identifier.ToString().IndexOf('/') == -1))
            {
                parent = FindParent(node);
            }

            MonitoredItem monitoredItem = new MonitoredItem(m_subscription.DefaultItem);

            if (parent != null)
            {
                monitoredItem.DisplayName = String.Format("{0}.{1}", parent, node);
            }
            else
            {
                monitoredItem.DisplayName = String.Format("{0}", node);
            }
             
            monitoredItem.StartNodeId = node.NodeId;
            monitoredItem.NodeClass   = node.NodeClass;

            if (parent != null)
            {
                List<Node> parents = new List<Node>();
                parents.Add(parent);

                while (parent.NodeClass != NodeClass.ObjectType && parent.NodeClass != NodeClass.VariableType)
                {
                    parent = FindParent(parent);

                    if (parent == null)
                    {
                        break;
                    }
                        
                    parents.Add(parent);
                }
                
                monitoredItem.StartNodeId = parents[parents.Count-1].NodeId;

                StringBuilder relativePath = new StringBuilder();

                for (int ii = parents.Count-2; ii >= 0; ii--)
                {
                    relativePath.AppendFormat(".{0}", parents[ii].BrowseName);
                }

                relativePath.AppendFormat(".{0}", node.BrowseName);

                monitoredItem.RelativePath = relativePath.ToString();
            }

            Session session = m_subscription.Session;

            if (node.NodeClass == NodeClass.Object || node.NodeClass == NodeClass.Variable)
            {
                node.Find(ReferenceTypeIds.HasChild, true);

            }

            m_subscription.AddItem(monitoredItem);
        }
        
        /// <summary>
        /// Apply any changes to the set of items.
        /// </summary>
        public void ApplyChanges(bool force)
        {
            if (m_batchUpdates && !force)
            {
                return;
            }

            if (m_subscription != null)
            {
                m_subscription.ApplyChanges();

                foreach (ListViewItem listItem in ItemsLV.Items)
                {
                    UpdateItem(listItem, listItem.Tag);
                }

                AdjustColumns();
            }
        }
        
        /// <summary>
        /// Closes all dialogs when the containing form closes.
        /// </summary>
        public void FormClosing()
        {
            List<MonitoredItemDlg> dialogsToClose = new List<MonitoredItemDlg>();

            foreach (KeyValuePair<uint,MonitoredItemDlg> current in m_dialogs)
            {
                dialogsToClose.Add(current.Value);
            }
            
            // this invokes a callback which will remove the dialog from the table.
            foreach (MonitoredItemDlg dialog in dialogsToClose)
            {
                dialog.Close();
            }
        }        
        #endregion
        
        #region Overridden Methods
        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
		{
            if (m_subscription != null)
            {
                NewMI.Enabled                 = true;
                EditMI.Enabled                = ItemsLV.SelectedItems.Count == 1;
                DeleteMI.Enabled              = ItemsLV.SelectedItems.Count > 0;
                SetMonitoringModeMI.Enabled   = ItemsLV.SelectedItems.Count > 0;
                SetFilterMI.Enabled           = ItemsLV.SelectedItems.Count == 1;
                SetSamplingIntervalMI.Enabled = ItemsLV.SelectedItems.Count == 1;
                MonitorMI.Enabled             = ItemsLV.SelectedItems.Count == 1;
            }
		}
                
        /// <see cref="BaseListCtrl.PickItems" />
        protected override void PickItems()
        {
            base.PickItems();
            MonitorMI_Click(this, null);
        }

        /// <see cref="BaseListCtrl.UpdateItem" />
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
			MonitoredItem monitoredItem = item as MonitoredItem;

			if (monitoredItem == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}
            
		    listItem.SubItems[0].Text  = String.Format("{0}", monitoredItem.Status.Id);
		    listItem.SubItems[1].Text  = String.Format("{0}", monitoredItem.DisplayName);
		    listItem.SubItems[2].Text  = String.Format("{0}", monitoredItem.NodeClass);
		    listItem.SubItems[3].Text  = String.Format("{0}", monitoredItem.StartNodeId);
		    listItem.SubItems[4].Text  = String.Format("{0}", monitoredItem.RelativePath);
		    listItem.SubItems[5].Text  = String.Format("{0}", Attributes.GetBrowseName(monitoredItem.AttributeId));
		    listItem.SubItems[6].Text  = String.Format("{0}", monitoredItem.IndexRange);
		    listItem.SubItems[7].Text  = String.Format("{0}", monitoredItem.Encoding);
		    listItem.SubItems[8].Text  = String.Format("{0}", monitoredItem.MonitoringMode);
		    listItem.SubItems[9].Text  = String.Format("{0}", monitoredItem.SamplingInterval);

            double revisedSampingInterval = monitoredItem.Created ? monitoredItem.Status.SamplingInterval : (double)0.0;

            listItem.SubItems[10].Text = String.Format("{0}", revisedSampingInterval);
            listItem.SubItems[11].Text = String.Format("{0}", monitoredItem.QueueSize);

            uint revisedQueueSize = monitoredItem.Created ? monitoredItem.Status.QueueSize : 0;

            listItem.SubItems[12].Text = String.Format("{0}", revisedQueueSize);
		    listItem.SubItems[13].Text = String.Format("{0}", monitoredItem.DiscardOldest);
            listItem.SubItems[14].Text = String.Format("{0}", monitoredItem.Status.Error);
 
            listItem.ForeColor = Color.Gray;

            if (monitoredItem.Status.Created)
            {
                listItem.ForeColor = Color.Empty;

                if ((revisedQueueSize != monitoredItem.QueueSize) && monitoredItem.AttributeId != Opc.Ua.Attributes.EventNotifier )                   
                {
                    listItem.ForeColor = Color.DarkOrange;
                }

                if ((revisedSampingInterval != monitoredItem.SamplingInterval) && monitoredItem.AttributeId != Opc.Ua.Attributes.EventNotifier)
                {
                    listItem.ForeColor = Color.DarkOrange;
                }
            }

            if (monitoredItem.Status.Id == 0)
            {
                listItem.ForeColor = Color.DarkOrange;
            }

            if (monitoredItem.Status.Error != null && ServiceResult.IsBad(monitoredItem.Status.Error))
            {
                listItem.ForeColor = Color.Red;
            }

            if (monitoredItem.AttributesModified)
            {
                listItem.ForeColor = Color.Red;
            }

			listItem.Tag = item;
        }

        /// <summary>
        /// Handles a drop event.
        /// </summary>
        protected override void ItemsLV_DragDrop(object sender, DragEventArgs e)
        {            
            try
            {
                ReferenceDescription reference = e.Data.GetData(typeof(ReferenceDescription)) as ReferenceDescription;

                if (reference == null)
                {
                    return;
                }
                    
                AddItem(reference);
                AdjustColumns();
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
		#endregion
        
        #region Event Handlers
        private void NewMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_subscription == null)
                {
                    return;
                }

                CreateItem(m_subscription);
                ApplyChanges(false);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void EditMI_Click(object sender, EventArgs e)
        {
            try
            {                
                MonitoredItem monitoredItem = SelectedTag as MonitoredItem;

                if (monitoredItem == null)
                {
                    return;
                }

                if (m_subscription == null)
                {
                    return;
                }

                if (!new MonitoredItemEditDlg().ShowDialog(m_subscription.Session, monitoredItem, true))
                {
                    return;
                }
                
                ApplyChanges(false);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void DeleteMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_subscription == null)
                {
                    return;
                }

                MonitoredItem[] monitoredItems = (MonitoredItem[])GetSelectedItems(typeof(MonitoredItem));

                if (monitoredItems.Length > 0)
                {
                    m_subscription.RemoveItems(monitoredItems);
                }

                IList<MonitoredItem> deletedItems = m_subscription.DeleteItems();

                string errorString = string.Empty;

                foreach(MonitoredItem item in deletedItems)
                {
                    if (item.Status.Error != null && ServiceResult.IsBad(item.Status.Error))
                    {
                        errorString += System.Environment.NewLine + item.Status.Error; 
                    }
                }

                foreach (ListViewItem listItem in ItemsLV.Items)
                {
                    UpdateItem(listItem, listItem.Tag);
                }

                AdjustColumns();

                if (!String.IsNullOrEmpty(errorString))
                {
                    throw new Exception(String.Format("DeleteMonitoredItems error: {0}", errorString));
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SetMonitoringModeMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_subscription == null)
                {
                    return;
                }

                MonitoredItem[] monitoredItems = (MonitoredItem[])GetSelectedItems(typeof(MonitoredItem));

                if (monitoredItems.Length > 0)
                {
                    MonitoringMode monitoringMode = monitoredItems[0].MonitoringMode;

                    if (!new SetMonitoringModeDlg().ShowDialog(ref monitoringMode))
                    {
                        return;
                    }

                    List<ServiceResult> errors = m_subscription.SetMonitoringMode(monitoringMode, monitoredItems);

                    if (errors != null)
                    {
                        string errorString = string.Empty;

                        foreach (ServiceResult result in errors)
                        {
                            errorString += System.Environment.NewLine + result; 
                        }

                        throw new Exception(String.Format("SetMonitoringMode error: {0}", errorString));
                    }
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SetFilterMI_Click(object sender, EventArgs e)
        {     
            try
            {
                if (m_subscription == null)
                {
                    return;
                }

                MonitoredItem[] monitoredItems = (MonitoredItem[])GetSelectedItems(typeof(MonitoredItem));

                if (monitoredItems.Length == 1)
                {
                    if (monitoredItems[0].NodeClass == NodeClass.Variable || monitoredItems[0].NodeClass == NodeClass.VariableType)
                    {
                        if (!new DataChangeFilterEditDlg().ShowDialog(m_subscription.Session, monitoredItems[0]))
                        {
                            return;
                        }
                    }
                    else
                    {
                        EventFilter filter = new EventFilterDlg().ShowDialog(m_subscription.Session, monitoredItems[0].Filter as EventFilter, false);

                        if (filter == null)
                        {
                            return;
                        }

                        monitoredItems[0].Filter = filter;
                    }

                    m_subscription.ModifyItems();
                    ApplyChanges(false);
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }        

        private void MonitorMI_Click(object sender, EventArgs e)
        {
            try
            {
                MonitoredItem monitoredItem = SelectedTag as MonitoredItem;

                if (monitoredItem == null)
                {
                    return;
                }

                if (m_subscription == null)
                {
                    return;
                }

                MonitoredItemDlg dialog = null;

                if (!m_dialogs.TryGetValue(monitoredItem.ClientHandle, out dialog))
                {
                    m_dialogs[monitoredItem.ClientHandle] = dialog = new MonitoredItemDlg();
                    dialog.FormClosing += new FormClosingEventHandler(MonitoredItemDlg_FormClosing);
                }             

                dialog.Show(monitoredItem);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        void MonitoredItemDlg_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                foreach (KeyValuePair<uint,MonitoredItemDlg> current in m_dialogs)
                {
                    if (current.Value == sender)
                    {
                        m_dialogs.Remove(current.Key);
                        break;
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
