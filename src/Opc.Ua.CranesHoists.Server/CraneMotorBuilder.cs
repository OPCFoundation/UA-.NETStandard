// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Machinery.Server.Builders;

namespace Opc.Ua.CranesHoists.Server
{
    /// <summary>
    /// Selects a crane motor's optional monitoring and component identification.
    /// </summary>
    public sealed class CraneMotorBuilder
    {
        /// <summary>
        /// Creates a motor definition.
        /// </summary>
        internal CraneMotorBuilder(CraneSystemBuilder owner, string id)
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
        /// Gets whether overtemperature monitoring is selected.
        /// </summary>
        internal bool Overheated { get; private set; }

        /// <summary>
        /// Gets whether inherited service-brake monitoring is selected.
        /// </summary>
        internal bool ServiceBrake { get; private set; }

        /// <summary>
        /// Gets the optional shared nameplate configuration.
        /// </summary>
        internal Action<MachineryIdentificationData>? Identification { get; private set; }

        /// <summary>
        /// Selects provider-reported overtemperature state.
        /// </summary>
        public CraneMotorBuilder WithOverheated()
        {
            Owner.EnsureMutable();
            Overheated = true;
            return this;
        }

        /// <summary>
        /// Selects the inherited Robotics service-brake monitoring signal.
        /// </summary>
        public CraneMotorBuilder WithServiceBrake()
        {
            Owner.EnsureMutable();
            ServiceBrake = true;
            return this;
        }

        /// <summary>
        /// Selects the optional component nameplate, using the Machinery writer.
        /// </summary>
        public CraneMotorBuilder WithIdentification(Action<MachineryIdentificationData> configure)
        {
            Owner.EnsureMutable();
            Identification = configure ?? throw new ArgumentNullException(nameof(configure));
            return this;
        }
    }
}
