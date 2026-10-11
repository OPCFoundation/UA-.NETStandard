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
using Opc.Ua.Server;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Fluent access to every declared member of a released Vision node.
    /// Names are resolved against the model, never materialized as untyped vendor properties.
    /// </summary>
    public sealed class MachineVisionNodeBuilder
    {
        /// <summary>
        /// Creates a declared-member builder using its owning manager and telemetry clock.
        /// </summary>
        internal MachineVisionNodeBuilder(AsyncCustomNodeManager manager, NodeState state, TimeProvider clock)
        {
            m_manager = manager;
            m_clock = clock;
            State = state;
        }

        /// <summary>
        /// Gets the generated node state for advanced typed configuration.
        /// </summary>
        public NodeState State { get; }

        /// <summary>
        /// Selects or creates a declared child. Supports the entire optional model surface.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public MachineVisionNodeBuilder Child(string browseName)
        {
            if (string.IsNullOrWhiteSpace(browseName))
            {
                throw new ArgumentException("A declared browse name is required.", nameof(browseName));
            }
            return new MachineVisionNodeBuilder(
                m_manager, VisionMachineryNodes.Require<BaseInstanceState>(m_manager, State, browseName), m_clock);
        }

        /// <summary>
        /// Sets a declared child variable and its data-change status and timestamp.
        /// </summary>
        public MachineVisionNodeBuilder Set(string browseName, Variant value)
        {
            BaseVariableState variable = VisionMachineryNodes.Require<BaseVariableState>(m_manager, State, browseName);
            MachineVisionNodes.Set(m_manager.SystemContext, variable, value, m_clock.GetUtcNow().UtcDateTime);
            return this;
        }

        /// <summary>
        /// Sets the selected variable. Use Child to reach variable metadata such as EngineeringUnits.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public MachineVisionNodeBuilder Value(Variant value)
        {
            if (State is not BaseVariableState variable)
            {
                throw new InvalidOperationException("The selected node is not a variable.");
            }
            MachineVisionNodes.Set(m_manager.SystemContext, variable, value, m_clock.GetUtcNow().UtcDateTime);
            return this;
        }

        /// <summary>
        /// Populates the common Machinery nameplate without introducing a second identity model.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="identification"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public MachineVisionNodeBuilder WithIdentification(MachineryIdentificationData identification)
        {
            if (identification == null)
            {
                throw new ArgumentNullException(nameof(identification));
            }
            if (identification.Manufacturer.IsNull || string.IsNullOrWhiteSpace(identification.SerialNumber))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "Manufacturer and SerialNumber are required.");
            }
            MachineVisionNodeBuilder nameplate = Child("Identification")
                .Set("Manufacturer", Variant.From(identification.Manufacturer))
                .Set("SerialNumber", Variant.From(identification.SerialNumber));
            if (identification.ProductInstanceUri != null)
            {
                nameplate.Set("ProductInstanceUri", Variant.From(identification.ProductInstanceUri));
            }
            if (identification.ManufacturerUri != null)
            {
                nameplate.Set("ManufacturerUri", Variant.From(identification.ManufacturerUri));
            }
            if (!identification.Model.IsNull)
            {
                nameplate.Set("Model", Variant.From(identification.Model));
            }
            if (identification.ProductCode != null)
            {
                nameplate.Set("ProductCode", Variant.From(identification.ProductCode));
            }
            if (identification.HardwareRevision != null)
            {
                nameplate.Set("HardwareRevision", Variant.From(identification.HardwareRevision));
            }
            if (identification.SoftwareRevision != null)
            {
                nameplate.Set("SoftwareRevision", Variant.From(identification.SoftwareRevision));
            }
            if (identification.AssetId != null)
            {
                nameplate.Set("AssetId", Variant.From(identification.AssetId));
            }
            if (!identification.ComponentName.IsNull)
            {
                nameplate.Set("ComponentName", Variant.From(identification.ComponentName));
            }
            if (identification.Location != null)
            {
                nameplate.Set("Location", Variant.From(identification.Location));
            }
            if (identification.DeviceRevision != null)
            {
                nameplate.Set("DeviceRevision", Variant.From(identification.DeviceRevision));
            }
            if (identification.DeviceClass != null)
            {
                nameplate.Set("DeviceClass", Variant.From(identification.DeviceClass));
            }
            if (identification.YearOfConstruction is ushort year)
            {
                nameplate.Set("YearOfConstruction", Variant.From(year));
            }
            if (identification.MonthOfConstruction is byte month)
            {
                if (month is < 1 or > 12)
                {
                    throw new ArgumentOutOfRangeException(nameof(identification), "MonthOfConstruction must be 1-12.");
                }
                nameplate.Set("MonthOfConstruction", Variant.From(month));
            }
            if (identification.InitialOperationDate is DateTime date)
            {
                DateTimeUtc timestamp = date;
                nameplate.Set("InitialOperationDate", Variant.From(timestamp));
            }
            return this;
        }

        /// <summary>
        /// Configures the optional maintenance information add-in.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <c>null</c>.</exception>
        public MachineVisionNodeBuilder WithMaintenance(Action<MachineVisionNodeBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            configure(Child("Maintenance"));
            return this;
        }

        /// <summary>
        /// Configures the optional health information add-in.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <c>null</c>.</exception>
        public MachineVisionNodeBuilder WithHealth(Action<MachineVisionNodeBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            configure(Child("Health"));
            return this;
        }

        private readonly AsyncCustomNodeManager m_manager;
        private readonly TimeProvider m_clock;
    }
}
