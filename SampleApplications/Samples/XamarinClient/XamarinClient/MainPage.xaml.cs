using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;


namespace XamarinClient
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class MainPage : ContentPage
	{
        StackLayout stacklayout = new StackLayout();
        static LabelViewModel textInfo = new LabelViewModel();
        SampleClient OpcClient = new SampleClient(textInfo);
        string endpointUrl = null;
   
        public MainPage()
        {
            InitializeComponent();
            BindingContext = textInfo;
        }

        async void OnConnect(object sender, EventArgs e)
        {
            endpointUrl = EntryUrl.Text;
            //endpointUrl = @"opc.tcp://10.164.98.92:51210/UA/SampleServer";   //Debug

            if (endpointUrl != null)
            {
                if (ConnectButton.Text == "Connect")
                {
                    bool connectToServer = true;
                    ConnectIndicator.IsRunning = true;

                    await Task.Run(() => OpcClient.CreateCertificate());

                    if (OpcClient.haveAppCertificate == false)
                    {
                        connectToServer = await DisplayAlert("Warning", "missing application certificate, \nusing unsecure connection. \nDo you want to continue?", "Yes", "No");
                    }

                    if (connectToServer == true)
                    {
                        var connectionStatus = await Task.Run(() => OpcClient.OpcClient(endpointUrl));

                        if (connectionStatus == SampleClient.ConnectionStatus.Connected)
                        {
                            Tree tree;
                            ConnectButton.Text = "Disconnect";

                            tree = OpcClient.GetRootNode(textInfo);
                            if (tree.currentView[0].children == true)
                            {
                                tree = OpcClient.GetChildren(tree.currentView[0].id);
                            }

                            ConnectIndicator.IsRunning = false;
                            Page treeViewRoot = new TreeView(tree, OpcClient);
                            treeViewRoot.Title = "/Root";
                            await Navigation.PushAsync(treeViewRoot);
                        }
                        else
                        {
                            ConnectIndicator.IsRunning = false;
                            await DisplayAlert("Warning", "Cannot connect to an OPC UA server", "Ok");
                        }
                    }
                    else
                    {
                        ConnectIndicator.IsRunning = false;
                    }
                }
                else
                {
                    OpcClient.Disconnect(OpcClient.session);
                    ConnectButton.Text = "Connect";
                }
            }
            else
            {
                await DisplayAlert("Warning", "Server endpoint URL cannot be null", "Ok");
            }
        }
    }
}