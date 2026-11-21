// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace FlatTagServer
{
    /// <summary>
    /// Configures one deterministic flat-tag source server.
    /// </summary>
    public sealed class FlatTagServerOptions
    {
        /// <summary>
        /// Source A namespace URI.
        /// </summary>
        public const string SourceANamespaceUri =
            "urn:opcfoundation.org:UA:WotAggregation:SourceA";

        /// <summary>
        /// Source B namespace URI.
        /// </summary>
        public const string SourceBNamespaceUri =
            "urn:opcfoundation.org:UA:WotAggregation:SourceB";

        /// <summary>
        /// Gets or sets the endpoint URL. When empty, host, port and instance name are used.
        /// </summary>
        public string? EndpointUrl { get; set; }

        /// <summary>
        /// Gets or sets the endpoint host.
        /// </summary>
        public string Host { get; set; } = "localhost";

        /// <summary>
        /// Gets or sets the endpoint port.
        /// </summary>
        public int Port { get; set; } = 62551;

        /// <summary>
        /// Gets or sets the source namespace URI.
        /// </summary>
        public string SourceNamespaceUri { get; set; } = SourceANamespaceUri;

        /// <summary>
        /// Gets or sets the OPC UA application name.
        /// </summary>
        public string ApplicationName { get; set; } = "FlatTagServer";

        /// <summary>
        /// Gets or sets the isolated PKI root.
        /// </summary>
        public string? PkiRoot { get; set; }

        /// <summary>
        /// Gets or sets the endpoint instance name.
        /// </summary>
        public string InstanceName { get; set; } = "SourceA";

        /// <summary>
        /// Gets or sets the deterministic values exposed by this source.
        /// </summary>
        public FlatTagValues Values { get; set; } = new();

        /// <summary>
        /// Gets or sets the deterministic values exposed for the second pump by this source.
        /// </summary>
        public FlatTagValues Pump2Values { get; set; } = new()
        {
            SerialNumber = "SN-002",
            ProductInstanceUri = "urn:simdevice:SimPump:PumpX-2000:SN-002",
            DifferentialPressure = 3.25,
            FluidTemperature = 318.15,
            MassFlow = 0.275,
            Level = 5.5,
            Cavitation = false,
            BearingTemperature = 331.4,
            PumpPowerInput = 14.25,
            PumpEfficiency = 84.5,
            NumberOfStarts = 29,
            MotorOverheat = false
        };
    }

    /// <summary>
    /// Deterministic values used by both source instances.
    /// </summary>
    public sealed class FlatTagValues
    {
        /// <summary>
        /// Gets or sets the pump manufacturer.
        /// </summary>
        public string Manufacturer { get; set; } = "SimPump Corp";

        /// <summary>
        /// Gets or sets the pump serial number.
        /// </summary>
        public string SerialNumber { get; set; } = "SN-001";

        /// <summary>
        /// Gets or sets the globally unique product instance URI.
        /// </summary>
        public string ProductInstanceUri { get; set; } = "urn:simdevice:SimPump:PumpX-2000:SN-001";

        /// <summary>
        /// Gets or sets the differential pressure.
        /// </summary>
        public double DifferentialPressure { get; set; } = 2.75;

        /// <summary>
        /// Gets or sets the fluid temperature.
        /// </summary>
        public double FluidTemperature { get; set; } = 315.65;

        /// <summary>
        /// Gets or sets the mass flow.
        /// </summary>
        public double MassFlow { get; set; } = 0.1825;

        /// <summary>
        /// Gets or sets the level.
        /// </summary>
        public double Level { get; set; } = 6.75;

        /// <summary>
        /// Gets or sets the cavitation state.
        /// </summary>
        public bool Cavitation { get; set; }

        /// <summary>
        /// Gets or sets the bearing temperature.
        /// </summary>
        public double BearingTemperature { get; set; } = 328.4;

        /// <summary>
        /// Gets or sets the pump power input.
        /// </summary>
        public double PumpPowerInput { get; set; } = 12.5;

        /// <summary>
        /// Gets or sets the pump efficiency.
        /// </summary>
        public double PumpEfficiency { get; set; } = 88.0;

        /// <summary>
        /// Gets or sets the number of starts.
        /// </summary>
        public uint NumberOfStarts { get; set; } = 17;

        /// <summary>
        /// Gets or sets the motor-overheat state.
        /// </summary>
        public bool MotorOverheat { get; set; }
    }
}
