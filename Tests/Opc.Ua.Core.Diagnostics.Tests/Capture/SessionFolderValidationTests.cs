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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Core.Diagnostics.Capture;
using Opc.Ua.Core.Diagnostics.Models;

using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Diagnostics.Tests.Capture
{
    /// <summary>
    /// Verifies capture session folder validation rejects paths outside the
    /// configured base folder before artifacts are created.
    /// </summary>
    [TestFixture]
    public sealed class SessionFolderValidationTests : TempDirectoryFixture
    {
        /// <summary>
        /// Verifies parent directory traversal cannot escape the base folder.
        /// </summary>
        [Test]
        public async Task StartAsyncRejectsParentTraversal()
        {
            var factory = new RecordingSourceFactory();
            await using var manager = new CaptureSessionManager(factory, TempDirectory);

            Assert.That(
                async () => await manager.StartAsync(
                    new StartCaptureRequest { SessionFolder = Path.Combine("..", "..", "etc") },
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("folder"));
            Assert.That(factory.SessionFolders, Is.Empty);
        }

        /// <summary>
        /// Verifies absolute paths outside the configured base folder are
        /// rejected.
        /// </summary>
        [Test]
        public async Task StartAsyncRejectsAbsolutePathOutsideBaseFolder()
        {
            var factory = new RecordingSourceFactory();
            string outsideFolder = OperatingSystem.IsWindows()
                ? Path.Combine(Path.GetPathRoot(TempDirectory) ?? "C:\\", "Windows", "foo")
                : "/etc/foo";
            await using var manager = new CaptureSessionManager(factory, TempDirectory);

            Assert.That(
                async () => await manager.StartAsync(
                    new StartCaptureRequest { SessionFolder = outsideFolder },
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("folder"));
            Assert.That(factory.SessionFolders, Is.Empty);
        }

        /// <summary>
        /// Verifies relative session folders are resolved beneath the
        /// configured base folder.
        /// </summary>
        [Test]
        public async Task StartAsyncAcceptsRelativeSubFolderWithinBase()
        {
            var factory = new RecordingSourceFactory();
            await using var manager = new CaptureSessionManager(factory, TempDirectory);

            CaptureSession session = await manager.StartAsync(
                new StartCaptureRequest { SessionFolder = "my-session-folder" },
                CancellationToken.None).ConfigureAwait(false);

            string expectedFolder = Path.Combine(Path.GetFullPath(TempDirectory), "my-session-folder");
            Assert.That(session.SessionFolder, Is.EqualTo(expectedFolder));
            Assert.That(session.Request.SessionFolder, Is.EqualTo(expectedFolder));
            Assert.That(factory.SessionFolders, Is.EqualTo([expectedFolder]));
            Assert.That(Directory.Exists(expectedFolder), Is.True);
        }

        /// <summary>
        /// Verifies absolute paths inside the configured base folder remain
        /// valid.
        /// </summary>
        [Test]
        public async Task StartAsyncAcceptsAbsolutePathInsideBase()
        {
            var factory = new RecordingSourceFactory();
            string requestedFolder = Path.Combine(TempDirectory, "subdir");
            await using var manager = new CaptureSessionManager(factory, TempDirectory);

            CaptureSession session = await manager.StartAsync(
                new StartCaptureRequest { SessionFolder = requestedFolder },
                CancellationToken.None).ConfigureAwait(false);

            string expectedFolder = Path.GetFullPath(requestedFolder);
            Assert.That(session.SessionFolder, Is.EqualTo(expectedFolder));
            Assert.That(session.Request.SessionFolder, Is.EqualTo(expectedFolder));
            Assert.That(factory.SessionFolders, Is.EqualTo([expectedFolder]));
            Assert.That(Directory.Exists(expectedFolder), Is.True);
        }

        /// <summary>
        /// Verifies null and empty session folders use the generated
        /// per-session folder beneath the configured base folder.
        /// </summary>
        [Test]
        public async Task StartAsyncAcceptsNullOrEmptySessionFolder()
        {
            var factory = new RecordingSourceFactory();
            await using var manager = new CaptureSessionManager(factory, TempDirectory);

            CaptureSession nullSessionFolder = await manager.StartAsync(
                new StartCaptureRequest(),
                CancellationToken.None).ConfigureAwait(false);
            CaptureSession emptySessionFolder = await manager.StartAsync(
                new StartCaptureRequest { SessionFolder = string.Empty },
                CancellationToken.None).ConfigureAwait(false);

            string fullBaseFolder = Path.GetFullPath(TempDirectory);
            string expectedNullFolder = Path.Combine(fullBaseFolder, nullSessionFolder.Id);
            string expectedEmptyFolder = Path.Combine(fullBaseFolder, emptySessionFolder.Id);
            Assert.That(nullSessionFolder.SessionFolder, Is.EqualTo(expectedNullFolder));
            Assert.That(emptySessionFolder.SessionFolder, Is.EqualTo(expectedEmptyFolder));
            Assert.That(factory.SessionFolders, Is.EqualTo([expectedNullFolder, expectedEmptyFolder]));
        }

        /// <summary>
        /// Documents the symlink escape scenario for future canonical-path
        /// hardening.
        /// </summary>
        [Test]
        [Platform("Linux,MacOSX")]
        public void StartAsyncRejectsBaseFolderEscapeViaSymlink()
        {
            Assert.Ignore(
                "Best-effort symlink escape coverage is skipped because the current guard validates " +
                "normalized paths with Path.GetFullPath and does not canonicalize symlink targets.");
        }

        private sealed class RecordingSourceFactory : ICaptureSourceFactory
        {
            public List<string> SessionFolders { get; } = [];

            public ICaptureSource Create(
                CaptureSourceKind kind,
                string sessionFolder,
                ILoggerFactory loggerFactory)
            {
                SessionFolders.Add(sessionFolder);
                return new InMemoryCaptureSource();
            }
        }
    }
}
