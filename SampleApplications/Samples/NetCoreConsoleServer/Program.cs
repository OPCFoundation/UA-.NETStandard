/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mono.Options;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Sample;
using Opc.Ua.Server;

namespace NetCoreConsoleServer
{
    public class ApplicationMessageDlg : IApplicationMessageDlg
    {
        private string message = string.Empty;
        private bool ask = false;

        public override void Message(string text, bool ask)
        {
            this.message = text;
            this.ask = ask;
        }

        public override async Task<bool> ShowAsync()
        {
            if (ask)
            {
                message += " (y/n, default y): ";
                Console.Write(message);
            }
            else
            {
                Console.WriteLine(message);
            }
            if (ask)
            {
                try
                {
                    ConsoleKeyInfo result = Console.ReadKey();
                    Console.WriteLine();
                    return await Task.FromResult((result.KeyChar == 'y') || (result.KeyChar == 'Y') || (result.KeyChar == '\r'));
                }
                catch
                {
                    // intentionally fall through
                }
            }
            return await Task.FromResult(true);
        }
    }

    public enum ExitCode : int
    {
        Ok = 0,
        ErrorServerNotStarted = 0x80,
        ErrorServerRunning = 0x81,
        ErrorServerException = 0x82,
        ErrorInvalidCommandLine = 0x100
    };

    public class Program
    {

        public static int Main(string[] args)
        {
            Console.WriteLine(
                (Utils.IsRunningOnMono() ? "Mono" : ".Net Core") + 
                " OPC UA Console Server sample");


            // command line options
            bool showHelp = false;
            int stopTimeout = 0;
            bool autoAccept = false;

            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                { "h|help", "show this message and exit", h => showHelp = h != null },
                { "a|autoaccept", "auto accept certificates (for testing only)", a => autoAccept = a != null },
                { "t|timeout=", "the number of seconds until the server stops.", (int t) => stopTimeout = t }
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
                Console.WriteLine(Utils.IsRunningOnMono() ? "Usage: mono MonoConsoleServer.exe [OPTIONS]" : "Usage: dotnet NetCoreConsoleServer.dll [OPTIONS]" );
                Console.WriteLine();

                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return (int)ExitCode.ErrorInvalidCommandLine;
            }

            MySampleServer server = new MySampleServer(autoAccept, stopTimeout);
            server.Run();

            return (int)MySampleServer.ExitCode;
        }
    }

    public class MySampleServer
    {
        SampleServer server;
        Task status;
        DateTime lastEventTime;
        int serverRunTime = Timeout.Infinite;
        static bool autoAccept = false;
        static ExitCode exitCode;

        public MySampleServer(bool _autoAccept, int _stopTimeout)
        {
            autoAccept = _autoAccept;
            serverRunTime = _stopTimeout == 0 ? Timeout.Infinite : _stopTimeout * 1000;
        }

        public void Run()
        {

            try
            {
                exitCode = ExitCode.ErrorServerNotStarted;
                ConsoleSampleServer().Wait();
                Console.WriteLine("Server started. Press Ctrl-C to exit...");
                exitCode = ExitCode.ErrorServerRunning;
            }
            catch (Exception ex)
            {
                Utils.Trace("ServiceResultException:" + ex.Message);
                Console.WriteLine("Exception: {0}", ex.Message);
                exitCode = ExitCode.ErrorServerException;
                return;
            }

            ManualResetEvent quitEvent = new ManualResetEvent(false);
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
            quitEvent.WaitOne(serverRunTime);

            if (server != null)
            {
                Console.WriteLine("Server stopped. Waiting for exit...");

                using (SampleServer _server = server)
                {
                    // Stop status thread
                    server = null;
                    status.Wait();
                    // Stop server and dispose
                    _server.Stop();
                }
            }

            exitCode = ExitCode.Ok;
        }

        public static ExitCode ExitCode { get => exitCode; }

        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = autoAccept;
                if (autoAccept)
                {
                    Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
                }
                else
                {
                    Console.WriteLine("Rejected Certificate: {0}", e.Certificate.Subject);
                }
            }
        }

        private async Task ConsoleSampleServer()
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            ApplicationInstance application = new ApplicationInstance();

            application.ApplicationName = "UA Core Sample Server";
            application.ApplicationType = ApplicationType.Server;
            application.ConfigSectionName = Utils.IsRunningOnMono() ? "Opc.Ua.MonoSampleServer" : "Opc.Ua.SampleServer";

            // load the application configuration.
            ApplicationConfiguration config = await application.LoadApplicationConfiguration(false);

            // check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(false, 0);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            if (!config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }

            // start the server.
            server = new SampleServer();
            await application.Start(server);

            // start the status thread
            status = Task.Run(new Action(StatusThread));

            // print notification on session events
            server.CurrentInstance.SessionManager.SessionActivated += EventStatus;
            server.CurrentInstance.SessionManager.SessionClosing += EventStatus;
            server.CurrentInstance.SessionManager.SessionCreated += EventStatus;

        }

        private void EventStatus(Session session, SessionEventReason reason)
        {
            lastEventTime = DateTime.UtcNow;
            PrintSessionStatus(session, reason.ToString());
        }

        void PrintSessionStatus(Session session, string reason, bool lastContact = false)
        {
            lock (session.DiagnosticsLock)
            {
                string item = String.Format("{0,9}:{1,20}:", reason, session.SessionDiagnostics.SessionName);
                if (lastContact)
                {
                    item += String.Format("Last Event:{0:HH:mm:ss}", session.SessionDiagnostics.ClientLastContactTime.ToLocalTime());
                }
                else
                {
                    if (session.Identity != null)
                    {
                        item += String.Format(":{0,20}", session.Identity.DisplayName);
                    }
                    item += String.Format(":{0}", session.Id);
                }
                Console.WriteLine(item);
            }
        }

        private async void StatusThread()
        {
            while (server != null)
            {
                if (DateTime.UtcNow - lastEventTime > TimeSpan.FromMilliseconds(6000))
                {
                    IList<Session> sessions = server.CurrentInstance.SessionManager.GetSessions();
                    for (int ii = 0; ii < sessions.Count; ii++)
                    {
                        Session session = sessions[ii];
                        PrintSessionStatus(session, "-Status-", true);
                    }
                    lastEventTime = DateTime.UtcNow;
                }
                await Task.Delay(1000);
            }
        }
    }
}
