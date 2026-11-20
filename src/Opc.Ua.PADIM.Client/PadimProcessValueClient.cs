/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Client;

namespace Opc.Ua.PADIM.Client
{
    /// <summary>
    /// Accesses a process variable's optional configuration through the common typed-member client.
    /// </summary>
    public sealed class PadimProcessValueClient
    {
        /// <summary>
        /// Creates member views borrowing the supplied session.
        /// </summary>
        public PadimProcessValueClient(ISession session, NodeId nodeId, ITelemetryContext telemetry)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (nodeId.IsNull)
            {
                throw new ArgumentException("A variable NodeId is required.", nameof(nodeId));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            var owner = new ValueOwner(session, nodeId, telemetry);
            ActualValue = new ClientVariable<Variant>(owner, Namespaces.PADIM, BrowseNames.ActualValue,
                false, static (value, _) => value, static value => value);
            SimulationValue = new ClientVariable<Variant>(owner, Namespaces.PADIM, BrowseNames.SimulationValue,
                true, static (value, _) => value, static value => value);
            SimulationState = new ClientVariable<bool>(owner, Namespaces.PADIM, BrowseNames.SimulationState,
                true, static (value, _) => value.TryGetValue(out bool result)
                    ? result : throw new ServiceResultException(StatusCodes.BadTypeMismatch),
                static value => Variant.From(value));
            Damping = new ClientVariable<float>(owner, Namespaces.PADIM, BrowseNames.Damping,
                true, static (value, _) => value.TryGetValue(out float result)
                    ? result : throw new ServiceResultException(StatusCodes.BadTypeMismatch),
                static value => Variant.From(value));
        }

        /// <summary>
        /// Gets the original acquired value with its authoritative quality and timestamp.
        /// </summary>
        public ClientVariable<Variant> ActualValue { get; }

        /// <summary>
        /// Gets the optional simulation value; ResolveAsync reports absence without enabling it.
        /// </summary>
        public ClientVariable<Variant> SimulationValue { get; }

        /// <summary>
        /// Gets the optional simulation switch.
        /// </summary>
        public ClientVariable<bool> SimulationState { get; }

        /// <summary>
        /// Gets the optional damping time constant in seconds.
        /// </summary>
        public ClientVariable<float> Damping { get; }

        /// <summary>
        /// Supplies the existing member resolver with the parent variable identifier.
        /// </summary>
        private sealed class ValueOwner : ObjectTypeClient
        {
            /// <summary>
            /// Creates the common resolver owner without introducing another browse implementation.
            /// </summary>
            public ValueOwner(ISession session, NodeId nodeId, ITelemetryContext telemetry)
                : base(session, nodeId, telemetry)
            {
            }
        }
    }
}
