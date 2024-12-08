/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace Quickstarts.ConsoleReferenceClient
{
    /// <summary>
    /// The program.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point.
        /// </summary>
        public static async Task Main(string[] args)
        {
            TextWriter output = Console.Out;
            output.WriteLine("OPC UA Console Reference Client");

            output.WriteLine("OPC UA library: {0} @ {1} -- {2}",
                Utils.GetAssemblyBuildNumber(),
                Utils.GetAssemblyTimestamp().ToString("G", CultureInfo.InvariantCulture),
                Utils.GetAssemblySoftwareVersion());

            // The application name and config file names
            var applicationName = "ConsoleReferenceClient";
            var configSectionName = "Quickstarts.ReferenceClient";
            var usage = $"Usage: dotnet {applicationName}.dll [OPTIONS]";

            // command line options
            bool showHelp = false;
            bool autoAccept = false;
            string username = null;
            string userpassword = null;
            string userCertificateThumbprint = null;
            string userCertificatePassword = null;
            bool logConsole = false;
            bool appLog = false;
            bool renewCertificate = false;
            bool loadTypes = false;
            bool managedbrowseall = false;
            bool browseall = false;
            bool fetchall = false;
            bool jsonvalues = false;
            bool verbose = false;
            bool subscribe = false;
            bool noSecurity = false;
            string password = null;
            int timeout = Timeout.Infinite;
            string logFile = null;
            string reverseConnectUrlString = null;
            bool leakChannels = false;
            bool forever = false;

            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                usage,
                { "h|help", "show this message and exit", h => showHelp = h != null },
                { "a|autoaccept", "auto accept certificates (for testing only)", a => autoAccept = a != null },
                { "nsec|nosecurity", "select endpoint with security NONE, least secure if unavailable", s => noSecurity = s != null },
                { "un|username=", "the name of the user identity for the connection", (string u) => username = u },
                { "up|userpassword=", "the password of the user identity for the connection", (string u) => userpassword = u },
                { "uc|usercertificate=", "the thumbprint of the user certificate for the user identity", (string u) => userCertificateThumbprint = u },
                { "ucp|usercertificatepassword=", "the password of the  user certificate for the user identity", (string u) => userCertificatePassword = u },
                { "c|console", "log to console", c => logConsole = c != null },
                { "l|log", "log app output", c => appLog = c != null },
                { "p|password=", "optional password for private key", (string p) => password = p },
                { "r|renew", "renew application certificate", r => renewCertificate = r != null },
                { "t|timeout=", "timeout in seconds to exit application", (int t) => timeout = t * 1000 },
                { "logfile=", "custom file name for log output", l => { if (l != null) { logFile = l; } } },
                { "lt|loadtypes", "Load custom types", lt => { if (lt != null) loadTypes = true; } },
                { "m|managedbrowseall", "Browse all references using the MangedBrowseAsync method", m => { if (m != null) managedbrowseall = true; } },
                { "b|browseall", "Browse all references", b => { if (b != null) browseall = true; } },
                { "f|fetchall", "Fetch all nodes", f => { if (f != null) fetchall = true; } },
                { "j|json", "Output all Values as JSON", j => { if (j != null) jsonvalues = true; } },
                { "v|verbose", "Verbose output", v => { if (v != null) verbose = true; } },
                { "s|subscribe", "Subscribe", s => { if (s != null) subscribe = true; } },
                { "rc|reverseconnect=", "Connect using the reverse connect endpoint. (e.g. rc=opc.tcp://localhost:65300)", (string url) => reverseConnectUrlString = url},
                { "forever", "run inner connect/disconnect loop forever", f => { if (f != null) forever = true; } },
                { "leakchannels", "Leave a channel leak open when disconnecting a session.", l => { if (l != null) leakChannels = true; } },
            };

            ReverseConnectManager reverseConnectManager = null;

            try
            {
                // parse command line and set options
                var extraArg = ConsoleUtils.ProcessCommandLine(output, args, options, ref showHelp, "REFCLIENT", false);

                // connect Url?
                Uri serverUrl = new Uri("opc.tcp://localhost:62541/Quickstarts/ReferenceServer");
                if (!string.IsNullOrEmpty(extraArg))
                {
                    serverUrl = new Uri(extraArg);
                }

                // log console output to logger
                if (logConsole && appLog)
                {
                    output = new LogWriter();
                }

                // Define the UA Client application
                ApplicationInstance.MessageDlg = new ApplicationMessageDlg(output);
                CertificatePasswordProvider PasswordProvider = new CertificatePasswordProvider(password);
                ApplicationInstance application = new ApplicationInstance {
                    ApplicationName = applicationName,
                    ApplicationType = ApplicationType.Client,
                    ConfigSectionName = configSectionName,
                    CertificatePasswordProvider = PasswordProvider
                };

                // load the application configuration.
                var config = await application.LoadApplicationConfiguration(silent: false).ConfigureAwait(false);

                // override logfile
                if (logFile != null)
                {
                    var logFilePath = config.TraceConfiguration.OutputFilePath;
                    var filename = Path.GetFileNameWithoutExtension(logFilePath);
                    config.TraceConfiguration.OutputFilePath = logFilePath.Replace(filename, logFile);
                    config.TraceConfiguration.DeleteOnLoad = true;
                    config.TraceConfiguration.ApplySettings();
                }

                // setup the logging
                ConsoleUtils.ConfigureLogging(config, applicationName, logConsole, LogLevel.Information);

                // delete old certificate
                if (renewCertificate)
                {
                    await application.DeleteApplicationInstanceCertificate().ConfigureAwait(false);
                }

                // check the application certificate.
                bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(false, minimumKeySize: 0).ConfigureAwait(false);
                if (!haveAppCertificate)
                {
                    throw new ErrorExitException("Application instance certificate invalid!", ExitCode.ErrorCertificate);
                }

                if (reverseConnectUrlString != null)
                {
                    // start the reverse connection manager
                    output.WriteLine("Create reverse connection endpoint at {0}.", reverseConnectUrlString);
                    reverseConnectManager = new ReverseConnectManager();
                    reverseConnectManager.AddEndpoint(new Uri(reverseConnectUrlString));
                    reverseConnectManager.StartService(config);
                }

                // wait for timeout or Ctrl-C
                var quitCTS = new CancellationTokenSource();
                var quitEvent = ConsoleUtils.CtrlCHandler(quitCTS);

                // connect to a server until application stops
                bool quit = false;
                DateTime start = DateTime.UtcNow;
                int waitTime = int.MaxValue;
                do
                {
                    if (timeout > 0)
                    {
                        waitTime = timeout - (int)DateTime.UtcNow.Subtract(start).TotalMilliseconds;
                        if (waitTime <= 0)
                        {
                            if (!forever)
                            {
                                break;
                            }
                            else
                            {
                                waitTime = 0;
                            }
                        }

                        if (forever)
                        {
                            start = DateTime.UtcNow;
                        }
                    }

                    // create the UA Client object and connect to configured server.
                    using (UAClient uaClient = new UAClient(application.ApplicationConfiguration, reverseConnectManager, output, ClientBase.ValidateResponse) {
                        AutoAccept = autoAccept,
                        SessionLifeTime = 60_000,
                    })
                    {
                        // set user identity of type username/pw
                        if (!string.IsNullOrEmpty(username))
                        {
                            uaClient.UserIdentity = new UserIdentity(username, userpassword ?? string.Empty);
                        }

                        // set user identity of type certificate
                        if (!string.IsNullOrEmpty(userCertificateThumbprint))
                        {
                            CertificateIdentifier userCertificateIdentifier =
                                await FindUserCertificateIdentifierAsync(userCertificateThumbprint,
                                    application.ApplicationConfiguration.SecurityConfiguration.TrustedUserCertificates);

                            if (userCertificateIdentifier != null)
                            {
                                uaClient.UserIdentity = new UserIdentity(userCertificateIdentifier, new CertificatePasswordProvider(userCertificatePassword ?? string.Empty));
                            }
                            else
                            {
                                output.WriteLine($"Failed to load user certificate with Thumbprint {userCertificateThumbprint}");
                            }
                        }

                        bool connected = await uaClient.ConnectAsync(serverUrl.ToString(), !noSecurity, quitCTS.Token).ConfigureAwait(false);
                        if (connected)
                        {
                            output.WriteLine("Connected! Ctrl-C to quit.");

                            // enable subscription transfer
                            uaClient.ReconnectPeriod = 1000;
                            uaClient.ReconnectPeriodExponentialBackoff = 10000;
                            uaClient.Session.MinPublishRequestCount = 3;
                            uaClient.Session.TransferSubscriptionsOnReconnect = true;
                            var samples = new ClientSamples(output, ClientBase.ValidateResponse, quitEvent, verbose);
                            if (loadTypes)
                            {
                                var complexTypeSystem = await samples.LoadTypeSystemAsync(uaClient.Session).ConfigureAwait(false);
                            }

                            if (browseall || fetchall || jsonvalues || managedbrowseall)
                            {
                                NodeIdCollection variableIds = null;
                                NodeIdCollection variableIdsManagedBrowse = null;
                                ReferenceDescriptionCollection referenceDescriptions = null;
                                ReferenceDescriptionCollection referenceDescriptionsFromManagedBrowse = null;

                                if (browseall)
                                {
                                    output.WriteLine("Browse the full address space.");
                                    referenceDescriptions =
                                        await samples.BrowseFullAddressSpaceAsync(uaClient, Objects.RootFolder).ConfigureAwait(false);
                                    variableIds = new NodeIdCollection(referenceDescriptions
                                        .Where(r => r.NodeClass == NodeClass.Variable && r.TypeDefinition.NamespaceIndex != 0)
                                        .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, uaClient.Session.NamespaceUris)));
                                }

                                if (managedbrowseall)
                                {
                                    output.WriteLine("ManagedBrowse the full address space.");
                                    referenceDescriptionsFromManagedBrowse =
                                        await samples.ManagedBrowseFullAddressSpaceAsync(uaClient, Objects.RootFolder).ConfigureAwait(false);
                                    variableIdsManagedBrowse = new NodeIdCollection(referenceDescriptionsFromManagedBrowse
                                        .Where(r => r.NodeClass == NodeClass.Variable && r.TypeDefinition.NamespaceIndex != 0)
                                        .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, uaClient.Session.NamespaceUris)));
                                }

                                // treat managedBrowseall result like browseall results if the latter is missing
                                if (!browseall && managedbrowseall)
                                {
                                    referenceDescriptions = referenceDescriptionsFromManagedBrowse;
                                    browseall = managedbrowseall;
                                }

                                IList<INode> allNodes = null;
                                if (fetchall)
                                {
                                    allNodes = await samples.FetchAllNodesNodeCacheAsync(uaClient, Objects.RootFolder, true, true, false).ConfigureAwait(false);
                                    variableIds = new NodeIdCollection(allNodes
                                        .Where(r => r.NodeClass == NodeClass.Variable && r is VariableNode && ((VariableNode)r).DataType.NamespaceIndex != 0)
                                        .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, uaClient.Session.NamespaceUris)));
                                }

                                if (jsonvalues && variableIds != null)
                                {
                                    var (allValues, results) = await samples.ReadAllValuesAsync(uaClient, variableIds).ConfigureAwait(false);
                                }

                                if (subscribe && (browseall || fetchall))
                                {
                                    // subscribe to 1000 random variables
                                    const int MaxVariables = 1000;
                                    NodeCollection variables = new NodeCollection();
                                    Random random = new Random(62541);
                                    if (fetchall)
                                    {
                                        variables.AddRange(allNodes
                                            .Where(r => r.NodeClass == NodeClass.Variable && r.NodeId.NamespaceIndex > 1)
                                            .Select(r => ((VariableNode)r))
                                            .OrderBy(o => random.Next())
                                            .Take(MaxVariables));
                                    }
                                    else if (browseall)
                                    {
                                        var variableReferences = referenceDescriptions
                                            .Where(r => r.NodeClass == NodeClass.Variable && r.NodeId.NamespaceIndex > 1)
                                            .Select(r => r.NodeId)
                                            .OrderBy(o => random.Next())
                                            .Take(MaxVariables)
                                            .ToList();
                                        variables.AddRange(uaClient.Session.NodeCache.Find(variableReferences).Cast<Node>());
                                    }

                                    await samples.SubscribeAllValuesAsync(uaClient,
                                        variableIds: new NodeCollection(variables),
                                        samplingInterval: 100,
                                        publishingInterval: 1000,
                                        queueSize: 10,
                                        lifetimeCount: 60,
                                        keepAliveCount: 2).ConfigureAwait(false);

                                    // Wait for DataChange notifications from MonitoredItems
                                    output.WriteLine("Subscribed to {0} variables. Press Ctrl-C to exit.", MaxVariables);

                                    // free unused memory
                                    uaClient.Session.NodeCache.Clear();

                                    waitTime = timeout - (int)DateTime.UtcNow.Subtract(start).TotalMilliseconds;
                                    DateTime endTime = waitTime > 0 ? DateTime.UtcNow.Add(TimeSpan.FromMilliseconds(waitTime)) : DateTime.MaxValue;
                                    var variableIterator = variables.GetEnumerator();
                                    while (!quit && endTime > DateTime.UtcNow)
                                    {
                                        if (variableIterator.MoveNext())
                                        {
                                            try
                                            {
                                                var value = await uaClient.Session.ReadValueAsync(variableIterator.Current.NodeId).ConfigureAwait(false);
                                                output.WriteLine("Value of {0} is {1}", variableIterator.Current.NodeId, value);
                                            }
                                            catch (Exception ex)
                                            {
                                                output.WriteLine("Error reading value of {0}: {1}", variableIterator.Current.NodeId, ex.Message);
                                            }
                                        }
                                        else
                                        {
                                            variableIterator = variables.GetEnumerator();
                                        }
                                        quit = quitEvent.WaitOne(500);
                                    }
                                }
                                else
                                {
                                    quit = true;
                                }
                            }
                            else
                            {
                                // Run tests for available methods on reference server.
                                samples.ReadNodes(uaClient.Session);
                                samples.WriteNodes(uaClient.Session);
                                samples.Browse(uaClient.Session);
                                samples.CallMethod(uaClient.Session);
                                samples.SubscribeToDataChanges(uaClient.Session, 120_000);

                                output.WriteLine("Waiting...");

                                // Wait for some DataChange notifications from MonitoredItems
                                quit = quitEvent.WaitOne(timeout > 0 ? waitTime : 30_000);
                            }

                            output.WriteLine("Client disconnected.");

                            uaClient.Disconnect(leakChannels);
                        }
                        else
                        {
                            output.WriteLine("Could not connect to server! Retry in 10 seconds or Ctrl-C to quit.");
                            quit = quitEvent.WaitOne(Math.Min(10_000, waitTime));
                        }
                    }

                } while (!quit);

                output.WriteLine("Client stopped.");
            }
            catch (Exception ex)
            {
                output.WriteLine(ex.Message);
            }
            finally
            {
                Utils.SilentDispose(reverseConnectManager);
                output.Close();
            }
        }
        /// <summary>
        /// returns a CertificateIdentifier of the Certificate with the specified thumbprint if it is found in the trustedUserCertificates TrustList
        /// </summary>
        /// <param name="thumbprint">the thumbprint of the certificate to select</param>
        /// <param name="trustedUserCertificates">the trustlist of the user certificates</param>
        /// <returns>Certificate Identifier</returns>
        private static async Task<CertificateIdentifier> FindUserCertificateIdentifierAsync(string thumbprint, CertificateTrustList trustedUserCertificates)
        {
            CertificateIdentifier userCertificateIdentifier = null;

            // get user certificate with matching thumbprint
            X509Certificate2Collection userCertifiactesWithMatchingThumbprint =
                (await trustedUserCertificates
                .GetCertificates())
                .Find(X509FindType.FindByThumbprint, thumbprint, false);

            // create Certificate Identifier
            if (userCertifiactesWithMatchingThumbprint.Count == 1)
            {
                userCertificateIdentifier = new CertificateIdentifier(userCertifiactesWithMatchingThumbprint[0]) {
                    StorePath = trustedUserCertificates.StorePath,
                    StoreType = trustedUserCertificates.StoreType
                };
            }

            return userCertificateIdentifier;
        }
    }
}
