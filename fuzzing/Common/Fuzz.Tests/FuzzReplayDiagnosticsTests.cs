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
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Verifies private reproducer fidelity and resource bounds without using restricted real-world findings.
    /// </summary>
    [TestFixture]
    public sealed class FuzzReplayDiagnosticsTests
    {
        /// <summary>
        /// Retains exact input bytes at the supported boundary and rejects oversized input before creating storage.
        /// </summary>
        /// <param name="size">The synthetic input size.</param>
        /// <param name="expected">Whether a reproducer should be retained.</param>
        [TestCase(0, true)]
        [TestCase(4096, true)]
        [TestCase(4097, false)]
        public async Task PrivateReproducerPreservesBytesWithinBoundsAsync(int size, bool expected)
        {
            string directory = Workspace();
            try
            {
                var diagnostics = new FuzzReplayDiagnostics(directory);
                byte[] bytes = new byte[size];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = (byte)(i % 251);
                }
                Assert.That(Directory.Exists(directory), Is.False);
                bool written = await diagnostics.TryWriteAsync(bytes, "synthetic restricted detail")
                    .ConfigureAwait(false);
                Assert.That(written, Is.EqualTo(expected));
                Assert.That(Directory.Exists(directory), Is.EqualTo(expected));
                if (expected)
                {
#if !NETFRAMEWORK
                    if (!OperatingSystem.IsWindows())
                    {
                        Assert.That(File.GetUnixFileMode(directory),
                            Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
                    }
#endif
                    Assert.That(File.ReadAllBytes(Path.Combine(directory, "00.bin")), Is.EqualTo(bytes));
                    Assert.That(File.ReadAllText(Path.Combine(directory, "00.txt")),
                        Is.EqualTo("synthetic restricted detail"));
                    Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(2));
                }
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        /// <summary>
        /// Bounds the number of retained inputs and truncates only diagnostic text, never reproducer bytes.
        /// </summary>
        /// <param name="detailLength">The length of the synthetic diagnostic text.</param>
        /// <param name="truncated">Whether the retained text should include a truncation marker.</param>
        [TestCase(65536, false)]
        [TestCase(65537, true)]
        public async Task PrivateReproducerCountAndDetailsAreBoundedAsync(int detailLength, bool truncated)
        {
            string directory = Workspace();
            try
            {
                var diagnostics = new FuzzReplayDiagnostics(directory);
                for (int i = 0; i < 60; i++)
                {
                    Assert.That(await diagnostics.TryWriteAsync([1, 2, 3], new string('x', detailLength))
                        .ConfigureAwait(false), Is.True);
                }
                Assert.That(
                    await diagnostics.TryWriteAsync([4], "must not be written").ConfigureAwait(false),
                    Is.False);
                Assert.That(Directory.GetFiles(directory, "*.bin"), Has.Length.EqualTo(60));
                Assert.That(Directory.GetFiles(directory, "*.txt"), Has.Length.EqualTo(60));
                Assert.That(
                    File.ReadAllText(Path.Combine(directory, "00.txt")),
                    Is.EqualTo(
                        new string('x', 65536) + (truncated ? "\n[diagnostic details truncated]" : string.Empty)));
                Assert.That(File.ReadAllBytes(Path.Combine(directory, "00.bin")), Is.EqualTo(new byte[] { 1, 2, 3 }));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        /// <summary>
        /// Surfaces storage failures without returning success or replacing existing files.
        /// </summary>
        [Test]
        public async Task PrivateReproducerIoFailureIsNotReportedAsSavedAsync()
        {
            string directory = Workspace();
            Directory.CreateDirectory(directory);
            try
            {
                string occupiedPath = Path.Combine(directory, "occupied");
                File.WriteAllText(occupiedPath, "existing content");
                var diagnostics = new FuzzReplayDiagnostics(occupiedPath);
                async Task WriteAsync() =>
                    _ = await diagnostics.TryWriteAsync([1], "synthetic detail").ConfigureAwait(false);
                await Assert.ThatAsync(WriteAsync, Throws.TypeOf<IOException>()).ConfigureAwait(false);
                Assert.That(File.ReadAllText(occupiedPath), Is.EqualTo("existing content"));
                Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string Workspace()
        {
            return Path.Combine(
                TestContext.CurrentContext.TestDirectory, "private-replay-" + Guid.NewGuid().ToString("N"));
        }
    }
}
