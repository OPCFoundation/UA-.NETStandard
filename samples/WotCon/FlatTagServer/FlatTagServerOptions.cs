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
