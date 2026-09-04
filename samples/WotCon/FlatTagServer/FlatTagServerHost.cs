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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlatTagServer
{
    /// <summary>
    /// Builds and runs the reusable flat-tag source host.
    /// </summary>
    public static class FlatTagServerHost
    {
        /// <summary>
        /// Builds a host from explicit options.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IHost Build(FlatTagServerOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            Configure(builder, options);
            return builder.Build();
        }

        /// <summary>
        /// Builds and runs a host from explicit options.
        /// </summary>
        public static async Task RunAsync(
            FlatTagServerOptions options,
            CancellationToken cancellationToken = default)
        {
            using IHost host = Build(options);
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds and runs a host from command-line configuration.
        /// </summary>
        public static async Task RunAsync(
            string[] args,
            CancellationToken cancellationToken = default)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
            FlatTagServerOptions options = FromConfiguration(builder.Configuration);
            Configure(builder, options);
            using IHost host = builder.Build();
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
        }

        private static void Configure(
            HostApplicationBuilder builder,
            FlatTagServerOptions options)
        {
            Validate(options);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Services.AddSingleton(options);

            string endpoint = options.EndpointUrl ??
                $"opc.tcp://{options.Host}:{options.Port}/{options.InstanceName}";

            builder.Services
                .AddOpcUa()
                .AddServer(server =>
                {
                    server.ApplicationName = options.ApplicationName;
                    server.ApplicationUri =
                        $"urn:localhost:OPCFoundation:{options.ApplicationName}:{options.InstanceName}";
                    server.ProductUri = "uri:opcfoundation.org:FlatTagServer";
                    if (!string.IsNullOrWhiteSpace(options.PkiRoot))
                    {
                        server.PkiRoot = options.PkiRoot;
                    }
                    server.AutoAcceptUntrustedCertificates = true;
                    server.IncludeUnsecurePolicyNone = true;
                    server.EndpointUrls.Add(endpoint);
                })
                .AddNodeManager<FlatTagNodeManagerFactory>();
        }

        private static FlatTagServerOptions FromConfiguration(ConfigurationManager configuration)
        {
            var options = new FlatTagServerOptions
            {
                EndpointUrl = configuration["endpoint"],
                Host = configuration["host"] ?? "localhost",
                Port = ReadInt32(configuration, "port", 62551),
                SourceNamespaceUri = configuration["namespace"] ??
                    FlatTagServerOptions.SourceANamespaceUri,
                ApplicationName = configuration["applicationName"] ?? "FlatTagServer",
                PkiRoot = configuration["pkiRoot"],
                InstanceName = configuration["instanceName"] ?? "SourceA"
            };

            options.Values.DifferentialPressure = ReadDouble(
                configuration, "differentialPressure", options.Values.DifferentialPressure);
            options.Values.FluidTemperature = ReadDouble(
                configuration, "fluidTemperature", options.Values.FluidTemperature);
            options.Values.MassFlow = ReadDouble(
                configuration, "massFlow", options.Values.MassFlow);
            options.Values.Level = ReadDouble(configuration, "level", options.Values.Level);
            options.Values.Cavitation = ReadBoolean(
                configuration, "cavitation", options.Values.Cavitation);
            options.Values.BearingTemperature = ReadDouble(
                configuration, "bearingTemperature", options.Values.BearingTemperature);
            options.Values.PumpPowerInput = ReadDouble(
                configuration, "pumpPowerInput", options.Values.PumpPowerInput);
            options.Values.PumpEfficiency = ReadDouble(
                configuration, "pumpEfficiency", options.Values.PumpEfficiency);
            options.Values.NumberOfStarts = ReadUInt32(
                configuration, "numberOfStarts", options.Values.NumberOfStarts);
            options.Values.MotorOverheat = ReadBoolean(
                configuration, "motorOverheat", options.Values.MotorOverheat);
            options.Pump2Values.DifferentialPressure = ReadDouble(
                configuration,
                "pump2DifferentialPressure",
                options.Pump2Values.DifferentialPressure);
            options.Pump2Values.FluidTemperature = ReadDouble(
                configuration,
                "pump2FluidTemperature",
                options.Pump2Values.FluidTemperature);
            options.Pump2Values.MassFlow = ReadDouble(
                configuration, "pump2MassFlow", options.Pump2Values.MassFlow);
            options.Pump2Values.Level = ReadDouble(
                configuration, "pump2Level", options.Pump2Values.Level);
            options.Pump2Values.Cavitation = ReadBoolean(
                configuration, "pump2Cavitation", options.Pump2Values.Cavitation);
            options.Pump2Values.BearingTemperature = ReadDouble(
                configuration,
                "pump2BearingTemperature",
                options.Pump2Values.BearingTemperature);
            options.Pump2Values.PumpPowerInput = ReadDouble(
                configuration,
                "pump2PumpPowerInput",
                options.Pump2Values.PumpPowerInput);
            options.Pump2Values.PumpEfficiency = ReadDouble(
                configuration,
                "pump2PumpEfficiency",
                options.Pump2Values.PumpEfficiency);
            options.Pump2Values.NumberOfStarts = ReadUInt32(
                configuration, "pump2NumberOfStarts", options.Pump2Values.NumberOfStarts);
            options.Pump2Values.MotorOverheat = ReadBoolean(
                configuration, "pump2MotorOverheat", options.Pump2Values.MotorOverheat);
            return options;
        }

        private static void Validate(FlatTagServerOptions options)
        {
            if (options.SourceNamespaceUri is not FlatTagServerOptions.SourceANamespaceUri and
                not FlatTagServerOptions.SourceBNamespaceUri)
            {
                throw new ArgumentException(
                    "The source namespace must identify SourceA or SourceB.",
                    nameof(options));
            }
            if (string.IsNullOrWhiteSpace(options.ApplicationName) ||
                string.IsNullOrWhiteSpace(options.InstanceName))
            {
                throw new ArgumentException(
                    "ApplicationName and InstanceName are required.",
                    nameof(options));
            }
            if (options.EndpointUrl is null &&
                (string.IsNullOrWhiteSpace(options.Host) || options.Port is < 1 or > 65535))
            {
                throw new ArgumentException("A valid host and port are required.", nameof(options));
            }
        }

        private static int ReadInt32(ConfigurationManager configuration, string key, int fallback)
        {
            return int.TryParse(
                configuration[key],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                ? value
                : fallback;
        }

        private static uint ReadUInt32(ConfigurationManager configuration, string key, uint fallback)
        {
            return uint.TryParse(
                configuration[key],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint value)
                ? value
                : fallback;
        }

        private static double ReadDouble(ConfigurationManager configuration, string key, double fallback)
        {
            return double.TryParse(
                configuration[key],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value)
                ? value
                : fallback;
        }

        private static bool ReadBoolean(ConfigurationManager configuration, string key, bool fallback)
        {
            return bool.TryParse(configuration[key], out bool value) ? value : fallback;
        }
    }
}
