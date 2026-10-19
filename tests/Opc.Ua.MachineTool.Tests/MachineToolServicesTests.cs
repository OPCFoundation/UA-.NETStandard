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
using System.IO;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Client;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Machinery.Jobs;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Tests;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises configured job and file providers through the published machine builder.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class MachineToolServicesTests
    {
        /// <summary>
        /// A derived companion composes existing Machinery bindings into the same registration and owned lifetime.
        /// </summary>
        [Test]
        public async Task AdditionalMachineryBindingsShareMachineRegistrationAndLifetimeAsync()
        {
            await using var server = new MachineToolRuntimeFixture();
            await server.StartAsync().ConfigureAwait(false);
            var resource = new Mock<IAsyncDisposable>();
            resource.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            NodeId boundRoot = default;
            await using MachineToolRuntime<MachineToolState> machine = await server.Context
                .AddMachineTool("ComposedBindings")
                .WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Services test");
                    identity.SerialNumber = "MT-BINDINGS";
                    identity.ProductInstanceUri = "urn:test:mt-bindings";
                })
                .WithMachinery(builder => builder.WithRegistrationBinding((root, _, _) =>
                {
                    boundRoot = root.NodeId;
                    return new ValueTask<IAsyncDisposable>(resource.Object);
                }))
                .BuildAsync().ConfigureAwait(false);

            Assert.That(boundRoot, Is.EqualTo(machine.NodeId));
            Assert.That(machine.Machinery.State, Is.SameAs(machine.State));
            Assert.That(server.Context.Manager.FindPredefinedNode(machine.NodeId), Is.SameAs(machine.State));
            resource.Verify(value => value.DisposeAsync(), Times.Never);
            await machine.DisposeAsync().ConfigureAwait(false);
            resource.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Selecting modern jobs cannot publish a receiver-only configuration or invoke an instance factory first.
        /// </summary>
        [Test]
        public async Task ModernJobsRequireBothProvidersBeforeCreatingTheMachineAsync()
        {
            await using var server = new MachineToolRuntimeFixture();
            await server.StartAsync().ConfigureAwait(false);
            using var jobs = new InMemoryIsa95JobControlProvider();
            var observations = new InMemoryMachineToolProvider(new MachineToolSnapshot());
            bool created = false;
            MachineToolBuilder<MachineToolState> builder = server.Context.AddMachineTool(
                "MissingResponses",
                (context, parent, name) =>
                {
                    created = true;
                    return context.CreateInstanceOfMachineToolType(parent, name);
                })
                .WithProvider(observations)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Services test");
                    identity.SerialNumber = "MT-SERVICES";
                    identity.ProductInstanceUri = "urn:test:mt-services";
                })
                .WithJobManagement(new MachineToolJobManagementOptions { Receiver = jobs });

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await builder.BuildAsync().ConfigureAwait(false)))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(created, Is.False);
        }

        /// <summary>
        /// Both modern job endpoints use the existing provider and declared nested namespace-qualified path.
        /// </summary>
        [Test]
        public async Task ModernJobsUseDeclaredControlAndResponseObjectsOverTcpAsync()
        {
            await using var server = new MachineToolRuntimeFixture();
            await server.StartAsync().ConfigureAwait(false);
            using var jobs = new InMemoryIsa95JobControlProvider();
            var observations = new InMemoryMachineToolProvider(new MachineToolSnapshot());
            await using MachineToolRuntime<MachineToolState> machine = await server.Context
                .AddMachineTool("CompleteJobs")
                .WithProvider(observations)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Services test");
                    identity.SerialNumber = "MT-JOBS";
                    identity.ProductInstanceUri = "urn:test:mt-jobs";
                })
                .WithJobManagement(new MachineToolJobManagementOptions
                {
                    Receiver = jobs,
                    ResponseProvider = jobs
                }).BuildAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(server.Root, "MachineToolJobs").ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await transport.ConnectAsync(server.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
                JobManagementTypeClient? management = await client.GetJobManagementAsync(machine.NodeId)
                    .ConfigureAwait(false);
                Assert.That(management, Is.Not.Null);
                V2.ISA95JobOrderReceiverObjectTypeClient? receiver = await management!
                    .GetJobOrderControlAsync(telemetry).ConfigureAwait(false);
                V2.ISA95JobResponseProviderObjectTypeClient? responses = await management
                    .GetJobOrderResultsAsync(telemetry).ConfigureAwait(false);
                Assert.That(receiver, Is.Not.Null);
                Assert.That(responses, Is.Not.Null);
                ulong stored = await receiver!.StoreAsync(
                    new V2.ISA95JobOrderDataType { JobOrderID = "WireJob" }, []).ConfigureAwait(false);
                Assert.That(stored, Is.EqualTo(Isa95JobReturnStatus.Success));
                ArrayOf<V2.ISA95JobOrderAndStateDataType> orders = await jobs.GetJobOrdersV2Async().ConfigureAwait(false);
                Assert.That(orders.Count, Is.EqualTo(1));
                Assert.That(orders[0].JobOrder!.JobOrderID, Is.EqualTo("WireJob"));
                Isa95JobResponseReceiptV2 accepted = await jobs.ReceiveJobResponseAsync(new V2.ISA95JobResponseDataType
                {
                    JobResponseID = "WireResponse",
                    JobOrderID = "WireJob",
                    JobState = orders[0].State
                }).ConfigureAwait(false);
                Assert.That(accepted.ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));
                (V2.ISA95JobResponseDataType response, ulong status) = await responses!
                    .RequestJobResponseByJobOrderIDAsync("WireJob")
                    .ConfigureAwait(false);
                Assert.That(status, Is.EqualTo(Isa95JobReturnStatus.Success));
                Assert.That(response.JobResponseID, Is.EqualTo("WireResponse"));
            }
            finally
            {
                await session.CloseAsync(default).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// FileSystem and WorkMasters reuse standard file bindings and keep the declared browse-name namespaces.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FileSystemAndWorkMastersUseOwnedStandardBindingsOverTcpAsync(bool workMasters)
        {
            await using var server = new MachineToolRuntimeFixture();
            await server.StartAsync().ConfigureAwait(false);
            var files = new PhysicalFileSystemProvider(Path.Combine(server.Root, "content"));
            if (workMasters)
            {
                await files.CreateDirectoryAsync("WorkMasters", default).ConfigureAwait(false);
            }
            string path = workMasters ? "WorkMasters/program.nc" : "program.nc";
            using (Stream stream = await files.OpenWriteAsync(path, FileWriteMode.Truncate, default)
                .ConfigureAwait(false))
            {
                byte[] content = [1, 3, 5, 7];
#if NETFRAMEWORK
                await stream.WriteAsync(content, 0, content.Length).ConfigureAwait(false);
#else
                await stream.WriteAsync(content.AsMemory()).ConfigureAwait(false);
#endif
            }
            await using MachineToolRuntime<MachineToolState> machine = await server.Context
                .AddMachineTool("Files")
                .WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Services test");
                    identity.SerialNumber = "MT-FILES";
                    identity.ProductInstanceUri = "urn:test:mt-files";
                })
                .WithFileSystem(files, includeWorkMasters: workMasters)
                .BuildAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(server.Root, "MachineToolFiles").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(server.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
                FileDirectoryTypeClient? root = await client.GetFileSystemAsync(machine.NodeId).ConfigureAwait(false);
                FileDirectoryTypeClient? masters = await client.GetWorkMastersAsync(machine.NodeId).ConfigureAwait(false);
                Assert.That(root, Is.Not.Null);
                Assert.That(masters == null, Is.EqualTo(!workMasters));
                NodeId parent = (masters ?? root!).ObjectId;
                var browser = new Browser(session)
                {
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent,
                    NodeClassMask = (uint)NodeClass.Object,
                    ContinueUntilDone = true
                };
                ArrayOf<ReferenceDescription> children = await browser.BrowseAsync(parent).ConfigureAwait(false);
                Assert.That(children.Count, Is.EqualTo(1));
                var file = new FileTypeClient(session,
                    ExpandedNodeId.ToNodeId(children[0].NodeId, session.NamespaceUris), telemetry);
                uint handle = await file.OpenAsync(1).ConfigureAwait(false);
                try
                {
                    ByteString bytes = await file.ReadAsync(handle, 16).ConfigureAwait(false);
                    Assert.That(bytes.ToArray(), Is.EqualTo(new byte[] { 1, 3, 5, 7 }));
                }
                finally
                {
                    await file.CloseAsync(handle).ConfigureAwait(false);
                }
                await files.CreateFileAsync(workMasters ? "WorkMasters/next.nc" : "next.nc", default)
                    .ConfigureAwait(false);
                await machine.RefreshFileSystemAsync().ConfigureAwait(false);
                children = await browser.BrowseAsync(parent).ConfigureAwait(false);
                Assert.That(children.Count, Is.EqualTo(2),
                    "Provider-side changes must be refreshed through the same per-machine file binding.");
            }
            finally
            {
                await session.CloseAsync(default).ConfigureAwait(false);
            }
        }
    }
}
