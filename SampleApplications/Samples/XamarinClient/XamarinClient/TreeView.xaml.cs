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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using System.Collections.ObjectModel;

namespace XamarinClient
{
    [XamlCompilation(XamlCompilationOptions.Compile)]

    public partial class TreeView : ContentPage
	{
        ObservableCollection<ListNode> nodes = new ObservableCollection<ListNode>();
        SampleClient opcClient;
        Tree storedTree;

        public TreeView (Tree tree, SampleClient client)
		{
			InitializeComponent();
            BindingContext = nodes;

            storedTree = tree;
            opcClient = client;
            DisplayNodes();
            
        }

        void DisplayNodes()
        {
            nodes.Clear();
            
            foreach (var node in storedTree.currentView)
            {
                nodes.Add(node);
            }

            //defined in XAML to follow
            treeView.ItemsSource = null;
            treeView.ItemsSource = nodes;              
        }

        async void OnSelection(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem == null)
            {
                return;
            }    
            treeView.SelectedItem = null; // deselect row
            ListNode selected = e.SelectedItem as ListNode;

            if (selected.children == true)
            {
                storedTree = opcClient.GetChildren(selected.id);

                Page treeViewPage = new TreeView(storedTree, opcClient);
                treeViewPage.Title = this.Title + "/" + selected.NodeName;
                await Navigation.PushAsync(treeViewPage);
            }
        }

        public void OnRead(object sender, EventArgs e)
        {
            var menu = sender as MenuItem;

            var selected = menu.CommandParameter as ListNode;
            var value = opcClient.VariableRead(selected.id);
            DisplayAlert(selected.NodeName, value, "OK");
        }

        private void OnBindingContextChanged(object sender, EventArgs e)
        {
            base.OnBindingContextChanged();

            if (BindingContext == null)
            {
                return;
            }
                
            ViewCell viewCell = sender as ViewCell;
            var item = viewCell.BindingContext as ListNode;
            viewCell.ContextActions.Clear();

            if (item != null)
            {
                if (item.nodeClass == "Variable")
                {
                    viewCell.ContextActions.Add(new MenuItem()
                    {
                        Text = "Read"
                    });

                    foreach (var action in viewCell.ContextActions)
                    {
                        action.SetBinding(MenuItem.CommandParameterProperty, new Binding("."));
                        action.Clicked += OnRead;
                    }
                }
            }
        }
    }
}