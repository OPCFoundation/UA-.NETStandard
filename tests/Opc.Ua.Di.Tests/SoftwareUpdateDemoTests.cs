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
                    system, new QualifiedName("Installation", di))!;
                var client = new SoftwareUpdateClient(
                    DiInProcessSessionBridge.Build(fixture).Object, update.NodeId, fixture.Manager.Server.Telemetry);
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
                    system, method, install.NodeId, input, new List<Variant>(), CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(install.CurrentState!.Value.Text, Is.EqualTo("Error"));

                using var payload = new MemoryStream(content, writable: false);
                SoftwareUpdateUploadResult uploaded = await client.UploadPackageWithResultAsync(
                    payload, "ualens-sample-demo").ConfigureAwait(false);
                Assert.That(uploaded.BytesUploaded, Is.EqualTo(5));
                Assert.That(uploaded.CompletionStateMachine.IsNull, Is.True);
                SoftwarePackage? staged = await packages.GetAsync("ualens-sample-demo").ConfigureAwait(false);
                Assert.That(staged, Is.Not.Null);
                Assert.That(staged!.Hash, Is.EqualTo(CoreUtils.ToHexString(hash.Span.ToArray())));

                ServiceResult installed = await method.OnCallMethod2Async!(
                    system, method, install.NodeId, input, new List<Variant>(), CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(installed.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(install.CurrentState!.Value.Text, Is.EqualTo("Idle"));
                Assert.That(install.LastTransition!.Value.Text, Is.EqualTo("InstallingToIdle"));

                using var replacement = new MemoryStream(new byte[] { 99 });
                await packages.AddAsync(staged, replacement).ConfigureAwait(false);
                ServiceResult changed = await method.OnCallMethod2Async!(
                    system, method, install.NodeId, input, new List<Variant>(), CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(changed.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(install.CurrentState!.Value.Text, Is.EqualTo("Error"));
            }
        }
    }
}
