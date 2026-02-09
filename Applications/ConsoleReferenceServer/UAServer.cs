/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace Quickstarts
{
    public class UAServer<T> where T : StandardServer
    {
        public IApplicationInstance Application { get; private set; }

        public ApplicationConfiguration Configuration => Application.ApplicationConfiguration;

        public bool AutoAccept { get; set; }

        public char[] Password { get; set; }

        public ExitCode ExitCode { get; private set; }

        public T Server { get; private set; }

        /// <summary>
        /// Ctor of the server.
        /// </summary>
        /// <param name="telemetry">The telemetry context.</param>
        public UAServer(ITelemetryContext telemetry, Func<ITelemetryContext, T> factory)
        {
            m_factory = factory;
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<UAServer<T>>();
        }

        /// <summary>
        /// Load the application configuration.
        /// </summary>
        /// <exception cref="ErrorExitException"></exception>
        public async Task LoadAsync(string applicationName, string configSectionName)
        {
            try
            {
                ExitCode = ExitCode.ErrorNotStarted;

                ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
                var passwordProvider = new CertificatePasswordProvider(Password);
                Application = new ApplicationInstance(m_telemetry)
                {
                    ApplicationName = applicationName,
                    ApplicationType = ApplicationType.Server,
                    ConfigSectionName = configSectionName,
                    CertificatePasswordProvider = passwordProvider
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
        /// <exception cref="ErrorExitException"></exception>
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
        /// <exception cref="ErrorExitException"></exception>
        public void Create(IList<INodeManagerFactory> nodeManagerFactories)
        {
            try
            {
                // create the server.
                Server = m_factory(m_telemetry);
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
        /// <exception cref="ErrorExitException"></exception>
        public async Task StartAsync()
        {
            try
            {
                // create the server.
                Server ??= m_factory(m_telemetry);

                // start the server
                await Application.StartAsync(Server).ConfigureAwait(false);

                // save state
                ExitCode = ExitCode.ErrorRunning;

                // print endpoint info
                foreach (string endpoint in Application.Server.GetEndpoints().Select(e => e.EndpointUrl).Distinct())
                {
                    Console.WriteLine(endpoint);
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
        /// <exception cref="ErrorExitException"></exception>
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
                    await server.StopAsync().ConfigureAwait(false);
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
                m_logger.LogInformation(
                    "Accepted Certificate: [{Subject}] [{Thumbprint}]",
                    e.Certificate.Subject,
                    e.Certificate.Thumbprint
                );
                e.Accept = true;
                return;
            }
            m_logger.LogInformation(
                "Rejected Certificate: {Error} [{Subject}] [{Thumbprint}]",
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
            LogSessionStatusFull(session, reason.ToString());
        }

        /// <summary>
        /// Output the status of a connected session.
        /// </summary>
        private void LogSessionStatusLastContact(ISession session, string reason)
        {
            lock (session.DiagnosticsLock)
            {
                m_logger.LogInformation(
                    "{Reason,9}:{Session,20}:Last Event:{LastContactTime:HH:mm:ss}",
                    reason,
                    session.SessionDiagnostics.SessionName,
                    session.SessionDiagnostics.ClientLastContactTime.ToLocalTime()
                );
            }
        }

        /// <summary>
        /// Output the status of a connected session.
        /// </summary>
        private void LogSessionStatusFull(ISession session, string reason)
        {
            lock (session.DiagnosticsLock)
            {
                m_logger.LogInformation(
                    "{Reason,9}:{Session,20}:Last Event:{LastContactTime:HH:mm:ss}:{UserIdentity,20}:{SessionId}",
                    reason,
                    session.SessionDiagnostics.SessionName,
                    session.SessionDiagnostics.ClientLastContactTime.ToLocalTime(),
                    session.Identity?.DisplayName ?? "Anonymous",
                    session.Id
                );
            }
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
                        LogSessionStatusLastContact(session, "-Status-");
                    }
                    m_lastEventTime = DateTime.UtcNow;
                }
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A dialog which asks for user input.
        /// </summary>
        public class ApplicationMessageDlg : IApplicationMessageDlg
        {
            private readonly TextWriter m_output;
            private string m_message = string.Empty;
            private bool m_ask;

            public ApplicationMessageDlg(TextWriter output = null)
            {
                m_output = output ?? Console.Out;
            }

            public override void Message(string text, bool ask)
            {
                m_message = text;
                m_ask = ask;
            }

            public override async Task<bool> ShowAsync()
            {
                if (m_ask)
                {
                    var message = new StringBuilder(m_message);
                    message.Append(" (y/n, default y): ");
                    m_output.Write(message.ToString());

                    try
                    {
                        ConsoleKeyInfo result = Console.ReadKey();
                        m_output.WriteLine();
                        return await Task.FromResult(result.KeyChar is 'y' or 'Y' or '\r')
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // intentionally fall through
                    }
                }
                else
                {
                    m_output.WriteLine(m_message);
                }

                return await Task.FromResult(true).ConfigureAwait(false);
            }
        }

        private readonly Func<ITelemetryContext, T> m_factory;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private Task m_status;
        private DateTime m_lastEventTime;
    }
}
