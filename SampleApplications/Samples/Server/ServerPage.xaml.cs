/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using Windows.UI.Xaml.Controls;
using System.Threading;
using Windows.UI.Core;

using Opc.Ua.Server;
using Opc.Ua.Configuration;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.SampleServer
{
    /// <summary>
    /// The primary form displayed by the application.
    /// </summary>
    public partial class ServerPage : Page
    {
        #region Constructors
        /// <summary>
        /// Creates an empty page.
        /// </summary>
        public ServerPage()
        {
            InitializeComponent();
        }
        
        /// <summary>
        /// Creates a form which displays the status for a UA server.
        /// </summary>
        public ServerPage(ApplicationInstance application)
        {
            InitializeComponent();

            m_application = application;

            if (application.Server is StandardServer)
            {
                ServerDiagnosticsCTRL.Initialize((StandardServer)application.Server, application.ApplicationConfiguration);
            }

            if (!application.ApplicationConfiguration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                application.ApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }

        }
        #endregion

        #region Private Fields
        private ApplicationInstance m_application;
        #endregion

        #region Event Handlers
        /// <summary>
        /// Handles a certificate validation error.
        /// </summary>
        void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            ManualResetEvent ev = new ManualResetEvent(false);
            Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                async () =>
                {
                    await GuiUtils.HandleCertificateValidationError(this, validator, e);
                    ev.Set();
                }
                ).AsTask().Wait();
            ev.WaitOne();
        }

        private void ServerPage_FormClosed(object sender)
        {
            try
            {
                m_application.Stop();
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Error stopping server.");
            }
        }
#endregion

    }
}
