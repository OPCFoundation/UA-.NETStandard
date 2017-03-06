using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Navigation;
using Windows.Storage;
using Opc.Ua.Configuration;
using Opc.Ua.Client.Controls;
using System.Threading.Tasks;

namespace Opc.Ua.SampleServer
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    sealed partial class App : Application
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            ApplicationInstance application = new ApplicationInstance();
            application.ApplicationName = "UA Sample Server";
            application.ApplicationType = ApplicationType.Server;
            application.ConfigSectionName = "Opc.Ua.SampleServer";

            // helper to let Opc.Ua Utils find the localFolder in the environment
            StorageFolder localFolder = ApplicationData.Current.LocalFolder;
            Utils.DefaultLocalFolder = localFolder.Path;

            // set empty page for MessageDlg
            Window.Current.Content = new ServerPage();

            // Ensure the current window is active
            Window.Current.Activate();

            // Allow the current window to activate since the stack initialization below can take some time
            // and the app can be terminated by the runtime if this takes too long
            await Task.Delay(1);

            try
            {
                // load the application configuration.
                await application.LoadApplicationConfiguration(false);

                // check the application certificate.
                await application.CheckApplicationInstanceCertificate(false, 0);

                // start the server.
                await application.Start(new Opc.Ua.Sample.SampleServer());

                // run the application interactively.
                Window.Current.Content = new ServerPage(application);
            }
            catch (Exception ex)
            {
                Utils.Trace("Exception:" + ex.Message);
                MessageDlg dialog = new MessageDlg(ex.Message);
                await dialog.ShowAsync();
                Application.Current.Exit();
            }
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            deferral.Complete();
        }
    }
}
