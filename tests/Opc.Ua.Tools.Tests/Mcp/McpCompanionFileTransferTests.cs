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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Proves file confinement, byte-limit boundaries, no-overwrite and cleanup through the public stream seam.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class McpCompanionFileTransferTests
    {
        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(Path.GetTempPath(), $"companion-transfer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(m_root);
            m_options = new OpcUaMcpOptions { TransferRoot = m_root, MaxTransferBytes = 4 };
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(m_root, recursive: true);
        }

        /// <summary>
        /// Upload returns actual bytes at empty, interior and exact-limit boundaries.
        /// </summary>
        [TestCase(0)]
        [TestCase(3)]
        [TestCase(4)]
        public async Task UploadPreservesBytesAtBoundariesAsync(int length)
        {
            byte[] expected = Enumerable.Range(0, length).Select(value => (byte)(value + 1)).ToArray();
            await File.WriteAllBytesAsync(Path.Combine(m_root, "source.bin"), expected).ConfigureAwait(false);
            using var received = new MemoryStream();
            var transfers = new McpFileTransfers(m_options);

            JsonObject result = await transfers.UploadAsync("source.bin", async (source, ct) =>
            {
                await source.CopyToAsync(received, ct).ConfigureAwait(false);
                return received.Length;
            }).ConfigureAwait(false);

            Assert.That(received.ToArray(), Is.EqualTo(expected));
            Assert.That(result["bytesTransferred"]!.GetValue<long>(), Is.EqualTo(length));
            Assert.That(result["filePath"]!.GetValue<string>(), Is.EqualTo("source.bin"));
        }

        /// <summary>
        /// Download publishes only the exact completed payload.
        /// </summary>
        [TestCase(0)]
        [TestCase(3)]
        [TestCase(4)]
        public async Task DownloadPublishesBytesAtBoundariesAsync(int length)
        {
            byte[] expected = Enumerable.Range(0, length).Select(value => (byte)(value + 1)).ToArray();
            var transfers = new McpFileTransfers(m_options);

            JsonObject result = await transfers.DownloadAsync("result.bin", async (target, ct) =>
            {
                await target.WriteAsync(expected, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.That(await File.ReadAllBytesAsync(Path.Combine(m_root, "result.bin")).ConfigureAwait(false),
                Is.EqualTo(expected));
            Assert.That(result["bytesTransferred"]!.GetValue<long>(), Is.EqualTo(length));
            Assert.That(Directory.GetFiles(m_root), Has.Length.EqualTo(1));
        }

        /// <summary>
        /// A known oversize source cannot invoke a remote upload.
        /// </summary>
        [Test]
        public async Task UploadRejectsOversizeBeforeCallbackAsync()
        {
            await File.WriteAllBytesAsync(Path.Combine(m_root, "source.bin"), [1, 2, 3, 4, 5]).ConfigureAwait(false);
            bool invoked = false;
            var transfers = new McpFileTransfers(m_options);

            Assert.That(async () => await transfers.UploadAsync("source.bin", (_, _) =>
            {
                invoked = true;
                return ValueTask.FromResult(5L);
            }).ConfigureAwait(false), Throws.TypeOf<IOException>().With.Message.Contains("limit"));
            Assert.That(invoked, Is.False);
        }

        /// <summary>
        /// Crossing the limit by one byte leaves neither destination nor partial file.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void DownloadRejectsOversizeAndCleansPartialFile(bool synchronous)
        {
            var transfers = new McpFileTransfers(m_options);
            Assert.That(async () => await transfers.DownloadAsync("result.bin", async (target, ct) =>
            {
                await target.WriteAsync(new byte[] { 1, 2, 3, 4 }, ct).ConfigureAwait(false);
                if (synchronous)
                {
                    target.WriteByte(5);
                }
                else
                {
                    await target.WriteAsync(new byte[] { 5 }, ct).ConfigureAwait(false);
                }
            }).ConfigureAwait(false), Throws.TypeOf<IOException>().With.Message.Contains("limit"));
            Assert.That(Directory.GetFiles(m_root), Is.Empty);
        }

        /// <summary>
        /// Missing host policy cannot expose local files through the callback.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void TransfersRequireExplicitRoot(string? root)
        {
            m_options.TransferRoot = root;
            bool invoked = false;
            var transfers = new McpFileTransfers(m_options);
            Assert.That(async () => await transfers.DownloadAsync("x", (_, _) =>
            {
                invoked = true;
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false), Throws.InvalidOperationException.With.Message.Contains("disabled"));
            Assert.That(invoked, Is.False);
        }

        /// <summary>
        /// Invalid byte limits fail before any file is opened.
        /// </summary>
        [TestCase(0)]
        [TestCase(-1)]
        public void TransfersRejectInvalidBytePolicy(long limit)
        {
            m_options.MaxTransferBytes = limit;
            bool invoked = false;
            var transfers = new McpFileTransfers(m_options);
            Assert.That(async () => await transfers.DownloadAsync("x", (_, _) =>
            {
                invoked = true;
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false), Throws.InvalidOperationException.With.Message.Contains("MaxTransferBytes"));
            Assert.That(invoked, Is.False);
            Assert.That(Directory.GetFiles(m_root), Is.Empty);
        }

        /// <summary>
        /// Relative and absolute escapes are rejected for both directions.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TransfersRejectEscapingPaths(bool absolute)
        {
            string escape = absolute
                ? Path.Combine(Path.GetDirectoryName(m_root)!, "outside.bin")
                : Path.Combine("..", "outside.bin");
            bool invoked = false;
            var transfers = new McpFileTransfers(m_options);
            Assert.That(async () => await transfers.UploadAsync(escape, (_, _) =>
            {
                invoked = true;
                return ValueTask.FromResult(0L);
            }).ConfigureAwait(false), Throws.ArgumentException.With.Message.Contains("TransferRoot"));
            Assert.That(async () => await transfers.DownloadAsync(escape, (_, _) =>
            {
                invoked = true;
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false), Throws.ArgumentException.With.Message.Contains("TransferRoot"));
            Assert.That(invoked, Is.False);
        }

        /// <summary>
        /// Windows alternate data streams cannot bypass the regular-file transfer policy.
        /// </summary>
        [Test]
        [Platform("Win")]
        public void TransfersRejectWindowsAlternateDataStreams()
        {
            bool invoked = false;
            var transfers = new McpFileTransfers(m_options);
            Assert.That(async () => await transfers.UploadAsync("file.bin:stream", (_, _) =>
            {
                invoked = true;
                return ValueTask.FromResult(0L);
            }).ConfigureAwait(false), Throws.ArgumentException.With.Message.Contains("TransferRoot"));
            Assert.That(async () => await transfers.DownloadAsync("file.bin:stream", (_, _) =>
            {
                invoked = true;
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false), Throws.ArgumentException.With.Message.Contains("TransferRoot"));
            Assert.That(invoked, Is.False);
            Assert.That(Directory.GetFiles(m_root), Is.Empty);
        }

        /// <summary>
        /// Links and Windows junctions are rejected both as the configured root and inside it.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task TransfersRejectLinkedRootOrPathAsync(bool rootIsLink)
        {
            string target = Path.Combine(m_root, "target");
            string link = Path.Combine(m_root, "link");
            Directory.CreateDirectory(target);
            if (OperatingSystem.IsWindows())
            {
                var start = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                start.ArgumentList.Add("/c");
                start.ArgumentList.Add("mklink");
                start.ArgumentList.Add("/J");
                start.ArgumentList.Add(link);
                start.ArgumentList.Add(target);
                using Process process = Process.Start(start) ??
                    throw new InvalidOperationException("Unable to create the test junction.");
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync().ConfigureAwait(false);
                string details = await output.ConfigureAwait(false) + await error.ConfigureAwait(false);
                Assert.That(process.ExitCode, Is.Zero, details);
            }
            else
            {
                Directory.CreateSymbolicLink(link, target);
            }
            try
            {
                m_options.TransferRoot = rootIsLink ? link : m_root;
                string path = rootIsLink ? "data.bin" : Path.Combine("link", "data.bin");
                bool invoked = false;
                var transfers = new McpFileTransfers(m_options);
                Assert.That(async () => await transfers.DownloadAsync(path, (_, _) =>
                {
                    invoked = true;
                    return ValueTask.CompletedTask;
                }).ConfigureAwait(false), Throws.ArgumentException.With.Message.Contains("links"));
                Assert.That(async () => await transfers.UploadAsync(path, (_, _) =>
                {
                    invoked = true;
                    return ValueTask.FromResult(0L);
                }).ConfigureAwait(false), Throws.ArgumentException.With.Message.Contains("links"));
                Assert.That(invoked, Is.False);
                Assert.That(Directory.GetFiles(target), Is.Empty);
            }
            finally
            {
                Directory.Delete(link);
            }
        }

        /// <summary>
        /// Existing content is never overwritten by a download, even after a race at publication.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DownloadsNeverOverwriteAsync(bool createdDuringDownload)
        {
            string destination = Path.Combine(m_root, "existing.bin");
            if (!createdDuringDownload)
            {
                await File.WriteAllBytesAsync(destination, [9]).ConfigureAwait(false);
            }
            bool invoked = false;
            var transfers = new McpFileTransfers(m_options);
            Assert.That(async () => await transfers.DownloadAsync("existing.bin", async (target, ct) =>
            {
                invoked = true;
                await target.WriteAsync(new byte[] { 1 }, ct).ConfigureAwait(false);
                await File.WriteAllBytesAsync(destination, [9], ct).ConfigureAwait(false);
            }).ConfigureAwait(false), Throws.TypeOf<IOException>());

            Assert.That(invoked, Is.EqualTo(createdDuringDownload));
            Assert.That(await File.ReadAllBytesAsync(destination).ConfigureAwait(false), Is.EqualTo(new byte[] { 9 }));
            Assert.That(Directory.GetFiles(m_root), Has.Length.EqualTo(1));
        }

        /// <summary>
        /// Cancellation and callback failures remove the temporary result and propagate the failure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void DownloadFailuresRemoveTemporaryFiles(bool cancellation)
        {
            var transfers = new McpFileTransfers(m_options);
            using var cancelled = new CancellationTokenSource();
            Func<Task> download = async () => await transfers.DownloadAsync("result.bin", async (target, ct) =>
            {
                await target.WriteAsync(new byte[] { 1 }, ct).ConfigureAwait(false);
                if (cancellation)
                {
                    cancelled.Cancel();
                    ct.ThrowIfCancellationRequested();
                }
                throw new InvalidOperationException("remote refused download");
            }, cancelled.Token).ConfigureAwait(false);
            if (cancellation)
            {
                Assert.That(download, Throws.InstanceOf<OperationCanceledException>());
            }
            else
            {
                Assert.That(download, Throws.InvalidOperationException.With.Message.EqualTo("remote refused download"));
            }
            Assert.That(Directory.GetFiles(m_root), Is.Empty);
        }

        /// <summary>
        /// An upload must consume the complete source and accurately report its consumption.
        /// </summary>
        [TestCase(0)]
        [TestCase(4)]
        public async Task UploadRejectsIncompleteOrMisreportedConsumptionAsync(long reported)
        {
            await File.WriteAllBytesAsync(Path.Combine(m_root, "source.bin"), [1, 2, 3, 4]).ConfigureAwait(false);
            var transfers = new McpFileTransfers(m_options);
            Assert.That(async () => await transfers.UploadAsync("source.bin", async (source, ct) =>
            {
                _ = await source.ReadAsync(new byte[1], ct).ConfigureAwait(false);
                return reported;
            }).ConfigureAwait(false), Throws.TypeOf<IOException>().With.Message.Contains("complete source"));
            using FileStream probe = File.Open(Path.Combine(m_root, "source.bin"),
                FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.That(probe.Length, Is.EqualTo(4));
        }

        /// <summary>
        /// External cancellation before entry cannot create a temporary artifact.
        /// </summary>
        [Test]
        public void PreCancelledDownloadDoesNotInvokeCallback()
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            bool invoked = false;
            var transfers = new McpFileTransfers(m_options);
            Assert.That(async () => await transfers.DownloadAsync("x", (_, _) =>
            {
                invoked = true;
                return ValueTask.CompletedTask;
            }, cancelled.Token).ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(invoked, Is.False);
            Assert.That(Directory.GetFiles(m_root), Is.Empty);
        }

        private string m_root = null!;
        private OpcUaMcpOptions m_options = null!;
    }
}
#endif
