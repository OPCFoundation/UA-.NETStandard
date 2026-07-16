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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Tests.Capture
{
    /// <summary>
    /// Verifies server-side in-process pcap capture against a hosted reference server.
    /// </summary>
    [TestFixture]
    [Category("Pcap")]
    public sealed class ServerCaptureIntegrationTests : ClientTestFramework
    {
        private ChannelCaptureRegistry m_registry = null!;
        private string m_captureRoot = string.Empty;

        /// <summary>
        /// Starts a reference server with the pcap-aware listener binding installed.
        /// </summary>
        [OneTimeSetUp]
        public override async Task OneTimeSetUpAsync()
        {
            m_registry = new ChannelCaptureRegistry();
            var bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
            PcapBindings.InstallServer(bindings, m_registry);
            TransportBindingRegistry = bindings;
            m_captureRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "server-capture-" + Guid.NewGuid().ToString("N"));

            await base.OneTimeSetUpAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the hosted reference server and removes capture artifacts.
        /// </summary>
        [OneTimeTearDown]
        public override async Task OneTimeTearDownAsync()
        {
            try
            {
                await base.OneTimeTearDownAsync().ConfigureAwait(false);
            }
            finally
            {
                TryDeleteCaptureRoot();
            }
        }

        /// <summary>
        /// Captures inbound traffic from a fresh client session into a pcap file.
        /// </summary>
        [Test]
        public async Task InProcessServerCaptureRecordsFreshSessionTrafficAsync()
        {
            await using var manager = new CaptureSessionManager(
                new DefaultCaptureSourceFactory(m_registry),
                m_captureRoot);

            CaptureSession capture = await manager
                .StartAsync(
                    new StartCaptureRequest
                    {
                        Source = CaptureSourceKind.InProcessServer,
                        SessionFolder = m_captureRoot
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            ISession? session = null;
            try
            {
                session = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);

                DataValue value = await session
                    .ReadValueAsync(VariableIds.Server_ServerStatus_CurrentTime, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(value.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                if (session != null)
                {
                    await session.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                    session.Dispose();
                }

                await manager.StopAsync(capture.Id, CancellationToken.None).ConfigureAwait(false);
            }

            string[] pcapFiles = Directory.GetFiles(capture.SessionFolder, "*.pcap");
            long totalBytes = pcapFiles.Sum(file => new FileInfo(file).Length);

            Assert.That(pcapFiles, Is.Not.Empty);
            Assert.That(totalBytes, Is.GreaterThan(24));
        }

        private void TryDeleteCaptureRoot()
        {
            try
            {
                if (Directory.Exists(m_captureRoot))
                {
                    Directory.Delete(m_captureRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                TestContext.Progress.WriteLine($"Unable to delete capture folder '{m_captureRoot}'.");
            }
            catch (UnauthorizedAccessException)
            {
                TestContext.Progress.WriteLine($"Unable to delete capture folder '{m_captureRoot}'.");
            }
        }
    }
}
