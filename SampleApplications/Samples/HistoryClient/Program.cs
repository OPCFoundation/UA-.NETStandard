/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua.Client;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Opc.Ua.Sample
{
    partial class Program
    {
        public const double PublishingInterval = 1;
        public const double SamplingInterval = 1;
        public const uint QueueSize = 3;
        public const int ItemsToMonitor = 100000;
        public const int ItemsPerPublish = 10000;
        public const uint NotificationsPerPublish = ItemsPerPublish * QueueSize;

        /// <summary>
        /// The URL of the server.
        /// </summary>
        public const string DefaultServerUrl = "http://localhost:6000/UA/SampleClient";
        // public const string DefaultServerUrl = "opc.tcp://localhost:21381/UA/MatrikonOpcUaWrapper";
        // public const string DefaultServerUrl = "opc.tcp://localhost:51210/UA/SampleServer";
        // public const string DefaultServerUrl = "http://localhost:5000/UA/SampleServer";
        
        /// <summary>
        /// The variables to read.
        /// </summary>
        static List<string> VariableBrowsePaths;

        static void Main(string[] args)
        {
            VariableBrowsePaths = new List<string>();
            VariableBrowsePaths.Add("/6:Data/6:Dynamic/6:Scalar/6:Int32Value");
            // VariableBrowsePaths.Add("/7:MatrikonOpc Sim Server/7:Simulation Items/7:Bucket Brigade/7:Int1");
            // VariableBrowsePaths.Add("/7:MatrikonOPC Sim Server/7:Simulation Items/7:Bucket Brigade/7:Int2");


            try
            {
                // create the configuration.     
                Task<ApplicationConfiguration> t = Helpers.CreateClientConfiguration();
                t.Wait();
                ApplicationConfiguration configuration = t.Result;

                // create the endpoint description.
                Task<EndpointDescription> t2 = Helpers.CreateEndpointDescription();
                t2.Wait();
                EndpointDescription endpointDescription = t2.Result;

                // create the endpoint configuration (use the application configuration to provide default values).
                EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(configuration);

                // the default timeout for a requests sent using the channel.
                endpointConfiguration.OperationTimeout = 600000;

                // use the pure XML encoding on the wire.
                endpointConfiguration.UseBinaryEncoding = true;

                // create the endpoint.
                ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

                // create the binding factory.
                ServiceMessageContext messageContext = configuration.CreateMessageContext();

                // update endpoint description using the discovery endpoint.
                if (endpoint.UpdateBeforeConnect)
                {
                    endpoint.UpdateFromServer();

                    Console.WriteLine("Updated endpoint description for url: {0}", endpointDescription.EndpointUrl);

                    endpointDescription = endpoint.Description;
                    endpointConfiguration = endpoint.Configuration;
                }

                Task<X509Certificate2> t3 = configuration.SecurityConfiguration.ApplicationCertificate.Find();
                t3.Wait();
                X509Certificate2 clientCertificate = t3.Result;

                // set up a callback to handle certificate validation errors.
                configuration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);

                // Initialize the channel which will be created with the server.
                ITransportChannel channel = SessionChannel.Create(
                    configuration,
                    endpointDescription,
                    endpointConfiguration,
                    clientCertificate,
                    messageContext);

                // Wrap the channel with the session object.
                // This call will fail if the server does not trust the client certificate.
                Session session = new Session(channel, configuration, endpoint, null);

                session.ReturnDiagnostics = DiagnosticsMasks.All;

                // register keep alive callback.
                // session.KeepAlive += new KeepAliveEventHandler(Session_KeepAlive);

                // passing null for the user identity will create an anonymous session.
                UserIdentity identity = null; // new UserIdentity("iamuser", "password");        

                // create the session. This actually connects to the server.
                session.Open("My Session Name", identity);

                //Read some history values:
                string str = "";
                do
                {
                    Console.WriteLine("Select action from the menu:\n");
                    Console.WriteLine("\t 0 - Browse");
                    Console.WriteLine("\t 1 - Update");
                    Console.WriteLine("\t 2 - ReadRaw");
                    Console.WriteLine("\t 3 - ReadProcessed");
                    Console.WriteLine("\t 4 - ReadAtTime");
                    Console.WriteLine("\t 5 - ReadAttributes");
                    Console.WriteLine("\t 6 - DeleteAtTime");
                    Console.WriteLine("\t 7 - DeleteRaw");


                    Console.WriteLine("\n\tQ - exit\n\n");

                    str = Console.ReadLine();
                    Console.WriteLine("\n");

                    try
                    {
                        if (str == "0")
                        {
                            Browse(session);
                        }
                        else if (str == "1")
                            HistoryUpdate(session);
                        else if (str == "2")
                            HistoryReadRaw(session);
                        else if (str == "3")
                            HistoryReadProcessed(session);
                        else if (str == "4")
                            HistoryReadAtTime(session);
                        else if (str == "5")
                            HistoryReadAttributes(session);
                        else if (str == "6")
                            HistoryDeleteAtTime(session);
                        else if (str == "7")
                            HistoryDeleteRaw(session);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Exception occured: " + e.Message);
                    }

                } while (str != "Q" && str != "q");


                // Display some friendly info to the console and then wait for the ENTER key to be pressed.
                Console.WriteLine("Connected to {0}.\nPress ENTER to disconnect to end.", DefaultServerUrl);
                Console.ReadLine();

                // Close and Dispose of our session, effectively disconnecting us from the UA Server.
                session.Close();
                session.Dispose();
            }
            catch (Exception e)
            {
                Console.WriteLine("Unexpected exception: {0}.\nPress ENTER to disconnect to end.", e.Message);
                Console.ReadLine();
                Console.WriteLine();
                Console.WriteLine("========================================================================================");
                Console.WriteLine();
            }
        }

        private class NodeOfInterest
        {
            public NodeId NodeId;
            public DataValue Value;
        }

        /// <summary>
        /// Returns the node ids for a set of relative paths.
        /// </summary>
        /// <param name="session">An open session with the server to use.</param>
        /// <param name="startNodeId">The starting node for the relative paths.</param>
        /// <param name="relativePaths">The relative paths.</param>
        /// <returns>A collection of local nodes.</returns>
        static List<NodeOfInterest> GetNodeIds(
            Session session,
            NodeId startNodeId,
            params string[] relativePaths)
        {
            // build the list of browse paths to follow by parsing the relative paths.
            BrowsePathCollection browsePaths = new BrowsePathCollection();

            if (relativePaths != null)
            {
                for (int ii = 0; ii < relativePaths.Length; ii++)
                {
                    BrowsePath browsePath = new BrowsePath();

                    // The relative paths used indexes in the namespacesUris table. These must be 
                    // converted to indexes used by the server. An error occurs if the relative path
                    // refers to a namespaceUri that the server does not recognize.

                    // The relative paths may refer to ReferenceType by their BrowseName. The TypeTree object
                    // allows the parser to look up the server's NodeId for the ReferenceType.

                    browsePath.RelativePath = RelativePath.Parse(
                        relativePaths[ii],
                        session.TypeTree,
                        session.NamespaceUris,
                        session.NamespaceUris);

                    browsePath.StartingNode = startNodeId;

                    browsePaths.Add(browsePath);
                }
            }

            // make the call to the server.
            BrowsePathResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = session.TranslateBrowsePathsToNodeIds(
                null,
                browsePaths,
                out results,
                out diagnosticInfos);

            // ensure that the server returned valid results.
            Session.ValidateResponse(results, browsePaths);
            Session.ValidateDiagnosticInfos(diagnosticInfos, browsePaths);

            Console.WriteLine("Translated {0} browse paths.", relativePaths.Length);

            // collect the list of node ids found.
            List<NodeOfInterest> nodes = new List<NodeOfInterest>();

            for (int ii = 0; ii < results.Count; ii++)
            {
                // check if the start node actually exists.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    ServiceResult error = new ServiceResult(
                        results[ii].StatusCode,
                        diagnosticInfos[ii],
                        responseHeader.StringTable);

                    Console.WriteLine("Path '{0}' is not valid. Error = {1}", relativePaths[ii], error);
                    continue;
                }

                // an empty list is returned if no node was found.
                if (results[ii].Targets.Count == 0)
                {
                    Console.WriteLine("Path '{0}' does not exist.", relativePaths[ii]);
                    continue;
                }

                // Multiple matches are possible, however, the node that matches the type model is the
                // one we are interested in here. The rest can be ignored.
                BrowsePathTarget target = results[ii].Targets[0];

                if (target.RemainingPathIndex != UInt32.MaxValue)
                {
                    Console.WriteLine("Path '{0}' refers to a node in another server.", relativePaths[ii]);
                    continue;
                }

                // The targetId is an ExpandedNodeId because it could be node in another server. 
                // The ToNodeId function is used to convert a local NodeId stored in a ExpandedNodeId to a NodeId.

                NodeOfInterest node = new NodeOfInterest();
                node.NodeId = ExpandedNodeId.ToNodeId(target.TargetId, session.NamespaceUris);
                nodes.Add(node);
            }

            Console.WriteLine("Translate found {0} local nodes.", nodes.Count);

            // return whatever was found.
            return nodes;
        }

        /// <summary>
        /// Used to synchronize access to data via multiple threads.
        /// </summary>
        private static object m_lock = new object();

        /// <summary>
        /// Handles an error validating the server certificate.
        /// </summary>
        /// <remarks>
        /// Applications should never accept certificates silently. Doing do will create the illusion of security
        /// that will come back to haunt the vendor in the future. Compliance tests by the OPC Foundation will
        /// fail products that silently accept untrusted certificates.
        /// </remarks>
        static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            e.Accept = true;
            Console.WriteLine("WARNING: Accepting Untrusted Certificate: {0}", e.Certificate.Subject);
        }

        /// <summary>
        /// Raised when a keep alive response is returned from the server.
        /// </summary>
        static void Session_KeepAlive(Session session, KeepAliveEventArgs e)
        {
            Console.WriteLine("===>>> Session KeepAlive: {0} ServerTime: {1:HH:MM:ss}", e.CurrentState, e.CurrentTime.ToLocalTime());
        }

        private static Queue<DataValue> m_publishes = new Queue<DataValue>();
        private static DateTime m_lastDump;
        private static uint m_lastMessage;
        private static uint m_actualSamples;
        private static uint m_expectedSamples;
        private static uint m_dumpCount;
        private static uint m_notifications;

        private static void Save(uint id, DataValue value)
        {
            lock (m_publishes)
            {
                m_publishes.Enqueue(value);
            }
        }

        /// <summary>
        /// Raised when a publish response arrives from the server.
        /// </summary>
        static void Session_Notification(Session session, NotificationEventArgs e)
        {
            NotificationMessage message = e.NotificationMessage;

            // check for keep alive.
            if (message.NotificationData.Count == 0)
            {
                Console.WriteLine(
                    "===>>> Subscription KeepAlive: SubscriptionId={0} MessageId={1} Time={2:HH:mm:ss.fff}",
                    e.Subscription.Id,
                    message.SequenceNumber,
                    message.PublishTime.ToLocalTime());

                return;
            }

            DataChangeNotification dcn = (DataChangeNotification)ExtensionObject.ToEncodeable(message.NotificationData[0]);
            // Console.WriteLine("{0:mm:ss.fff} - SeqNo={1}, Items={2}", message.PublishTime, message.SequenceNumber, dcn.MonitoredItems.Count);

            int count = 0;

            // get the data changes (oldest to newest).
            foreach (MonitoredItemNotification datachange in message.GetDataChanges(false))
            {
                // lookup the monitored item.
                MonitoredItem monitoredItem = e.Subscription.FindItemByClientHandle(datachange.ClientHandle);

                if (monitoredItem == null)
                {
                    Console.WriteLine("MonitoredItem ClientHandle not known: {0}", datachange.ClientHandle);
                    continue;
                }

                // this is called on another thread so we need to synchronize before accessing the node.
                lock (m_lock)
                {
                    NodeOfInterest node = monitoredItem.Handle as NodeOfInterest;

                    //Console.WriteLine(
                    //    "Update for {0}: {1} Status={2} Timestamp={3:HH:mm:ss.fff}", 
                    //    node.DisplayName, 
                    //    datachange.Value.WrappedValue,
                    //    datachange.Value.StatusCode,
                    //    datachange.Value.SourceTimestamp.ToLocalTime());

                    node.Value = datachange.Value;
                    Save(datachange.ClientHandle, node.Value);
                    count++;
                }
            }

            if (count > NotificationsPerPublish)
            {
                Console.WriteLine("Too many notifications in Publish: {0}/{1}", count, NotificationsPerPublish);
            }

            lock (m_publishes)
            {
                m_notifications++;

                if (m_lastDump.AddSeconds(1) > DateTime.UtcNow)
                {
                    return;
                }

                int sampleCount = 0;
                int itemCount = 0;

                DateTime timestamp = DateTime.MinValue;

                while (m_publishes.Count > 0)
                {
                    DataValue value1 = m_publishes.Dequeue();

                    if (timestamp < value1.SourceTimestamp)
                    {
                        if (timestamp != DateTime.MinValue)
                        {
                            //Console.WriteLine(
                            //    "Items = {1}, Timestamp = {0:mm:ss.fff}", 
                            //    timestamp,
                            //    itemCount);
                        }

                        timestamp = value1.SourceTimestamp;
                        itemCount = 0;
                    }

                    sampleCount++;
                    itemCount++;
                }

                //Console.WriteLine(
                //    "Items = {1}, Timestamp = {0:mm:ss.fff}", 
                //    timestamp,
                //    itemCount);

                uint expectedSamples = 10000; // (uint)((1000.0/SamplingInterval)*ItemsToMonitor);
                uint expectedNotifications = 50;

                Console.WriteLine(
                    "{0:mm:ss.fff}-{1:mm:ss.fff}, Messages = {2}/{3}, Samples = {4}/{5}, MissedSamples = {6}",
                    m_lastDump,
                    m_lastDump.AddSeconds(1),
                    m_notifications,
                    expectedNotifications,
                    sampleCount,
                    expectedSamples,
                    (int)m_expectedSamples - (int)m_actualSamples);

                m_lastDump = m_lastDump.AddSeconds(1);
                m_lastMessage = message.SequenceNumber;
                m_dumpCount++;
                m_notifications = 0;

                m_actualSamples += (uint)sampleCount;
                m_expectedSamples += expectedSamples;

                if (m_dumpCount == 10)
                {
                    m_actualSamples = 0;
                    m_expectedSamples = 0;
                }
            }
        }


        /// <summary>
        /// Reads the values for a set of variables.
        /// </summary>
        static void Read(Session session)
        {
            IList<NodeOfInterest> results = GetNodeIds(session, Opc.Ua.Objects.ObjectsFolder,
                VariableBrowsePaths.ToArray());
            // build list of nodes to read.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            for (int ii = 0; ii < results.Count; ii++)
            {
                ReadValueId nodeToRead = new ReadValueId();

                nodeToRead.NodeId = results[ii].NodeId;
                nodeToRead.AttributeId = Attributes.Value;
                
                nodesToRead.Add(nodeToRead);
            }

            // read values.
            DataValueCollection values;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = session.Read(
                null,
                0,
                TimestampsToReturn.Both,
                nodesToRead,
                out values,
                out diagnosticInfos);

            // verify that the server returned the correct number of results.
            Session.ValidateResponse(values, nodesToRead);
            Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
                          
            // process results.
            for (int ii = 0; ii < values.Count; ii++)
            {

                // check for error.
                if (StatusCode.IsBad(values[ii].StatusCode))
                {
                    ServiceResult result = Session.GetResult(values[ii].StatusCode, ii, diagnosticInfos, responseHeader);
                    Console.WriteLine("Read result for {0}: {1}", VariableBrowsePaths[ii], result.ToLongString());
                    continue;
                }
                
                // write value.
                Console.WriteLine( "{0}: V={1}, Q={2}, SrvT={3}, SrcT={4}",nodesToRead[ii].NodeId, values[ii].Value.ToString(),
                    values[ii].StatusCode.ToString(), values[ii].ServerTimestamp, values[ii].SourceTimestamp);
            }
        }


        /// <summary>
        /// Reads the history of values for a set of variables.
        /// </summary>
        static void HistoryReadRaw(Session session)
        {
            // translate browse paths.
            IList<NodeOfInterest> nodeIds = GetNodeIds(session, Opc.Ua.Objects.ObjectsFolder, 
                VariableBrowsePaths.ToArray());


            DiagnosticInfoCollection diagnosticInfos;

            ReadRawModifiedDetails readDetails = new ReadRawModifiedDetails();
            readDetails.StartTime = DateTime.MinValue;
            readDetails.EndTime = DateTime.Now;
            readDetails.IsReadModified = false;
            readDetails.NumValuesPerNode = 100;
            readDetails.ReturnBounds = false;

            ExtensionObject eo = new ExtensionObject(readDetails.TypeId, readDetails);

            HistoryReadValueIdCollection idCollection = new HistoryReadValueIdCollection();
            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                HistoryReadValueId readValueId = new HistoryReadValueId();
                readValueId.NodeId = nodeIds[ii].NodeId;
                readValueId.Processed = false;
                idCollection.Add(readValueId);
            }

            HistoryReadResultCollection historyReadResults;

            ResponseHeader responseHeader = 
                session.HistoryRead(null, eo, TimestampsToReturn.Both, true,
                idCollection, out historyReadResults, out diagnosticInfos);

            // process results.

            for (int ii = 0; ii < historyReadResults.Count; ii++)
            {
                HistoryReadResult historyReadResult = historyReadResults[ii];
                HistoryData historyData = null;
                DataValueCollection dataValues = null;
                if (historyReadResult.HistoryData != null)
                {
                    historyData = ExtensionObject.ToEncodeable(historyReadResult.HistoryData) as HistoryData;
                    dataValues = historyData.DataValues;
                }

                ServiceResult result = Session.GetResult(historyReadResult.StatusCode, ii, diagnosticInfos, responseHeader);
                Console.WriteLine("HistoryRead result code for {0}:  {1}", VariableBrowsePaths[ii], result.StatusCode.ToString());

                if (StatusCode.IsBad(historyReadResult.StatusCode))
                {
                    continue;
                }

                if (dataValues == null)
                {
                    Console.WriteLine("dataValues == null");
                    continue;
                }

                for (int jj = 0; jj < dataValues.Count; jj++)
                {

                    DataValue dataValue = dataValues[jj];

                    // write value.
                    Console.WriteLine("{0}: V={1}, Q={2}, SrvT={3}, SrcT={4}", jj, 
                        dataValue.Value == null ? "null" : dataValue.Value.ToString(), 
                        dataValue.StatusCode.ToString(), 
                        dataValue.ServerTimestamp, dataValue.SourceTimestamp);
                }
            }
        }


        /// <summary>
        /// Reads the history of attributes for Bucket Brigade.Int1.
        /// </summary>
        static void HistoryReadAttributes(Session session)
        {

            List<string> VariableBrowsePaths = new List<string>();
                VariableBrowsePaths.Add("/7:MatrikonOpc Sim Server/7:Simulation Items/7:Bucket Brigade/7:Int1/7:Description");
                VariableBrowsePaths.Add("/7:MatrikonOpc Sim Server/7:Simulation Items/7:Bucket Brigade/7:Int1/7:DataType");
                VariableBrowsePaths.Add("/7:MatrikonOpc Sim Server/7:Simulation Items/7:Bucket Brigade/7:Int1/7:ITEMID");

            // translate browse paths.
            IList<NodeOfInterest> nodeIds = GetNodeIds(session, Opc.Ua.Objects.ObjectsFolder, 
                VariableBrowsePaths.ToArray());


            DiagnosticInfoCollection diagnosticInfos;

            ReadRawModifiedDetails readDetails = new ReadRawModifiedDetails();
            readDetails.StartTime = DateTime.MinValue;
            readDetails.EndTime = DateTime.Now;
            readDetails.IsReadModified = false;
            readDetails.NumValuesPerNode = 100;
            readDetails.ReturnBounds = false;

            ExtensionObject eo = new ExtensionObject(readDetails.TypeId, readDetails);

            HistoryReadValueIdCollection idCollection = new HistoryReadValueIdCollection();
            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                HistoryReadValueId readValueId = new HistoryReadValueId();
                readValueId.NodeId = nodeIds[ii].NodeId;
                readValueId.Processed = false;
                idCollection.Add(readValueId);
            }

            HistoryReadResultCollection historyReadResults;

            ResponseHeader responseHeader =
                session.HistoryRead(null, eo, TimestampsToReturn.Both, true,
                idCollection, out historyReadResults, out diagnosticInfos);

            // process results.

            for (int ii = 0; ii < historyReadResults.Count; ii++)
            {
                HistoryReadResult historyReadResult = historyReadResults[ii];
                HistoryData historyData = null;
                DataValueCollection dataValues = null;
                if (historyReadResult.HistoryData != null)
                {
                    historyData = ExtensionObject.ToEncodeable(historyReadResult.HistoryData) as HistoryData;
                    dataValues = historyData.DataValues;
                }

                ServiceResult result = Session.GetResult(historyReadResult.StatusCode, ii, diagnosticInfos, responseHeader);
                
                Console.WriteLine("\nHistoryRead result code for {0}:  {1}", VariableBrowsePaths[ii], result.StatusCode.ToString());

                if (StatusCode.IsBad(historyReadResult.StatusCode))
                {
                    continue;
                }

                if (dataValues == null)
                {
                    Console.WriteLine("dataValues == null");
                    continue;
                }

                for (int jj = 0; jj < dataValues.Count; jj++)
                {

                    DataValue dataValue = dataValues[jj];

                    // write value.
                    Console.WriteLine("\t{0}: V={1}",jj, dataValue.Value == null ? "null" : dataValue.Value.ToString());
                    Console.WriteLine("\t Q={0}, SrvT={1}, SrcT={2}\n",
                        dataValue.StatusCode.ToString(), dataValue.ServerTimestamp, dataValue.SourceTimestamp);
                }
            }
        }

        /// <summary>
        /// Reads the history of values for a set of variables at given time.
        /// </summary>
        static void HistoryReadAtTime(Session session)
        {
            // translate browse paths.
            IList<NodeOfInterest> nodeIds = GetNodeIds(session, Opc.Ua.Objects.ObjectsFolder, 
                VariableBrowsePaths.ToArray());


            DiagnosticInfoCollection diagnosticInfos;

            ReadAtTimeDetails readDetails = new ReadAtTimeDetails();

            readDetails.ReqTimes = new DateTimeCollection();

            for (int jj = 0; jj < 10; jj++)
            {
                readDetails.ReqTimes.Add(new DateTime(2008, 01, 01, 12, 0, jj));
                readDetails.ReqTimes.Add(new DateTime(2008, 01, 01, 12, 0, jj, (int) 500));
            }

            ExtensionObject eo = new ExtensionObject(readDetails.TypeId, readDetails);

            HistoryReadValueIdCollection idCollection = new HistoryReadValueIdCollection();
            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                HistoryReadValueId readValueId = new HistoryReadValueId();
                readValueId.NodeId = nodeIds[ii].NodeId;
                readValueId.Processed = false;
                idCollection.Add(readValueId);
            }

            HistoryReadResultCollection historyReadResults;

            ResponseHeader responseHeader =
                session.HistoryRead(null, eo, TimestampsToReturn.Both, true,
                idCollection, out historyReadResults, out diagnosticInfos);

            // process results.

            for (int ii = 0; ii < historyReadResults.Count; ii++)
            {
                HistoryReadResult historyReadResult = historyReadResults[ii];
                HistoryData historyData = null;
                DataValueCollection dataValues = null;
                if (historyReadResult.HistoryData != null)
                {
                    historyData = ExtensionObject.ToEncodeable(historyReadResult.HistoryData) as HistoryData;
                    dataValues = historyData.DataValues;
                }

                ServiceResult result = Session.GetResult(historyReadResult.StatusCode, ii, diagnosticInfos, responseHeader);
                Console.WriteLine("HistoryRead result code for {0}:  {1}", VariableBrowsePaths[ii], result.StatusCode.ToString());

                if (StatusCode.IsBad(historyReadResult.StatusCode))
                {
                    continue;
                }

                if (dataValues == null)
                {
                    Console.WriteLine("dataValues == null");
                    continue;
                }

                for (int jj = 0; jj < dataValues.Count; jj++)
                {

                    DataValue dataValue = dataValues[jj];

                    // write value.
                    Console.WriteLine("{0}: V={1}, Q={2}, SrvT={3}, SrcT={4}", jj,
                        dataValue.Value == null ? "null" : dataValue.Value.ToString(),
                        dataValue.StatusCode.ToString(),
                        dataValue.ServerTimestamp, dataValue.SourceTimestamp);
                }
            }
        }

        /// <summary>
        /// Deletes the history of values for a set of variables at given time.
        /// </summary>
        static void HistoryDeleteAtTime(Session session)
        {
            // translate browse paths.
            IList<NodeOfInterest> results = GetNodeIds(session, Opc.Ua.Objects.ObjectsFolder, 
                VariableBrowsePaths.ToArray());


            DiagnosticInfoCollection diagnosticInfos;

            ExtensionObjectCollection eoc = new ExtensionObjectCollection();
            for (int ii = 0; ii < results.Count; ii++)
            {
                DeleteAtTimeDetails deleteDetails = new DeleteAtTimeDetails();
                deleteDetails.ReqTimes = new DateTimeCollection();
                for (int jj = 0; jj < 5; jj++)
                {
                    deleteDetails.ReqTimes.Add(new DateTime(2008, 01, 01, 12, 0, jj * 2));
                }

                deleteDetails.NodeId = results[ii].NodeId;
                deleteDetails.Processed = false;

                ExtensionObject eo = new ExtensionObject(deleteDetails.TypeId, deleteDetails);
                eoc.Add(eo);
            }

            HistoryUpdateResultCollection historyUpdateResults;

            ResponseHeader responseHeader =
                session.HistoryUpdate(null, eoc, out historyUpdateResults, out diagnosticInfos);

            // process results.

            for (int ii = 0; ii < historyUpdateResults.Count; ii++)
            {
                HistoryUpdateResult historyUpdateResult = historyUpdateResults[ii];

                Console.WriteLine("HistoryUpdate result code for {0}:  {1}", VariableBrowsePaths[ii], historyUpdateResult.StatusCode.ToString());

                if (StatusCode.IsGood(historyUpdateResult.StatusCode))
                {
                    for (int jj = 0; jj < historyUpdateResult.OperationResults.Count; jj++)
                    {
                        Console.WriteLine("    {0}: {1}", jj, historyUpdateResult.OperationResults[jj]);
                    }
                    Console.WriteLine("");
                }
            }
        }


        /// <summary>
        /// Deletes the history of values for a set of variables at given time interval.
        /// </summary>
        static void HistoryDeleteRaw(Session session)
        {
            // translate browse paths.
            IList<NodeOfInterest> results = GetNodeIds(session, Opc.Ua.Objects.ObjectsFolder, 
                VariableBrowsePaths.ToArray());


            DiagnosticInfoCollection diagnosticInfos;

            ExtensionObjectCollection eoc = new ExtensionObjectCollection();
            for (int ii = 0; ii < results.Count; ii++)
            {
                DeleteRawModifiedDetails deleteDetails = new DeleteRawModifiedDetails();
                deleteDetails.StartTime = new DateTime(2008, 1, 1, 12, 0, 0);
                deleteDetails.EndTime = new DateTime(2008, 1, 1, 12, 0, 10);

                deleteDetails.NodeId = results[ii].NodeId;
                deleteDetails.IsDeleteModified = false;

                ExtensionObject eo = new ExtensionObject(deleteDetails.TypeId, deleteDetails);
                eoc.Add(eo);
            }

            HistoryUpdateResultCollection historyUpdateResults;

            ResponseHeader responseHeader =
                session.HistoryUpdate(null, eoc, out historyUpdateResults, out diagnosticInfos);

            // process results.

            for (int ii = 0; ii < historyUpdateResults.Count; ii++)
            {
                HistoryUpdateResult historyUpdateResult = historyUpdateResults[ii];

                Console.WriteLine("HistoryUpdate result code for {0}:  {1}", VariableBrowsePaths[ii], historyUpdateResult.StatusCode.ToString());

                if (StatusCode.IsGood(historyUpdateResult.StatusCode))
                {
                    for (int jj = 0; jj < historyUpdateResult.OperationResults.Count; jj++)
                    {
                        Console.WriteLine("    {0}: {1}", jj, historyUpdateResult.OperationResults[jj]);
                    }
                    Console.WriteLine("");
                }
            }
        }
        

        static void HistoryReadProcessed(Session session)
        {
            // translate browse paths.
            IList<NodeOfInterest> nodeIds = GetNodeIds(session, Opc.Ua.Objects.ObjectsFolder, 
                VariableBrowsePaths.ToArray());

            DiagnosticInfoCollection diagnosticInfos;

            NodeId aggregateNodeId = null;

            RequestHeader rh = null;
            ViewDescription vd = null;
            ReferenceDescriptionCollection references;
            byte[] cp;

            //Get the list of avalilable aggregate functions:
            session.Browse(
                rh, 
                vd, 
                Opc.Ua.ObjectIds.Server_ServerCapabilities_AggregateFunctions,
                1000,
                BrowseDirection.Forward, 
                ReferenceTypeIds.Aggregates, 
                false, 
                0, 
                out cp, 
                out references);

            Console.WriteLine("{0} aggregates are detected:", references.Count);

            //Print the list of avalible aggregates:
            int i = 0;
            foreach (ReferenceDescription rd in references)
            {
                i++;
                Console.WriteLine("{0}. {1} {2}", i, rd.BrowseName, rd.NodeId.Identifier.ToString());
            }

            //Select aggregate function:
            Console.WriteLine("\nEnter aggregate number: ");
            string str = Console.ReadLine();

            i = System.Int16.Parse(str);

            if (i > 0 && i <= references.Count)
            {
                aggregateNodeId = ExpandedNodeId.ToNodeId(references[i - 1].NodeId, session.NamespaceUris);
            }

            //Prepare arguments to pass to read processed history
            ReadProcessedDetails readDetails = new ReadProcessedDetails();

            readDetails.StartTime = new DateTime(2008, 1, 1, 12, 0, 0);
            readDetails.EndTime = new DateTime(2008, 1, 1, 12, 0, 12);

            readDetails.AggregateType = new NodeIdCollection(nodeIds.Count);
            for (int x = 0; x < nodeIds.Count; x++)
            {
                readDetails.AggregateType.Add (aggregateNodeId);
            }

            readDetails.ProcessingInterval = 500; //500 milliseconds

            ExtensionObject eo = new ExtensionObject(readDetails.TypeId, readDetails);

            HistoryReadValueIdCollection idCollection = new HistoryReadValueIdCollection();
            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                HistoryReadValueId readValueId = new HistoryReadValueId();
                readValueId.NodeId = nodeIds[ii].NodeId;
                readValueId.Processed = true;
                idCollection.Add(readValueId);
            }

            HistoryReadResultCollection historyReadResults;

            //Read processed history:
            ResponseHeader responseHeader =
                session.HistoryRead(null, eo, TimestampsToReturn.Both, true,
                idCollection, out historyReadResults, out diagnosticInfos);

            //Print results:
            for (int ii = 0; ii < historyReadResults.Count; ii++)
            {
                HistoryReadResult historyReadResult = historyReadResults[ii];
                ServiceResult result = Session.GetResult(historyReadResult.StatusCode, ii, diagnosticInfos, responseHeader);
                
                HistoryData historyData = null;
                DataValueCollection dataValues = null;
                if ( !(historyReadResult.HistoryData == null) )
                {
                    historyData = ExtensionObject.ToEncodeable(historyReadResult.HistoryData) as HistoryData;
                    if (historyData == null)
                        dataValues = null;
                    else
                        dataValues = historyData.DataValues;
                }

                Console.WriteLine("\nHistoryRead result code for {0}:  {1}", VariableBrowsePaths[ii], result.StatusCode.ToString());

                if (dataValues == null)
                {
                    Console.WriteLine("dataValues == null");
                    continue;
                }

                for (int jj = 0; jj < dataValues.Count; jj++)
                {
                    DataValue dataValue = dataValues[jj];
                    if (dataValue == null)
                        continue;

                    // write value.
                    Console.WriteLine("{0}: V={1}, Q={2}, SrvT={3}, SrcT={4}", jj,
                        dataValue.Value == null ? "null" : dataValue.Value.ToString(),
                        dataValue.StatusCode.ToString(),
                        dataValue.ServerTimestamp, dataValue.SourceTimestamp);
                }
            }
        }

        static void Browse(Session session)
        {
            DiagnosticInfoCollection diagnosticInfos;

            BrowseDescriptionCollection bc = new BrowseDescriptionCollection();
            BrowseDescription bd = new BrowseDescription();
            bd.BrowseDirection = BrowseDirection.Forward;
            NodeId nodeId = Opc.Ua.Objects.ObjectsFolder;

            do
            {
                Console.WriteLine("\n Enter nodeId to Browse (or q to exit)");
                string s = Console.ReadLine();
                if (s == "q")
                    break;
                if (s.Length == 0)
                {
                    nodeId = Opc.Ua.Objects.ObjectsFolder;
                }
                else
                    nodeId = new NodeId(s);
                bc.Clear();

                bd.NodeId = nodeId;

                bc.Add(bd);

                BrowseResultCollection results;

                ResponseHeader rh =
                    session.Browse(null, null, 100, bc, out results, out diagnosticInfos);
                
                foreach ( BrowseResult res in results)
                {
                    foreach (ReferenceDescription rdc in res.References)
                    {
                        Console.WriteLine(String.Format(" Node = {0} (namespace {1}) {2}", rdc.NodeId.ToString(),rdc.NodeId.NamespaceIndex, Environment.NewLine));
                    }
                }
            } while (true);

        }

        static void HistoryUpdate(Session session)
        {
            DiagnosticInfoCollection diagnosticInfos;

            // translate browse paths.
            IList<NodeOfInterest> nodeIds;

            nodeIds = GetNodeIds(session, Opc.Ua.Objects.ObjectsFolder,
                VariableBrowsePaths.ToArray());

            ExtensionObjectCollection eoc = new ExtensionObjectCollection();
            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                UpdateDataDetails updateDetails = new UpdateDataDetails();

                updateDetails.NodeId = nodeIds[ii].NodeId;
                updateDetails.PerformInsertReplace = PerformUpdateType.Update;
                updateDetails.UpdateValues = new DataValueCollection();

                for (int jj = 0; jj <= 5; jj++)
                {
                    DataValue dv = new DataValue(new Variant(jj*10), StatusCodes.Good, new DateTime(2008, 01, 01, 12, 0, jj*2));
                    updateDetails.UpdateValues.Add(dv);
                }
                ExtensionObject eo = new ExtensionObject(updateDetails.TypeId, updateDetails);
                eoc.Add(eo);
            }

            HistoryUpdateResultCollection historyUpdateResults;

            ResponseHeader responseHeader =
                session.HistoryUpdate(null, eoc, out historyUpdateResults, out diagnosticInfos);

            
            // process results.

            for (int ii = 0; ii < historyUpdateResults.Count; ii++)
            {
                HistoryUpdateResult historyUpdateResult = historyUpdateResults[ii];

                Console.WriteLine("HistoryUpdate result code for {0}:  {1}", VariableBrowsePaths[ii], historyUpdateResult.StatusCode.ToString());

                if (StatusCode.IsGood(historyUpdateResult.StatusCode))
                {
                    for (int jj = 0; jj < historyUpdateResult.OperationResults.Count; jj++)
                    {
                        Console.WriteLine("    {0}: {1}", jj, historyUpdateResult.OperationResults[jj]);
                    }
                    Console.WriteLine("");
                }
            }
        }
    }
}
