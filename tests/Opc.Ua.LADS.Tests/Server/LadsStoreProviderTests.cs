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
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.LADS.Server;
using Opc.Ua.Redundancy;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests provider snapshot contracts without hosting a device or recovering physical execution.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [Parallelizable(ParallelScope.All)]
    public sealed class LadsStoreProviderTests
    {
        [Test]
        public async Task RoundTripPreservesEverySnapshotFieldAsync([Values] bool fileSystem)
        {
            using var scope = new StoreScope(fileSystem);
            LadsStoredDeviceData expected = CreateSnapshot();

            await scope.Store.SaveAsync(kIdentity, expected).ConfigureAwait(false);
            LadsStoredDeviceData? actual = await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false);

            AssertSnapshot(actual, expected);
        }

        [Test]
        public async Task SaveAndLoadDoNotShareMutableSnapshotStateAsync([Values] bool fileSystem)
        {
            using var scope = new StoreScope(fileSystem);
            LadsStoredDeviceData input = CreateSnapshot();
            await scope.Store.SaveAsync(kIdentity, input).ConfigureAwait(false);
            MutateSnapshot(input);

            LadsStoredDeviceData? first = await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false);
            AssertSnapshot(first, CreateSnapshot());
            MutateSnapshot(first!);
            LadsStoredDeviceData? second = await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false);

            Assert.That(second, Is.Not.SameAs(first));
            AssertSnapshot(second, CreateSnapshot());
        }

        [Test]
        public async Task RepeatedSaveReplacesAndDeleteIsIdempotentAsync([Values] bool fileSystem)
        {
            using var scope = new StoreScope(fileSystem);
            Assert.That(await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false), Is.Null);
            await scope.Store.DeleteAsync(kIdentity).ConfigureAwait(false);
            await scope.Store.SaveAsync(kIdentity, CreateSnapshot()).ConfigureAwait(false);
            LadsStoredDeviceData replacement = CreateSnapshot();
            replacement.OperationCycles = 17;
            replacement.FunctionalUnits[0].Results = [];
            await scope.Store.SaveAsync(kIdentity, replacement).ConfigureAwait(false);

            AssertSnapshot(await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false), replacement);
            await scope.Store.DeleteAsync(kIdentity).ConfigureAwait(false);
            await scope.Store.DeleteAsync(kIdentity).ConfigureAwait(false);
            Assert.That(await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false), Is.Null);
            await scope.Store.SaveAsync(kIdentity, CreateSnapshot()).ConfigureAwait(false);
            AssertSnapshot(await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false), CreateSnapshot());
        }

        /// <summary>
        /// Verifies the authority-backed adapter preserves complete snapshots and isolates equal keys by group.
        /// </summary>
        [Test]
        public async Task LeasePublicationStorePreservesSnapshotAndGroupIsolationAsync()
        {
            var authority = new InMemoryLeasePublicationAuthority();
            LeasePublicationToken firstToken = await AcquireAsync(authority, "lads-a", "owner-a").ConfigureAwait(false);
            LeasePublicationToken secondToken = await AcquireAsync(authority, "lads-b", "owner-b").ConfigureAwait(false);
            var firstStore = new LeasePublicationLadsStore(authority, firstToken);
            var secondStore = new LeasePublicationLadsStore(authority, secondToken);
            LadsStoredDeviceData expected = CreateSnapshot();

            Assert.That(await secondStore.LoadAsync(kIdentity).ConfigureAwait(false), Is.Null);
            await firstStore.SaveAsync(kIdentity, expected).ConfigureAwait(false);

            AssertSnapshot(await firstStore.LoadAsync(kIdentity).ConfigureAwait(false), expected);
            Assert.That(await secondStore.LoadAsync(kIdentity).ConfigureAwait(false), Is.Null);
            LadsStoredDeviceData invalid = CreateSnapshot();
            invalid.Identity = "urn:device:mismatched";
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await firstStore.SaveAsync(kIdentity, invalid).ConfigureAwait(false)).ConfigureAwait(false);
            AssertSnapshot(await firstStore.LoadAsync(kIdentity).ConfigureAwait(false), expected);
            await firstStore.DeleteAsync(kIdentity).ConfigureAwait(false);
            Assert.That(await firstStore.LoadAsync(kIdentity).ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// Verifies the adapter rejects expired save and delete operations without changing the prior snapshot.
        /// </summary>
        [Test]
        public async Task LeasePublicationStoreRejectsExpiredSaveAndDeleteAsync()
        {
            var time = new ManualTimeProvider();
            var authority = new InMemoryLeasePublicationAuthority(time);
            LeasePublicationToken token = await AcquireAsync(authority, "lads", "owner").ConfigureAwait(false);
            var store = new LeasePublicationLadsStore(authority, token);
            LadsStoredDeviceData previous = CreateSnapshot();
            await store.SaveAsync(kIdentity, previous).ConfigureAwait(false);
            LadsStoredDeviceData replacement = CreateSnapshot();
            replacement.OperationCycles = 17;
            time.Advance(TimeSpan.FromSeconds(30));

            InvalidOperationException saveFailure =
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await store.SaveAsync(kIdentity, replacement).ConfigureAwait(false)).ConfigureAwait(false) ??
                throw new AssertionException("An expired lease save was not rejected.");
            InvalidOperationException deleteFailure =
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await store.DeleteAsync(kIdentity).ConfigureAwait(false)).ConfigureAwait(false) ??
                throw new AssertionException("An expired lease delete was not rejected.");

            Assert.That(saveFailure.Message, Does.Contain(nameof(LeasePublicationStatus.LeaseExpired)));
            Assert.That(deleteFailure.Message, Does.Contain(nameof(LeasePublicationStatus.LeaseExpired)));
            AssertSnapshot(await store.LoadAsync(kIdentity).ConfigureAwait(false), previous);
        }

        [Test]
        public async Task DifferentDeviceIdentitiesRemainIndependentAsync([Values] bool fileSystem)
        {
            using var scope = new StoreScope(fileSystem);
            const string otherIdentity = "urn:device:other";
            LadsStoredDeviceData other = CreateSnapshot();
            other.Identity = otherIdentity;
            other.OperationCycles = 29;
            await scope.Store.SaveAsync(kIdentity, CreateSnapshot()).ConfigureAwait(false);
            await scope.Store.SaveAsync(otherIdentity, other).ConfigureAwait(false);
            await scope.Store.DeleteAsync(kIdentity).ConfigureAwait(false);

            Assert.That(await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false), Is.Null);
            AssertSnapshot(await scope.Store.LoadAsync(otherIdentity).ConfigureAwait(false), other);
        }

        [Test]
        public async Task NullAndEmptyCollectionsAndByteStringsRemainDistinctAsync([Values] bool fileSystem)
        {
            using var scope = new StoreScope(fileSystem);
            var data = new LadsStoredDeviceData
            {
                Identity = kIdentity,
                MaintenanceTasks = [],
                FunctionalUnits =
                [
                    new LadsStoredFunctionalUnitData
                    {
                        Name = "Null",
                        Templates = [new LadsStoredTemplateData { Id = "NullContent" }]
                    },
                    new LadsStoredFunctionalUnitData
                    {
                        Name = "Empty",
                        Templates = [new LadsStoredTemplateData { Id = "EmptyContent", Content = ByteString.Empty }],
                        Results = [],
                        RemovedTemplateIds = []
                    }
                ]
            };
            await scope.Store.SaveAsync(kIdentity, data).ConfigureAwait(false);
            LadsStoredDeviceData? actual = await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false);

            Assert.That(actual, Is.Not.Null);
            Assert.That(actual!.MaintenanceTasks.IsNull, Is.False);
            Assert.That(actual.MaintenanceTasks.Count, Is.Zero);
            Assert.That(actual.FunctionalUnits[0].Results.IsNull, Is.True);
            Assert.That(actual.FunctionalUnits[0].RemovedTemplateIds.IsNull, Is.True);
            Assert.That(actual.FunctionalUnits[0].Templates[0].Content.IsNull, Is.True);
            Assert.That(actual.FunctionalUnits[1].Results.IsNull, Is.False);
            Assert.That(actual.FunctionalUnits[1].Results.Count, Is.Zero);
            Assert.That(actual.FunctionalUnits[1].RemovedTemplateIds.IsNull, Is.False);
            Assert.That(actual.FunctionalUnits[1].Templates[0].Content.IsNull, Is.False);
            Assert.That(actual.FunctionalUnits[1].Templates[0].Content.Length, Is.Zero);
        }

        [Test]
        public async Task InvalidSnapshotIsRejectedWithoutReplacingPriorDataAsync(
            [Values] bool fileSystem,
            [Values("Schema", "Identity", "Unit", "Task", "Template", "Result", "File", "Tombstone",
                "DeletedTemplate", "Outcome", "MaintenanceState", "MaintenanceResult",
                "PauseExceedsRuntime", "StoppedBeforeStarted")] string invalidKind)
        {
            using var scope = new StoreScope(fileSystem);
            await scope.Store.SaveAsync(kIdentity, CreateSnapshot()).ConfigureAwait(false);
            LadsStoredDeviceData invalid = CreateSnapshot();
            MakeInvalid(invalid, invalidKind);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await scope.Store.SaveAsync(kIdentity, invalid).ConfigureAwait(false)).ConfigureAwait(false);

            AssertSnapshot(await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false), CreateSnapshot());
        }

        [Test]
        public async Task InvalidNumericDurationIsRejectedWithoutReplacingPriorDataAsync(
            [Values] bool fileSystem,
            [Values("Operation", "PowerOn", "Runtime", "Pause")] string field,
            [Values(-1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e15)] double value)
        {
            using var scope = new StoreScope(fileSystem);
            await scope.Store.SaveAsync(kIdentity, CreateSnapshot()).ConfigureAwait(false);
            LadsStoredDeviceData invalid = CreateSnapshot();
            switch (field)
            {
                case "Operation":
                    invalid.OperationDurationMilliseconds = value;
                    break;
                case "PowerOn":
                    invalid.PowerOnDurationMilliseconds = value;
                    break;
                case "Runtime":
                    invalid.FunctionalUnits[0].Results[0].TotalRuntimeMilliseconds = value;
                    break;
                case "Pause":
                    invalid.FunctionalUnits[0].Results[0].TotalPauseTimeMilliseconds = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(field));
            }

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await scope.Store.SaveAsync(kIdentity, invalid).ConfigureAwait(false)).ConfigureAwait(false);
            AssertSnapshot(await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false), CreateSnapshot());
        }

        [Test]
        public async Task ValidTimingBoundariesRoundTripAsync(
            [Values] bool fileSystem,
            [Values("Zero", "Maximum", "AllPaused")] string boundary)
        {
            using var scope = new StoreScope(fileSystem);
            LadsStoredDeviceData expected = CreateSnapshot();
            LadsStoredResultData result = expected.FunctionalUnits[0].Results[0];
            switch (boundary)
            {
                case "Zero":
                    expected.OperationDurationMilliseconds = 0;
                    expected.PowerOnDurationMilliseconds = 0;
                    result.TotalRuntimeMilliseconds = 0;
                    result.TotalPauseTimeMilliseconds = 0;
                    result.Stopped = result.Started;
                    break;
                case "Maximum":
                    expected.OperationDurationMilliseconds = TimeSpan.MaxValue.TotalMilliseconds;
                    expected.PowerOnDurationMilliseconds = TimeSpan.MaxValue.TotalMilliseconds;
                    result.TotalRuntimeMilliseconds = TimeSpan.MaxValue.TotalMilliseconds;
                    break;
                case "AllPaused":
                    result.TotalPauseTimeMilliseconds = result.TotalRuntimeMilliseconds;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(boundary));
            }

            await scope.Store.SaveAsync(kIdentity, expected).ConfigureAwait(false);

            AssertSnapshot(await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false), expected);
        }

        [Test]
        public async Task InvalidKeysAndNullSnapshotAreRejectedAsync([Values] bool fileSystem)
        {
            using var scope = new StoreScope(fileSystem);
            foreach (string key in new[] { string.Empty, " ", "\r\n" })
            {
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await scope.Store.LoadAsync(key).ConfigureAwait(false)).ConfigureAwait(false);
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await scope.Store.SaveAsync(key, CreateSnapshot()).ConfigureAwait(false)).ConfigureAwait(false);
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await scope.Store.DeleteAsync(key).ConfigureAwait(false)).ConfigureAwait(false);
            }
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await scope.Store.SaveAsync(kIdentity, null!).ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public async Task CancelledOperationsLeavePriorDataUnchangedAsync([Values] bool fileSystem)
        {
            using var scope = new StoreScope(fileSystem);
            await scope.Store.SaveAsync(kIdentity, CreateSnapshot()).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await scope.Store.LoadAsync(kIdentity, cancellation.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await scope.Store.SaveAsync(kIdentity, CreateSnapshot(), cancellation.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await scope.Store.DeleteAsync(kIdentity, cancellation.Token).ConfigureAwait(false))
                .ConfigureAwait(false);

            AssertSnapshot(await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false), CreateSnapshot());
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void NonPositiveSizeLimitsAreRejected(int maxBytes)
        {
            using var fileSystem = new VirtualFileSystem();
            Assert.That(() => new InMemoryLadsStore(maxBytes), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new FileSystemLadsStore(fileSystem, kVirtualDirectory, maxBytes),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task SnapshotAtExactByteLimitSucceedsAndOneByteLessRejectsAsync([Values] bool fileSystem)
        {
            LadsStoredDeviceData data = CreateSnapshot();
            using var sizing = new StoreScope(fileSystem: true);
            await sizing.Store.SaveAsync(kIdentity, data).ConfigureAwait(false);
            int length = sizing.FileSystem.Get(sizing.SnapshotPath).Length;
            using var exact = new StoreScope(fileSystem, length);
            await exact.Store.SaveAsync(kIdentity, data).ConfigureAwait(false);
            AssertSnapshot(await exact.Store.LoadAsync(kIdentity).ConfigureAwait(false), data);

            using var smaller = new StoreScope(fileSystem, length - 1);
            var previous = new LadsStoredDeviceData { Identity = kIdentity, OperationCycles = 3 };
            await smaller.Store.SaveAsync(kIdentity, previous).ConfigureAwait(false);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await smaller.Store.SaveAsync(kIdentity, data).ConfigureAwait(false)).ConfigureAwait(false);
            AssertSnapshot(await smaller.Store.LoadAsync(kIdentity).ConfigureAwait(false), previous);
            if (fileSystem)
            {
                Assert.That(smaller.FileSystem.Files.Count(), Is.EqualTo(1));
            }
        }

        [Test]
        public async Task OversizedFileReadRejectsWithoutModifyingPersistedBytesAsync()
        {
            using var scope = new StoreScope(fileSystem: true);
            await scope.Store.SaveAsync(kIdentity, CreateSnapshot()).ConfigureAwait(false);
            string path = scope.SnapshotPath;
            byte[] bytes = scope.FileSystem.Get(path);
            using var limited = new FileSystemLadsStore(scope.FileSystem, kVirtualDirectory, bytes.Length - 1);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await limited.LoadAsync(kIdentity).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(scope.FileSystem.Get(path), Is.EqualTo(bytes));
            AssertSnapshot(await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false), CreateSnapshot());
        }

        [TestCase("Malformed")]
        [TestCase("Truncated")]
        [TestCase("Schema")]
        [TestCase("Identity")]
        [TestCase("Unknown")]
        [TestCase("Negative")]
        [TestCase("ExcessiveDuration")]
        [TestCase("PauseExceedsRuntime")]
        [TestCase("StoppedBeforeStarted")]
        [TestCase("Duplicate")]
        [TestCase("Dtd")]
        public async Task InvalidPersistedXmlPropagatesWithoutFallbackOrFileChangesAsync(string invalidKind)
        {
            using var scope = new StoreScope(fileSystem: true);
            await scope.Store.SaveAsync(kIdentity, CreateSnapshot()).ConfigureAwait(false);
            string path = scope.SnapshotPath;
            byte[] valid = scope.FileSystem.Get(path);
            byte[] invalid = InvalidXml(valid, invalidKind);
            scope.FileSystem.Add(path, invalid);

            Exception? failure = await Assert.CatchAsync(async () =>
                await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure, Is.InstanceOf<XmlException>().Or.InstanceOf<InvalidDataException>());
            Assert.That(scope.FileSystem.Get(path), Is.EqualTo(invalid));
            scope.FileSystem.Add(path, valid);
            AssertSnapshot(await scope.Store.LoadAsync(kIdentity).ConfigureAwait(false), CreateSnapshot());
        }

        [Test]
        public async Task PersistedFileContainsAllFileBytesAndUsesOnlyIdentityHashAsync()
        {
            string directory = Path.Combine(Path.GetTempPath(), "LadsStoreProviders-" + Guid.NewGuid().ToString("N"));
            const string identity = @"..\..\device/../../outside?name=#é";
            LadsStoredDeviceData expected = CreateSnapshot();
            expected.Identity = identity;
            try
            {
                using (var store = new FileSystemLadsStore(LocalFileSystem.Instance, directory))
                {
                    await store.SaveAsync(identity, expected).ConfigureAwait(false);
                }
                string[] paths = Directory.GetFiles(directory);
                Assert.That(paths, Has.Length.EqualTo(1));
                string path = paths[0];
                Assert.That(Path.GetDirectoryName(path), Is.EqualTo(directory));
                Assert.That(Path.GetFileName(path), Does.Match("^[a-f0-9]{64}\\.lads\\.xml$"));
                Assert.That(Path.GetFileName(path),
                    Is.EqualTo("e3efb4452f1195b1af97a146b8bcd5925bac9c1155e535a2fd2bc30266d7bc66.lads.xml"));
                byte[] persisted;
                using (Stream stream = LocalFileSystem.Instance.OpenRead(path))
                using (var buffer = new MemoryStream())
                {
                    await stream.CopyToAsync(buffer).ConfigureAwait(false);
                    persisted = buffer.ToArray();
                }
                XDocument xml = XDocument.Parse(Encoding.UTF8.GetString(persisted));
                Assert.That(xml.Root!.Element("Identity")!.Value, Is.EqualTo(identity));
                Assert.That(xml.Root.Element("SchemaVersion")!.Value, Is.EqualTo("1"));
                XElement result = xml.Root.Element("FunctionalUnits")!.Element("Item")!
                    .Element("Results")!.Element("Item")!;
                byte[] fileBytes = Convert.FromBase64String(result.Element("Files")!.Element("Item")!
                    .Element("Content")!.Value);
                Assert.That(fileBytes, Is.EqualTo(expected.FunctionalUnits[0].Results[0].Files[0].Content.ToArray()));
                Assert.That(result.Element("Template")!.Element("Description")!.Element("Locale")!.Value,
                    Is.EqualTo("de-DE"));
                using var reopened = new FileSystemLadsStore(LocalFileSystem.Instance, directory);
                AssertSnapshot(await reopened.LoadAsync(identity).ConfigureAwait(false), expected);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Test]
        public async Task AtomicReplaceFailurePreservesPriorSnapshotAndRemovesStagingFileAsync()
        {
            using var fileSystem = new FaultingFileSystem();
            using var store = new FileSystemLadsStore(fileSystem, kVirtualDirectory);
            await store.SaveAsync(kIdentity, CreateSnapshot()).ConfigureAwait(false);
            string path = fileSystem.Inner.Files.Single();
            byte[] previous = fileSystem.Inner.Get(path);
            fileSystem.FailReplace = true;
            LadsStoredDeviceData replacement = CreateSnapshot();
            replacement.OperationCycles = 44;

            await Assert.ThrowsAsync<IOException>(async () =>
                await store.SaveAsync(kIdentity, replacement).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(fileSystem.Inner.Files.ToArray(), Is.EqualTo(new[] { path }));
            Assert.That(fileSystem.Inner.Get(path), Is.EqualTo(previous));
            AssertSnapshot(await store.LoadAsync(kIdentity).ConfigureAwait(false), CreateSnapshot());
            fileSystem.FailReplace = false;
            await store.SaveAsync(kIdentity, replacement).ConfigureAwait(false);
            AssertSnapshot(await store.LoadAsync(kIdentity).ConfigureAwait(false), replacement);
        }

        [Test]
        public async Task FileAccessFailurePropagatesInsteadOfReturningMissingSnapshotAsync()
        {
            using var fileSystem = new FaultingFileSystem();
            using var store = new FileSystemLadsStore(fileSystem, kVirtualDirectory);
            await store.SaveAsync(kIdentity, CreateSnapshot()).ConfigureAwait(false);
            fileSystem.FailRead = true;

            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await store.LoadAsync(kIdentity).ConfigureAwait(false)).ConfigureAwait(false);

            fileSystem.FailRead = false;
            AssertSnapshot(await store.LoadAsync(kIdentity).ConfigureAwait(false), CreateSnapshot());
        }

        /// <summary>
        /// Creates a deterministic snapshot with distinct template versions and binary data spanning base64 chunks.
        /// </summary>
        private static LadsStoredDeviceData CreateSnapshot()
        {
            var template = new LadsStoredTemplateData
            {
                Id = "Recipe<&>",
                Author = "Author é\r\nLab",
                Version = "2.3",
                Description = new LocalizedText("en-US", "Recipe <description>\r\nLine two"),
                SupervisoryTemplateId = "super-template",
                Created = new DateTimeUtc(2026, 1, 2, 3, 4, 5),
                Modified = new DateTimeUtc(2026, 2, 3, 4, 5, 6),
                Content = new LadsProgramTemplate("Recipe").Step("Heat", TimeSpan.FromMilliseconds(125)).ToContent()
            };
            byte[] bytes = new byte[10241];
            for (int ii = 0; ii < bytes.Length; ii++)
            {
                bytes[ii] = (byte)(ii % 251);
            }
            return new LadsStoredDeviceData
            {
                Identity = kIdentity,
                OperationCycles = uint.MaxValue,
                OperationDurationMilliseconds = 12345.625,
                PowerOnDurationMilliseconds = 54321.125,
                FunctionalUnits =
                [
                    new LadsStoredFunctionalUnitData
                    {
                        Name = "Chamber",
                        InterruptedRunId = "interrupted-17",
                        LastInterruptedRunId = "previous-interruption-16",
                        Templates = [template],
                        RemovedTemplateIds = ["RemovedConfiguredRecipe", "RemovedUploadedRecipe"],
                        Results =
                        [
                            new LadsStoredResultData
                            {
                                RunId = "run-42",
                                Template = new LadsStoredTemplateData
                                {
                                    Id = template.Id,
                                    Author = "Previous author",
                                    Version = "1.0",
                                    Description = new LocalizedText("de-DE", "Ergebnis ä"),
                                    SupervisoryTemplateId = null,
                                    Created = template.Created,
                                    Modified = template.Created,
                                    Content = ByteString.From(new byte[] { 0, 10, 13, 255 })
                                },
                                Outcome = LadsProgramOutcome.Stopped,
                                Started = new DateTimeUtc(2026, 3, 4, 5, 6, 7),
                                Stopped = new DateTimeUtc(2026, 3, 4, 5, 7, 8),
                                Properties =
                                [
                                    new KeyValueType { Key = "Operator", Value = "Alice<&>\r\né" },
                                    new KeyValueType { Key = "Empty", Value = string.Empty }
                                ],
                                Samples =
                                [
                                    new SampleInfoType
                                    {
                                        ContainerId = "Rack-1",
                                        SampleId = "Sample-2",
                                        Position = "B3",
                                        CustomData = "custom<&>\r\né"
                                    }
                                ],
                                SupervisoryJobId = "Job-8",
                                SupervisoryTaskId = "Task-9",
                                Variables = [new KeyValueType { Key = "Temperature", Value = "36.75" }],
                                Files =
                                [
                                    new LadsResultFile(
                                        "report.bin", "application/octet-stream", ByteString.From(bytes)),
                                    new LadsResultFile("empty.txt", "text/plain", ByteString.Empty)
                                ],
                                TotalRuntimeMilliseconds = 60000.25,
                                TotalPauseTimeMilliseconds = 999.5
                            }
                        ]
                    }
                ],
                MaintenanceTasks =
                [
                    new LadsStoredMaintenanceData
                    {
                        Name = "Calibrate",
                        State = LadsMaintenanceStateValue.Finished,
                        LastExecutionDate = new DateTimeUtc(2026, 4, 5, 6, 7, 8),
                        LastResult = MaintenanceTaskResultEnum.Failure,
                        PlannedDate = new DateTimeUtc(2026, 5, 6, 7, 8, 9),
                        Comment = new LocalizedText("fr-FR", "Réétalonnage <requis>\r\nDeux")
                    }
                ]
            };
        }

        /// <summary>
        /// Acquires the fencing identity used by an authority-backed store.
        /// </summary>
        private static async Task<LeasePublicationToken> AcquireAsync(
            InMemoryLeasePublicationAuthority authority,
            string group,
            string owner)
        {
            LeaseAcquisitionResult result = await authority.TryAcquireLeaseAsync(
                group,
                owner,
                TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo(LeaseAcquisitionStatus.Acquired));
            return result.Token ?? throw new AssertionException("Successful acquisition did not return a token.");
        }

        /// <summary>
        /// Compares every public snapshot field, including nested structures and each file byte.
        /// </summary>
        private static void AssertSnapshot(LadsStoredDeviceData? actual, LadsStoredDeviceData expected)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual!.SchemaVersion, Is.EqualTo(expected.SchemaVersion));
            Assert.That(actual.Identity, Is.EqualTo(expected.Identity));
            Assert.That(actual.OperationCycles, Is.EqualTo(expected.OperationCycles));
            Assert.That(actual.OperationDurationMilliseconds, Is.EqualTo(expected.OperationDurationMilliseconds));
            Assert.That(actual.PowerOnDurationMilliseconds, Is.EqualTo(expected.PowerOnDurationMilliseconds));
            Assert.That(actual.FunctionalUnits.Count, Is.EqualTo(expected.FunctionalUnits.Count));
            for (int ii = 0; ii < expected.FunctionalUnits.Count; ii++)
            {
                LadsStoredFunctionalUnitData unit = actual.FunctionalUnits[ii];
                LadsStoredFunctionalUnitData expectedUnit = expected.FunctionalUnits[ii];
                Assert.That(unit.Name, Is.EqualTo(expectedUnit.Name));
                Assert.That(unit.InterruptedRunId, Is.EqualTo(expectedUnit.InterruptedRunId));
                Assert.That(unit.LastInterruptedRunId, Is.EqualTo(expectedUnit.LastInterruptedRunId));
                Assert.That(unit.RemovedTemplateIds.ToArray(), Is.EqualTo(expectedUnit.RemovedTemplateIds.ToArray()));
                Assert.That(unit.Templates.Count, Is.EqualTo(expectedUnit.Templates.Count));
                for (int jj = 0; jj < expectedUnit.Templates.Count; jj++)
                {
                    AssertTemplate(unit.Templates[jj], expectedUnit.Templates[jj]);
                }
                Assert.That(unit.Results.Count, Is.EqualTo(expectedUnit.Results.Count));
                for (int jj = 0; jj < expectedUnit.Results.Count; jj++)
                {
                    LadsStoredResultData result = unit.Results[jj];
                    LadsStoredResultData expectedResult = expectedUnit.Results[jj];
                    Assert.That(result.RunId, Is.EqualTo(expectedResult.RunId));
                    AssertTemplate(result.Template, expectedResult.Template);
                    Assert.That(result.Outcome, Is.EqualTo(expectedResult.Outcome));
                    Assert.That(result.Started, Is.EqualTo(expectedResult.Started));
                    Assert.That(result.Stopped, Is.EqualTo(expectedResult.Stopped));
                    Assert.That(result.SupervisoryJobId, Is.EqualTo(expectedResult.SupervisoryJobId));
                    Assert.That(result.SupervisoryTaskId, Is.EqualTo(expectedResult.SupervisoryTaskId));
                    Assert.That(result.TotalRuntimeMilliseconds, Is.EqualTo(expectedResult.TotalRuntimeMilliseconds));
                    Assert.That(result.TotalPauseTimeMilliseconds,
                        Is.EqualTo(expectedResult.TotalPauseTimeMilliseconds));
                    AssertKeyValues(result.Properties, expectedResult.Properties);
                    AssertKeyValues(result.Variables, expectedResult.Variables);
                    Assert.That(result.Samples.Count, Is.EqualTo(expectedResult.Samples.Count));
                    for (int kk = 0; kk < expectedResult.Samples.Count; kk++)
                    {
                        SampleInfoType sample = result.Samples[kk];
                        SampleInfoType expectedSample = expectedResult.Samples[kk];
                        Assert.That(sample.ContainerId, Is.EqualTo(expectedSample.ContainerId));
                        Assert.That(sample.SampleId, Is.EqualTo(expectedSample.SampleId));
                        Assert.That(sample.Position, Is.EqualTo(expectedSample.Position));
                        Assert.That(sample.CustomData, Is.EqualTo(expectedSample.CustomData));
                    }
                    Assert.That(result.Files.Count, Is.EqualTo(expectedResult.Files.Count));
                    for (int kk = 0; kk < expectedResult.Files.Count; kk++)
                    {
                        Assert.That(result.Files[kk].Name, Is.EqualTo(expectedResult.Files[kk].Name));
                        Assert.That(result.Files[kk].MimeType, Is.EqualTo(expectedResult.Files[kk].MimeType));
                        Assert.That(result.Files[kk].Content.ToArray(),
                            Is.EqualTo(expectedResult.Files[kk].Content.ToArray()));
                    }
                }
            }
            Assert.That(actual.MaintenanceTasks.Count, Is.EqualTo(expected.MaintenanceTasks.Count));
            for (int ii = 0; ii < expected.MaintenanceTasks.Count; ii++)
            {
                LadsStoredMaintenanceData task = actual.MaintenanceTasks[ii];
                LadsStoredMaintenanceData expectedTask = expected.MaintenanceTasks[ii];
                Assert.That(task.Name, Is.EqualTo(expectedTask.Name));
                Assert.That(task.State, Is.EqualTo(expectedTask.State));
                Assert.That(task.LastExecutionDate, Is.EqualTo(expectedTask.LastExecutionDate));
                Assert.That(task.LastResult, Is.EqualTo(expectedTask.LastResult));
                Assert.That(task.PlannedDate, Is.EqualTo(expectedTask.PlannedDate));
                Assert.That(task.Comment.Locale, Is.EqualTo(expectedTask.Comment.Locale));
                Assert.That(task.Comment.Text, Is.EqualTo(expectedTask.Comment.Text));
            }
        }

        /// <summary>
        /// Compares template metadata and serialized content independently of object identity.
        /// </summary>
        private static void AssertTemplate(LadsStoredTemplateData actual, LadsStoredTemplateData expected)
        {
            Assert.That(actual.Id, Is.EqualTo(expected.Id));
            Assert.That(actual.Author, Is.EqualTo(expected.Author));
            Assert.That(actual.Version, Is.EqualTo(expected.Version));
            Assert.That(actual.Description.Locale, Is.EqualTo(expected.Description.Locale));
            Assert.That(actual.Description.Text, Is.EqualTo(expected.Description.Text));
            Assert.That(actual.SupervisoryTemplateId, Is.EqualTo(expected.SupervisoryTemplateId));
            Assert.That(actual.Created, Is.EqualTo(expected.Created));
            Assert.That(actual.Modified, Is.EqualTo(expected.Modified));
            Assert.That(actual.Content.ToArray(), Is.EqualTo(expected.Content.ToArray()));
        }

        /// <summary>
        /// Compares every string-valued key-value pair.
        /// </summary>
        private static void AssertKeyValues(ArrayOf<KeyValueType> actual, ArrayOf<KeyValueType> expected)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (int ii = 0; ii < expected.Count; ii++)
            {
                Assert.That(actual[ii].Key, Is.EqualTo(expected[ii].Key));
                Assert.That(actual[ii].Value, Is.EqualTo(expected[ii].Value));
            }
        }

        /// <summary>
        /// Mutates independent nested objects and collection backing arrays to expose shallow copying.
        /// </summary>
        private static void MutateSnapshot(LadsStoredDeviceData data)
        {
            data.OperationCycles = 0;
            LadsStoredFunctionalUnitData unit = data.FunctionalUnits[0];
            unit.Name = "changed";
            unit.LastInterruptedRunId = "changed interruption";
            unit.Templates[0].Author = "changed";
            unit.Templates[0].Content = ByteString.Empty;
            unit.Results[0].Template.Description = new LocalizedText("changed");
            unit.Results[0].Properties[0].Value = "changed";
            unit.Results[0].Samples[0].SampleId = "changed";
            unit.Results[0].Variables[0].Value = "changed";
            Assert.That(MemoryMarshal.TryGetArray(unit.Results[0].Files[0].Content.Memory,
                out ArraySegment<byte> fileBytes), Is.True);
            fileBytes.Array![fileBytes.Offset] ^= 0xFF;
            unit.Results[0].Files = [];
            Assert.That(MemoryMarshal.TryGetArray(unit.RemovedTemplateIds.Memory,
                out ArraySegment<string> removedIds), Is.True);
            removedIds.Array![removedIds.Offset] = "changed backing array";
            data.MaintenanceTasks[0].Comment = new LocalizedText("changed");
        }

        /// <summary>
        /// Creates one invalid snapshot partition while leaving the other validation inputs valid.
        /// </summary>
        private static void MakeInvalid(LadsStoredDeviceData data, string invalidKind)
        {
            LadsStoredFunctionalUnitData unit = data.FunctionalUnits[0];
            switch (invalidKind)
            {
                case "Schema":
                    data.SchemaVersion = 2;
                    break;
                case "Identity":
                    data.Identity = "foreign";
                    break;
                case "Unit":
                    data.FunctionalUnits = [unit, unit];
                    break;
                case "Task":
                    data.MaintenanceTasks = [data.MaintenanceTasks[0], data.MaintenanceTasks[0]];
                    break;
                case "Template":
                    unit.Templates = [unit.Templates[0], unit.Templates[0]];
                    break;
                case "Result":
                    unit.Results = [unit.Results[0], unit.Results[0]];
                    break;
                case "File":
                    unit.Results[0].Files = [unit.Results[0].Files[0], unit.Results[0].Files[0]];
                    break;
                case "Tombstone":
                    unit.RemovedTemplateIds = ["duplicate", "duplicate"];
                    break;
                case "DeletedTemplate":
                    unit.RemovedTemplateIds = [unit.Templates[0].Id];
                    break;
                case "Outcome":
                    unit.Results[0].Outcome = (LadsProgramOutcome)99;
                    break;
                case "MaintenanceState":
                    data.MaintenanceTasks[0].State = (LadsMaintenanceStateValue)99;
                    break;
                case "MaintenanceResult":
                    data.MaintenanceTasks[0].LastResult = (MaintenanceTaskResultEnum)99;
                    break;
                case "PauseExceedsRuntime":
                    unit.Results[0].TotalPauseTimeMilliseconds = unit.Results[0].TotalRuntimeMilliseconds + 1;
                    break;
                case "StoppedBeforeStarted":
                    unit.Results[0].Stopped = new DateTimeUtc(unit.Results[0].Started.Value - 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(invalidKind));
            }
        }

        /// <summary>
        /// Damages persisted XML without going through the provider's save-time validation.
        /// </summary>
        private static byte[] InvalidXml(byte[] valid, string invalidKind)
        {
            string text = Encoding.UTF8.GetString(valid);
            if (invalidKind == "Malformed")
            {
                return Encoding.UTF8.GetBytes("<LadsDevice");
            }
            if (invalidKind == "Truncated")
            {
                return valid.Take(valid.Length / 2).ToArray();
            }
            if (invalidKind == "Dtd")
            {
                return Encoding.UTF8.GetBytes(
                    "<!DOCTYPE LadsDevice [<!ENTITY x 'unsafe'>]><LadsDevice>&x;</LadsDevice>");
            }
            XDocument document = XDocument.Parse(text);
            XElement root = document.Root!;
            switch (invalidKind)
            {
                case "Schema":
                    root.Element("SchemaVersion")!.Value = "2";
                    break;
                case "Identity":
                    root.Element("Identity")!.Value = "foreign";
                    break;
                case "Unknown":
                    root.Element("OperationCycles")!.Name = "UnknownField";
                    break;
                case "Negative":
                    root.Element("PowerOnDurationMilliseconds")!.Value = "-1";
                    break;
                case "ExcessiveDuration":
                    root.Element("OperationDurationMilliseconds")!.Value = "1000000000000000";
                    break;
                case "PauseExceedsRuntime":
                    root.Descendants("TotalPauseTimeMilliseconds").Single().Value = "60001";
                    break;
                case "StoppedBeforeStarted":
                    root.Descendants("Stopped").Single().Value = "0";
                    break;
                case "Duplicate":
                    XElement units = root.Element("FunctionalUnits")!;
                    units.Add(new XElement(units.Element("Item")!));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(invalidKind));
            }
            return Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
        }

        /// <summary>A deterministic device key containing non-ASCII text.</summary>
        private const string kIdentity = "urn:lab:device:é-42";

        /// <summary>An isolated virtual path; no files are written there by virtual providers.</summary>
        private const string kVirtualDirectory = @"D:\temp\LadsStoreProviderTests";

        /// <summary>
        /// Supplies a test-controlled monotonic timestamp without wall-clock delays.
        /// </summary>
        private sealed class ManualTimeProvider : TimeProvider
        {
            /// <summary>
            /// Gets the frequency of the test clock's timestamp units.
            /// </summary>
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            /// <summary>
            /// Gets the current monotonic timestamp.
            /// </summary>
            public override long GetTimestamp()
            {
                return Interlocked.Read(ref m_timestamp);
            }

            /// <summary>
            /// Advances the test clock.
            /// </summary>
            public void Advance(TimeSpan duration)
            {
                Interlocked.Add(ref m_timestamp, duration.Ticks);
            }

            /// <summary>The test-controlled monotonic timestamp.</summary>
            private long m_timestamp;
        }

        /// <summary>
        /// Owns an isolated provider and virtual file system for one test case.
        /// </summary>
        private sealed class StoreScope : IDisposable
        {
            /// <summary>
            /// Creates either store using an independent backing map.
            /// </summary>
            public StoreScope(bool fileSystem, int maxBytes = 64 * 1024 * 1024)
            {
                Store = fileSystem
                    ? new FileSystemLadsStore(FileSystem, kVirtualDirectory, maxBytes)
                    : new InMemoryLadsStore(maxBytes);
            }

            /// <summary>
            /// Gets the selected store.
            /// </summary>
            public ILadsStore Store { get; }

            /// <summary>
            /// Gets the virtual file system for inspecting complete persisted bytes.
            /// </summary>
            public VirtualFileSystem FileSystem { get; } = new();

            /// <summary>
            /// Gets the sole committed snapshot path.
            /// </summary>
            public string SnapshotPath => FileSystem.Files.Single();

            /// <summary>
            /// Disposes the selected store and backing map.
            /// </summary>
            public void Dispose()
            {
                (Store as IDisposable)?.Dispose();
                FileSystem.Dispose();
            }
        }

        /// <summary>
        /// Injects deterministic publication and read failures around the actual virtual file system.
        /// </summary>
        private sealed class FaultingFileSystem : IFileSystem, IDisposable
        {
            /// <summary>
            /// Gets the backing file system for byte-for-byte assertions.
            /// </summary>
            public VirtualFileSystem Inner { get; } = new();

            /// <summary>
            /// Gets or sets whether atomic publication fails before replacing the old snapshot.
            /// </summary>
            public bool FailReplace { get; set; }

            /// <summary>
            /// Gets or sets whether reading fails with an access error.
            /// </summary>
            public bool FailRead { get; set; }

            /// <inheritdoc/>
            public bool Exists(string path, bool isDirectory = false)
            {
                return Inner.Exists(path, isDirectory);
            }

            /// <inheritdoc/>
            public void Delete(string path, bool isDirectory = false)
            {
                Inner.Delete(path, isDirectory);
            }

            /// <inheritdoc/>
            public Stream OpenRead(string path)
            {
                return FailRead
                    ? throw new UnauthorizedAccessException("Injected read failure.")
                    : Inner.OpenRead(path);
            }

            /// <inheritdoc/>
            public Stream OpenWrite(string path)
            {
                return Inner.OpenWrite(path);
            }

            /// <inheritdoc/>
            public void Replace(string sourcePath, string destinationPath)
            {
                if (FailReplace)
                {
                    throw new IOException("Injected atomic replacement failure.");
                }
                Inner.Replace(sourcePath, destinationPath);
            }

            /// <inheritdoc/>
            public DateTime GetLastWriteTime(string path)
            {
                return Inner.GetLastWriteTime(path);
            }

            /// <inheritdoc/>
            public long GetLength(string path)
            {
                return Inner.GetLength(path);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                Inner.Dispose();
            }
        }
    }
}
