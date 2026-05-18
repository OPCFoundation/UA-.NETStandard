/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// End-to-end tests that exercise the client-side
    /// <see cref="FileSystemClient"/> against a real reference server
    /// configured with a
    /// <see cref="PhysicalFileSystemProvider"/> rooted at a per-test
    /// temp directory.
    /// </summary>
    /// <remarks>
    /// One <c>ServerFixture</c> + <c>ClientFixture</c> + temp-dir
    /// provider is created per test (via <c>[SetUp]</c>) so each test
    /// gets a clean slate. Tests are tagged <c>Integration</c> +
    /// <c>FileSystem</c> for selective filtering.
    /// </remarks>
    [TestFixture]
    [Category("Client")]
    [Category("FileSystem")]
    [Category("Integration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class FileSystemClientIntegrationTests
    {
        private ServerFixture<ReferenceServer> m_serverFixture;
        private ClientFixture m_clientFixture;
        private ReferenceServer m_server;
        private ISession m_session;
        private string m_pkiRoot;
        private string m_providerRoot;
        private ITelemetryContext m_telemetry;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            m_providerRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(m_providerRoot);

            m_serverFixture = new ServerFixture<ReferenceServer>(
                t => new ReferenceServer(t)
                {
                    EnableFileSystemNodeManager = true,
                    FileSystemProvider = new PhysicalFileSystemProvider(
                        m_providerRoot,
                        mountName: "TestRoot")
                })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = false,
                OperationLimits = true
            };

            await m_serverFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_server = await m_serverFixture.StartAsync().ConfigureAwait(false);

            m_clientFixture = new ClientFixture(false, false, m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);

            string url = $"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}";
            m_session = await m_clientFixture
                .ConnectAsync(new Uri(url), SecurityPolicies.None)
                .ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            try
            {
                if (m_session != null)
                {
                    await m_session.CloseAsync().ConfigureAwait(false);
                    m_session.Dispose();
                }
            }
            catch
            {
            }

            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
            m_clientFixture?.Dispose();

            TryDeleteDirectory(m_pkiRoot);
            TryDeleteDirectory(m_providerRoot);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }

        private static readonly string[] s_rootListing = ["a.txt", "b.txt", "sub"];
        private static readonly string[] s_nestedListing = ["inside.txt"];

        private FileSystemClient OpenClient()
        {
            NodeId mountRoot = ResolveMountRoot();
            return new FileSystemClient(m_session, mountRoot);
        }

        private NodeId ResolveMountRoot()
        {
            var browseDescriptions = new BrowseDescription[]
            {
                new()
                {
                    NodeId = ObjectIds.FileSystem,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)NodeClass.Object,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };
            BrowseResponse response = m_session
                .BrowseAsync(default, null, 0, browseDescriptions.ToArrayOf(), default)
                .AsTask().GetAwaiter().GetResult();

            Assert.That(response.Results.Count, Is.EqualTo(1));
            foreach (ReferenceDescription reference in response.Results[0].References)
            {
                if (reference.BrowseName.Name == "TestRoot")
                {
                    return ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);
                }
            }
            throw new AssertionException(
                "FileSystem mount 'TestRoot' was not found under Server.FileSystem.");
        }

        [Test]
        public async Task RoundTripWriteThenReadFileAsync()
        {
            FileSystemClient client = OpenClient();

            UaFileInfo file = await client.Root
                .CreateFileAsync("hello.txt")
                .ConfigureAwait(false);

            byte[] payload = Encoding.UTF8.GetBytes("hello world");
            await file.WriteAllBytesAsync(payload).ConfigureAwait(false);

            byte[] roundTrip = await file.ReadAllBytesAsync().ConfigureAwait(false);
            Assert.That(roundTrip, Is.EqualTo(payload));
        }

        [Test]
        public async Task EnumerateDirectoryListsCreatedEntriesAsync()
        {
            FileSystemClient client = OpenClient();
            await client.Root.CreateFileAsync("a.txt").ConfigureAwait(false);
            await client.Root.CreateFileAsync("b.txt").ConfigureAwait(false);
            UaDirectoryInfo sub = await client.Root
                .CreateSubdirectoryAsync("sub")
                .ConfigureAwait(false);
            await sub.CreateFileAsync("c.txt").ConfigureAwait(false);

            var names = new List<string>();
            await foreach (UaFileSystemInfo info in client.Root.EnumerateAsync().ConfigureAwait(false))
            {
                names.Add(info.Name);
            }
            Assert.That(names, Is.EquivalentTo(s_rootListing));
        }

        [Test]
        public async Task CreateDirectoryThenSubdirectoryAsync()
        {
            FileSystemClient client = OpenClient();
            UaDirectoryInfo first = await client.Root
                .CreateSubdirectoryAsync("level1")
                .ConfigureAwait(false);
            await first.CreateSubdirectoryAsync("level2").ConfigureAwait(false);

            Assert.That(
                Directory.Exists(Path.Combine(m_providerRoot, "level1", "level2")),
                Is.True);

            UaFileSystemInfo found = await client
                .GetInfoAsync("level1/level2").ConfigureAwait(false);
            Assert.That(found, Is.InstanceOf<UaDirectoryInfo>());
        }

        [Test]
        public async Task DeleteFileRemovesFromEnumerationAsync()
        {
            FileSystemClient client = OpenClient();
            UaFileInfo doomed = await client.Root
                .CreateFileAsync("doomed.txt")
                .ConfigureAwait(false);
            await doomed.DeleteAsync().ConfigureAwait(false);

            bool exists = await client.ExistsAsync("doomed.txt").ConfigureAwait(false);
            Assert.That(exists, Is.False);
            Assert.That(File.Exists(Path.Combine(m_providerRoot, "doomed.txt")), Is.False);
        }

        [Test]
        public async Task MoveFileChangesNodeIdAsync()
        {
            FileSystemClient client = OpenClient();
            UaFileInfo src = await client.Root
                .CreateFileAsync("src.txt")
                .ConfigureAwait(false);
            NodeId originalNodeId = src.NodeId;

            UaFileSystemInfo moved = await src
                .MoveToAsync(client.Root, newName: "dest.txt")
                .ConfigureAwait(false);

            Assert.That(moved.NodeId, Is.Not.EqualTo(originalNodeId));
            Assert.That(File.Exists(Path.Combine(m_providerRoot, "dest.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(m_providerRoot, "src.txt")), Is.False);
        }

        [Test]
        public async Task CopyFilePreservesOriginalAsync()
        {
            FileSystemClient client = OpenClient();
            UaFileInfo src = await client.Root
                .CreateFileAsync("orig.txt")
                .ConfigureAwait(false);
            byte[] payload = Encoding.UTF8.GetBytes("payload");
            await src.WriteAllBytesAsync(payload).ConfigureAwait(false);

            await src.CopyToAsync(client.Root, newName: "copy.txt")
                .ConfigureAwait(false);

            Assert.That(File.Exists(Path.Combine(m_providerRoot, "orig.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(m_providerRoot, "copy.txt")), Is.True);
            Assert.That(
                File.ReadAllBytes(Path.Combine(m_providerRoot, "copy.txt")),
                Is.EqualTo(File.ReadAllBytes(Path.Combine(m_providerRoot, "orig.txt"))));
        }

        [Test]
        public async Task LargeFileChunkedReadAsync()
        {
            FileSystemClient client = OpenClient();
            UaFileInfo file = await client.Root
                .CreateFileAsync("big.bin")
                .ConfigureAwait(false);

            // Deterministic test payload generation, not cryptographic
            // material - use the shared UnsecureRandom wrapper.
            var rng = new UnsecureRandom(42);
            byte[] payload = new byte[64 * 1024];
            rng.NextBytes(payload);
            await file.WriteAllBytesAsync(payload).ConfigureAwait(false);

            byte[] roundTrip = await file.ReadAllBytesAsync().ConfigureAwait(false);
            Assert.That(roundTrip, Is.EqualTo(payload));
        }

        [Test]
        public async Task MountBrowseNameMatchesProviderAsync()
        {
            // FileSystemClient stores a synthetic local "FileSystem"
            // BrowseName on Root, so verify the actual mount via a
            // direct Read of the BrowseName attribute.
            FileSystemClient client = OpenClient();
            ArrayOf<ReadValueId> nodesToRead = new[]
            {
                new ReadValueId
                {
                    NodeId = client.Root.NodeId,
                    AttributeId = Attributes.BrowseName
                }
            }.ToArrayOf();
            ReadResponse response = await m_session.ReadAsync(
                null,
                0.0,
                TimestampsToReturn.Neither,
                nodesToRead,
                default).ConfigureAwait(false);
            QualifiedName actual = response.Results[0].GetValue<QualifiedName>(QualifiedName.Null);
            Assert.That(actual.Name, Is.EqualTo("TestRoot"));
        }

        [Test]
        public async Task GetInfoOnMissingPathReturnsNullAsync()
        {
            FileSystemClient client = OpenClient();
            UaFileSystemInfo info = await client
                .GetInfoAsync("does-not-exist.txt")
                .ConfigureAwait(false);
            Assert.That(info, Is.Null);
        }

        [Test]
        public async Task NestedDirectoryEnumerationReturnsCreatedChildrenAsync()
        {
            FileSystemClient client = OpenClient();
            UaDirectoryInfo level1 = await client.Root
                .CreateSubdirectoryAsync("nest1")
                .ConfigureAwait(false);
            UaDirectoryInfo level2 = await level1
                .CreateSubdirectoryAsync("nest2")
                .ConfigureAwait(false);
            await level2.CreateFileAsync("inside.txt").ConfigureAwait(false);

            var names = new List<string>();
            await foreach (UaFileSystemInfo info in level2.EnumerateAsync().ConfigureAwait(false))
            {
                names.Add(info.Name);
            }
            Assert.That(names, Is.EquivalentTo(s_nestedListing));
        }

        [Test]
        public async Task ReadWrittenTextRoundTripsAsync()
        {
            FileSystemClient client = OpenClient();
            UaFileInfo file = await client.Root
                .CreateFileAsync("text.txt")
                .ConfigureAwait(false);
            const string content = "Hello, OPC UA!";
            await file.WriteAllTextAsync(content).ConfigureAwait(false);

            string readBack = await file.ReadAllTextAsync().ConfigureAwait(false);
            Assert.That(readBack, Is.EqualTo(content));
        }

        [Test]
        public async Task ReadOnlyProviderRejectsWriteAsync()
        {
            // Re-mount the same content under a read-only provider by
            // restarting the server stack in this single test.
            await m_session.CloseAsync().ConfigureAwait(false);
            m_session.Dispose();
            await m_serverFixture.StopAsync().ConfigureAwait(false);

            m_serverFixture = new ServerFixture<ReferenceServer>(
                t => new ReferenceServer(t)
                {
                    EnableFileSystemNodeManager = true,
                    FileSystemProvider = new PhysicalFileSystemProvider(
                        m_providerRoot,
                        mountName: "TestRoot",
                        isWritable: false)
                })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = false,
                OperationLimits = true
            };
            await m_serverFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_server = await m_serverFixture.StartAsync().ConfigureAwait(false);

            string url = $"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}";
            m_session = await m_clientFixture
                .ConnectAsync(new Uri(url), SecurityPolicies.None)
                .ConfigureAwait(false);

            FileSystemClient client = OpenClient();
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await client.Root.CreateFileAsync("denied.txt").ConfigureAwait(false));
        }
    }
}
