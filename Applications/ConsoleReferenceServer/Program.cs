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

using Mono.Options;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// A dialog which asks for user input.
    /// </summary>
    public class ApplicationMessageDlg : IApplicationMessageDlg
    {
        private string m_message = string.Empty;
        private bool m_ask = false;

        public override void Message(string text, bool ask)
        {
            m_message = text;
            m_ask = ask;
        }

        public override async Task<bool> ShowAsync()
        {
            if (m_ask)
            {
                m_message += " (y/n, default y): ";
                Console.Write(m_message);
            }
            else
            {
                Console.WriteLine(m_message);
            }
            if (m_ask)
            {
                try
                {
                    ConsoleKeyInfo result = Console.ReadKey();
                    Console.WriteLine();
                    return await Task.FromResult((result.KeyChar == 'y') || (result.KeyChar == 'Y') || (result.KeyChar == '\r')).ConfigureAwait(false);
                }
                catch
                {
                    // intentionally fall through
                }
            }
            return await Task.FromResult(true).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The error code why the server exited.
    /// </summary>
    public enum ExitCode : int
    {
        Ok = 0,
        ErrorServerNotStarted = 0x80,
        ErrorServerRunning = 0x81,
        ErrorServerException = 0x82,
        ErrorInvalidCommandLine = 0x100
    };

    /// <summary>
    /// The program.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine("{0} OPC UA Reference Server", Utils.IsRunningOnMono() ? "Mono" : ".Net Core");

            // command line options
            bool showHelp = false;
            bool autoAccept = false;
            bool console = false;
            string password = null;

            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                { "h|help", "show this message and exit", h => showHelp = h != null },
                { "a|autoaccept", "auto accept certificates (for testing only)", a => autoAccept = a != null },
                { "c|console", "log trace to console", c => console = c != null },
                { "p|password=", "optional password for private key", (string p) => password = p }
            };

            try
            {
                IList<string> extraArgs = options.Parse(args);
                foreach (string extraArg in extraArgs)
                {
                    Console.WriteLine("Error: Unknown option: {0}", extraArg);
                    showHelp = true;
                }
            }
            catch (OptionException e)
            {
                Console.WriteLine(e.Message);
                showHelp = true;
            }

            if (showHelp)
            {
                Console.WriteLine(Utils.IsRunningOnMono() ? "Usage: mono MonoReferenceServer.exe [OPTIONS]" : "Usage: dotnet ConsoleReferenceServer.dll [OPTIONS]");
                Console.WriteLine();

                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return (int)ExitCode.ErrorInvalidCommandLine;
            }

            var server = new MyRefServer() {
                AutoAccept = autoAccept,
                LogConsole = console,
                Password = password
            };
            await server.Run().ConfigureAwait(false);

            return (int)server.ExitCode;
        }
    }

    public class MyRefServer
    {
        private ReferenceServer m_server;
        private Task m_status;
        private DateTime m_lastEventTime;
        public bool LogConsole { get; set; } = false;
        public bool AutoAccept { get; set; } = false;
        public string Password { get; set; } = null;
        public ExitCode ExitCode { get; private set; }

        public async Task Run()
        {
            try
            {
                ExitCode = ExitCode.ErrorServerNotStarted;
                await ConsoleSampleServer().ConfigureAwait(false);
                Console.WriteLine("Server started. Press Ctrl-C to exit...");
                ExitCode = ExitCode.ErrorServerRunning;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception: {0}", ex.Message);
                ExitCode = ExitCode.ErrorServerException;
                return;
            }

            var quitEvent = new ManualResetEvent(false);
            try
            {
                Console.CancelKeyPress += (sender, eArgs) => {
                    quitEvent.Set();
                    eArgs.Cancel = true;
                };
            }
            catch
            {
            }

            // wait for timeout or Ctrl-C
            quitEvent.WaitOne();

            if (m_server != null)
            {
                Console.WriteLine("Server stopped. Waiting for exit...");

                using (ReferenceServer server = m_server)
                {
                    // Stop status thread
                    m_server = null;
                    m_status.Wait();
                    // Stop server and dispose
                    server.Stop();
                }
            }

            ExitCode = ExitCode.Ok;
        }

        private void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                if (AutoAccept)
                {
                    if (!LogConsole)
                    {
                        Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
                    }
                    Utils.Trace(Utils.TraceMasks.Security, "Accepted Certificate: {0}", e.Certificate.Subject);
                    e.Accept = true;
                    return;
                }
            }
            if (!LogConsole)
            {
                Console.WriteLine("Rejected Certificate: {0} {1}", e.Error, e.Certificate.Subject);
            }
            Utils.Trace(Utils.TraceMasks.Security, "Rejected Certificate: {0} {1}", e.Error, e.Certificate.Subject);
        }

        private async Task ConsoleSampleServer()
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            CertificatePasswordProvider PasswordProvider = new CertificatePasswordProvider(Password);
            ApplicationInstance application = new ApplicationInstance {
                ApplicationName = "Quickstart Reference Server",
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = Utils.IsRunningOnMono() ? "Quickstarts.MonoReferenceServer" : "Quickstarts.ReferenceServer",
                CertificatePasswordProvider = PasswordProvider
            };

            // load the application configuration.
            ApplicationConfiguration config = await application.LoadApplicationConfiguration(false).ConfigureAwait(false);

            var loggerConfiguration = new Serilog.LoggerConfiguration();
            if (LogConsole)
            {
                loggerConfiguration.WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning);
            }
#if DEBUG
            else
            {
                loggerConfiguration.WriteTo.Debug(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning);
            }
#endif
            SerilogTraceLogger.Create(loggerConfiguration, config);

            // check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(
                false, CertificateFactory.DefaultKeySize, CertificateFactory.DefaultLifeTime).ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            if (!config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }

            // start the server.
            m_server = new ReferenceServer();
            await application.Start(m_server).ConfigureAwait(false);

            // print endpoint info
            var endpoints = application.Server.GetEndpoints().Select(e => e.EndpointUrl).Distinct();
            foreach (var endpoint in endpoints)
            {
                Console.WriteLine(endpoint);
            }

            // start the status thread
            m_status = Task.Run(new Action(StatusThread));

            // print notification on session events
            m_server.CurrentInstance.SessionManager.SessionActivated += EventStatus;
            m_server.CurrentInstance.SessionManager.SessionClosing += EventStatus;
            m_server.CurrentInstance.SessionManager.SessionCreated += EventStatus;
        }

        private void EventStatus(Session session, SessionEventReason reason)
        {
            m_lastEventTime = DateTime.UtcNow;
            PrintSessionStatus(session, reason.ToString());
        }

        private void PrintSessionStatus(Session session, string reason, bool lastContact = false)
        {
            lock (session.DiagnosticsLock)
            {
                StringBuilder item = new StringBuilder();
                item.AppendFormat("{0,9}:{1,20}:", reason, session.SessionDiagnostics.SessionName);
                if (lastContact)
                {
                    item.AppendFormat("Last Event:{0:HH:mm:ss}", session.SessionDiagnostics.ClientLastContactTime.ToLocalTime());
                }
                else
                {
                    if (session.Identity != null)
                    {
                        item.AppendFormat(":{0,20}", session.Identity.DisplayName);
                    }
                    item.AppendFormat(":{0}", session.Id);
                }
                Console.WriteLine(item.ToString());
            }
        }

        private async void StatusThread()
        {
            while (m_server != null)
            {
                if (DateTime.UtcNow - m_lastEventTime > TimeSpan.FromMilliseconds(6000))
                {
                    IList<Session> sessions = m_server.CurrentInstance.SessionManager.GetSessions();
                    for (int ii = 0; ii < sessions.Count; ii++)
                    {
                        Session session = sessions[ii];
                        PrintSessionStatus(session, "-Status-", true);
                    }
                    m_lastEventTime = DateTime.UtcNow;
                }
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }
}
