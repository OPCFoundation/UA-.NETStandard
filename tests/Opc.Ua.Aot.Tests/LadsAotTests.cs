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

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Health;
using Opc.Ua.Di;
using Opc.Ua.LADS;
using Opc.Ua.LADS.Client;
using Opc.Ua.LADS.Server;
using Opc.Ua.Server;
using LadsProgramTemplate = Opc.Ua.LADS.Server.LadsProgramTemplate;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises LADS cooperative execution and file-backed recovery in the native test executable.
    /// </summary>
    public sealed class LadsAotTests
    {
        /// <summary>
        /// Completes and interrupts real runs, then restores durable data without restarting execution.
        /// </summary>
        [Test]
        public async Task LadsCooperativeLifecycleAndFileBackedRecoveryAreAotSafeAsync()
        {
            string directory = Path.Combine(Path.GetTempPath(), "LadsAot-" + Guid.NewGuid().ToString("N"));
            string storage = Path.Combine(directory, "store");
            string completedRun;
            string interruptedRun;
            ByteString fileBytes;
            DateTimeUtc maintenanceDate;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            CancellationToken cancellationToken = timeout.Token;
            try
            {
                using (var store = new FileSystemLadsStore(LocalFileSystem.Instance, storage))
                {
                    var executor = new NativeExecutor();
                    await using ServiceProvider provider = CreateProvider(directory, store, executor);
                    IHostedService host = provider.GetServices<IHostedService>().Single();
                    await host.StartAsync(cancellationToken).ConfigureAwait(false);
                    IAssetHandle? asset = null;
                    try
                    {
                        ILadsDevice device = await provider.GetRequiredService<ILadsDeviceRegistry>()
                            .WaitForDeviceAsync(kDevice, cancellationToken).ConfigureAwait(false);
                        asset = device.Asset ??
                            throw new InvalidOperationException("The configured LADS asset handle is missing.");
                        await Assert.That(asset.NodeId).IsEqualTo(device.NodeId);
                        await Assert.That(asset.ProductInstanceUri).IsEqualTo("urn:tests:lads:native:device");
                        await Assert.That(asset.AssetId).IsEqualTo("Native-1");
                        await Assert.That(asset.IsRegistered).IsTrue();
                        IAssetHealth health = asset.Health ??
                            throw new InvalidOperationException("The configured LADS asset health is missing.");
                        await health.SetDeviceHealthAsync(DeviceHealthEnumeration.FAILURE, cancellationToken)
                            .ConfigureAwait(false);
                        await Assert.That(health.DeviceHealth).IsEqualTo(DeviceHealthEnumeration.FAILURE);
                        await health.SetDeviceHealthAsync(DeviceHealthEnumeration.NORMAL, cancellationToken)
                            .ConfigureAwait(false);
                        ILadsFunctionalUnit unit = device.GetFunctionalUnit(kUnit);
                        IAnalogControlFunction flow = unit.GetFunction<IAnalogControlFunction>("Flow");
                        await flow.ResetTotalizerAsync(12.5, cancellationToken).ConfigureAwait(false);
                        await Assert.That(flow.TotalizedValue).IsEqualTo(12.5);
                        await flow.ResetTotalizerAsync(cancellationToken).ConfigureAwait(false);
                        await Assert.That(flow.TotalizedValue).IsEqualTo(0.0);
                        await flow.SetParameterAsync(
                            LadsFunctionMembers.CtrlP, 1.5, cancellationToken).ConfigureAwait(false);
                        IAnalogControlFunction composed = unit.GetFunction<IAnalogControlFunction>("Composed");
                        await composed.SetComposedTargetValueAsync("Amplitude", 2, cancellationToken)
                            .ConfigureAwait(false);
                        await Assert.That(composed.GetComposedTargetValue("Amplitude")).IsEqualTo(2.0);
                        await composed.ReportComputedTargetValueAsync(8, cancellationToken).ConfigureAwait(false);
                        await Assert.That(composed.TargetValue).IsEqualTo(8.0);
                        ICoverFunction cover = unit.GetFunction<ICoverFunction>("Guard");
                        await cover.ReportStateAsync(LadsCoverStateValue.Opened, cancellationToken)
                            .ConfigureAwait(false);
                        await Assert.That(cover.State).IsEqualTo(LadsCoverStateValue.Opened);
                        ILadsProgramManager programs = unit.Programs ??
                            throw new InvalidOperationException("The configured LADS program manager is missing.");
                        ILadsRunningOperations running = unit.RunningOperations ??
                            throw new InvalidOperationException("The configured LADS running operations are missing.");
                        await programs.AddTemplateAsync(
                            new LadsProgramTemplate(kTemplate).Step("Hold", TimeSpan.FromMinutes(10)),
                            cancellationToken).ConfigureAwait(false);
                        completedRun = await programs.StartProgramAsync(
                            kTemplate, cancellationToken: cancellationToken).ConfigureAwait(false);
                        await running.HoldAsync(cancellationToken).ConfigureAwait(false);
                        await Assert.That(unit.RunningState).IsEqualTo(LadsRunningStateValue.Held);
                        await running.UnholdAsync(cancellationToken).ConfigureAwait(false);
                        await Assert.That(unit.RunningState).IsEqualTo(LadsRunningStateValue.Execute);
                        await running.SuspendAsync(cancellationToken).ConfigureAwait(false);
                        await Assert.That(unit.RunningState).IsEqualTo(LadsRunningStateValue.Suspended);
                        await running.UnsuspendAsync(cancellationToken).ConfigureAwait(false);
                        await Assert.That(unit.RunningState).IsEqualTo(LadsRunningStateValue.Execute);
                        await running.ToCompleteAsync(cancellationToken).ConfigureAwait(false);
                        ILadsResult result = await programs.WaitForResultAsync(completedRun, cancellationToken)
                            .ConfigureAwait(false);
                        await Assert.That(executor.Executions).IsEqualTo(1);
                        await Assert.That(result.Outcome).IsEqualTo(LadsProgramOutcome.Completed);
                        await Assert.That(result.Files.Count).IsEqualTo(1);
                        fileBytes = result.Files[0].Content;
                        await Assert.That(fileBytes.Length).IsGreaterThan(0);
                        await Assert.That(unit.State).IsEqualTo(LadsFunctionalStateValue.Running);
                        await Assert.That(unit.RunningState).IsEqualTo(LadsRunningStateValue.Complete);
                        await Assert.That(device.OperationCycleCounter).IsEqualTo(1u);
                        await running.ResetAsync(cancellationToken).ConfigureAwait(false);
                        await Assert.That(unit.RunningState).IsEqualTo(LadsRunningStateValue.Idle);
                        ILadsMaintenanceTask maintenance = device.GetMaintenanceTask(kMaintenance);
                        await maintenance.StartAsync(cancellationToken).ConfigureAwait(false);
                        await maintenance.StopAsync(
                            MaintenanceTaskResultEnum.Success, new LocalizedText("Native calibration"),
                            cancellationToken).ConfigureAwait(false);
                        maintenanceDate = maintenance.LastExecutionDate;
                        interruptedRun = await programs.StartProgramAsync(
                            kTemplate, cancellationToken: cancellationToken).ConfigureAwait(false);
                        await Assert.That(programs.ActiveRun).IsNotNull();
                    }
                    finally
                    {
                        await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    await Assert.That(asset!.IsRegistered).IsFalse();
                }

                using var reopened = new FileSystemLadsStore(LocalFileSystem.Instance, storage);
                var restoredExecutor = new NativeExecutor();
                await using ServiceProvider restoredProvider = CreateProvider(directory, reopened, restoredExecutor);
                IHostedService restoredHost = restoredProvider.GetServices<IHostedService>().Single();
                await restoredHost.StartAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    ILadsDevice device = await restoredProvider.GetRequiredService<ILadsDeviceRegistry>()
                        .WaitForDeviceAsync(kDevice, cancellationToken).ConfigureAwait(false);
                    ILadsFunctionalUnit unit = device.GetFunctionalUnit(kUnit);
                    ILadsProgramManager programs = unit.Programs ??
                        throw new InvalidOperationException("The restored LADS program manager is missing.");
                    await Assert.That(unit.State).IsEqualTo(LadsFunctionalStateValue.Stopped);
                    await Assert.That(programs.ActiveRun).IsNull();
                    await Assert.That(programs.InterruptedRunId).IsEqualTo(interruptedRun);
                    await Assert.That(restoredExecutor.Executions).IsEqualTo(0);
                    await Assert.That(programs.Templates.Count).IsEqualTo(1);
                    await Assert.That(programs.Templates[0].Id).IsEqualTo(kTemplate);
                    await Assert.That(programs.Templates[0].Steps[0].Duration).IsEqualTo(TimeSpan.FromMinutes(10));
                    await Assert.That(programs.Results.Count).IsEqualTo(1);
                    await Assert.That(programs.Results[0].RunId).IsEqualTo(completedRun);
                    await Assert.That(programs.Results[0].Files.Count).IsEqualTo(1);
                    await Assert.That(programs.Results[0].Files[0].Content.Span.SequenceEqual(fileBytes.Span)).IsTrue();
                    await Assert.That(device.OperationCycleCounter).IsEqualTo(1u);
                    ILadsMaintenanceTask maintenance = device.GetMaintenanceTask(kMaintenance);
                    await Assert.That(maintenance.State).IsEqualTo(LadsMaintenanceStateValue.Finished);
                    await Assert.That(maintenance.LastResult).IsEqualTo(MaintenanceTaskResultEnum.Success);
                    await Assert.That(maintenance.LastExecutionDate).IsEqualTo(maintenanceDate);
                    await Assert.That(device.RequiresReadinessConfirmation).IsTrue();
                    await unit.StartAsync(cancellationToken).ConfigureAwait(false);
                    await Assert.That(device.RequiresReadinessConfirmation).IsFalse();
                    await Assert.That(restoredExecutor.Executions).IsEqualTo(0);
                    await unit.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await restoredHost.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Roots typed client factory registration independently from the server's hosted services.
        /// </summary>
        [Test]
        public async Task LadsClientRegistrationIsAotSafeAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(options => options.ApplicationName = "LadsAotClient").AddLadsClient();
            await using ServiceProvider provider = services.BuildServiceProvider();
            await Assert.That(provider.GetRequiredService<LadsClientFactory>()).IsNotNull();
            await Assert.That(provider.GetRequiredService<Func<CancellationToken, Task<LadsClient>>>()).IsNotNull();
        }

        /// <summary>
        /// Composes a standalone LADS server through the public DI entry point.
        /// </summary>
        private static ServiceProvider CreateProvider(
            string directory,
            ILadsStore store,
            ILadsProgramExecutor executor)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(executor);
            services.AddOpcUa().AddServer<StandardServer>(options =>
            {
                options.ApplicationName = "LadsAot";
                options.ApplicationUri = "urn:localhost:LadsAot";
                options.ProductUri = "urn:opcfoundation:LadsAot";
                options.PkiRoot = Path.Combine(directory, "pki");
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeUnsecurePolicyNone = true;
                options.EndpointUrls.Clear();
                options.EndpointUrls.Add("opc.tcp://localhost:" +
                    AotServerFixtureSupport.GetNextFreeIPPort().ToString(CultureInfo.InvariantCulture) + "/LadsAot");
            }).AddLads(options =>
            {
                options.Store = store;
                options.ConfirmInterruptedDeviceReadinessAsync = (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return new ValueTask<bool>(true);
                };
            })
                .ConfigureLads(context => context.AddDevice(kDevice, device => device
                    .Identification(id => id.ProductInstanceUri = "urn:tests:lads:native:device")
                    .AssetManagement(asset => asset.WithConfigurableAssetId("Native-1").WithDeviceHealth())
                    .MaintenanceTask(kMaintenance)
                    .FunctionalUnit(kUnit, unit => unit
                        .AnalogControl("Flow", options =>
                        {
                            options.Kind = LadsAnalogControlKind.Totalizer;
                            options.Pid = new LadsPidOptions { CtrlP = 1, CtrlTi = 2, CtrlTd = 3 };
                        })
                        .AnalogControl("Composed", options =>
                        {
                            options.Kind = LadsAnalogControlKind.Composed;
                            options.ComposedTargetValues = ["Amplitude"];
                        })
                        .Cover("Guard", options =>
                        {
                            options.Controllable = false;
                            options.Lockable = false;
                        })
                        .RunningOperations()
                        .Programs(LadsTemplateOperations.None))));
            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Counts physical execution entry while delegating cooperative behavior to the reference executor.
        /// </summary>
        private sealed class NativeExecutor : ILadsControlledProgramExecutor
        {
            /// <summary>
            /// Gets the number of execution requests received by this server instance.
            /// </summary>
            public int Executions => Volatile.Read(ref m_executions);

            /// <inheritdoc/>
            public ValueTask<ServiceResult> ChangeExecutionAsync(
                LadsProgramRun run,
                LadsRunningStateValue target,
                CancellationToken cancellationToken)
            {
                return m_executor.ChangeExecutionAsync(run, target, cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask ExecuteAsync(LadsProgramRun run, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref m_executions);
                return m_executor.ExecuteAsync(run, cancellationToken);
            }

            /// <summary>Cooperative reference implementation used for the real run.</summary>
            private readonly InMemoryLadsProgramExecutor m_executor = new();

            /// <summary>Execution requests received by this instance.</summary>
            private int m_executions;
        }

        /// <summary>Device name shared by the two server instances.</summary>
        private const string kDevice = "NativeDevice";

        /// <summary>Functional unit with cooperative execution enabled.</summary>
        private const string kUnit = "Chamber";

        /// <summary>Uploaded template retained across the restart.</summary>
        private const string kTemplate = "NativeRecipe";

        /// <summary>Maintenance task retained across the restart.</summary>
        private const string kMaintenance = "Calibration";
    }
}
