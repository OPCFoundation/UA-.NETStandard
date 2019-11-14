/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Options;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Configuration;

namespace NetCoreConsoleClient
{

    public enum ExitCode : int
    {
        Ok = 0,
        ErrorCreateApplication = 0x11,
        ErrorDiscoverEndpoints = 0x12,
        ErrorCreateSession = 0x13,
        ErrorBrowseNamespace = 0x14,
        ErrorCreateSubscription = 0x15,
        ErrorMonitoredItem = 0x16,
        ErrorAddSubscription = 0x17,
        ErrorRunning = 0x18,
        ErrorLoadTypeDictionary = 0x19,
        ErrorReadComplexTypes = 0x1a,
        ErrorNoKeepAlive = 0x30,
        ErrorInvalidCommandLine = 0x100
    };

    public class Program
    {
        public static int Main(string[] args)
        {
            Console.WriteLine(".Net Core OPC UA Complex Types Client sample");

            // command line options
            bool showHelp = false;
            int stopTimeout = Timeout.Infinite;
            bool autoAccept = false;
            bool writeComplexInt = false;
            bool loadTypeSystem = false;
            bool verbose = false;

            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                { "h|help", "show this message and exit", h => showHelp = h != null },
                { "a|autoaccept", "auto accept certificates (for testing only)", a => autoAccept = a != null },
                { "t|timeout=", "the number of seconds until the client stops.", (int t) => stopTimeout = t },
                { "w|writeint", "Read and increment all complex types with an Int32.", w => writeComplexInt = w != null},
                { "l|loadtypes", "Load the type system dictionary from the server.", n => loadTypeSystem = n != null},
                { "v|verbose", "Verbose output.", v => verbose = v != null}
            };

            IList<string> extraArgs = null;
            try
            {
                extraArgs = options.Parse(args);
                if (extraArgs.Count > 1)
                {
                    foreach (string extraArg in extraArgs)
                    {
                        Console.WriteLine("Error: Unknown option: {0}", extraArg);
                        showHelp = true;
                    }
                }
            }
            catch (OptionException e)
            {
                Console.WriteLine(e.Message);
                showHelp = true;
            }

            if (showHelp)
            {
                // show some app description message
                Console.WriteLine("Usage: dotnet NetCoreConsoleClient.dll [OPTIONS] [ENDPOINTURL]");
                Console.WriteLine();

                // output the options
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return (int)ExitCode.ErrorInvalidCommandLine;
            }

            string endpointURL;
            if (extraArgs.Count == 0)
            {
                // use OPC UA .Net Sample server 
                endpointURL = "opc.tcp://localhost:51210/UA/SampleServer";
            }
            else
            {
                endpointURL = extraArgs[0];
            }

            MySampleClient client = new MySampleClient(endpointURL, autoAccept, stopTimeout);
            client.Verbose = verbose;
            client.LoadTypeSystem = loadTypeSystem;
            client.WriteComplexInt = writeComplexInt;
            return (int)client.Run();
        }
    }

    public class MySampleClient
    {
        const int ReconnectPeriod = 10;
        public ExitCode ExitCode { get; set; }
        public bool Verbose { get; set; } = false;
        public bool WriteComplexInt { get; set; } = false;
        public bool LoadTypeSystem { get; set; } = false;

        public MySampleClient(
            string endpointURL,
            bool autoAccept,
            int stopTimeout)
        {
            _endpointURL = endpointURL;
            _autoAccept = autoAccept;
            _clientRunTime = stopTimeout <= 0 ? Timeout.Infinite : stopTimeout * 1000;
        }

        public ExitCode Run()
        {
            Session session;

            try
            {
                session = ConsoleSampleClient().Result;
            }
            catch (Exception ex)
            {
                Utils.Trace("ServiceResultException:" + ex.Message);
                Console.WriteLine("Exception: {0}", ex.Message);
                return ExitCode;
            }

            ManualResetEvent quitEvent = new ManualResetEvent(false);
            try
            {
                Console.CancelKeyPress += (sender, eArgs) =>
                {
                    quitEvent.Set();
                    eArgs.Cancel = true;
                };
            }
            catch
            {
            }

            // Test the session reconnecthandler
            bool eventResult = quitEvent.WaitOne(5000);
            if (!eventResult)
            {
                Console.WriteLine(" --- Start simulated reconnect... --- ");
                _reconnectHandler = new SessionReconnectHandler();
                _reconnectHandler.BeginReconnect(session, 1000, Client_ReconnectComplete);
            }

            // wait for timeout or Ctrl-C
            quitEvent.WaitOne(_clientRunTime);

            // return error conditions
            if (session.KeepAliveStopped)
            {
                ExitCode = ExitCode.ErrorNoKeepAlive;
                return ExitCode;
            }

            ExitCode = ExitCode.Ok;
            return ExitCode;
        }

        private async Task<Session> ConsoleSampleClient()
        {
            Console.WriteLine("1 - Create an Application Configuration.");
            ExitCode = ExitCode.ErrorCreateApplication;

            ApplicationInstance application = new ApplicationInstance
            {
                ApplicationName = "UA Core Complex Client",
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "Opc.Ua.ComplexClient"
            };

            // load the application configuration.
            ApplicationConfiguration config = await application.LoadApplicationConfiguration(false);

            // check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(false, 0);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            if (haveAppCertificate)
            {
                config.ApplicationUri = Utils.GetApplicationUriFromCertificate(config.SecurityConfiguration.ApplicationCertificate.Certificate);
                if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    _autoAccept = true;
                }
                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }
            else
            {
                Console.WriteLine("    WARN: missing application certificate, using unsecure connection.");
            }

            Console.WriteLine("2 - Discover endpoints of {0}.", _endpointURL);
            ExitCode = ExitCode.ErrorDiscoverEndpoints;
            var selectedEndpoint = CoreClientUtils.SelectEndpoint(_endpointURL, haveAppCertificate, 15000);
            Console.WriteLine("    Selected endpoint uses: {0}",
                selectedEndpoint.SecurityPolicyUri.Substring(selectedEndpoint.SecurityPolicyUri.LastIndexOf('#') + 1));

            Console.WriteLine("3 - Create a session with OPC UA server.");
            ExitCode = ExitCode.ErrorCreateSession;
            var endpointConfiguration = EndpointConfiguration.Create(config);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            _session = await Session.Create(config, endpoint, false, "OPC UA Console Client", 60000, new UserIdentity(new AnonymousIdentityToken()), null);

            // register keep alive handler
            _session.KeepAlive += Client_KeepAlive;

            Console.WriteLine("4 - Load the server type dictionary.");
            ExitCode = ExitCode.ErrorLoadTypeDictionary;

            Console.WriteLine("5 - Browse for all custom type variables.");
            ExitCode = ExitCode.ErrorReadComplexTypes;

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();
            var allVariableNodes = BrowseAllVariables();
            var allCustomTypeVariables = allVariableNodes.Where(n => ((VariableNode)n).DataType == DataTypeIds.Structure).ToList();
            allCustomTypeVariables.AddRange(allVariableNodes.Where(n => ((VariableNode)n).DataType.NamespaceIndex != 0).ToList());
            stopWatch.Stop();

            Console.WriteLine($" -- Browse all nodes took {stopWatch.ElapsedMilliseconds}ms.");
            Console.WriteLine($" -- Browsed {allVariableNodes.Count} nodes, from which {allCustomTypeVariables.Count} are custom type variables.");

            if (LoadTypeSystem)
            {
                stopWatch.Reset();
                stopWatch.Start();

                var complexTypeSystem = new ComplexTypeSystem(_session);
                await complexTypeSystem.Load();

                stopWatch.Stop();

                Console.WriteLine($"Load type system took {stopWatch.ElapsedMilliseconds}ms.");

                Console.WriteLine($"Custom types defined for this session:");
                foreach (var type in complexTypeSystem.GetDefinedTypes())
                {
                    Console.WriteLine($"{type.Namespace}.{type.Name}");
                }

                Console.WriteLine($"Loaded {_session.DataTypeSystem.Count} dictionaries:");
                foreach (var dictionary in _session.DataTypeSystem)
                {
                    Console.WriteLine($" + {dictionary.Value.Name}");
                    foreach (var type in dictionary.Value.DataTypes)
                    {
                        Console.WriteLine($" -- {type.Key}:{type.Value}");
                    }
                }
            }

            foreach (VariableNode variableNode in allCustomTypeVariables)
            {
                try
                {
                    var value = _session.ReadValue(variableNode.NodeId);
                    CastInt32ToEnum(variableNode, value);
                    Console.WriteLine($" -- {variableNode}:{value}");

                    var complexType = value.Value as BaseComplexType;
                    if (complexType != null)
                    {
                        break;
                    }

                    var extensionObject = value.Value as ExtensionObject;
                    if (extensionObject != null)
                    {
                        complexType = extensionObject.Body as BaseComplexType;
                        if (complexType != null)
                        {
                            foreach (var item in complexType.GetPropertyEnumerator())
                            {
                                if (Verbose)
                                {
                                    Console.WriteLine($" -- -- {item.Name}:{complexType[item.Name]}");
                                }
                                if (WriteComplexInt && item.PropertyType == typeof(Int32))
                                {
                                    var data = complexType[item.Name];
                                    if (data != null)
                                    {
                                        complexType[item.Name] = (Int32)data + 1;
                                    }
                                    Console.WriteLine($" -- -- Write: {item.Name}, {complexType[item.Name]}");
                                    WriteValue(_session, variableNode.NodeId, value);
                                }
                            }
                        }
                    }
                }
                catch (ServiceResultException sre)
                {
                    if (sre.StatusCode == StatusCodes.BadUserAccessDenied)
                    {
                        Console.WriteLine($" -- {variableNode}: Access denied!");
                    }
                }
            }

            Console.WriteLine("6 - Create a subscription with publishing interval of 1 second.");
            ExitCode = ExitCode.ErrorCreateSubscription;
            var subscription = new Subscription(_session.DefaultSubscription) { PublishingInterval = 1000 };

            Console.WriteLine("7 - Add all custom values and the server time to the subscription.");
            ExitCode = ExitCode.ErrorMonitoredItem;
            var list = new List<MonitoredItem> {
                new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = "ServerStatusCurrentTime", StartNodeId = "i="+Variables.Server_ServerStatus_CurrentTime.ToString()
                }
            };
            list.ForEach(i => i.Notification += OnNotification);

            foreach (var customVariable in allCustomTypeVariables)
            {
                var newItem = new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = customVariable.DisplayName.Text,
                    StartNodeId = ExpandedNodeId.ToNodeId(customVariable.NodeId, _session.NamespaceUris)
                };
                newItem.Notification += OnComplexTypeNotification;
                list.Add(newItem);
            }

            subscription.AddItems(list);

            Console.WriteLine("8 - Add the subscription to the session.");
            ExitCode = ExitCode.ErrorAddSubscription;
            _session.AddSubscription(subscription);
            subscription.Create();

            Console.WriteLine("9 - Running...Press Ctrl-C to exit...");
            ExitCode = ExitCode.ErrorRunning;

            return _session;
        }

        /// <summary>
        /// Browse all variables in the objects folder.
        /// </summary>
        private IList<INode> BrowseAllVariables()
        {
            var result = new List<INode>();
            var nodesToBrowse = new ExpandedNodeIdCollection();
            nodesToBrowse.Add(ObjectIds.ObjectsFolder);

            while (nodesToBrowse.Count > 0)
            {
                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                foreach (var node in nodesToBrowse)
                {
                    try
                    {
                        var organizers = _session.NodeCache.FindReferences(
                            node,
                            ReferenceTypeIds.Organizes,
                            false,
                            false);
                        var components = _session.NodeCache.FindReferences(
                            node,
                            ReferenceTypeIds.HasComponent,
                            false,
                            false);
                        var properties = _session.NodeCache.FindReferences(
                            node,
                            ReferenceTypeIds.HasProperty,
                            false,
                            false);
                        nextNodesToBrowse.AddRange(organizers
                            .Where(n => n is ObjectNode)
                            .Select(n => n.NodeId).ToList());
                        nextNodesToBrowse.AddRange(components
                            .Where(n => n is ObjectNode)
                            .Select(n => n.NodeId).ToList());
                        result.AddRange(organizers.Where(n => n is VariableNode));
                        result.AddRange(components.Where(n => n is VariableNode));
                        result.AddRange(properties.Where(n => n is VariableNode));
                    }
                    catch (ServiceResultException sre)
                    {
                        if (sre.StatusCode == StatusCodes.BadUserAccessDenied)
                        {
                            Console.WriteLine($"Access denied: Skip node {node}.");
                        }
                    }
                }
                nodesToBrowse = nextNodesToBrowse;
            }
            return result;
        }

        private void Client_KeepAlive(Session sender, KeepAliveEventArgs e)
        {
            if (e.Status != null && ServiceResult.IsNotGood(e.Status))
            {
                Console.WriteLine("{0} {1}/{2}", e.Status, sender.OutstandingRequestCount, sender.DefunctRequestCount);

                if (_reconnectHandler == null)
                {
                    Console.WriteLine("--- RECONNECTING ---");
                    _reconnectHandler = new SessionReconnectHandler();
                    _reconnectHandler.BeginReconnect(sender, ReconnectPeriod * 1000, Client_ReconnectComplete);
                }
            }
        }

        private void Client_ReconnectComplete(object sender, EventArgs e)
        {
            // ignore callbacks from discarded objects.
            if (!Object.ReferenceEquals(sender, _reconnectHandler))
            {
                return;
            }

            _session = _reconnectHandler.Session;
            _reconnectHandler.Dispose();
            _reconnectHandler = null;

            Console.WriteLine("--- RECONNECTED ---");
        }

        /// <summary>
        /// Helper to cast a enumeration node value to an enumeration type.
        /// </summary>
        private void CastInt32ToEnum(VariableNode variableNode, DataValue value)
        {
            if (value.Value.GetType() == typeof(Int32))
            {
                // test if this is an enum datatype?
                Type systemType = _session.Factory.GetSystemType(
                    NodeId.ToExpandedNodeId(variableNode.DataType, _session.NamespaceUris)
                    );
                if (systemType != null)
                {
                    value.Value = Enum.ToObject(systemType, value.Value);
                }
            }
        }

        private void WriteValue(Session session, NodeId variableId, DataValue value)
        {
            WriteValue nodeToWrite = new WriteValue();
            nodeToWrite.NodeId = variableId;
            nodeToWrite.AttributeId = Attributes.Value;
            nodeToWrite.Value = new DataValue();
            nodeToWrite.Value.WrappedValue = value.WrappedValue;

            WriteValueCollection nodesToWrite = new WriteValueCollection();
            nodesToWrite.Add(nodeToWrite);

            // read the attributes.
            StatusCodeCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            ResponseHeader responseHeader = session.Write(
                null,
                nodesToWrite,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, nodesToWrite);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToWrite);

            // check for error.
            if (StatusCode.IsBad(results[0]))
            {
                throw ServiceResultException.Create(results[0], 0, diagnosticInfos, responseHeader.StringTable);
            }
        }

        private void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            foreach (var value in item.DequeueValues())
            {
                Console.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
            }
        }

        private void OnComplexTypeNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            foreach (var value in item.DequeueValues())
            {
                Console.WriteLine("{0}: {1}, {2}", item.DisplayName, value.SourceTimestamp, value.StatusCode);
                Console.WriteLine(value.Value);
            }
        }

        private void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = _autoAccept;
                if (_autoAccept)
                {
                    Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
                }
                else
                {
                    Console.WriteLine("Rejected Certificate: {0}", e.Certificate.Subject);
                }
            }
        }

        private Session _session;
        private SessionReconnectHandler _reconnectHandler;
        private string _endpointURL;
        private bool _autoAccept = false;
        private int _clientRunTime = Timeout.Infinite;

    }

}
