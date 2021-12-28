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
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// The program.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            TextWriter output = Console.Out;
            output.WriteLine("{0} OPC UA Reference Server", Utils.IsRunningOnMono() ? "Mono" : ".NET Core");

            // The application name and config file names
            var applicationName = Utils.IsRunningOnMono() ? "MonoReferenceServer" : "ConsoleReferenceServer";
            var configSectionName = Utils.IsRunningOnMono() ? "Quickstarts.MonoReferenceServer" : "Quickstarts.ReferenceServer";

            // command line options
            bool showHelp = false;
            bool autoAccept = false;
            bool logConsole = false;
            bool appLog = false;
            bool renewCertificate = false;
            string password = null;
            int timeout = -1;

            var usage = Utils.IsRunningOnMono() ? $"Usage: mono {applicationName}.exe [OPTIONS]" : $"Usage: dotnet {applicationName}.dll [OPTIONS]";
            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                usage,
                { "h|help", "show this message and exit", h => showHelp = h != null },
                { "a|autoaccept", "auto accept certificates (for testing only)", a => autoAccept = a != null },
                { "c|console", "log to console", c => logConsole = c != null },
                { "l|log", "log app output", c => appLog = c != null },
                { "p|password=", "optional password for private key", (string p) => password = p },
                { "r|renew", "renew application certificate", r => renewCertificate = r != null },
                { "t|timeout=", "timeout in seconds to exit application", (int t) => timeout = t * 1000 },
            };

            try
            {
                // parse command line and set options
                ConsoleUtils.ProcessCommandLine(output, args, options, ref showHelp);

                if (logConsole && appLog)
                {
                    output = new LogWriter();
                }

                // create the UA server
                var server = new UAServer<ReferenceServer>(output) {
                    AutoAccept = autoAccept,
                    Password = password
                };

                // load the server configuration, validate certificates
                output.WriteLine("Loading configuration from {0}.", configSectionName);
                await server.LoadAsync(applicationName, configSectionName).ConfigureAwait(false);

                // setup the logging
                ConsoleUtils.ConfigureLogging(server.Configuration, applicationName, logConsole, LogLevel.Information);

                // check or renew the certificate
                output.WriteLine("Check the certificate.");
                await server.CheckCertificateAsync(renewCertificate).ConfigureAwait(false);

                // start the server
                output.WriteLine("Start the server.");
                await server.StartAsync().ConfigureAwait(false);

                output.WriteLine("Server started. Press Ctrl-C to exit...");

                // wait for timeout or Ctrl-C
                var quitEvent = ConsoleUtils.CtrlCHandler();
                bool ctrlc = quitEvent.WaitOne(timeout);

                // stop server. May have to wait for clients to disconnect.
                output.WriteLine("Server stopped. Waiting for exit...");
                await server.StopAsync().ConfigureAwait(false);

                return (int)ExitCode.Ok;
            }
            catch (ErrorExitException eee)
            {
                output.WriteLine("The application exits with error: {0}", eee.Message);
                return (int)eee.ExitCode;
            }
        }
    }
}

