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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace Quickstarts
{
    public class UAServer<T>
        where T : StandardServer, new()
    {
        public ApplicationInstance Application { get; private set; }
        public ApplicationConfiguration Configuration => Application.ApplicationConfiguration;

        public bool AutoAccept { get; set; }
        public string Password { get; set; }

        public ExitCode ExitCode { get; private set; }
        public T Server { get; private set; }

        /// <summary>
        /// Ctor of the server.
        /// </summary>
        /// <param name="writer">The text output.</param>
        public UAServer(TextWriter writer)
        {
            m_output = writer;
        }

        /// <summary>
        /// Load the application configuration.
        /// </summary>
        public async Task LoadAsync(string applicationName, string configSectionName)
        {
            try
            {
                ExitCode = ExitCode.ErrorNotStarted;

                ApplicationInstance.MessageDlg = new ApplicationMessageDlg(m_output);
                var passwordProvider = new CertificatePasswordProvider(Password);
                Application = new ApplicationInstance
                {
                    ApplicationName = applicationName,
                    ApplicationType = ApplicationType.Server,
                    ConfigSectionName = configSectionName,
                    CertificatePasswordProvider = passwordProvider,
                };

                // load the application configuration.
                await Application.LoadApplicationConfigurationAsync(false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new ErrorExitException(ex.Message, ExitCode);
            }
        }

        /// <summary>
        /// Load the application configuration.
        /// </summary>
        public async Task CheckCertificateAsync(bool renewCertificate)
        {
            try
            {
                ApplicationConfiguration config = Application.ApplicationConfiguration;
                if (renewCertificate)
                {
                    await Application.DeleteApplicationInstanceCertificateAsync().ConfigureAwait(false);
                }

                // check the application certificate.
                bool haveAppCertificate = await Application
                    .CheckApplicationInstanceCertificatesAsync(false)
                    .ConfigureAwait(false);
                if (!haveAppCertificate)
                {
                    throw new ErrorExitException("Application instance certificate invalid!");
                }

                if (!config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(
                        CertificateValidator_CertificateValidation
                    );
                }
            }
            catch (Exception ex)
            {
                throw new ErrorExitException(ex.Message, ExitCode);
            }
        }

        /// <summary>
        /// Create server instance and add node managers.
        /// </summary>
        public void Create(IList<INodeManagerFactory> nodeManagerFactories)
        {
            try
            {
                // create the server.
                Server = new T();
                if (nodeManagerFactories != null)
                {
                    foreach (INodeManagerFactory factory in nodeManagerFactories)
                    {
                        Server.AddNodeManager(factory);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new ErrorExitException(ex.Message, ExitCode);
            }
        }

        /// <summary>
        /// Start the server.
        /// </summary>
        public async Task StartAsync()
        {
            try
            {
                // create the server.
                Server ??= new T();

                // start the server
                await Application.StartAsync(Server).ConfigureAwait(false);

                // save state
                ExitCode = ExitCode.ErrorRunning;

                // print endpoint info
                IEnumerable<string> endpoints = Application
                    .Server.GetEndpoints()
                    .Select(e => e.EndpointUrl)
                    .Distinct();
                foreach (string endpoint in endpoints)
                {
                    m_output.WriteLine(endpoint);
                }

                // start the status thread
                m_status = Task.Run(StatusThreadAsync);

                // print notification on session events
                Server.CurrentInstance.SessionManager.SessionActivated += EventStatus;
                Server.CurrentInstance.SessionManager.SessionClosing += EventStatus;
                Server.CurrentInstance.SessionManager.SessionCreated += EventStatus;
            }
            catch (Exception ex)
            {
                throw new ErrorExitException(ex.Message, ExitCode);
            }
        }

        /// <summary>
        /// Stops the server.
        /// </summary>
        public async Task StopAsync()
        {
            try
            {
                if (Server != null)
                {
                    using T server = Server;
                    // Stop status thread
                    Server = null;
                    await m_status.ConfigureAwait(false);

                    // Stop server and dispose
                    server.Stop();
                }

                ExitCode = ExitCode.Ok;
            }
            catch (Exception ex)
            {
                throw new ErrorExitException(ex.Message, ExitCode.ErrorStopping);
            }
        }

        /// <summary>
        /// The certificate validator is used
        /// if auto accept is not selected in the configuration.
        /// </summary>
        private void CertificateValidator_CertificateValidation(
            CertificateValidator validator,
            CertificateValidationEventArgs e
        )
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted && AutoAccept)
            {
                m_output.WriteLine(
                    "Accepted Certificate: [{0}] [{1}]",
                    e.Certificate.Subject,
                    e.Certificate.Thumbprint
                );
                e.Accept = true;
                return;
            }
            m_output.WriteLine(
                "Rejected Certificate: {0} [{1}] [{2}]",
                e.Error,
                e.Certificate.Subject,
                e.Certificate.Thumbprint
            );
        }

        /// <summary>
        /// Update the session status.
        /// </summary>
        private void EventStatus(ISession session, SessionEventReason reason)
        {
            m_lastEventTime = DateTime.UtcNow;
            PrintSessionStatus(session, reason.ToString());
        }

        /// <summary>
        /// Output the status of a connected session.
        /// </summary>
        private void PrintSessionStatus(ISession session, string reason, bool lastContact = false)
        {
            var item = new StringBuilder();
            lock (session.DiagnosticsLock)
            {
                item.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{0,9}:{1,20}:",
                    reason,
                    session.SessionDiagnostics.SessionName
                );
                if (lastContact)
                {
                    item.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "Last Event:{0:HH:mm:ss}",
                        session.SessionDiagnostics.ClientLastContactTime.ToLocalTime()
                    );
                }
                else
                {
                    if (session.Identity != null)
                    {
                        item.AppendFormat(CultureInfo.InvariantCulture, ":{0,20}", session.Identity.DisplayName);
                    }
                    item.AppendFormat(CultureInfo.InvariantCulture, ":{0}", session.Id);
                }
            }
            m_output.WriteLine(item.ToString());
        }

        /// <summary>
        /// Status thread, prints connection status every 10 seconds.
        /// </summary>
        private async Task StatusThreadAsync()
        {
            while (Server != null)
            {
                if (DateTime.UtcNow - m_lastEventTime > TimeSpan.FromMilliseconds(10000))
                {
                    IList<ISession> sessions = Server.CurrentInstance.SessionManager.GetSessions();
                    for (int ii = 0; ii < sessions.Count; ii++)
                    {
                        ISession session = sessions[ii];
                        PrintSessionStatus(session, "-Status-", true);
                    }
                    m_lastEventTime = DateTime.UtcNow;
                }
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        #region Private Members
        private readonly TextWriter m_output;
        private Task m_status;
        private DateTime m_lastEventTime;
        #endregion
    }
}
