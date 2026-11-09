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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// The EPC Gen2 behavior of the simulated RFID reader: status codes of OPC 30010 Table 33 for
    /// read, write, identifier initialisation, lock, password and kill.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    public sealed class InMemoryRfidReaderProviderTests
    {
        private static readonly ByteString s_epc = AutoIdTestData.Bytes(0xE2, 0x00, 0x10, 0x20);
        private static readonly ByteString s_access = AutoIdTestData.Bytes(1, 2, 3, 4);
        private static readonly ByteString s_kill = AutoIdTestData.Bytes(9, 8, 7, 6);
        private static readonly ByteString s_other = AutoIdTestData.Bytes(5, 5, 5, 5);

        [SetUp]
        public async Task SetUpAsync()
        {
            m_reader = new InMemoryRfidReaderProvider(TimeSpan.FromMilliseconds(2));
            await m_reader.PlaceTagAsync(new InMemoryRfidTag(s_epc)
            {
                AccessPassword = s_access,
                KillPassword = s_kill,
                UserMemorySize = 16,
                Antenna = 2,
                Strength = -48
            }).ConfigureAwait(false);
        }

        [TearDown]
        public void TearDown()
        {
            m_reader!.Dispose();
        }

        [Test]
        public void TagsMustHaveAnEvenEpcOfValidLength()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(() => new InMemoryRfidTag(AutoIdTestData.Bytes(1)));
                Assert.Throws<ArgumentException>(() => new InMemoryRfidTag(AutoIdTestData.Bytes(1, 2, 3)));
                Assert.Throws<ArgumentException>(() => new InMemoryRfidTag(ByteString.From(new byte[64])));
                Assert.DoesNotThrow(() => new InMemoryRfidTag(ByteString.From(new byte[62])));
            });
        }

        [Test]
        public async Task PlacingAnExistingTagAgainFailsAndTheCountIsKeptAsync()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await m_reader!.PlaceTagAsync(new InMemoryRfidTag(s_epc)).ConfigureAwait(false));
            Assert.That(m_reader!.TagCount, Is.EqualTo(1));
            Assert.That(await m_reader.RemoveTagAsync(s_epc).ConfigureAwait(false), Is.True);
            Assert.That(await m_reader.RemoveTagAsync(s_epc).ConfigureAwait(false), Is.False);
            await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await m_reader.PlaceTagAsync(null!).ConfigureAwait(false));
        }

        [Test]
        public async Task AScanReportsEveryTagWithItsSightingAsync()
        {
            AutoIdScanOutcome outcome = await m_reader!.ScanAsync(
                AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);

            var result = (RfidScanResult)outcome.Results[0];
            Assert.Multiple(() =>
            {
                Assert.That(outcome.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(outcome.Results.Count, Is.EqualTo(1));
                Assert.That(result.CodeType, Is.EqualTo("EPC"));
                Assert.That(result.ScanData.SwitchField, Is.EqualTo(ScanDataFields.Epc));
                Assert.That(result.ScanData.Epc.UId, Is.EqualTo(s_epc));
                Assert.That(result.ScanData.Epc.PC, Is.EqualTo((ushort)(2 << 11)));
                Assert.That(result.Sighting[0].Antenna, Is.EqualTo(2));
                Assert.That(result.Sighting[0].Strength, Is.EqualTo(-48));
            });
        }

        [Test]
        public async Task AScanWithoutTagsReportsNoIdentifierAsync()
        {
            await m_reader!.RemoveTagAsync(s_epc).ConfigureAwait(false);

            AutoIdScanOutcome outcome = await m_reader.ScanAsync(AutoIdTestData.Settings(cycles: 2)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.NO_IDENTIFIER));
                Assert.That(outcome.Results.Count, Is.Zero);
            });
        }

        [Test]
        public async Task AScanWaitsForTheDurationOrForDataAsync()
        {
            await m_reader!.RemoveTagAsync(s_epc).ConfigureAwait(false);
            DateTime start = DateTime.UtcNow;
            AutoIdScanOutcome empty = await m_reader.ScanAsync(AutoIdTestData.Settings(duration: 60)).ConfigureAwait(false);
            TimeSpan waited = DateTime.UtcNow - start;

            Task<AutoIdScanOutcome> waiting = m_reader.ScanAsync(
                AutoIdTestData.Settings(duration: 5000, dataAvailable: true)).AsTask();
            await Task.Delay(30).ConfigureAwait(false);
            await m_reader.PlaceTagAsync(new InMemoryRfidTag(s_epc)).ConfigureAwait(false);
            AutoIdScanOutcome found = await waiting.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(waited, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(40)));
                Assert.That(empty.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.NO_IDENTIFIER));
                Assert.That(found.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(found.Results.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task AScanHonoursCancellationAsync()
        {
            using var cancel = new CancellationTokenSource(20);
            await Assert.CatchAsync<OperationCanceledException>(async () => await m_reader!.ScanAsync(
                AutoIdTestData.Settings(duration: 60_000), cancel.Token).ConfigureAwait(false));
        }

        [TestCase(0, 0u, 4u, AutoIdOperationStatusEnumeration.SUCCESS, 4)]
        [TestCase(1, 0u, 0u, AutoIdOperationStatusEnumeration.SUCCESS, 8)]
        [TestCase(2, 0u, 4u, AutoIdOperationStatusEnumeration.SUCCESS, 4)]
        [TestCase(3, 4u, 0u, AutoIdOperationStatusEnumeration.SUCCESS, 12)]
        [TestCase(3, 17u, 1u, AutoIdOperationStatusEnumeration.OUT_OF_RANGE_ERROR, 0)]
        [TestCase(3, 8u, 9u, AutoIdOperationStatusEnumeration.OUT_OF_RANGE_ERROR, 0)]
        [TestCase(4, 0u, 1u, AutoIdOperationStatusEnumeration.REGION_NOT_FOUND_ERROR, 0)]
        public async Task ReadTagReturnsTheBankOrTheMatchingStatusAsync(
            int region, uint offset, uint length, AutoIdOperationStatusEnumeration status, int bytes)
        {
            AutoIdTagReadOutcome outcome = await m_reader!.ReadTagAsync(
                Identify(s_epc), "EPC", (ushort)region, offset, length, default).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Status, Is.EqualTo(status));
                Assert.That(outcome.Data.Length, Is.EqualTo(bytes));
            });
        }

        [Test]
        public async Task ReadTagReturnsTheEpcBankLayoutAsync()
        {
            AutoIdTagReadOutcome outcome = await m_reader!.ReadTagAsync(
                Identify(s_epc), "EPC", 1, 2, 6, default).ConfigureAwait(false);

            Assert.That(
                outcome.Data,
                Is.EqualTo(AutoIdTestData.Bytes(0x10, 0x00, 0xE2, 0x00, 0x10, 0x20)),
                "protocol control word, then the EPC");
        }

        [Test]
        public async Task TheReservedBankIsReadableOnlyUnlessLockedAsync()
        {
            AutoIdTagReadOutcome open = await m_reader!.ReadTagAsync(
                Identify(s_epc), "EPC", 0, 0, 8, default).ConfigureAwait(false);
            await m_reader.LockTagAsync(
                Identify(s_epc), "EPC", s_access, RfidLockRegionEnumeration.Access, RfidLockOperationEnumeration.Lock, 0, 0)
                .ConfigureAwait(false);
            AutoIdTagReadOutcome noPassword = await m_reader.ReadTagAsync(
                Identify(s_epc), "EPC", 0, 0, 8, default).ConfigureAwait(false);
            AutoIdTagReadOutcome wrongPassword = await m_reader.ReadTagAsync(
                Identify(s_epc), "EPC", 0, 0, 8, s_other).ConfigureAwait(false);
            AutoIdTagReadOutcome rightPassword = await m_reader.ReadTagAsync(
                Identify(s_epc), "EPC", 0, 0, 8, s_access).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(open.Data, Is.EqualTo(ByteString.From(s_kill.Span.ToArray().Concat(s_access.Span.ToArray()).ToArray())));
                Assert.That(noPassword.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.PERMISSON_ERROR));
                Assert.That(wrongPassword.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.PASSWORD_ERROR));
                Assert.That(rightPassword.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
            });
        }

        [TestCase("EPC", ScanDataFields.ByteString, true)]
        [TestCase("UID", ScanDataFields.ByteString, true)]
        [TestCase("RAW:BYTES", ScanDataFields.ByteString, true)]
        [TestCase("RAW:STRING", ScanDataFields.String, true)]
        [TestCase("EPC", ScanDataFields.Epc, true)]
        [TestCase("GS1", ScanDataFields.ByteString, false)]
        [TestCase("RAW:STRING", ScanDataFields.ByteString, false)]
        [TestCase("EPC", ScanDataFields.String, false)]
        public async Task TagsAreAddressedByTheCodeTypesTheReaderSupportsAsync(
            string codeType, ScanDataFields arm, bool supported)
        {
            ScanData identifier = arm switch
            {
                ScanDataFields.Epc => Identify(s_epc),
                ScanDataFields.String => new ScanData { SwitchField = ScanDataFields.String, String = "E2001020" },
                _ => new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = s_epc }
            };

            AutoIdTagReadOutcome outcome = await m_reader!.ReadTagAsync(
                identifier, codeType, 1, 0, 4, default).ConfigureAwait(false);

            Assert.That(
                outcome.Status,
                Is.EqualTo(supported
                    ? AutoIdOperationStatusEnumeration.SUCCESS
                    : AutoIdOperationStatusEnumeration.NOT_SUPPORTED_BY_DEVICE));
        }

        [Test]
        public async Task AnUnknownTagAndMalformedHexAreNoIdentifierAsync()
        {
            AutoIdTagReadOutcome unknown = await m_reader!.ReadTagAsync(
                Identify(s_other), "EPC", 1, 0, 4, default).ConfigureAwait(false);
            AutoIdTagReadOutcome malformed = await m_reader.ReadTagAsync(
                new ScanData { SwitchField = ScanDataFields.String, String = "zz" },
                "RAW:STRING", 1, 0, 4, default).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(unknown.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.NO_IDENTIFIER));
                Assert.That(malformed.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.NO_IDENTIFIER));
            });
        }

        [Test]
        public async Task WriteTagStoresDataInTheUserMemoryAsync()
        {
            AutoIdOperationStatusEnumeration status = await m_reader!.WriteTagAsync(
                Identify(s_epc), "EPC", 3, 4, AutoIdTestData.Bytes("ޭ"u8.ToArray()), default).ConfigureAwait(false);
            ByteString bank = m_reader.GetBank(s_epc, 3);

            Assert.Multiple(() =>
            {
                Assert.That(status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(bank.Span.Slice(4, 2).ToArray(), Is.EqualTo("ޭ"u8.ToArray()));
            });
        }

        [TestCase(2, 0u, AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR)]
        [TestCase(1, 0u, AutoIdOperationStatusEnumeration.OUT_OF_RANGE_ERROR)]
        [TestCase(1, 1u, AutoIdOperationStatusEnumeration.OUT_OF_RANGE_ERROR)]
        [TestCase(3, 15u, AutoIdOperationStatusEnumeration.OUT_OF_RANGE_ERROR)]
        [TestCase(7, 0u, AutoIdOperationStatusEnumeration.REGION_NOT_FOUND_ERROR)]
        public async Task WriteTagRefusesTheMemoryItCannotWriteAsync(
            int region, uint offset, AutoIdOperationStatusEnumeration expected)
        {
            AutoIdOperationStatusEnumeration status = await m_reader!.WriteTagAsync(
                Identify(s_epc), "EPC", (ushort)region, offset, AutoIdTestData.Bytes(1, 2), default).ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(expected));
        }

        [Test]
        public async Task WriteTagToTheEpcBankChangesTheIdentifierAsync()
        {
            await m_reader!.WriteTagAsync(
                Identify(s_epc), "EPC", 1, 4, AutoIdTestData.Bytes(0xAA, 0xBB), default).ConfigureAwait(false);

            AutoIdScanOutcome outcome = await m_reader.ScanAsync(AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);

            Assert.That(
                outcome.Results[0].ScanData.Epc.UId,
                Is.EqualTo(AutoIdTestData.Bytes(0xAA, 0xBB, 0x10, 0x20)));
        }

        [Test]
        public async Task LockedMemoryNeedsTheAccessPasswordAsync()
        {
            AutoIdOperationStatusEnumeration noPassword = await m_reader!.LockTagAsync(
                Identify(s_epc), "EPC", default, RfidLockRegionEnumeration.User, RfidLockOperationEnumeration.Lock, 0, 0)
                .ConfigureAwait(false);
            AutoIdOperationStatusEnumeration wrongPassword = await m_reader.LockTagAsync(
                Identify(s_epc), "EPC", s_other, RfidLockRegionEnumeration.User, RfidLockOperationEnumeration.Lock, 0, 0)
                .ConfigureAwait(false);
            AutoIdOperationStatusEnumeration locked = await m_reader.LockTagAsync(
                Identify(s_epc), "EPC", s_access, RfidLockRegionEnumeration.User, RfidLockOperationEnumeration.Lock, 0, 0)
                .ConfigureAwait(false);
            AutoIdOperationStatusEnumeration writeWithout = await m_reader.WriteTagAsync(
                Identify(s_epc), "EPC", 3, 0, AutoIdTestData.Bytes(1), default).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration writeWrong = await m_reader.WriteTagAsync(
                Identify(s_epc), "EPC", 3, 0, AutoIdTestData.Bytes(1), s_other).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration writeRight = await m_reader.WriteTagAsync(
                Identify(s_epc), "EPC", 3, 0, AutoIdTestData.Bytes(1), s_access).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration unlocked = await m_reader.LockTagAsync(
                Identify(s_epc), "EPC", s_access, RfidLockRegionEnumeration.User, RfidLockOperationEnumeration.Unlock, 0, 0)
                .ConfigureAwait(false);
            AutoIdOperationStatusEnumeration writeOpen = await m_reader.WriteTagAsync(
                Identify(s_epc), "EPC", 3, 0, AutoIdTestData.Bytes(2), default).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(noPassword, Is.EqualTo(AutoIdOperationStatusEnumeration.PERMISSON_ERROR));
                Assert.That(wrongPassword, Is.EqualTo(AutoIdOperationStatusEnumeration.PASSWORD_ERROR));
                Assert.That(locked, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(writeWithout, Is.EqualTo(AutoIdOperationStatusEnumeration.PERMISSON_ERROR));
                Assert.That(writeWrong, Is.EqualTo(AutoIdOperationStatusEnumeration.PASSWORD_ERROR));
                Assert.That(writeRight, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(unlocked, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(writeOpen, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
            });
        }

        [Test]
        public async Task PermanentLocksCannotBeUndoneAsync()
        {
            AutoIdOperationStatusEnumeration permanent = await m_reader!.LockTagAsync(
                Identify(s_epc), "EPC", s_access, RfidLockRegionEnumeration.User,
                RfidLockOperationEnumeration.PermanentLock, 0, 0).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration write = await m_reader.WriteTagAsync(
                Identify(s_epc), "EPC", 3, 0, AutoIdTestData.Bytes(1), s_access).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration unlock = await m_reader.LockTagAsync(
                Identify(s_epc), "EPC", s_access, RfidLockRegionEnumeration.User,
                RfidLockOperationEnumeration.Unlock, 0, 0).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration permanentUnlock = await m_reader.LockTagAsync(
                Identify(s_epc), "EPC", s_access, RfidLockRegionEnumeration.EPC,
                RfidLockOperationEnumeration.PermanentUnlock, 0, 0).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration lockAgain = await m_reader.LockTagAsync(
                Identify(s_epc), "EPC", s_access, RfidLockRegionEnumeration.EPC,
                RfidLockOperationEnumeration.Lock, 0, 0).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(permanent, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(write, Is.EqualTo(AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR));
                Assert.That(unlock, Is.EqualTo(AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR));
                Assert.That(permanentUnlock, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(lockAgain, Is.EqualTo(AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR));
            });
        }

        [Test]
        public async Task ATagWithoutAccessPasswordCannotBeLockedWithOneAsync()
        {
            ByteString open = AutoIdTestData.Bytes(0x11, 0x22);
            await m_reader!.PlaceTagAsync(new InMemoryRfidTag(open)).ConfigureAwait(false);

            AutoIdOperationStatusEnumeration status = await m_reader.LockTagAsync(
                Identify(open), "EPC", default, RfidLockRegionEnumeration.User, RfidLockOperationEnumeration.Lock, 0, 0)
                .ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS), "no password set, no password needed");
        }

        [Test]
        public async Task SetTagPasswordChangesTheAccessAndKillPasswordsAsync()
        {
            AutoIdOperationStatusEnumeration access = await m_reader!.SetTagPasswordAsync(
                Identify(s_epc), "EPC", RfidPasswordTypeEnumeration.Access, default, s_other).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration kill = await m_reader.SetTagPasswordAsync(
                Identify(s_epc), "EPC", RfidPasswordTypeEnumeration.Kill, default, AutoIdTestData.Bytes(0, 0, 0, 1))
                .ConfigureAwait(false);
            AutoIdOperationStatusEnumeration read = await m_reader.SetTagPasswordAsync(
                Identify(s_epc), "EPC", RfidPasswordTypeEnumeration.Read, default, s_other).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration write = await m_reader.SetTagPasswordAsync(
                Identify(s_epc), "EPC", RfidPasswordTypeEnumeration.Write, default, s_other).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration length = await m_reader.SetTagPasswordAsync(
                Identify(s_epc), "EPC", RfidPasswordTypeEnumeration.Access, default, AutoIdTestData.Bytes(1))
                .ConfigureAwait(false);
            ByteString reserved = m_reader.GetBank(s_epc, 0);

            Assert.Multiple(() =>
            {
                Assert.That(access, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(kill, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(read, Is.EqualTo(AutoIdOperationStatusEnumeration.NOT_SUPPORTED_BY_TAG));
                Assert.That(write, Is.EqualTo(AutoIdOperationStatusEnumeration.NOT_SUPPORTED_BY_TAG));
                Assert.That(length, Is.EqualTo(AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR));
                Assert.That(reserved.Span.ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 1, 5, 5, 5, 5 }));
            });
        }

        [Test]
        public async Task ALockedPasswordNeedsTheCurrentAccessPasswordToChangeAsync()
        {
            await m_reader!.LockTagAsync(
                Identify(s_epc), "EPC", s_access, RfidLockRegionEnumeration.Access,
                RfidLockOperationEnumeration.Lock, 0, 0).ConfigureAwait(false);

            AutoIdOperationStatusEnumeration wrong = await m_reader.SetTagPasswordAsync(
                Identify(s_epc), "EPC", RfidPasswordTypeEnumeration.Access, s_other, s_other).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration right = await m_reader.SetTagPasswordAsync(
                Identify(s_epc), "EPC", RfidPasswordTypeEnumeration.Access, s_access, s_other).ConfigureAwait(false);
            await m_reader.LockTagAsync(
                Identify(s_epc), "EPC", s_other, RfidLockRegionEnumeration.Kill,
                RfidLockOperationEnumeration.PermanentLock, 0, 0).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration permanent = await m_reader.SetTagPasswordAsync(
                Identify(s_epc), "EPC", RfidPasswordTypeEnumeration.Kill, s_other, s_access).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(wrong, Is.EqualTo(AutoIdOperationStatusEnumeration.PASSWORD_ERROR));
                Assert.That(right, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(permanent, Is.EqualTo(AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR));
            });
        }

        [Test]
        public async Task WriteTagIdRewritesTheEpcAndTheProtocolControlWordAsync()
        {
            ByteString next = AutoIdTestData.Bytes(1, 2, 3, 4, 5, 6);

            AutoIdOperationStatusEnumeration status = await m_reader!.WriteTagIdAsync(
                Identify(s_epc), "EPC", next, 0x17, true, default).ConfigureAwait(false);
            AutoIdScanOutcome scan = await m_reader.ScanAsync(AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);

            var result = (RfidScanResult)scan.Results[0];
            Assert.Multiple(() =>
            {
                Assert.That(status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(result.ScanData.Epc.UId, Is.EqualTo(next));
                Assert.That(result.ScanData.Epc.PC, Is.EqualTo((ushort)((3 << 11) | 0x0100 | 0x17)));
                Assert.That(m_reader.TagCount, Is.EqualTo(1));
            });
        }

        [TestCase(1, AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR)]
        [TestCase(3, AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR)]
        [TestCase(0, AutoIdOperationStatusEnumeration.OUT_OF_RANGE_ERROR)]
        [TestCase(64, AutoIdOperationStatusEnumeration.OUT_OF_RANGE_ERROR)]
        public async Task WriteTagIdRejectsInvalidLengthsAsync(int length, AutoIdOperationStatusEnumeration expected)
        {
            AutoIdOperationStatusEnumeration status = await m_reader!.WriteTagIdAsync(
                Identify(s_epc), "EPC", ByteString.From(new byte[length]), 0, false, default).ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(expected));
        }

        [Test]
        public async Task WriteTagIdCannotDuplicateAnotherTagAndNeedsThePasswordWhenLockedAsync()
        {
            ByteString second = AutoIdTestData.Bytes(0x77, 0x88);
            await m_reader!.PlaceTagAsync(new InMemoryRfidTag(second)).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration duplicate = await m_reader.WriteTagIdAsync(
                Identify(second), "EPC", s_epc, 0, false, default).ConfigureAwait(false);
            await m_reader.LockTagAsync(
                Identify(s_epc), "EPC", s_access, RfidLockRegionEnumeration.EPC,
                RfidLockOperationEnumeration.Lock, 0, 0).ConfigureAwait(false);

            AutoIdOperationStatusEnumeration locked = await m_reader.WriteTagIdAsync(
                Identify(s_epc), "EPC", AutoIdTestData.Bytes(1, 1), 0, false, default).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration unlockedWithPassword = await m_reader.WriteTagIdAsync(
                Identify(s_epc), "EPC", AutoIdTestData.Bytes(1, 1), 0, false, s_access).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(duplicate, Is.EqualTo(AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR));
                Assert.That(locked, Is.EqualTo(AutoIdOperationStatusEnumeration.PERMISSON_ERROR));
                Assert.That(unlockedWithPassword, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
            });
        }

        [Test]
        public async Task KillTagNeedsANonZeroKillPasswordAndTheRightOneAsync()
        {
            ByteString zero = AutoIdTestData.Bytes("3D"u8.ToArray());
            await m_reader!.PlaceTagAsync(new InMemoryRfidTag(zero)).ConfigureAwait(false);

            AutoIdOperationStatusEnumeration ignored = await m_reader.KillTagAsync(
                Identify(zero), "EPC", s_kill).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration empty = await m_reader.KillTagAsync(
                Identify(s_epc), "EPC", default).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration wrong = await m_reader.KillTagAsync(
                Identify(s_epc), "EPC", s_other).ConfigureAwait(false);
            int before = m_reader.TagCount;
            AutoIdOperationStatusEnumeration killed = await m_reader.KillTagAsync(
                Identify(s_epc), "EPC", s_kill).ConfigureAwait(false);
            AutoIdOperationStatusEnumeration gone = await m_reader.KillTagAsync(
                Identify(s_epc), "EPC", s_kill).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ignored, Is.EqualTo(AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR));
                Assert.That(empty, Is.EqualTo(AutoIdOperationStatusEnumeration.PERMISSON_ERROR));
                Assert.That(wrong, Is.EqualTo(AutoIdOperationStatusEnumeration.PASSWORD_ERROR));
                Assert.That(before, Is.EqualTo(2));
                Assert.That(killed, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(gone, Is.EqualTo(AutoIdOperationStatusEnumeration.NO_IDENTIFIER));
                Assert.That(m_reader.TagCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task GetBankIgnoresUnknownTagsAndBanksAsync()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(m_reader!.GetBank(s_other, 1).Length, Is.Zero);
                Assert.That(m_reader.GetBank(s_epc, 4).Length, Is.Zero);
                Assert.That(m_reader.GetBank(s_epc, -1).Length, Is.Zero);
            });
        }

        [Test]
        public async Task TriggerScanNeedsASinkAndATagAsync()
        {
            var sink = new RecordingSink();
            bool detached = await m_reader!.TriggerScanAsync().ConfigureAwait(false);
            await m_reader.AttachAsync(sink).ConfigureAwait(false);
            bool triggered = await m_reader.TriggerScanAsync().ConfigureAwait(false);
            await m_reader.RemoveTagAsync(s_epc).ConfigureAwait(false);
            bool empty = await m_reader.TriggerScanAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(detached, Is.False);
                Assert.That(triggered, Is.True);
                Assert.That(empty, Is.False);
                Assert.That(sink.Scans, Has.Count.EqualTo(1));
                Assert.That(sink.Presence, Is.EqualTo(new ushort[] { 0 }), "removing the tag reports the presence");
            });
        }

        [Test]
        public async Task AsynchronousScanReportsUntilItIsStoppedAsync()
        {
            var sink = new RecordingSink();
            await m_reader!.AttachAsync(sink).ConfigureAwait(false);

            AutoIdOperationStatusEnumeration started = await m_reader.StartScanAsync(AutoIdTestData.Settings())
                .ConfigureAwait(false);
            AutoIdOperationStatusEnumeration second = await m_reader.StartScanAsync(AutoIdTestData.Settings())
                .ConfigureAwait(false);
            await sink.WaitForScansAsync(2).ConfigureAwait(false);
            await m_reader.StopScanAsync().ConfigureAwait(false);
            int afterStop = sink.Scans.Count;
            await Task.Delay(30).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(started, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(second, Is.EqualTo(AutoIdOperationStatusEnumeration.DEVICE_NOT_READY));
                Assert.That(sink.Scans, Has.Count.EqualTo(afterStop), "no report after the stop returned");
                Assert.That(sink.Completions, Is.Zero, "a stop is not a completion");
            });
        }

        [Test]
        public async Task AsynchronousScanCompletesOnItsOwnWhenTheDataIsAvailableAsync()
        {
            var sink = new RecordingSink();
            await m_reader!.AttachAsync(sink).ConfigureAwait(false);

            await m_reader.StartScanAsync(AutoIdTestData.Settings(dataAvailable: true)).ConfigureAwait(false);
            await sink.WaitForCompletionAsync().ConfigureAwait(false);
            AutoIdOperationStatusEnumeration restart = await m_reader.StartScanAsync(
                AutoIdTestData.Settings(cycles: 1)).ConfigureAwait(false);
            await sink.WaitForCompletionAsync(2).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(sink.Scans, Has.Count.EqualTo(2));
                Assert.That(restart, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS), "the provider is free again");
            });
        }

        [Test]
        public async Task AsynchronousScanWithoutASinkDoesNotStartAndDetachingStopsItAsync()
        {
            AutoIdOperationStatusEnumeration unattached = await m_reader!.StartScanAsync(AutoIdTestData.Settings())
                .ConfigureAwait(false);
            var sink = new RecordingSink();
            await m_reader.AttachAsync(sink).ConfigureAwait(false);
            await m_reader.StartScanAsync(AutoIdTestData.Settings()).ConfigureAwait(false);

            await m_reader.DetachAsync().ConfigureAwait(false);
            int afterDetach = sink.Scans.Count;
            await Task.Delay(30).ConfigureAwait(false);

            await Assert.MultipleAsync(async () =>
            {
                Assert.That(unattached, Is.EqualTo(AutoIdOperationStatusEnumeration.DEVICE_NOT_READY));
                Assert.That(sink.Scans, Has.Count.EqualTo(afterDetach));
                await Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await m_reader.AttachAsync(null!).ConfigureAwait(false));
            });
        }

        private static ScanData Identify(ByteString epc)
        {
            return AutoIdTestData.Epc(epc);
        }

        private InMemoryRfidReaderProvider? m_reader;
    }

    /// <summary>
    /// Records what a provider reports.
    /// </summary>
    internal sealed class RecordingSink : IAutoIdReaderSink
    {
        public List<ArrayOf<ScanResult>> Scans { get; } = [];

        public List<ushort> Presence { get; } = [];

        public int Completions { get; private set; }

        public ValueTask ReportScanAsync(ArrayOf<ScanResult> results, CancellationToken cancellationToken = default)
        {
            lock (Scans)
            {
                Scans.Add(results);
            }
            return default;
        }

        public ValueTask CompleteScanAsync(CancellationToken cancellationToken = default)
        {
            Completions++;
            return default;
        }

        public ValueTask ReportPresenceAsync(ushort presence, CancellationToken cancellationToken = default)
        {
            Presence.Add(presence);
            return default;
        }

        public ValueTask ReportLogEntryAsync(string entry, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask ReportAccessAsync(
            string command, string client, ArrayOf<AccessResult> results, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public async Task WaitForScansAsync(int count)
        {
            for (int ii = 0; ii < 500; ii++)
            {
                lock (Scans)
                {
                    if (Scans.Count >= count)
                    {
                        return;
                    }
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.Fail("The provider did not report " + count + " scans.");
        }

        public async Task WaitForCompletionAsync(int count = 1)
        {
            for (int ii = 0; ii < 500; ii++)
            {
                if (Completions >= count)
                {
                    return;
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.Fail("The provider did not complete the scan.");
        }
    }
}
