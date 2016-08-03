using System;
using System.Windows.Forms;
using Opc.Ua.Configuration;

namespace Opc.Ua.GdsClient
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Initialize the user interface.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            ApplicationInstance application = new ApplicationInstance();
            application.ApplicationType = ApplicationType.Client;
            application.ConfigSectionName = "Opc.Ua.GdsClient";

            try
            {
                // load the application configuration.
                application.LoadApplicationConfiguration(false);

                // check the application certificate.
                application.CheckApplicationInstanceCertificate(false, 0);

                // run the application interactively.
                Application.Run(new MainForm(application));
            }
            catch (Exception e)
            {
                MessageBox.Show(application.ApplicationName + ": " + e.Message);
            }
        }
    }
}
