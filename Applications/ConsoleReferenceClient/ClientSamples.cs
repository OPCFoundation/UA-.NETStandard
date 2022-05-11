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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Opc.Ua;
using Opc.Ua.Client;

namespace Quickstarts.ConsoleReferenceClient
{
    /// <summary>
    /// Sample Session calls based on the reference server node model.
    /// </summary>
    public class ClientSamples
    {

        public ClientSamples(TextWriter output, Action<IList, IList> validateResponse)
        {
            m_output = output;
            m_validateResponse = validateResponse;
        }

        #region Public Sample Methods
        /// <summary>
        /// Read a list of nodes from Server
        /// </summary>
        public void ReadNodes(Session session)
        {
            if (session == null || session.Connected == false)
            {
                m_output.WriteLine("Session not connected!");
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
                m_output.WriteLine("Reading nodes...");

                // Call Read Service
                session.Read(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    nodesToRead,
                    out DataValueCollection resultsValues,
                    out DiagnosticInfoCollection diagnosticInfos);

                // Validate the results
                m_validateResponse(resultsValues, nodesToRead);

                // Display the results.
                foreach (DataValue result in resultsValues)
                {
                    m_output.WriteLine("Read Value = {0} , StatusCode = {1}", result.Value, result.StatusCode);
                }
                #endregion

                #region Read the Value attribute of a node by calling the Session.ReadValue method
                // Read Server NamespaceArray
                m_output.WriteLine("Reading Value of NamespaceArray node...");
                DataValue namespaceArray = session.ReadValue(Variables.Server_NamespaceArray);
                // Display the result
                m_output.WriteLine($"NamespaceArray Value = {namespaceArray}");
                #endregion
            }
            catch (Exception ex)
            {
                // Log Error
                m_output.WriteLine($"Read Nodes Error : {ex.Message}.");
            }
        }

        /// <summary>
        /// Write a list of nodes to the Server
        /// </summary>
        public void WriteNodes(Session session)
        {
            if (session == null || session.Connected == false)
            {
                m_output.WriteLine("Session not connected!");
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
                m_output.WriteLine("Writing nodes...");

                // Call Write Service
                session.Write(null,
                                nodesToWrite,
                                out results,
                                out diagnosticInfos);

                // Validate the response
                m_validateResponse(results, nodesToWrite);

                // Display the results.
                m_output.WriteLine("Write Results :");

                foreach (StatusCode writeResult in results)
                {
                    m_output.WriteLine("     {0}", writeResult);
                }
            }
            catch (Exception ex)
            {
                // Log Error
                m_output.WriteLine($"Write Nodes Error : {ex.Message}.");
            }
        }

        /// <summary>
        /// Browse Server nodes
        /// </summary>
        public void Browse(Session session)
        {
            if (session == null || session.Connected == false)
            {
                m_output.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Create a Browser object
                Browser browser = new Browser(session);

                // Set browse parameters
                browser.BrowseDirection = BrowseDirection.Forward;
                browser.NodeClassMask = (int)NodeClass.Object | (int)NodeClass.Variable;
                browser.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;

                NodeId nodeToBrowse = ObjectIds.Server;

                // Call Browse service
                m_output.WriteLine("Browsing {0} node...", nodeToBrowse);
                ReferenceDescriptionCollection browseResults = browser.Browse(nodeToBrowse);

                // Display the results
                m_output.WriteLine("Browse returned {0} results:", browseResults.Count);

                foreach (ReferenceDescription result in browseResults)
                {
                    m_output.WriteLine("     DisplayName = {0}, NodeClass = {1}", result.DisplayName.Text, result.NodeClass);
                }
            }
            catch (Exception ex)
            {
                // Log Error
                m_output.WriteLine($"Browse Error : {ex.Message}.");
            }
        }

        /// <summary>
        /// Call UA method
        /// </summary>
        public void CallMethod(Session session)
        {
            if (session == null || session.Connected == false)
            {
                m_output.WriteLine("Session not connected!");
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
                m_output.WriteLine("Calling UAMethod for node {0} ...", methodId);
                outputArguments = session.Call(objectId, methodId, inputArguments);

                // Display results
                m_output.WriteLine("Method call returned {0} output argument(s):", outputArguments.Count);

                foreach (var outputArgument in outputArguments)
                {
                    m_output.WriteLine("     OutputValue = {0}", outputArgument.ToString());
                }
            }
            catch (Exception ex)
            {
                m_output.WriteLine("Method call error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Create Subscription and MonitoredItems for DataChanges
        /// </summary>
        public void SubscribeToDataChanges(Session session, uint minLifeTime)
        {
            if (session == null || session.Connected == false)
            {
                m_output.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Create a subscription for receiving data change notifications

                // Define Subscription parameters
                Subscription subscription = new Subscription(session.DefaultSubscription) {
                    DisplayName = "Console ReferenceClient Subscription",
                    PublishingEnabled = true,
                    PublishingInterval = 1000,
                    LifetimeCount = 0,
                    MinLifetimeInterval = minLifeTime,
                };

                session.AddSubscription(subscription);

                // Create the subscription on Server side
                subscription.Create();
                m_output.WriteLine("New Subscription created with SubscriptionId = {0}.", subscription.Id);

                // Create MonitoredItems for data changes (Reference Server)

                MonitoredItem intMonitoredItem = new MonitoredItem(subscription.DefaultItem);
                // Int32 Node - Objects\CTT\Scalar\Simulation\Int32
                intMonitoredItem.StartNodeId = new NodeId("ns=2;s=Scalar_Simulation_Int32");
                intMonitoredItem.AttributeId = Attributes.Value;
                intMonitoredItem.DisplayName = "Int32 Variable";
                intMonitoredItem.SamplingInterval = 1000;
                intMonitoredItem.QueueSize = 10;
                intMonitoredItem.DiscardOldest = true;
                intMonitoredItem.Notification += OnMonitoredItemNotification;

                subscription.AddItem(intMonitoredItem);

                MonitoredItem floatMonitoredItem = new MonitoredItem(subscription.DefaultItem);
                // Float Node - Objects\CTT\Scalar\Simulation\Float
                floatMonitoredItem.StartNodeId = new NodeId("ns=2;s=Scalar_Simulation_Float");
                floatMonitoredItem.AttributeId = Attributes.Value;
                floatMonitoredItem.DisplayName = "Float Variable";
                floatMonitoredItem.SamplingInterval = 1000;
                floatMonitoredItem.QueueSize = 10;
                floatMonitoredItem.Notification += OnMonitoredItemNotification;

                subscription.AddItem(floatMonitoredItem);

                MonitoredItem stringMonitoredItem = new MonitoredItem(subscription.DefaultItem);
                // String Node - Objects\CTT\Scalar\Simulation\String
                stringMonitoredItem.StartNodeId = new NodeId("ns=2;s=Scalar_Simulation_String");
                stringMonitoredItem.AttributeId = Attributes.Value;
                stringMonitoredItem.DisplayName = "String Variable";
                stringMonitoredItem.SamplingInterval = 1000;
                stringMonitoredItem.QueueSize = 10;
                stringMonitoredItem.Notification += OnMonitoredItemNotification;

                subscription.AddItem(stringMonitoredItem);

                // Create the monitored items on Server side
                subscription.ApplyChanges();
                m_output.WriteLine("MonitoredItems created for SubscriptionId = {0}.", subscription.Id);
            }
            catch (Exception ex)
            {
                m_output.WriteLine("Subscribe error: {0}", ex.Message);
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
                m_output.WriteLine("Notification: {0} \"{1}\" and Value = {2}.", notification.Message.SequenceNumber, monitoredItem.DisplayName, notification.Value);
            }
            catch (Exception ex)
            {
                m_output.WriteLine("OnMonitoredItemNotification error: {0}", ex.Message);
            }
        }
        #endregion

        private Action<IList, IList> m_validateResponse;
        private TextWriter m_output;
    }
}
