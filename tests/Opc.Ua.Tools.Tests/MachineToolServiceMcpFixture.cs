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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Jobs;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Composes actual MachineTool job sources and file providers behind real authenticated opc.tcp sessions.
    /// </summary>
    internal sealed class MachineToolServiceMcpFixture : IAsyncDisposable
    {
        /// <summary>
        /// Creates isolated source engines, bounded provider mocks forwarding to real storage, and short local PKI.
        /// </summary>
        internal MachineToolServiceMcpFixture()
        {
            m_server = new ServerFixture<ServiceServer>(telemetry => new ServiceServer(telemetry, m_password))
            {
                SecurityNone = true,
                AutoAccept = true
            };
            Source = CreateSource();
            IndependentSource = CreateSource();
            m_receiver.Setup(receiver => receiver.ReceiveJobOrderAsync(
                It.IsAny<Isa95JobOrderOperationV2>(), It.IsAny<V2.ISA95JobOrderDataType>(),
                It.IsAny<ArrayOf<LocalizedText>>(), It.IsAny<CancellationToken>()))
                .Returns((Isa95JobOrderOperationV2 operation, V2.ISA95JobOrderDataType order,
                    ArrayOf<LocalizedText> comments, CancellationToken ct) =>
                {
                    ReceivedEncodingMask = order.EncodingMask;
                    return Source.ReceiveJobOrderAsync(operation, order, comments, ct);
                });
            Physical = new PhysicalFileSystemProvider(Path.Combine(m_root, "content"));
            IndependentFiles = new PhysicalFileSystemProvider(Path.Combine(m_root, "other"));
            Provider.SetupGet(provider => provider.MountName).Returns("Services");
            Provider.SetupGet(provider => provider.IsWritable).Returns(() => !DenyWrites);
            Provider.Setup(provider => provider.GetEntryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => Physical.GetEntryAsync(path, ct));
            Provider.Setup(provider => provider.EnumerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => Physical.EnumerateAsync(path, ct));
            Provider.Setup(provider => provider.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => OpenAsync(path, null, ct));
            Provider.Setup(provider => provider.OpenWriteAsync(
                It.IsAny<string>(), It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()))
                .Returns((string path, FileWriteMode mode, CancellationToken ct) => OpenAsync(path, mode, ct));
            Provider.Setup(provider => provider.CreateDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => Physical.CreateDirectoryAsync(path, ct));
            Provider.Setup(provider => provider.CreateFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => Physical.CreateFileAsync(path, ct));
            Provider.Setup(provider => provider.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) =>
                {
                    Mutations.Enqueue("Delete");
                    return Physical.DeleteAsync(path, ct);
                });
            Provider.Setup(provider => provider.MoveAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string from, string to, CancellationToken ct) => Physical.MoveAsync(from, to, ct));
            Provider.Setup(provider => provider.CopyAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string from, string to, CancellationToken ct) => Physical.CopyAsync(from, to, ct));
        }

        /// <summary>
        /// Gets the authorized real UA session.
        /// </summary>
        internal ISession Session { get; private set; } = null!;

        /// <summary>
        /// Gets another authorized session with a different session identity and file-handle ownership.
        /// </summary>
        internal ISession Peer { get; private set; } = null!;

        /// <summary>
        /// Gets an actual anonymous UA session, denied standard file mutations and configured job controls.
        /// </summary>
        internal ISession Anonymous { get; private set; } = null!;

        /// <summary>
        /// Gets the full machine and its actual nested services.
        /// </summary>
        internal MachineToolRuntime<MachineToolState> Machine { get; private set; } = null!;

        /// <summary>
        /// Gets another machine with independent jobs/files and no WorkMasters selection.
        /// </summary>
        internal MachineToolRuntime<MachineToolState> Independent { get; private set; } = null!;

        /// <summary>
        /// Gets a machine without either optional service.
        /// </summary>
        internal MachineToolRuntime<MachineToolState> Minimal { get; private set; } = null!;

        /// <summary>
        /// Gets the real source that composes the existing ISA-95 engine and explicit responses/catalog.
        /// </summary>
        internal MachineToolJobSource Source { get; }

        /// <summary>
        /// Gets the isolated second source.
        /// </summary>
        internal MachineToolJobSource IndependentSource { get; }

        /// <summary>
        /// Gets real storage exposed only through the standard runtime's file binding.
        /// </summary>
        internal PhysicalFileSystemProvider Physical { get; }

        /// <summary>
        /// Gets the second root's independent storage.
        /// </summary>
        internal PhysicalFileSystemProvider IndependentFiles { get; }

        /// <summary>
        /// Gets the forwarding provider used to observe resource lifetime and simulate delayed storage.
        /// </summary>
        internal Mock<IFileSystemProvider> Provider { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets or sets a provider refusal policy independent of caller consent and authenticated identity.
        /// </summary>
        internal bool DenyWrites { get; set; }

        /// <summary>
        /// Gets the number of provider streams not yet closed by the standard file handle binder.
        /// </summary>
        internal int ActiveStreams => Volatile.Read(ref m_activeStreams);

        /// <summary>
        /// Gets the actual decoded wire mask before the reference engine's canonical V1/V2 normalization.
        /// </summary>
        internal uint ReceivedEncodingMask { get; private set; }

        /// <summary>
        /// Gets observed writes/deletes to detect implicit retry or compensation.
        /// </summary>
        internal ConcurrentQueue<string> Mutations { get; } = new();

        /// <summary>
        /// Gets the actual generated Jobs state, for asserting namespace-qualified service binding.
        /// </summary>
        internal JobManagementState Management { get; private set; } = null!;

        /// <summary>
        /// Starts isolated storage, the real endpoint, full/minimal machine instances, and three real sessions.
        /// </summary>
        internal async Task StartAsync()
        {
            await Physical.CreateDirectoryAsync("WorkMasters", default).ConfigureAwait(false);
            await SeedAsync(Physical, "file.bin", new byte[] { 1, 2, 3, 4 }).ConfigureAwait(false);
            await SeedAsync(Physical, "WorkMasters/program.nc", new byte[] { 5, 6, 7 }).ConfigureAwait(false);
            await SeedAsync(Physical, "large.bin", new byte[131072]).ConfigureAwait(false);
            await SeedAsync(Physical, "pause.bin", new byte[] { 8, 9, 10 }).ConfigureAwait(false);
            await SeedAsync(Physical, "empty.bin", ReadOnlyMemory<byte>.Empty).ConfigureAwait(false);
            await SeedAsync(IndependentFiles, "file.bin", new byte[] { 99 }).ConfigureAwait(false);
            await m_server.LoadConfigurationAsync(Path.Combine(m_root, "s")).ConfigureAwait(false);
            m_server.Config.ServerConfiguration!.UserTokenPolicies =
            [
                new UserTokenPolicy(UserTokenType.Anonymous),
                new UserTokenPolicy(UserTokenType.UserName) { SecurityPolicyUri = SecurityPolicies.Basic256Sha256 }
            ];
            ServiceServer server = await m_server.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = server.Manager.CreateMachineryBuildContext();
            Machine = await Define(context, "Services", Source)
                .WithProduction(new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.DynamicProductionPlan
                })
                .WithJobManagement(new MachineToolJobManagementOptions
                {
                    Receiver = m_receiver.Object,
                    ResponseProvider = Source,
                    Catalog = Source
                })
                .WithFileSystem(Provider.Object, includeWorkMasters: true)
                .BuildAsync().ConfigureAwait(false);
            var fileChildren = new List<BaseInstanceState>();
            Machine.State.FileSystem!.GetChildren(context.Context, fileChildren);
            FileState paused = fileChildren.OfType<FileState>()
                .Single(file => file.BrowseName.Name == "pause.bin");
            paused.MaxByteStringLength!.Value = 1;
            AuthorizeFiles(Machine.State.FileSystem, context.Context);
            Independent = await Define(context, "Independent", IndependentSource)
                .WithJobManagement(new MachineToolJobManagementOptions
                {
                    Receiver = IndependentSource,
                    ResponseProvider = IndependentSource,
                    Catalog = IndependentSource
                })
                .WithFileSystem(IndependentFiles)
                .BuildAsync().ConfigureAwait(false);
            Minimal = await Define(context, "Minimal", new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                .BuildAsync().ConfigureAwait(false);
            BaseInstanceState blocks = Machine.State.FindChild(context.Context,
                new QualifiedName("MachineryBuildingBlocks",
                    server.Manager.SystemContext.NamespaceUris.GetIndexOrAppend(
                        Opc.Ua.Machinery.Namespaces.Machinery)))!;
            Management = (JobManagementState)blocks.FindChild(context.Context,
                new QualifiedName(Opc.Ua.Machinery.Jobs.BrowseNames.JobManagement,
                    server.Manager.SystemContext.NamespaceUris.GetIndexOrAppend(
                        Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs)))!;
            var methods = new List<BaseInstanceState>();
            Management.JobOrderControl!.GetChildren(context.Context, methods);
            foreach (MethodState method in methods.OfType<MethodState>())
            {
                method.OnReadUserExecutable = AuthorizeCaller;
            }
            Session = await ConnectAsync("a", true).ConfigureAwait(false);
            Peer = await ConnectAsync("b", true).ConfigureAwait(false);
            Anonymous = await ConnectAsync("c", false).ConfigureAwait(false);
        }

        /// <summary>
        /// Observes the first real write or pauses a real asynchronous read until the test explicitly releases it.
        /// </summary>
        internal IoGate Pause(bool write)
        {
            var gate = new IoGate(write);
            m_gate = gate;
            return gate;
        }

        /// <summary>
        /// Creates standard orders with an explicit known WorkMaster and optional priority field.
        /// </summary>
        internal static V2.ISA95JobOrderDataType Order(string id) => new()
        {
            EncodingMask = (uint)(V2.ISA95JobOrderDataTypeFields.WorkMasterID |
                V2.ISA95JobOrderDataTypeFields.Priority),
            JobOrderID = id,
            WorkMasterID = [new V2.ISA95WorkMasterDataType { ID = "WM-1" }]
        };

        /// <summary>
        /// Seeds application-owned provider data, not a second OPC UA file protocol.
        /// </summary>
        internal static async Task SeedAsync(IFileSystemProvider provider, string path, ReadOnlyMemory<byte> content)
        {
            Stream stream = await provider.OpenWriteAsync(path, FileWriteMode.Truncate, default).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(content).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_gate?.Release.TrySetResult(true);
            foreach (ISession session in m_sessions)
            {
                if (session.Connected)
                {
                    await session.CloseAsync(1000, true).ConfigureAwait(false);
                }
                if (session is IAsyncDisposable asynchronous)
                {
                    await asynchronous.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    session.Dispose();
                }
            }
            foreach (ClientFixture client in m_clients)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            if (Machine != null)
            {
                await Machine.DisposeAsync().ConfigureAwait(false);
            }
            if (Independent != null)
            {
                await Independent.DisposeAsync().ConfigureAwait(false);
            }
            if (Minimal != null)
            {
                await Minimal.DisposeAsync().ConfigureAwait(false);
            }
            await Source.DisposeAsync().ConfigureAwait(false);
            await IndependentSource.DisposeAsync().ConfigureAwait(false);
            await m_server.StopAsync().ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(m_password);
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        /// <summary>
        /// Authenticates both command sessions with ephemeral fixture-only credentials.
        /// </summary>
        private async Task<ISession> ConnectAsync(string name, bool authenticated)
        {
            var client = new ClientFixture(NUnitTelemetryContext.Create());
            m_clients.Add(client);
            await client.LoadClientConfigurationAsync(Path.Combine(m_root, name), name).ConfigureAwait(false);
            ISession session = await client.ConnectAsync(new Uri($"opc.tcp://localhost:{m_server.Port}"),
                SecurityPolicies.Basic256Sha256,
                userIdentity: authenticated ? new UserIdentity("operator", m_password) : new UserIdentity())
                .ConfigureAwait(false);
            m_sessions.Add(session);
            return session;
        }

        /// <summary>
        /// Uses server-side UserExecutable policy; MCP consent cannot turn an anonymous session into an operator.
        /// </summary>
        private static ServiceResult AuthorizeCaller(ISystemContext context, NodeState node, ref bool value)
        {
            value = context is ISessionSystemContext { UserIdentity.TokenType: UserTokenType.UserName };
            return ServiceResult.Good;
        }

        /// <summary>
        /// Configures the integrator-owned directory's server policy, independently of the stock provider policy.
        /// </summary>
        private static void AuthorizeFiles(FileDirectoryState directory, ISystemContext context)
        {
            var children = new List<BaseInstanceState>();
            directory.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is MethodState method)
                {
                    method.OnReadUserExecutable = AuthorizeCaller;
                }
                else if (child is FileDirectoryState nested)
                {
                    AuthorizeFiles(nested, context);
                }
                else if (child is FileState file)
                {
                    file.Open!.OnReadUserExecutable = AuthorizeCaller;
                }
            }
        }

        /// <summary>
        /// Defines ordinary public MachineTool builder instances with application-owned providers.
        /// </summary>
        private static MachineToolBuilder<MachineToolState> Define(
            IMachineryBuildContext context, string name, IMachineToolProvider provider)
        {
            return context.AddMachineTool(name).WithProvider(provider).WithIdentification(identity =>
            {
                identity.Manufacturer = new LocalizedText("Service MCP tests");
                identity.SerialNumber = name;
                identity.ProductInstanceUri = "urn:service-mcp:" + name;
            });
        }

        /// <summary>
        /// Declares independent source mappings, not implicit job-to-program guesses.
        /// </summary>
        private static MachineToolJobSource CreateSource()
        {
            return new MachineToolJobSource(new MachineToolJobSourceOptions
            {
                Jobs = Enumerable.Range(0, 32).Select(index => new MachineToolJobSourceDescriptor
                {
                    JobOrderId = "Job-" + index,
                    WorkMasterId = "WM-1",
                    ProgramName = "PROGRAM" + index + ".NC"
                }).ToArrayOf(),
                MaximumPendingRevisions = 1024
            }, new MachineToolSnapshot
            {
                Operation = new MachineToolOperation { Mode = MachineOperationMode.Manual }
            });
        }

        /// <summary>
        /// Observes actual provider streams without bypassing any standard server or client method.
        /// </summary>
        private async ValueTask<Stream> OpenAsync(string path, FileWriteMode? mode, CancellationToken ct)
        {
            Stream stream;
            if (mode.HasValue)
            {
                if (DenyWrites)
                {
                    throw new UnauthorizedAccessException("The application file provider refuses this write.");
                }
                Mutations.Enqueue("Write");
                stream = await Physical.OpenWriteAsync(path, mode.Value, ct).ConfigureAwait(false);
            }
            else
            {
                stream = await Physical.OpenReadAsync(path, ct).ConfigureAwait(false);
            }
            Interlocked.Increment(ref m_activeStreams);
            IoGate? gate = path == "pause.bin" && m_gate?.Write == mode.HasValue ? m_gate : null;
            return new ObservedStream(stream, gate, () => Interlocked.Decrement(ref m_activeStreams));
        }

        /// <summary>
        /// Registers the real companion models and the modern server identity authenticator.
        /// </summary>
        internal sealed class ServiceServer : StandardServer
        {
            /// <summary>
            /// Creates an isolated server whose credential exists only for this fixture lifetime.
            /// </summary>
            internal ServiceServer(ITelemetryContext telemetry, byte[] password)
                : base(telemetry)
            {
                m_password = password;
                var options = new MachineryServerOptions
                {
                    Parts = MachineryParts.BuildingBlocks | MachineryParts.Jobs
                };
                AddNodeManager(new MachineryNodeManagerFactory(
                    [new MachineryModelProvider(options.Parts), new MachineToolModelProvider()], options));
            }

            /// <summary>
            /// Gets the real shared Machinery node manager.
            /// </summary>
            internal MachineryNodeManager Manager { get; private set; } = null!;

            /// <inheritdoc/>
            protected override async ValueTask OnNodeManagerStartedAsync(
                IServerInternal server,
                CancellationToken cancellationToken = default)
            {
                Manager = server.NodeManager.AsyncNodeManagers.OfType<MachineryNodeManager>().Single();
                await base.OnNodeManagerStartedAsync(server, cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            protected override void OnServerStarted(IServerInternal server)
            {
                base.OnServerStarted(server);
                server.IdentityRegistry.Register(new AnonymousAuthenticator());
                server.IdentityRegistry.Register(new UserNamePasswordAuthenticator((token, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (token.UserName != "operator" || token.DecryptedPassword == null ||
                        !CryptographicOperations.FixedTimeEquals(token.DecryptedPassword, m_password))
                    {
                        throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                    }
                    return new ValueTask<IUserIdentity>(new UserIdentity(token));
                }));
            }

            /// <summary>The ephemeral test credential, erased by the owning fixture.</summary>
            private readonly byte[] m_password;
        }

        /// <summary>
        /// Signals actual provider I/O and stream disposal, with an optional asynchronous read barrier.
        /// </summary>
        internal sealed class IoGate
        {
            /// <summary>
            /// Selects a write observation or asynchronous read pause.
            /// </summary>
            internal IoGate(bool write)
            {
                Write = write;
            }

            /// <summary>
            /// Gets the selected operation.
            /// </summary>
            internal bool Write { get; }

            /// <summary>
            /// Gets a signal after the real file handle has reached provider I/O.
            /// </summary>
            internal TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the test-controlled continuation, always released in cleanup.
            /// </summary>
            internal TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets a signal after the server closes the actual provider stream.
            /// </summary>
            internal TaskCompletionSource<bool> Closed { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Wraps a real backing stream to observe writes/disposal and pause asynchronous reads.
        /// </summary>
        private sealed class ObservedStream(Stream inner, IoGate? gate, Action onClose) : Stream
        {
            /// <inheritdoc/>
            public override bool CanRead => inner.CanRead;

            /// <inheritdoc/>
            public override bool CanSeek => inner.CanSeek;

            /// <inheritdoc/>
            public override bool CanWrite => inner.CanWrite;

            /// <inheritdoc/>
            public override long Length => inner.Length;

            /// <inheritdoc/>
            public override long Position
            {
                get => inner.Position;
                set => inner.Position = value;
            }

            /// <inheritdoc/>
            public override void Flush() => inner.Flush();

            /// <inheritdoc/>
            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

            /// <inheritdoc/>
            public override void SetLength(long value) => inner.SetLength(value);

            /// <inheritdoc/>
            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

            /// <inheritdoc/>
            public override void Write(byte[] buffer, int offset, int count)
            {
                inner.Write(buffer, offset, count);
                if (gate is { Write: true })
                {
                    gate.Entered.TrySetResult(true);
                }
            }

            /// <inheritdoc/>
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            {
                if (gate is { Write: false })
                {
                    gate.Entered.TrySetResult(true);
                    await gate.Release.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                return await inner.ReadAsync(buffer, ct).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
            {
                if (gate is { Write: true })
                {
                    gate.Entered.TrySetResult(true);
                }
                await inner.WriteAsync(buffer, ct).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                if (disposing && Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    inner.Dispose();
                    onClose();
                    gate?.Closed.TrySetResult(true);
                }
                base.Dispose(disposing);
            }

            /// <summary>The idempotent close gate for counter accounting.</summary>
            private int m_disposed;
        }

        /// <summary>Short, repository-local, fixture-owned certificate and provider storage.</summary>
        private readonly string m_root = Path.GetFullPath(Path.Combine(".ms-pki", Guid.NewGuid().ToString("N")[..8]));

        /// <summary>Ephemeral credentials; never a real or hardcoded password.</summary>
        private readonly byte[] m_password = System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));

        /// <summary>The real server fixture.</summary>
        private readonly ServerFixture<ServiceServer> m_server;

        /// <summary>Observes native decoded requests and forwards every verb to the actual source engine.</summary>
        private readonly Mock<IIsa95JobOrderReceiverV2> m_receiver = new(MockBehavior.Strict);

        /// <summary>The connected clients, disposed after their sessions.</summary>
        private readonly List<ClientFixture> m_clients = [];

        /// <summary>The real sessions owned exclusively by the fixture.</summary>
        private readonly List<ISession> m_sessions = [];

        /// <summary>The optional bounded storage pause for the next selected file open.</summary>
        private IoGate? m_gate;

        /// <summary>The number of server-owned provider streams still open.</summary>
        private int m_activeStreams;
    }
}
#endif
