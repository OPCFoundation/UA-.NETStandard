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
using System.Linq;
using System.Reflection;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.Servers
{
    /// <summary>
    /// Helpers to find node managers implemented in this library.
    /// </summary>

    public static class Utils
    {
        /// <summary>
        /// Applies custom settings to quickstart servers for CTT run.
        /// </summary>
        /// <param name="server"></param>
        public static void ApplyCTTMode(StandardServer server)
        {
            var methodsToCall = new CallMethodRequestCollection();
            var index = server.CurrentInstance.NamespaceUris.GetIndex(Alarms.Namespaces.Alarms);
            if (index > 0)
            {
                try
                {
                    methodsToCall.Add(
                        // Start the Alarms with infinite runtime
                        new CallMethodRequest {
                        MethodId = new NodeId("Alarms.Start", (ushort)index),
                        ObjectId = new NodeId("Alarms", (ushort)index),
                        InputArguments = new VariantCollection() { new Variant((UInt32)UInt32.MaxValue)}
                    });
                    var requestHeader = new RequestHeader() {
                        Timestamp = DateTime.UtcNow,
                        TimeoutHint = 10000
                    };
                    var context = new OperationContext(requestHeader, RequestType.Call);
                    server.CurrentInstance.NodeManager.Call(context, methodsToCall, out CallMethodResultCollection results, out DiagnosticInfoCollection diagnosticInfos);
                    foreach (var result in results)
                    {
                        if (ServiceResult.IsBad(result.StatusCode))
                        {
                            Opc.Ua.Utils.LogError("Error calling method {0}.", result.StatusCode);
                        }
                    }
                    return;
                }
                catch (Exception ex)
                {
                    Opc.Ua.Utils.LogError(ex, "Failed to start alarms for CTT.");
                }
            }
            Opc.Ua.Utils.LogWarning("The alarms could not be enabled for CTT, the namespace does not exits.");
        }

        /// <summary>
        /// Add all available node manager factories to the server.
        /// </summary>
        public static void AddDefaultNodeManagers(StandardServer server)
        {
            foreach (var nodeManagerFactory in NodeManagerFactories)
            {
                server.AddNodeManager(nodeManagerFactory);
            }
        }

        /// <summary>
        /// The property with available node manager factories.
        /// </summary>
        public static ReadOnlyList<INodeManagerFactory> NodeManagerFactories
        {
            get
            {
                if (m_nodeManagerFactories == null)
                {
                    m_nodeManagerFactories = GetNodeManagerFactories();
                }
                return new ReadOnlyList<INodeManagerFactory>(m_nodeManagerFactories);
            }
        }

        /// <summary>
        /// Helper to determine the INodeManagerFactory by reflection.
        /// </summary>
        private static INodeManagerFactory IsINodeManagerFactoryType(Type type)
        {
            var nodeManagerTypeInfo = type.GetTypeInfo();
            if (nodeManagerTypeInfo.IsAbstract ||
                !typeof(INodeManagerFactory).IsAssignableFrom(type))
            {
                return null;
            }
            return Activator.CreateInstance(type) as INodeManagerFactory;
        }

        /// <summary>
        /// Enumerates all node manager factories.
        /// </summary>
        /// <returns></returns>
        private static IList<INodeManagerFactory> GetNodeManagerFactories()
        {
            var assembly = typeof(Utils).Assembly;
            var nodeManagerFactories = assembly.GetExportedTypes().Select(type => IsINodeManagerFactoryType(type)).Where(type => type != null);
            return nodeManagerFactories.ToList();
        }

        private static IList<INodeManagerFactory> m_nodeManagerFactories;
    }
}
