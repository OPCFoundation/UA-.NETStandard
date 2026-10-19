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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Lds.Server;

namespace Opc.Ua.Lds.Tests
{
    /// <summary>
    /// Regression tests proving that semaphore validation precedes file-system access.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class SemaphorePathTests
    {
        /// <summary>
        /// Unsafe paths must be rejected on every operating system, without network resolution.
        /// </summary>
        public static IEnumerable<string> UnsafePaths =>
        [
            @"\\attacker.invalid\share\server.sem",
            "//attacker.invalid/share/server.sem",
            @"\/attacker.invalid/share/server.sem",
            @"/\attacker.invalid\share\server.sem",
            @"\\?\UNC\attacker.invalid\share\server.sem",
            @"\\.\UNC\attacker.invalid\share\server.sem",
            @"//?/UNC/attacker.invalid/share/server.sem",
            @"\\?\C:\server.sem",
            @"\\.\C:\server.sem",
            @"//./pipe/server.sem",
            @"\\?\GLOBALROOT\Device\Mup\attacker.invalid\share\server.sem",
            @"\??\UNC\attacker.invalid\share\server.sem",
            @"\Device\Mup\attacker.invalid\share\server.sem",
            @"\\.\pipe\server.sem",
            @" \\\attacker.invalid\share\server.sem",
            "\t//attacker.invalid/share/server.sem",
            "file://attacker.invalid/share/server.sem",
            "smb://attacker.invalid/share/server.sem",
            @"C:server.sem",
            @"\server.sem",
            "server.sem",
            " ",
            Path.GetFullPath("server.sem") + "\0",
            Path.GetFullPath("server.sem") + "\n",
            Path.GetFullPath("server.sem") + ":stream",
            Path.GetFullPath("server.sem") + ".",
            Path.GetFullPath("server.sem") + " ",
            Path.GetFullPath(".") + Path.DirectorySeparatorChar + "server?.sem",
            Path.GetFullPath(".") + Path.DirectorySeparatorChar + "NUL.txt",
            Path.GetFullPath(".") + Path.DirectorySeparatorChar + "NUL .txt",
            Path.GetFullPath(".") + Path.DirectorySeparatorChar + "con",
            Path.GetFullPath(".") + Path.DirectorySeparatorChar + "CONIN$",
            Path.GetFullPath(".") + Path.DirectorySeparatorChar + "CONOUT$",
            Path.GetFullPath(".") + Path.DirectorySeparatorChar + "COM1.sem",
            Path.GetFullPath(".") + Path.DirectorySeparatorChar + "lpt¹.sem",
            Path.Combine(Path.GetFullPath("."), "..", "server.sem")
        ];

        /// <summary>
        /// A direct store call must not bypass the protocol-level path policy, even when unregistering.
        /// </summary>
        [Test]
        public void StoreRejectsUnsafePathsBeforeFileSystemAccess(
            [ValueSource(nameof(UnsafePaths))] string path,
            [Values] bool isOnline)
        {
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            using var store = new RegisteredServerStore(null, null, fileSystem.Object);
            store.SeedRegistration(new RegistrationEntry
            {
                ServerUri = "urn:test:semaphore",
                ProductUri = "urn:test:original"
            });
            var registration = new RegisteredServer
            {
                ServerUri = "urn:test:semaphore",
                SemaphoreFilePath = path,
                IsOnline = isOnline
            };

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await store.RegisterAsync(registration, null).ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(store.Snapshot(), Has.Count.EqualTo(1));
            Assert.That(store.Snapshot()[0].ProductUri, Is.EqualTo("urn:test:original"));
            fileSystem.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Prune protects legacy or externally loaded registrations as well as fresh requests.
        /// </summary>
        [TestCaseSource(nameof(UnsafePaths))]
        public void PruneRemovesUnsafeStoredPathsWithoutFileSystemAccess(string path)
        {
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            using var store = new RegisteredServerStore(null, null, fileSystem.Object);
            DateTime now = DateTime.UtcNow;
            store.SeedRegistration(new RegistrationEntry
            {
                ServerUri = "urn:test:semaphore",
                SemaphoreFilePath = path,
                LastSeenUtc = now,
                MdnsServerName = "unsafe",
                ServerCapabilities = ["DA"],
                DiscoveryUrls = ["opc.tcp://localhost:4840"]
            });
            Assert.That(store.SnapshotNetworkRecords(), Has.Count.EqualTo(1));

            store.Prune(now);

            Assert.That(store.Snapshot(), Is.Empty);
            Assert.That(store.SnapshotNetworkRecords(), Is.Empty);
            fileSystem.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Local semaphore existence still controls registration and subsequent pruning.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task LocalSemaphoreControlsRegistrationAndPruningAsync(bool exists)
        {
            string path = Path.GetFullPath("ordinary local.sem");
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            fileSystem.Setup(f => f.Exists(path, false)).Returns(exists);
            using var store = new RegisteredServerStore(null, null, fileSystem.Object);

            RegistrationEntry? entry = await store.RegisterAsync(new RegisteredServer
            {
                ServerUri = "urn:test:semaphore",
                SemaphoreFilePath = path,
                IsOnline = true
            }, null).ConfigureAwait(false);

            Assert.That(entry != null, Is.EqualTo(exists));
            Assert.That(store.Snapshot(), Has.Count.EqualTo(exists ? 1 : 0));
            if (exists)
            {
                Assert.That(entry!.SemaphoreFilePath, Is.EqualTo(path));
                store.Prune(entry.LastSeenUtc);
                Assert.That(store.Snapshot(), Has.Count.EqualTo(1));
                fileSystem.Setup(f => f.Exists(path, false)).Returns(false);
                store.Prune(entry.LastSeenUtc);
                Assert.That(store.Snapshot(), Is.Empty);
            }

            fileSystem.Verify(f => f.Exists(path, false), Times.Exactly(exists ? 3 : 1));
            fileSystem.VerifyNoOtherCalls();
        }

        /// <summary>
        /// No semaphore and explicit unregisters never require file-system access.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        public async Task AbsentSemaphoreAndOfflineRegistrationDoNotAccessFileSystemAsync(string? path)
        {
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            using var store = new RegisteredServerStore(null, null, fileSystem.Object);
            var registration = new RegisteredServer
            {
                ServerUri = "urn:test:semaphore",
                SemaphoreFilePath = path,
                IsOnline = true
            };

            Assert.That(await store.RegisterAsync(registration, null).ConfigureAwait(false), Is.Not.Null);
            store.Prune(store.Snapshot()[0].LastSeenUtc);
            Assert.That(store.Snapshot(), Has.Count.EqualTo(1));
            registration.IsOnline = false;
            registration.SemaphoreFilePath = Path.GetFullPath("ordinary.sem");

            Assert.That(await store.RegisterAsync(registration, null).ConfigureAwait(false), Is.Null);
            Assert.That(store.Snapshot(), Is.Empty);
            fileSystem.VerifyNoOtherCalls();
        }

        /// <summary>
        /// LDS hosting uses the existing injectable file-system abstraction for validated semaphores.
        /// </summary>
        [Test]
        public async Task HostedStoreUsesInjectedFileSystemAsync()
        {
            string path = Path.GetFullPath("local.sem");
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            fileSystem.Setup(f => f.Exists(path, false)).Returns(true);
            var services = new ServiceCollection();
            services.AddSingleton(fileSystem.Object);
            services.AddOpcUa().AddLdsServer(options =>
            {
                options.ApplicationUri = "urn:test:lds";
                options.ProductUri = "urn:test:product";
            });
            using ServiceProvider provider = services.BuildServiceProvider();
            IRegisteredServerStore store = provider.GetRequiredService<IRegisteredServerStore>();

            RegistrationEntry? entry = await store.RegisterAsync(new RegisteredServer
            {
                ServerUri = "urn:test:semaphore",
                SemaphoreFilePath = path,
                IsOnline = true
            }, null).ConfigureAwait(false);

            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.SemaphoreFilePath, Is.EqualTo(path));
            fileSystem.Verify(f => f.Exists(path, false), Times.Once);
            fileSystem.VerifyNoOtherCalls();
        }
    }
}
