/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
 * This notice and this permission notice shall be
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
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

namespace MinimalClient
{
    /// <summary>
    /// Minimal OPC UA console client using the fluent ManagedSession API.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Main entry point for the minimal OPC UA client.
        /// </summary>
        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine("OPC UA Minimal Console Client");
            Console.WriteLine("OPC UA library: {0}", Utils.GetAssemblyBuildNumber());
            Console.WriteLine();

            // Get endpoint URL from arguments or use default
            string endpointUrl = args.Length > 0 ? args[0] : "opc.tcp://localhost:62541/MinimalBoilerServer";

            try
            {
                await RunClientAsync(endpointUrl).ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                return 1;
            }
        }

        private static async Task RunClientAsync(string endpointUrl)
        {
            Console.WriteLine($"Connecting to: {endpointUrl}");
            Console.WriteLine();

            // Create telemetry context
            using MinimalTelemetryContext telemetry = new MinimalTelemetryContext();

            // Create application configuration
            ApplicationConfiguration config = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "MinimalClient",
                ApplicationUri = "urn:localhost:OPCFoundation:MinimalClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    AutoAcceptUntrustedCertificates = true,
                },
            };

            await config.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);

            // Create a simple endpoint configuration
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create();

            // Create a basic endpoint
            EndpointDescription endpoint = new EndpointDescription
            {
                EndpointUrl = endpointUrl,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
            };

            ConfiguredEndpoint configuredEndpoint = new ConfiguredEndpoint(null, endpoint, endpointConfiguration);

            Console.WriteLine($"Using endpoint: {endpoint.EndpointUrl}");
            Console.WriteLine($"Security mode: {endpoint.SecurityMode}");
            Console.WriteLine($"Security policy: {endpoint.SecurityPolicyUri}");
            Console.WriteLine();

            // Create and connect managed session
            Console.WriteLine("Creating session...");
            ManagedSession session = await new ManagedSessionBuilder(config, telemetry)
                .UseEndpoint(configuredEndpoint)
                .WithSessionName("MinimalClient")
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .ConnectAsync()
                .ConfigureAwait(false);

            using (session)
            {
                Console.WriteLine("Connected!");
                Console.WriteLine();

                // Browse the Objects folder using Browser helper
                Console.WriteLine("Browsing Objects folder...");
                var browser = new Browser(session)
                {
                    BrowseDirection = BrowseDirection.Forward,
                    NodeClassMask = (uint)NodeClass.Object | (uint)NodeClass.Variable,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                };

                try
                {
                    ArrayOf<ReferenceDescription> references = await browser.BrowseAsync(
                        ObjectIds.ObjectsFolder,
                        CancellationToken.None).ConfigureAwait(false);

                    Console.WriteLine($"Found {references.Count} references");
                    foreach (ReferenceDescription reference in references)
                    {
                        Console.WriteLine($"  - {reference.DisplayName} ({reference.NodeClass})");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Browse failed: {ex.Message}");
                }

                Console.WriteLine();

                // Read the server time
                Console.WriteLine("Reading ServerStatus.CurrentTime...");
                try
                {
                    ReadResponse readResponse = await session.ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Both,
                        new ReadValueId[]
                        {
                            new ReadValueId
                            {
                                NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                                AttributeId = Attributes.Value,
                            },
                        },
                        CancellationToken.None).ConfigureAwait(false);

                    if (readResponse.Results.Count > 0)
                    {
                        DataValue dataValue = readResponse.Results[0];
                        if (!StatusCode.IsBad(dataValue.StatusCode))
                        {
                            Console.WriteLine($"Server time: {dataValue.WrappedValue}");
                        }
                        else
                        {
                            Console.WriteLine($"Failed to read server time: {dataValue.StatusCode}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Read failed: {ex.Message}");
                }

                Console.WriteLine();
                Console.WriteLine("Disconnecting...");
            }

            Console.WriteLine("Done");
        }

        private sealed class MinimalTelemetryContext : ITelemetryContext, IDisposable
        {
            private readonly ILoggerFactory m_loggerFactory;
            private readonly ActivitySource m_activitySource;
            private bool m_disposed;

            public MinimalTelemetryContext()
            {
                m_loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
                    builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
                m_activitySource = new ActivitySource("MinimalClient");
            }

            public ILoggerFactory LoggerFactory => m_loggerFactory;

            public ActivitySource ActivitySource => m_activitySource;

            public Meter CreateMeter()
            {
                return new Meter("MinimalClient");
            }

            public void Dispose()
            {
                if (!m_disposed)
                {
                    m_loggerFactory?.Dispose();
                    m_activitySource?.Dispose();
                    m_disposed = true;
                }
            }
        }
    }
}
