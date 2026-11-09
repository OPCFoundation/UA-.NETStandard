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

extern alias PumpUpdateSample;

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.SoftwareUpdate;
using Opc.Ua.Server.Hosting;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using UaLens.Samples;
using Di = Opc.Ua.Di;
using PumpUpdateSimulator = PumpUpdateSample::Pumps.SoftwareUpdateDemo;

namespace UaLens.Tests.Samples
{
    public sealed partial class RepositorySampleLiveTests
    {
        /// <summary>
        /// Runs the actual package simulator and controlled DI preparation through authorized secure workspaces.
        /// </summary>
        [Test]
        [Explicit("Hosts private-PKI, nonphysical DI package and abort/resume workflows.")]
        [Category("RepositorySampleProbe")]
        public async Task PrivateDiUpdatePinsBytesObservesCompletionAndAbortsOnlyOwnedPreparationAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "UaLens-owned-update-" + Guid.NewGuid());
            string serverPki = Path.Combine(root, "server");
            Directory.CreateDirectory(root);
            using var port = new TcpListener(IPAddress.Loopback, 0);
            port.Start();
            int number = ((IPEndPoint)port.LocalEndpoint).Port;
            port.Stop();
            string endpointUrl = $"opc.tcp://localhost:{number}/UaLensOwnedCompanion";
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var commitEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var prepareEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releasePrepare = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var packages = new MemoryPackageStore();
            var delayed = new Mock<ISoftwarePackageStore>(MockBehavior.Strict);
            delayed.Setup(store => store.AddAsync(It.IsAny<SoftwarePackage>(), It.IsAny<Stream>(),
                It.IsAny<CancellationToken>())).Returns(
                async (SoftwarePackage metadata, Stream bytes, CancellationToken token) =>
                {
                    commitEntered.TrySetResult();
                    await releaseCommit.Task.WaitAsync(token).ConfigureAwait(false);
                    return await packages.AddAsync(metadata, bytes, token).ConfigureAwait(false);
                });
            delayed.Setup(store => store.ListAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => packages.ListAsync(token));
            delayed.Setup(store => store.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string id, CancellationToken token) => packages.OpenReadAsync(id, token));
            int preparationCount = 0;
            NodeId sample = NodeId.Null;
            NodeId controlled = NodeId.Null;
            var started = new OwnedCompanionStartup();
            using IHost host = CreatePrivateCompanionHost(false, serverPki, endpointUrl, started, server =>
                server.AddOpcUaDi().ConfigureDevicesFor<DiNodeManager>(async setup =>
                {
                    sample = await PumpUpdateSimulator.CreateAsync(
                        setup.Manager, delayed.Object, setup.CancellationToken).ConfigureAwait(false);
                    IDeviceBuilder<Di.DeviceState> device = await setup.Manager.CreateDeviceAsync(
                        new QualifiedName("ControlledPreparation", setup.Manager.InstanceNamespaceIndex),
                        cancellationToken: setup.CancellationToken).ConfigureAwait(false);
                    controlled = device.Device.NodeId;
                    device.WithSoftwareUpdate(packages, update => update.OnPrepare(async (_, token) =>
                    {
                        if (Interlocked.Increment(ref preparationCount) == 1)
                        {
                            prepareEntered.TrySetResult();
                            await releasePrepare.Task.WaitAsync(token).ConfigureAwait(false);
                        }
                    }));
                }));
            try
            {
                await host.StartAsync(deadline.Token).ConfigureAwait(false);
                await started.Ready.WaitAsync(deadline.Token).ConfigureAwait(false);
                ITelemetryContext telemetry = host.Services.GetRequiredService<ITelemetryContext>();
                await ExercisePrivateCompanionAsync(telemetry, root, serverPki, endpointUrl,
                    (_, _) => Task.CompletedTask, async (context, _, _, token) =>
                    {
                        var samples = new Mock<IRepositorySampleService>(MockBehavior.Strict);
                        EndpointDescription endpoint = context.Session.Endpoint;
                        var snapshot = new RepositorySampleSnapshot(
                            RepositorySampleId.PumpSoftwareUpdateSimulator,
                            RepositorySamplePhase.AdvertisedReady, "Owned in-process DI simulation")
                        {
                            RunId = Guid.NewGuid(),
                            OwnsResources = true,
                            CompanionOperationsAuthorized = true,
                            CompanionOperationsExpireAt = DateTimeOffset.UtcNow.AddMinutes(1),
                            Evidence = new RepositorySampleEvidence(new Uri(endpoint.EndpointUrl!),
                                endpoint.Server.ApplicationUri!, "UaLensOwnedCompanion",
                                endpoint.Server.ProductUri!,
                                ByteString.From(SHA256.HashData(endpoint.ServerCertificate.Span)))
                        };
                        samples.SetupGet(value => value.Snapshot).Returns(() => snapshot);
                        var policy = new RepositorySampleCompanionPolicy(samples.Object);
                        var provider = new DeviceCompanionProvider();
                        var workspace = new CompanionWorkspace([provider], telemetry, deploymentPolicy: policy);
                        await using (workspace.ConfigureAwait(false))
                        {
                            await workspace.BindAsync(context.Session, token).ConfigureAwait(false);
                            ArrayOf<CompanionTarget> targets = await workspace.DiscoverAsync("di", token)
                                .ConfigureAwait(false);
                            NodeId updateId = await IndustrialCompanionAccess.ResolveChildAsync(context, sample,
                                Di.Namespaces.OpcUaDi, "SoftwareUpdate", false, token).ConfigureAwait(false);
                            CompanionTarget target = targets.ToList().Single(value => value.NodeId == updateId);
                            await ExecuteOwnedDiTaskAsync(workspace, target, "prepare-sample", [], token)
                                .ConfigureAwait(false);
                            await RequireDiStateAsync(
                                workspace, target, "PrepareForUpdate", "PreparedForUpdate", token)
                                .ConfigureAwait(false);

                            byte[] payload = Enumerable.Range(0, 16403).Select(value => (byte)(value % 251)).ToArray();
                            string path = Path.Combine(root, "owned-package.bin");
                            const string storedPackageId = "ualens-sample-owned-package";
                            await File.WriteAllBytesAsync(path, payload, token).ConfigureAwait(false);
                            string digest = Convert.ToHexString(SHA256.HashData(payload));
                            await workspace.InspectAsync(target, token).ConfigureAwait(false);
                            CompanionOperationDraft upload = await workspace.PrepareTaskAsync(target,
                                "upload-package-sample",
                                [
                                    new("path", Variant.From(path)),
                                    new("packageId", Variant.From("owned-package")),
                                    new("sha256", Variant.From(digest))
                                ], token).ConfigureAwait(false);
                            byte[] changedPackage = [99];
                            await File.WriteAllBytesAsync(path, changedPackage, token).ConfigureAwait(false);
                            Task<CompanionOperationResult> uploading = workspace.ExecuteTaskAsync(
                                upload, false, true, null, token);
                            try
                            {
                                Task first = await Task.WhenAny(commitEntered.Task, uploading).WaitAsync(token)
                                    .ConfigureAwait(false);
                                if (ReferenceEquals(first, uploading))
                                {
                                    await uploading.ConfigureAwait(false);
                                    Assert.Fail("Upload completed before the owned delayed commit was entered.");
                                }
                                Assert.That(uploading.IsCompleted, Is.False);
                                Assert.That(await packages.ExistsAsync(storedPackageId, token).ConfigureAwait(false),
                                    Is.False);
                            }
                            finally
                            {
                                releaseCommit.TrySetResult();
                            }
                            CompanionOperationResult uploaded = await uploading.ConfigureAwait(false);
                            Assert.That(Value(uploaded, "Package bytes").TryGetValue(out long bytes), Is.True);
                            Assert.That(bytes, Is.EqualTo(payload.Length));
                            Assert.That(Value(uploaded, "Package id").TryGetValue(out string packageId), Is.True);
                            Assert.That(packageId, Is.EqualTo(storedPackageId));
                            using (Stream source = await packages.OpenReadAsync(storedPackageId, token)
                                .ConfigureAwait(false))
                            {
                                using var copy = new MemoryStream();
                                await source.CopyToAsync(copy, token).ConfigureAwait(false);
                                Assert.That(copy.ToArray(), Is.EqualTo(payload));
                            }
                            await RequireDiStateAsync(workspace, target, "Installation", "Idle", token)
                                .ConfigureAwait(false);
                            await Assert.ThatAsync(() => workspace.ExecuteTaskAsync(upload, true, true, null, token),
                                Throws.InvalidOperationException).ConfigureAwait(false);

                            await ExecuteOwnedDiTaskAsync(workspace, target, "install-package-sample",
                            [
                                new("manufacturer", Variant.From("urn:ualens:owned:manufacturer")),
                                new("revision", Variant.From("1.0.0")),
                                new("patches", Variant.From(string.Empty)),
                                new("sha256", Variant.From(digest))
                            ], token).ConfigureAwait(false);
                            await RequireDiStateAsync(workspace, target, "Installation", "Idle", token)
                                .ConfigureAwait(false);
                            await RequireDiStateAsync(workspace, target, "Confirmation", "NotWaitingForConfirm", token)
                                .ConfigureAwait(false);
                            await ExecuteOwnedDiTaskAsync(workspace, target, "confirm-sample", [], token)
                                .ConfigureAwait(false);
                            await RequireDiStateAsync(workspace, target, "Confirmation", "NotWaitingForConfirm", token)
                                .ConfigureAwait(false);
                            await ExecuteOwnedDiTaskAsync(workspace, target, "resume-prepare-sample", [], token)
                                .ConfigureAwait(false);
                            await RequireDiStateAsync(workspace, target, "PrepareForUpdate", "Idle", token)
                                .ConfigureAwait(false);

                            NodeId controlledUpdate = await IndustrialCompanionAccess.ResolveChildAsync(
                                context, controlled, Di.Namespaces.OpcUaDi, "SoftwareUpdate", false, token)
                                .ConfigureAwait(false);
                            CompanionTarget control = targets.ToList()
                                .Single(value => value.NodeId == controlledUpdate);
                            await workspace.InspectAsync(control, token).ConfigureAwait(false);
                            CompanionOperationDraft preparation = await workspace.PrepareAsync(
                                control, "prepare-sample", null, token).ConfigureAwait(false);
                            Task preparing = workspace.ExecuteTaskAsync(
                                preparation, false, true, null, token);
                            try
                            {
                                Task first = await Task.WhenAny(prepareEntered.Task, preparing).WaitAsync(token)
                                    .ConfigureAwait(false);
                                if (ReferenceEquals(first, preparing))
                                {
                                    await preparing.ConfigureAwait(false);
                                    Assert.Fail("Preparation completed before the owned delay was entered.");
                                }
                                var aborter = new CompanionWorkspace([provider], telemetry, deploymentPolicy: policy);
                                await using (aborter.ConfigureAwait(false))
                                {
                                    await aborter.BindAsync(context.Session, token).ConfigureAwait(false);
                                    await aborter.DiscoverAsync("di", token).ConfigureAwait(false);
                                    await RequireDiStateAsync(aborter, control, "PrepareForUpdate", "Preparing", token)
                                        .ConfigureAwait(false);
                                    await ExecuteOwnedDiTaskAsync(aborter, control, "abort-prepare-sample", [], token)
                                        .ConfigureAwait(false);
                                    await RequireDiStateAsync(aborter, control, "PrepareForUpdate", "Idle", token)
                                        .ConfigureAwait(false);
                                }
                                await Assert.ThatAsync(() => preparing,
                                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                                        .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);
                            }
                            finally
                            {
                                releasePrepare.TrySetResult();
                                await preparing.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                            }
                            await ExecuteOwnedDiTaskAsync(workspace, control, "prepare-sample", [], token)
                                .ConfigureAwait(false);
                            await ExecuteOwnedDiTaskAsync(workspace, control, "resume-prepare-sample", [], token)
                                .ConfigureAwait(false);
                            await RequireDiStateAsync(workspace, control, "PrepareForUpdate", "Idle", token)
                                .ConfigureAwait(false);
                            snapshot = snapshot with { CompanionOperationsAuthorized = false };
                            await Assert.ThatAsync(
                                () => workspace.PrepareAsync(control, "prepare-sample", null, token),
                                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                        }
                        await RequireOwnedReadAsync(context.Session, token).ConfigureAwait(false);
                    }, deadline.Token).ConfigureAwait(false);
            }
            finally
            {
                releaseCommit.TrySetResult();
                releasePrepare.TrySetResult();
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await host.StopAsync(cleanup.Token).ConfigureAwait(false);
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static async Task<CompanionOperationResult> ExecuteOwnedDiTaskAsync(
            CompanionWorkspace workspace,
            CompanionTarget target,
            string operation,
            ArrayOf<CompanionValue> input,
            CancellationToken token)
        {
            await workspace.InspectAsync(target, token).ConfigureAwait(false);
            CompanionOperationDraft draft = operation == "prepare-sample"
                ? await workspace.PrepareAsync(target, operation, null, token).ConfigureAwait(false)
                : await workspace.PrepareTaskAsync(target, operation, input, token).ConfigureAwait(false);
            return await workspace.ExecuteTaskAsync(draft, false, true, null, token).ConfigureAwait(false);
        }

        private static async Task RequireDiStateAsync(
            CompanionWorkspace workspace,
            CompanionTarget target,
            string facet,
            string expected,
            CancellationToken token)
        {
            CompanionInspection state = await workspace.InspectAsync(target, token).ConfigureAwait(false);
            Assert.That(state.Values.ToList().Single(value => value.Name == facet + " state").Value
                .TryGetValue(out LocalizedText current), Is.True);
            Assert.That(current.Text, Is.EqualTo(expected));
        }
    }
}
