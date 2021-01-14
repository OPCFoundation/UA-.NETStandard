/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server.Controls;
using System.Threading.Tasks;

namespace Quickstarts.ReferenceServer
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
            application.ApplicationType   = ApplicationType.Server;
            application.ConfigSectionName = "Quickstarts.ReferenceServer";

            try
            {

                // load the application configuration.
                application.LoadApplicationConfiguration(false).Wait();

                // check the application certificate.
                bool certOk = application.CheckApplicationInstanceCertificate(false, 0).Result;
                if (!certOk)
                {
                    throw new Exception("Application instance certificate invalid!");
                }

                // start the server.
                application.Start(new ReferenceServer()).Wait();

                // check whether the invalid certificates dialog should be displayed.
                bool showCertificateValidationDialog = false;
                ReferenceServerConfiguration refServerconfiguration = application.ApplicationConfiguration.ParseExtension<ReferenceServerConfiguration>();

                if (refServerconfiguration != null)
                {
                    showCertificateValidationDialog = refServerconfiguration.ShowCertificateValidationDialog;
                }

                // run the application interactively.
                Application.Run(new ServerForm(application, showCertificateValidationDialog));
            }
            catch (Exception e)
            {
                ExceptionDlg.Show(application.ApplicationName, e);
            }
        }
    }

    /// <summary>
    /// The <b>ReferenceServer</b> namespace contains classes which implement a Quickstart Server.
    /// </summary>
    /// <exclude/>
    [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public class NamespaceDoc
    {
    }
}
