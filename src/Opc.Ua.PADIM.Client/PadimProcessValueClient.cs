/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
