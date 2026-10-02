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

namespace Opc.Ua.Pumps.Server.Builders
{
    /// <summary>
    /// Builds one OPC 40223 pump: its nameplate and whichever of the seven
    /// <c>PumpType</c> groups the pump publishes.
    /// </summary>
    /// <remarks>
    /// Every group is optional and none is materialised until it is asked
    /// for, so a pump ends up publishing exactly what the application
    /// configured and nothing else. That matters: a server that materialises
    /// the full <c>PumpType</c> surface publishes some 3000 nodes per pump,
    /// almost all of them empty, and every one of them costs a client a browse
    /// and a read to discover it has no value.
    /// </remarks>
    public interface IPumpBuilder
    {
        /// <summary>
        /// Gets the pump being built.
        /// </summary>
        PumpState Pump { get; }

        /// <summary>
        /// Gets the pump's NodeId.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// Gets a builder for the mandatory <c>Identification</c> nameplate.
        /// </summary>
        IPumpGroupBuilder Identification { get; }

        /// <summary>
        /// Gets a builder for the <c>Configuration</c> group, materialising
        /// it on first use.
        /// </summary>
        IPumpGroupBuilder Configuration { get; }

        /// <summary>
        /// Gets a builder for the <c>Operational</c> group, materialising it
        /// on first use.
        /// </summary>
        IPumpGroupBuilder Operational { get; }

        /// <summary>
        /// Gets a builder for the <c>Events</c> supervision group,
        /// materialising it on first use.
        /// </summary>
        IPumpGroupBuilder Events { get; }

        /// <summary>
        /// Gets a builder for the <c>Maintenance</c> group, materialising it
        /// on first use.
        /// </summary>
        IPumpGroupBuilder Maintenance { get; }

        /// <summary>
        /// Gets a builder for the <c>Documentation</c> group, materialising
        /// it on first use.
        /// </summary>
        IPumpGroupBuilder Documentation { get; }

        /// <summary>
        /// Gets a builder for <c>Configuration/Design</c>.
        /// </summary>
        IPumpGroupBuilder Design { get; }

        /// <summary>
        /// Gets a builder for <c>Configuration/Implementation</c>.
        /// </summary>
        IPumpGroupBuilder Implementation { get; }

        /// <summary>
        /// Gets a builder for <c>Configuration/SystemRequirements</c>.
        /// </summary>
        IPumpGroupBuilder SystemRequirements { get; }

        /// <summary>
        /// Gets a builder for <c>Operational/Measurements</c>.
        /// </summary>
        IPumpGroupBuilder Measurements { get; }

        /// <summary>
        /// Gets a builder for <c>Operational/Signals</c>.
        /// </summary>
        IPumpGroupBuilder Signals { get; }

        /// <summary>
        /// Gets a builder for <c>Operational/Control</c>.
        /// </summary>
        IPumpGroupBuilder Control { get; }

        /// <summary>
        /// Gets a builder for <c>Operational/PumpActuation</c>.
        /// </summary>
        IPumpGroupBuilder PumpActuation { get; }

        /// <summary>
        /// Gets a builder for <c>Operational/MultiPump</c>.
        /// </summary>
        IPumpGroupBuilder MultiPump { get; }

        /// <summary>
        /// Gets a builder for one of the seven <c>Events</c> supervision
        /// categories.
        /// </summary>
        /// <param name="browseName">
        /// One of the <c>Supervision…</c> <see cref="BrowseNames"/> constants.
        /// </param>
        IPumpGroupBuilder Supervision(string browseName);

        /// <summary>
        /// Gets a builder for one of the four <c>Maintenance</c> categories.
        /// </summary>
        /// <param name="browseName">
        /// One of the <c>…Maintenance</c> <see cref="BrowseNames"/> constants.
        /// </param>
        IPumpGroupBuilder MaintenanceCategory(string browseName);

        /// <summary>
        /// Fills the nameplate from a <see cref="PumpNameplate"/>, publishing
        /// every field the record carries and leaving the rest unmaterialised.
        /// </summary>
        /// <param name="nameplate">The nameplate data.</param>
        IPumpBuilder WithNameplate(PumpNameplate nameplate);

        /// <summary>
        /// Materialises the pump's <c>Ports</c> group and adds one port to it.
        /// </summary>
        /// <param name="kind">Which of the three OPC 40223 port types to add.</param>
        /// <param name="name">
        /// The port's browse name. <c>PortsGroupType</c> declares its ports as
        /// placeholders, so the application names each one.
        /// </param>
        /// <returns>A builder for the new port.</returns>
        IPumpGroupBuilder AddPort(PumpPortKind kind, string name);

        /// <summary>
        /// Hands the pump to <paramref name="configure"/> for anything this
        /// interface does not cover.
        /// </summary>
        /// <param name="configure">Mutates the pump.</param>
        IPumpBuilder With(Action<PumpState> configure);
    }
}
