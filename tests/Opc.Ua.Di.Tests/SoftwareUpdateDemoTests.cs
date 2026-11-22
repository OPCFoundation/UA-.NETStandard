// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Client;
using Opc.Ua.Di.Server.SoftwareUpdate;
using Pumps;

namespace Opc.Ua.Di.Tests
{
    [TestFixture]
    [Category("Integration")]
    public sealed class SoftwareUpdateDemoTests
    {
        [Test]
        public async Task UploadedPackageIsVerifiedByDigestBeforeTheSimulatorInstallsAsync()
        {
            var fixture = new DiServerFixture();
            await using (fixture.ConfigureAwait(false))
            {
                await fixture.StartAsync().ConfigureAwait(false);
                var packages = new MemoryPackageStore();
                NodeId deviceId = await SoftwareUpdateDemo.CreateAsync(fixture.Manager, packages).ConfigureAwait(false);
                DeviceState device = fixture.Manager.FindPredefinedNode<DeviceState>(deviceId)!;
                ISystemContext system = fixture.Manager.SystemContext;
                ushort di = fixture.Manager.DiNamespaceIndex;
                var update = (SoftwareUpdateState)device.FindChild(system, new QualifiedName("SoftwareUpdate", di))!;
                var install = (InstallationStateMachineState)update.FindChild(
                    system,
                    new QualifiedName("Installation", di))!;
                var client = new SoftwareUpdateClient(
                    DiInProcessSessionBridge.Build(fixture).Object,
                    update.NodeId,
                    fixture.Manager.Server.Telemetry);
                byte[] content = [2, 4, 6, 8, 10];
                ByteString hash = new(SHA256.HashData(content));
                ArrayOf<Variant> input =
                [
                    Variant.From("urn:ualens:sample:manufacturer"),
                    Variant.From("1.0.0"),
                    Variant.From(ArrayOf<string>.Empty),
                    Variant.From(hash)
                ];
                MethodState method = install.InstallSoftwarePackage!;
                ServiceResult missing = await method.OnCallMethod2Async!(
                    system,
                    method,
                    install.NodeId,
                    input,
                    new List<Variant>(),
                    CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(install.CurrentState!.Value.Text, Is.EqualTo("Error"));

                using var payload = new MemoryStream(content, writable: false);
                SoftwareUpdateUploadResult uploaded = await client.UploadPackageWithResultAsync(
                    payload,
                    "ualens-sample-demo").ConfigureAwait(false);
                Assert.That(uploaded.BytesUploaded, Is.EqualTo(5));
                Assert.That(uploaded.CompletionStateMachine.IsNull, Is.True);
                SoftwarePackage? staged = await packages.GetAsync("ualens-sample-demo").ConfigureAwait(false);
                Assert.That(staged, Is.Not.Null);
                Assert.That(staged!.Hash, Is.EqualTo(CoreUtils.ToHexString(hash.Span.ToArray())));
                Assert.That(install.CurrentState!.Value.Text, Is.EqualTo("Error"));

                MethodState resume = install.Resume!;
                ServiceResult resumed = await resume.OnCallMethod2Async!(
                    system,
                    resume,
                    install.NodeId,
                    ArrayOf<Variant>.Empty,
                    new List<Variant>(),
                    CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(resumed.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(install.CurrentState!.Value.Text, Is.EqualTo("Idle"));
                Assert.That(install.LastTransition!.Value.Text, Is.EqualTo("ErrorToIdle"));

                ServiceResult installed = await method.OnCallMethod2Async!(
                    system,
                    method,
                    install.NodeId,
                    input,
                    new List<Variant>(),
                    CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(installed.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(install.CurrentState!.Value.Text, Is.EqualTo("Idle"));
                Assert.That(install.LastTransition!.Value.Text, Is.EqualTo("InstallingToIdle"));

                using var replacement = new MemoryStream(new byte[] { 99 });
                await packages.AddAsync(staged, replacement).ConfigureAwait(false);
                ServiceResult changed = await method.OnCallMethod2Async!(
                    system,
                    method,
                    install.NodeId,
                    input,
                    new List<Variant>(),
                    CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(changed.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(install.CurrentState!.Value.Text, Is.EqualTo("Error"));
            }
        }
    }
}
