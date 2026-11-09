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
using System.Collections.Generic;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Robotics;

namespace Opc.Ua.CranesHoists.Server
{
    /// <summary>
    /// Configures a real CraneAxisType, its power train, optional load and backup brakes.
    /// </summary>
    public sealed class CraneAxisBuilder
    {
        /// <summary>
        /// Creates an axis definition.
        /// </summary>
        internal CraneAxisBuilder(CraneSystemBuilder owner, string id)
        {
            Owner = owner;
            Id = id;
        }

        /// <summary>
        /// Gets the system-local provider identity.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the owning system definition.
        /// </summary>
        internal CraneSystemBuilder Owner { get; }

        /// <summary>
        /// Gets whether the hardware provides actual position.
        /// </summary>
        internal bool PositionAvailable { get; private set; }

        /// <summary>
        /// Gets the inherited motion profile.
        /// </summary>
        internal AxisMotionProfileEnumeration MotionProfile { get; private set; }

        /// <summary>
        /// Gets whether axis-function telemetry is selected.
        /// </summary>
        internal bool AxisFunctions { get; private set; }

        /// <summary>
        /// Gets whether moving-state telemetry is selected.
        /// </summary>
        internal bool Moving { get; private set; }

        /// <summary>
        /// Gets whether standard limit requests and active-limit telemetry are selected.
        /// </summary>
        internal bool SpeedLimits { get; private set; }

        /// <summary>
        /// Gets the optional nominal speed.
        /// </summary>
        internal double? RatedSpeed { get; private set; }

        /// <summary>
        /// Gets configured speed engineering units.
        /// </summary>
        internal EUInformation? SpeedUnits { get; private set; }

        /// <summary>
        /// Gets whether actual-load monitoring is selected.
        /// </summary>
        internal bool ActualLoad { get; private set; }

        /// <summary>
        /// Gets optional advanced configuration of the generated Robotics load.
        /// </summary>
        internal Action<LoadState, ISystemContext>? ConfigureLoad { get; private set; }

        /// <summary>
        /// Gets the motor definitions for this axis's power train.
        /// </summary>
        internal List<CraneMotorBuilder> Motors { get; } = [];

        /// <summary>
        /// Gets the explicit backup brake definitions.
        /// </summary>
        internal List<CraneBrakeDefinition> Brakes { get; } = [];

        /// <summary>
        /// Declares real position capability; an unavailable position is published with BadNoData.
        /// </summary>
        public CraneAxisBuilder WithPositionCapability(
            bool available,
            AxisMotionProfileEnumeration profile = default)
        {
            Owner.EnsureMutable();
            PositionAvailable = available;
            MotionProfile = profile;
            return this;
        }

        /// <summary>
        /// Selects the standard discrete axis-function telemetry and all nine standard EnumValues.
        /// </summary>
        public CraneAxisBuilder WithAxisFunctions()
        {
            Owner.EnsureMutable();
            AxisFunctions = true;
            return this;
        }

        /// <summary>
        /// Selects provider-reported moving state.
        /// </summary>
        public CraneAxisBuilder WithMovingState()
        {
            Owner.EnsureMutable();
            Moving = true;
            return this;
        }

        /// <summary>
        /// Selects the standard directional requests and provider-reported active limits.
        /// </summary>
        public CraneAxisBuilder WithSpeedLimits()
        {
            Owner.EnsureMutable();
            SpeedLimits = true;
            return this;
        }

        /// <summary>
        /// Declares the nominal axis speed and its engineering units.
        /// </summary>
        public CraneAxisBuilder WithRatedSpeed(double value, EUInformation units)
        {
            Owner.EnsureMutable();
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            if (units == null)
            {
                throw new ArgumentNullException(nameof(units));
            }
            RatedSpeed = value;
            SpeedUnits = units;
            return this;
        }

        /// <summary>
        /// Selects actual mass telemetry and permits native load inertia/center-of-mass configuration.
        /// </summary>
        public CraneAxisBuilder WithActualLoad(Action<LoadState, ISystemContext>? configure = null)
        {
            Owner.EnsureMutable();
            ActualLoad = true;
            ConfigureLoad = configure;
            return this;
        }

        /// <summary>
        /// Adds a native crane motor to this axis's standard power train.
        /// </summary>
        public CraneAxisBuilder AddMotor(string id, Action<CraneMotorBuilder>? configure = null)
        {
            Owner.EnsureMutable();
            Owner.ReserveId(id);
            var motor = new CraneMotorBuilder(Owner, id);
            configure?.Invoke(motor);
            Motors.Add(motor);
            return this;
        }

        /// <summary>
        /// Adds a native backup brake with mandatory reported BrakeReleased state.
        /// </summary>
        public CraneAxisBuilder AddBackupBrake(string id, Action<MachineryIdentificationData>? identification = null)
        {
            Owner.EnsureMutable();
            Owner.ReserveId(id);
            Brakes.Add(new CraneBrakeDefinition(id, identification));
            return this;
        }
    }

    /// <summary>
    /// Stages a backup brake's identity without introducing an actuation interface.
    /// </summary>
    /// <param name="Id">The system-local provider identity.</param>
    /// <param name="Identification">The optional shared Machinery nameplate configuration.</param>
    internal sealed record CraneBrakeDefinition(string Id, Action<MachineryIdentificationData>? Identification);
}
