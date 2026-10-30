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
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Storage;

namespace Opc.Ua.Core.Tests.Storage
{
    /// <summary>
    /// Exercises the bounded local store with real protection and filesystem barriers.
    /// These tests do not qualify hardware power-loss behavior or full RSE protocol modes.
    /// </summary>
    [TestFixture]
    [Category("Storage")]
    [Parallelizable]
    public sealed class FileSharedKeyValueStoreTests
    {
        /// <summary>
        /// Creates a short, exclusively owned temporary parent and ephemeral protection keys.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_testDirectory = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "fkv-" + Guid.NewGuid().ToString("N"))).FullName;
            m_storeDirectory = Path.Combine(m_testDirectory, "store");
            byte[] masterKey = new byte[32];
            using var random = RandomNumberGenerator.Create();
            random.GetBytes(masterKey);
            try
            {
                m_protector = new AesCbcHmacRecordProtector(masterKey);
            }
            finally
            {
                CryptoUtils.ZeroMemory(masterKey);
            }
        }

        /// <summary>
        /// Releases every opened store before removing only this test's own directory.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            try
            {
                for (int index = m_stores.Count - 1; index >= 0; index--)
                {
                    await m_stores[index].DisposeAsync().AsTask().WaitAsync(s_watchdog).ConfigureAwait(false);
                }
            }
            finally
            {
                m_stores.Clear();
                m_protector.Dispose();
                if (Directory.Exists(m_testDirectory))
                {
                    Directory.Delete(m_testDirectory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Persists initialized identity and committed bytes without rewriting them on reopen.
        /// </summary>
        [Test]
        public async Task InitializationPersistsCommittedStateAcrossReopenAsync()
        {
            FileSharedKeyValueStore store = CreateStore();
            Assert.That(Directory.Exists(m_storeDirectory), Is.False, "Construction must not perform I/O.");

            await store.VerifyDurabilityAsync().ConfigureAwait(false);
            (bool initiallyFound, ByteString initiallyMissing) =
                await store.TryGetAsync("persistent/key").ConfigureAwait(false);
            Assert.That(initiallyFound, Is.False);
            Assert.That(initiallyMissing.IsNull, Is.True);
            Assert.That(
                Directory.GetFiles(m_storeDirectory).Select(Path.GetFileName),
                Is.EquivalentTo(s_committedFileNames));

            await store.SetAsync("persistent/key", ByteString.From(0x00, 0x7f, 0x80, 0xff)).ConfigureAwait(false);
            byte[] identity = await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false);
            byte[] snapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            Assert.That(
                m_protector.TryUnprotect(
                    RecordProtectionContext.Create("FileSharedKeyValueStore.Identity.1", kLogicalStoreId),
                    new ByteString(identity),
                    out ByteString epoch),
                Is.True);
            Assert.That(epoch, Has.Length.EqualTo(16));
            Assert.That(new Guid(epoch.ToArray()), Is.Not.EqualTo(Guid.Empty));
            Assert.That(snapshot, Has.Length.InRange(1, kMaximumSnapshotBytes));

            await store.DisposeAsync().ConfigureAwait(false);
            FileSharedKeyValueStore reopened = CreateStore();
            await reopened.VerifyDurabilityAsync().ConfigureAwait(false);
            (bool found, ByteString value) = await reopened.TryGetAsync("persistent/key").ConfigureAwait(false);

            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(new byte[] { 0x00, 0x7f, 0x80, 0xff }));
            Assert.That(await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false), Is.EqualTo(identity));
            Assert.That(await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false), Is.EqualTo(snapshot));
            Assert.That(
                Directory.GetFiles(m_storeDirectory).Select(Path.GetFileName),
                Is.EquivalentTo(s_committedFileNames));
        }

        /// <summary>
        /// Never replaces lost committed state with a newly initialized empty generation.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task MissingCommittedFilesFailClosedAsync(bool removeIdentity, bool removeSnapshot)
        {
            FileSharedKeyValueStore original = CreateStore();
            await original.SetAsync("persistent/key", ByteString.From(0x37, 0x00, 0xa5, 0xff)).ConfigureAwait(false);
            await original.DisposeAsync().ConfigureAwait(false);
            byte[] identity = await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false);
            byte[] snapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            if (removeIdentity)
            {
                File.Delete(StorePath("identity.bin"));
            }
            if (removeSnapshot)
            {
                File.Delete(StorePath("snapshot.bin"));
            }
            Assert.That(File.Exists(StorePath("owner.lock")), Is.True, "The initialized root still exists.");

            FileSharedKeyValueStore reopened = CreateStore();
            await Assert.ThrowsAsync<InvalidDataException>(
                async () => await reopened.VerifyDurabilityAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(File.Exists(StorePath("identity.bin")), Is.EqualTo(!removeIdentity));
            Assert.That(File.Exists(StorePath("snapshot.bin")), Is.EqualTo(!removeSnapshot));
            if (!removeIdentity)
            {
                Assert.That(
                    await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false),
                    Is.EqualTo(identity));
            }
            if (!removeSnapshot)
            {
                Assert.That(
                    await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                    Is.EqualTo(snapshot));
            }
            await Assert.ThrowsAsync<IOException>(
                async () => await reopened.TryGetAsync("persistent/key").ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.ThrowsAsync<IOException>(
                async () => await reopened.VerifyDurabilityAsync().ConfigureAwait(false)).ConfigureAwait(false);

            using var releasedOwner = new FileStream(
                StorePath("owner.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.That(releasedOwner.Length, Is.Zero, "Failed initialization must release the live owner handle.");
        }

        /// <summary>
        /// Preserves unrecognized initial content instead of silently treating it as an empty store.
        /// </summary>
        [Test]
        public async Task UnknownInitialContentIsNotAnEmptyStoreAsync()
        {
            Directory.CreateDirectory(m_storeDirectory);
            string unknownPath = StorePath("not-a-store.bin");
            byte[] unknownContent = [0xca, 0xfe, 0x00, 0x25];
            await WriteAllBytesAsync(unknownPath, unknownContent).ConfigureAwait(false);
            FileSharedKeyValueStore store = CreateStore();

            await Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.VerifyDurabilityAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(await ReadAllBytesAsync(unknownPath).ConfigureAwait(false), Is.EqualTo(unknownContent));
            Assert.That(File.Exists(StorePath("identity.bin")), Is.False);
            Assert.That(File.Exists(StorePath("snapshot.bin")), Is.False);
            await Assert.ThrowsAsync<IOException>(
                async () => await store.TryGetAsync("missing").ConfigureAwait(false)).ConfigureAwait(false);
            using var releasedOwner = new FileStream(
                StorePath("owner.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.That(releasedOwner.Length, Is.Zero);
        }

        /// <summary>
        /// Rejects missing construction dependencies before touching the filesystem.
        /// </summary>
        [Test]
        public void ConstructorRejectsMissingDependencies()
        {
            ArgumentNullException directoryError = Assert.Throws<ArgumentNullException>(
                () => new FileSharedKeyValueStore(null!, kLogicalStoreId, m_protector, 256))!;
            ArgumentNullException protectorError = Assert.Throws<ArgumentNullException>(
                () => new FileSharedKeyValueStore(m_storeDirectory, kLogicalStoreId, null!, 256))!;
            ArgumentException unauthenticatedError = Assert.Throws<ArgumentException>(
                () => new FileSharedKeyValueStore(
                    m_storeDirectory, kLogicalStoreId, NullRecordProtector.Instance, 256))!;

            Assert.That(directoryError.ParamName, Is.EqualTo("directory"));
            Assert.That(protectorError.ParamName, Is.EqualTo("protector"));
            Assert.That(unauthenticatedError.ParamName, Is.EqualTo("protector"));
            Assert.That(Directory.Exists(m_storeDirectory), Is.False);
        }

        /// <summary>
        /// Requires a dedicated path rather than an empty path or filesystem root.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ConstructorRejectsEmptyOrRootDirectory(bool root)
        {
            string directory = root ? Path.GetPathRoot(m_testDirectory)! : string.Empty;

            Assert.Throws<ArgumentException>(
                () => new FileSharedKeyValueStore(directory, kLogicalStoreId, m_protector, 256));

            Assert.That(Directory.Exists(m_storeDirectory), Is.False);
        }

        /// <summary>
        /// Requires a stable nonblank logical identity.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t ")]
        public void ConstructorRejectsMissingLogicalIdentity(string? logicalStoreId)
        {
            ArgumentException error = Assert.Throws<ArgumentException>(
                () => new FileSharedKeyValueStore(m_storeDirectory, logicalStoreId!, m_protector, 256))!;

            Assert.That(error.ParamName, Is.EqualTo("logicalStoreId"));
            Assert.That(Directory.Exists(m_storeDirectory), Is.False);
        }

        /// <summary>
        /// Rejects negative, zero and immediately below-minimum capacity.
        /// </summary>
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(255)]
        public void ConstructorRejectsInsufficientCapacity(int capacity)
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
                () => new FileSharedKeyValueStore(m_storeDirectory, kLogicalStoreId, m_protector, capacity))!;

            Assert.That(error.ParamName, Is.EqualTo("maximumSnapshotBytes"));
            Assert.That(Directory.Exists(m_storeDirectory), Is.False);
        }

        /// <summary>
        /// Accepts the minimum, its neighbor and the largest finite integer bound.
        /// </summary>
        [TestCase(256)]
        [TestCase(257)]
        [TestCase(int.MaxValue)]
        public async Task FiniteCapacityCanInitializeAsync(int capacity)
        {
            FileSharedKeyValueStore store = CreateStore(maximumSnapshotBytes: capacity);
            Assert.That(store.MaximumSnapshotBytes, Is.EqualTo(capacity));
            Assert.That(Directory.Exists(m_storeDirectory), Is.False);

            await store.VerifyDurabilityAsync().ConfigureAwait(false);

            Assert.That(
                await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                Has.Length.EqualTo(101));
            await AssertValueAsync(store, "absent", default, found: false).ConfigureAwait(false);
        }

        /// <summary>
        /// Describes local linearizability without claiming a distributed backend.
        /// </summary>
        [TestCase(null, false)]
        [TestCase("", true)]
        [TestCase("鍵/é", true)]
        public void ConsistencyProbesDescribeProcessLocalStorage(string? key, bool valid)
        {
            FileSharedKeyValueStore store = CreateStore();

            Assert.That(store.IsLinearizable(key!), Is.EqualTo(valid));
            Assert.That(store.IsProcessLocal(key!), Is.EqualTo(valid));
            Assert.That(Directory.Exists(m_storeDirectory), Is.False);
        }

        /// <summary>
        /// Requires the caller to provision the parent directory.
        /// </summary>
        [Test]
        public async Task MissingParentIsNotCreatedAsync()
        {
            string missingParent = Path.Combine(m_testDirectory, "missing");
            FileSharedKeyValueStore store = CreateStore(directory: Path.Combine(missingParent, "store"));

            await Assert.ThrowsAsync<DirectoryNotFoundException>(
                async () => await store.VerifyDurabilityAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(Directory.Exists(missingParent), Is.False);
        }

        /// <summary>
        /// Registers only store services, all backed by one asynchronously disposable owner.
        /// </summary>
        [Test]
        public async Task RegistrationSharesOnlyStoreServicesAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IRecordProtector>(m_protector);
            IOpcUaBuilder builder = services.AddOpcUa();
            ServiceDescriptor[] baseline = [.. services];

            IOpcUaBuilder returned = builder.AddFileSharedKeyValueStore(
                m_storeDirectory, kLogicalStoreId, kMaximumSnapshotBytes);

            Assert.That(returned, Is.SameAs(builder));
            Assert.That(services.Take(baseline.Length), Is.EqualTo(baseline));
            ServiceDescriptor[] added = [.. services.Skip(baseline.Length)];
            Type[] expectedTypes =
            [
                typeof(FileSharedKeyValueStore),
                typeof(ISharedKeyValueStore),
                typeof(ISharedKeyValueStoreConsistency),
                typeof(ISharedKeyValueStoreDurability),
                typeof(ISharedKeyValueStoreCapacity)
            ];
            Assert.That(added.Select(descriptor => descriptor.ServiceType), Is.EquivalentTo(expectedTypes));
            Assert.That(added.Select(descriptor => descriptor.Lifetime), Is.All.EqualTo(ServiceLifetime.Singleton));

            ServiceProvider provider = services.BuildServiceProvider();
            try
            {
                FileSharedKeyValueStore concrete = provider.GetRequiredService<FileSharedKeyValueStore>();
                ISharedKeyValueStore shared = provider.GetRequiredService<ISharedKeyValueStore>();
                ISharedKeyValueStoreConsistency consistency =
                    provider.GetRequiredService<ISharedKeyValueStoreConsistency>();
                ISharedKeyValueStoreDurability durability =
                    provider.GetRequiredService<ISharedKeyValueStoreDurability>();
                ISharedKeyValueStoreCapacity capacity =
                    provider.GetRequiredService<ISharedKeyValueStoreCapacity>();
                Assert.That(shared, Is.SameAs(concrete));
                Assert.That(consistency, Is.SameAs(concrete));
                Assert.That(durability, Is.SameAs(concrete));
                Assert.That(capacity, Is.SameAs(concrete));
                Assert.That(provider.GetRequiredService<FileSharedKeyValueStore>(), Is.SameAs(concrete));
                Assert.That(Directory.Exists(m_storeDirectory), Is.False);

                await durability.VerifyDurabilityAsync().ConfigureAwait(false);
                await shared.SetAsync("di/key", ByteString.From(0x91, 0x00, 0xff)).ConfigureAwait(false);
                await AssertValueAsync(shared, "di/key", ByteString.From(0x91, 0x00, 0xff)).ConfigureAwait(false);
                Assert.That(consistency.IsLinearizable("di/key"), Is.True);
                Assert.That(consistency.IsProcessLocal("di/key"), Is.True);
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }

            FileSharedKeyValueStore reopened = CreateStore();
            await AssertValueAsync(reopened, "di/key", ByteString.From(0x91, 0x00, 0xff)).ConfigureAwait(false);
        }

        /// <summary>
        /// Fails registration/resolution rather than silently choosing an unprotected backend.
        /// </summary>
        [Test]
        public async Task RegistrationRequiresBuilderAndProtectorAsync()
        {
            ArgumentNullException error = Assert.Throws<ArgumentNullException>(
                () => OpcUaDurableStoreBuilderExtensions.AddFileSharedKeyValueStore(
                    null!, m_storeDirectory, kLogicalStoreId, 256))!;
            Assert.That(error.ParamName, Is.EqualTo("builder"));
            var services = new ServiceCollection();
            services.AddOpcUa().AddFileSharedKeyValueStore(m_storeDirectory, kLogicalStoreId, 256);
            ServiceProvider provider = services.BuildServiceProvider();
            try
            {
                Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ISharedKeyValueStore>());
                Assert.That(Directory.Exists(m_storeDirectory), Is.False);
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Keeps empty, null, binary, case-distinct and non-normalized Unicode values across reopen.
        /// </summary>
        [Test]
        public async Task ValuesAndOrdinalKeysSurviveReopenAsync()
        {
            FileSharedKeyValueStore store = CreateStore();
            var expected = new Dictionary<string, ByteString>(StringComparer.Ordinal)
            {
                [string.Empty] = ByteString.From(0x17),
                ["A"] = ByteString.From(0x01),
                ["a"] = ByteString.From(0x00, 0x7f, 0x80, 0xff),
                ["café"] = ByteString.From(0x02),
                ["cafe\u0301"] = ByteString.From(0x03),
                ["鍵/\0"] = ByteString.From(0xfe, 0x00),
                ["empty"] = ByteString.Empty,
                ["null"] = default
            };
            await store.SetAsync("a", ByteString.From("U"u8)).ConfigureAwait(false);
            foreach (KeyValuePair<string, ByteString> entry in expected)
            {
                await store.SetAsync(entry.Key, entry.Value).ConfigureAwait(false);
                await AssertValueAsync(store, entry.Key, entry.Value).ConfigureAwait(false);
            }
            await store.DisposeAsync().ConfigureAwait(false);
            FileSharedKeyValueStore reopened = CreateStore();

            foreach (KeyValuePair<string, ByteString> entry in expected)
            {
                await AssertValueAsync(reopened, entry.Key, entry.Value).ConfigureAwait(false);
            }
            KeyValuePair<string, ByteString>[] entries = await ScanAsync(reopened).ConfigureAwait(false);
            Assert.That(
                entries.Select(entry => entry.Key),
                Is.EqualTo(expected.Keys.OrderBy(key => key, StringComparer.Ordinal)));
            await AssertValueAsync(reopened, "missing", default, found: false).ConfigureAwait(false);
        }

        /// <summary>
        /// Distinguishes absent, null, empty, equal and different CAS operands and deletion.
        /// </summary>
        [TestCase("absent-insert")]
        [TestCase("absent-insert-empty")]
        [TestCase("absent-mismatch")]
        [TestCase("absent-delete")]
        [TestCase("equal-replace")]
        [TestCase("equal-replace-empty")]
        [TestCase("different")]
        [TestCase("present-expects-absent")]
        [TestCase("null-expects-absent")]
        [TestCase("empty-expects-absent")]
        [TestCase("equal-delete")]
        [TestCase("empty-replace")]
        [TestCase("empty-delete")]
        public async Task CompareAndSwapUsesAbsenceAndExactExpectedValueAsync(string scenario)
        {
            bool present = !scenario.StartsWith("absent-", StringComparison.Ordinal);
            ByteString initial = scenario.StartsWith("empty-", StringComparison.Ordinal)
                ? ByteString.Empty
                : scenario == "null-expects-absent" ? default : ByteString.From(0x11, 0x80);
            ByteString expected = scenario is "absent-insert" or "absent-insert-empty" or "absent-delete"
                or "present-expects-absent" or "null-expects-absent" or "empty-expects-absent"
                ? default
                : scenario == "different" ? ByteString.From(0x10, 0x80) : initial;
            ByteString replacement = scenario.EndsWith("-delete", StringComparison.Ordinal)
                ? default : scenario.EndsWith("-empty", StringComparison.Ordinal)
                    ? ByteString.Empty : ByteString.From(0x22, 0xff);
            bool shouldSwap = scenario is "absent-insert" or "absent-insert-empty" or "absent-delete" or "equal-replace"
                or "equal-replace-empty" or "equal-delete" or "empty-replace" or "empty-delete";
            bool changesState = shouldSwap && scenario != "absent-delete";
            bool finalFound = shouldSwap ? !replacement.IsNull : present;
            ByteString finalValue = shouldSwap ? replacement : present ? initial : default;
            FileSharedKeyValueStore store = CreateStore();
            await store.VerifyDurabilityAsync().ConfigureAwait(false);
            if (present)
            {
                await store.SetAsync("cas", initial).ConfigureAwait(false);
            }
            byte[] before = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);

            bool swapped = await store.CompareAndSwapAsync("cas", expected, replacement).ConfigureAwait(false);

            Assert.That(swapped, Is.EqualTo(shouldSwap));
            await AssertValueAsync(store, "cas", finalValue, finalFound).ConfigureAwait(false);
            byte[] after = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            Assert.That(after.SequenceEqual(before), Is.EqualTo(!changesState));
            Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Is.Empty);
            await store.DisposeAsync().ConfigureAwait(false);
            await AssertValueAsync(CreateStore(), "cas", finalValue, finalFound).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes even present-null entries, persists absence and leaves missing deletes unchanged.
        /// </summary>
        [TestCase("null")]
        [TestCase("empty")]
        [TestCase("binary")]
        public async Task CommittedDeleteCannotResurrectAsync(string kind)
        {
            ByteString value = kind == "null" ? default
                : kind == "empty" ? ByteString.Empty : ByteString.From(0x01, 0xff);
            FileSharedKeyValueStore store = CreateStore();
            await store.SetAsync("deleted", value).ConfigureAwait(false);
            await AssertValueAsync(store, "deleted", value).ConfigureAwait(false);

            Assert.That(await store.DeleteAsync("deleted").ConfigureAwait(false), Is.True);
            byte[] committed = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            Assert.That(await store.DeleteAsync("deleted").ConfigureAwait(false), Is.False);
            Assert.That(
                await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                Is.EqualTo(committed));
            await AssertValueAsync(store, "deleted", default, found: false).ConfigureAwait(false);
            await store.DisposeAsync().ConfigureAwait(false);

            FileSharedKeyValueStore reopened = CreateStore();
            await AssertValueAsync(reopened, "deleted", default, found: false).ConfigureAwait(false);
            Assert.That(await ScanAsync(reopened).ConfigureAwait(false), Is.Empty);
        }

        /// <summary>
        /// Captures ordinal prefix results before later insertions, overwrites and deletions.
        /// </summary>
        [Test]
        public async Task ScanIsAnOrderedIsolatedPrefixSnapshotAsync()
        {
            FileSharedKeyValueStore store = CreateStore();
            await store.SetAsync("pre/b", ByteString.From(0x02)).ConfigureAwait(false);
            await store.SetAsync("pre/a", ByteString.From(0x01)).ConfigureAwait(false);
            await store.SetAsync("Pre/a", ByteString.From(0x90)).ConfigureAwait(false);
            await store.SetAsync("other", ByteString.From(0xff)).ConfigureAwait(false);
            IAsyncEnumerator<KeyValuePair<string, ByteString>> scan = store.ScanAsync("pre/").GetAsyncEnumerator();
            try
            {
                Assert.That(await scan.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(scan.Current.Key, Is.EqualTo("pre/a"));
                Assert.That(scan.Current.Value, Is.EqualTo(ByteString.From(0x01)));
                await store.SetAsync("pre/b", ByteString.From("\""u8)).ConfigureAwait(false);
                await store.SetAsync("pre/c", ByteString.From(0x03)).ConfigureAwait(false);
                Assert.That(await store.DeleteAsync("pre/a").ConfigureAwait(false), Is.True);

                Assert.That(await scan.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(scan.Current.Key, Is.EqualTo("pre/b"));
                Assert.That(scan.Current.Value, Is.EqualTo(ByteString.From(0x02)));
                Assert.That(await scan.MoveNextAsync().ConfigureAwait(false), Is.False);
            }
            finally
            {
                await scan.DisposeAsync().ConfigureAwait(false);
            }
            KeyValuePair<string, ByteString>[] current = await ScanAsync(store, "pre/").ConfigureAwait(false);
            string[] expectedKeys = ["pre/b", "pre/c"];
            Assert.That(current.Select(entry => entry.Key), Is.EqualTo(expectedKeys));
            Assert.That(current[0].Value, Is.EqualTo(ByteString.From("\""u8)));
            Assert.That(current[1].Value, Is.EqualTo(ByteString.From(0x03)));
            Assert.That(await ScanAsync(store, "missing/").ConfigureAwait(false), Is.Empty);
        }

        /// <summary>
        /// Null and empty prefixes enumerate the empty, singleton and ordinal multi-entry snapshots.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        public async Task ScanAllPrefixesIncludeEmptyKeysAsync(string? prefix)
        {
            FileSharedKeyValueStore store = CreateStore();
            Assert.That(await ScanAsync(store, prefix).ConfigureAwait(false), Is.Empty);
            await store.SetAsync(string.Empty, ByteString.From(0x10)).ConfigureAwait(false);
            KeyValuePair<string, ByteString>[] singleton = await ScanAsync(store, prefix).ConfigureAwait(false);
            Assert.That(singleton, Has.Length.EqualTo(1));
            Assert.That(singleton[0].Key, Is.Empty);
            Assert.That(singleton[0].Value, Is.EqualTo(ByteString.From(0x10)));
            await store.SetAsync("é", ByteString.From(" "u8)).ConfigureAwait(false);
            await store.SetAsync("A", ByteString.From("0"u8)).ConfigureAwait(false);
            KeyValuePair<string, ByteString>[] entries = await ScanAsync(store, prefix).ConfigureAwait(false);
            string[] expectedKeys = [string.Empty, "A", "é"];
            Assert.That(entries.Select(entry => entry.Key), Is.EqualTo(expectedKeys));
            Assert.That(entries[1].Value, Is.EqualTo(ByteString.From("0"u8)));
            Assert.That(entries[2].Value, Is.EqualTo(ByteString.From(" "u8)));
        }

        /// <summary>
        /// Applies the complete encrypted-size bound before any state or staged-file mutation.
        /// </summary>
        [TestCase(260, false)]
        [TestCase(261, true)]
        [TestCase(262, true)]
        public async Task ProtectedSnapshotCapacityIncludesEnvelopeAsync(int capacity, bool fits)
        {
            bool armed = false;
            int commitCalls = 0;
            FileSharedKeyValueStore store = CreateStore(
                maximumSnapshotBytes: capacity,
                commitPhase: _ =>
                {
                    if (armed)
                    {
                        commitCalls++;
                    }
                });
            await store.SetAsync("k", ByteString.From("q"u8)).ConfigureAwait(false);
            byte[] before = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            byte[] payload = CreateBytes(151);
            armed = true;

            if (fits)
            {
                await store.SetAsync("k", new ByteString(payload)).ConfigureAwait(false);
                Assert.That(
                    await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                    Has.Length.EqualTo(261));
                Assert.That(commitCalls, Is.EqualTo(5));
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(
                    async () => await store.SetAsync("k", new ByteString(payload)).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(
                    await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                    Is.EqualTo(before));
                Assert.That(commitCalls, Is.Zero);
            }
            ByteString expected = fits ? new ByteString(payload) : ByteString.From("q"u8);
            await AssertValueAsync(store, "k", expected).ConfigureAwait(false);
            await store.VerifyDurabilityAsync().ConfigureAwait(false);
            Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Is.Empty);
            await store.DisposeAsync().ConfigureAwait(false);
            await AssertValueAsync(CreateStore(maximumSnapshotBytes: capacity), "k", expected).ConfigureAwait(false);
        }

        /// <summary>
        /// Copies sliced inputs and get/scan outputs instead of exposing the store's owned memory.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ValuesHaveIndependentOwnershipAsync(bool useCas)
        {
            byte[] input = [0xaa, 0x01, 0x80, 0xff, 0xbb];
            var sliced = new ByteString(input.AsMemory(1, 3));
            FileSharedKeyValueStore store = CreateStore();
            if (useCas)
            {
                Assert.That(await store.CompareAndSwapAsync("owned", default, sliced).ConfigureAwait(false), Is.True);
            }
            else
            {
                await store.SetAsync("owned", sliced).ConfigureAwait(false);
            }
            input[1] = 0x77;
            var expected = ByteString.From(0x01, 0x80, 0xff);
            await AssertValueAsync(store, "owned", expected).ConfigureAwait(false);
            (bool found, ByteString read) = await store.TryGetAsync("owned").ConfigureAwait(false);
            Assert.That(found, Is.True);
            MutateFirstByte(read, 0x66);
            await AssertValueAsync(store, "owned", expected).ConfigureAwait(false);
            KeyValuePair<string, ByteString>[] entries = await ScanAsync(store).ConfigureAwait(false);
            Assert.That(entries, Has.Length.EqualTo(1));
            Assert.That(entries[0].Key, Is.EqualTo("owned"));
            MutateFirstByte(entries[0].Value, 0x55);
            await AssertValueAsync(store, "owned", expected).ConfigureAwait(false);
            await store.DisposeAsync().ConfigureAwait(false);
            await AssertValueAsync(CreateStore(), "owned", expected).ConfigureAwait(false);
        }

        /// <summary>
        /// Does not expose distinctive synthetic key or payload sequences in either protected file.
        /// </summary>
        [Test]
        public async Task CiphertextDoesNotExposeSyntheticDataAsync()
        {
            const string key = "synthetic-key/unique-persistence-canary/鍵";
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] payload = Encoding.UTF8.GetBytes("synthetic-payload/only-a-test/not-a-secret/0123456789");
            FileSharedKeyValueStore store = CreateStore();
            await store.SetAsync(key, new ByteString(payload)).ConfigureAwait(false);

            foreach (string fileName in new[] { "identity.bin", "snapshot.bin" })
            {
                byte[] ciphertext = await ReadAllBytesAsync(StorePath(fileName)).ConfigureAwait(false);
                Assert.That(ciphertext.AsSpan().IndexOf(keyBytes), Is.EqualTo(-1), fileName);
                Assert.That(ciphertext.AsSpan().IndexOf(payload), Is.EqualTo(-1), fileName);
            }
            await store.DisposeAsync().ConfigureAwait(false);
            await AssertValueAsync(CreateStore(), key, new ByteString(payload)).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects damaged or missing committed files before sweeping recoverable stage names.
        /// </summary>
        [TestCase("identity.bin", "corrupt")]
        [TestCase("snapshot.bin", "corrupt")]
        [TestCase("identity.bin", "truncated")]
        [TestCase("snapshot.bin", "truncated")]
        [TestCase("identity.bin", "empty")]
        [TestCase("snapshot.bin", "empty")]
        [TestCase("identity.bin", "oversized")]
        [TestCase("snapshot.bin", "oversized")]
        [TestCase("identity.bin", "missing")]
        [TestCase("snapshot.bin", "missing")]
        public async Task ProtectedFilesRejectDamageBeforeStageCleanupAsync(string fileName, string damage)
        {
            FileSharedKeyValueStore original = CreateStore();
            await original.SetAsync("committed", ByteString.From(0x27, 0x00, 0xff)).ConfigureAwait(false);
            await original.DisposeAsync().ConfigureAwait(false);
            string path = StorePath(fileName);
            byte[] originalBytes = await ReadAllBytesAsync(path).ConfigureAwait(false);
            byte[] damaged = damage switch
            {
                "truncated" => [.. originalBytes.Take(originalBytes.Length - 1)],
                "empty" => [],
                "oversized" => new byte[kMaximumSnapshotBytes + 1],
                _ => [.. originalBytes]
            };
            if (damage == "corrupt")
            {
                damaged[^1] ^= 0x80;
            }
            if (damage == "missing")
            {
                File.Delete(path);
            }
            else
            {
                await WriteAllBytesAsync(path, damaged).ConfigureAwait(false);
            }
            string companion = fileName == "identity.bin" ? "snapshot.bin" : "identity.bin";
            byte[] companionBefore = await ReadAllBytesAsync(StorePath(companion)).ConfigureAwait(false);
            string stage = await CreateStageAsync(ByteString.From(0x12, 0x34, 0x56)).ConfigureAwait(false);
            FileSharedKeyValueStore reopened = CreateStore();

            await AssertRecoveryRejectedAsync(reopened).ConfigureAwait(false);

            Assert.That(File.Exists(path), Is.EqualTo(damage != "missing"));
            if (damage != "missing")
            {
                Assert.That(await ReadAllBytesAsync(path).ConfigureAwait(false), Is.EqualTo(damaged));
            }
            Assert.That(
                await ReadAllBytesAsync(StorePath(companion)).ConfigureAwait(false),
                Is.EqualTo(companionBefore));
            Assert.That(
                await ReadAllBytesAsync(stage).ConfigureAwait(false),
                Is.EqualTo(new byte[] { 0x12, 0x34, 0x56 }));
        }

        /// <summary>
        /// Authenticates the logical store, key material and distinct identity/snapshot contexts.
        /// </summary>
        [TestCase("logical-id")]
        [TestCase("key")]
        [TestCase("identity-context")]
        [TestCase("snapshot-context")]
        public async Task ProtectionBindingMismatchFailsClosedAsync(string mismatch)
        {
            FileSharedKeyValueStore original = CreateStore();
            await original.SetAsync("bound", ByteString.From(0x42, 0x81)).ConfigureAwait(false);
            await original.DisposeAsync().ConfigureAwait(false);
            using AesCbcHmacRecordProtector differentKey = CreateProtector();
            if (mismatch.EndsWith("-context", StringComparison.Ordinal))
            {
                string fileName = mismatch == "identity-context" ? "identity.bin" : "snapshot.bin";
                ByteString plaintext = await ReadPlaintextAsync(fileName).ConfigureAwait(false);
                string wrongContext = fileName == "identity.bin"
                    ? "FileSharedKeyValueStore.Snapshot.1" : "FileSharedKeyValueStore.Identity.1";
                await WriteAuthenticatedFileAsync(fileName, plaintext, wrongContext).ConfigureAwait(false);
            }
            byte[] identity = await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false);
            byte[] snapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            FileSharedKeyValueStore reopened = CreateStore(
                logicalStoreId: mismatch == "logical-id" ? kLogicalStoreId.ToUpperInvariant() : kLogicalStoreId,
                protector: mismatch == "key" ? differentKey : m_protector);

            await AssertRecoveryRejectedAsync(reopened).ConfigureAwait(false);

            Assert.That(await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false), Is.EqualTo(identity));
            Assert.That(await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false), Is.EqualTo(snapshot));
        }

        /// <summary>
        /// Rejects authenticated identity records with the wrong epoch length or an empty epoch.
        /// </summary>
        [TestCase(15)]
        [TestCase(16)]
        [TestCase(17)]
        public async Task AuthenticatedInvalidIdentityFailsClosedAsync(int length)
        {
            FileSharedKeyValueStore original = CreateStore();
            await original.VerifyDurabilityAsync().ConfigureAwait(false);
            await original.DisposeAsync().ConfigureAwait(false);
            await WriteAuthenticatedFileAsync("identity.bin", new ByteString(new byte[length])).ConfigureAwait(false);
            byte[] identity = await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false);
            byte[] snapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);

            await AssertRecoveryRejectedAsync(CreateStore()).ConfigureAwait(false);

            Assert.That(await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false), Is.EqualTo(identity));
            Assert.That(await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false), Is.EqualTo(snapshot));
        }

        /// <summary>
        /// Valid authentication cannot make malformed snapshot fields acceptable.
        /// </summary>
        [TestCase("short-header")]
        [TestCase("schema")]
        [TestCase("epoch")]
        [TestCase("foreign-epoch")]
        [TestCase("revision")]
        [TestCase("negative-count")]
        [TestCase("excessive-count")]
        [TestCase("duplicate")]
        [TestCase("trailing")]
        [TestCase("negative-key")]
        [TestCase("key-overrun")]
        [TestCase("invalid-utf8")]
        [TestCase("negative-value")]
        [TestCase("value-overrun")]
        [TestCase("missing-value-header")]
        public async Task AuthenticatedMalformedSnapshotFailsClosedAsync(string defect)
        {
            FileSharedKeyValueStore original = CreateStore();
            await original.SetAsync("committed", ByteString.From(0x19, 0xff)).ConfigureAwait(false);
            await original.DisposeAsync().ConfigureAwait(false);
            var epoch = new Guid((await ReadPlaintextAsync("identity.bin").ConfigureAwait(false)).ToArray());
            await WriteAuthenticatedFileAsync("snapshot.bin", CreateMalformedSnapshot(defect, epoch))
                .ConfigureAwait(false);
            byte[] snapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            string stage = await CreateStageAsync(ByteString.From(0xba, 0xd0)).ConfigureAwait(false);
            FileSharedKeyValueStore reopened = CreateStore();

            await AssertRecoveryRejectedAsync(
                reopened, truncated: defect == "missing-value-header").ConfigureAwait(false);

            Assert.That(await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false), Is.EqualTo(snapshot));
            Assert.That(await ReadAllBytesAsync(stage).ConfigureAwait(false), Is.EqualTo(new byte[] { 0xba, 0xd0 }));
        }

        /// <summary>
        /// Rejects revision overflow without publishing or faulting an otherwise healthy generation.
        /// </summary>
        [Test]
        public async Task RevisionOverflowPreservesCommittedStateAsync()
        {
            FileSharedKeyValueStore original = CreateStore();
            await original.SetAsync("revision", ByteString.From("1"u8)).ConfigureAwait(false);
            await original.DisposeAsync().ConfigureAwait(false);
            byte[] plaintext = (await ReadPlaintextAsync("snapshot.bin").ConfigureAwait(false)).ToArray();
            using (var stream = new MemoryStream(plaintext, writable: true))
            using (var writer = new BinaryWriter(stream))
            {
                stream.Position = 20;
                writer.Write(ulong.MaxValue);
            }
            await WriteAuthenticatedFileAsync("snapshot.bin", new ByteString(plaintext)).ConfigureAwait(false);
            byte[] snapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            FileSharedKeyValueStore reopened = CreateStore();
            await AssertValueAsync(reopened, "revision", ByteString.From("1"u8)).ConfigureAwait(false);

            await Assert.ThrowsAsync<OverflowException>(
                async () => await reopened.SetAsync("revision", ByteString.From("2"u8)).ConfigureAwait(false))
                .ConfigureAwait(false);

            await reopened.VerifyDurabilityAsync().ConfigureAwait(false);
            await AssertValueAsync(reopened, "revision", ByteString.From("1"u8)).ConfigureAwait(false);
            Assert.That(await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false), Is.EqualTo(snapshot));
            Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Is.Empty);
        }

        /// <summary>
        /// Preserves unknown files, directories and malformed stage names during rejected recovery.
        /// </summary>
        [TestCase("unknown.bin", false)]
        [TestCase("unknown-directory", true)]
        [TestCase("stage-short.bin", false)]
        [TestCase("stage-zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz.bin", false)]
        [TestCase("Stage-0123456789abcdef0123456789abcdef.bin", false)]
        public async Task UnexpectedRecoveryContentIsPreservedAsync(string name, bool directory)
        {
            FileSharedKeyValueStore original = CreateStore();
            await original.SetAsync("committed", ByteString.From("u"u8)).ConfigureAwait(false);
            await original.DisposeAsync().ConfigureAwait(false);
            string unknown = StorePath(name);
            if (directory)
            {
                Directory.CreateDirectory(unknown);
            }
            else
            {
                await WriteAllBytesAsync(unknown, "qr"u8.ToArray()).ConfigureAwait(false);
            }
            byte[] snapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);

            await AssertRecoveryRejectedAsync(CreateStore()).ConfigureAwait(false);

            Assert.That(await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false), Is.EqualTo(snapshot));
            if (directory)
            {
                Assert.That(Directory.Exists(unknown), Is.True);
            }
            else
            {
                Assert.That(await ReadAllBytesAsync(unknown).ConfigureAwait(false), Is.EqualTo("qr"u8.ToArray()));
            }
        }

        /// <summary>
        /// A lone abandoned stage does not qualify an uninitialized root for empty-store recovery.
        /// </summary>
        [Test]
        public async Task StageOnlyRootFailsWithoutDeletingTheStageAsync()
        {
            Directory.CreateDirectory(m_storeDirectory);
            string stage = await CreateStageAsync(ByteString.From(0x16, 0x00, 0xff)).ConfigureAwait(false);

            await AssertRecoveryRejectedAsync(CreateStore()).ConfigureAwait(false);

            Assert.That(
                await ReadAllBytesAsync(stage).ConfigureAwait(false),
                Is.EqualTo(new byte[] { 0x16, 0x00, 0xff }));
            Assert.That(File.Exists(StorePath("identity.bin")), Is.False);
            Assert.That(File.Exists(StorePath("snapshot.bin")), Is.False);
        }

        /// <summary>
        /// Valid and partial abandoned stages never replace the authenticated committed primary.
        /// </summary>
        [Test]
        public async Task AbandonedStagesCannotReplacePrimaryAsync()
        {
            FileSharedKeyValueStore original = CreateStore();
            await original.SetAsync("committed", ByteString.From(0x01)).ConfigureAwait(false);
            byte[] olderSnapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            await original.SetAsync("committed", ByteString.From(0x02)).ConfigureAwait(false);
            byte[] latestSnapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            await original.DisposeAsync().ConfigureAwait(false);
            string olderStage = await CreateStageAsync(new ByteString(olderSnapshot)).ConfigureAwait(false);
            string incompleteStage = await CreateStageAsync(ByteString.From(0xff), upperCaseGuid: true)
                .ConfigureAwait(false);
            FileSharedKeyValueStore reopened = CreateStore();

            await AssertValueAsync(reopened, "committed", ByteString.From(0x02)).ConfigureAwait(false);

            Assert.That(
                await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                Is.EqualTo(latestSnapshot));
            Assert.That(File.Exists(olderStage), Is.False);
            Assert.That(File.Exists(incompleteStage), Is.False);
            Assert.That(
                Directory.GetFiles(m_storeDirectory).Select(Path.GetFileName),
                Is.EquivalentTo(s_committedFileNames));
        }

        /// <summary>
        /// Interrupted changes neither notify nor allow stale reads; reopen observes the real publication boundary.
        /// </summary>
        [TestCase((int)FileStoreCommitPhase.BeforeStage, false)]
        [TestCase((int)FileStoreCommitPhase.AfterStage, false)]
        [TestCase((int)FileStoreCommitPhase.BeforePublish, false)]
        [TestCase((int)FileStoreCommitPhase.AfterPublish, false)]
        [TestCase((int)FileStoreCommitPhase.AfterDirectorySync, false)]
        [TestCase((int)FileStoreCommitPhase.BeforeStage, true)]
        [TestCase((int)FileStoreCommitPhase.AfterStage, true)]
        [TestCase((int)FileStoreCommitPhase.BeforePublish, true)]
        [TestCase((int)FileStoreCommitPhase.AfterPublish, true)]
        [TestCase((int)FileStoreCommitPhase.AfterDirectorySync, true)]
        public async Task InterruptedMutationReopensAtPublicationBoundaryAsync(int phaseNumber, bool delete)
        {
            var faultPhase = (FileStoreCommitPhase)phaseNumber;
            var injected = new IOException("Injected publication boundary: " + faultPhase);
            bool armed = false;
            FileSharedKeyValueStore store = CreateStore(commitPhase: phase =>
            {
                if (armed && phase == faultPhase)
                {
                    throw injected;
                }
            });
            await store.SetAsync("target", ByteString.From("A"u8)).ConfigureAwait(false);
            await store.SetAsync("previously-deleted", ByteString.From("D"u8)).ConfigureAwait(false);
            Assert.That(await store.DeleteAsync("previously-deleted").ConfigureAwait(false), Is.True);
            byte[] oldSnapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            IAsyncEnumerator<KeyValueChange> watcher = store.WatchAsync(string.Empty).GetAsyncEnumerator();
            Task<bool> pending = watcher.MoveNextAsync().AsTask();
            try
            {
                armed = true;
                IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                {
                    if (delete)
                    {
                        await store.DeleteAsync("target").ConfigureAwait(false);
                    }
                    else
                    {
                        await store.SetAsync("target", ByteString.From("B"u8)).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
                Assert.That(error, Is.SameAs(injected));
                await AssertFaultedAsync(store).ConfigureAwait(false);
                await store.DisposeAsync().ConfigureAwait(false);
                Assert.That(await pending.WaitAsync(s_watchdog).ConfigureAwait(false), Is.False);
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
                await FinishWatcherAsync(watcher, pending).ConfigureAwait(false);
            }
            bool published = faultPhase is FileStoreCommitPhase.AfterPublish or FileStoreCommitPhase.AfterDirectorySync;
            byte[] committedSnapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            Assert.That(committedSnapshot.SequenceEqual(oldSnapshot), Is.EqualTo(!published));
            int abandonedCount = faultPhase is FileStoreCommitPhase.AfterStage or FileStoreCommitPhase.BeforePublish
                ? 1 : 0;
            Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Has.Length.EqualTo(abandonedCount));
            FileSharedKeyValueStore reopened = CreateStore();
            ByteString expected = published ? delete ? default : ByteString.From("B"u8) : ByteString.From("A"u8);
            await AssertValueAsync(reopened, "target", expected, found: !published || !delete).ConfigureAwait(false);
            await AssertValueAsync(reopened, "previously-deleted", default, found: false).ConfigureAwait(false);
            Assert.That(
                await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                Is.EqualTo(committedSnapshot));
            Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Is.Empty);
        }

        /// <summary>
        /// Failed initialization releases ownership and never promotes incomplete stages during recovery.
        /// </summary>
        [TestCase((int)FileStoreCommitPhase.BeforeStage)]
        [TestCase((int)FileStoreCommitPhase.AfterStage)]
        [TestCase((int)FileStoreCommitPhase.BeforePublish)]
        [TestCase((int)FileStoreCommitPhase.AfterPublish)]
        [TestCase((int)FileStoreCommitPhase.AfterDirectorySync)]
        public async Task InitializationFailureReleasesOwnershipAsync(int phaseNumber)
        {
            var faultPhase = (FileStoreCommitPhase)phaseNumber;
            var injected = new IOException("Injected initialization boundary: " + faultPhase);
            FileSharedKeyValueStore store = CreateStore(commitPhase: phase =>
            {
                if (phase == faultPhase)
                {
                    throw injected;
                }
            });

            IOException? error = await Assert.ThrowsAsync<IOException>(
                async () => await store.VerifyDurabilityAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error, Is.SameAs(injected));
            await AssertFaultedAsync(store).ConfigureAwait(false);
            using (var owner = new FileStream(
                StorePath("owner.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(owner.Length, Is.Zero);
            }
            Assert.That(File.Exists(StorePath("identity.bin")), Is.True);
            bool published = faultPhase is FileStoreCommitPhase.AfterPublish or FileStoreCommitPhase.AfterDirectorySync;
            Assert.That(File.Exists(StorePath("snapshot.bin")), Is.EqualTo(published));
            string[] stages = Directory.GetFiles(m_storeDirectory, "stage-*");
            int stageCount = faultPhase is FileStoreCommitPhase.AfterStage or FileStoreCommitPhase.BeforePublish
                ? 1 : 0;
            Assert.That(stages, Has.Length.EqualTo(stageCount));
            FileSharedKeyValueStore reopened = CreateStore();
            if (published)
            {
                byte[] snapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
                await reopened.VerifyDurabilityAsync().ConfigureAwait(false);
                Assert.That(await ScanAsync(reopened).ConfigureAwait(false), Is.Empty);
                Assert.That(
                    await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                    Is.EqualTo(snapshot));
            }
            else
            {
                await AssertRecoveryRejectedAsync(reopened).ConfigureAwait(false);
                Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Is.EquivalentTo(stages));
                Assert.That(File.Exists(StorePath("snapshot.bin")), Is.False);
            }
        }

        /// <summary>
        /// Holds one real live owner and releases its lock for an explicit subsequent instance.
        /// </summary>
        [Test]
        public async Task LiveOwnershipIsExclusiveAndReleasedAsync()
        {
            FileSharedKeyValueStore owner = CreateStore();
            await owner.SetAsync("exclusive", ByteString.From(0x11, 0x77)).ConfigureAwait(false);
            byte[] snapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            FileSharedKeyValueStore contender = CreateStore();

            await Assert.ThrowsAsync<IOException>(
                async () => await contender.VerifyDurabilityAsync().ConfigureAwait(false)).ConfigureAwait(false);

            await AssertValueAsync(owner, "exclusive", ByteString.From(0x11, 0x77)).ConfigureAwait(false);
            Assert.That(await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false), Is.EqualTo(snapshot));
            await contender.DisposeAsync().ConfigureAwait(false);
            await owner.DisposeAsync().ConfigureAwait(false);
            await AssertValueAsync(CreateStore(), "exclusive", ByteString.From(0x11, 0x77)).ConfigureAwait(false);
        }

        /// <summary>
        /// Delivers ordered committed changes, not history, other prefixes or unsuccessful/no-op operations.
        /// </summary>
        [Test]
        public async Task WatchReportsOnlyOrderedCommittedChangesAsync()
        {
            FileSharedKeyValueStore store = CreateStore();
            await store.SetAsync("watch/history", ByteString.From("\t"u8)).ConfigureAwait(false);
            IAsyncEnumerator<KeyValueChange> watcher = store.WatchAsync("watch/").GetAsyncEnumerator();
            Task<bool> pending = watcher.MoveNextAsync().AsTask();
            try
            {
                await store.SetAsync("other/key", ByteString.From(0x80)).ConfigureAwait(false);
                await store.SetAsync("Watch/key", ByteString.From(0x81)).ConfigureAwait(false);
                await store.SetAsync("watch/key", ByteString.From(0x01)).ConfigureAwait(false);
                await AssertChangeAsync(watcher, pending, KeyValueChangeKind.Set, "watch/key", ByteString.From(0x01))
                    .ConfigureAwait(false);
                await AssertValueAsync(store, "watch/key", ByteString.From(0x01)).ConfigureAwait(false);

                pending = watcher.MoveNextAsync().AsTask();
                Assert.That(
                    await store.CompareAndSwapAsync("watch/key", ByteString.From(0x99), ByteString.From(0x88))
                        .ConfigureAwait(false),
                    Is.False);
                Assert.That(await store.DeleteAsync("watch/missing").ConfigureAwait(false), Is.False);
                Assert.That(
                    await store.CompareAndSwapAsync("watch/missing", default, default).ConfigureAwait(false), Is.True);
                await store.SetAsync("watch/key", ByteString.From(0x02)).ConfigureAwait(false);
                await AssertChangeAsync(watcher, pending, KeyValueChangeKind.Set, "watch/key", ByteString.From(0x02))
                    .ConfigureAwait(false);

                pending = watcher.MoveNextAsync().AsTask();
                Assert.That(
                    await store.CompareAndSwapAsync("watch/key", ByteString.From(0x02), ByteString.From(0x03))
                        .ConfigureAwait(false),
                    Is.True);
                await AssertChangeAsync(watcher, pending, KeyValueChangeKind.Set, "watch/key", ByteString.From(0x03))
                    .ConfigureAwait(false);
                await AssertValueAsync(store, "watch/key", ByteString.From(0x03)).ConfigureAwait(false);

                pending = watcher.MoveNextAsync().AsTask();
                Assert.That(
                    await store.CompareAndSwapAsync("watch/key", ByteString.From(0x03), default).ConfigureAwait(false),
                    Is.True);
                await AssertChangeAsync(watcher, pending, KeyValueChangeKind.Delete, "watch/key", default)
                    .ConfigureAwait(false);
                await AssertValueAsync(store, "watch/key", default, found: false).ConfigureAwait(false);

                pending = watcher.MoveNextAsync().AsTask();
                await store.SetAsync("watch/null", default).ConfigureAwait(false);
                await AssertChangeAsync(watcher, pending, KeyValueChangeKind.Set, "watch/null", default)
                    .ConfigureAwait(false);
                await AssertValueAsync(store, "watch/null", default).ConfigureAwait(false);

                pending = watcher.MoveNextAsync().AsTask();
                Assert.That(await store.DeleteAsync("watch/null").ConfigureAwait(false), Is.True);
                await AssertChangeAsync(watcher, pending, KeyValueChangeKind.Delete, "watch/null", default)
                    .ConfigureAwait(false);
                pending = watcher.MoveNextAsync().AsTask();
                await store.DisposeAsync().ConfigureAwait(false);
                Assert.That(await pending.WaitAsync(s_watchdog).ConfigureAwait(false), Is.False);
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
                await FinishWatcherAsync(watcher, pending).ConfigureAwait(false);
            }
            FileSharedKeyValueStore reopened = CreateStore();
            await AssertValueAsync(reopened, "watch/key", default, found: false).ConfigureAwait(false);
            await AssertValueAsync(reopened, "watch/null", default, found: false).ConfigureAwait(false);
            await AssertValueAsync(reopened, "other/key", ByteString.From(0x80)).ConfigureAwait(false);
        }

        /// <summary>
        /// Preserves every buffered event up to capacity and reports overflow instead of silent loss.
        /// </summary>
        [TestCase(15)]
        [TestCase(16)]
        [TestCase(17)]
        public async Task WatchOverflowRequiresResnapshotAsync(int bufferedChanges)
        {
            FileSharedKeyValueStore store = CreateStore();
            await store.VerifyDurabilityAsync().ConfigureAwait(false);
            IAsyncEnumerator<KeyValueChange> watcher = store.WatchAsync("key").GetAsyncEnumerator();
            Task<bool> pending = watcher.MoveNextAsync().AsTask();
            try
            {
                await store.SetAsync("key", ByteString.From(0)).ConfigureAwait(false);
                await AssertChangeAsync(watcher, pending, KeyValueChangeKind.Set, "key", ByteString.From(0))
                    .ConfigureAwait(false);
                for (int index = 1; index <= bufferedChanges; index++)
                {
                    await store.SetAsync("key", ByteString.From((byte)index)).ConfigureAwait(false);
                }
                for (int index = 1; index <= Math.Min(bufferedChanges, 16); index++)
                {
                    pending = watcher.MoveNextAsync().AsTask();
                    await AssertChangeAsync(
                        watcher, pending, KeyValueChangeKind.Set, "key", ByteString.From((byte)index))
                        .ConfigureAwait(false);
                }
                pending = watcher.MoveNextAsync().AsTask();
                if (bufferedChanges > 16)
                {
                    await Assert.ThrowsAsync<IOException>(
                        async () => await pending.WaitAsync(s_watchdog).ConfigureAwait(false)).ConfigureAwait(false);
                    await store.VerifyDurabilityAsync().ConfigureAwait(false);
                    await AssertValueAsync(store, "key", ByteString.From(17)).ConfigureAwait(false);
                }
                else
                {
                    await store.DisposeAsync().ConfigureAwait(false);
                    Assert.That(await pending.WaitAsync(s_watchdog).ConfigureAwait(false), Is.False);
                }
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
                await FinishWatcherAsync(watcher, pending).ConfigureAwait(false);
            }
            await AssertValueAsync(CreateStore(), "key", ByteString.From((byte)bufferedChanges)).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancels a pending all-keys watch without faulting the initialized store.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        public async Task WatchCancellationPreservesStoreAsync(string? prefix)
        {
            FileSharedKeyValueStore store = CreateStore();
            await store.SetAsync("before", ByteString.From(0x10)).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            IAsyncEnumerator<KeyValueChange> watcher =
                store.WatchAsync(prefix!, cancellation.Token).GetAsyncEnumerator();
            Task<bool> pending = watcher.MoveNextAsync().AsTask();
            try
            {
                await store.SetAsync("any/key", ByteString.From(" "u8)).ConfigureAwait(false);
                await AssertChangeAsync(watcher, pending, KeyValueChangeKind.Set, "any/key", ByteString.From(" "u8))
                    .ConfigureAwait(false);
                pending = MoveNextPreservingCancellationAsync(watcher);
                await cancellation.CancelAsync().ConfigureAwait(false);

                OperationCanceledException? error = await Assert.CatchAsync<OperationCanceledException>(
                    async () => await pending.WaitAsync(s_watchdog).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                await FinishWatcherAsync(watcher, pending).ConfigureAwait(false);
            }
            await store.VerifyDurabilityAsync().ConfigureAwait(false);
            await store.SetAsync("after", ByteString.From("0"u8)).ConfigureAwait(false);
            await AssertValueAsync(store, "before", ByteString.From(0x10)).ConfigureAwait(false);
            await AssertValueAsync(store, "any/key", ByteString.From(" "u8)).ConfigureAwait(false);
            await AssertValueAsync(store, "after", ByteString.From("0"u8)).ConfigureAwait(false);
        }

        /// <summary>
        /// A watcher owns its returned value and may be disposed without closing the store.
        /// </summary>
        [Test]
        public async Task WatchValueAndEnumeratorHaveIndependentOwnershipAsync()
        {
            FileSharedKeyValueStore store = CreateStore();
            await store.VerifyDurabilityAsync().ConfigureAwait(false);
            IAsyncEnumerator<KeyValueChange> watcher = store.WatchAsync("owned").GetAsyncEnumerator();
            Task<bool> pending = watcher.MoveNextAsync().AsTask();
            try
            {
                await store.SetAsync("owned", ByteString.From(0x15, 0xff)).ConfigureAwait(false);
                await AssertChangeAsync(watcher, pending, KeyValueChangeKind.Set, "owned", ByteString.From(0x15, 0xff))
                    .ConfigureAwait(false);
                MutateFirstByte(watcher.Current.Value, 0x99);
                await AssertValueAsync(store, "owned", ByteString.From(0x15, 0xff)).ConfigureAwait(false);
                await watcher.DisposeAsync().ConfigureAwait(false);
                await store.SetAsync("owned", ByteString.From(0x16, 0xff)).ConfigureAwait(false);
                Assert.That(await watcher.MoveNextAsync().ConfigureAwait(false), Is.False);
                await AssertValueAsync(store, "owned", ByteString.From(0x16, 0xff)).ConfigureAwait(false);
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
                await FinishWatcherAsync(watcher, pending).ConfigureAwait(false);
            }
            await AssertValueAsync(CreateStore(), "owned", ByteString.From(0x16, 0xff)).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects invalid keys at each keyed API before creating any store files.
        /// </summary>
        [TestCase("get", true)]
        [TestCase("set", true)]
        [TestCase("cas", true)]
        [TestCase("delete", true)]
        [TestCase("get", false)]
        [TestCase("set", false)]
        [TestCase("cas", false)]
        [TestCase("delete", false)]
        public async Task InvalidKeysDoNotTouchStorageAsync(string operation, bool nullKey)
        {
            FileSharedKeyValueStore store = CreateStore();
            string? key = nullKey ? null : "\uD800";
            if (nullKey)
            {
                ArgumentNullException? error = await Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await ExerciseOperationAsync(store, operation, key: key).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(error!.ParamName, Is.EqualTo("key"));
            }
            else
            {
                await Assert.ThrowsAsync<EncoderFallbackException>(
                    async () => await ExerciseOperationAsync(store, operation, key: key).ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
            Assert.That(Directory.Exists(m_storeDirectory), Is.False);
        }

        /// <summary>
        /// Rejects an invalid UTF-16 logical identity rather than normalizing its authenticated context.
        /// </summary>
        [Test]
        public void ConstructorRejectsMalformedLogicalIdentity()
        {
            Assert.Throws<EncoderFallbackException>(
                () => new FileSharedKeyValueStore(m_storeDirectory, "\uD800", m_protector, 256));

            Assert.That(Directory.Exists(m_storeDirectory), Is.False);
        }

        /// <summary>
        /// Preserves the caller's already-cancelled token and performs no initialization I/O.
        /// </summary>
        [TestCase("get")]
        [TestCase("set")]
        [TestCase("cas")]
        [TestCase("delete")]
        [TestCase("scan")]
        [TestCase("watch")]
        [TestCase("ready")]
        public async Task PreCancellationDoesNotTouchStorageAsync(string operation)
        {
            FileSharedKeyValueStore store = CreateStore();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            OperationCanceledException? error = await Assert.CatchAsync<OperationCanceledException>(
                async () => await ExerciseOperationAsync(store, operation, cancellationToken: cancellation.Token)
                    .WaitAsync(s_watchdog).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(Directory.Exists(m_storeDirectory), Is.False);
            await store.VerifyDurabilityAsync().ConfigureAwait(false);
            await AssertValueAsync(store, "key", default, found: false).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancels queued read or write work without cancelling a different active commit or faulting the store.
        /// </summary>
        [TestCase("get")]
        [TestCase("set")]
        public async Task QueuedCancellationPreservesHealthyStoreAsync(string operation)
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool armed = false;
            FileSharedKeyValueStore store = CreateStore(commitPhase: phase =>
            {
                if (armed && phase == FileStoreCommitPhase.BeforeStage)
                {
                    entered.TrySetResult(true);
                    if (!release.Wait(s_watchdog))
                    {
                        throw new TimeoutException("The test did not release its commit gate.");
                    }
                }
            });
            await store.SetAsync("key", ByteString.From("1"u8)).ConfigureAwait(false);
            armed = true;
            var mutation = Task.Run(
                async () => await store.SetAsync("active", ByteString.From("B"u8)).ConfigureAwait(false));
            try
            {
                await entered.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
                using var cancellation = new CancellationTokenSource();
                Task queued = ExerciseOperationAsync(store, operation, cancellationToken: cancellation.Token);
                Assert.That(queued.IsCompleted, Is.False, "The active operation owns the gate.");
                await cancellation.CancelAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(
                    async () => await queued.WaitAsync(s_watchdog).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(mutation.IsCompleted, Is.False);
            }
            finally
            {
                release.Set();
                await mutation.WaitAsync(s_watchdog).ConfigureAwait(false);
            }
            await store.VerifyDurabilityAsync().ConfigureAwait(false);
            await AssertValueAsync(store, "key", ByteString.From("1"u8)).ConfigureAwait(false);
            await AssertValueAsync(store, "active", ByteString.From("B"u8)).ConfigureAwait(false);
            await store.DisposeAsync().ConfigureAwait(false);
            FileSharedKeyValueStore reopened = CreateStore();
            await AssertValueAsync(reopened, "key", ByteString.From("1"u8)).ConfigureAwait(false);
            await AssertValueAsync(reopened, "active", ByteString.From("B"u8)).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancellation between scan results leaves both the store and committed snapshot healthy.
        /// </summary>
        [Test]
        public async Task ScanCancellationDoesNotFaultHealthyStoreAsync()
        {
            FileSharedKeyValueStore store = CreateStore();
            await store.SetAsync("a", ByteString.From(0x01)).ConfigureAwait(false);
            await store.SetAsync("b", ByteString.From(0x02)).ConfigureAwait(false);
            byte[] snapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            IAsyncEnumerator<KeyValuePair<string, ByteString>> scan =
                store.ScanAsync(string.Empty, cancellation.Token).GetAsyncEnumerator();
            try
            {
                Assert.That(await scan.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(scan.Current.Key, Is.EqualTo("a"));
                Assert.That(scan.Current.Value, Is.EqualTo(ByteString.From(0x01)));
                await cancellation.CancelAsync().ConfigureAwait(false);

                OperationCanceledException? error = await Assert.CatchAsync<OperationCanceledException>(
                    async () => await scan.MoveNextAsync().ConfigureAwait(false)).ConfigureAwait(false);

                Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            }
            finally
            {
                await scan.DisposeAsync().ConfigureAwait(false);
            }
            await store.VerifyDurabilityAsync().ConfigureAwait(false);
            await AssertValueAsync(store, "a", ByteString.From(0x01)).ConfigureAwait(false);
            await AssertValueAsync(store, "b", ByteString.From(0x02)).ConfigureAwait(false);
            Assert.That(await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false), Is.EqualTo(snapshot));
        }

        /// <summary>
        /// Linked operation cancellation survives the lease lifetime and concurrent disposal completes once.
        /// </summary>
        [Test]
        public async Task ConcurrentDisposalCancelsActiveAndQueuedOperationsAsync()
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool armed = false;
            FileSharedKeyValueStore store = CreateStore(stageFileCreated: () =>
            {
                if (armed)
                {
                    entered.TrySetResult(true);
                    if (!release.Wait(s_watchdog))
                    {
                        throw new TimeoutException("The test did not release its commit gate.");
                    }
                }
            });
            await store.SetAsync("key", ByteString.From("a"u8)).ConfigureAwait(false);
            byte[] snapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            IAsyncEnumerator<KeyValueChange> watcher = store.WatchAsync(string.Empty).GetAsyncEnumerator();
            Task<bool> pending = watcher.MoveNextAsync().AsTask();
            armed = true;
            var mutation = Task.Run(
                async () => await store.SetAsync("key", ByteString.From("b"u8)).ConfigureAwait(false));
            try
            {
                await entered.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
                Task queued = store.TryGetAsync("key").AsTask();
                Assert.That(queued.IsCompleted, Is.False);
                Task firstDispose = store.DisposeAsync().AsTask();
                Task secondDispose = store.DisposeAsync().AsTask();
                Assert.That(firstDispose.IsCompleted, Is.False);
                Assert.That(secondDispose.IsCompleted, Is.False);
                await Assert.CatchAsync<OperationCanceledException>(
                    async () => await queued.WaitAsync(s_watchdog).ConfigureAwait(false)).ConfigureAwait(false);
                release.Set();
                await Assert.CatchAsync<OperationCanceledException>(
                    async () => await mutation.WaitAsync(s_watchdog).ConfigureAwait(false)).ConfigureAwait(false);
                await Task.WhenAll(firstDispose, secondDispose).WaitAsync(s_watchdog).ConfigureAwait(false);
                Assert.That(await pending.WaitAsync(s_watchdog).ConfigureAwait(false), Is.False);
            }
            finally
            {
                release.Set();
                await store.DisposeAsync().AsTask().WaitAsync(s_watchdog).ConfigureAwait(false);
                await FinishWatcherAsync(watcher, pending).ConfigureAwait(false);
            }
            Assert.That(await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false), Is.EqualTo(snapshot));
            Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Is.Empty);
            await AssertValueAsync(CreateStore(), "key", ByteString.From("a"u8)).ConfigureAwait(false);
        }

        /// <summary>
        /// Repeated disposal of an unopened store performs no I/O and rejects all later asynchronous entry points.
        /// </summary>
        [TestCase("get")]
        [TestCase("set")]
        [TestCase("cas")]
        [TestCase("delete")]
        [TestCase("scan")]
        [TestCase("watch")]
        [TestCase("ready")]
        public async Task DisposedStoreRejectsOperationsAsync(string operation)
        {
            FileSharedKeyValueStore store = CreateStore();
            await store.DisposeAsync().ConfigureAwait(false);
            await store.DisposeAsync().ConfigureAwait(false);

            await Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await ExerciseOperationAsync(store, operation).WaitAsync(s_watchdog).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(Directory.Exists(m_storeDirectory), Is.False);
        }

        /// <summary>
        /// A loaded owner checks committed files before returning either cached values or missing-key results.
        /// </summary>
        [TestCase("identity.bin")]
        [TestCase("snapshot.bin")]
        public async Task LiveOwnerRejectsChangedCommittedFilesAsync(string fileName)
        {
            FileSharedKeyValueStore store = CreateStore();
            await store.SetAsync("committed", ByteString.From(0x15, 0x00, 0xff)).ConfigureAwait(false);
            byte[] damaged = await ReadAllBytesAsync(StorePath(fileName)).ConfigureAwait(false);
            damaged[^1] ^= 0x80;
            await WriteAllBytesAsync(StorePath(fileName), damaged).ConfigureAwait(false);

            await Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.TryGetAsync("committed").ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.TryGetAsync("missing").ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.VerifyDurabilityAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(await ReadAllBytesAsync(StorePath(fileName)).ConfigureAwait(false), Is.EqualTo(damaged));
            Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Is.Empty);
            await store.DisposeAsync().ConfigureAwait(false);
            await AssertRecoveryRejectedAsync(CreateStore()).ConfigureAwait(false);
        }

        /// <summary>
        /// Scan and concurrent watchers distinguish composed and decomposed Unicode prefixes literally.
        /// </summary>
        [Test]
        public async Task OrdinalUnicodePrefixesRemainDistinctAsync()
        {
            FileSharedKeyValueStore store = CreateStore();
            await store.SetAsync("é/original", ByteString.From(0x11)).ConfigureAwait(false);
            await store.SetAsync("e\u0301/original", ByteString.From("\""u8)).ConfigureAwait(false);
            KeyValuePair<string, ByteString>[] composed = await ScanAsync(store, "é/").ConfigureAwait(false);
            KeyValuePair<string, ByteString>[] decomposed = await ScanAsync(store, "e\u0301/").ConfigureAwait(false);
            Assert.That(composed, Has.Length.EqualTo(1));
            Assert.That(composed[0].Key, Is.EqualTo("é/original"));
            Assert.That(composed[0].Value, Is.EqualTo(ByteString.From(0x11)));
            Assert.That(decomposed, Has.Length.EqualTo(1));
            Assert.That(decomposed[0].Key, Is.EqualTo("e\u0301/original"));
            Assert.That(decomposed[0].Value, Is.EqualTo(ByteString.From("\""u8)));
            IAsyncEnumerator<KeyValueChange> first = store.WatchAsync("é/").GetAsyncEnumerator();
            IAsyncEnumerator<KeyValueChange> second = store.WatchAsync("e\u0301/").GetAsyncEnumerator();
            Task<bool> firstPending = MoveNextPreservingCancellationAsync(first);
            Task<bool> secondPending = MoveNextPreservingCancellationAsync(second);
            try
            {
                await store.SetAsync("e\u0301/new", ByteString.From("D"u8)).ConfigureAwait(false);
                await store.SetAsync("é/new", ByteString.From("3"u8)).ConfigureAwait(false);
                await AssertChangeAsync(first, firstPending, KeyValueChangeKind.Set, "é/new", ByteString.From("3"u8))
                    .ConfigureAwait(false);
                await AssertChangeAsync(
                    second, secondPending, KeyValueChangeKind.Set, "e\u0301/new", ByteString.From("D"u8))
                    .ConfigureAwait(false);
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
                await FinishWatcherAsync(first, firstPending).ConfigureAwait(false);
                await FinishWatcherAsync(second, secondPending).ConfigureAwait(false);
            }
            FileSharedKeyValueStore reopened = CreateStore();
            await AssertValueAsync(reopened, "é/new", ByteString.From("3"u8)).ConfigureAwait(false);
            await AssertValueAsync(reopened, "e\u0301/new", ByteString.From("D"u8)).ConfigureAwait(false);
        }

        /// <summary>
        /// Serializes competing absence checks so exactly one CAS commits and the loser changes no generation.
        /// </summary>
        [Test]
        public async Task CompetingCompareAndSwapHasOnePersistentWinnerAsync()
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool armed = false;
            int commits = 0;
            FileSharedKeyValueStore store = CreateStore(commitPhase: phase =>
            {
                if (armed && phase == FileStoreCommitPhase.BeforeStage && Interlocked.Increment(ref commits) == 1)
                {
                    entered.TrySetResult(true);
                    if (!release.Wait(s_watchdog))
                    {
                        throw new TimeoutException("The test did not release its CAS gate.");
                    }
                }
            });
            await store.VerifyDurabilityAsync().ConfigureAwait(false);
            armed = true;
            Task<bool> first = Task.Run(
                async () => await store.CompareAndSwapAsync("winner", default, ByteString.From(0x11))
                    .ConfigureAwait(false));
            Task<bool>? second = null;
            try
            {
                await entered.Task.WaitAsync(s_watchdog).ConfigureAwait(false);
                second = store.CompareAndSwapAsync("winner", default, ByteString.From("\""u8)).AsTask();
                Assert.That(second.IsCompleted, Is.False, "The first CAS owns the publication gate.");
            }
            finally
            {
                release.Set();
                await first.WaitAsync(s_watchdog).ConfigureAwait(false);
                if (second != null)
                {
                    await second.WaitAsync(s_watchdog).ConfigureAwait(false);
                }
            }
            Assert.That(await first.ConfigureAwait(false), Is.True);
            Assert.That(await second!.ConfigureAwait(false), Is.False);
            Assert.That(commits, Is.EqualTo(1));
            await AssertValueAsync(store, "winner", ByteString.From(0x11)).ConfigureAwait(false);
            await store.DisposeAsync().ConfigureAwait(false);
            await AssertValueAsync(CreateStore(), "winner", ByteString.From(0x11)).ConfigureAwait(false);
        }

        /// <summary>
        /// The optional capacity contract reports applied insertion and replacement with durable exact values.
        /// </summary>
        [Test]
        public async Task CapacityCasAppliedTransitionsPersistAsync()
        {
            const int capacity = 261;
            bool armed = false;
            int commitCalls = 0;
            FileSharedKeyValueStore store = CreateStore(
                maximumSnapshotBytes: capacity,
                commitPhase: _ =>
                {
                    if (armed)
                    {
                        commitCalls++;
                    }
                });
            await store.VerifyDurabilityAsync().ConfigureAwait(false);
            byte[] identity = await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false);
            byte[] emptySnapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            armed = true;

            SharedKeyValueStoreWriteResult inserted = await store.TryCompareAndSwapAsync(
                "k", default, ByteString.From(0x11, 0x80)).ConfigureAwait(false);

            Assert.That(inserted, Is.EqualTo(SharedKeyValueStoreWriteResult.Applied));
            await AssertValueAsync(store, "k", ByteString.From(0x11, 0x80)).ConfigureAwait(false);
            Assert.That(await ReadSnapshotRevisionAsync().ConfigureAwait(false), Is.EqualTo(2UL));
            byte[] insertedSnapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            Assert.That(insertedSnapshot, Is.Not.EqualTo(emptySnapshot));

            SharedKeyValueStoreWriteResult replaced = await store.TryCompareAndSwapAsync(
                "k", ByteString.From(0x11, 0x80), ByteString.From(0x22, 0xff)).ConfigureAwait(false);

            Assert.That(replaced, Is.EqualTo(SharedKeyValueStoreWriteResult.Applied));
            await AssertValueAsync(store, "k", ByteString.From(0x22, 0xff)).ConfigureAwait(false);
            Assert.That(await ReadSnapshotRevisionAsync().ConfigureAwait(false), Is.EqualTo(3UL));
            Assert.That(commitCalls, Is.EqualTo(10));
            byte[] committed = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            Assert.That(committed, Is.Not.EqualTo(insertedSnapshot));
            Assert.That(await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false), Is.EqualTo(identity));
            Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Is.Empty);
            await store.DisposeAsync().ConfigureAwait(false);

            FileSharedKeyValueStore reopened = CreateStore(maximumSnapshotBytes: capacity);
            await AssertValueAsync(reopened, "k", ByteString.From(0x22, 0xff)).ConfigureAwait(false);
            Assert.That(
                await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                Is.EqualTo(committed));
        }

        /// <summary>
        /// A failed condition wins over capacity checking and cannot publish or notify even on a full store.
        /// </summary>
        [TestCase("absent")]
        [TestCase("expects-absence")]
        [TestCase("different-value")]
        public async Task CapacityCasConditionNotMetDoesNotPublishAsync(string condition)
        {
            const int capacity = 261;
            var fullValue = new ByteString(CreateBytes(166));
            bool armed = false;
            int commitCalls = 0;
            FileSharedKeyValueStore store = CreateStore(
                maximumSnapshotBytes: capacity,
                commitPhase: _ =>
                {
                    if (armed)
                    {
                        commitCalls++;
                    }
                });
            await store.SetAsync("k", fullValue).ConfigureAwait(false);
            byte[] identity = await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false);
            byte[] committed = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            Assert.That(committed, Has.Length.EqualTo(capacity));
            IAsyncEnumerator<KeyValueChange> watcher = store.WatchAsync(string.Empty).GetAsyncEnumerator();
            Task<bool> pending = MoveNextPreservingCancellationAsync(watcher);
            try
            {
                armed = true;
                string key = condition == "absent" ? "n" : "k";
                ByteString expected = condition == "expects-absence" ? default : ByteString.From(0x99);
                var oversized = new ByteString(CreateBytes(221));

                SharedKeyValueStoreWriteResult result = await store.TryCompareAndSwapAsync(
                    key, expected, oversized).ConfigureAwait(false);

                Assert.That(result, Is.EqualTo(SharedKeyValueStoreWriteResult.ConditionNotMet));
                Assert.That(commitCalls, Is.Zero);
                Assert.That(await ReadSnapshotRevisionAsync().ConfigureAwait(false), Is.EqualTo(2UL));
                Assert.That(
                    await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false),
                    Is.EqualTo(identity));
                Assert.That(
                    await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                    Is.EqualTo(committed));
                Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Is.Empty);
                await AssertValueAsync(store, "k", fullValue).ConfigureAwait(false);
                await AssertValueAsync(store, "n", default, found: false).ConfigureAwait(false);
                await store.VerifyDurabilityAsync().ConfigureAwait(false);
                await store.DisposeAsync().ConfigureAwait(false);
                Assert.That(await pending.WaitAsync(s_watchdog).ConfigureAwait(false), Is.False);
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
                await FinishWatcherAsync(watcher, pending).ConfigureAwait(false);
            }
            FileSharedKeyValueStore reopened = CreateStore(maximumSnapshotBytes: capacity);
            await AssertValueAsync(reopened, "k", fullValue).ConfigureAwait(false);
            await AssertValueAsync(reopened, "n", default, found: false).ConfigureAwait(false);
            Assert.That(
                await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                Is.EqualTo(committed));
        }

        /// <summary>
        /// Plaintext and envelope overflow reject admission without publication, notification or a faulted owner.
        /// </summary>
        [TestCase(0, 216)]
        [TestCase(46, 262)]
        public async Task CapacityCasRejectsFullStoreWithoutMutationOrNotificationAsync(
            int replacementLength, int candidatePlaintextLength)
        {
            const int capacity = 261;
            var fullValue = new ByteString(CreateBytes(166));
            var replacement = new ByteString(CreateBytes(replacementLength));
            Assert.That(replacement.IsNull, Is.False, "An empty admission must not become an absent-delete no-op.");
            bool armed = false;
            int commitCalls = 0;
            FileSharedKeyValueStore store = CreateStore(
                maximumSnapshotBytes: capacity,
                commitPhase: _ =>
                {
                    if (armed)
                    {
                        commitCalls++;
                    }
                });
            await store.SetAsync("k", fullValue).ConfigureAwait(false);
            byte[] identity = await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false);
            byte[] committed = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            Assert.That(committed, Has.Length.EqualTo(capacity));
            ByteString plaintext = await ReadPlaintextAsync("snapshot.bin").ConfigureAwait(false);
            Assert.That(plaintext, Has.Length.EqualTo(207));
            Assert.That(plaintext.Length + 9 + replacement.Length, Is.EqualTo(candidatePlaintextLength));
            IAsyncEnumerator<KeyValueChange> watcher = store.WatchAsync(string.Empty).GetAsyncEnumerator();
            Task<bool> pending = MoveNextPreservingCancellationAsync(watcher);
            try
            {
                armed = true;

                SharedKeyValueStoreWriteResult result = await store.TryCompareAndSwapAsync(
                    "n", default, replacement).ConfigureAwait(false);

                Assert.That(result, Is.EqualTo(SharedKeyValueStoreWriteResult.CapacityRejected));
                Assert.That(commitCalls, Is.Zero);
                Assert.That(await ReadSnapshotRevisionAsync().ConfigureAwait(false), Is.EqualTo(2UL));
                Assert.That(
                    await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false),
                    Is.EqualTo(identity));
                Assert.That(
                    await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                    Is.EqualTo(committed));
                Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Is.Empty);
                await AssertValueAsync(store, "k", fullValue).ConfigureAwait(false);
                await AssertValueAsync(store, "n", default, found: false).ConfigureAwait(false);
                await store.VerifyDurabilityAsync().ConfigureAwait(false);

                await Assert.ThrowsAsync<IOException>(
                    async () => await store.CompareAndSwapAsync("n", default, replacement).ConfigureAwait(false))
                    .ConfigureAwait(false);

                Assert.That(commitCalls, Is.Zero);
                Assert.That(
                    await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                    Is.EqualTo(committed));
                Assert.That(
                    await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false),
                    Is.EqualTo(identity));
                Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Is.Empty);
                await AssertValueAsync(store, "k", fullValue).ConfigureAwait(false);
                await AssertValueAsync(store, "n", default, found: false).ConfigureAwait(false);
                await store.VerifyDurabilityAsync().ConfigureAwait(false);
                await store.DisposeAsync().ConfigureAwait(false);
                Assert.That(await pending.WaitAsync(s_watchdog).ConfigureAwait(false), Is.False);
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
                await FinishWatcherAsync(watcher, pending).ConfigureAwait(false);
            }
            FileSharedKeyValueStore reopened = CreateStore(maximumSnapshotBytes: capacity);
            await AssertValueAsync(reopened, "k", fullValue).ConfigureAwait(false);
            await AssertValueAsync(reopened, "n", default, found: false).ConfigureAwait(false);
            Assert.That(
                await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                Is.EqualTo(committed));
        }

        /// <summary>
        /// Rejected admissions leave shrink and deletion available so existing results can release capacity.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(46, true)]
        public async Task CapacityCasFullStoreCanShrinkDeleteAndReadmitAsync(
            int admissionLength, bool useCapacityCasDelete)
        {
            const int capacity = 261;
            var fullValue = new ByteString(CreateBytes(166));
            var admission = new ByteString(CreateBytes(admissionLength));
            int shrunkLength = admissionLength == 0 ? 157 : 111;
            var shrunkValue = new ByteString(CreateBytes(shrunkLength));
            byte[] readmittedBytes = CreateBytes(shrunkLength);
            readmittedBytes[0] = 0xc3;
            var readmittedValue = new ByteString(readmittedBytes);
            bool armed = false;
            int commitCalls = 0;
            FileSharedKeyValueStore store = CreateStore(
                maximumSnapshotBytes: capacity,
                commitPhase: _ =>
                {
                    if (armed)
                    {
                        commitCalls++;
                    }
                });
            await store.SetAsync("k", fullValue).ConfigureAwait(false);
            byte[] identity = await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false);
            byte[] fullSnapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            Assert.That(fullSnapshot, Has.Length.EqualTo(capacity));
            Assert.That(await ReadPlaintextAsync("snapshot.bin").ConfigureAwait(false), Has.Length.EqualTo(207));
            IAsyncEnumerator<KeyValueChange> watcher = store.WatchAsync(string.Empty).GetAsyncEnumerator();
            Task<bool> pending = MoveNextPreservingCancellationAsync(watcher);
            try
            {
                armed = true;
                Assert.That(
                    await store.TryCompareAndSwapAsync("n", default, admission).ConfigureAwait(false),
                    Is.EqualTo(SharedKeyValueStoreWriteResult.CapacityRejected));
                Assert.That(commitCalls, Is.Zero);
                Assert.That(
                    await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                    Is.EqualTo(fullSnapshot));
                await AssertValueAsync(store, "k", fullValue).ConfigureAwait(false);
                await AssertValueAsync(store, "n", default, found: false).ConfigureAwait(false);

                Assert.That(
                    await store.TryCompareAndSwapAsync("k", fullValue, shrunkValue).ConfigureAwait(false),
                    Is.EqualTo(SharedKeyValueStoreWriteResult.Applied));
                await AssertChangeAsync(watcher, pending, KeyValueChangeKind.Set, "k", shrunkValue)
                    .ConfigureAwait(false);
                await AssertValueAsync(store, "k", shrunkValue).ConfigureAwait(false);
                Assert.That(await ReadSnapshotRevisionAsync().ConfigureAwait(false), Is.EqualTo(3UL));
                Assert.That(
                    await ReadPlaintextAsync("snapshot.bin").ConfigureAwait(false),
                    Has.Length.EqualTo(admissionLength == 0 ? 198 : 152));
                Assert.That(
                    await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                    Has.Length.EqualTo(admissionLength == 0 ? 261 : 213));

                pending = MoveNextPreservingCancellationAsync(watcher);
                Assert.That(
                    await store.TryCompareAndSwapAsync("n", default, admission).ConfigureAwait(false),
                    Is.EqualTo(SharedKeyValueStoreWriteResult.Applied));
                await AssertChangeAsync(watcher, pending, KeyValueChangeKind.Set, "n", admission)
                    .ConfigureAwait(false);
                await AssertValueAsync(store, "n", admission).ConfigureAwait(false);
                Assert.That(await ReadSnapshotRevisionAsync().ConfigureAwait(false), Is.EqualTo(4UL));
                Assert.That(await ReadPlaintextAsync("snapshot.bin").ConfigureAwait(false), Has.Length.EqualTo(207));
                byte[] fullAgain = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
                Assert.That(fullAgain, Has.Length.EqualTo(capacity));

                pending = MoveNextPreservingCancellationAsync(watcher);
                Assert.That(
                    await store.TryCompareAndSwapAsync("p", default, readmittedValue).ConfigureAwait(false),
                    Is.EqualTo(SharedKeyValueStoreWriteResult.CapacityRejected));
                Assert.That(commitCalls, Is.EqualTo(10));
                Assert.That(
                    await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                    Is.EqualTo(fullAgain));
                await AssertValueAsync(store, "k", shrunkValue).ConfigureAwait(false);
                await AssertValueAsync(store, "n", admission).ConfigureAwait(false);
                await AssertValueAsync(store, "p", default, found: false).ConfigureAwait(false);
                await store.VerifyDurabilityAsync().ConfigureAwait(false);

                if (useCapacityCasDelete)
                {
                    Assert.That(
                        await store.TryCompareAndSwapAsync("k", shrunkValue, default).ConfigureAwait(false),
                        Is.EqualTo(SharedKeyValueStoreWriteResult.Applied));
                }
                else
                {
                    Assert.That(await store.DeleteAsync("k").ConfigureAwait(false), Is.True);
                }
                await AssertChangeAsync(watcher, pending, KeyValueChangeKind.Delete, "k", default)
                    .ConfigureAwait(false);
                await AssertValueAsync(store, "k", default, found: false).ConfigureAwait(false);
                await AssertValueAsync(store, "n", admission).ConfigureAwait(false);
                Assert.That(await ReadSnapshotRevisionAsync().ConfigureAwait(false), Is.EqualTo(5UL));
                Assert.That(
                    await ReadPlaintextAsync("snapshot.bin").ConfigureAwait(false),
                    Has.Length.EqualTo(admissionLength == 0 ? 41 : 87));
                Assert.That(
                    await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                    Has.Length.EqualTo(admissionLength == 0 ? 101 : 149));

                pending = MoveNextPreservingCancellationAsync(watcher);
                Assert.That(
                    await store.TryCompareAndSwapAsync("p", default, readmittedValue).ConfigureAwait(false),
                    Is.EqualTo(SharedKeyValueStoreWriteResult.Applied));
                await AssertChangeAsync(watcher, pending, KeyValueChangeKind.Set, "p", readmittedValue)
                    .ConfigureAwait(false);
                await AssertValueAsync(store, "p", readmittedValue).ConfigureAwait(false);
                Assert.That(await ReadSnapshotRevisionAsync().ConfigureAwait(false), Is.EqualTo(6UL));
                Assert.That(await ReadPlaintextAsync("snapshot.bin").ConfigureAwait(false), Has.Length.EqualTo(207));
                Assert.That(
                    await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                    Has.Length.EqualTo(capacity));
                Assert.That(
                    await ReadAllBytesAsync(StorePath("identity.bin")).ConfigureAwait(false),
                    Is.EqualTo(identity));
                Assert.That(commitCalls, Is.EqualTo(20));
                Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Is.Empty);
                await store.VerifyDurabilityAsync().ConfigureAwait(false);

                pending = MoveNextPreservingCancellationAsync(watcher);
                await store.DisposeAsync().ConfigureAwait(false);
                Assert.That(await pending.WaitAsync(s_watchdog).ConfigureAwait(false), Is.False);
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
                await FinishWatcherAsync(watcher, pending).ConfigureAwait(false);
            }
            FileSharedKeyValueStore reopened = CreateStore(maximumSnapshotBytes: capacity);
            await AssertValueAsync(reopened, "k", default, found: false).ConfigureAwait(false);
            await AssertValueAsync(reopened, "n", admission).ConfigureAwait(false);
            await AssertValueAsync(reopened, "p", readmittedValue).ConfigureAwait(false);
            Assert.That(await ReadSnapshotRevisionAsync().ConfigureAwait(false), Is.EqualTo(6UL));
        }

        /// <summary>
        /// Publication failures remain exceptional and cannot be mislabeled as a known unchanged capacity rejection.
        /// </summary>
        [TestCase((int)FileStoreCommitPhase.BeforePublish)]
        [TestCase((int)FileStoreCommitPhase.AfterPublish)]
        public async Task CapacityCasPublicationFailureRemainsExceptionalAsync(int phaseNumber)
        {
            const int capacity = 261;
            var faultPhase = (FileStoreCommitPhase)phaseNumber;
            var originalValue = new ByteString(CreateBytes(166));
            byte[] replacementBytes = CreateBytes(166);
            replacementBytes[0] = 0x80;
            var replacement = new ByteString(replacementBytes);
            var injected = new IOException("Injected capacity-CAS publication boundary: " + faultPhase);
            bool armed = false;
            FileSharedKeyValueStore store = CreateStore(
                maximumSnapshotBytes: capacity,
                commitPhase: phase =>
                {
                    if (armed && phase == faultPhase)
                    {
                        throw injected;
                    }
                });
            await store.SetAsync("k", originalValue).ConfigureAwait(false);
            byte[] oldSnapshot = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            Assert.That(oldSnapshot, Has.Length.EqualTo(capacity));
            IAsyncEnumerator<KeyValueChange> watcher = store.WatchAsync(string.Empty).GetAsyncEnumerator();
            Task<bool> pending = MoveNextPreservingCancellationAsync(watcher);
            try
            {
                armed = true;
                IOException? error = await Assert.ThrowsAsync<IOException>(
                    async () => await store.TryCompareAndSwapAsync("k", originalValue, replacement)
                        .ConfigureAwait(false)).ConfigureAwait(false);

                Assert.That(error, Is.SameAs(injected));
                await AssertFaultedAsync(store).ConfigureAwait(false);
                await Assert.ThrowsAsync<IOException>(
                    async () => await store.TryCompareAndSwapAsync("n", default, ByteString.Empty)
                        .ConfigureAwait(false)).ConfigureAwait(false);
                await store.DisposeAsync().ConfigureAwait(false);
                Assert.That(await pending.WaitAsync(s_watchdog).ConfigureAwait(false), Is.False);
            }
            finally
            {
                await store.DisposeAsync().ConfigureAwait(false);
                await FinishWatcherAsync(watcher, pending).ConfigureAwait(false);
            }
            bool published = faultPhase == FileStoreCommitPhase.AfterPublish;
            byte[] committed = await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false);
            Assert.That(committed.SequenceEqual(oldSnapshot), Is.EqualTo(!published));
            Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Has.Length.EqualTo(published ? 0 : 1));
            FileSharedKeyValueStore reopened = CreateStore(maximumSnapshotBytes: capacity);
            await AssertValueAsync(reopened, "k", published ? replacement : originalValue).ConfigureAwait(false);
            Assert.That(await ReadSnapshotRevisionAsync().ConfigureAwait(false), Is.EqualTo(published ? 3UL : 2UL));
            Assert.That(
                await ReadAllBytesAsync(StorePath("snapshot.bin")).ConfigureAwait(false),
                Is.EqualTo(committed));
            Assert.That(Directory.GetFiles(m_storeDirectory, "stage-*"), Is.Empty);
        }

        private static async Task<byte[]> ReadAllBytesAsync(string path)
        {
#if NET
            return await File.ReadAllBytesAsync(path).ConfigureAwait(false);
#else
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            using var content = new MemoryStream();
            await stream.CopyToAsync(content).ConfigureAwait(false);
            return content.ToArray();
#endif
        }

        private static async Task WriteAllBytesAsync(string path, byte[] content)
        {
#if NET
            await File.WriteAllBytesAsync(path, content).ConfigureAwait(false);
#else
            using var stream = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
            await stream.WriteAsync(content, 0, content.Length).ConfigureAwait(false);
#endif
        }

        private async Task<ulong> ReadSnapshotRevisionAsync()
        {
            ByteString plaintext = await ReadPlaintextAsync("snapshot.bin").ConfigureAwait(false);
            using var stream = new MemoryStream(plaintext.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);
            stream.Position = 20;
            return reader.ReadUInt64();
        }

        private FileSharedKeyValueStore CreateStore(
            int maximumSnapshotBytes = kMaximumSnapshotBytes,
            Action<FileStoreCommitPhase>? commitPhase = null,
            string? directory = null,
            string logicalStoreId = kLogicalStoreId,
            IRecordProtector? protector = null,
            Action? stageFileCreated = null)
        {
            var store = new FileSharedKeyValueStore(
                directory ?? m_storeDirectory, logicalStoreId, protector ?? m_protector,
                maximumSnapshotBytes, commitPhase, stageFileCreated);
            m_stores.Add(store);
            return store;
        }

        private static async Task AssertValueAsync(
            ISharedKeyValueStore store, string key, ByteString expected, bool found = true)
        {
            (bool actualFound, ByteString actual) = await store.TryGetAsync(key).ConfigureAwait(false);
            Assert.That(actualFound, Is.EqualTo(found), key);
            Assert.That(actual.IsNull, Is.EqualTo(expected.IsNull), key);
            Assert.That(actual.ToArray(), Is.EqualTo(expected.ToArray()), key);
        }

        private static async Task<KeyValuePair<string, ByteString>[]> ScanAsync(
            FileSharedKeyValueStore store, string? prefix = null, CancellationToken cancellationToken = default)
        {
            var entries = new List<KeyValuePair<string, ByteString>>();
            await foreach (KeyValuePair<string, ByteString> entry in
                store.ScanAsync(prefix!, cancellationToken).ConfigureAwait(false))
            {
                entries.Add(entry);
            }
            return [.. entries];
        }

        private static void MutateFirstByte(ByteString value, byte replacement)
        {
            Assert.That(MemoryMarshal.TryGetArray(value.Memory, out ArraySegment<byte> segment), Is.True);
            segment.Array![segment.Offset] = replacement;
        }

        private static byte[] CreateBytes(int length)
        {
            byte[] bytes = new byte[length];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = (byte)index;
            }
            return bytes;
        }

        private static AesCbcHmacRecordProtector CreateProtector()
        {
            byte[] masterKey = new byte[32];
            using var random = RandomNumberGenerator.Create();
            random.GetBytes(masterKey);
            try
            {
                return new AesCbcHmacRecordProtector(masterKey);
            }
            finally
            {
                CryptoUtils.ZeroMemory(masterKey);
            }
        }

        private async Task<string> CreateStageAsync(ByteString content, bool upperCaseGuid = false)
        {
            string id = Guid.NewGuid().ToString("N");
            string path = StorePath("stage-" + (upperCaseGuid ? id.ToUpperInvariant() : id) + ".bin");
            await WriteAllBytesAsync(path, content.ToArray()).ConfigureAwait(false);
            return path;
        }

        private async Task<ByteString> ReadPlaintextAsync(string fileName)
        {
            string context = fileName == "identity.bin"
                ? "FileSharedKeyValueStore.Identity.1" : "FileSharedKeyValueStore.Snapshot.1";
            Assert.That(
                m_protector.TryUnprotect(
                    RecordProtectionContext.Create(context, kLogicalStoreId),
                    new ByteString(await ReadAllBytesAsync(StorePath(fileName)).ConfigureAwait(false)),
                    out ByteString plaintext),
                Is.True);
            return plaintext;
        }

        private async Task WriteAuthenticatedFileAsync(
            string fileName, ByteString plaintext, string? context = null)
        {
            context ??= fileName == "identity.bin"
                ? "FileSharedKeyValueStore.Identity.1" : "FileSharedKeyValueStore.Snapshot.1";
            ByteString record = m_protector.Protect(
                RecordProtectionContext.Create(context, kLogicalStoreId), plaintext);
            await WriteAllBytesAsync(StorePath(fileName), record.ToArray()).ConfigureAwait(false);
        }

        private static ByteString CreateMalformedSnapshot(string defect, Guid epoch)
        {
            if (defect == "short-header")
            {
                return new ByteString(new byte[31]);
            }
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(defect == "schema" ? 2 : 1);
            byte[] epochBytes = (defect == "epoch" ? Guid.Empty : epoch).ToByteArray();
            if (defect == "foreign-epoch")
            {
                epochBytes[0] ^= 0x80;
            }
            writer.Write(epochBytes);
            writer.Write(defect == "revision" ? 0UL : 1UL);
            writer.Write(defect switch
            {
                "negative-count" => -1,
                "excessive-count" => int.MaxValue,
                "duplicate" => 2,
                "schema" or "epoch" or "foreign-epoch" or "revision" or "trailing" => 0,
                _ => 1
            });
            switch (defect)
            {
                case "duplicate":
                    for (int index = 0; index < 2; index++)
                    {
                        writer.Write(1);
                        writer.Write((byte)'k');
                        writer.Write(1);
                        writer.Write((byte)(index + 1));
                    }
                    break;
                case "trailing":
                    writer.Write((byte)0x7f);
                    break;
                case "negative-key":
                    writer.Write(-1);
                    writer.Write(0);
                    break;
                case "key-overrun":
                    writer.Write(5);
                    writer.Write(0);
                    break;
                case "invalid-utf8":
                    writer.Write(1);
                    writer.Write((byte)0xff);
                    writer.Write(0);
                    break;
                case "negative-value":
                    writer.Write(1);
                    writer.Write((byte)'k');
                    writer.Write(-2);
                    break;
                case "value-overrun":
                    writer.Write(1);
                    writer.Write((byte)'k');
                    writer.Write(2);
                    writer.Write((byte)0x11);
                    break;
                case "missing-value-header":
                    writer.Write(4);
                    writer.Write(Encoding.UTF8.GetBytes("key!"));
                    break;
            }
            writer.Flush();
            return new ByteString(stream.ToArray());
        }

        private async Task AssertRecoveryRejectedAsync(FileSharedKeyValueStore store, bool truncated = false)
        {
            if (truncated)
            {
                await Assert.ThrowsAsync<EndOfStreamException>(
                    async () => await store.VerifyDurabilityAsync().ConfigureAwait(false)).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(
                    async () => await store.VerifyDurabilityAsync().ConfigureAwait(false)).ConfigureAwait(false);
            }
            await AssertFaultedAsync(store).ConfigureAwait(false);
            using var releasedOwner = new FileStream(
                StorePath("owner.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.That(releasedOwner.Length, Is.Zero);
        }

        private static async Task AssertFaultedAsync(FileSharedKeyValueStore store)
        {
            foreach (string operation in s_operations)
            {
                await Assert.ThrowsAsync<IOException>(
                    async () => await ExerciseOperationAsync(store, operation).WaitAsync(s_watchdog)
                        .ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
        }

        private static async Task AssertChangeAsync(
            IAsyncEnumerator<KeyValueChange> watcher, Task<bool> pending,
            KeyValueChangeKind kind, string key, ByteString value)
        {
            Assert.That(await pending.WaitAsync(s_watchdog).ConfigureAwait(false), Is.True);
            Assert.That(watcher.Current.Kind, Is.EqualTo(kind));
            Assert.That(watcher.Current.Key, Is.EqualTo(key));
            Assert.That(watcher.Current.Value.IsNull, Is.EqualTo(value.IsNull));
            Assert.That(watcher.Current.Value.ToArray(), Is.EqualTo(value.ToArray()));
        }

        private static async Task FinishWatcherAsync(IAsyncEnumerator<KeyValueChange> watcher, Task<bool> pending)
        {
            try
            {
                // The test has asserted the result; observe it again only to finish cleanup before disposal.
                await pending.WaitAsync(s_watchdog).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected for tests which explicitly cancelled their watch.
            }
            catch (IOException)
            {
                // Expected for the asserted bounded-channel overflow.
            }
            await watcher.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task<bool> MoveNextPreservingCancellationAsync(IAsyncEnumerator<KeyValueChange> watcher)
        {
            // Await the ValueTask directly: legacy AsTask conversion can replace the cancellation token.
            return await watcher.MoveNextAsync().ConfigureAwait(false);
        }

        private static async Task ExerciseOperationAsync(
            FileSharedKeyValueStore store, string operation,
            string? key = "key", CancellationToken cancellationToken = default)
        {
            switch (operation)
            {
                case "get":
                    await store.TryGetAsync(key!, cancellationToken).ConfigureAwait(false);
                    break;
                case "set":
                    await store.SetAsync(key!, ByteString.From(0x99), cancellationToken).ConfigureAwait(false);
                    break;
                case "cas":
                    await store.CompareAndSwapAsync(key!, default, ByteString.From(0x99), cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "delete":
                    await store.DeleteAsync(key!, cancellationToken).ConfigureAwait(false);
                    break;
                case "scan":
                    await ScanAsync(store, key, cancellationToken).ConfigureAwait(false);
                    break;
                case "watch":
                    IAsyncEnumerator<KeyValueChange> watcher =
                        store.WatchAsync(key!, cancellationToken).GetAsyncEnumerator(cancellationToken);
                    try
                    {
                        // Only the store, not this deadlock watchdog, may satisfy the cancellation assertion.
                        await MoveNextPreservingCancellationAsync(watcher).WaitAsync(s_watchdog, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        await watcher.DisposeAsync().ConfigureAwait(false);
                    }
                    break;
                case "ready":
                    await store.VerifyDurabilityAsync(cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        private string StorePath(string fileName)
        {
            return Path.Combine(m_storeDirectory, fileName);
        }

        private string m_testDirectory = null!;
        private string m_storeDirectory = null!;
        private AesCbcHmacRecordProtector m_protector = null!;
        private readonly List<FileSharedKeyValueStore> m_stores = [];
        private static readonly string[] s_committedFileNames = ["owner.lock", "identity.bin", "snapshot.bin"];
        private static readonly string[] s_operations = ["get", "set", "cas", "delete", "scan", "watch", "ready"];
        private static readonly TimeSpan s_watchdog = TimeSpan.FromSeconds(15);
        private const string kLogicalStoreId = "tests/rse04/local-reference";
        private const int kMaximumSnapshotBytes = 4096;
    }
}
