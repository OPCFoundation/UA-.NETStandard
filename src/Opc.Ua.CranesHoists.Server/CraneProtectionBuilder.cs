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
using Opc.Ua.Machinery.Server.Builders;

namespace Opc.Ua.CranesHoists.Server
{
    /// <summary>
    /// Selects protective-function monitoring; it never exposes commands to disable protection.
    /// </summary>
    public sealed class CraneProtectionBuilder
    {
        /// <summary>
        /// Creates a protection definition.
        /// </summary>
        internal CraneProtectionBuilder(CraneSystemBuilder owner, string id, ArrayOf<string> axes)
        {
            Owner = owner;
            Id = id;
            Axes = axes;
        }

        /// <summary>
        /// Gets the system-local provider identity.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the owning definition.
        /// </summary>
        internal CraneSystemBuilder Owner { get; }

        /// <summary>
        /// Gets the protected axes.
        /// </summary>
        internal ArrayOf<string> Axes { get; }

        /// <summary>
        /// Gets whether Enabled and Active monitoring is selected together.
        /// </summary>
        internal bool StateMonitoring { get; private set; }

        /// <summary>
        /// Gets the optional standard category.
        /// </summary>
        internal ProtectiveFunctionEnumeration? Mode { get; private set; }

        /// <summary>
        /// Gets the optional human-readable name.
        /// </summary>
        internal string? Name { get; private set; }

        /// <summary>
        /// Gets the optional graceful ramp-down capability.
        /// </summary>
        internal bool? RampDown { get; private set; }

        /// <summary>
        /// Gets optional directional capabilities in minus-slowdown, minus-stop, plus-slowdown, plus-stop order.
        /// </summary>
        internal ArrayOf<bool> Directions { get; private set; }

        /// <summary>
        /// Gets optional shared component identification.
        /// </summary>
        internal Action<MachineryIdentificationData>? Identification { get; private set; }

        /// <summary>
        /// Selects coherent provider-reported Enabled and Active state.
        /// </summary>
        public CraneProtectionBuilder WithStateMonitoring()
        {
            Owner.EnsureMutable();
            StateMonitoring = true;
            return this;
        }

        /// <summary>
        /// Selects descriptive mode, name and ramp-down properties.
        /// </summary>
        public CraneProtectionBuilder WithDescription(
            ProtectiveFunctionEnumeration mode,
            string name,
            bool rampDown)
        {
            Owner.EnsureMutable();
            CraneSystemBuilder.ValidateId(name);
            Mode = mode;
            Name = name;
            RampDown = rampDown;
            return this;
        }

        /// <summary>
        /// Declares direction-specific stop and slowdown capabilities independently of current Active state.
        /// </summary>
        public CraneProtectionBuilder WithDirections(
            bool minusSlowdown,
            bool minusStop,
            bool plusSlowdown,
            bool plusStop)
        {
            Owner.EnsureMutable();
            Directions = [minusSlowdown, minusStop, plusSlowdown, plusStop];
            return this;
        }

        /// <summary>
        /// Selects the optional shared component nameplate.
        /// </summary>
        public CraneProtectionBuilder WithIdentification(Action<MachineryIdentificationData> configure)
        {
            Owner.EnsureMutable();
            Identification = configure ?? throw new ArgumentNullException(nameof(configure));
            return this;
        }
    }
}
