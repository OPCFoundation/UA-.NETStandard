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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Recycling.Model;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Recycling.Samples.Tests
{
    /// <summary>
    /// Opt-in proof that a published native executable can host the real secure protocol and durable causal engine.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class NativeRecyclingSmokeTests
    {
        /// <summary>
        /// Uses only production CLI flags, explicit trust, generated clients and smoke-owned native child processes.
        /// </summary>
        [TestCase(RecyclingProcess.Collection)]
        [TestCase(RecyclingProcess.Drying)]
        [TestCase(RecyclingProcess.Extrusion)]
        [TestCase(RecyclingProcess.MeltFiltration)]
        [TestCase(RecyclingProcess.Pelletizing)]
        [Category("NativeSmoke")]
        [Explicit(
            "Set OPCUA_RECYCLING_NATIVE_EXE to the published NativeAOT executable and select this test explicitly.")]
        public async Task NativeCoordinatorAndProcessViewSupportSecureTypedCallsAndDurableRecovery(
            RecyclingProcess selectedProcess)
        {
            string executable = Environment.GetEnvironmentVariable("OPCUA_RECYCLING_NATIVE_EXE")
                ?? throw new InvalidOperationException("OPCUA_RECYCLING_NATIVE_EXE is required.");
            Assert.That(File.Exists(executable), Is.True);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            RecyclingConfiguration recipe = RecyclingConfiguration.ForScenario("equipment-faults") with
            {
                PlannedSourceBatches = 4,
                SourceZones = ["zone-1", "zone-2"],
                SourceColorVariationMilligrams = 0,
                HotWashPh = 9.25
            };
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.ProvisionNativeAsync(
                timeout.Token, recipe).ConfigureAwait(false);
            RecyclingHostOptions coordinatorOptions = environment.ServerOptions;
            RecyclingHostOptions viewOptions = await environment.ProvisionNativeProcessViewAsync(
                selectedProcess, timeout.Token)
                .ConfigureAwait(false);
            string roles = Path.Combine(environment.Root, "roles.json");
            string scenario = Path.Combine(environment.Root, "scenario.json");
            await File.WriteAllTextAsync(roles, JsonSerializer.Serialize(
                new RecyclingIdentityConfiguration { Identities = coordinatorOptions.Identities },
                RecyclingJsonContext.Default.RecyclingIdentityConfiguration), timeout.Token).ConfigureAwait(false);
            await File.WriteAllTextAsync(scenario, JsonSerializer.Serialize(
                coordinatorOptions.Simulation, RecyclingJsonContext.Default.RecyclingConfiguration), timeout.Token)
                .ConfigureAwait(false);

            NativeProcess? coordinator = null;
            NativeProcess? view = null;
            NativeProcess? restarted = null;
            var cleanupFailures = new List<Exception>();
            try
            {
                coordinator = await NativeProcess.StartAsync(
                    executable, environment.Root, Arguments(coordinatorOptions, roles, scenario), timeout.Token)
                    .ConfigureAwait(false);
                view = await NativeProcess.StartAsync(
                    executable, environment.Root, Arguments(viewOptions, roles, scenario), timeout.Token)
                    .ConfigureAwait(false);
                RecyclingCoordinatorClient administrator = await environment.ConnectAsync(
                    "administrator", timeout.Token).ConfigureAwait(false);
                RecyclingCoordinatorClient observer = await environment.ConnectAsync("observer", timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(administrator.Session.ConfiguredEndpoint.Description.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                await AssertMetadataAsync(administrator, timeout.Token).ConfigureAwait(false);
                await WaitForProcessAsync(observer, selectedProcess, timeout.Token).ConfigureAwait(false);
                foreach (RecyclingProcess process in Enum.GetValues<RecyclingProcess>())
                {
                    if (process == selectedProcess)
                    {
                        continue;
                    }
                    RecyclingCoordinatorClient participant = await environment.ConnectAsync(
                        "view-" + process, timeout.Token).ConfigureAwait(false);
                    await ExecuteAsync(participant, coordinatorOptions.LineId,
                        RecyclingCommandKind.Register, process, 0, timeout.Token).ConfigureAwait(false);
                    await ExecuteAsync(participant, coordinatorOptions.LineId,
                        RecyclingCommandKind.Initialize, process, 0, timeout.Token).ConfigureAwait(false);
                }
                await ExecuteAsync(administrator, coordinatorOptions.LineId,
                    RecyclingCommandKind.Pause, RecyclingProcess.Collection, 0, timeout.Token).ConfigureAwait(false);
                RecyclingCommand? lastStep = null;
                RecyclingCommandOutcome? lastOutcome = null;
                for (int i = 0; i < 5; i++)
                {
                    (lastStep, lastOutcome) = await ExecuteAsync(
                        administrator, coordinatorOptions.LineId, RecyclingCommandKind.Step,
                        RecyclingProcess.Collection, 10, timeout.Token).ConfigureAwait(false);
                }
                CommittedSnapshot committed = await administrator.ReadSnapshotAsync(timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(committed.SchemaVersion, Is.EqualTo(2));
                Assert.That(committed.Lifecycle, Is.EqualTo(RunLifecycle.Paused));
                Assert.That(committed.Tick, Is.EqualTo(50));
                Assert.That(committed.SourceBatches, Is.EqualTo(4));
                Assert.That(committed.Packages.Count, Is.GreaterThan(0));
                Assert.That(committed.Stages[10].Ph, Is.EqualTo(9.25));
                Assert.That(committed.Stages[3].JamEvents, Is.EqualTo(1));
                Assert.That(committed.Stages[6].JamEvents, Is.EqualTo(1));
                Assert.That(committed.Stages[18].StrandBreakEvents, Is.EqualTo(1));

                RecyclingCoordinatorClient viewObserver = await environment.ConnectAsync(
                    "observer", timeout.Token, viewOptions.EndpointUrl).ConfigureAwait(false);
                CommittedSnapshot projected = await WaitForViewAsync(
                    viewObserver, committed.Revision, timeout.Token).ConfigureAwait(false);
                Assert.That(projected.Tick, Is.EqualTo(committed.Tick));
                Assert.That(projected.Packages, Is.EqualTo(committed.Packages));
                Assert.That(projected.SourceTimestampUtc, Is.EqualTo(committed.SourceTimestampUtc));
                await CompanionProcessViewTests.AssertNativeProfileAsync(
                    viewObserver.Session, selectedProcess, coordinatorOptions.LineId, committed, timeout.Token)
                    .ConfigureAwait(false);
                RecyclingCommand unauthorized = lastStep! with
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    ExpectedRevision = projected.Revision
                };
                ServiceResultException denied = (await Assert.ThrowsAsync<ServiceResultException>(() =>
                    observer.ExecuteAsync(unauthorized, timeout.Token).AsTask()))!;
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                TestContext.Out.WriteLine(
                    $"Native secure coordinator PID={coordinator.Id}, {selectedProcess} PID={view.Id}; " +
                    $"schema={committed.SchemaVersion}, tick={committed.Tick}, " +
                    $"packages={committed.Packages.Count}, typed metadata/calls/subscriptions verified.");

                await environment.CloseClientsAsync().ConfigureAwait(false);
                int originalPid = coordinator.Id;
                await coordinator.TerminateAsync().ConfigureAwait(false);
                restarted = await NativeProcess.StartAsync(
                    executable, environment.Root, Arguments(coordinatorOptions, roles, scenario), timeout.Token)
                    .ConfigureAwait(false);
                RecyclingCoordinatorClient recoveredClient = await environment.ConnectAsync(
                    "administrator", timeout.Token).ConfigureAwait(false);
                RecyclingCommandOutcome retry = await recoveredClient.ExecuteAsync(lastStep!, timeout.Token)
                    .ConfigureAwait(false);
                CommittedSnapshot recovered = await recoveredClient.ReadSnapshotAsync(timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(retry, Is.EqualTo(lastOutcome));
                Assert.That(recovered.RunId, Is.EqualTo(committed.RunId));
                Assert.That(recovered.Lifecycle, Is.EqualTo(RunLifecycle.Paused));
                Assert.That(recovered.Tick, Is.EqualTo(committed.Tick));
                Assert.That(recovered.SourceMass, Is.EqualTo(committed.SourceMass));
                Assert.That(recovered.Parcels, Is.EqualTo(committed.Parcels));
                Assert.That(recovered.Packages, Is.EqualTo(committed.Packages));
                Assert.That(recovered.History, Is.EqualTo(committed.History));
                Assert.That(recovered.SourceTimestampUtc, Is.EqualTo(committed.SourceTimestampUtc));
                CommittedSnapshot registered = await WaitForProcessAsync(
                    recoveredClient, selectedProcess, timeout.Token)
                    .ConfigureAwait(false);
                RecyclingCoordinatorClient recoveredView = await environment.ConnectAsync(
                    "observer", timeout.Token, viewOptions.EndpointUrl).ConfigureAwait(false);
                CommittedSnapshot reprojected = await WaitForViewAsync(
                    recoveredView, registered.Revision, timeout.Token).ConfigureAwait(false);
                Assert.That(reprojected.RunId, Is.EqualTo(committed.RunId));
                Assert.That(reprojected.Tick, Is.EqualTo(50));
                Assert.That(reprojected.Packages, Is.EqualTo(committed.Packages));
                Assert.That(reprojected.SourceTimestampUtc, Is.EqualTo(committed.SourceTimestampUtc));
                await CompanionProcessViewTests.AssertNativeProfileAsync(
                    recoveredView.Session, selectedProcess, coordinatorOptions.LineId, reprojected, timeout.Token)
                    .ConfigureAwait(false);
                TestContext.Out.WriteLine(
                    $"Native restart PID={originalPid}->{restarted.Id}; run identity, paused tick50, " +
                    $"{committed.Packages.Count} packages, genealogy, source time and request outcome preserved.");
            }
            finally
            {
                try
                {
                    await environment.CloseClientsAsync().ConfigureAwait(false);
                }
                finally
                {
                    foreach (NativeProcess? child in new[] { view, restarted, coordinator })
                    {
                        if (child == null)
                        {
                            continue;
                        }
                        try
                        {
                            await child.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            cleanupFailures.Add(exception);
                        }
                    }
                }
            }
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException(
                    "One or more smoke-owned native processes failed cleanup.", cleanupFailures);
            }
        }

        private static async Task AssertMetadataAsync(
            RecyclingCoordinatorClient client,
            CancellationToken cancellationToken)
        {
            ushort index = checked((ushort)client.Session.NamespaceUris.GetIndex(RecyclingModelMapping.NamespaceUri));
            ReadResponse metadata = await client.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId
                    {
                        NodeId = new NodeId(Model.DataTypes.SnapshotDataType, index),
                        AttributeId = Attributes.DataTypeDefinition
                    }
                ], cancellationToken).ConfigureAwait(false);
            Assert.That(metadata.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(metadata.Results[0].WrappedValue.TryGetValue<StructureDefinition>(
                out StructureDefinition? definition, client.Session.MessageContext), Is.True);
            Assert.That(definition!.Fields.Span.ToArray().Any(static field => field.Name == "Stages"), Is.True);
        }

        private static async Task<CommittedSnapshot> WaitForProcessAsync(
            RecyclingCoordinatorClient client,
            RecyclingProcess process,
            CancellationToken cancellationToken)
        {
            await foreach (CoordinatorNotification notification in client.WatchAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (notification.IsAvailable && notification.Snapshot.Participants.Span.ToArray().Any(
                    participant => participant.Process == process &&
                        participant.Initialized))
                {
                    return notification.Snapshot;
                }
            }
            throw new InvalidOperationException("The native process view did not initialize.");
        }

        private static async Task<CommittedSnapshot> WaitForViewAsync(
            RecyclingCoordinatorClient connection,
            long minimumRevision,
            CancellationToken cancellationToken)
        {
            ushort index = checked((ushort)connection.Session.NamespaceUris
                .GetIndex(RecyclingModelMapping.NamespaceUri));
            var options = new MonitoredItemOptions
            {
                SamplingInterval = TimeSpan.FromMilliseconds(100),
                QueueSize = 1,
                Filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValueTimestamp }
            };
            await foreach (DataValueChange change in connection.Session.DefaultStreaming.SubscribeDataChangesAsync(
                new NodeId(Model.Variables.ProcessView_State, index), options, cancellationToken)
                .ConfigureAwait(false))
            {
                if (StatusCode.IsGood(change.Value.StatusCode) &&
                    change.Value.WrappedValue.TryGetValue<SnapshotDataType>(
                        out SnapshotDataType? wire, connection.Session.MessageContext) &&
                    wire != null && wire.Revision >= minimumRevision)
                {
                    return RecyclingModelMapping.FromWire(wire);
                }
            }
            throw new InvalidOperationException("The native view did not project the committed image.");
        }

        private static async Task<(RecyclingCommand Command, RecyclingCommandOutcome Outcome)> ExecuteAsync(
            RecyclingCoordinatorClient client,
            string lineId,
            RecyclingCommandKind kind,
            RecyclingProcess process,
            int steps,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < 32; attempt++)
            {
                CommittedSnapshot state = await client.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
                var command = new RecyclingCommand
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    LineId = lineId,
                    RunId = state.RunId,
                    ExpectedRevision = state.Revision,
                    Kind = kind,
                    Process = process,
                    Steps = steps
                };
                try
                {
                    RecyclingCommandOutcome outcome = await client.ExecuteAsync(command, cancellationToken)
                        .ConfigureAwait(false);
                    return (command, outcome);
                }
                catch (ServiceResultException exception)
                    when (exception.StatusCode == StatusCodes.BadInvalidState && attempt < 31)
                {
                    // Only definitive revision rejections get a new request; native view heartbeats may interleave.
                }
            }
            throw new InvalidOperationException("Native command contention exceeded the smoke bound.");
        }

        private static ArrayOf<string> Arguments(RecyclingHostOptions options, string roles, string scenario)
        {
            var arguments = new List<string>
            {
                options.Role == RecyclingHostRole.Coordinator ? "--role" : "--process",
                options.Role == RecyclingHostRole.Coordinator
                    ? "coordinator" : RecyclingUaEnvironment.ProcessArgument(options.Process),
                "--line-id", options.LineId,
                "--endpoint", options.EndpointUrl,
                "--pki-root", options.PkiRoot,
                "--state-root", options.StateRoot,
                "--identities", roles,
                "--scenario", scenario,
                "--pace-ms", "60000"
            };
            if (options.Role == RecyclingHostRole.ProcessView)
            {
                arguments.AddRange(
                [
                    "--coordinator", options.CoordinatorUrl,
                    "--identity-store", options.IdentityStorePath,
                    "--identity-subject", options.IdentitySubject
                ]);
            }
            return arguments.ToArrayOf();
        }

        /// <summary>
        /// Owns exactly one child process and bounded log capture; no global process lookup or name-based kill.
        /// </summary>
        private sealed class NativeProcess : IAsyncDisposable
        {
            private NativeProcess(Process process)
            {
                m_process = process;
                m_output = DrainAsync(process.StandardOutput);
                m_error = DrainAsync(process.StandardError);
            }

            /// <summary>
            /// Identifier of this exclusively owned child.
            /// </summary>
            internal int Id => m_process.Id;

            /// <summary>
            /// Waits on actual native endpoint readiness, not a startup delay.
            /// </summary>
            internal static async Task<NativeProcess> StartAsync(
                string executable,
                string directory,
                ArrayOf<string> arguments,
                CancellationToken cancellationToken)
            {
                var info = new ProcessStartInfo(executable)
                {
                    WorkingDirectory = directory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                foreach (string argument in arguments)
                {
                    info.ArgumentList.Add(argument);
                }
                var process = new Process { StartInfo = info };
                try
                {
                    if (!process.Start())
                    {
                        throw new InvalidOperationException("Native process could not be started.");
                    }
                }
                catch
                {
                    process.Dispose();
                    throw;
                }
                var child = new NativeProcess(process);
                try
                {
                    Task exit = process.WaitForExitAsync(cancellationToken);
                    Task winner = await Task.WhenAny(child.m_ready.Task, exit)
                        .WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (winner == exit)
                    {
                        await Task.WhenAll(child.m_output, child.m_error).ConfigureAwait(false);
                        throw new InvalidOperationException(
                            "Native process exited before endpoint readiness: " +
                            string.Join(Environment.NewLine, child.m_lines));
                    }
                    await child.m_ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return child;
                }
                catch
                {
                    await child.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            /// <summary>
            /// Terminates only the child represented by this process handle, including on test failure.
            /// </summary>
            internal async ValueTask TerminateAsync()
            {
                if (!m_process.HasExited)
                {
                    m_process.Kill(entireProcessTree: true);
                }
                await m_process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await Task.WhenAll(m_output, m_error).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }
                await TerminateAsync().ConfigureAwait(false);
                m_process.Dispose();
            }

            private async Task DrainAsync(StreamReader reader)
            {
                while (await reader.ReadLineAsync().ConfigureAwait(false) is string line)
                {
                    m_lines.Enqueue(line);
                    while (m_lines.Count > 120)
                    {
                        m_lines.TryDequeue(out _);
                    }
                    if (line.Contains("OPC UA server listening at", StringComparison.Ordinal))
                    {
                        m_ready.TrySetResult();
                    }
                }
            }

            private readonly Process m_process;
            private readonly Task m_output;
            private readonly Task m_error;
            private readonly ConcurrentQueue<string> m_lines = new();
            private readonly TaskCompletionSource m_ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_disposed;
        }
    }
}
