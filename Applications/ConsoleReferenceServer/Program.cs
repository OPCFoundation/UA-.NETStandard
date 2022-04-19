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
using System.Globalization;
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

            output.WriteLine("OPC UA library: {0} @ {1} -- {2}",
                Utils.GetAssemblyBuildNumber(),
                Utils.GetAssemblyTimestamp().ToString("G", CultureInfo.InvariantCulture),
                Utils.GetAssemblySoftwareVersion());

            // The application name and config file names
            var applicationName = Utils.IsRunningOnMono() ? "MonoReferenceServer" : "ConsoleReferenceServer";
            var configSectionName = Utils.IsRunningOnMono() ? "Quickstarts.MonoReferenceServer" : "Quickstarts.ReferenceServer";

            // command line options
            bool showHelp = false;
            bool autoAccept = false;
            bool logConsole = false;
            bool appLog = false;
            bool renewCertificate = false;
            bool shadowConfig = false;
            bool cttMode = false;
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
                { "s|shadowconfig", "create configuration in pki root", s => shadowConfig = s != null },
                { "ctt", "CTT mode, use to preset alarms for CTT testing.", c => cttMode = c != null },
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

                // use the shadow config to map the config to an externally accessible location
                if (shadowConfig)
                {
                    output.WriteLine("Using shadow configuration.");
                    var shadowPath = Directory.GetParent(Path.GetDirectoryName(
                        Utils.ReplaceSpecialFolderNames(server.Configuration.TraceConfiguration.OutputFilePath))).FullName;
                    var shadowFilePath = Path.Combine(shadowPath, Path.GetFileName(server.Configuration.SourceFilePath));
                    if (!File.Exists(shadowFilePath))
                    {
                        output.WriteLine("Create a copy of the config in the shadow location.");
                        File.Copy(server.Configuration.SourceFilePath, shadowFilePath, true);
                    }
                    output.WriteLine("Reloading configuration from {0}.", shadowFilePath);
                    await server.LoadAsync(applicationName, Path.Combine(shadowPath, configSectionName)).ConfigureAwait(false);
                }

                // setup the logging
                ConsoleUtils.ConfigureLogging(server.Configuration, applicationName, logConsole, LogLevel.Information);

                // check or renew the certificate
                output.WriteLine("Check the certificate.");
                await server.CheckCertificateAsync(renewCertificate).ConfigureAwait(false);

                // Create and add the node managers
                server.Create(Servers.Utils.NodeManagerFactories);

                // start the server
                output.WriteLine("Start the server.");
                await server.StartAsync().ConfigureAwait(false);

                // Apply custom settings for CTT testing
                if (cttMode)
                {
                    output.WriteLine("Apply settings for CTT.");
                    // start Alarms and other settings for CTT test
                    Quickstarts.Servers.Utils.ApplyCTTMode(output, server.Server);
                }

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

#if TODO
            StringBuilder xmlBomb = new StringBuilder();
            xmlBomb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
            xmlBomb.AppendLine("<!DOCTYPE lolz [<!ENTITY lol \"lol\">");
            xmlBomb.AppendLine("<!ENTITY lol1 \"&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;\" >");
            xmlBomb.AppendLine("<!ENTITY lol2 \"&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;\" >");
            xmlBomb.AppendLine("<!ENTITY lol3 \"&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;\" >");
            xmlBomb.AppendLine("<!ENTITY lol4 \"&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;\" >");
            xmlBomb.AppendLine("<!ENTITY lol5 \"&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;\" >");
            xmlBomb.AppendLine("<!ENTITY lol6 \"&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;\" >");
            xmlBomb.AppendLine("<!ENTITY lol7 \"&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;\" >");
            xmlBomb.AppendLine("<!ENTITY lol8 \"&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;\" >");
            xmlBomb.AppendLine("<!ENTITY lol9 \"&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;\" >]>");
            xmlBomb.AppendLine("<lolz>&lol9;</lolz>");

            try
            {
                XmlDocument document = new XmlDocument();
                document.LoadXml(xmlBomb.ToString()); // throws
                var element = document.DocumentElement;
            }
            catch (Exception)
            {

            }

            StringBuilder xxeBomb = new StringBuilder();
            xxeBomb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
            xxeBomb.AppendLine("<!DOCTYPE updateProfile [<!ENTITY file SYSTEM \"file:///c:/windows/win.ini\">");
            xxeBomb.AppendLine("]>");
            xxeBomb.AppendLine("<updateProfile><firstname>Joe</firstname><lastname>&file;</lastname></updateProfile>");

            try
            {
                XmlDocument document = new XmlDocument();
                document.LoadXml(xxeBomb.ToString());
                Console.WriteLine(document.InnerText); // win.ini is not loaded
                // with url resolver
                XmlDocument document2 = new XmlDocument() { XmlResolver = new XmlUrlResolver() };
                document2.LoadXml(xxeBomb.ToString());
                Console.WriteLine(document2.InnerText); // win.ini is loaded
            }
            catch (Exception)
            {

            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(new StringReader(xmlBomb.ToString()));
            }
            catch (Exception)
            {

            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.InnerXml = xmlBomb.ToString();
                var test = document.DocumentElement;
            }
            catch (Exception)
            {

            }

            try
            {
                XmlReaderSettings xmlReaderSettings = new XmlReaderSettings();
                XmlReader reader = XmlReader.Create(new StringReader(xmlBomb.ToString()), xmlReaderSettings);
                XmlDocument document = new XmlDocument();
                document.Load(reader);
                document.Load(xmlBomb.ToString());
            }
            catch (Exception)
            {

            }

public class UseXmlReaderForDeserialize
    {
        public void TestMethod(Stream stream)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(UseXmlReaderForDeserialize));
            serializer.Deserialize(stream); // warn
        }


    }

    class TestClass
    {
        public XmlSchema Test
        {
            get
            {
                var src = "";
                TextReader tr = new StreamReader(src);
                XmlSchema schema = XmlSchema.Read(tr, null); // warn
                return schema;
            }
        }
    }

    public class TestClass2
    {
        public XmlReaderSettings settings = new XmlReaderSettings();
        public void TestMethod(string path)
        {
            var reader = XmlReader.Create(path, settings);  // warn
        }
    }
#endif


