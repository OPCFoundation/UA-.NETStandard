using System;
using System.Windows.Forms;
using Opc.Ua.Configuration;
using System.Threading.Tasks;
using Opc.Ua.Client.Controls;

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

            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            ApplicationInstance application = new ApplicationInstance();
            application.ApplicationType = ApplicationType.Client;
            application.ConfigSectionName = "Opc.Ua.GdsClient";

            try
            {
                // load the application configuration.
                Task<ApplicationConfiguration> task = application.LoadApplicationConfiguration(false);
                task.Wait();

                // check the application certificate.
                Task<bool> task2 = application.CheckApplicationInstanceCertificate(false, 0);
                task2.Wait();

                // run the application interactively.
                Application.Run(new MainForm(application));
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    MessageBox.Show(e.Message + "\r\n" + e.InnerException.Message);
                }
                else
                {
                    MessageBox.Show(e.Message);
                }
            }
        }
    }
}
