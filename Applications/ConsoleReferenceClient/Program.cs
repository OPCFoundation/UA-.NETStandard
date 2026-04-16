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
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;
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
        /// <exception cref="ErrorExitException"></exception>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification = "NodeSet2 export uses XmlSerializer with known OPC UA types.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "NodeSet2 export uses XmlSerializer with known OPC UA types.")]
        public static Task<int> Main(string[] args)
        {
            Console.WriteLine("OPC UA Console Reference Client");

            Console.WriteLine(
                "OPC UA library: {0} @ {1} -- {2}",
                Utils.GetAssemblyBuildNumber(),
                Utils.GetAssemblyTimestamp().ToString("G", CultureInfo.InvariantCulture),
                Utils.GetAssemblySoftwareVersion()
            );

            // The application name and config file names
            const string applicationName = "ConsoleReferenceClient";
            const string configSectionName = "Quickstarts.ReferenceClient";

            // command line options
            var autoAcceptOption = new Option<bool>("--autoaccept", "-a")
            {
                Description = "auto accept certificates (for testing only)"
            };
            var noSecurityOption = new Option<bool>("--nosecurity", "--nsec")
            {
                Description = "select endpoint with security NONE, least secure if unavailable"
            };
            var usernameOption = new Option<string>("--username", "--un")
            {
                Description = "the name of the user identity for the connection"
            };
            var userPasswordOption = new Option<string>("--userpassword", "--up")
            {
                Description = "the password of the user identity for the connection"
            };
            var userCertificateOption = new Option<string>("--usercertificate", "--uc")
            {
                Description = "the thumbprint of the user certificate for the user identity"
            };
            var userCertificatePasswordOption = new Option<string>("--usercertificatepassword", "--ucp")
            {
                Description = "the password of the user certificate for the user identity"
            };
            var consoleOption = new Option<bool>("--console", "-c") { Description = "log to console" };
            var logOption = new Option<bool>("--log", "-l") { Description = "log app output" };
            var fileOption = new Option<bool>("--file", "-f") { Description = "log to file" };
            var passwordOption = new Option<string>("--password", "-p")
            {
                Description = "optional password for private key"
            };
            var renewOption = new Option<bool>("--renew", "-r")
            {
                Description = "renew application certificate"
            };
            var timeoutOption = new Option<int>("--timeout", "-t")
            {
                Description = "timeout in seconds to exit application",
                DefaultValueFactory = _ => Timeout.Infinite
            };
            var logFileOption = new Option<string>("--logfile")
            {
                Description = "custom file name for log output"
            };
            var loadTypesOption = new Option<bool>("--loadtypes", "--lt")
            {
                Description = "Load custom types"
            };
            var managedBrowseAllOption = new Option<bool>("--managedbrowseall", "-m")
            {
                Description = "Browse all references using the MangedBrowseAsync method"
            };
            var browseAllOption = new Option<bool>("--browseall", "-b")
            {
                Description = "Browse all references"
            };
            var exportOption = new Option<bool>("--export", "-e")
            {
                Description = "Export all fetched nodes into Nodeset2 xml per default"
            };
            var fetchAllOption = new Option<bool>("--fetchall", "--fa")
            {
                Description = "Fetch all nodes"
            };
            var jsonOption = new Option<bool>("--json", "-j")
            {
                Description = "Output all Values as JSON"
            };
            var verboseOption = new Option<bool>("--verbose", "-v") { Description = "Verbose output" };
            var subscribeOption = new Option<bool>("--subscribe", "-s") { Description = "Subscribe" };
            var testallEndpointsOption = new Option<bool>("--testall", "--ea") { Description = "Test All Endpoints" };
            var reverseConnectOption = new Option<string>("--reverseconnect", "--rc")
            {
                Description = "Connect using the reverse connect endpoint. (e.g. --rc opc.tcp://localhost:65300)"
            };
            var foreverOption = new Option<bool>("--forever")
            {
                Description = "run inner connect/disconnect loop forever"
            };
            var leakChannelsOption = new Option<bool>("--leakchannels")
            {
                Description = "Leave a channel leak open when disconnecting a session."
            };
            var durableSubscriptionOption = new Option<bool>("--durablesubscription", "--ds")
            {
                Description = "SetDurableSubscription example"
            };
            var serverUrlArgument = new Argument<string>("serverUrl")
            {
                Description = "The OPC UA server URL to connect to",
                DefaultValueFactory = _ => "opc.tcp://localhost:62541/Quickstarts/ReferenceServer"
            };

            var rootCommand = new RootCommand($"Usage: dotnet {applicationName}.dll [OPTIONS] [serverUrl]")
            {
                autoAcceptOption,
                noSecurityOption,
                usernameOption,
                userPasswordOption,
                userCertificateOption,
                userCertificatePasswordOption,
                consoleOption,
                logOption,
                fileOption,
                passwordOption,
                renewOption,
                timeoutOption,
                logFileOption,
                loadTypesOption,
                managedBrowseAllOption,
                browseAllOption,
                exportOption,
                fetchAllOption,
                jsonOption,
                verboseOption,
                subscribeOption,
                testallEndpointsOption,
                reverseConnectOption,
                foreverOption,
                leakChannelsOption,
                durableSubscriptionOption,
                serverUrlArgument
            };

            rootCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                bool autoAccept = parseResult.GetValue(autoAcceptOption);
                bool noSecurity = parseResult.GetValue(noSecurityOption);
                string username = parseResult.GetValue(usernameOption);
                byte[] userpassword =
                    parseResult.GetValue(userPasswordOption) is string upStr ?
                        Encoding.UTF8.GetBytes(upStr) : null;
                string userCertificateThumbprint =
                    parseResult.GetValue(userCertificateOption);
                byte[] userCertificatePassword =
                    parseResult.GetValue(userCertificatePasswordOption) is string ucpStr ?
                        Encoding.UTF8.GetBytes(ucpStr) : null;
                bool logConsole = parseResult.GetValue(consoleOption);
                bool appLog = parseResult.GetValue(logOption);
                bool fileLog = parseResult.GetValue(fileOption);
                byte[] pfxPassword =
                    parseResult.GetValue(passwordOption) is string pfxStr ?
                        Encoding.UTF8.GetBytes(pfxStr) : null;
                bool renewCertificate = parseResult.GetValue(renewOption);
                int timeout = parseResult.GetValue(timeoutOption);
                if (timeout > 0)
                {
                    timeout *= 1000;
                }
                string logFile = parseResult.GetValue(logFileOption);
                bool loadTypes = parseResult.GetValue(loadTypesOption);
                bool managedbrowseall =
                    parseResult.GetValue(managedBrowseAllOption);
                bool browseall = parseResult.GetValue(browseAllOption);
                bool exportNodes = parseResult.GetValue(exportOption);
                bool fetchall = parseResult.GetValue(fetchAllOption);
                bool jsonvalues = parseResult.GetValue(jsonOption);
                bool verbose = parseResult.GetValue(verboseOption);
                bool subscribe = parseResult.GetValue(subscribeOption);
                string reverseConnectUrlString =
                    parseResult.GetValue(reverseConnectOption);
                bool forever = parseResult.GetValue(foreverOption);
                bool leakChannels = parseResult.GetValue(leakChannelsOption);
                bool enableDurableSubscriptions =
                    parseResult.GetValue(durableSubscriptionOption);
                var serverUrl = new Uri(parseResult.GetValue(serverUrlArgument));
                var testallEndpoints = parseResult.GetValue(testallEndpointsOption);

                ReverseConnectManager reverseConnectManager = null;
                using var telemetry = new ConsoleTelemetry();
                ILogger logger = LoggerUtils.Null.Logger;
                try
                {
                    // log console output to logger
                    if (logConsole && appLog)
                    {
                        logger = telemetry.CreateLogger("Main");
                    }

                    // Define the UA Client application
                    ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
                    var passwordProvider = new CertificatePasswordProvider(pfxPassword);
                    var application = new ApplicationInstance(telemetry)
                    {
                        ApplicationName = applicationName,
                        ApplicationType = ApplicationType.Client,
                        ConfigSectionName = configSectionName,
                        CertificatePasswordProvider = passwordProvider
                    };

                    // load the application configuration.
                    ApplicationConfiguration config = await application
                        .LoadApplicationConfigurationAsync(silent: false, ct: cancellationToken)
                        .ConfigureAwait(false);

                    // override logfile
                    if (logFile != null)
                    {
                        string logFilePath = config.TraceConfiguration.OutputFilePath;
                        string filename = Path.GetFileNameWithoutExtension(logFilePath);
                        config.TraceConfiguration.OutputFilePath = logFilePath.Replace(
                            filename,
                            logFile,
                            StringComparison.Ordinal
                        );
                        config.TraceConfiguration.DeleteOnLoad = true;
#pragma warning disable CS0618 // Type or member is obsolete
                        config.TraceConfiguration.ApplySettings();
#pragma warning restore CS0618 // Type or member is obsolete
                    }

                    // setup the logging
                    telemetry.ConfigureLogging(
                        config,
                        applicationName,
                        logConsole,
                        fileLog,
                        appLog,
                        LogLevel.Warning);

                    // delete old certificate
                    if (renewCertificate)
                    {
                        await application.DeleteApplicationInstanceCertificateAsync(ct: cancellationToken)
                            .ConfigureAwait(false);
                    }

                    // check the application certificate.
                    bool haveAppCertificate = await application
                        .CheckApplicationInstanceCertificatesAsync(false, ct: cancellationToken)
                        .ConfigureAwait(false);
                    if (!haveAppCertificate)
                    {
                        throw new ErrorExitException(
                            "Application instance certificate invalid!",
                            ExitCode.ErrorCertificate
                        );
                    }

                    if (reverseConnectUrlString != null)
                    {
                        // start the reverse connection manager
                        Console.WriteLine($"Create reverse connection endpoint at {reverseConnectUrlString}.");
                        reverseConnectManager = new ReverseConnectManager(telemetry);
                        reverseConnectManager.AddEndpoint(new Uri(reverseConnectUrlString));
                        reverseConnectManager.StartService(config);
                    }

                    // wait for timeout or Ctrl-C
                    var quitCTS = new CancellationTokenSource();
                    CancellationToken ct = quitCTS.Token;
                    ManualResetEvent quitEvent = ConsoleUtils.CtrlCHandler(quitCTS);

                    // handle connect all endpoints test.
                    if (testallEndpoints)
                    {
                        var tester = new ClientSamples(
                            telemetry,
                            null,
                            quitEvent,
                            verbose);

                        if (await tester.RunAsync(quitEvent, ct).ConfigureAwait(false))
                        {
                            return;
                        }
                    }

                    var userIdentity = new UserIdentity();

                    // set user identity of type username/pw
                    if (!string.IsNullOrEmpty(username))
                    {
                        if (userpassword == null)
                        {
                            Console.WriteLine($"No password provided for user {username}, using empty password.");
                        }

                        userIdentity = new UserIdentity(username, userpassword ?? ""u8);
                        Console.WriteLine($"Connect with user identity for user {username}");
                    }

                    // set user identity of type certificate
                    if (!string.IsNullOrEmpty(userCertificateThumbprint))
                    {
                        CertificateIdentifier userCertificateIdentifier
                                = await FindUserCertificateIdentifierAsync(
                                    userCertificateThumbprint,
                                    application.ApplicationConfiguration.SecurityConfiguration
                                        .TrustedUserCertificates,
                                    telemetry,
                                    ct
                                )
                                .ConfigureAwait(true);

                        if (userCertificateIdentifier != null)
                        {
                            userIdentity = UserIdentity.CreateAsync(
                                userCertificateIdentifier,
                                new CertificatePasswordProvider(userCertificatePassword),
                                telemetry,
                                ct
                            ).GetAwaiter().GetResult();

                            Console.WriteLine($"Connect with user certificate with Thumbprint {userCertificateThumbprint}");
                        }
                        else
                        {
                            Console.WriteLine($"Failed to load user certificate with Thumbprint {userCertificateThumbprint}");
                        }
                    }

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

                                waitTime = 0;
                            }

                            if (forever)
                            {
                                start = DateTime.UtcNow;
                            }
                        }

                        // create the UA Client object and connect to configured server.
                        using var uaClient = new UAClient(
                            application.ApplicationConfiguration,
                            reverseConnectManager,
                            telemetry,
                            null
                        )
                        {
                            AutoAccept = autoAccept,
                            SessionLifeTime = 60_000,
                            UserIdentity = userIdentity
                        };

                        if (enableDurableSubscriptions)
                        {
                            uaClient.ReconnectPeriodExponentialBackoff = 60000;
                        }

                        bool connected = await uaClient
                            .ConnectAsync(serverUrl.ToString(), !noSecurity, ct)
                            .ConfigureAwait(false);
                        if (connected)
                        {
                            Console.WriteLine("Connected! Ctrl-C to quit.");

                            // enable subscription transfer
                            uaClient.ReconnectPeriod = 1000;
                            uaClient.ReconnectPeriodExponentialBackoff = 10000;
                            uaClient.Session.MinPublishRequestCount = 3;
                            uaClient.Session.TransferSubscriptionsOnReconnect = true;
                            var samples = new ClientSamples(
                                telemetry,
                                null,
                                quitEvent,
                                verbose);
                            if (loadTypes)
                            {
                                var complexTypeSystem = new ComplexTypeSystem(uaClient.Session, telemetry);
                                await samples.LoadTypeSystemAsync(complexTypeSystem, ct).ConfigureAwait(false);
                            }

                            if (browseall || fetchall || jsonvalues || managedbrowseall)
                            {
                                List<NodeId> variableIds = null;
                                List<NodeId> variableIdsManagedBrowse = null;
                                ArrayOf<ReferenceDescription> referenceDescriptions = default;
                                ArrayOf<ReferenceDescription> referenceDescriptionsFromManagedBrowse = default;

                                if (browseall)
                                {
                                    Console.WriteLine("Browse the full address space.");
                                    referenceDescriptions = await samples
                                        .BrowseFullAddressSpaceAsync(uaClient, ObjectIds.RootFolder, ct: ct)
                                        .ConfigureAwait(false);
                                    variableIds =
                                    [
                                        .. referenceDescriptions
                                        .Filter(r =>
                                            r.NodeClass == NodeClass.Variable &&
                                            r.TypeDefinition.NamespaceIndex != 0
                                        )
                                        .ConvertAll(r => ExpandedNodeId.ToNodeId(
                                            r.NodeId,
                                            uaClient.Session.NamespaceUris))
                                    ];
                                }

                                if (managedbrowseall)
                                {
                                    Console.WriteLine("ManagedBrowse the full address space.");
                                    referenceDescriptionsFromManagedBrowse = await samples
                                        .ManagedBrowseFullAddressSpaceAsync(
                                            uaClient,
                                            ObjectIds.RootFolder,
                                            ct: ct)
                                        .ConfigureAwait(false);
                                    variableIdsManagedBrowse =
                                    [
                                        .. referenceDescriptionsFromManagedBrowse
                                        .Filter(r =>
                                            r.NodeClass == NodeClass.Variable &&
                                            r.TypeDefinition.NamespaceIndex != 0
                                        )
                                        .ConvertAll(r => ExpandedNodeId.ToNodeId(
                                            r.NodeId,
                                            uaClient.Session.NamespaceUris))
                                    ];
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
                                    allNodes = await samples
                                        .FetchAllNodesNodeCacheAsync(
                                            uaClient,
                                            ObjectIds.RootFolder,
                                            true,
                                            true,
                                            false,
                                            ct: ct)
                                        .ConfigureAwait(false);
                                    variableIds =
                                    [
                                        .. allNodes
                                        .Where(r =>
                                            r.NodeClass == NodeClass.Variable &&
                                            r is VariableNode v &&
                                            v.DataType.NamespaceIndex != 0
                                        )
                                        .Select(r => ExpandedNodeId.ToNodeId(
                                            r.NodeId,
                                            uaClient.Session.NamespaceUris))
                                    ];

                                    if (exportNodes)
                                    {
                                        await samples
                                            .ExportNodesToNodeSet2PerNamespaceAsync(uaClient.Session, allNodes, Environment.CurrentDirectory, cancellationToken)
                                            .ConfigureAwait(false);
                                    }
                                }

                                if (jsonvalues && variableIds != null)
                                {
                                    (
                                        ArrayOf<DataValue> allValues,
                                        ArrayOf<ServiceResult> results
                                    ) = await samples
                                        .ReadAllValuesAsync(uaClient, variableIds.ToArrayOf(), ct)
                                        .ConfigureAwait(false);
                                }

                                if (subscribe && (browseall || fetchall))
                                {
                                    // subscribe to 1000 random variables
                                    const int maxVariables = 1000;
                                    var variables = new List<Node>();

                                    if (fetchall)
                                    {
                                        variables.AddRange(
                                            allNodes
                                                .Where(r =>
                                                    r.NodeClass == NodeClass.Variable &&
                                                    r.NodeId.NamespaceIndex > 1
                                                )
                                                .Cast<VariableNode>()
                                                .OrderBy(o => UnsecureRandom.Shared.Next())
                                                .Take(maxVariables)
                                        );
                                    }
                                    else if (browseall)
                                    {
                                        var variableReferences = referenceDescriptions
                                            .ToList()
                                            .Where(r => r.NodeClass == NodeClass.Variable &&
                                                r.NodeId.NamespaceIndex > 1)
                                            .Select(r => r.NodeId)
                                            .OrderBy(o => UnsecureRandom.Shared.Next())
                                            .Take(maxVariables)
                                            .ToList();
                                        variables.AddRange(
                                            (await uaClient.Session.NodeCache.FindAsync(variableReferences, ct)
                                                .ConfigureAwait(false))
                                                .ToList()
                                                .Cast<Node>()
                                        );
                                    }

                                    await samples
                                        .SubscribeAllValuesAsync(
                                            uaClient,
                                            variableIds: [.. variables],
                                            samplingInterval: 100,
                                            publishingInterval: 1000,
                                            queueSize: 10,
                                            lifetimeCount: 60,
                                            keepAliveCount: 2,
                                            ct: ct
                                        )
                                        .ConfigureAwait(false);

                                    // Wait for DataChange notifications from MonitoredItems
                                    Console.WriteLine($"Subscribed to {maxVariables} variables. Press Ctrl-C to exit.");

                                    // free unused memory
                                    uaClient.Session.NodeCache.Clear();

                                    waitTime = timeout -
                                        (int)DateTime.UtcNow.Subtract(start).TotalMilliseconds;
                                    DateTime endTime =
                                        waitTime > 0
                                            ? DateTime.UtcNow.Add(TimeSpan.FromMilliseconds(waitTime))
                                            : DateTime.MaxValue;
                                    List<Node>.Enumerator variableIterator = variables.GetEnumerator();
                                    while (!quit && endTime > DateTime.UtcNow)
                                    {
                                        if (variableIterator.MoveNext())
                                        {
                                            try
                                            {
                                                DataValue value = await uaClient
                                                    .Session.ReadValueAsync(
                                                        variableIterator.Current.NodeId, ct)
                                                    .ConfigureAwait(false);
                                                Console.WriteLine($"Value of {variableIterator.Current.NodeId} is {value}");
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Error reading value of {variableIterator.Current.NodeId}: {ex.Message}");
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
                                int quitTimeout = 65_000;
                                if (enableDurableSubscriptions)
                                {
                                    quitTimeout = 150_000;
                                    uaClient.ReconnectPeriod = 500_000;
                                }

                                NodeId sessionNodeId = uaClient.Session.SessionId;
                                // Run tests for available methods on reference server.
                                await samples.ReadNodesAsync(
                                    uaClient.Session,
                                    ct).ConfigureAwait(false);
                                await samples.WriteNodesAsync(
                                    uaClient.Session,
                                    ct).ConfigureAwait(false);
                                await samples.BrowseAsync(
                                    uaClient.Session,
                                    ct).ConfigureAwait(false);
                                await samples.CallMethodAsync(
                                    uaClient.Session,
                                    ct).ConfigureAwait(false);
                                await samples.EnableEventsAsync(
                                    uaClient.Session,
                                    (uint)quitTimeout,
                                    ct).ConfigureAwait(false);
                                await samples.SubscribeToDataChangesAsync(
                                    uaClient.Session,
                                    60_000,
                                    enableDurableSubscriptions,
                                    ct).ConfigureAwait(false);

                                Console.WriteLine("Waiting...");

                                // Wait for some DataChange notifications from MonitoredItems
                                int waitCounters = 0;
                                const int checkForWaitTime = 1000;
                                const int closeSessionTime = checkForWaitTime * 15;
                                const int restartSessionTime = checkForWaitTime * 45;
                                const bool stopNotQuit = false;
                                int stopCount = 0;
                                while (!quit && !stopNotQuit && waitCounters < quitTimeout)
                                {
                                    quit = quitEvent.WaitOne(checkForWaitTime);
                                    waitCounters += checkForWaitTime;
                                    if (enableDurableSubscriptions)
                                    {
                                        if (waitCounters == closeSessionTime &&
                                            uaClient.Session.SubscriptionCount == 1)
                                        {
                                            Console.WriteLine($"Closing Session (CurrentTime: {DateTime.Now:T})");
                                            await uaClient.Session.CloseAsync(closeChannel: false, ct: ct)
                                                .ConfigureAwait(false);
                                        }

                                        if (waitCounters == restartSessionTime)
                                        {
                                            Console.WriteLine($"Restarting Session (CurrentTime: {DateTime.Now:T})");
                                            await uaClient
                                                .DurableSubscriptionTransferAsync(
                                                    serverUrl.ToString(),
                                                    useSecurity: !noSecurity,
                                                    ct
                                                )
                                                .ConfigureAwait(true);
                                        }

                                        if (waitCounters is > closeSessionTime and < restartSessionTime)
                                        {
                                            Console.WriteLine(
                                                "No Communication Interval " +
                                                stopCount.ToString(CultureInfo.InvariantCulture)
                                            );
                                            stopCount++;
                                        }
                                    }
                                }
                            }

                            Console.WriteLine("Client disconnected.");

                            await uaClient.DisconnectAsync(leakChannels, ct).ConfigureAwait(false);
                        }
                        else
                        {
                            Console.WriteLine("Could not connect to server! Retry in 10 seconds or Ctrl-C to quit.");
                            quit = quitEvent.WaitOne(Math.Min(10_000, waitTime));
                        }
                    } while (!quit);

                    Console.WriteLine("Client stopped.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.Message}");
                }
                finally
                {
                    Utils.SilentDispose(reverseConnectManager);
                }
            });

            args = ConsoleUtils.MergeEnvironmentArgs(args, "REFCLIENT", rootCommand);
            ParseResult cmdParseResult = rootCommand.Parse(args);
            return cmdParseResult
                .InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
        }

        /// <summary>
        /// returns a CertificateIdentifier of the Certificate with the specified thumbprint
        /// if it is found in the trustedUserCertificates TrustList
        /// </summary>
        /// <param name="thumbprint">the thumbprint of the certificate to select</param>
        /// <param name="trustedUserCertificates">the trustlist of the user certificates</param>
        /// <param name="ct">the cancellation token</param>
        /// <returns>Certificate Identifier</returns>
        private static async Task<CertificateIdentifier> FindUserCertificateIdentifierAsync(
            string thumbprint,
            CertificateTrustList trustedUserCertificates,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            CertificateIdentifier userCertificateIdentifier = null;

            X509Certificate2Collection userCertificatesWithMatchingThumbprint =
                await trustedUserCertificates.GetCertificatesAsync(telemetry, ct).ConfigureAwait(false);
            // get user certificate with matching thumbprint
            userCertificatesWithMatchingThumbprint =
                userCertificatesWithMatchingThumbprint.Find(X509FindType.FindByThumbprint, thumbprint, false);

            // create Certificate Identifier
            if (userCertificatesWithMatchingThumbprint.Count == 1)
            {
                userCertificateIdentifier = new CertificateIdentifier(
                    userCertificatesWithMatchingThumbprint[0])
                {
                    StorePath = trustedUserCertificates.StorePath,
                    StoreType = trustedUserCertificates.StoreType
                };
            }

            return userCertificateIdentifier;
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
    }
}
