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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.SoftwareUpdate;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Tests;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Exercises the software-folder boundary inside a larger physical provider mount.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("SoftwareUpdate")]
    [Category("SecurityRegression")]
    public sealed class SecurityF38SoftwareFolderTests
    {
        /// <summary>
        /// Creates a test-owned directory beneath the test working directory.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.GetFullPath(
                "SecurityF38-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            m_provider = new PhysicalFileSystemProvider(m_directory, isWritable: true);
        }

        /// <summary>
        /// Removes only this test's provider mount.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_directory))
            {
                Directory.Delete(m_directory, recursive: true);
            }
        }

        /// <summary>
        /// Invalid revisions cannot reach the provider through any version operation.
        /// </summary>
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("...")]
        [TestCase(".. ")]
        [TestCase("../SoftwareSibling")]
        [TestCase("..\\SoftwareSibling")]
        [TestCase("1.0/..")]
        [TestCase("1.0\\..")]
        [Category("SecurityRegression")]
        public async Task SecurityF38VersionPathsCannotEscapeSoftwareFolder(string version)
        {
            Dictionary<string, byte[]> sentinels = await SeedAdjacentFilesAsync().ConfigureAwait(false);
            Mock<IFileSystemProvider> recording = CreateRecordingProvider();
            var folder = new FileSystemSoftwareFolder(recording.Object, new NodeId("Device", 2));
            using var payload = new MemoryStream([1, 2, 3]);

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<ArgumentException>(
                    async () => await folder.GetVersionAsync(version).ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentException>(async () =>
                {
                    using Stream stream = await folder.OpenVersionAsync(version).ConfigureAwait(false);
                });
                Assert.ThrowsAsync<ArgumentException>(
                    async () => await folder.RemoveVersionAsync(version).ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentException>(
                    async () => await folder.SetCurrentVersionAsync(version).ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentException>(
                    async () => await folder.AddVersionAsync(Package(version), payload).ConfigureAwait(false));
                Assert.That(recording.Invocations, Is.Empty, "Rejected revisions must not reach storage.");
            });

            await AssertSentinelsAsync(sentinels).ConfigureAwait(false);
            Assert.That(
                await m_provider.GetEntryAsync("/metadata.json", CancellationToken.None).ConfigureAwait(false),
                Is.Null,
                "The default installation write path must not create parent metadata.");
        }

        /// <summary>
        /// A poisoned marker cannot select parent metadata or cause a partially completed removal.
        /// </summary>
        [TestCase(".")]
        [TestCase("..")]
        [TestCase(".. ")]
        [TestCase("../SoftwareSibling")]
        [TestCase("..\\SoftwareSibling")]
        [Category("SecurityRegression")]
        public async Task SecurityF38MarkerCannotSelectParent(string version)
        {
            Dictionary<string, byte[]> sentinels = await SeedAdjacentFilesAsync().ConfigureAwait(false);
            await WriteBytesAsync("/metadata.json", sentinels["/SoftwareSibling/metadata.json"])
                .ConfigureAwait(false);
            sentinels["/metadata.json"] = sentinels["/SoftwareSibling/metadata.json"];
            var trustedFolder = new FileSystemSoftwareFolder(m_provider, new NodeId("Device", 2));
            using var payload = new MemoryStream([4, 5, 6]);
            await trustedFolder.AddVersionAsync(Package("1.0.0"), payload).ConfigureAwait(false);
            sentinels["/Software/1.0.0/payload.bin"] = payload.ToArray();
            sentinels["/Software/1.0.0/metadata.json"] = await ReadBytesAsync("/Software/1.0.0/metadata.json")
                .ConfigureAwait(false);
            sentinels["/Software/.current"] = Encoding.UTF8.GetBytes(version);
            await WriteBytesAsync("/Software/.current", sentinels["/Software/.current"]).ConfigureAwait(false);

            Mock<IFileSystemProvider> recording = CreateRecordingProvider();
            var folder = new FileSystemSoftwareFolder(recording.Object, new NodeId("Device", 2));

            Assert.ThrowsAsync<ArgumentException>(
                async () => await folder.GetCurrentVersionAsync().ConfigureAwait(false));
            Assert.ThrowsAsync<ArgumentException>(
                async () => await folder.RemoveVersionAsync("1.0.0").ConfigureAwait(false));

            recording.Verify(
                p => p.GetEntryAsync("/Software/.current", It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            recording.Verify(
                p => p.OpenReadAsync("/Software/.current", It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            recording.Verify(
                p => p.GetEntryAsync("/Software/1.0.0", It.IsAny<CancellationToken>()),
                Times.Once);
            recording.VerifyGet(p => p.IsWritable, Times.Once);
            recording.VerifyNoOtherCalls();
            await AssertSentinelsAsync(sentinels).ConfigureAwait(false);
        }

        /// <summary>
        /// Provider-supplied entry names are validated before metadata reads or recursive deletion.
        /// </summary>
        [TestCase("..")]
        [TestCase("../SoftwareSibling")]
        [TestCase("..\\SoftwareSibling")]
        [Category("SecurityRegression")]
        public async Task SecurityF38EntryNamesCannotEscapeSoftwareFolder(string name)
        {
            Dictionary<string, byte[]> sentinels = await SeedAdjacentFilesAsync().ConfigureAwait(false);
            var trustedFolder = new FileSystemSoftwareFolder(m_provider, new NodeId("Device", 2));
            using var payload = new MemoryStream([4, 5, 6]);
            await trustedFolder.AddVersionAsync(Package("1.0.0"), payload).ConfigureAwait(false);
            sentinels["/Software/1.0.0/payload.bin"] = payload.ToArray();
            sentinels["/Software/1.0.0/metadata.json"] = await ReadBytesAsync("/Software/1.0.0/metadata.json")
                .ConfigureAwait(false);
            Mock<IFileSystemProvider> recording = CreateRecordingProvider();
            var entry = new FileSystemEntry(name, name, true, 0, true, default, string.Empty);
            recording.Setup(p => p.EnumerateAsync("/Software", It.IsAny<CancellationToken>()))
                .Returns(SingleEntryAsync(entry));
            recording.Setup(p => p.EnumerateAsync("/Software/1.0.0", It.IsAny<CancellationToken>()))
                .Returns(SingleEntryAsync(entry));
            var folder = new FileSystemSoftwareFolder(recording.Object, new NodeId("Device", 2));

            Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await foreach (SoftwarePackage package in folder.ListVersionsAsync())
                {
                    Assert.Fail("An invalid provider name must not produce a package: " + package.Version);
                }
            });
            Assert.ThrowsAsync<ArgumentException>(
                async () => await folder.RemoveVersionAsync("1.0.0").ConfigureAwait(false));

            recording.Verify(
                p => p.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
            recording.Verify(
                p => p.OpenWriteAsync(It.IsAny<string>(), It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()),
                Times.Never);
            await AssertSentinelsAsync(sentinels).ConfigureAwait(false);
        }

        /// <summary>
        /// Configured roots cannot smuggle dot segments past the folder boundary.
        /// </summary>
        [TestCase("/Software/../SoftwareSibling")]
        [TestCase("/Software/.")]
        [TestCase("..\\Software")]
        [TestCase("/SoftwareSibling/..")]
        [Category("SecurityRegression")]
        public void SecurityF38RootRejectsDotSegments(string rootPath)
        {
            var provider = new Mock<IFileSystemProvider>(MockBehavior.Strict);

            Assert.That(
                () => new FileSystemSoftwareFolder(provider.Object, new NodeId("Device", 2), rootPath),
                Throws.ArgumentException);
            Assert.That(provider.Invocations, Is.Empty);
        }

        /// <summary>
        /// Canonical roots and ordinary embedded dots preserve the complete version lifecycle.
        /// </summary>
        [TestCase("/Software/", "/Software/")]
        [TestCase("/Software//Device\\", "/Software/Device/")]
        [TestCase("/", "/")]
        [TestCase("Software", "/Software/")]
        [Category("SecurityRegression")]
        public async Task SecurityF38CanonicalRootsPreserveValidDottedVersions(string rootPath, string prefix)
        {
            Mock<IFileSystemProvider> recording = CreateRecordingProvider();
            var folder = new FileSystemSoftwareFolder(recording.Object, new NodeId("Device", 2), rootPath);
            using var payload = new MemoryStream([3, 1, 4]);
            const string version = "1.0.0-rc..1";

            SoftwarePackage stored = await folder.AddVersionAsync(Package(version), payload).ConfigureAwait(false);
            await folder.SetCurrentVersionAsync(version).ConfigureAwait(false);
            SoftwarePackage? current = await folder.GetCurrentVersionAsync().ConfigureAwait(false);
            SoftwarePackage? fetched = await folder.GetVersionAsync(version).ConfigureAwait(false);
            using var copy = new MemoryStream();
            using (Stream stream = await folder.OpenVersionAsync(version).ConfigureAwait(false))
            {
                await stream.CopyToAsync(copy).ConfigureAwait(false);
            }
            var versions = new List<string>();
            await foreach (SoftwarePackage package in folder.ListVersionsAsync())
            {
                versions.Add(package.Version);
            }

            Assert.Multiple(() =>
            {
                Assert.That(stored.SizeBytes, Is.EqualTo(3));
                Assert.That(current?.Version, Is.EqualTo(version));
                Assert.That(fetched?.Version, Is.EqualTo(version));
                Assert.That(copy.ToArray(), Is.EqualTo(payload.ToArray()));
                Assert.That(versions, Is.EqualTo([version]));
            });
            Assert.That(await folder.RemoveVersionAsync(version).ConfigureAwait(false), Is.True);
            Assert.That(await folder.GetCurrentVersionAsync().ConfigureAwait(false), Is.Null);
            string[] paths = [.. recording.Invocations
                .Where(invocation => invocation.Arguments.Count > 0)
                .Select(invocation => invocation.Arguments[0])
                .OfType<string>()];
            Assert.That(paths.All(path =>
                path.StartsWith(prefix, StringComparison.Ordinal) || path == prefix.TrimEnd('/')), Is.True);
            Assert.That(
                paths.Any(path =>
#if NETFRAMEWORK
                    path.Contains("\\", StringComparison.Ordinal) ||
#else
                    path.Contains('\\', StringComparison.Ordinal) ||
#endif
                    path.Contains("//", StringComparison.Ordinal)),
                Is.False);
        }

        /// <summary>
        /// Calls the real installation binding without a listener, using the default folder handler.
        /// </summary>
        [TestCase(".", false)]
        [TestCase("..", false)]
        [TestCase("2.0.0-rc.1", true)]
        [Category("SecurityRegression")]
        public async Task SecurityF38InstallationKeepsRevisionWithinSoftwareFolder(string revision, bool accepted)
        {
            Dictionary<string, byte[]> sentinels = await SeedAdjacentFilesAsync().ConfigureAwait(false);
            await using DiNodeManager manager = CreateInProcessManager();
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("Devices", manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Devices", manager.InstanceNamespaceIndex)
            };
            IDeviceBuilder<DeviceState> device = await manager.CreateDeviceAsync(
                new QualifiedName("Device", manager.InstanceNamespaceIndex), parent).ConfigureAwait(false);
            var folder = new FileSystemSoftwareFolder(m_provider, device.Device.NodeId);
            device.WithSoftwareUpdate(new MemoryPackageStore(), update => update.WithSoftwareFolder(folder));
            var softwareUpdate = (SoftwareUpdateState)device.Device.FindChild(
                manager.SystemContext, new QualifiedName("SoftwareUpdate", manager.DiNamespaceIndex))!;
            InstallationStateMachineState installation = softwareUpdate.Installation!;
            MethodState method = installation.InstallSoftwarePackage!;
            var inputs = new ArrayOf<Variant>(new[]
            {
                Variant.From("urn:trusted:manufacturer"),
                Variant.From(revision),
                Variant.From(ArrayOf<string>.Empty),
                Variant.From(ByteString.Empty)
            });
            var outputs = new List<Variant>();

            ServiceResult result = await method.OnCallMethod2Async!(
                manager.SystemContext, method, installation.NodeId, inputs, outputs, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.EqualTo(accepted));
            Assert.That(installation.CurrentState!.Value.Text, Is.EqualTo(accepted ? "Idle" : "Error"));
            if (accepted)
            {
                SoftwarePackage? current = await folder.GetCurrentVersionAsync().ConfigureAwait(false);
                Assert.That(current?.Version, Is.EqualTo(revision));
                Assert.That(current?.SizeBytes, Is.Zero);
                Assert.That(
                    await m_provider.GetEntryAsync(
                        "/Software/" + revision + "/payload.bin", CancellationToken.None).ConfigureAwait(false),
                    Is.Not.Null);
            }
            else
            {
                Assert.That(await folder.GetCurrentVersionAsync().ConfigureAwait(false), Is.Null);
            }
            await AssertSentinelsAsync(sentinels).ConfigureAwait(false);
            Assert.That(
                await m_provider.GetEntryAsync("/metadata.json", CancellationToken.None).ConfigureAwait(false),
                Is.Null);
        }

        /// <summary>
        /// Creates a real node manager with local collaborators and no network or certificate store.
        /// </summary>
        private static DiNodeManager CreateInProcessManager()
        {
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.SetupGet(value => value.NodeManager).Returns(Mock.Of<IMasterNodeManager>());
            server.SetupGet(value => value.CoreNodeManager).Returns(Mock.Of<ICoreNodeManager>());
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return new DiNodeManager(server.Object, new ApplicationConfiguration
            {
                ApplicationUri = "urn:opcfoundation:security-f38:tests",
                ServerConfiguration = new ServerConfiguration()
            });
        }

        /// <summary>
        /// Yields a deliberately malformed provider entry.
        /// </summary>
        private static async IAsyncEnumerable<FileSystemEntry> SingleEntryAsync(FileSystemEntry entry)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return entry;
        }

        /// <summary>
        /// Creates a package with an ordinary identifier and caller-controlled revision.
        /// </summary>
        private static SoftwarePackage Package(string version)
        {
            return new SoftwarePackage("package", version, "trusted", string.Empty, 0, default, string.Empty);
        }

        /// <summary>
        /// Records actual storage calls while retaining physical provider behavior.
        /// </summary>
        private Mock<IFileSystemProvider> CreateRecordingProvider()
        {
            var provider = new Mock<IFileSystemProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.IsWritable).Returns(true);
            provider.Setup(value => value.GetEntryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => m_provider.GetEntryAsync(path, ct));
            provider.Setup(value => value.EnumerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => m_provider.EnumerateAsync(path, ct));
            provider.Setup(value => value.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => m_provider.OpenReadAsync(path, ct));
            provider.Setup(value => value.OpenWriteAsync(
                It.IsAny<string>(), It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()))
                .Returns((string path, FileWriteMode mode, CancellationToken ct) =>
                    m_provider.OpenWriteAsync(path, mode, ct));
            provider.Setup(value => value.CreateDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => m_provider.CreateDirectoryAsync(path, ct));
            provider.Setup(value => value.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => m_provider.DeleteAsync(path, ct));
            return provider;
        }

        /// <summary>
        /// Places independent files at the folder root, provider root and a prefix-sharing sibling.
        /// </summary>
        private async Task<Dictionary<string, byte[]>> SeedAdjacentFilesAsync()
        {
            var sibling = new FileSystemSoftwareFolder(m_provider, new NodeId("Sibling", 2), "/");
            using var payload = new MemoryStream([9, 8, 7]);
            await sibling.AddVersionAsync(Package("SoftwareSibling"), payload).ConfigureAwait(false);
            byte[] metadata = await ReadBytesAsync("/SoftwareSibling/metadata.json").ConfigureAwait(false);
            var sentinels = new Dictionary<string, byte[]>
            {
                ["/payload.bin"] = [7, 7, 7],
                ["/Software/payload.bin"] = [6, 6, 6],
                ["/Software/metadata.json"] = metadata,
                ["/SoftwareSibling/payload.bin"] = payload.ToArray(),
                ["/SoftwareSibling/metadata.json"] = metadata
            };
            await m_provider.CreateDirectoryAsync("/Software", CancellationToken.None).ConfigureAwait(false);
            foreach (KeyValuePair<string, byte[]> sentinel in sentinels)
            {
                await WriteBytesAsync(sentinel.Key, sentinel.Value).ConfigureAwait(false);
            }
            return sentinels;
        }

        /// <summary>
        /// Verifies that storage outside version subdirectories is byte-for-byte unchanged.
        /// </summary>
        private async Task AssertSentinelsAsync(Dictionary<string, byte[]> sentinels)
        {
            foreach (KeyValuePair<string, byte[]> sentinel in sentinels)
            {
                Assert.That(
                    await ReadBytesAsync(sentinel.Key).ConfigureAwait(false),
                    Is.EqualTo(sentinel.Value),
                    sentinel.Key);
            }
        }

        /// <summary>
        /// Reads a provider file without native path normalization in the test.
        /// </summary>
        private async Task<byte[]> ReadBytesAsync(string path)
        {
            using Stream stream = await m_provider.OpenReadAsync(path, CancellationToken.None).ConfigureAwait(false);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy).ConfigureAwait(false);
            return copy.ToArray();
        }

        /// <summary>
        /// Writes a complete test-owned provider file.
        /// </summary>
        private async Task WriteBytesAsync(string path, byte[] bytes)
        {
            using Stream stream = await m_provider.OpenWriteAsync(
                path, FileWriteMode.Truncate, CancellationToken.None).ConfigureAwait(false);
#if NETFRAMEWORK
            await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
#else
            await stream.WriteAsync(bytes.AsMemory()).ConfigureAwait(false);
#endif
        }

        /// <summary>The test-owned provider mount path.</summary>
        private string m_directory = string.Empty;

        /// <summary>The real provider shared by the test's two software folders.</summary>
        private PhysicalFileSystemProvider m_provider = null!;
    }
}
