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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;

namespace UaLens.Tests.Companions
{
    /// <summary>
    /// Local addressing and a sample checkbox cannot replace configured DI deployment authorization.
    /// </summary>
    [TestFixture]
    public sealed class DeviceUpdateAuthorizationTests
    {
        [Test]
        public async Task UnapprovedLoopbackMutationCannotBypassConfiguredAuthorizationAsync(
            [Values] bool secure,
            [Values("prepare-sample", "upload-package-sample", "install-package-sample",
                "abort-prepare-sample", "resume-prepare-sample", "resume-installation-sample", "confirm-sample")]
            string operation)
        {
            var context = new DeviceUpdateTestContext();
            context.Endpoint.SecurityMode = secure ? MessageSecurityMode.SignAndEncrypt : MessageSecurityMode.None;
            context.Endpoint.SecurityPolicyUri = secure ? SecurityPolicies.Basic256Sha256 : SecurityPolicies.None;
            var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
            policy.Setup(value => value.AuthorizeAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new UnauthorizedAccessException("No approved nonphysical target was configured."));
            var workspace = new CompanionWorkspace([context.Provider], context.Server.Telemetry,
                deploymentPolicy: policy.Object);
            await using (workspace.ConfigureAwait(false))
            {
                await workspace.BindAsync(context.Server.Session.Object).ConfigureAwait(false);
                await workspace.DiscoverAsync("di").ConfigureAwait(false);
                await workspace.InspectAsync(context.Target).ConfigureAwait(false);

                await Assert.ThatAsync(async () =>
                {
                    CompanionOperationDraft draft = operation == "prepare-sample"
                        ? await workspace.PrepareAsync(context.Target, operation, null).ConfigureAwait(false)
                        : await workspace.PrepareTaskAsync(context.Target, operation, context.Inputs(operation))
                            .ConfigureAwait(false);
                    await workspace.ExecuteTaskAsync(draft, true, true, null).ConfigureAwait(false);
                }, Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);

                Assert.That(context.Server.Calls.IsEmpty, Is.True);
                policy.Verify(value => value.AuthorizeAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(),
                    It.IsAny<CancellationToken>()), secure ? Times.Once() : Times.Never());
            }
            context.Server.Session.Verify(session => session.Dispose(), Times.Never);
        }
    }
}
