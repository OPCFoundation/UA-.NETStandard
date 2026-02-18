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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
        public static async Task ApplyCTTModeAsync(TextWriter output, IStandardServer server)
        {
            var methodsToCall = new CallMethodRequestCollection();
            int index = server.CurrentInstance.NamespaceUris.GetIndex(Alarms.Namespaces.Alarms);
            if (index > 0)
            {
                try
                {
                    methodsToCall.Add(
                        // Start the Alarms with infinite runtime
                        new CallMethodRequest
                        {
                            MethodId = new NodeId("Alarms.Start", (ushort)index),
                            ObjectId = new NodeId("Alarms", (ushort)index),
                            InputArguments = [new Variant(uint.MaxValue)]
                        });
                    var requestHeader = new RequestHeader
                    {
                        Timestamp = DateTime.UtcNow,
                        TimeoutHint = 10000
                    };
                    var context = new OperationContext(requestHeader, null, RequestType.Call);
                    (CallMethodResultCollection results, DiagnosticInfoCollection diagnosticInfos) = await server.CurrentInstance.NodeManager.CallAsync(
                        context,
                        methodsToCall)
                        .ConfigureAwait(false);
                    foreach (CallMethodResult result in results)
                    {
                        if (ServiceResult.IsBad(result.StatusCode))
                        {
                            ILogger<StandardServer> logger = server.CurrentInstance.Telemetry.CreateLogger<StandardServer>();
                            logger.LogError("Error calling method with status code {StatusCode}.", result.StatusCode);
                        }
                    }
                    output.WriteLine("The Alarms for CTT mode are active.");
                    return;
                }
                catch (Exception ex)
                {
                    ILogger<StandardServer> logger = server.CurrentInstance.Telemetry.CreateLogger<StandardServer>();
                    logger.LogError(ex, "Failed to start alarms for CTT.");
                }
            }
            output.WriteLine(
                "The alarms could not be enabled for CTT, the namespace does not exist.");
        }

        /// <summary>
        /// Add all available node manager factories to the server.
        /// </summary>
        public static void AddDefaultNodeManagers(IStandardServer server)
        {
            foreach (INodeManagerFactory nodeManagerFactory in NodeManagerFactories)
            {
                server.AddNodeManager(nodeManagerFactory);
            }
        }

        /// <summary>
        /// Add all available node manager factories to the server.
        /// </summary>
        public static void UseSamplingGroupsInReferenceNodeManager(
            ReferenceServer.ReferenceServer server)
        {
            server.UseSamplingGroupsInReferenceNodeManager = true;
        }

        /// <summary>
        /// Enable provisioning mode in the ReferenceServer.
        /// </summary>
        public static void EnableProvisioningMode(
            ReferenceServer.ReferenceServer server)
        {
            server.ProvisioningMode = true;
        }

        /// <summary>
        /// The property with available node manager factories.
        /// </summary>
        public static ArrayOf<INodeManagerFactory> NodeManagerFactories
        {
            get
            {
                s_nodeManagerFactories ??= GetNodeManagerFactories();
                return s_nodeManagerFactories.ToArrayOf();
            }
        }

        /// <summary>
        /// Helper to determine the INodeManagerFactory by reflection.
        /// </summary>
        private static INodeManagerFactory IsINodeManagerFactoryType(Type type)
        {
            System.Reflection.TypeInfo nodeManagerTypeInfo = type.GetTypeInfo();
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
        private static List<INodeManagerFactory> GetNodeManagerFactories()
        {
            Assembly assembly = typeof(Utils).Assembly;
            IEnumerable<INodeManagerFactory> nodeManagerFactories = assembly
                .GetExportedTypes()
                .Select(IsINodeManagerFactoryType)
                .Where(type => type != null);
            return [.. nodeManagerFactories];
        }

        private static IList<INodeManagerFactory> s_nodeManagerFactories;
    }
}
