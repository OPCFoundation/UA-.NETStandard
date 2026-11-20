/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.PubSub.Adapter.Diagnostics;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.DataSets;

namespace Opc.Ua.PubSub.Adapter.Subscriber
{
    /// <summary>
    /// Convenience factory that builds a subscriber-side
    /// <see cref="ISubscribedDataSetSink"/> which materialises received DataSet
    /// fields onto an external OPC UA server. It wires a
    /// <see cref="TargetVariablesSink"/> over an
    /// <see cref="ServerTargetVariableWriter"/> so the wiring stage only
    /// needs the TargetVariables configuration and a connected session.
    /// </summary>
    public static class ServerSubscribedDataSetSink
    {
        /// <summary>
        /// Creates a <see cref="TargetVariablesSink"/> that writes the configured
        /// target variables to the supplied external-server session.
        /// </summary>
        /// <param name="configuration">
        /// The TargetVariables configuration holding the per-field
        /// <see cref="FieldTargetDataType"/> entries.
        /// </param>
        /// <param name="session">
        /// The external-server session used to apply the writes.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used to create the writer's logger.
        /// </param>
        /// <param name="metrics">
        /// Optional metrics sink that records write activity.
        /// </param>
        /// <returns>
        /// A subscribed dataset sink backed by the external server.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="configuration"/>, <paramref name="session"/>
        /// or <paramref name="telemetry"/> is <see langword="null"/>.
        /// </exception>
        public static ISubscribedDataSetSink Create(
            TargetVariablesDataType configuration,
            IServerSession session,
            ITelemetryContext telemetry,
            AdapterMetrics? metrics = null)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            var writer = new ServerTargetVariableWriter(session, telemetry, metrics);
            return new TargetVariablesSink(configuration, writer);
        }
    }
}
