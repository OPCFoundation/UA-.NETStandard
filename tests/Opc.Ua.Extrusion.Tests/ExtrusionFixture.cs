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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Extrusion.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.PlasticsRubber;
using Opc.Ua.PlasticsRubber.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Extrusion.Tests
{
    /// <summary>
    /// Hosts a direct Machinery owner for deterministic model/runtime tests.
    /// </summary>
    internal sealed class ExtrusionFixture : IAsyncDisposable
    {
        /// <summary>
        /// Gets the real owner.
        /// </summary>
        internal MachineryNodeManager Manager { get; private set; } = null!;

        /// <summary>
        /// Gets the public family runtime.
        /// </summary>
        internal ExtrusionRuntime Runtime => ExtrusionRuntime.Create(Manager);

        /// <summary>
        /// Loads all real models, intentionally including a repeated provider.
        /// </summary>
        internal async Task StartAsync(bool shiftedNamespaces = false, bool extrusion = true)
        {
            m_root = Path.GetFullPath(Path.Combine("artifacts", "extrusion-tests", "pki",
                Guid.NewGuid().ToString("N")));
            m_server = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            StandardServer server = await m_server.StartAsync(m_root).ConfigureAwait(false);
            if (shiftedNamespaces)
            {
                server.CurrentInstance.NamespaceUris.GetIndexOrAppend("urn:tests:unrelated:namespace");
            }
            ArrayOf<IMachineryModelProvider> providers = extrusion
                ? [new ExtrusionModelProvider(), new MachineryModelProvider(MachineryParts.BuildingBlocks),
                    new ExtrusionModelProvider()]
                : [new MachineryModelProvider(MachineryParts.BuildingBlocks)];
            Manager = new MachineryNodeManager(server.CurrentInstance, m_server.Config, providers,
                new MachineryServerOptions
                {
                    Parts = MachineryParts.BuildingBlocks,
                    InstanceNamespaceUri = "urn:tests:extrusion:instances"
                });
            await Manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Manager != null)
            {
                await Manager.DisposeAsync().ConfigureAwait(false);
            }
            if (m_server != null)
            {
                await m_server.StopAsync().ConfigureAwait(false);
            }
            if (m_root != null && Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        /// <summary>
        /// Creates explicit test-only provider selections; production has no allow-all default.
        /// </summary>
        internal static ExtrusionDeviceOptions Options(string serial, bool all = false)
        {
            var options = new ExtrusionDeviceOptions(new ExtrusionIdentity(
                "Test", serial, new LocalizedText("Model"), "urn:tests:extrusion:device:" + serial, "Controller"))
            {
                LineId = "TestLine",
                Commands = new CommandProvider(),
                Jobs = new JobProvider()
            };
            options.Common.AccessPolicy = new TestAccessPolicy();
            options.Common.StartDevices = new Mock<IStartDeviceProvider>().Object;
            options.Common.Maintenance = new Mock<IMaintenanceProvider>().Object;
            options.Common.Parameters = new Mock<IMonitoredParameterProvider>().Object;
            options.Common.ClosedLoopControl = new Mock<IClosedLoopControlProvider>().Object;
            options.Common.MachineConfiguration = new Mock<IMachineConfigurationProvider>().Object;
            options.Common.MesMessages = new Mock<IMesMessageProvider>().Object;
            options.Common.ProductionDatasets = new Mock<IProductionDatasetProvider>().Object;
            if (all)
            {
                options.Common.Materials = new Mock<IMaterialListProvider>().Object;
                options.Common.MaterialDensityUnit = KilogramPerCubicMetre;
                options.Features.IncludeAll();
            }
            return options;
        }

        /// <summary>
        /// Gets the UNECE kilogram per cubic metre (KMQ) a test material catalogue publishes as its DensityUnit.
        /// </summary>
        internal static EUInformation KilogramPerCubicMetre => new()
        {
            NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
            UnitId = 4934993,
            DisplayName = new LocalizedText("en", "kg/m³"),
            Description = new LocalizedText("en", "kilogram per cubic metre")
        };

        /// <summary>
        /// Exercises every concrete public constructor without creating an abstract ExtrusionDevice.
        /// </summary>
        internal static async ValueTask<ExtrusionDeviceState> CreateAsync(
            ExtrusionRuntime runtime,
            int part,
            ExtrusionDeviceOptions options)
        {
            return part switch
            {
                2 => (await runtime.CreateLineAsync(options).ConfigureAwait(false)).Node,
                3 => (await runtime.CreateExtruderAsync(options).ConfigureAwait(false)).Node,
                4 => (await runtime.CreateHaulOffAsync(options).ConfigureAwait(false)).Node,
                5 => (await runtime.CreateMeltPumpAsync(options).ConfigureAwait(false)).Node,
                6 => (await runtime.CreateFilterAsync(options).ConfigureAwait(false)).Node,
                7 => (await runtime.CreateDieAsync(options).ConfigureAwait(false)).Node,
                8 => (await runtime.CreatePelletizerAsync(options).ConfigureAwait(false)).Node,
                9 => (await runtime.CreateCutterAsync(options).ConfigureAwait(false)).Node,
                10 => (await runtime.CreateCalibratorAsync(options).ConfigureAwait(false)).Node,
                11 => (await runtime.CreateCorrugatorAsync(options).ConfigureAwait(false)).Node,
                12 => (await runtime.CreateCalenderAsync(options).ConfigureAwait(false)).Node,
                _ => throw new ArgumentOutOfRangeException(nameof(part))
            };
        }

        /// <summary>
        /// Gets a deterministic source timestamp distinct from the server clock.
        /// </summary>
        internal static DateTimeUtc Timestamp { get; } =
            new(new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc));

        /// <summary>The isolated existing server fixture.</summary>
        private ServerFixture<StandardServer>? m_server;

        /// <summary>The exact worktree-local PKI folder owned by this fixture.</summary>
        private string? m_root;
    }

    /// <summary>
    /// Explicit test authorization policy that records the original context and can reject it.
    /// </summary>
    internal sealed class TestAccessPolicy : IPlasticsRubberAccessPolicy
    {
        /// <summary>
        /// Gets or sets whether operations are denied.
        /// </summary>
        internal bool Deny { get; set; }

        /// <summary>
        /// Gets the last caller context.
        /// </summary>
        internal ISystemContext? LastContext { get; private set; }

        /// <inheritdoc/>
        public ValueTask AuthorizeAsync(
            PlasticsRubberRequestContext context,
            NodeId memberId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastContext = context.SystemContext;
            if (Deny)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            return default;
        }
    }

    /// <summary>
    /// Commits only explicit test writes; method acceptance does not manufacture production counts.
    /// </summary>
    internal sealed class CommandProvider : IExtrusionCommandProvider
    {
        /// <summary>
        /// Gets or sets whether original-caller authorization is denied.
        /// </summary>
        internal bool Deny { get; set; }

        /// <summary>
        /// Gets the number of actual provider executions.
        /// </summary>
        internal int Executions { get; private set; }

        /// <summary>
        /// Gets the last original command.
        /// </summary>
        internal ExtrusionCommand? LastCommand { get; private set; }

        /// <summary>
        /// Gets or sets an optional deterministic blocking seam.
        /// </summary>
        internal TaskCompletionSource<bool>? Blocking { get; set; }

        /// <inheritdoc/>
        public ValueTask<StatusCode> AuthorizeAsync(
            ExtrusionCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCommand = command;
            return new ValueTask<StatusCode>(Deny ? StatusCodes.BadUserAccessDenied : StatusCodes.Good);
        }

        /// <inheritdoc/>
        public async ValueTask<ExtrusionCommandResult> ExecuteAsync(
            ExtrusionCommand command,
            CancellationToken cancellationToken = default)
        {
            Executions++;
            if (Blocking != null)
            {
                await Blocking.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            return command.Kind == ExtrusionCommandKind.Write
                ? new ExtrusionCommandResult(StatusCodes.Good, new ExtrusionSnapshot(++m_revision,
                    [new ExtrusionValue(command.Target.NodeId,
                        new DataValue(command.Value, StatusCodes.Good, ExtrusionFixture.Timestamp))]))
                : new ExtrusionCommandResult(StatusCodes.Good);
        }

        /// <summary>The test authority's commit receipt sequence, not a physical production counter.</summary>
        private ulong m_revision = 100;
    }

    /// <summary>
    /// Deterministic authority for exercising the complete extrusion job protocol.
    /// </summary>
    internal sealed class JobProvider : IExtrusionJobProvider
    {
        /// <summary>
        /// Gets or sets whether authorization is denied.
        /// </summary>
        internal bool Deny { get; set; }

        /// <summary>
        /// Gets or sets the provider's commit result.
        /// </summary>
        internal StatusCode Result { get; set; } = StatusCodes.Good;

        /// <summary>
        /// Gets the number of committed provider calls.
        /// </summary>
        internal int Executions { get; private set; }

        /// <summary>
        /// Gets the exact last provider request.
        /// </summary>
        internal ExtrusionJobRequest? LastRequest { get; private set; }

        /// <inheritdoc/>
        public ValueTask<StatusCode> AuthorizeAsync(
            ExtrusionJobRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return new ValueTask<StatusCode>(Deny ? StatusCodes.BadUserAccessDenied : StatusCodes.Good);
        }

        /// <inheritdoc/>
        public ValueTask<ExtrusionJobResult> ExecuteAsync(
            ExtrusionJobRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Executions++;
            JobStatusEnumeration status = request.Operation switch
            {
                ExtrusionJobOperation.StartGroup or ExtrusionJobOperation.StartJob =>
                    JobStatusEnumeration.JOB_IN_PRODUCTION,
                ExtrusionJobOperation.InterruptGroup or ExtrusionJobOperation.InterruptJob =>
                    JobStatusEnumeration.JOB_INTERRUPTED,
                ExtrusionJobOperation.FinishGroup or ExtrusionJobOperation.FinishJob =>
                    JobStatusEnumeration.JOB_FINISHED,
                _ => JobStatusEnumeration.TRANSFERRED_ASSIGNED
            };
            return new ValueTask<ExtrusionJobResult>(new ExtrusionJobResult(Result, status,
            [
                new ConfigurationParameterType
                {
                    Id = 1,
                    Description = new LocalizedText("Length"),
                    DefaultValue = Variant.From(25.0),
                    Unit = ExtrusionUnits.Millimetre
                }
            ]));
        }
    }
}
