// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
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
        /// The alarm simulation period in CTT mode: a full alarm cycle takes 40 periods (20 s), which the
        /// CTT's "Alarm Cycle Time" setting must match.
        /// </summary>
        public const int CttAlarmSimulationInterval = 500;

        /// <summary>
        /// Configures the node manager factories for a CTT run. Call before the node managers are created.
        /// </summary>
        public static void ConfigureCttModeNodeManagers()
        {
            foreach (IAsyncNodeManagerFactory factory in AsyncNodeManagerFactories)
            {
                if (factory is Alarms.AlarmNodeManagerFactory alarmFactory)
                {
                    alarmFactory.SimulationInterval = CttAlarmSimulationInterval;
                }
            }
        }

        /// <summary>
        /// Applies custom settings to quickstart servers for CTT run.
        /// </summary>
        public static async Task ApplyCTTModeAsync(TextWriter output, IStandardServer server)
        {
            var methodsToCall = new List<CallMethodRequest>();
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
                    using var context = new OperationContext(requestHeader, null, RequestType.Call, RequestLifetime.None);
                    (ArrayOf<CallMethodResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos) = await server.CurrentInstance.NodeManager.CallAsync(
                        context,
                        methodsToCall)
                        .ConfigureAwait(false);
                    foreach (CallMethodResult result in results)
                    {
                        if (ServiceResult.IsBad(result.StatusCode))
                        {
                            ILogger<StandardServer> logger =
                                server.CurrentInstance.Telemetry.CreateLogger<StandardServer>();
                            logger.ErrorCallingMethod(result.StatusCode);
                        }
                    }
                    output.WriteLine("The Alarms for CTT mode are active.");
                    return;
                }
                catch (Exception ex)
                {
                    ILogger<StandardServer> logger = server.CurrentInstance.Telemetry.CreateLogger<StandardServer>();
                    logger.FailedToStartAlarmsForCtt(ex);
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
            foreach (IAsyncNodeManagerFactory asyncFactory in AsyncNodeManagerFactories)
            {
                server.AddNodeManager(asyncFactory);
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
        /// The property with available async node manager factories.
        /// </summary>
        public static ArrayOf<IAsyncNodeManagerFactory> AsyncNodeManagerFactories
        {
            get
            {
                s_asyncNodeManagerFactories ??= GetAsyncNodeManagerFactories();
                return s_asyncNodeManagerFactories.ToArrayOf();
            }
        }

        /// <summary>
        /// Enumerates all async node manager factories.
        /// </summary>
        /// <remarks>
        /// The order fixes the namespace indexes: MemoryBuffer first, then Boiler,
        /// the same order the samples had while MemoryBuffer was a synchronous
        /// factory whose namespaces were reserved ahead of the async ones.
        /// </remarks>
        private static List<IAsyncNodeManagerFactory> GetAsyncNodeManagerFactories()
        {
            List<IAsyncNodeManagerFactory> factories = [];

            factories.Add(new MemoryBuffer.MemoryBufferNodeManagerFactory());
            factories.Add(new Boiler.BoilerNodeManagerFactory());
            factories.Add(new TestData.TestDataNodeManagerFactory());
            factories.Add(new Alarms.AlarmNodeManagerFactory());

            return factories;
        }

        private static IList<IAsyncNodeManagerFactory>? s_asyncNodeManagerFactories;
    }

    internal static partial class UtilsLog
    {
        [LoggerMessage(
            EventId = QuickstartsServersEventIds.Utils + 0, Level = LogLevel.Error,
            Message = "Error calling method with status code {StatusCode}.")]
        public static partial void ErrorCallingMethod(this ILogger logger, StatusCode statusCode);

        [LoggerMessage(
            EventId = QuickstartsServersEventIds.Utils + 1, Level = LogLevel.Error,
            Message = "Failed to start alarms for CTT.")]
        public static partial void FailedToStartAlarmsForCtt(this ILogger logger, Exception exception);
    }

}
