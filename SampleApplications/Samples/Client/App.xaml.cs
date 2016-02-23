using Opc.Ua.Client.Controls;
using Opc.Ua.Configuration;
using Opc.Ua.Sample;
using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Navigation;

namespace Opc.Ua.SampleClient
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
        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            ApplicationInstance application = new ApplicationInstance();
            application.ApplicationName = "UA Sample Client";
            application.ApplicationType = ApplicationType.ClientAndServer;
            application.ConfigSectionName = "Opc.Ua.SampleClient";

            // set empty page for MessageDlg
            Window.Current.Content = new ClientPage();

            // Ensure the current window is active
            Window.Current.Activate();

            try
            {
                // load the application configuration.
                await application.LoadApplicationConfiguration(false);

                // allow UA servers to use the same certificate for HTTPS validation.
                ApplicationInstance.SetUaValidationForHttps(application.ApplicationConfiguration.CertificateValidator);

                // run the application interactively.
                Window.Current.Content = new ClientPage(application.ApplicationConfiguration.CreateMessageContext(), application, null, application.ApplicationConfiguration);

                // Ensure the current window is active
                Window.Current.Activate();

            }
            catch (ServiceResultException ex)
            {
                Utils.Trace("ServiceResultException:" + ex.Message);
                MessageDlg dialog = new MessageDlg("Client not started. Exit.\r\n" + ex.Message);
                await dialog.ShowAsync();
                Application.Current.Exit();
                return;
            }

            try
            { 

                // check the application certificate.
                await application.CheckApplicationInstanceCertificate(false, 0);

                // start the server.
                await application.Start(new SampleServer());

            }
            catch (ServiceResultException ex)
            {
                Utils.Trace("ServiceResultException:" + ex.Message);
                MessageDlg dialog = new MessageDlg("Client ok but Server has not started.\r\n" + ex.Message);
                await dialog.ShowAsync();
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
