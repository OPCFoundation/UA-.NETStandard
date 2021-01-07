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
using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Client;

namespace Quickstarts.ConsoleReferenceClient
{
    /// <summary>
    /// OPC UA Client with examples of basic functionality.
    /// </summary>
    class UAClient
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the UAClient class.
        /// </summary>
        public UAClient(ApplicationConfiguration configuration)
        {
            m_configuration = configuration;
            m_configuration.CertificateValidator.CertificateValidation += CertificateValidation;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the client session.
        /// </summary>
        public Session Session => m_session;

        /// <summary>
        /// Gets or sets the server URL.
        /// </summary>
        public string ServerUrl
        {
            get
            {
                return m_serverUrl;
            }
            set
            {
                m_serverUrl = value;
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Creates a session with the UA server
        /// </summary>
        public bool Connect()
        {
            try
            {
                if (m_session != null && m_session.Connected == true)
                {
                    Console.WriteLine("Session already connected!");
                }
                else
                {
                    Console.WriteLine("Connecting...");

                    // Get the endpoint by connecting to server's discovery endpoint.
                    // Try to find the first endopint without security.
                    EndpointDescription endpointDescription = CoreClientUtils.SelectEndpoint(m_serverUrl, false);

                    EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_configuration);
                    ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

                    // Create the session
                    Session session = Session.Create(
                        m_configuration,
                        endpoint,
                        false,
                        false,
                        m_configuration.ApplicationName,
                        30 * 60 * 1000,
                        new UserIdentity(),
                        null).Result;

                    // Assign the created session
                    if (session != null && session.Connected)
                    {
                        m_session = session;
                    }

                    // Session created successfully.
                    string logMessage = String.Format("New Session Created with SessionName = {0}.", m_session.SessionName);
                    Console.WriteLine(logMessage);
                }

                return true;
            }
            catch (Exception ex)
            {
                // Log Error
                string logMessage = String.Format("Create Session Error : {0}.", ex.Message);
                Console.WriteLine(logMessage);
                return false;
            }
        }

        /// <summary>
        /// Disconnects the session.
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (m_session != null)
                {
                    Console.WriteLine("Disconnecting...");

                    m_session.Close();
                    m_session.Dispose();
                    m_session = null;

                    // Log Session Disconnected event
                    string logMessage = String.Format("Session Disconnected.");
                    Console.WriteLine(logMessage);
                }
                else
                {
                    Console.WriteLine("Session not created!");
                }
            }
            catch (Exception ex)
            {
                // Log Error
                string logMessage = String.Format("Disconnect Error : {0}.", ex.Message);
                Console.WriteLine(logMessage);
            }
        }

        /// <summary>
        /// Read a list of nodes from Server
        /// </summary>
        public void ReadNodes()
        {
            if (m_session == null || m_session.Connected == false)
            {
                Console.WriteLine("Session not connected!");
                return;
            }

            try
            {
                #region Read a node by calling the Read Service

                // build a list of nodes to be read
                ReadValueIdCollection nodesToRead = new ReadValueIdCollection()
                {
                    // Value of ServerStatus
                    new ReadValueId() { NodeId = Variables.Server_ServerStatus, AttributeId = Attributes.Value },
                    // BrowseName of ServerStatus_StartTime
                    new ReadValueId() { NodeId = Variables.Server_ServerStatus_StartTime, AttributeId = Attributes.BrowseName },
                    // Value of ServerStatus_StartTime
                    new ReadValueId() { NodeId = Variables.Server_ServerStatus_StartTime, AttributeId = Attributes.Value }
                };

                // Read the node attributes
                DataValueCollection resultsValues = null;
                DiagnosticInfoCollection diagnosticInfos = null;
                Console.WriteLine("Reading nodes...");

                // Call Read Service
                m_session.Read(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    nodesToRead,
                    out resultsValues,
                    out diagnosticInfos);

                // Validate the results
                ClientBase.ValidateResponse(resultsValues, nodesToRead);

                // Display the results.
                foreach (DataValue result in resultsValues)
                {
                    Console.WriteLine("Read Value = {0} , StatusCode = {1}", result.Value, result.StatusCode);
                }
                #endregion

                #region Read the Value attribute of a node by calling the Session.ReadValue method
                // Read Server NamespaceArray
                Console.WriteLine("Reading Value of NamespaceArray node...");
                DataValue namespaceArray = m_session.ReadValue(Variables.Server_NamespaceArray);
                // Display the result
                Console.WriteLine(String.Format("NamespaceArray Value = {0}", namespaceArray.ToString()));
                #endregion
            }
            catch (Exception ex)
            {
                // Log Error
                string logMessage = String.Format("Read Nodes Error : {0}.", ex.Message);
                Console.WriteLine(logMessage);
            }
        }

        /// <summary>
        /// Write a list of nodes to the Server
        /// </summary>
        public void WriteNodes()
        {
            if (m_session == null || m_session.Connected == false)
            {
                Console.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Write the configured nodes
                WriteValueCollection nodesToWrite = new WriteValueCollection();

                // Int32 Node - Objects\CTT\Scalar\Scalar_Static\Int32
                WriteValue intWriteVal = new WriteValue();
                intWriteVal.NodeId = new NodeId("ns=2;s=Scalar_Static_Int32");
                intWriteVal.AttributeId = Attributes.Value;
                intWriteVal.Value = new DataValue();
                intWriteVal.Value.Value = (int)100;
                nodesToWrite.Add(intWriteVal);

                // Float Node - Objects\CTT\Scalar\Scalar_Static\Float
                WriteValue floatWriteVal = new WriteValue();
                floatWriteVal.NodeId = new NodeId("ns=2;s=Scalar_Static_Float");
                floatWriteVal.AttributeId = Attributes.Value;
                floatWriteVal.Value = new DataValue();
                floatWriteVal.Value.Value = (float)100.5;
                nodesToWrite.Add(floatWriteVal);

                // String Node - Objects\CTT\Scalar\Scalar_Static\String
                WriteValue stringWriteVal = new WriteValue();
                stringWriteVal.NodeId = new NodeId("ns=2;s=Scalar_Static_String");
                stringWriteVal.AttributeId = Attributes.Value;
                stringWriteVal.Value = new DataValue();
                stringWriteVal.Value.Value = "String Test";
                nodesToWrite.Add(stringWriteVal);

                // Write the node attributes
                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos;
                Console.WriteLine("Writing nodes...");

                // Call Write Service
                m_session.Write(null,
                                nodesToWrite,
                                out results,
                                out diagnosticInfos);

                // Validate the response
                ClientBase.ValidateResponse(results, nodesToWrite);

                // Display the results.
                Console.WriteLine("Write Results :");

                foreach (StatusCode writeResult in results)
                {
                    Console.WriteLine("     {0}", writeResult);
                }
            }
            catch (Exception ex)
            {
                // Log Error
                string logMessage = String.Format("Write Nodes Error : {0}.", ex.Message);
                Console.WriteLine(logMessage);
            }
        }

        /// <summary>
        /// Browse Server nodes
        /// </summary>
        public void Browse()
        {
            if (m_session == null || m_session.Connected == false)
            {
                Console.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Create a Browser object
                Browser browser = new Browser(m_session);

                // Set browse parameters
                browser.BrowseDirection = BrowseDirection.Forward;
                browser.NodeClassMask = (int)NodeClass.Object | (int)NodeClass.Variable;
                browser.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;

                NodeId nodeToBrowse = ObjectIds.Server;

                // Call Browse service
                Console.WriteLine("Browsing {0} node...", nodeToBrowse);
                ReferenceDescriptionCollection browseResults = browser.Browse(nodeToBrowse);

                // Display the results
                Console.WriteLine("Browse returned {0} results:", browseResults.Count);

                foreach (ReferenceDescription result in browseResults)
                {
                    Console.WriteLine("     DisplayName = {0}, NodeClass = {1}", result.DisplayName.Text, result.NodeClass);
                }
            }
            catch (Exception ex)
            {
                // Log Error
                string logMessage = String.Format("Browse Error : {0}.", ex.Message);
                Console.WriteLine(logMessage);
            }
        }

        /// <summary>
        /// Call UA method
        /// </summary>
        public void CallMethod()
        {
            if (m_session == null || m_session.Connected == false)
            {
                Console.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Define the UA Method to call
                // Parent node - Objects\CTT\Methods
                // Method node - Objects\CTT\Methods\Add
                NodeId objectId = new NodeId("ns=2;s=Methods");
                NodeId methodId = new NodeId("ns=2;s=Methods_Add");

                // Define the method parameters
                // Input argument requires a Float and an UInt32 value
                object[] inputArguments = new object[] { (float)10.5, (uint)10 };
                IList<object> outputArguments = null;

                // Invoke Call service
                Console.WriteLine("Calling UAMethod for node {0} ...", methodId);
                outputArguments = m_session.Call(objectId, methodId, inputArguments);

                // Display results
                Console.WriteLine("Method call returned {0} output argument(s):", outputArguments.Count);

                foreach (var outputArgument in outputArguments)
                {
                    Console.WriteLine("     OutputValue = {0}", outputArgument.ToString());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Method call error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Create Subscription and MonitoredItems for DataChanges
        /// </summary>
        public void SubscribeToDataChanges()
        {
            if (m_session == null || m_session.Connected == false)
            {
                Console.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Create a subscription for receiving data change notifications

                // Define Subscription parameters
                Subscription subscription = new Subscription(m_session.DefaultSubscription);

                subscription.DisplayName = "Console ReferenceClient Subscription";
                subscription.PublishingEnabled = true;
                subscription.PublishingInterval = 1000;

                m_session.AddSubscription(subscription);

                // Create the subscription on Server side
                subscription.Create();
                Console.WriteLine("New Subscription created with SubscriptionId = {0}.", subscription.Id);

                // Create MonitoredItems for data changes

                MonitoredItem intMonitoredItem = new MonitoredItem(subscription.DefaultItem);
                // Int32 Node - Objects\CTT\Scalar\Simulation\Int32
                intMonitoredItem.StartNodeId = new NodeId("ns=2;s=Scalar_Simulation_Int32");
                intMonitoredItem.AttributeId = Attributes.Value;
                intMonitoredItem.DisplayName = "Int32 Variable";
                intMonitoredItem.SamplingInterval = 1000;
                intMonitoredItem.Notification += OnMonitoredItemNotification;

                subscription.AddItem(intMonitoredItem);

                MonitoredItem floatMonitoredItem = new MonitoredItem(subscription.DefaultItem);
                // Float Node - Objects\CTT\Scalar\Simulation\Float
                floatMonitoredItem.StartNodeId = new NodeId("ns=2;s=Scalar_Simulation_Float");
                floatMonitoredItem.AttributeId = Attributes.Value;
                floatMonitoredItem.DisplayName = "Float Variable";
                floatMonitoredItem.SamplingInterval = 1000;
                floatMonitoredItem.Notification += OnMonitoredItemNotification;

                subscription.AddItem(floatMonitoredItem);

                MonitoredItem stringMonitoredItem = new MonitoredItem(subscription.DefaultItem);
                // String Node - Objects\CTT\Scalar\Simulation\String
                stringMonitoredItem.StartNodeId = new NodeId("ns=2;s=Scalar_Simulation_String");
                stringMonitoredItem.AttributeId = Attributes.Value;
                stringMonitoredItem.DisplayName = "String Variable";
                stringMonitoredItem.SamplingInterval = 1000;
                stringMonitoredItem.Notification += OnMonitoredItemNotification;

                subscription.AddItem(stringMonitoredItem);

                // Create the monitored items on Server side
                subscription.ApplyChanges();
                Console.WriteLine("MonitoredItems created for SubscriptionId = {0}.", subscription.Id);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Subscribe error: {0}", ex.Message);
            }
        }
        #endregion

        #region Private Methods

        /// <summary>
        /// Handle DataChange notifications from Server
        /// </summary>
        private void OnMonitoredItemNotification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            try
            {
                // Log MonitoredItem Notification event
                MonitoredItemNotification notification = e.NotificationValue as MonitoredItemNotification;
                Console.WriteLine("Notification Received for Variable \"{0}\" and Value = {1}.", monitoredItem.DisplayName, notification.Value);
            }
            catch (Exception ex)
            {
                Console.WriteLine("OnMonitoredItemNotification error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Handles the certificate validation event.
        /// This event is triggered every time an untrusted certificate is received from the server.
        /// </summary>
        private void CertificateValidation(CertificateValidator sender, CertificateValidationEventArgs e)
        {
            bool certificateAccepted = true;

            // ****
            // Implement a custom logic to decide if the certificate should be
            // accepted or not and set certificateAccepted flag accordingly.
            // The certificate can be retrieved from the e.Certificate field
            // ***

            ServiceResult error = e.Error;
            while (error != null)
            {
                Console.WriteLine(error);
                error = error.InnerResult;
            }

            if (certificateAccepted)
            {
                Console.WriteLine("Untrusted Certificate accepted. SubjectName = {0}", e.Certificate.SubjectName);
            }

            e.AcceptAll = certificateAccepted;
        }
        #endregion

        #region Private Fields

        private ApplicationConfiguration m_configuration;

        private string m_serverUrl = "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";

        private Session m_session;

        #endregion
    }
}
