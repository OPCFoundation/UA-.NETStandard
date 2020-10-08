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
using System.Collections;
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
    /// A base class for list controls.
    /// </summary>
    public partial class BaseListCtrl : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BaseListCtrl"/> class.
        /// </summary>
        public BaseListCtrl()
        {
            InitializeComponent();

            ItemsLV.SmallImageList = new GuiUtils().ImageList;
            ItemsLV.ListViewItemSorter = new BaseListCtrlSorter(this);
        }

        /// <summary>
        /// The ListView contained in the control.
        /// </summary>
        protected System.Windows.Forms.ListView ItemsLV;

		#region Public Interface
		/// <summary>
		/// Whether the control should allow items to be dragged.
		/// </summary>
        [DefaultValue(false)]
		public bool EnableDragging
		{
			get { return m_enableDragging;  }
			set { m_enableDragging = value; }
		}

        /// <summary>
        /// The instructions to display when no items are in the list.
        /// </summary>
        public string Instructions
        {
            get { return m_instructions;  }
            set { m_instructions = value; }
        }

        /// <summary>
        /// Whether new items should be pre-pended to the list.
        /// </summary>
        [DefaultValue(false)]
        public bool PrependItems
        {
            get { return m_prependItems;  }
            set { m_prependItems = value; }
        }

		/// <summary>
		/// Raised whenever items are 'picked' in the control.
		/// </summary>
		public event ListItemActionEventHandler ItemsPicked
		{
			add    { m_ItemsPicked += value; }
			remove { m_ItemsPicked -= value; }
		}

		/// <summary>
		/// Raised whenever items are selected in the control.
		/// </summary>
		public event ListItemActionEventHandler ItemsSelected
		{
			add    { m_ItemsSelected += value; }
			remove { m_ItemsSelected -= value; }
		}

		/// <summary>
		/// Raised whenever items are added to the control.
		/// </summary>
		public event ListItemActionEventHandler ItemsAdded
		{
			add    { m_ItemsAdded += value; }
			remove { m_ItemsAdded -= value; }
		}

		/// <summary>
		/// Raised whenever items are modified in the control.
		/// </summary>
		public event ListItemActionEventHandler ItemsModified
		{
			add    { m_ItemsModified += value; }
			remove { m_ItemsModified -= value; }
		}

		/// <summary>
		/// Raised whenever items are removed from the control.
		/// </summary>
		public event ListItemActionEventHandler ItemsRemoved
		{
			add    { m_ItemsRemoved += value; }
			remove { m_ItemsRemoved -= value; }
		}

		/// <summary>
		/// Returns the number of items in the control.
		/// </summary>
		public int Count
		{
			get { return ItemsLV.Items.Count; }
		}

		/// <summary>
		/// Returns the objects associated with the items in the control.
		/// </summary>
		public Array GetItems(System.Type type)
		{
			ArrayList items = new ArrayList();

			foreach (ListViewItem listItem in ItemsLV.Items)
			{
				items.Add(listItem.Tag);
			}

			return items.ToArray(type);
		}

		/// <summary>
		/// Returns the objects associated with the selected items in the control.
		/// </summary>
		public Array GetSelectedItems(System.Type type)
		{
			ArrayList items = new ArrayList();

            if (ItemsLV.View == View.Details)
            {
                foreach (ListViewItem listItem in ItemsLV.SelectedItems)
                {
                    items.Add(listItem.Tag);
                }
            }

			return items.ToArray(type);
		}
		#endregion

		#region Private Members
        private bool m_prependItems;
		private event ListItemActionEventHandler m_ItemsPicked;
		private event ListItemActionEventHandler m_ItemsSelected;
		private event ListItemActionEventHandler m_ItemsAdded;
		private event ListItemActionEventHandler m_ItemsModified;
		private event ListItemActionEventHandler m_ItemsRemoved;
		private object[][] m_columns;
        private bool m_updating;
        private int m_updateCount;
        private string m_instructions;
        private Point m_dragPosition;
        private bool m_enableDragging;
		#endregion

		#region Protected Methods
        /// <summary>
        /// Returns tag of the selected item. Null if no items or more than one item is selected.
        /// </summary>
        protected object SelectedTag
        {
            get
            {
                if (ItemsLV.SelectedItems.Count != 1)
                {
                    return null;
                }

                return ItemsLV.SelectedItems[0].Tag;
            }
        }

		/// <summary>
		/// Deletes the currently selected items.
		/// </summary>
        protected virtual void DeleteSelection()
        {
            List<ListViewItem> itemsToDelete = new List<ListViewItem>();

            foreach (ListViewItem item in ItemsLV.SelectedItems)
            {
                itemsToDelete.Add(item);
            }

            foreach (ListViewItem item in itemsToDelete)
            {
                item.Remove();
            }
        }

		/// <summary>
		/// Compares two items in the list.
		/// </summary>
        protected virtual int CompareItems(object item1, object item2)
        {
            IComparable comparable = item1 as IComparable;

            if (comparable != null)
            {
                return comparable.CompareTo(item2);
            }

            return 0;
        }

        /// <summary>
        /// Returns the data to drag.
        /// </summary>
        protected virtual object GetDataToDrag()
        {
            if (ItemsLV.SelectedItems.Count > 0)
            {
                ArrayList data = new ArrayList();

                foreach (ListViewItem listItem in ItemsLV.SelectedItems)
                {
                    data.Add(listItem.Tag);
                }

                return data.ToArray();
            }

            return null;
        }

		/// <summary>
		/// Adds an item to the list.
		/// </summary>
        protected virtual ListViewItem AddItem(object item)
        {
            return AddItem(item, "SimpleItem", -1);
        }

		/// <summary>
		/// Adds an item to the list.
		/// </summary>
		protected virtual ListViewItem AddItem(object item, string icon, int index)
		{
            // switch to detail view as soon as an item is added.
            if (ItemsLV.View == View.List)
            {
                ItemsLV.Items.Clear();                
                ItemsLV.View = View.Details;
            }

            ListViewItem listItem = null;

            if (m_updating)
            {
                if (m_updateCount < ItemsLV.Items.Count)
                {
                    listItem = ItemsLV.Items[m_updateCount];
                }

                m_updateCount++;
            }
            
            if (listItem == null)
            {
                listItem = new ListViewItem();
            }

			listItem.Text     = String.Format("{0}", item);
			listItem.ImageKey = icon;
            listItem.Tag      = item;
            
			// fill columns with blanks.
            for (int ii = listItem.SubItems.Count; ii < ItemsLV.Columns.Count-1; ii++)
            {
                listItem.SubItems.Add(String.Empty);
            }
            
            // calculate new index.
            int newIndex = index;

            if (index < 0 || index > ItemsLV.Items.Count)
            {
                newIndex = ItemsLV.Items.Count;
            }

			// update columns.
			UpdateItem(listItem, item, newIndex);

            if (listItem.ListView == null)
            {
                // add to control.
                if (index >= 0 && index <= ItemsLV.Items.Count)
                {
                    ItemsLV.Items.Insert(index, listItem);
                }
                else
                {
                    ItemsLV.Items.Add(listItem);
                }
            }

            NotifyItemAdded(item);

			// return new item.
			return listItem;
		}

        /// <summary>
        /// Starts overwriting the contents of the control.
        /// </summary>
        protected void BeginUpdate()
        {
            m_updating = true;
            m_updateCount = 0;
        }
        
        /// <summary>
        /// Finishes overwriting the contents of the control.
        /// </summary>
        protected void EndUpdate()
        {
            m_updating = false;

            while (ItemsLV.Items.Count > m_updateCount)
            {
                ItemsLV.Items[ItemsLV.Items.Count-1].Remove();
            }

            m_updateCount = 0;
            AdjustColumns();
        }

		/// <summary>
		/// Updates a list item with the current contents of an object.
		/// </summary>
		protected virtual void UpdateItem(ListViewItem listItem, object item)
		{
			listItem.Tag = item;
		}
        
		/// <summary>
		/// Updates a list item with the current contents of an object.
		/// </summary>
		protected virtual void UpdateItem(ListViewItem listItem, object item, int index)
		{
			UpdateItem(listItem, item);
		}

		/// <summary>
		/// Sets the columns shown in the list view.
		/// </summary>
		protected virtual void SetColumns(object[][] columns)
		{		
			ItemsLV.Clear();

			m_columns = columns;

			foreach (object[] column in columns)
			{
				ColumnHeader header = new ColumnHeader();
				
				header.Text      = column[0] as string;
				header.TextAlign = (HorizontalAlignment)column[1];

				ItemsLV.Columns.Add(header);
			}

			ColumnHeader blank = new ColumnHeader();
			blank.Text = String.Empty;
			ItemsLV.Columns.Add(blank);

			AdjustColumns();
		}

		/// <summary>
		/// Adjusts the columns shown in the list view.
		/// </summary>
		protected virtual void AdjustColumns()
		{
            if (ItemsLV.View == View.List || ItemsLV.Items.Count == 0)
            {
                ItemsLV.View = View.List;

                if (ItemsLV.Items.Count == 0 && !String.IsNullOrEmpty(m_instructions))
                {
                    ListViewItem item = new ListViewItem(m_instructions);
                    
                    item.ImageKey  = "Info";
                    item.ForeColor = Color.Gray;

                    ItemsLV.Items.Add(item);
                }

                ItemsLV.Columns[0].Width = -2;
                return;
            }

            ItemsLV.View = View.Details;

			for (int ii = 0; ii < m_columns.Length && ii < ItemsLV.Columns.Count; ii++)
			{
				// check for fixed width columns.
				if (m_columns[ii].Length >= 4 && m_columns[ii][3] != null)
				{
                    int width = (int)m_columns[ii][3];

                    if (ItemsLV.Columns[ii].Width < width)
                    {
					    ItemsLV.Columns[ii].Width = width;
                    }

					continue;
				}

				// check mandatory columns.
				if (m_columns[ii].Length < 3 || m_columns[ii][2] == null)
				{
					ItemsLV.Columns[ii].Width = -2;
					continue;
				}

				// check if all items have the default value for the column.
				bool display = false;

				foreach (ListViewItem listItem in ItemsLV.Items)
				{
					if (!m_columns[ii][2].Equals(listItem.SubItems[ii].Text))
					{
						display = true;
						break;
					}
				}

				// only display columns with non-default information.
				if (display)
				{
					ItemsLV.Columns[ii].Width = -2;
				}
				else
				{
					ItemsLV.Columns[ii].Width = 0;
				}
			}

			if (ItemsLV.Columns.Count > 0)
			{
				ItemsLV.Columns[ItemsLV.Columns.Count-1].Width = 0;
			}
		}
		
		/// <summary>
		/// Enables the state of menu items.
		/// </summary>
		protected virtual void EnableMenuItems(ListViewItem clickedItem)
		{
			// do nothing.
		}

		/// <summary>
		/// Sends notifications whenever items in the control are 'picked'.
		/// </summary>
		protected virtual void PickItems()
		{
			if (m_ItemsPicked != null)
			{
				ICollection data = GetDataToDrag() as ICollection;

                if (data != null)
                {
                    m_ItemsPicked(this, new ListItemActionEventArgs(ListItemAction.Picked, data));
                }
			}
		}
        
		/// <summary>
		/// Sends notifications whenever items in the control are 'selected'.
		/// </summary>
		protected virtual void SelectItems()
		{
			if (m_ItemsSelected != null)
			{
				object[] selectedObjects = new object[ItemsLV.SelectedItems.Count];

				for (int ii = 0; ii < selectedObjects.Length; ii++)
				{
					selectedObjects[ii] = ItemsLV.SelectedItems[ii].Tag;
				}

				m_ItemsSelected(this, new ListItemActionEventArgs(ListItemAction.Selected, selectedObjects));
			}
		}

		/// <summary>
		/// Sends notifications that an item has been added to the control.
		/// </summary>
		protected virtual void NotifyItemAdded(object item)
		{
			NotifyItemsAdded(new object[] { item });
		}

		/// <summary>
		/// Sends notifications that items have been added to the control.
		/// </summary>
		protected virtual void NotifyItemsAdded(object[] items)
		{
			if (m_ItemsAdded != null && items != null && items.Length > 0)
			{
				m_ItemsAdded(this, new ListItemActionEventArgs(ListItemAction.Added, items));
			}
		}

		/// <summary>
		/// Sends notifications that an item has been modified in the control.
		/// </summary>
		protected virtual void NotifyItemModified(object item)
		{
			NotifyItemsModified(new object[] { item });
		}

		/// <summary>
		/// Sends notifications that items have been modified in the control.
		/// </summary>
		protected virtual void NotifyItemsModified(object[] items)
		{
			if (m_ItemsModified != null && items != null && items.Length > 0)
			{
				m_ItemsModified(this, new ListItemActionEventArgs(ListItemAction.Modified, items));
			}
		}

		/// <summary>
		/// Sends notifications that and item has been removed from the control.
		/// </summary>
		protected virtual void NotifyItemRemoved(object item)
		{
			NotifyItemsRemoved(new object[] { item });
		}

		/// <summary>
		/// Sends notifications that items have been removed from the control.
		/// </summary>
		protected virtual void NotifyItemsRemoved(object[] items)
		{
			if (m_ItemsRemoved != null && items != null && items.Length > 0)
			{
				m_ItemsRemoved(this, new ListItemActionEventArgs(ListItemAction.Removed, items));
			}
		}

		/// <summary>
		/// Finds the list item with specified tag in the control,
		/// </summary>
		protected ListViewItem FindItem(object tag)
		{
			foreach (ListViewItem listItem in ItemsLV.Items)
			{
				if (Object.ReferenceEquals(tag, listItem.Tag))
				{
					return listItem;
				}
			}

			return null;
		}

        /// <summary>
        /// Returns the tag associated with a selected item.
        /// </summary>
        protected object GetSelectedTag(int index)
        {
            if (ItemsLV.SelectedItems.Count > index)
            {
                return ItemsLV.SelectedItems[index].Tag;
            }

            return null;
        }
		#endregion

        #region BaseListCtrlSorter Class
        /// <summary>
        /// A class that allows the list to be sorted.
        /// </summary>
        private class BaseListCtrlSorter : IComparer
        {            
            /// <summary>
            /// Initializes the sorter.
            /// </summary>
            public BaseListCtrlSorter(BaseListCtrl control)
            {
                m_control = control;
            }

            /// <summary>
            /// Compares the two items.
            /// </summary>
            public int Compare(object x, object y)
            {
                ListViewItem itemX = x as ListViewItem;
                ListViewItem itemY = y as ListViewItem;

                return m_control.CompareItems(itemX.Tag, itemY.Tag);
            }
                        
            private BaseListCtrl m_control;
        }
        #endregion
        
        #region Event Handlers
		private void ItemsLV_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
		{
			try
			{
                // ignore non-right clicks.
                if (e.Button == MouseButtons.Left)
                {
                    m_dragPosition = e.Location;
                    return;
                }

				// disable everything.
				if (ItemsLV.ContextMenuStrip != null)
				{
					foreach (ToolStripItem item in ItemsLV.ContextMenuStrip.Items)
					{
                        ToolStripMenuItem menuItem = item as ToolStripMenuItem;

                        if (menuItem == null)
                        {
                            continue;
                        }

                        menuItem.Enabled = false;

                        if (menuItem.DropDown != null)
                        {
                            foreach (ToolStripItem subItem in menuItem.DropDown.Items)
                            {
                                ToolStripMenuItem subMenuItem = subItem as ToolStripMenuItem;

                                if (subMenuItem != null)
                                {
                                    subMenuItem.Enabled = false;
                                }
                            }
                        }
					}
				}

				// selects the item that was right clicked on.
				ListViewItem clickedItem = ItemsLV.GetItemAt(e.X, e.Y);

				// ensure clicked item is selected.
				if (clickedItem != null)
				{
					clickedItem.Selected = true;
				}
                
			    // enable menu items according to context.
                EnableMenuItems(clickedItem);
			}
			catch (Exception exception)
			{
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
			}
		}        

        private void ItemsLV_MouseUp(object sender, MouseEventArgs e)
        {
			try
			{
                if (e.Button == MouseButtons.Left)
                {
                    m_dragPosition = e.Location;
                    return;
                }
			}
			catch (Exception exception)
			{
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
			}
        }

		private void ItemsLV_DoubleClick(object sender, System.EventArgs e)
		{
			try
			{
				PickItems();
			}
			catch (Exception exception)
			{
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
			}		
		}

		private void ItemsLV_SelectedIndexChanged(object sender, System.EventArgs e)
		{
			try
			{
                SelectItems();
			}
			catch (Exception exception)
			{
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
			}		
		}

        private void ItemsLV_MouseMove(object sender, MouseEventArgs e)
        {
            if (m_enableDragging && e.Button == MouseButtons.Left && !m_dragPosition.Equals(e.Location))
            {
                object data = GetDataToDrag();

                if (data != null)
                {
                    ItemsLV.DoDragDrop(data, DragDropEffects.Copy);
                }
            }
        }

        /// <summary>
        /// Handles the DragEnter event of the ItemsLV control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.DragEventArgs"/> instance containing the event data.</param>
        protected virtual void ItemsLV_DragEnter(object sender, DragEventArgs e)
        {
            if (m_enableDragging)
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        /// <summary>
        /// Handles the DragDrop event of the ItemsLV control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.DragEventArgs"/> instance containing the event data.</param>
        protected virtual void ItemsLV_DragDrop(object sender, DragEventArgs e)
        {
            // overriden by sub-class.
        }
        #endregion
    }
    
    #region ListItemAction Enumeration
	/// <summary>
	/// The possible actions that could affect an item.
	/// </summary>
	public enum ListItemAction
    {

        /// <summary>
        /// The item was picked (double clicked).
        /// </summary>
        Picked,

        /// <summary>
        /// The item was selected.
        /// </summary>
        Selected,

        /// <summary>
        /// The item was added.
        /// </summary>
        Added,

        /// <summary>
        /// The item was modified.
        /// </summary>
		Modified,

        /// <summary>
        /// The item was removed.
        /// </summary>
		Removed
    }
    #endregion

    #region ListItemActionEventArgs Class
    /// <summary>
    /// The event argurments passed when an item event occurs.
    /// </summary>
	public class ListItemActionEventArgs : EventArgs
	{
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ListItemActionEventArgs"/> class.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <param name="items">The items.</param>
		public ListItemActionEventArgs(ListItemAction action, ICollection items)
		{
			m_items  = items;
			m_action = action;
		}
        #endregion
        
        #region Public Properties
        /// <summary>
        /// Gets the items.
        /// </summary>
        /// <value>The items.</value>
		public ICollection Items  
        {
            get { return m_items;  }
        }

        /// <summary>
        /// Gets the action.
        /// </summary>
        /// <value>The action.</value>
        public ListItemAction Action 
        {
            get { return m_action; }
        }
        #endregion
        
        #region Private Fields
		private ICollection m_items;
		private ListItemAction m_action;
        #endregion
	}

    /// <summary>
    /// The delegate used to receive item action events.
    /// </summary>
    public delegate void ListItemActionEventHandler(object sender, ListItemActionEventArgs e);
    #endregion
}
