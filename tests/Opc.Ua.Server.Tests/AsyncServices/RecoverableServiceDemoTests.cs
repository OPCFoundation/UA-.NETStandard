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
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RecoverableServiceDemo;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Runs the executable sample's real local Server, generated proxy and network interruption.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class RecoverableServiceDemoTests
    {
        /// <summary>
        /// Verifies the long-running result, no replay, original Session and explicit acceptance.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LongRunningMethodKeepsOneInvocationAndAcknowledgesAfterStorage(bool interruptNetwork)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            RecoveryDemoResult result;
            try
            {
                result = await RecoveryDemo.RunAsync(interruptNetwork, output, timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                await TestContext.Out.WriteLineAsync(output.ToString()).ConfigureAwait(false);
                throw;
            }
            Assert.Multiple(() =>
            {
                Assert.That(result.Value, Is.EqualTo(7));
                Assert.That(result.Invocations, Is.EqualTo(1));
                Assert.That(result.SameSession, Is.True);
                Assert.That(result.Acknowledged, Is.True);
                Assert.That(result.NetworkInterrupted, Is.EqualTo(interruptNetwork));
                Assert.That(output.ToString(), Does.Contain("PASS: value=7, invocations=1"));
                if (interruptNetwork)
                {
                    Assert.That(output.ToString(), Does.Contain("Server finished while disconnected"));
                }
                Assert.That(output.ToString(), Does.Contain("acceptance file; acknowledging now"));
            });
        }

        /// <summary>
        /// Binds the bounded real-child restart TEST; Windows peer-PID IPC is not a cross-platform qualification.
        /// The net10 build target stages the sample's own runtime closure in an isolated test-output directory.
        /// </summary>
        [Test]
        [Platform("Win")]
        [SupportedOSPlatform("windows")]
        public async Task RealServerProcessRestartPreservesOwnerOutcomesEffectsAndBoundaries()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            ProcessRestartDemoResult result;
            try
            {
                string childHost = Path.Combine(
                    TestContext.CurrentContext.TestDirectory, "rse-process-host", "RecoverableServiceDemo.exe");
                await TestContext.Out.WriteLineAsync("Restart child host: " + childHost).ConfigureAwait(false);
                await TestContext.Out.WriteLineAsync(
                    "Restart child runtimeconfig: " + Path.ChangeExtension(childHost, ".runtimeconfig.json"))
                    .ConfigureAwait(false);
                result = await ProcessRestartDemo.RunTestAsync(childHost, output, timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                await TestContext.Out.WriteLineAsync(output.ToString()).ConfigureAwait(false);
                throw;
            }
            Assert.Multiple(() =>
            {
                Assert.That(result.OwnerValue, Is.EqualTo(3));
                Assert.That(result.RetainedValue, Is.EqualTo(10));
                Assert.That(result.BusinessValue, Is.EqualTo(21));
                Assert.That(result.Invocations, Is.EqualTo(3));
                Assert.That(result.LaunchedChildren, Is.EqualTo(5));
                Assert.That(result.KilledChildren, Is.EqualTo(4));
                Assert.That(result.HistoryBoundaryVerified, Is.True);
                Assert.That(result.ClockFailureVerified, Is.True);
            });
        }
    }
}
