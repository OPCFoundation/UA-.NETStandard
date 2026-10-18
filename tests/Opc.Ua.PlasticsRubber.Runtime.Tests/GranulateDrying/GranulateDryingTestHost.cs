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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.GranulateDrying.Client;
using Opc.Ua.GranulateDrying.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.PlasticsRubber;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.GranulateDrying.Tests
{
    /// <summary>
    /// An actual encrypted server/client host at the approved runtime and typed-client seams.
    /// All PKI and validation output stays under this worktree, never the operating-system temp directory.
    /// </summary>
    internal sealed class GranulateDryingTestHost : IAsyncDisposable
    {
        /// <summary>
        /// Creates a standalone or composed-host test.
        /// </summary>
        internal GranulateDryingTestHost(bool composed)
        {
            m_composed = composed;
            m_policy.Setup(policy => policy.CanControl(It.IsAny<ISystemContext>()))
                .Returns((ISystemContext context) => Allowed(context));
            m_policy.Setup(policy => policy.AuthorizeAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<GranulateDryingCommand>(), It.IsAny<CancellationToken>()))
                .Returns((ISystemContext context, GranulateDryingCommand command, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    bool readOnly = command.Member.EndsWith("/GetPage", StringComparison.Ordinal) ||
                        command.Member.EndsWith("/GetCurrentPage", StringComparison.Ordinal);
                    return new ValueTask<StatusCode>(readOnly || Allowed(context)
                        ? StatusCodes.Good : StatusCodes.BadUserAccessDenied);
                });
        }

        /// <summary>
        /// Gets the public runtime.
        /// </summary>
        internal GranulateDryingRuntime Runtime { get; private set; } = null!;

        /// <summary>
        /// Gets the authorized control session.
        /// </summary>
        internal ManagedSession Session { get; private set; } = null!;

        /// <summary>
        /// Gets the complete typed client.
        /// </summary>
        internal GranulateDryingClient Client { get; private set; } = null!;

        /// <summary>
        /// Gets the existing asset-management sidecar.
        /// </summary>
        internal IAssetManagement Assets { get; private set; } = null!;

        /// <summary>
        /// Gets telemetry for generated proxies.
        /// </summary>
        internal ITelemetryContext Telemetry { get; } = NUnitTelemetryContext.Create();

        /// <summary>
        /// Gets the dependency-injection container for ownership assertions.
        /// </summary>
        internal ServiceProvider Services => m_services!;

        /// <summary>
        /// Starts the real host and connects its first secure managed session.
        /// </summary>
        internal async Task StartAsync()
        {
            m_root = Path.GetFullPath(Path.Combine("artifacts", "gdd-tests", Guid.NewGuid().ToString("N")[..8]));
            var services = new ServiceCollection();
            services.AddLogging();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer<StandardServer>(ConfigureServer);
            if (m_composed)
            {
                builder.AddMachinery(options =>
                    {
                        options.Parts = MachineryParts.BuildingBlocks;
                        options.InstanceNamespaceUri = "urn:tests:gdd:composed";
                    })
                    .AddGranulateDryingToMachinery();
            }
            else
            {
                builder.AddGranulateDrying(options => options.InstanceNamespaceUri = "urn:tests:gdd:standalone");
            }
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            builder.AddAssetManagement().ConfigureGranulateDrying((context, _) =>
            {
                Runtime = context.Runtime;
                Assets = context.GetRequiredService<IAssetManagement>();
                ready.TrySetResult(true);
                return default;
            });
            m_services = services.BuildServiceProvider();
            m_host = m_services.GetServices<IHostedService>().Single();
            await m_host.StartAsync(CancellationToken.None).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            Task startup = m_host is BackgroundService { ExecuteTask: { } execute }
                ? await Task.WhenAny(ready.Task, execute).WaitAsync(timeout.Token).ConfigureAwait(false)
                : ready.Task;
            await startup.WaitAsync(timeout.Token).ConfigureAwait(false);
            await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            m_clientFixture = new ClientFixture(Telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(Path.Combine(m_root, "client")).ConfigureAwait(false);
            Session = await OpenSessionAsync(true).ConfigureAwait(false);
            Client = new GranulateDryingClient(Session, Telemetry);
        }

        /// <summary>
        /// Creates another secure session, optionally allowed to acquire control.
        /// </summary>
        internal async ValueTask<ManagedSession> OpenSessionAsync(bool allowControl)
        {
            EndpointDescription endpoint = await CoreClientUtils.SelectEndpointAsync(
                m_clientFixture!.Config, m_endpoint, useSecurity: true, discoverTimeout: 15000, Telemetry)
                .ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The test server has no secure endpoint.");
            ManagedSession session = await new ManagedSessionBuilder(m_clientFixture.Config, Telemetry)
                .UseEndpoint(new ConfiguredEndpoint(null, endpoint,
                    EndpointConfiguration.Create(m_clientFixture.Config)))
                .WithSessionName(allowControl ? "Drying controller" : "Drying observer")
                .ConnectAsync().ConfigureAwait(false);
            if (allowControl)
            {
                lock (m_permissionLock)
                {
                    m_allowed.Add(session.SessionId);
                }
            }
            return session;
        }

        /// <summary>
        /// Creates a device through the public runtime and explicitly injected authority/policy.
        /// </summary>
        internal ValueTask<GranulateDryingDevice> CreateAsync(
            GranulateDryingDeviceOptions options,
            DryingAuthority authority,
            GranulateDryingCommonProviders? common = null,
            CancellationToken cancellationToken = default)
        {
            return Runtime.CreateDeviceAsync(options, authority.Provider, m_policy.Object,
                common ?? new GranulateDryingCommonProviders(), cancellationToken);
        }

        /// <summary>
        /// Releases only this fixture's sessions, host, container, and uniquely owned PKI directory.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Session != null)
            {
                await Session.DisposeAsync().ConfigureAwait(false);
            }
            if (m_host != null)
            {
                await m_host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            if (m_services != null)
            {
                await m_services.DisposeAsync().ConfigureAwait(false);
            }
            m_clientFixture?.Dispose();
            if (m_root != null && Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        /// <summary>
        /// Creates a generic physical identity without private/vendor scenario fixtures.
        /// </summary>
        internal static GranulateDryingDeviceOptions BasicOptions(string name)
        {
            return new GranulateDryingDeviceOptions
            {
                BrowseName = name,
                Manufacturer = new LocalizedText("Test manufacturer"),
                Model = new LocalizedText("Drying device"),
                SerialNumber = name,
                ProductInstanceUri = "urn:tests:drying:" + name,
                AnalogMetadata =
                [
                    new GranulateDryingAnalogMetadata(GranulateDryingTarget.Bin(1), "Temperature/ActualValue",
                        GranulateDryingUnits.Celsius, new Range { Low = -20, High = 250 })
                ]
            };
        }

        /// <summary>
        /// Selects every GDD group while leaving common options independently selectable.
        /// </summary>
        internal static GranulateDryingDeviceOptions FullOptions(string name)
        {
            EUInformation hours = new()
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 0x485552,
                DisplayName = new LocalizedText("h"),
                Description = new LocalizedText("hour")
            };
            return BasicOptions(name) with
            {
                Features = GranulateDryingDeviceFeatures.All,
                Bins = [new GranulateDryingBinOptions { Features = GranulateDryingBinFeatures.All }],
                DesiccantBeds = [new GranulateDryingBedOptions { Features = GranulateDryingBedFeatures.All }],
                Maintenance =
                [
                    new GranulateDryingMaintenanceOptions
                    {
                        Id = "inspection",
                        DisplayName = new LocalizedText("Inspection"),
                        Description = new LocalizedText("Inspect the device"),
                        Instructions = ByteString.From(new byte[] { 1, 3, 5, 7 }),
                        OptionalMembers = ["Interval", "RemainingInterval", "TotalOperation", "AdditionalInformation"]
                    }
                ],
                AnalogMetadata =
                [
                    new GranulateDryingAnalogMetadata(GranulateDryingTarget.Bin(1), "Temperature/ActualValue",
                        GranulateDryingUnits.Celsius, new Range { Low = -20, High = 250 }),
                    new GranulateDryingAnalogMetadata(GranulateDryingTarget.Maintenance(1), "Interval",
                        hours, new Range { Low = 0, High = 1000000 }),
                    new GranulateDryingAnalogMetadata(GranulateDryingTarget.Maintenance(1), "RemainingInterval",
                        hours, new Range { Low = 0, High = 1000000 }),
                    new GranulateDryingAnalogMetadata(GranulateDryingTarget.Maintenance(1), "TotalOperation",
                        hours, new Range { Low = 0, High = 1000000 })
                ]
            };
        }

        /// <summary>
        /// Evaluates the fixture's explicit per-session ACL.
        /// </summary>
        private bool Allowed(ISystemContext context)
        {
            NodeId session = (context as ISessionSystemContext)?.SessionId ?? NodeId.Null;
            lock (m_permissionLock)
            {
                return m_allowed.Contains(session);
            }
        }

        /// <summary>
        /// Uses a short, worktree-local PKI root and an ephemeral test endpoint.
        /// </summary>
        private void ConfigureServer(OpcUaServerOptions options)
        {
            options.ApplicationName = "GddTests";
            options.ApplicationUri = "urn:localhost:tests:gdd";
            options.ProductUri = "urn:tests:gdd";
            options.PkiRoot = Path.Combine(m_root!, "server");
            options.AutoAcceptUntrustedCertificates = true;
            options.IncludeUnsecurePolicyNone = true;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            m_endpoint = "opc.tcp://localhost:" + port.ToString(CultureInfo.InvariantCulture) + "/gdd";
            options.EndpointUrls.Clear();
            options.EndpointUrls.Add(m_endpoint);
        }

        /// <summary>
        /// The selected registration route.
        /// </summary>
        private readonly bool m_composed;

        /// <summary>
        /// The explicitly mocked public authorization policy.
        /// </summary>
        private readonly Mock<IGranulateDryingAuthorization> m_policy = new(MockBehavior.Strict);

        /// <summary>
        /// Authorized session identities, unrelated to HMI user strings.
        /// </summary>
        private readonly HashSet<NodeId> m_allowed = [];

        /// <summary>
        /// Protects the ACL used by concurrent server callbacks.
        /// </summary>
        private readonly Lock m_permissionLock = new();

        /// <summary>
        /// The fixture's container.
        /// </summary>
        private ServiceProvider? m_services;

        /// <summary>
        /// The fixture's server host.
        /// </summary>
        private IHostedService? m_host;

        /// <summary>
        /// The fixture's client configuration.
        /// </summary>
        private ClientFixture? m_clientFixture;

        /// <summary>
        /// The unique owned PKI directory.
        /// </summary>
        private string? m_root;

        /// <summary>
        /// The fixture's endpoint.
        /// </summary>
        private string m_endpoint = string.Empty;
    }

    /// <summary>
    /// Deterministic mock of the public authority seam, with externally supplied committed results.
    /// It does not duplicate the server's control implementation.
    /// </summary>
    internal sealed class DryingAuthority
    {
        /// <summary>
        /// Creates a known committed initial state.
        /// </summary>
        internal DryingAuthority(bool full = false)
        {
            Current = Initial(full);
            m_provider.Setup(provider => provider.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (ReadFailure != null)
                    {
                        throw ReadFailure;
                    }
                    return new ValueTask<GranulateDryingCommit>(Current);
                });
            m_provider.Setup(provider => provider.ExecuteAsync(
                    It.IsAny<GranulateDryingCommand>(), It.IsAny<CancellationToken>()))
                .Returns((GranulateDryingCommand command, CancellationToken ct) =>
                {
                    LastCommand = command;
                    return Execute != null ? Execute(command, ct)
                        : new ValueTask<GranulateDryingResult>(new GranulateDryingResult(StatusCodes.BadNotSupported));
                });
        }

        /// <summary>
        /// Gets the mocked public interface.
        /// </summary>
        internal IGranulateDryingProvider Provider => m_provider.Object;

        /// <summary>
        /// Gets or sets the external committed state.
        /// </summary>
        internal GranulateDryingCommit Current { get; set; }

        /// <summary>
        /// Gets or sets a deterministic initial-read failure.
        /// </summary>
        internal Exception? ReadFailure { get; set; }

        /// <summary>
        /// Gets or sets the externally controlled request outcome.
        /// </summary>
        internal Func<GranulateDryingCommand, CancellationToken,
            ValueTask<GranulateDryingResult>>? Execute { get; set; }

        /// <summary>
        /// Gets the last request observed at the public provider seam.
        /// </summary>
        internal GranulateDryingCommand? LastCommand { get; private set; }

        /// <summary>
        /// Creates a later absolute commit; no counter increments or timer simulation occur here.
        /// </summary>
        internal GranulateDryingCommit Next(params GranulateDryingValue[] values)
        {
            Current = Current with
            {
                Revision = Current.Revision + 1,
                Timestamp = Time((int)Current.Revision + 1),
                Values = values.ToArrayOf(),
                RemoteControlEnabled = null,
                Errors = default,
                MachineState = null,
                OperationMode = null,
                User = null
            };
            return Current;
        }

        /// <summary>
        /// Creates a literal source reading with independently controlled quality and time.
        /// </summary>
        internal static GranulateDryingValue Value(
            GranulateDryingTarget target, string member, Variant value, int seconds = 1, StatusCode quality = default)
        {
            return new GranulateDryingValue(target, member, new DataValue(value, quality, Time(seconds)));
        }

        /// <summary>
        /// Gets a deterministic UTC instant.
        /// </summary>
        internal static DateTimeUtc Time(int seconds) =>
            (DateTimeUtc)new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);

        /// <summary>
        /// Defines known initial values independently of the runtime's selection implementation.
        /// </summary>
        private static GranulateDryingCommit Initial(bool full)
        {
            GranulateDryingTarget device = GranulateDryingTarget.Device;
            GranulateDryingTarget bin = GranulateDryingTarget.Bin(1);
            var values = new List<GranulateDryingValue>
            {
                Value(device, "Operation/DeviceMappingNumber", Variant.From(17u)),
                Value(device, "MachineConfiguration/UserMachineName", Variant.From("Drying")),
                Value(device, "MachineConfiguration/LocationName", Variant.From("Test cell")),
                Value(device, "MachineConfiguration/TimeZoneOffset",
                    Variant.FromStructure(new TimeZoneDataType { Offset = 0, DaylightSavingInOffset = false })),
                Value(bin, "DryingMode", Variant.From(DryingModeEnumeration.BATCH)),
                Value(bin, "Temperature/ActualValue", Variant.From(75.0))
            };
            if (full)
            {
                values.AddRange(
                [
                    Value(device, "DisplayLanguage", Variant.From("en")),
                    Value(device, "Operation/MainFailureBehaviour",
                        Variant.From(MainFailureBehaviourEnumeration.STANDBY)),
                    Value(device, "Operation/EnergyConsumptionLimit", Variant.From(100.0)),
                    Value(device, "Operation/EnergyConsumptionResettable", Variant.From(12.0)),
                    Value(device, "Operation/TotalEnergy/PowerConsumption", Variant.From(2000.0)),
                    Value(device, "Operation/TotalEnergy/ActualPower", Variant.From(2.0)),
                    Value(bin, "MaterialDryingTime", Variant.From(60000.0)),
                    Value(bin, "RemainingMaterialDryingTime", Variant.From(30000.0)),
                    Value(bin, "ExpiredDryingTime", Variant.From(30000.0)),
                    Value(bin, "EnergyConsumptionResettable", Variant.From(4.0)),
                    Value(bin, "TotalEnergy/PowerConsumption", Variant.From(800.0)),
                    Value(bin, "TotalEnergy/ActualPower", Variant.From(0.5)),
                    Value(bin, "DryingProtectionStatus", Variant.From(FunctionStatusEnumeration.OFF)),
                    Value(bin, "MaterialFeederDryerStatus", Variant.From(FunctionStatusEnumeration.OFF)),
                    Value(bin, "MaterialFeederMachineStatus", Variant.From(FunctionStatusEnumeration.OFF)),
                    Value(bin, "AllFeedersStatus", Variant.From(FunctionStatusEnumeration.OFF)),
                    Value(bin, "TemperatureControllerStatus", Variant.From(FunctionStatusEnumeration.OFF)),
                    Value(bin, "DryingProtectionAirVolumeFlowMode", Variant.From(FunctionStatusEnumeration.OFF)),
                    Value(GranulateDryingTarget.Bed(1), "EnergyConsumptionResettable", Variant.From(8.0)),
                    Value(GranulateDryingTarget.Bed(1), "DesiccantBedControllerStatus",
                        Variant.From(FunctionStatusEnumeration.OFF)),
                    Value(GranulateDryingTarget.Maintenance(1), "Status",
                        Variant.From(MaintenanceStatusEnumeration.NOT_DUE)),
                    Value(GranulateDryingTarget.Maintenance(1), "Interval", Variant.From(240.0)),
                    Value(GranulateDryingTarget.Maintenance(1), "RemainingInterval", Variant.From(240.0)),
                    Value(GranulateDryingTarget.Maintenance(1), "TotalOperation", Variant.From(10000.0)),
                    Value(GranulateDryingTarget.Maintenance(1), "AdditionalInformation",
                        Variant.From("Inspect filters"))
                ]);
            }
            return new GranulateDryingCommit(1, Time(1))
            {
                Values = values.ToArrayOf(),
                RemoteControlEnabled = true,
                Errors = [],
                MachineState = MachineryItemStateValue.NotExecuting,
                OperationMode = full ? MachineryOperationModeValue.None : null,
                User = full ? new GranulateDryingUser { IsPresent = false } : null
            };
        }

        /// <summary>
        /// The strictly mocked public authority.
        /// </summary>
        private readonly Mock<IGranulateDryingProvider> m_provider = new(MockBehavior.Strict);
    }
}
