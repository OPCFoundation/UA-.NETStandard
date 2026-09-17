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
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    [TestFixture]
    [NonParallelizable]
    [Platform("Win")]
    public sealed class WotPreparedMetadataReviewTests
    {
        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "metadata-review", Guid.NewGuid().ToString("N"));
            TestContext.Out.WriteLine(
                $"Runtime={RuntimeInformation.FrameworkDescription}; CLR={Environment.Version}; " +
                $"ServerGC={GCSettings.IsServerGC}; Assembly={typeof(WotRegistryService).Assembly.Location}");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ThrowingChangedSubscriberCannotStrandCommittedGeneration(bool durabilityWarning)
        {
            bool armed = false;
            var persistenceFailure = new IOException("The manifest switch completed before its warning.");
            using FileWotRegistryStore store = durabilityWarning
                ? new FileWotRegistryStore(
                    m_root,
                    directorySyncFailureInjector: null,
                    manifestReplace: (source, destination, backup) =>
                    {
                        File.Replace(source, destination, backup);
                        if (armed)
                        {
                            throw persistenceFailure;
                        }
                    })
                : new FileWotRegistryStore(m_root);
            Assert.That(store.SupportsPreparedCommits, Is.True);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource resource = await AddAsync(registry).ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            var notificationFailure = new InvalidOperationException("The Changed subscriber failed once.");
            int notifications = 0;
            EventHandler<WotRegistryChangedEventArgs> handler = (_, args) =>
            {
                notifications++;
                Assert.That(args.Current.Generation, Is.EqualTo(before.Generation + 1));
                if (notifications == 1)
                {
                    throw notificationFailure;
                }
            };
            Exception? reportedFailure = null;
            registry.Changed += handler;
            armed = true;
            try
            {
                await registry.ApplyProjectionResultsAsync([Projection(resource, 1)]).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                reportedFailure = exception;
            }
            finally
            {
                registry.Changed -= handler;
                armed = false;
            }

            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(registry.Current.Generation, Is.EqualTo(before.Generation + 1));
            Assert.That(registry.Current.FindResource(resource.GroupId, resource.ResourceId)!.RefreshGeneration,
                Is.EqualTo(1u));
            using (var observer = new FileWotRegistryStore(m_root))
            {
                WotRegistrySnapshot durable = await observer.LoadAsync().ConfigureAwait(false);
                Assert.That(durable.Generation, Is.EqualTo(registry.Current.Generation));
                Assert.That(durable.FindResource(resource.GroupId, resource.ResourceId)!.RefreshGeneration,
                    Is.EqualTo(1u));
            }

            await registry.ApplyProjectionResultsAsync([Projection(resource, 2)]).ConfigureAwait(false);

            Assert.That(registry.Current.Generation, Is.EqualTo(before.Generation + 2));
            Assert.That(registry.Current.FindResource(resource.GroupId, resource.ResourceId)!.RefreshGeneration,
                Is.EqualTo(2u));
            Assert.That(reportedFailure, Is.TypeOf<WotRegistryCommitDurabilityUncertainException>());
            var committedFailure = (WotRegistryCommitDurabilityUncertainException)reportedFailure!;
            Assert.That(committedFailure.CommittedGeneration, Is.EqualTo(before.Generation + 1));
            if (durabilityWarning)
            {
                Assert.That(committedFailure.PersistenceFailure, Is.TypeOf<AggregateException>());
                var failures = (AggregateException)committedFailure.PersistenceFailure;
                Assert.That(failures.Flatten().InnerExceptions, Has.Count.EqualTo(2));
                Assert.That(failures.Flatten().InnerExceptions, Does.Contain(persistenceFailure));
                Assert.That(failures.Flatten().InnerExceptions, Does.Contain(notificationFailure));
            }
            else
            {
                Assert.That(committedFailure.PersistenceFailure, Is.SameAs(notificationFailure));
            }
        }

        [Test]
        public async Task RelativeLocalRootRetainsValidatedResourceKeyAcrossDirectoryChange()
        {
            string originalDirectory = Directory.GetCurrentDirectory();
            string firstDirectory = Path.Combine(m_root, "a");
            string secondDirectory = Path.Combine(m_root, "b");
            Directory.CreateDirectory(firstDirectory);
            Directory.CreateDirectory(Path.Combine(secondDirectory, "blobs"));
            try
            {
                Directory.SetCurrentDirectory(firstDirectory);
                using var content = new WotBlobResourceStore("blobs");
                using var store = new FileWotRegistryStore(Path.Combine(m_root, "metadata"), content);
                using var registry = new WotRegistryService(store);
                Assert.That(content.SupportsImmutableContentLeases, Is.True);
                await registry.InitializeAsync().ConfigureAwait(false);
                WotResource resource = await AddAsync(registry).ConfigureAwait(false);
                using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                    .ConfigureAwait(false);
                string key = resource.DefaultVersion!.DigestHex;
                ByteString original = await content.ReadAsync(key, 0, int.MaxValue).ConfigureAwait(false);
                byte[] other = new byte[original.Length + 1];
                original.Span.CopyTo(other);
                other[0] ^= 0xff;
                string otherPath = Path.Combine(secondDirectory, "blobs", key + ".bin");
                File.WriteAllBytes(otherPath, other);
                long before = registry.Current.Generation;

                Directory.SetCurrentDirectory(secondDirectory);
                await registry.ApplyProjectionResultsAsync([Projection(resource, 1)]).ConfigureAwait(false);
                ByteString ordinary = await content.ReadAsync(key, 0, int.MaxValue).ConfigureAwait(false);

                Assert.That(registry.Current.Generation, Is.EqualTo(before + 1));
                Assert.That(captured.Snapshot.Generation, Is.EqualTo(before));
                Assert.That(ordinary, Is.EqualTo(original),
                    "Ordinary reads must use the same root-to-key association as the retained validated evidence.");
                Assert.That(await content.GetLengthAsync(key).ConfigureAwait(false), Is.EqualTo(original.Length));
                using IWotRegistryContentLease nextLease = await content.AcquireContentLeaseAsync(key)
                    .ConfigureAwait(false);
                Assert.That(await nextLease.ReadAsync(0, int.MaxValue).ConfigureAwait(false), Is.EqualTo(original));
                await Assert.ThatAsync(
                    async () => await content.WriteAsync(key, 0, ByteString.From(other)).ConfigureAwait(false),
                    Throws.TypeOf<IOException>().Or.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                await Assert.ThatAsync(
                    async () => await content.DeleteAsync(key).ConfigureAwait(false),
                    Throws.TypeOf<IOException>().Or.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                Assert.That(File.ReadAllBytes(otherPath), Is.EqualTo(other));
                using var observer = new FileWotRegistryStore(Path.Combine(m_root, "metadata"), content);
                Assert.That((await observer.LoadAsync().ConfigureAwait(false)).Generation, Is.EqualTo(before + 1));
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDirectory);
            }
        }

        [Test]
        public async Task CustomFileSystemRetainsItsRelativePathsWithoutAdvertisingImmutableLeases()
        {
            string relativePath = Path.Combine("blobs", "key.bin");
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            fileSystem.Setup(value => value.Exists(relativePath, false)).Returns(true);
            fileSystem.Setup(value => value.GetLength(relativePath)).Returns(17);
            using var content = new WotBlobResourceStore("blobs", fileSystem.Object);

            Assert.That(content.SupportsImmutableContentLeases, Is.False);
            Assert.That(await content.GetLengthAsync("key").ConfigureAwait(false), Is.EqualTo(17));
            await Assert.ThatAsync(
                async () => await content.AcquireContentLeaseAsync("key").ConfigureAwait(false),
                Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);
            fileSystem.VerifyAll();
        }

        private static async Task<WotResource> AddAsync(WotRegistryService registry)
        {
            WotRegistryMutationResult result = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "selected",
                VersionId = "v1",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(TestMaterialization.Td("urn:metadata-review"))
            }).ConfigureAwait(false);
            Assert.That(result.Changed, Is.True, result.Message);
            return result.Resource ?? throw new InvalidOperationException("The resource was not created.");
        }

        private static WotResourceProjection Projection(WotResource resource, uint generation)
        {
            return new WotResourceProjection(
                resource.GroupId,
                resource.ResourceId,
                WoTLoadStateEnum.Active,
                resource.DefaultVersionId,
                generation,
                1,
                new NodeId("selected-root", 2),
                null,
                [],
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(generation))
            {
                VersionId = resource.DefaultVersionId
            };
        }

        private string m_root = null!;
    }
}
