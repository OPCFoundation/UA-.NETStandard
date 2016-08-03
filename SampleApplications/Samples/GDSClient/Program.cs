using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Configuration;
using Cannoli.Services;

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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            ApplicationInstance application = new ApplicationInstance(ApplicationType.Client);

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
                ExceptionDlg.Show(application.ApplicationName, e);
                return;
            }
        }
    }
}
