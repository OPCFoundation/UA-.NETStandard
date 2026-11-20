// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using Opc.Ua.Machinery.Server.Builders;

namespace Opc.Ua.CranesHoists.Server
{
    /// <summary>
    /// Selects a physical crane device and its optional monitoring facets.
    /// </summary>
    public sealed class CraneDeviceBuilder
    {
        /// <summary>
        /// Creates a device definition owned by its system builder.
        /// </summary>
        internal CraneDeviceBuilder(CraneSystemBuilder owner, string id, CraneMotionDeviceCategoryEnum category)
        {
            Owner = owner;
            Id = id;
            Category = category;
        }

        /// <summary>
        /// Gets the system-local provider identity.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the selected standard crane category.
        /// </summary>
        public CraneMotionDeviceCategoryEnum Category { get; }

        /// <summary>
        /// Gets the containing definition.
        /// </summary>
        internal CraneSystemBuilder Owner { get; }

        /// <summary>
        /// Gets the configured Machinery nameplate writer.
        /// </summary>
        internal Action<MachineryIdentificationData>? Identification { get; private set; }

        /// <summary>
        /// Gets whether working-period telemetry is selected.
        /// </summary>
        internal bool WorkingPeriod { get; private set; }

        /// <summary>
        /// Gets whether the optional Components add-in is selected.
        /// </summary>
        internal bool Components { get; private set; }

        /// <summary>
        /// Gets the axes staged for this physical device.
        /// </summary>
        internal List<CraneAxisBuilder> Axes { get; } = [];

        /// <summary>
        /// Gets direct physical children, identified independently of their Robotics owning folder.
        /// </summary>
        internal List<string> PhysicalChildren { get; } = [];

        /// <summary>
        /// Configures the mandatory existing Machinery nameplate.
        /// </summary>
        public CraneDeviceBuilder WithIdentification(Action<MachineryIdentificationData> configure)
        {
            Owner.EnsureMutable();
            Identification = configure ?? throw new ArgumentNullException(nameof(configure));
            return this;
        }

        /// <summary>
        /// Selects provider-reported designed working period.
        /// </summary>
        public CraneDeviceBuilder WithWorkingPeriod()
        {
            Owner.EnsureMutable();
            WorkingPeriod = true;
            return this;
        }

        /// <summary>
        /// Selects a Components discovery add-in referencing existing physical children.
        /// </summary>
        public CraneDeviceBuilder WithComponents()
        {
            Owner.EnsureMutable();
            Components = true;
            return this;
        }

        /// <summary>
        /// References a direct physical subcomponent without moving it out of MotionDevices.
        /// </summary>
        public CraneDeviceBuilder WithPhysicalComponent(string componentId)
        {
            Owner.EnsureMutable();
            CraneSystemBuilder.ValidateId(componentId);
            if (PhysicalChildren.Contains(componentId))
            {
                throw new ArgumentException("The physical component is already selected.", nameof(componentId));
            }
            PhysicalChildren.Add(componentId);
            return this;
        }

        /// <summary>
        /// Adds an axis; its motors remain in the standard device PowerTrains folder.
        /// </summary>
        public CraneDeviceBuilder AddAxis(string id, Action<CraneAxisBuilder> configure)
        {
            Owner.EnsureMutable();
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            Owner.ReserveId(id);
            var axis = new CraneAxisBuilder(Owner, id);
            configure(axis);
            Axes.Add(axis);
            return this;
        }
    }
}
