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
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Tools.Tests.McpCompanion.Workflows
{
    /// <summary>
    /// Exercises encrypted local server bindings, file effects, history and actual managed refresh lifecycle.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class WorkflowLiveTests
    {
        /// <summary>
        /// Gets the session manager owned and disposed by the dependency-injection container.
        /// </summary>
        private OpcUaSessionManager Sessions => m_services.GetRequiredService<OpcUaSessionManager>();

        /// <summary>
        /// Starts only a repository reference-server fixture on loopback, with isolated filesystem and PKI roots.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, $"client-workflows-live-{Guid.NewGuid():N}");
            string providerRoot = Path.Combine(m_root, "remote-files");
            Directory.CreateDirectory(providerRoot);
            m_fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry)
            {
                EnableFileSystemNodeManager = true,
                FileSystemProvider = new PhysicalFileSystemProvider(providerRoot, mountName: "WorkflowFiles")
            })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                AutoAccept = true,
                SecurityNone = true,
                AllNodeManagers = true,
                OperationLimits = false
            };
            _ = await m_fixture.StartAsync(Path.Combine(m_root, "server-pki")).ConfigureAwait(false);
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore(new OpcUaMcpOptions
            {
                TransferRoot = m_root,
                Execution = new McpExecutionOptions
                {
                    EnabledCapabilities = [McpCapability.IdentityAdministration],
                    CallerGrants =
                    [
                        new McpCallerGrant
                        {
                            Caller = WorkflowTestContext.Owner,
                            Capabilities = [McpCapability.IdentityAdministration],
                            SessionNames = ["workflow-live"]
                        }
                    ]
                }
            }).AddOpcUaMcpWorkflows();
            services.Configure<OpcUaClientOptions>(options => options.PkiRoot = Path.Combine(m_root, "client-pki"));
            m_services = services.BuildServiceProvider();
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await Sessions.ConnectAsync("workflow-live",
                $"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}", "SignAndEncrypt", "Basic256Sha256",
                "Anonymous", null, null, true, startup.Token).ConfigureAwait(false);
            Assert.That(Sessions.GetSessionInfoOrThrow("workflow-live").Endpoint.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        }

        /// <summary>
        /// Joins hosted cleanup before removing only this fixture's private artifacts.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            if (m_services is not null)
            {
                await m_services.DisposeAsync().ConfigureAwait(false);
            }
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            if (m_root is not null && Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        /// <summary>
        /// Host opt-in is additional to, not a replacement for, encrypted server-side SecurityAdmin authorization.
        /// </summary>
        [Test]
        public async Task EncryptedRoleListingAndServerUserDenialUseRealBindingsAsync()
        {
            RoleWorkflowTools roles = m_services.GetRequiredService<RoleWorkflowTools>();
            JsonElement page = Success(await roles.ListAsync(maxResults: 1, sessionName: "workflow-live")
                .ConfigureAwait(false));
            Assert.That(page.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(page.GetProperty("items")[0].GetProperty("expandedNodeId").GetString(), Is.Not.Empty);
            using IDisposable caller = m_services.GetRequiredService<McpCallerContext>()
                .EnterTrustedCaller(WorkflowTestContext.Owner);
            CallToolResult refused = await m_services.GetRequiredService<UserWorkflowTools>()
                .RemoveAsync("nonexistent-workflow-test-user", "workflow-live").ConfigureAwait(false);
            RemoteModelTestSession.Error(refused, StatusCodes.BadUserAccessDenied);
        }

        /// <summary>
        /// Real FileDirectory/FileType methods implement a confined encrypted create/upload/download/delete cycle.
        /// </summary>
        [Test]
        public async Task EncryptedFileWorkflowStreamsRoundTripWithoutExposingHandlesAsync()
        {
            FileWorkflowTools files = m_services.GetRequiredService<FileWorkflowTools>();
            JsonElement roots = Success(await files.RootsAsync(
                ObjectIds.FileSystem.ToString(), maxDepth: 1, sessionName: "workflow-live").ConfigureAwait(false));
            string directory = roots.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("browseName").GetString() == "WorkflowFiles")
                .GetProperty("nodeId").GetString()!;
            string fileName = $"workflow-{Guid.NewGuid():N}.bin";
            JsonElement created = Success(await files.CreateFileAsync(
                directory, fileName, "workflow-live").ConfigureAwait(false));
            string file = created.GetProperty("created").GetProperty("nodeId").GetString()!;
            byte[] expected = Encoding.UTF8.GetBytes("MCP streamed file through an encrypted repository fixture.");
            string upload = "upload-" + Guid.NewGuid().ToString("N") + ".bin";
            string download = "download-" + Guid.NewGuid().ToString("N") + ".bin";
            await File.WriteAllBytesAsync(Path.Combine(m_root, upload), expected).ConfigureAwait(false);
            try
            {
                JsonElement uploaded = Success(await files.UploadAsync(file, upload, true, "workflow-live")
                    .ConfigureAwait(false));
                Assert.That(uploaded.GetProperty("artifact").GetProperty("bytesTransferred").GetInt64(),
                    Is.EqualTo(expected.Length));
                JsonElement downloaded = Success(await files.DownloadAsync(file, download, "workflow-live")
                    .ConfigureAwait(false));
                Assert.That(downloaded.TryGetProperty("handle", out _), Is.False);
                Assert.That(await File.ReadAllBytesAsync(Path.Combine(m_root, download)).ConfigureAwait(false),
                    Is.EqualTo(expected));
            }
            finally
            {
                _ = Success(await files.DeleteAsync(directory, file, sessionName: "workflow-live")
                    .ConfigureAwait(false));
            }
        }

        /// <summary>
        /// History reads and early continuation release traverse the real service binding.
        /// </summary>
        [Test]
        public async Task EncryptedHistoryReadUsesActualHistorianAndReleasesOwnedCursorAsync()
        {
            ISession session = Sessions.GetSessionOrThrow("workflow-live");
            int index = session.NamespaceUris.GetIndex(Quickstarts.ReferenceServer.Namespaces.ReferenceServer);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            var node = new NodeId("Scalar_Static_Double", checked((ushort)index));
            HistoryWorkflowTools history = m_services.GetRequiredService<HistoryWorkflowTools>();
            using IDisposable caller = m_services.GetRequiredService<McpCallerContext>()
                .EnterTrustedCaller(WorkflowTestContext.Owner);
            JsonElement values = Success(await history.ReadRawAsync(node.ToString(),
                "2000-01-01T00:00:00Z", "2099-01-01T00:00:00Z", maxResults: 2,
                retainCursor: true, sessionName: "workflow-live").ConfigureAwait(false));
            Assert.That(values.GetProperty("items").GetArrayLength(), Is.EqualTo(2));
            Assert.That(values.GetProperty("hasMore").GetBoolean(), Is.True);
            string cursor = values.GetProperty("cursor").GetString()!;
            JsonElement release = Success(await history.ReleaseAsync(cursor).ConfigureAwait(false));
            Assert.That(release.GetProperty("released").GetBoolean(), Is.True);
            RemoteModelTestSession.Error(await history.NextAsync(cursor).ConfigureAwait(false),
                StatusCodes.BadContinuationPointInvalid);
        }

        /// <summary>
        /// A real managed subscription emits refresh markers and remains usable for subsequent observations.
        /// </summary>
        [Test]
        public async Task EncryptedAlarmRefreshUsesReadinessAndLeavesBorrowedStreamUsableAsync()
        {
            AlarmWorkflowTools alarms = m_services.GetRequiredService<AlarmWorkflowTools>();
            JsonElement refresh = Success(await alarms.RefreshAsync(
                durationMilliseconds: 5000, maxResults: 500, sessionName: "workflow-live").ConfigureAwait(false));
            Assert.That(refresh.GetProperty("refreshStarted").GetBoolean(), Is.True);
            Assert.That(refresh.GetProperty("refreshEnded").GetBoolean(), Is.True);
            Assert.That(refresh.GetProperty("stoppedBy").GetString(), Is.EqualTo("refreshEnd"));
            JsonElement observed = Success(await alarms.ObserveAsync(
                durationMilliseconds: 100, maxResults: 5, sessionName: "workflow-live").ConfigureAwait(false));
            Assert.That(observed.GetProperty("stoppedBy").GetString(),
                Is.AnyOf("itemLimit", "durationLimit", "sourceCompleted"));
        }

        /// <summary>
        /// Extracts only a successful, structured tool response.
        /// </summary>
        private static JsonElement Success(CallToolResult result)
        {
            Assert.That(result.IsError, Is.False, result.StructuredContent?.GetRawText());
            return RemoteModelTestSession.Payload(result);
        }

        private ServiceProvider m_services = null!;
        private ServerFixture<ReferenceServer> m_fixture = null!;
        private string m_root = null!;
    }
}
#endif
