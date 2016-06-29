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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A base class for list controls.
    /// </summary>
    public partial class BaseListCtrl : UserControl
    {
        /// <summary>
        /// The ListView contained in the control.
        /// </summary>
        protected ListView ItemsLV;

        #region Public Interface
        /// <summary>
        /// Initializes a new instance of the <see cref="BaseListCtrl"/> class.
        /// </summary>
        public BaseListCtrl()
        {
            InitializeComponent();
            ItemsLV = _ItemsLV;
        }

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
			List<object> items = new List<object>();

			foreach (ListViewItem listItem in ItemsLV.Items)
			{
				items.Add(listItem.Tag);
			}

			return items.ToArray();
		}

		/// <summary>
		/// Returns the objects associated with the selected items in the control.
		/// </summary>
		public Array GetSelectedItems(System.Type type)
		{
            List<object> items = new List<object>();

            foreach (ListViewItem listItem in ItemsLV.SelectedItems)
            {
                items.Add(listItem.Tag);
            }
            
			return items.ToArray();
		}
		#endregion

		#region Private Members
        private bool m_prependItems;
		private event ListItemActionEventHandler m_ItemsPicked;
		private event ListItemActionEventHandler m_ItemsSelected;
		private event ListItemActionEventHandler m_ItemsAdded;
		private event ListItemActionEventHandler m_ItemsModified;
		private event ListItemActionEventHandler m_ItemsRemoved;
        private bool m_updating;
        private int m_updateCount;
        private string m_instructions;
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

                return ((ListViewItem) ItemsLV.SelectedItems[0]).Tag;
            }
        }

		/// <summary>
		/// Deletes the currently selected items.
		/// </summary>
        protected virtual void DeleteSelection()
        {
            foreach (ListViewItem item in ItemsLV.Items)
            {
                if (ItemsLV.SelectedItems.Contains(item))
                {
                    ItemsLV.Items.Remove(item);
                }
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
                List<object> data = new List<object>();

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
            ListViewItem listItem = null;

            if (m_updating)
            {
                if (m_updateCount < ItemsLV.Items.Count)
                {
                    listItem = (ListViewItem) ItemsLV.Items[m_updateCount];
                }

                m_updateCount++;
            }
            
            if (listItem == null)
            {
                listItem = new ListViewItem();
            }

			listItem.Name     = String.Format("{0}", item);
			listItem.Tag      = item;
            
			// calculate new index.
            int newIndex = index;

            if (index < 0 || index > ItemsLV.Items.Count)
            {
                newIndex = ItemsLV.Items.Count;
            }

			// update columns.
			UpdateItem(listItem, item, newIndex);

            if (listItem.Parent == null)
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
                ItemsLV.Items.Remove(ItemsLV.Items[ItemsLV.Items.Count-1]);
            }

            m_updateCount = 0;
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
					selectedObjects[ii] = ((ListViewItem) ItemsLV.SelectedItems[ii]).Tag;
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
                return ((ListViewItem) ItemsLV.SelectedItems[index]).Tag;
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
		private void ItemsLV_DoubleClick(object sender, System.EventArgs e)
		{
			try
			{
				PickItems();
			}
			catch (Exception exception)
			{
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
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
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }		
		}

        /// <summary>
        /// Handles the DragDrop event of the ItemsLV control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.DragEventArgs"/> instance containing the event data.</param>
        protected virtual void ItemsLV_DragDrop(object sender, PointerRoutedEventArgs e)
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
