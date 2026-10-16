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

#if NET10_0
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Hosts two independent real MachineTool instances for passive MCP workflow checks.
    /// </summary>
    internal sealed class MachineToolMcpFixture : IAsyncDisposable
    {
        internal MachineToolMcpFixture()
        {
            m_server = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
                server.AddNodeManager(m_factory);
                return server;
            })
            {
                AutoAccept = true,
                SecurityNone = true
            };
            m_client = new ClientFixture(NUnitTelemetryContext.Create());
            m_client.UseSubscriptionEngineFactory(Opc.Ua.Client.DefaultSubscriptionEngineFactory.Instance);
        }

        internal static DateTimeUtc Acquired { get; } = new(new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc));

        internal MachineToolRuntime<MachineToolState> Machine { get; private set; } = null!;

        internal MachineToolRuntime<MachineToolState> Minimal { get; private set; } = null!;

        internal ISession Session { get; private set; } = null!;

        internal InMemoryMachineToolProvider Provider { get; private set; } = null!;

        internal async Task StartAsync()
        {
            await m_server.LoadConfigurationAsync(Path.Combine(m_root, "server")).ConfigureAwait(false);
            await m_server.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = m_factory.Manager!.CreateMachineryBuildContext();
            var sample = new MachineToolSampleInfo(Acquired, StatusCodes.UncertainLastUsableValue);
            var program = new MachineToolProgram
            {
                Name = "CUT.NC", Key = "program", JobIdentifier = "job-1",
                Phase = MachineToolProductionPhase.Initializing, Sample = sample
            };
            Provider = new InMemoryMachineToolProvider(new MachineToolSnapshot
            {
                Operation = new MachineToolOperation { Mode = MachineOperationMode.Automatic, Sample = sample },
                ActiveProgram = program,
                Production = new MachineToolProductionObservation
                {
                    Jobs = [new MachineToolProductionJob
                    {
                        Key = "job", Identifier = "job-1", RunsPlanned = 3, RunsPlannedIsValid = true,
                        Programs = [program], Sample = sample,
                        PartSets = [new MachineToolProductionPartSet
                        {
                            Key = "batch", Name = "Batch A", PartsPlannedPerRun = 1, Sample = sample,
                            Parts = [new MachineToolProductionPart
                            {
                                Key = "part", Name = "Housing", Identifier = "part-1",
                                Phase = MachineToolProductionPhase.Initializing, Sample = sample
                            }]
                        }]
                    }]
                },
                Equipment = new MachineToolEquipmentObservation
                {
                    Tools = [new MachineToolTool
                    {
                        Key = "Cutter", Name = "Cutter A", Identifier = "tool-a",
                        Location = new MachineToolLocation("Magazine", 4),
                        Control = new MachineToolToolControl(
                            7, ToolManagement.NumberBased, false, ToolLocked.CapabilityUnavailable),
                        Life = [new MachineToolToolLife
                        {
                            Name = "Parts", Indication = ToolLifeIndication.NumberOfParts, IsCountingUp = true,
                            Value = new DataValue(Variant.From(9007199254740993ul), sample.StatusCode, Acquired),
                            EngineeringUnits = new EUInformation
                            {
                                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                                UnitId = 0x433632, DisplayName = new LocalizedText("1")
                            },
                            Sample = sample
                        }],
                        Sample = sample
                    }]
                },
                Prognoses = new MachineToolPrognosisObservation
                {
                    Entries = [new MachineToolPrognosis
                    {
                        Key = "Service", Kind = MachineToolPrognosisKind.Maintenance,
                        Activity = new LocalizedText("Inspect cutter"),
                        PredictedTime = new DataValue(Variant.From(Acquired), StatusCodes.Good, Acquired),
                        Sample = sample
                    }]
                }
            });
            Machine = await context.AddMachineTool("McpMachine")
                .WithProvider(Provider)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("MCP fixture");
                    identity.SerialNumber = "Machine-A";
                    identity.ProductInstanceUri = "urn:mcp:machine:a";
                })
                .WithEquipment(new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools | MachineToolFeatures.ToolLife
                })
                .WithPrognoses(new MachineToolPrognosisOptions
                {
                    Features = MachineToolFeatures.Prognoses,
                    SupportedKinds = [MachineToolPrognosisKind.Maintenance]
                })
                .WithProduction(new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.ProductionParts
                })
                .WithNotifications(new MachineToolNotificationOptions { Provider = new Notifications() })
                .BuildAsync().ConfigureAwait(false);
            Minimal = await context.AddMachineTool("McpMinimal")
                .WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("MCP fixture");
                    identity.SerialNumber = "Machine-B";
                    identity.ProductInstanceUri = "urn:mcp:machine:b";
                }).BuildAsync().ConfigureAwait(false);
            await m_client.LoadClientConfigurationAsync(Path.Combine(m_root, "client"), "MachineToolMcp")
                .ConfigureAwait(false);
            Session = await m_client.ConnectAsync(new Uri(m_server.Config.ServerConfiguration!.BaseAddresses[0]),
                SecurityPolicies.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Supplies an acquired active condition so refresh exercises real generated event fields.
        /// </summary>
        private sealed class Notifications : IMachineToolNotificationProvider
        {
            /// <inheritdoc/>
            public ValueTask<MachineToolNotificationSnapshot> ReadAsync(
                ulong afterSequence, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<MachineToolNotificationSnapshot>(new MachineToolNotificationSnapshot
                {
                    Alerts = [new MachineToolAlert
                    {
                        Key = "Lubricant", ErrorCode = "W17", Message = new LocalizedText("Low lubricant"),
                        Severity = 450, IsActive = true,
                        Sample = new MachineToolSampleInfo(Acquired, StatusCodes.UncertainLastUsableValue)
                    }]
                });
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Session != null)
            {
                await Session.CloseAsync(default).ConfigureAwait(false);
                Session.Dispose();
            }
            await m_client.DisposeAsync().ConfigureAwait(false);
            await m_server.StopAsync().ConfigureAwait(false);
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        private sealed class Factory : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => MachineToolModels.GetNamespaceUris();

            internal MachineryNodeManager? Manager { get; private set; }

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                var options = new MachineryServerOptions
                {
                    Parts = MachineryParts.BuildingBlocks | MachineryParts.Jobs,
                    InstanceNamespaceUri = "urn:machine-mcp:instances"
                };
                Manager = new MachineryNodeManager(server, configuration,
                    [new MachineryModelProvider(options.Parts), new MachineToolModelProvider()], options);
                return new ValueTask<IAsyncNodeManager>(Manager);
            }
        }

        private readonly Factory m_factory = new();
        private readonly ServerFixture<StandardServer> m_server;
        private readonly ClientFixture m_client;
        private readonly string m_root = Path.Combine(Path.GetTempPath(), "mt-mcp", Guid.NewGuid().ToString("N"));
    }
}
#endif
