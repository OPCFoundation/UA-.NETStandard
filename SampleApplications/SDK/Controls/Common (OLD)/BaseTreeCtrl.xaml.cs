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
using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using WinRTXamlToolkit.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI.Xaml.Input;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A helper class for tree views.
    /// </summary>
    public abstract class BindableBase : INotifyPropertyChanged
    {
        /// <summary>
        /// Multicast event for property change notifications.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Checks if a property already matches a desired value.  Sets the property and
        /// notifies listeners only when necessary.
        /// </summary>
        /// <typeparam name="T">Type of the property.</typeparam>
        /// <param name="storage">Reference to a property with both getter and setter.</param>
        /// <param name="value">Desired value for the property.</param>
        /// <param name="propertyName">Name of the property used to notify listeners.  This
        /// value is optional and can be provided automatically when invoked from compilers that
        /// support CallerMemberName.</param>
        /// <returns>True if the value was changed, false if the existing value matched the
        /// desired value.</returns>
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] String propertyName = null)
        {
            if (object.Equals(storage, value)) return false;

            storage = value;
            this.OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Notifies listeners that a property value has changed.
        /// </summary>
        /// <param name="propertyName">Name of the property used to notify listeners.  This
        /// value is optional and can be provided automatically when invoked from compilers
        /// that support <see cref="CallerMemberNameAttribute"/>.</param>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var eventHandler = this.PropertyChanged;
            if (eventHandler != null)
            {
                eventHandler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    /// <summary>
    /// The view model for the tree view.
    /// </summary>
    public class TreeViewPageViewModel : BindableBase
    {
        public delegate Task TreeItemViewModelHandler(TreeItemViewModel node);

        public TreeItemViewModelHandler OnLoadPropertiesAsync;
        public TreeItemViewModelHandler OnLoadChildrenAsync;
        public TreeItemViewModelHandler OnRefreshAsync;

        #region TreeItems
        private ObservableCollection<TreeItemViewModel> _treeItems;
        public ObservableCollection<TreeItemViewModel> TreeItems
        {
            get { return _treeItems; }
            set { this.SetProperty(ref _treeItems, value); }
        }
        #endregion

        #region SelectedItem
        private TreeItemViewModel _selectedItem;
        public TreeItemViewModel SelectedItem
        {
            get { return _selectedItem; }
            set { SetProperty(ref _selectedItem, value); }
        }
        #endregion

        public TreeViewPageViewModel()
        {
            TreeItems = new ObservableCollection<TreeItemViewModel>();
        }

        #region internal
        internal virtual async void LoadPropertiesAsync(TreeItemViewModel node)
        {
            if (OnLoadPropertiesAsync != null)
            {
                await OnLoadPropertiesAsync(node);
            }
        }

        internal virtual async void LoadChildrenAsync(TreeItemViewModel node)
        {
            if (OnLoadChildrenAsync != null)
            {
                await OnLoadChildrenAsync(node);
            }
        }

        internal virtual async void RefreshAsync(TreeItemViewModel node)
        {
            if (OnRefreshAsync != null)
            {
                await OnRefreshAsync(node);
            }
        }
        #endregion
    }

    /// <summary>
    /// The view model for tree items.
    /// </summary>
    public class TreeItemViewModel : BindableBase
    {
        public TreeViewPageViewModel TreeModel { get; protected set; }
        private bool _everSelected;

        public TreeItemViewModel(
            TreeViewPageViewModel treeModel,
            TreeItemViewModel parent)
        {
            TreeModel = treeModel;
            Parent = parent;
            _everSelected = false;
        }

        #region Parent
        private TreeItemViewModel _parent;
        public TreeItemViewModel Parent
        {
            get { return _parent; }
            set { this.SetProperty(ref _parent, value); }
        }
        #endregion

        #region Text
        private string _text;
        /// <summary>
        /// Gets or sets the text.
        /// </summary>
        public string Text
        {
            get { return _text; }
            set { this.SetProperty(ref _text, value); }
        }
        #endregion

        #region Icon
        private string _icon;
        /// <summary>
        /// Gets or sets the icon.
        /// </summary>
        public string Icon
        {
            get { return _icon; }
            set { this.SetProperty(ref _icon, value); }
        }
        #endregion

        #region Children
        private ObservableCollection<TreeItemViewModel> _children = new ObservableCollection<TreeItemViewModel>();
        /// <summary>
        /// Gets or sets the child items.
        /// </summary>
        public ObservableCollection<TreeItemViewModel> Children
        {
            get { return _children; }
            set { this.SetProperty(ref _children, value); }
        }
        #endregion

        #region Brush
        private SolidColorBrush _brush;
        /// <summary>
        /// Gets or sets the brush.
        /// </summary>
        public SolidColorBrush Brush
        {
            get { return _brush; }
            set { this.SetProperty(ref _brush, value); }
        }
        #endregion

        #region Item
        private Object _item;
        /// <summary>
        /// Gets or sets the session.
        /// </summary>
        public Object Item
        {
            get { return _item; }
            set { this.SetProperty(ref _item, value); }
        }
        #endregion

        #region IsSelected
        private bool _isSelected;
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (this.SetProperty(ref _isSelected, value) && value)
                {
                    if (!_everSelected)
                    {
                        _everSelected = true;
                        TreeModel.LoadPropertiesAsync(this);
                    }
                    this.TreeModel.SelectedItem = this;
                }
            }
        }
        #endregion

        #region IsExpanded
        private bool _isExpanded;
        public bool IsExpanded
        {
            get { return _isExpanded; }
            set
            {
                if (this.SetProperty(ref _isExpanded, value) && value)
                {
                    TreeModel.LoadChildrenAsync(this);
                }
            }
        }
        #endregion

    }


    /// <summary>
    /// A base class for tree controls.
    /// </summary>
    public partial class BaseTreeCtrl : UserControl
    {
#region Public Interface
        /// <summary>
        /// The TreeView contained in the Control.
        /// </summary>
        private TreeView TV;
        protected TreeViewPageViewModel NodesTV;

        /// <summary>
        /// Initialize tree control.
        /// </summary>
        public BaseTreeCtrl()
        {
            InitializeComponent();
            DataContext = NodesTV = new TreeViewPageViewModel();
            ContainerGrid.Children.Clear();
            NodesTV.TreeItems?.Clear();
            ContainerGrid.Children.Add(TV = (TreeView)this.TreeViewTemplate.LoadContent());
            NodesTV.OnLoadChildrenAsync += BeforeExpand;
        }

        /// <summary>
        /// Raised whenever a node is 'picked' in the control.
        /// </summary>
        public event TreeNodeActionEventHandler NodePicked
        {
            add { m_NodePicked += value; }
            remove { m_NodePicked -= value; }
        }

        /// <summary>
        /// Raised whenever a node is selected in the control.
        /// </summary>
        public event TreeNodeActionEventHandler NodeSelected
        {
            add { m_NodeSelected += value; }
            remove { m_NodeSelected -= value; }
        }

        /// <summary>
        /// Whether the control should allow items to be dragged.
        /// </summary>
        public bool EnableDragging
        {
            get { return m_enableDragging; }
            set { m_enableDragging = value; }
        }

        /// <summary>
        /// Clears the contents of the TreeView
        /// </summary>
        public void Clear()
        {
            NodesTV.TreeItems?.Clear();
        }

        #endregion

        #region Private Fields
        private event TreeNodeActionEventHandler m_NodePicked;
        private event TreeNodeActionEventHandler m_NodeSelected;
        private bool m_enableDragging;
#endregion

#region Protected Methods
        /// <summary>
        /// Adds an item to the tree.
        /// </summary>
        protected virtual TreeItemViewModel AddNode(TreeItemViewModel treeNode, object item)
        {
            return AddNode(treeNode, item, String.Format("{0}", item), "ClosedFolder");
        }

        /// <summary>
        /// Adds an item to the tree.
        /// </summary>
        protected virtual TreeItemViewModel AddNode(TreeItemViewModel parent, object item, string text, string icon)
        {
            // create node.
            TreeItemViewModel treeNode = new TreeItemViewModel(NodesTV, parent);

            // update text/icon.
            UpdateNode(treeNode, item, text, icon);

            // add to control.
            if (parent == null)
            {
                NodesTV.TreeItems?.Add(treeNode);
            }
            else
            {
                parent.Children.Add(treeNode);
            }

            // return new tree node.
            return treeNode;
        }

        /// <summary>
        /// Updates a tree node with the current contents of an object.
        /// </summary>
        protected virtual void UpdateNode(TreeItemViewModel treeNode, object item, string text, string icon)
        {
            treeNode.Text = text;
            treeNode.Item = item;
            treeNode.Icon = icon;
            switch (icon)
            {
                case "Server":
                    treeNode.Brush = new SolidColorBrush(Colors.Green);
                    break;
                case "ServerStopped":
                    treeNode.Brush = new SolidColorBrush(Colors.Red);
                    break;
                case "ServerKeepAliveStopped":
                    treeNode.Brush = new SolidColorBrush(Colors.Yellow);
                    break;
                default:
                    treeNode.Brush = null;
                    break;
            }
        }

        /// <summary>
        /// Returns the data to drag.
        /// </summary>
        protected virtual object GetDataToDrag(TreeItemViewModel node)
        {
            return node.Item;
        }

        /// <summary>
        /// Enables the state of menu items.
        /// </summary>
        protected virtual void EnableMenuItems(TreeItemViewModel clickedNode)
        {
            // do nothing.
        }

        /// <summary>
        /// Initializes a node before expanding it.
        /// </summary>
        protected virtual Task BeforeExpand(TreeItemViewModel clickedNode)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends notifications whenever a node in the control is 'picked'.
        /// </summary>
        protected virtual void PickNode()
        {
            if (m_NodePicked != null)
            {
                if (NodesTV.SelectedItem != null)
                {
                    object parent = null;

                    if (NodesTV.SelectedItem.Parent != null)
                    {
                        parent = NodesTV.SelectedItem.Parent;
                    }

                    m_NodePicked(this, new TreeNodeActionEventArgs(TreeNodeAction.Picked, NodesTV.SelectedItem.Item, parent));
                }
            }
        }

        /// <summary>
        /// Sends notifications whenever a node in the control is 'selected'.
        /// </summary>
        protected virtual void SelectNode()
        {
            if (m_NodeSelected != null)
            {
                if (NodesTV.SelectedItem != null)
                {
                    object parent = null;

                    if (NodesTV.SelectedItem.Parent != null)
                    {
                        parent = NodesTV.SelectedItem.Parent;
                    }

                    m_NodeSelected(this, new TreeNodeActionEventArgs(TreeNodeAction.Selected, NodesTV.SelectedItem.Item, parent));
                }
            }
        }

        /// <summary>
        /// Returns the Tag for the current selection.
        /// </summary>
        public object SelectedTag
        {
            get
            {
                if (NodesTV.SelectedItem != null)
                {
                    return NodesTV.SelectedItem.Item;
                }

                return null;
            }
        }
        #endregion

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            SelectNode();
        }

        private void TreeView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            TreeView tv = sender as TreeView;
        }
    }

    #region TreeNodeAction Eumeration
    /// <summary>
    /// The possible actions that could affect a node.
    /// </summary>
    public enum TreeNodeAction
	{
        /// <summary>
        /// A node was picked in the tree.
        /// </summary>
		Picked,

        /// <summary>
        /// A node was selected in the tree.
        /// </summary>
		Selected
	}
#endregion
    
#region TreeNodeActionEventArgs class
	/// <summary>
	/// The event argurments passed when an node event occurs.
	/// </summary>
	public class TreeNodeActionEventArgs : EventArgs
	{
#region Constructor
        /// <summary>
        /// Initializes the object.
        /// </summary>
		public TreeNodeActionEventArgs(TreeNodeAction action, object node, object parent)
		{
			m_node   = node;
            m_parent = parent;
			m_action = action;
		}
#endregion
        
#region Public Fields
        /// <summary>
        /// The tag associated with the node that was acted on.
        /// </summary>
		public object Node
        {
            get { return m_node; }
        }
		
        /// <summary>
        /// The tag associated with the parent of the node that was acted on.
        /// </summary>
		public object Parent
        {
            get { return m_parent; }
        }
		
        /// <summary>
        /// The action in question.
        /// </summary>
        public TreeNodeAction Action 
        {
            get { return m_action; }
        }
#endregion

#region Private Fields
		private object m_node;
        private object m_parent;
		private TreeNodeAction m_action;
#endregion
	}

    /// <summary>
    /// The delegate used to receive node action events.
    /// </summary>
    public delegate void TreeNodeActionEventHandler(object sender, TreeNodeActionEventArgs e);
#endregion
}
