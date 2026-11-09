/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

#if NET10_0_OR_GREATER
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using OnboardingRegistrar;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies the sample's public-only checkpoint persistence and exclusive writer boundary.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class PushRegistrarCheckpointFilesTests
    {
        /// <summary>
        /// Selects one isolated directory for each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            string temporary = Path.GetTempPath();
            m_directory = Path.Combine(
                temporary, nameof(PushRegistrarCheckpointFilesTests), Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// Removes only the test's own directory after all writer leases have been disposed.
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
        /// Replacement persists across writer restart and an explicit removal remains removed.
        /// </summary>
        [Test]
        public async Task PublishedReplacementAndRemovalSurviveWriterRestart()
        {
            byte[] original = [1, 2, 3];
            byte[] replacement = [4, 5];
            using (var files = new PushRegistrarCheckpointFiles(m_directory))
            {
                Assert.That(await files.ReadAsync("receipt", CancellationToken.None).ConfigureAwait(false), Is.Null);
                await files.WriteAsync("receipt", original, CancellationToken.None).ConfigureAwait(false);
                await files.WriteAsync("receipt", replacement, CancellationToken.None).ConfigureAwait(false);
            }
            using (var restarted = new PushRegistrarCheckpointFiles(m_directory))
            {
                Assert.That(await restarted.ReadAsync("receipt", CancellationToken.None).ConfigureAwait(false),
                    Is.EqualTo(replacement));
                Assert.That(await restarted.RemoveAsync("receipt", CancellationToken.None).ConfigureAwait(false),
                    Is.True);
                Assert.That(await restarted.RemoveAsync("receipt", CancellationToken.None).ConfigureAwait(false),
                    Is.False);
            }
            using var final = new PushRegistrarCheckpointFiles(m_directory);
            Assert.That(await final.ReadAsync("receipt", CancellationToken.None).ConfigureAwait(false), Is.Null);
            Assert.That(Directory.GetFiles(m_directory, "*.pending"), Is.Empty);
        }

        /// <summary>
        /// The operating system rejects a second owning process or provider before it can publish.
        /// </summary>
        [Test]
        public void SecondWriterIsRejectedWhileOriginalLeaseIsHeld()
        {
            using (var original = new PushRegistrarCheckpointFiles(m_directory))
            {
                Assert.Throws<IOException>(() =>
                {
                    using var conflicting = new PushRegistrarCheckpointFiles(m_directory);
                });
            }
            using var replacement = new PushRegistrarCheckpointFiles(m_directory);
            Assert.That(Directory.GetFiles(m_directory, "*.bin"), Is.Empty);
        }

        /// <summary>
        /// Invalid or canceled publication preserves the preceding complete checkpoint.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task RejectedPublicationPreservesPreviousValue(int scenario)
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            byte[] original = [1, 2, 3];
            await files.WriteAsync("receipt", original, CancellationToken.None).ConfigureAwait(false);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            if (scenario == 2)
            {
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await files.WriteAsync("receipt", new byte[] { 4 }, canceled.Token).ConfigureAwait(false));
            }
            else
            {
                byte[] invalid = scenario == 0 ? [] : new byte[PushRegistrarCheckpointFiles.MaximumBytes + 1];
                await Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await files.WriteAsync("receipt", invalid, CancellationToken.None).ConfigureAwait(false));
            }
            Assert.That(await files.ReadAsync("receipt", CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(original));
            Assert.That(Directory.GetFiles(m_directory, "*.pending"), Is.Empty);
        }

        /// <summary>
        /// Both valid size boundaries can be read through the bounded streaming reader.
        /// </summary>
        [TestCase(1)]
        [TestCase(PushRegistrarCheckpointFiles.MaximumBytes)]
        public async Task ValidSizeBoundariesRoundTrip(int size)
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            byte[] value = new byte[size];
            value[0] = 1;
            value[^1] = 2;

            await files.WriteAsync("receipt", value, CancellationToken.None).ConfigureAwait(false);

            Assert.That(await files.ReadAsync("receipt", CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(value));
        }

        /// <summary>
        /// A failed replacement leaves the preceding checkpoint and reports staging cleanup failures as well.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedReplacementPreservesPublishedStateAndReportsCleanup(bool failCleanup)
        {
            byte[] original = [1, 2, 3];
            using (var files = new PushRegistrarCheckpointFiles(m_directory))
            {
                await files.WriteAsync("receipt", original, CancellationToken.None).ConfigureAwait(false);
            }
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            fileSystem.Setup(value => value.OpenWrite(It.IsAny<string>()))
                .Returns((string path) => LocalFileSystem.Instance.OpenWrite(path));
            fileSystem.Setup(value => value.Replace(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new IOException("replacement failed"));
            fileSystem.Setup(value => value.Exists(It.IsAny<string>(), false)).Returns(true);
            fileSystem.Setup(value => value.Delete(It.IsAny<string>(), false))
                .Callback<string, bool>((path, _) =>
                {
                    if (failCleanup)
                    {
                        throw new IOException("cleanup failed");
                    }
                    LocalFileSystem.Instance.Delete(path, isDirectory: false);
                });
            using (var failing = new PushRegistrarCheckpointFiles(m_directory, fileSystem.Object))
            {
                if (failCleanup)
                {
                    AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                        await failing.WriteAsync("receipt", new byte[] { 4 }, CancellationToken.None)
                            .ConfigureAwait(false));
                    Assert.That(error!.InnerExceptions, Has.Count.EqualTo(2));
                    Assert.That(error.InnerExceptions[0].Message, Is.EqualTo("replacement failed"));
                    Assert.That(error.InnerExceptions[1].Message, Is.EqualTo("cleanup failed"));
                }
                else
                {
                    IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                        await failing.WriteAsync("receipt", new byte[] { 4 }, CancellationToken.None)
                            .ConfigureAwait(false));
                    Assert.That(error!.Message, Is.EqualTo("replacement failed"));
                }
            }
            using var restarted = new PushRegistrarCheckpointFiles(m_directory);
            Assert.That(await restarted.ReadAsync("receipt", CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(original));
            Assert.That(Directory.GetFiles(m_directory, "*.pending"), Has.Length.EqualTo(failCleanup ? 1 : 0));
        }

        /// <summary>
        /// Canonical public enrollment identifiers reach storage without claiming a synchronous cache hit.
        /// </summary>
        [Test]
        public async Task CanonicalEnrollmentJournalPersistsAndReturnsDetachedReads()
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            var store = new PushEnrollmentCheckpointStore(files);
            var id = new SecretIdentifier(
                "PushEnrollment-" + Convert.ToBase64String(new byte[32]), store.StoreType);
            byte[] value = [1, 2, 3];
            await store.SetAsync(id, value).ConfigureAwait(false);
            Assert.That(store.TryGet(id), Is.Null);
            using (ISecret? first = await store.GetAsync(id).ConfigureAwait(false))
            {
                Assert.That(first, Is.Not.Null);
                Assert.That(first!.Bytes.ToArray(), Is.EqualTo(value));
            }
            using (ISecret? second = await store.GetAsync(id).ConfigureAwait(false))
            {
                Assert.That(second, Is.Not.Null);
                Assert.That(second!.Bytes.ToArray(), Is.EqualTo(value));
            }
            Assert.That(await store.RemoveAsync(id).ConfigureAwait(false), Is.True);
            using ISecret? removed = await store.GetAsync(id).ConfigureAwait(false);
            Assert.That(removed, Is.Null);
        }

        /// <summary>
        /// The secret-store adapter accepts only the enrollment provider's canonical public journal identifiers.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task AdapterRejectsIdentifiersOutsidePublicEnrollmentScope(int scenario)
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            var store = new PushEnrollmentCheckpointStore(files);
            string name = "PushEnrollment-" + Convert.ToBase64String(new byte[32]);
            var id = scenario switch
            {
                0 => new SecretIdentifier("DeviceSecurity-sensitive", store.StoreType),
                1 => new SecretIdentifier(name, "AnotherStore"),
                2 => new SecretIdentifier("PushEnrollment-invalid", store.StoreType),
                _ => new SecretIdentifier(name, store.StoreType, "another-path")
            };

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.SetAsync(id, new byte[] { 1 }, CancellationToken.None).ConfigureAwait(false));
            Assert.That(Directory.GetFiles(m_directory, "*.bin"), Is.Empty);
        }

        /// <summary>The exact directory owned by the current test.</summary>
        private string m_directory = null!;
    }
}
#endif
