/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Server;

namespace ReverseHelloServer
{
    /// <summary>
    /// Implements a basic Quickstart Server.
    /// </summary>
    /// <remarks>
    /// Each server instance must have one instance of a StandardServer object which is
    /// responsible for reading the configuration file, creating the endpoints and dispatching
    /// incoming requests to the appropriate handler.
    /// 
    /// This sub-class specifies non-configurable metadata such as Product Name and initializes
    /// the EmptyNodeManager which provides access to the data exposed by the Server.
    /// </remarks>
    public partial class ReverseHelloServer : StandardServer
    {
        private Dictionary<Uri, ServiceResult> m_connections = new Dictionary<Uri, ServiceResult>();

        #region Overridden Methods
        /// <summary>
        /// Creates the node managers for the server.
        /// </summary>
        /// <remarks>
        /// This method allows the sub-class create any additional node managers which it uses. The SDK
        /// always creates a CoreNodeManager which handles the built-in nodes defined by the specification.
        /// Any additional NodeManagers are expected to handle application specific nodes.
        /// </remarks>
        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            Utils.Trace("Creating the Node Managers.");

            List<INodeManager> nodeManagers = new List<INodeManager>();

            // create the custom node managers.
            nodeManagers.Add(new ReverseHelloNodeManager(server, configuration));

            // create master node manager.
            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
        }

        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);
        }

        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        /// <remarks>
        /// These properties are exposed by the server but cannot be changed by administrators.
        /// </remarks>
        protected override ServerProperties LoadServerProperties()
        {
            ServerProperties properties = new ServerProperties();

            properties.ManufacturerName = "OPC Foundation";
            properties.ProductName = "ReverseHello Server";
            properties.ProductUri = Namespaces.ReverseHelloServer + "/v1.0";
            properties.SoftwareVersion = Utils.GetAssemblySoftwareVersion();
            properties.BuildNumber = Utils.GetAssemblyBuildNumber();
            properties.BuildDate = Utils.GetAssemblyTimestamp();

            return properties;
        }
        #endregion

        public void StartMonitoringConnection(Uri url)
        {
            lock (m_connections)
            {
                m_connections.Add(url, null);
            }

            base.CreateConnection(url);
        }

        public void StopMonitoringConnection(Uri url)
        {
            lock (m_connections)
            {
                m_connections.Remove(url);
            }
        }

        protected override void OnConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
        {
            Utils.Trace((int)Utils.TraceMasks.Information, "Client Status Changed! [{0}]", e.ChannelStatus);

            if (ServiceResult.IsBad(e.ChannelStatus))
            {
                lock (m_connections)
                {
                    if (e.ChannelStatus.Code == StatusCodes.BadTcpMessageTypeInvalid)
                    {
                        Utils.Trace((int)Utils.TraceMasks.Information, "Client Rejected Connection! [{0}]", e.EndpointUrl);
                        m_connections.Remove(e.EndpointUrl);
                        return;
                    }

                    ServiceResult priorStatus = null;

                    if (m_connections.TryGetValue(e.EndpointUrl, out priorStatus))
                    {
                        if (e.Closed)
                        {
                            base.CreateConnection(e.EndpointUrl);
                        }

                        m_connections[e.EndpointUrl] = e.ChannelStatus;
                    }
                }
            }
        }
    }
}
