// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Server;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// The instance-scoped GMS facade. The shared MachineTool runtime owns its actual Machinery registration.
    /// </summary>
    public sealed partial class GmsHandle : IAsyncDisposable, IConformanceContributor
    {
        /// <summary>
        /// Retains the successfully registered root and its real result publisher.
        /// </summary>
        internal GmsHandle(
            MachineToolRuntime<GMSState> machine, GmsResultPublisher results, GmsSystemBinding binding,
            GmsResultBindingOptions resultOptions)
        {
            MachineTool = machine;
            Results = results;
            m_binding = binding;
            m_resultOptions = resultOptions;
        }

        /// <summary>
        /// Gets the actual generated GMS state, not a copied MachineTool root.
        /// </summary>
        public GMSState State => MachineTool.State;

        /// <summary>
        /// Gets this instance's node identifier.
        /// </summary>
        public NodeId NodeId => State.NodeId;

        /// <summary>
        /// Gets the actual shared Machinery handle returned by its one registration.
        /// </summary>
        public IMachineHandle<GMSState> Machinery => MachineTool.Machinery;

        /// <summary>
        /// Gets the existing instance-bound GMS result publisher.
        /// </summary>
        public GmsResultPublisher Results { get; }

        /// <summary>
        /// Gets the last coherently accepted provider observation.
        /// </summary>
        public GmsSystemSnapshot? Observation => m_binding.Observation;

        /// <summary>
        /// Gets bounded accepted calibration observations; this is not a new OPC UA calibration service.
        /// </summary>
        public ArrayOf<GmsCalibrationObservation> CalibrationHistory => m_binding.CalibrationHistory;

        /// <summary>
        /// Gets current native CUs, dependency-qualified profiles and precise upstream exceptions for this instance.
        /// </summary>
        public GmsSystemCapabilities Capabilities => m_binding.Capabilities(MachineTool, m_resultOptions);

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits => Capabilities.ConformanceUnits;

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles => Capabilities.ServerProfiles;

        /// <summary>
        /// Gets the inherited observation runtime composed by the native facade.
        /// </summary>
        internal MachineToolRuntime<GMSState> MachineTool { get; }

        /// <summary>
        /// Resolves a committed Sensor or MultiSensor by its per-instance slot key.
        /// </summary>
        public bool TryGetSensor(string key, [NotNullWhen(true)] out BaseToolState? sensor)
        {
            return m_binding.TryGetSensor(key, out sensor);
        }

        /// <summary>
        /// Resolves a native condition for standard acknowledgement, enable/disable and comment workflows.
        /// </summary>
        public bool TryGetSensorWarning(string key, [NotNullWhen(true)] out SensorWarningAlarmState? warning)
        {
            return m_binding.TryGetSensorWarning(key, out warning);
        }

        /// <summary>
        /// Acquires and publishes equipment observations without commanding a measurement.
        /// </summary>
        public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            return m_binding.RefreshAsync(MachineTool, cancellationToken);
        }

        /// <summary>
        /// Retries accepted value and native event publication without reacquiring or commanding the equipment.
        /// </summary>
        public ValueTask RetryPublicationAsync(CancellationToken cancellationToken = default)
        {
            return m_binding.RefreshAsync(MachineTool, cancellationToken, retry: true);
        }

        /// <summary>
        /// Refreshes the explicitly selected inherited operator-notification provider through its existing runtime.
        /// </summary>
        public ValueTask RefreshNotificationsAsync(CancellationToken cancellationToken = default)
        {
            m_binding.EnsureNotInCallback();
            return MachineTool.RefreshNotificationsAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_binding.EnsureNotInCallback();
            var failures = new List<Exception>();
            try
            {
                await m_binding.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            try
            {
                await MachineTool.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            if (failures.Count != 0)
            {
                throw new AggregateException("GMS instance cleanup failed.", failures);
            }
        }

        /// <summary>The native observation binding owned by the containing root.</summary>
        private readonly GmsSystemBinding m_binding;

        /// <summary>The result capabilities actually bound on this root.</summary>
        private readonly GmsResultBindingOptions m_resultOptions;
    }
}
