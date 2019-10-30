﻿/* ========================================================================
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


using Mono.Options;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                { "h|help", "show this message and exit", h => showHelp = h != null },
                { "a|autoaccept", "auto accept certificates (for testing only)", a => autoAccept = a != null },
                { "t|timeout=", "the number of seconds until the client stops.", (int t) => stopTimeout = t }
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
            client.Run();

            return (int)MySampleClient.ExitCode;
        }
    }

    public class MySampleClient
    {
        const int ReconnectPeriod = 10;
        Session session;
        SessionReconnectHandler reconnectHandler;
        string endpointURL;
        int clientRunTime = Timeout.Infinite;
        static bool autoAccept = false;
        static ExitCode exitCode;

        public MySampleClient(string _endpointURL, bool _autoAccept, int _stopTimeout)
        {
            endpointURL = _endpointURL;
            autoAccept = _autoAccept;
            clientRunTime = _stopTimeout <= 0 ? Timeout.Infinite : _stopTimeout * 1000;
        }

        public void Run()
        {
            try
            {
                ConsoleSampleClient().Wait();
            }
            catch (Exception ex)
            {
                Utils.Trace("ServiceResultException:" + ex.Message);
                Console.WriteLine("Exception: {0}", ex.Message);
                return;
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

            // wait for timeout or Ctrl-C
            quitEvent.WaitOne(clientRunTime);

            // return error conditions
            if (session.KeepAliveStopped)
            {
                exitCode = ExitCode.ErrorNoKeepAlive;
                return;
            }

            exitCode = ExitCode.Ok;
        }

        public static ExitCode ExitCode => exitCode;

        private async Task ConsoleSampleClient()
        {
            Console.WriteLine("1 - Create an Application Configuration.");
            exitCode = ExitCode.ErrorCreateApplication;

            ApplicationInstance application = new ApplicationInstance
            {
                ApplicationName = "UA Core Sample Client",
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
                    autoAccept = true;
                }
                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }
            else
            {
                Console.WriteLine("    WARN: missing application certificate, using unsecure connection.");
            }

            Console.WriteLine("2 - Discover endpoints of {0}.", endpointURL);
            exitCode = ExitCode.ErrorDiscoverEndpoints;
            var selectedEndpoint = CoreClientUtils.SelectEndpoint(endpointURL, haveAppCertificate, 15000);
            Console.WriteLine("    Selected endpoint uses: {0}",
                selectedEndpoint.SecurityPolicyUri.Substring(selectedEndpoint.SecurityPolicyUri.LastIndexOf('#') + 1));

            Console.WriteLine("3 - Create a session with OPC UA server.");
            exitCode = ExitCode.ErrorCreateSession;
            var endpointConfiguration = EndpointConfiguration.Create(config);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            session = await Session.Create(config, endpoint, false, "OPC UA Console Client", 60000, new UserIdentity(new AnonymousIdentityToken()), null);

            // register keep alive handler
            session.KeepAlive += Client_KeepAlive;

            Console.WriteLine("4 - Browse the OPC UA data dictionary.");
            exitCode = ExitCode.ErrorBrowseNamespace;
            ReferenceDescriptionCollection references;
            Byte[] continuationPoint;

            session.Browse(
                null,
                null,
                DataTypeIds.Enumeration,
                0u,
                BrowseDirection.Forward,
                ReferenceTypeIds.HasSubtype,
                false,
                (uint)NodeClass.DataType,
                out continuationPoint,
                out references);

            var complexTypeSystem = new ComplexTypeSystem(session);
            await complexTypeSystem.Load();

            var nodes = new List<Node>();
            var values = new List<DataValue>();

            // UA Ansi C++ server
            var testId = new NodeId("Demo.Static.Scalar.Structures", 2);
            if (TestNodeId(testId))
            {
                session.Browse(
                    null,
                    null,
                    testId,
                    0u,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HierarchicalReferences,
                    true,
                    (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                    out continuationPoint,
                    out references);

                foreach (var reference in references)
                {
                    var node = session.ReadNode(ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris));
                    nodes.Add(node);
                    Console.WriteLine($"{node.BrowseName}");
                    try
                    {
                        var nodeValue = session.ReadValue(ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris));
                        values.Add(nodeValue);
                        Console.WriteLine($"{nodeValue.Value}");
                    }
                    catch { }
                }

                var vectorNodeId = new NodeId("Demo.Static.Arrays.Vector", 2);
                var vectorNode = session.ReadNode(vectorNodeId);
                var vectorValue = session.ReadValue(vectorNodeId);

                var workOrderNodeId = new NodeId("Demo.Static.Arrays.WorkOrder", 2);
                var workOrderNode = session.ReadNode(workOrderNodeId);
                var workOrderValue = session.ReadValue(workOrderNodeId);

                var workOrderVarNodeId = new NodeId("Demo.WorkOrder.WorkOrderVariable", 2);
                var workOrderVarNode = session.ReadNode(workOrderVarNodeId);
                var workOrderVarValue = session.ReadValue(workOrderVarNodeId);

                var workOrderVarNodeId2 = new NodeId("Demo.WorkOrder.WorkOrderVariable2", 2);
                var workOrderVarNode2 = session.ReadNode(workOrderVarNodeId2);
                var workOrderVarValue2 = session.ReadValue(workOrderVarNodeId2);

                nodes.Add(vectorNode);
                nodes.Add(workOrderNode);
                nodes.Add(workOrderVarNode);
                nodes.Add(workOrderVarNode2);

                values.Add(vectorValue);
                values.Add(workOrderValue);
                values.Add(workOrderVarValue);
                values.Add(workOrderVarValue2);

            }

            // UA Ansi C server
            if (TestNodeId(new NodeId("Demo.WorkOrder.WorkOrderVariable2.StatusComments", 4)))
            {
                // WorkOrderStatusType
                var workOrderNodeId = new NodeId("Demo.WorkOrder.WorkOrderVariable2.StatusComments", 4);
                var statusCommentNodeId = session.ReadNode(workOrderNodeId);
                var statusComment = session.ReadValue(workOrderNodeId);

                //workOrderNodeId = new NodeId("Demo.WorkOrder.WorkOrderVariable", 4);
                //var workOrder = session.ReadNode(workOrderNodeId);
                //var workOrderValue = session.ReadValue(workOrderNodeId);

                // Vector
                var nodeId = new NodeId("Demo.Static.Scalar.Vector", 4);
                var vector = session.ReadNode(nodeId);
                var vectorValue = session.ReadValue(nodeId);

                // Work Order
                nodeId = new NodeId("Demo.Static.Scalar.WorkOrder", 4);
                var workOrder = session.ReadNode(nodeId);
                var workOrderValue = session.ReadValue(nodeId);

                // Union
                nodeId = new NodeId("Demo.Static.Scalar.Union", 4);
                var union = session.ReadNode(nodeId);
                var unionValue = session.ReadValue(nodeId);

                nodeId = new NodeId("Demo.Static.Arrays.Vector", 4);
                var vectorArray = session.ReadNode(nodeId);
                var vectorArrayValue = session.ReadValue(nodeId);

                nodeId = new NodeId("Demo.Static.Matrix.Vector", 4);
                var vectorMatrix = session.ReadNode(nodeId);
                var vectorMatrixValue = session.ReadValue(nodeId);

                nodeId = new NodeId("Demo.Static.Scalar.OptionalFields", 4);
                var optionalFields = session.ReadNode(nodeId);
                var optionalFieldsValue = session.ReadValue(nodeId);

                nodeId = new NodeId("Demo.Static.Scalar.Priority", 4);
                var priority = session.ReadNode(nodeId);
                var priorityValue = session.ReadValue(nodeId);

                nodeId = new NodeId("Demo.BoilerDemo.Boiler1.HeaterStatus", 4);
                var heater = session.ReadNode(nodeId);
                var heaterValue = session.ReadValue(nodeId);

                // AccessRights
                nodeId = new NodeId("Demo.Static.Scalar.OptionSet", 4);
                var optionSet = session.ReadNode(nodeId);
                var optionSetValue = session.ReadValue(nodeId);

                // structure
                nodeId = new NodeId("Demo.Static.Scalar.Structure", 4);
                var node = session.ReadNode(nodeId);
                var value = session.ReadValue(nodeId);
            }

            // Quickstart DataTypes server
            if (TestNodeId(new NodeId(283, 4)))
            {
                // read various nodes...
                var vehiclesInLotNode = session.ReadNode(new NodeId(283, 4));
                var parkingLotNode = session.ReadNode(new NodeId(281, 4));

                //var vehiclesInLot = session.ReadValue(new NodeId(283, 4));
                var lotTypeNodeId = session.ReadNode(new NodeId(380, 4));
                var lotType = session.ReadValue(new NodeId(380, 4));
                var ownedVehiclesNodeId = session.ReadNode(new NodeId(377, 4));
                var ownedVehicles = session.ReadValue(new NodeId(377, 4));
                Console.WriteLine(ownedVehicles);
                var primaryVehicleNode = session.ReadNode(new NodeId(376, 4));
                var primaryVehicle = session.ReadValue(new NodeId(376, 4));
                Console.WriteLine(primaryVehicle);
                var vehiclesInLot = session.ReadValue(new NodeId(283, 4));
                Console.WriteLine(vehiclesInLot);
            }

            var jsonEncoder = new JsonEncoder(session.MessageContext, true);

            int v = 1;
            foreach (var value in values)
            {
                jsonEncoder.WriteDataValue($"Value{v++}", value);
            }
            var textbuffer = jsonEncoder.CloseAndReturnText();

            Console.WriteLine(textbuffer);

            Console.WriteLine("4 - Browse the OPC UA server namespace.");
            exitCode = ExitCode.ErrorBrowseNamespace;

            session.Browse(
                null,
                null,
                ObjectIds.ObjectsFolder,
                0u,
                BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences,
                true,
                (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                out continuationPoint,
                out references);

            Console.WriteLine(" DisplayName, BrowseName, NodeClass");
            foreach (var rd in references)
            {
                Console.WriteLine(" {0}, {1}, {2}", rd.DisplayName, rd.BrowseName, rd.NodeClass);
                ReferenceDescriptionCollection nextRefs;
                byte[] nextCp;
                session.Browse(
                    null,
                    null,
                    ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris),
                    0u,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HierarchicalReferences,
                    true,
                    (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                    out nextCp,
                    out nextRefs);

                foreach (var nextRd in nextRefs)
                {
                    Console.WriteLine("   + {0}, {1}, {2}", nextRd.DisplayName, nextRd.BrowseName, nextRd.NodeClass);
                }
            }

            Console.WriteLine("5 - Create a subscription with publishing interval of 1 second.");
            exitCode = ExitCode.ErrorCreateSubscription;
            var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 1000 };

            Console.WriteLine("6 - Add a list of items (server current time and status) to the subscription.");
            exitCode = ExitCode.ErrorMonitoredItem;
            var list = new List<MonitoredItem> {
                new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = "ServerStatusCurrentTime", StartNodeId = "i="+Variables.Server_ServerStatus_CurrentTime.ToString()
                }
            };
            list.ForEach(i => i.Notification += OnNotification);
            subscription.AddItems(list);

            Console.WriteLine("7 - Add the subscription to the session.");
            exitCode = ExitCode.ErrorAddSubscription;
            session.AddSubscription(subscription);
            subscription.Create();

            Console.WriteLine("8 - Running...Press Ctrl-C to exit...");
            exitCode = ExitCode.ErrorRunning;
        }

        private bool TestNodeId(NodeId nodeId)
        {
            try
            {
                session.ReadNode(nodeId);
                return true;
            }
            catch
            {
            }
            return false;
        }

        private void Client_KeepAlive(Session sender, KeepAliveEventArgs e)
        {
            if (e.Status != null && ServiceResult.IsNotGood(e.Status))
            {
                Console.WriteLine("{0} {1}/{2}", e.Status, sender.OutstandingRequestCount, sender.DefunctRequestCount);

                if (reconnectHandler == null)
                {
                    Console.WriteLine("--- RECONNECTING ---");
                    reconnectHandler = new SessionReconnectHandler();
                    reconnectHandler.BeginReconnect(sender, ReconnectPeriod * 1000, Client_ReconnectComplete);
                }
            }
        }

        private void Client_ReconnectComplete(object sender, EventArgs e)
        {
            // ignore callbacks from discarded objects.
            if (!Object.ReferenceEquals(sender, reconnectHandler))
            {
                return;
            }

            session = reconnectHandler.Session;
            reconnectHandler.Dispose();
            reconnectHandler = null;

            Console.WriteLine("--- RECONNECTED ---");
        }

        private static void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            foreach (var value in item.DequeueValues())
            {
                Console.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
            }
        }

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

    }

}
