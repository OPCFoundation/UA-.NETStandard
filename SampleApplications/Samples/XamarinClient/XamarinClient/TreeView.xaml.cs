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