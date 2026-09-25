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
using Opc.Ua;
using UaLens.Samples;

namespace UaLens.Tests.Samples
{
    internal sealed class RepositorySampleTestContext : IAsyncDisposable
    {
        private RepositorySampleTestContext()
        {
            Root = Path.Combine(Path.GetTempPath(), "UaLens sample tests " + Guid.NewGuid().ToString("N"));
            Source = new RepositorySampleSource(
                Path.Combine(Root, "source"),
                RepositorySampleBuildConfiguration.Release,
                RepositorySampleFramework.Net10);
            RunParent = Path.Combine(Root, "runs");
            Files = new RepositorySampleFiles(RunParent, LocalFileSystem.Instance);
            Service = new RepositorySampleService(Files, Ports.Object, Runtime.Object, Probe.Object, Clock.Provider);
            Ports.Setup(value => value.ReserveAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult<IRepositorySamplePortLease>(Port);
                });
            Runtime.Setup(value => value.StartAsync(
                It.IsAny<RepositorySampleLaunch>(),
                It.IsAny<RepositorySampleOutput>(),
                It.IsAny<CancellationToken>()))
                .Returns((RepositorySampleLaunch launch, RepositorySampleOutput output, CancellationToken token) =>
                    StartRuntimeAsync(launch, output, token));
            Probe.Setup(value => value.DiscoverAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .Returns((Uri endpoint, CancellationToken token) =>
                {
                    ProbeEntered.TrySetResult();
                    return ProbeHandler is null ? Task.FromResult(Endpoints()) : ProbeHandler(endpoint, token);
                });
        }

        public string Root { get; }

        public RepositorySampleSource Source { get; }

        public string RunParent { get; }

        public RepositorySampleFiles Files { get; }

        public RepositorySampleService Service { get; }

        public SampleTestClock Clock { get; } = new();

        public SampleTestProcess Process { get; } = new(73101);

        public SampleTestPortLease Port { get; } = new(58123);

        public Mock<IRepositorySamplePortAllocator> Ports { get; } = new(MockBehavior.Strict);

        public Mock<IRepositorySampleRuntime> Runtime { get; } = new(MockBehavior.Strict);

        public Mock<IRepositorySampleProbe> Probe { get; } = new(MockBehavior.Strict);

        public TaskCompletionSource StartEntered { get; } = NewSignal();

        public TaskCompletionSource ProbeEntered { get; } = NewSignal();

        public TaskCompletionSource StartupCanceled { get; } = NewSignal();

        public RepositorySampleLaunch? Launch { get; private set; }

        public RepositorySampleOutput? Output { get; private set; }

        public Func<CancellationToken, Task<IRepositorySampleProcess>>? StartHandler { get; set; }

        public Func<Uri, CancellationToken, Task<ArrayOf<EndpointDescription>>>? ProbeHandler { get; set; }

        public bool WritePublicCertificate { get; set; } = true;

        public static ByteString PublicCertificate => ByteString.From("test-only-public-certificate"u8);

        public static TimeSpan Bound => TimeSpan.FromSeconds(10);

        public static RepositorySampleRunOptions Options => new()
        {
            RunSeconds = 10,
            StartupTimeout = TimeSpan.FromSeconds(5),
            ShutdownAllowance = TimeSpan.FromSeconds(2),
            TerminationTimeout = TimeSpan.FromSeconds(1),
            ProbeInterval = TimeSpan.FromMilliseconds(100)
        };

        public static TimeSpan GracefulBudget =>
            Options.StartupTimeout + TimeSpan.FromSeconds(Options.RunSeconds) + Options.ShutdownAllowance;

        public static async Task<RepositorySampleTestContext> CreateAsync()
        {
            var context = new RepositorySampleTestContext();
            bool created = false;
            try
            {
                Directory.CreateDirectory(context.Source.Root);
                await File.WriteAllTextAsync(Path.Combine(context.Source.Root, "UA.slnx"), "<Solution />")
                    .ConfigureAwait(false);
                ArrayOf<RepositorySampleDescriptor> samples = RepositorySampleCatalog.Entries;
                for (int index = 0; index < samples.Count; index++)
                {
                    RepositorySampleDescriptor sample = samples[index];
                    string project = Path.Combine(context.Source.Root, sample.ProjectDirectory);
                    Directory.CreateDirectory(project);
                    await File.WriteAllTextAsync(
                        Path.Combine(project, sample.AssemblyName + ".csproj"), "<Project />").ConfigureAwait(false);
                    string build = RepositorySampleCatalog.GetBuildDirectory(context.Source, sample.Id);
                    Directory.CreateDirectory(build);
                    ArrayOf<string> names =
                    [
                        sample.AssemblyName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty),
                        sample.AssemblyName + ".dll",
                        sample.AssemblyName + ".deps.json",
                        sample.AssemblyName + ".runtimeconfig.json"
                    ];
                    for (int artifact = 0; artifact < names.Count; artifact++)
                    {
                        string name = names[artifact];
                        await File.WriteAllTextAsync(Path.Combine(build, name), "Not executable: test fixture only.")
                            .ConfigureAwait(false);
                    }
                    if (sample.Id == RepositorySampleId.VisualInspectionCell)
                    {
                        string fixtures = Path.Combine(build, "Fixtures");
                        Directory.CreateDirectory(fixtures);
                        ArrayOf<string> fixtureNames =
                            ["bracket-ok.png", "bracket-not-ok.png", "bracket-ambiguous.png"];
                        for (int fixture = 0; fixture < fixtureNames.Count; fixture++)
                        {
                            await File.WriteAllTextAsync(
                                Path.Combine(fixtures, fixtureNames[fixture]), "Not an image: fixture only.")
                                .ConfigureAwait(false);
                        }
                    }
                }
                await File.WriteAllTextAsync(context.ReferenceTemplatePath(), ReferenceTemplate).ConfigureAwait(false);
                created = true;
                return context;
            }
            finally
            {
                if (!created)
                {
                    await context.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        public async Task ConfigureAsync(RepositorySampleId sample = RepositorySampleId.ConsoleReferenceServer)
        {
            await Service.ConfigureAsync(sample, Source).ConfigureAwait(false);
        }

        public async Task<RepositorySampleLaunch> PrepareAsync(RepositorySampleId sample)
        {
            RepositorySampleRunFiles files = Files.AllocateRun();
            m_manualFiles.Add(files);
            await files.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
            return await Files.PrepareAsync(Source, sample, Port.Port, Options, files, CancellationToken.None)
                .ConfigureAwait(false);
        }

        public ArrayOf<EndpointDescription> Endpoints()
        {
            RepositorySampleLaunch launch = Launch ??
                throw new InvalidOperationException("No fake sample was started.");
            return Endpoints(launch);
        }

        public static ArrayOf<EndpointDescription> Endpoints(RepositorySampleLaunch launch)
        {
            return
            [
                new EndpointDescription
                {
                    EndpointUrl = launch.Endpoint.AbsoluteUri,
                    ServerCertificate = PublicCertificate,
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    Server = new ApplicationDescription
                    {
                        ApplicationName = new LocalizedText(launch.Descriptor.ApplicationName),
                        ApplicationUri = launch.ApplicationUris[0],
                        ProductUri = launch.Descriptor.ProductUri,
                        ApplicationType = ApplicationType.Server
                    }
                }
            ];
        }

        public string ReferenceTemplatePath()
        {
            return Path.Combine(
                Source.Root,
                "samples", "Reference", "ConsoleReferenceServer",
                "Quickstarts.ReferenceServer.Config.xml");
        }

        public async ValueTask DisposeAsync()
        {
            Process.Complete(0);
            await Service.DisposeAsync().AsTask().WaitAsync(Bound).ConfigureAwait(false);
            foreach (RepositorySampleRunFiles files in m_manualFiles)
            {
                await files.CleanupAsync(CancellationToken.None).ConfigureAwait(false);
            }
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        public static TaskCompletionSource NewSignal()
        {
            return new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private async Task<IRepositorySampleProcess> StartRuntimeAsync(
            RepositorySampleLaunch launch,
            RepositorySampleOutput output,
            CancellationToken token)
        {
            Launch = launch;
            Output = output;
            if (WritePublicCertificate)
            {
                string certificates = launch.Descriptor.Id == RepositorySampleId.ConsoleReferenceServer
                    ? Path.Combine(launch.Files.PkiRoot, "own", "certs")
                    : Path.Combine(launch.Files.PkiRoot, "certs");
                Directory.CreateDirectory(certificates);
                await File.WriteAllBytesAsync(
                    Path.Combine(certificates, "fixture.der"), PublicCertificate.ToArray(), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            using CancellationTokenRegistration registration = token.Register(() => StartupCanceled.TrySetResult());
            output.Append(RepositorySampleOutputStream.StandardOutput, "Fake sample starting.\n");
            StartEntered.TrySetResult();
            return StartHandler is null
                ? Process
                : await StartHandler(token).WaitAsync(Bound, CancellationToken.None).ConfigureAwait(false);
        }

        private const string ReferenceTemplate = """
            <ApplicationConfiguration xmlns="http://opcfoundation.org/UA/SDK/Configuration.xsd"
              xmlns:ua="http://opcfoundation.org/UA/2008/02/Types.xsd">
              <ApplicationName>Source template</ApplicationName>
              <SecurityConfiguration><StorePath>HOST-PKI-MUST-NOT-BE-USED</StorePath></SecurityConfiguration>
              <TransportQuotas><OperationTimeout>120000</OperationTimeout></TransportQuotas>
              <ServerConfiguration>
                <BaseAddresses><ua:String>opc.tcp://localhost:62541/Old</ua:String></BaseAddresses>
                <AlternateBaseAddresses><ua:String>opc.tcp://remote:4840</ua:String></AlternateBaseAddresses>
                <SecurityPolicies><ServerSecurityPolicy><SecurityMode>None_1</SecurityMode>
                  <SecurityPolicyUri>unsafe</SecurityPolicyUri></ServerSecurityPolicy></SecurityPolicies>
                <UserTokenPolicies><ua:UserTokenPolicy><ua:TokenType>UserName_1</ua:TokenType>
                  </ua:UserTokenPolicy></UserTokenPolicies>
                <ReverseConnect><EndpointUrl>opc.tcp://remote:4840</EndpointUrl></ReverseConnect>
                <RegistrationEndpoint><EndpointUrl>opc.tcp://remote:4840</EndpointUrl></RegistrationEndpoint>
                <MaxRegistrationInterval>1000</MaxRegistrationInterval>
                <NodeManagerSaveFile>HOST-PERSISTENCE-MUST-NOT-BE-USED</NodeManagerSaveFile>
                <ServerCapabilities><ua:String>GDS</ua:String></ServerCapabilities>
                <MultiCastDnsEnabled>true</MultiCastDnsEnabled>
                <DurableSubscriptionsEnabled>true</DurableSubscriptionsEnabled>
              </ServerConfiguration>
              <Extensions><ua:XmlElement><ExternalStore>HOST-PKI-MUST-NOT-BE-USED</ExternalStore>
                </ua:XmlElement></Extensions>
              <TraceConfiguration><OutputFilePath>HOST-LOG-MUST-NOT-BE-USED</OutputFilePath></TraceConfiguration>
            </ApplicationConfiguration>
            """;

        private readonly List<RepositorySampleRunFiles> m_manualFiles = [];
    }

    internal sealed class SampleTestPortLease(int port) : IRepositorySamplePortLease
    {
        public int Port { get; } = port;

        public int ReleaseCount => Volatile.Read(ref m_releaseCount);

        public int DisposeCount => Volatile.Read(ref m_disposeCount);

        public ValueTask ReleaseSocketForLaunchAsync()
        {
            Interlocked.Increment(ref m_releaseCount);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref m_disposeCount);
            return ValueTask.CompletedTask;
        }

        private int m_releaseCount;
        private int m_disposeCount;
    }

    internal sealed class SampleTestProcess(int id) : IRepositorySampleProcess
    {
        public int Id { get; } = id;

        public Task<int> Exit => m_exit.Task;

        public int TerminationCount => Volatile.Read(ref m_terminationCount);

        public int DisposeCount => Volatile.Read(ref m_disposeCount);

        public TaskCompletionSource TerminationEntered { get; } = RepositorySampleTestContext.NewSignal();

        public TaskCompletionSource DisposeEntered { get; } = RepositorySampleTestContext.NewSignal();

        public Func<CancellationToken, ValueTask>? TerminateHandler { get; set; }

        public Func<ValueTask>? DisposeHandler { get; set; }

        public void Complete(int exitCode)
        {
            m_exit.TrySetResult(exitCode);
        }

        public async ValueTask TerminateOwnedTreeAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref m_terminationCount);
            TerminationEntered.TrySetResult();
            if (TerminateHandler is not null)
            {
                await TerminateHandler(cancellationToken).ConfigureAwait(false);
                return;
            }
            cancellationToken.ThrowIfCancellationRequested();
            Complete(-1);
        }

        public async ValueTask DisposeAsync()
        {
            if (!Exit.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Disposal must not abandon the fake process.");
            }
            Interlocked.Increment(ref m_disposeCount);
            DisposeEntered.TrySetResult();
            if (DisposeHandler is not null)
            {
                await DisposeHandler().ConfigureAwait(false);
            }
        }

        private readonly TaskCompletionSource<int> m_exit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int m_terminationCount;
        private int m_disposeCount;
    }

    /// <summary>
    /// Drives the real TimeProvider-based service deadlines using Moq's public
    /// TimeProvider/ITimer boundaries. No sleeps, processes or network resources.
    /// </summary>
    internal sealed class SampleTestClock
    {
        public SampleTestClock()
        {
            m_provider.SetupGet(value => value.TimestampFrequency).Returns(TimeSpan.TicksPerSecond);
            m_provider.Setup(value => value.GetTimestamp()).Returns(() => Interlocked.Read(ref m_timestamp));
            m_provider.Setup(value => value.GetUtcNow())
                .Returns(() => DateTimeOffset.UnixEpoch.AddTicks(Interlocked.Read(ref m_timestamp)));
            m_provider.Setup(value => value.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                .Returns((TimerCallback callback, object? state, TimeSpan due, TimeSpan period) =>
                    Register(() => callback(state), due, period));
        }

        public TimeProvider Provider => m_provider.Object;

        public Task WaitForTimerAsync(TimeSpan due)
        {
            lock (m_gate)
            {
                if (m_scheduled.Contains(due))
                {
                    return Task.CompletedTask;
                }
                TaskCompletionSource signal = RepositorySampleTestContext.NewSignal();
                m_waiters.Add((due, signal));
                return signal.Task.WaitAsync(RepositorySampleTestContext.Bound);
            }
        }

        public void Advance(TimeSpan elapsed)
        {
            ArrayOf<Action> callbacks;
            lock (m_gate)
            {
                m_timestamp += elapsed.Ticks;
                var due = new List<Action>();
                foreach (Registration timer in m_timers)
                {
                    if (!timer.Disposed && timer.Due <= m_timestamp)
                    {
                        due.Add(timer.Callback);
                        timer.Due = timer.Period < 0 ? long.MaxValue : m_timestamp + timer.Period;
                    }
                }
                callbacks = due.ToArrayOf();
            }
            foreach (Action callback in callbacks)
            {
                callback();
            }
        }

        private ITimer Register(Action callback, TimeSpan due, TimeSpan period)
        {
            var registration = new Registration(callback);
            var timer = new Mock<ITimer>(MockBehavior.Strict);
            timer.Setup(value => value.Change(It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                .Returns((TimeSpan next, TimeSpan interval) => Schedule(registration, next, interval));
            timer.Setup(value => value.Dispose()).Callback(() => Remove(registration));
            timer.Setup(value => value.DisposeAsync()).Returns(() =>
            {
                Remove(registration);
                return ValueTask.CompletedTask;
            });
            lock (m_gate)
            {
                m_timers.Add(registration);
                Schedule(registration, due, period);
            }
            return timer.Object;
        }

        private bool Schedule(Registration timer, TimeSpan due, TimeSpan period)
        {
            lock (m_gate)
            {
                if (timer.Disposed)
                {
                    return false;
                }
                timer.Due = due < TimeSpan.Zero ? long.MaxValue : m_timestamp + due.Ticks;
                timer.Period = period.Ticks;
                m_scheduled.Add(due);
                foreach ((TimeSpan expected, TaskCompletionSource signal) in m_waiters)
                {
                    if (expected == due)
                    {
                        signal.TrySetResult();
                    }
                }
                return true;
            }
        }

        private void Remove(Registration timer)
        {
            lock (m_gate)
            {
                timer.Disposed = true;
            }
        }

        private readonly Mock<TimeProvider> m_provider = new(MockBehavior.Strict);
        private readonly List<Registration> m_timers = [];
        private readonly HashSet<TimeSpan> m_scheduled = [];
        private readonly List<(TimeSpan Due, TaskCompletionSource Signal)> m_waiters = [];
        private readonly Lock m_gate = new();
        private long m_timestamp;

        private sealed class Registration(Action callback)
        {
            public Action Callback { get; } = callback;

            public long Due { get; set; }

            public long Period { get; set; }

            public bool Disposed { get; set; }
        }
    }
}
