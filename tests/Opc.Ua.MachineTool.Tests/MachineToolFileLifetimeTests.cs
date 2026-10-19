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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies the shared file-directory lifetime required by MachineTool FileSystem and WorkMasters.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class MachineToolFileLifetimeTests
    {
        /// <summary>
        /// Closing a session releases its directory-backed handles without closing another session's reader.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task FileSystemReleasesOnlyClosedSessionHandlesAsync(bool workMasters, bool abandonFile)
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "mt-files", Guid.NewGuid().ToString("N"));
            var serverFixture = new ServerFixture<FileServer>(telemetry => new FileServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var firstClient = new ClientFixture(telemetry);
            await using var secondClient = new ClientFixture(telemetry);
            ClientSession? first = null;
            ClientSession? second = null;
            try
            {
                await serverFixture.LoadConfigurationAsync(root).ConfigureAwait(false);
                FileServer server = await serverFixture.StartAsync().ConfigureAwait(false);
                IMachineryBuildContext context = server.Manager.CreateMachineryBuildContext();
                MachineToolState machine = context.Context.CreateInstanceOfMachineToolType(
                    context.MachinesFolder, new QualifiedName("FileMachine", context.InstanceNamespaceIndex));
                var builder = context.AddMachine(machine, new QualifiedName("FileMachine"));
                builder.WithIdentification(machine.Identification!, identity =>
                {
                    identity.Manufacturer = new LocalizedText("File lifetime test");
                    identity.SerialNumber = "FileMachine";
                    identity.ProductInstanceUri = "urn:machinetool-tests:file-lifetime";
                });
                machine.AddFileSystem(context.Context);
                FileDirectoryState directory = machine.FileSystem!;
                if (workMasters)
                {
                    ushort modelIndex = (ushort)context.Context.NamespaceUris.GetIndex(Namespaces.MachineTool);
                    FileDirectoryState workMastersDirectory = context.Context.CreateInstanceOfFileDirectoryType(
                        directory, new QualifiedName(BrowseNames.WorkMasters, modelIndex));
                    directory.AddChild(workMastersDirectory);
                    directory = workMastersDirectory;
                }
                await builder.BuildAsync().ConfigureAwait(false);
                await using IFileDirectoryBinding binding = await new FileDirectoryBinder().BindAsync(
                    directory,
                    CreateProvider(),
                    context.Context,
                    registerNode: (node, ct) => server.Manager.AddPredefinedNodeAsync(node, ct),
                    deregisterNode: async (node, ct) =>
                    {
                        await server.Manager.DeleteNodeAsync(server.Manager.SystemContext, node.NodeId, ct)
                            .ConfigureAwait(false);
                    }).ConfigureAwait(false);
                var children = new List<BaseInstanceState>();
                directory.GetChildren(context.Context, children);
                FileState file = (FileState)children.Find(static child => child is FileState)!;
                Assert.That(file, Is.Not.Null);

                var endpoint = new Uri($"opc.tcp://localhost:{serverFixture.Port}");
                await firstClient.LoadClientConfigurationAsync(root, "FileClientA").ConfigureAwait(false);
                await secondClient.LoadClientConfigurationAsync(root, "FileClientB").ConfigureAwait(false);
                first = await firstClient.ConnectAsync(endpoint, SecurityPolicies.None).ConfigureAwait(false);
                second = await secondClient.ConnectAsync(endpoint, SecurityPolicies.None).ConfigureAwait(false);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                CancellationToken ct = timeout.Token;
                var firstFile = new FileTypeClient(first, file.NodeId, telemetry);
                var secondFile = new FileTypeClient(second, file.NodeId, telemetry);
                uint firstHandle = await firstFile.OpenAsync(1, ct).ConfigureAwait(false);
                uint secondHandle = await secondFile.OpenAsync(1, ct).ConfigureAwait(false);
                Assert.That(await ReadOpenCountAsync(second, file, ct).ConfigureAwait(false), Is.EqualTo(2));

                ServiceResultException wrongOwner = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await secondFile.ReadAsync(firstHandle, 3, ct).ConfigureAwait(false)))!;
                Assert.That(wrongOwner.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                if (!abandonFile)
                {
                    await firstFile.CloseAsync(firstHandle, ct).ConfigureAwait(false);
                }
                StatusCode closed = await first.CloseAsync(ct).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(closed), Is.True);
                first.Dispose();
                first = null;

                ByteString surviving = await secondFile.ReadAsync(secondHandle, 3, ct).ConfigureAwait(false);
                Assert.That(surviving.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                await secondFile.CloseAsync(secondHandle, ct).ConfigureAwait(false);
                Assert.That(await ReadOpenCountAsync(second, file, ct).ConfigureAwait(false), Is.Zero,
                    "Session close must release abandoned FileSystem/WorkMasters handles before returning.");
            }
            finally
            {
                if (first != null)
                {
                    await first.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    first.Dispose();
                }
                if (second != null)
                {
                    await second.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    second.Dispose();
                }
                await serverFixture.StopAsync().ConfigureAwait(false);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>
        /// Reads OpenCount over the real session rather than inspecting the binder's private handle store.
        /// </summary>
        private static async ValueTask<ushort> ReadOpenCountAsync(
            ClientSession session,
            FileState file,
            CancellationToken cancellationToken)
        {
            DataValue value = await session.ReadValueAsync(file.OpenCount!.NodeId, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out ushort count), Is.True);
            return count;
        }

        /// <summary>
        /// Supplies a read-only file through the public provider contract.
        /// </summary>
        private static IFileSystemProvider CreateProvider()
        {
            var provider = new Mock<IFileSystemProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.MountName).Returns("Files");
            provider.SetupGet(value => value.IsWritable).Returns(false);
            provider.Setup(value => value.EnumerateAsync(string.Empty, It.IsAny<CancellationToken>()))
                .Returns((string _, CancellationToken ct) => EnumerateFileAsync(ct));
            provider.Setup(value => value.GetEntryAsync("program.nc", It.IsAny<CancellationToken>()))
                .ReturnsAsync(s_fileEntry);
            provider.Setup(value => value.OpenReadAsync("program.nc", It.IsAny<CancellationToken>()))
                .Returns((string _, CancellationToken _) =>
                    new ValueTask<Stream>(new MemoryStream([1, 2, 3], writable: false)));
            return provider.Object;
        }

        /// <summary>
        /// Enumerates the deterministic provider file while honoring cancellation.
        /// </summary>
        private static async IAsyncEnumerable<FileSystemEntry> EnumerateFileAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield return s_fileEntry;
        }

        /// <summary>
        /// Hosts the actual generated MachineTool model on the existing Machinery manager.
        /// </summary>
        private sealed class FileServer : StandardServer
        {
            /// <summary>
            /// Creates a test server without another namespace owner.
            /// </summary>
            public FileServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            /// <summary>
            /// Gets the shared namespace owner.
            /// </summary>
            public MachineryNodeManager Manager { get; private set; } = null!;

            /// <inheritdoc/>
            protected override ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                var options = new MachineryServerOptions
                {
                    Parts = MachineryParts.BuildingBlocks | MachineryParts.Jobs
                };
                Manager = new MachineryNodeManager(
                    server,
                    configuration,
                    [new MachineryModelProvider(options.Parts), new ModelProvider()],
                    options);
                return new ValueTask<IMasterNodeManager>(
                    new MasterNodeManager(server, configuration, null, [Manager]));
            }
        }

        /// <summary>
        /// Loads only MachineTool's released namespace closure for the lifetime regression.
        /// </summary>
        private sealed class ModelProvider : IMachineryModelProvider
        {
            /// <inheritdoc/>
            public int Order => 100;

            /// <inheritdoc/>
            public ArrayOf<string> NamespaceUris => MachineToolModels.GetNamespaceUris(MachineToolParts.MachineTool);

            /// <inheritdoc/>
            public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
            {
                nodes.AddMachineToolTypeSystem(context);
                context.EncodeableFactory.Builder.AddMachineToolTypeSystem().Commit();
            }
        }

        /// <summary>Metadata for the provider's single immutable file.</summary>
        private static readonly FileSystemEntry s_fileEntry = new(
            "program.nc", "program.nc", false, 3, false,
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), "application/octet-stream");
    }
}
