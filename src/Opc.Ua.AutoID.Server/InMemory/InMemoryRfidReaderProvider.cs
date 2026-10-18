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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// A simulated EPC Gen2 RFID reader with tags in its range.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It supports every RFID method: <c>Scan</c>, <c>ScanStart</c> and the six tag access methods,
    /// with the status codes of OPC 30010 Table 33. Read, write, lock, kill and password handling
    /// follow EPC Class 1 Gen2 in the small: the access password protects locked memory, a kill
    /// needs a non-zero kill password, permanent locks cannot be undone and the tag identifier
    /// bank is read-only. Tags are addressed by their EPC with the code types <c>EPC</c>,
    /// <c>UID</c>, <c>RAW:BYTES</c> and <c>RAW:STRING</c> (hexadecimal text).
    /// </para>
    /// <para>
    /// All members are thread safe.
    /// </para>
    /// </remarks>
    public sealed class InMemoryRfidReaderProvider : IAutoIdScanProvider, IAutoIdAsyncScanProvider, IAutoIdRfidProvider, IDisposable
    {
        /// <summary>
        /// Creates a reader without tags.
        /// </summary>
        /// <param name="cycleInterval">
        /// The duration of one scan cycle, which paces <c>Scan</c> and the scan events of
        /// <c>ScanStart</c>; defaults to one second.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">The cycle interval is not positive.</exception>
        public InMemoryRfidReaderProvider(TimeSpan? cycleInterval = null)
        {
            m_engine = new InMemoryScanEngine(Snapshot, cycleInterval);
        }

        /// <inheritdoc/>
        public AutoIdReaderKind Kind => AutoIdReaderKind.Rfid;

        /// <summary>
        /// Gets the duration of one scan cycle.
        /// </summary>
        public TimeSpan CycleInterval => m_engine.CycleInterval;

        /// <inheritdoc/>
        public AutoIdRfidOperations SupportedOperations => AutoIdRfidOperations.All;

        /// <summary>
        /// Gets the number of tags in range.
        /// </summary>
        public int TagCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_tags.Count;
                }
            }
        }

        /// <summary>
        /// Brings a tag into range and reports the changed presence.
        /// </summary>
        /// <param name="tag">The tag.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <exception cref="InvalidOperationException">A tag with this EPC is already in range.</exception>
        /// <exception cref="ArgumentNullException"></exception>
        public async ValueTask PlaceTagAsync(InMemoryRfidTag tag, CancellationToken cancellationToken = default)
        {
            if (tag is null)
            {
                throw new ArgumentNullException(nameof(tag));
            }
            int count;
            lock (m_lock)
            {
                if (FindLocked(tag.Epc) != null)
                {
                    throw new InvalidOperationException("A tag with this EPC is already in range.");
                }
                m_tags.Add(new TagState(tag));
                count = m_tags.Count;
            }
            await ReportPresenceAsync(count, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Takes a tag out of range and reports the changed presence.
        /// </summary>
        /// <param name="epc">The EPC of the tag.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns><see langword="true"/> when the tag was in range.</returns>
        public async ValueTask<bool> RemoveTagAsync(ByteString epc, CancellationToken cancellationToken = default)
        {
            int count;
            lock (m_lock)
            {
                TagState? tag = FindLocked(epc);
                if (tag == null)
                {
                    return false;
                }
                m_tags.Remove(tag);
                count = m_tags.Count;
            }
            await ReportPresenceAsync(count, cancellationToken).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Scans on the device's own initiative: reports the tags in range as a scan event without a
        /// client having called a scan method (OPC 30010 6.1.1 option 3).
        /// </summary>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns><see langword="false"/> when no tag is in range or the provider is not attached.</returns>
        public async ValueTask<bool> TriggerScanAsync(CancellationToken cancellationToken = default)
        {
            IAutoIdReaderSink? sink = m_engine.Sink;
            ArrayOf<ScanResult> found = Snapshot();
            if (sink is null || found.Count == 0)
            {
                return false;
            }
            await sink.ReportScanAsync(found, cancellationToken).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Gets the bytes of a bank of a tag in range, for tests and diagnostics.
        /// </summary>
        /// <param name="epc">The EPC of the tag.</param>
        /// <param name="bank">The bank, 0 to 3.</param>
        /// <returns>The bank contents, or an empty value when the tag or bank does not exist.</returns>
        public ByteString GetBank(ByteString epc, int bank)
        {
            lock (m_lock)
            {
                TagState? tag = FindLocked(epc);
                return tag == null || bank is < 0 or > 3 ? ByteString.Empty : ByteString.From(tag.Banks[bank]);
            }
        }

        /// <inheritdoc/>
        public ValueTask AttachAsync(IAutoIdReaderSink sink, CancellationToken cancellationToken = default)
        {
            m_engine.Attach(sink ?? throw new ArgumentNullException(nameof(sink)));
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DetachAsync(CancellationToken cancellationToken = default)
        {
            return m_engine.DetachAsync();
        }

        /// <inheritdoc/>
        public ValueTask<AutoIdScanOutcome> ScanAsync(
            ScanSettings settings,
            CancellationToken cancellationToken = default)
        {
            return m_engine.ScanAsync(settings, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<AutoIdOperationStatusEnumeration> StartScanAsync(
            ScanSettings settings,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<AutoIdOperationStatusEnumeration>(m_engine.Start(settings));
        }

        /// <inheritdoc/>
        public ValueTask StopScanAsync(CancellationToken cancellationToken = default)
        {
            return m_engine.StopAsync();
        }

        /// <inheritdoc/>
        public ValueTask<AutoIdTagReadOutcome> ReadTagAsync(
            ScanData identifier,
            string codeType,
            ushort region,
            uint offset,
            uint length,
            ByteString password,
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                AutoIdOperationStatusEnumeration status = Resolve(identifier, codeType, out TagState? tag);
                if (tag == null)
                {
                    return Read(ByteString.Empty, status);
                }
                if (region > 3)
                {
                    return Read(ByteString.Empty, AutoIdOperationStatusEnumeration.REGION_NOT_FOUND_ERROR);
                }
                byte[] bank = tag.Banks[region];
                if (offset > bank.Length)
                {
                    return Read(ByteString.Empty, AutoIdOperationStatusEnumeration.OUT_OF_RANGE_ERROR);
                }
                long count = length == 0 ? bank.Length - offset : length;
                if (offset + count > bank.Length)
                {
                    return Read(ByteString.Empty, AutoIdOperationStatusEnumeration.OUT_OF_RANGE_ERROR);
                }
                if (region == 0 && (tag.IsLocked(LockArea.Kill) || tag.IsLocked(LockArea.Access)))
                {
                    status = CheckPassword(tag, password);
                    if (status != AutoIdOperationStatusEnumeration.SUCCESS)
                    {
                        return Read(ByteString.Empty, status);
                    }
                }
                byte[] data = new byte[count];
                Array.Copy(bank, offset, data, 0, count);
                return Read(ByteString.From(data), AutoIdOperationStatusEnumeration.SUCCESS);
            }
        }

        /// <inheritdoc/>
        public ValueTask<AutoIdOperationStatusEnumeration> WriteTagAsync(
            ScanData identifier,
            string codeType,
            ushort region,
            uint offset,
            ByteString data,
            ByteString password,
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                AutoIdOperationStatusEnumeration status = Resolve(identifier, codeType, out TagState? tag);
                if (tag == null)
                {
                    return Status(status);
                }
                if (region > 3)
                {
                    return Status(AutoIdOperationStatusEnumeration.REGION_NOT_FOUND_ERROR);
                }
                if (region == 2)
                {
                    return Status(AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR);
                }
                byte[] bank = tag.Banks[region];
                // The CRC (bytes 0 and 1 of the EPC bank) is computed by the tag, not written.
                uint first = region == 1 ? 2u : 0u;
                if (offset < first || offset + data.Length > bank.Length)
                {
                    return Status(AutoIdOperationStatusEnumeration.OUT_OF_RANGE_ERROR);
                }
                status = CheckWriteAccess(tag, region, password);
                if (status != AutoIdOperationStatusEnumeration.SUCCESS)
                {
                    return Status(status);
                }
                data.Span.CopyTo(bank.AsSpan((int)offset));
                if (region == 0)
                {
                    TagState.SyncPasswordsFromBank();
                }
                else if (region == 1)
                {
                    tag.SyncEpcFromBank();
                }
                return Status(AutoIdOperationStatusEnumeration.SUCCESS);
            }
        }

        /// <inheritdoc/>
        public ValueTask<AutoIdOperationStatusEnumeration> WriteTagIdAsync(
            ScanData identifier,
            string codeType,
            ByteString newUId,
            byte afi,
            bool toggle,
            ByteString password,
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                AutoIdOperationStatusEnumeration status = Resolve(identifier, codeType, out TagState? tag);
                if (tag == null)
                {
                    return Status(status);
                }
                if ((newUId.Length & 1) != 0)
                {
                    // OPC 30010 6.5.3.9: the byte length shall be even.
                    return Status(AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR);
                }
                if (newUId.Length is < 2 or > 62)
                {
                    return Status(AutoIdOperationStatusEnumeration.OUT_OF_RANGE_ERROR);
                }
                status = CheckWriteAccess(tag, 1, password);
                if (status != AutoIdOperationStatusEnumeration.SUCCESS)
                {
                    return Status(status);
                }
                if (FindLocked(newUId) is { } other && !ReferenceEquals(other, tag))
                {
                    return Status(AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR);
                }
                tag.SetEpc(newUId, afi, toggle);
                return Status(AutoIdOperationStatusEnumeration.SUCCESS);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<AutoIdOperationStatusEnumeration> KillTagAsync(
            ScanData identifier,
            string codeType,
            ByteString killPassword,
            CancellationToken cancellationToken = default)
        {
            int count;
            lock (m_lock)
            {
                AutoIdOperationStatusEnumeration status = Resolve(identifier, codeType, out TagState? tag);
                if (tag == null)
                {
                    return status;
                }
                if (IsZero(tag.Banks[0].AsSpan(0, 4)))
                {
                    // A tag with a zero kill password ignores the kill command.
                    return AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR;
                }
                if (killPassword.Length == 0)
                {
                    return AutoIdOperationStatusEnumeration.PERMISSON_ERROR;
                }
                if (!killPassword.Span.SequenceEqual(tag.Banks[0].AsSpan(0, 4)))
                {
                    return AutoIdOperationStatusEnumeration.PASSWORD_ERROR;
                }
                m_tags.Remove(tag);
                count = m_tags.Count;
            }
            await ReportPresenceAsync(count, cancellationToken).ConfigureAwait(false);
            return AutoIdOperationStatusEnumeration.SUCCESS;
        }

        /// <inheritdoc/>
        public ValueTask<AutoIdOperationStatusEnumeration> LockTagAsync(
            ScanData identifier,
            string codeType,
            ByteString password,
            RfidLockRegionEnumeration region,
            RfidLockOperationEnumeration lockOperation,
            uint offset,
            uint length,
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                AutoIdOperationStatusEnumeration status = Resolve(identifier, codeType, out TagState? tag);
                if (tag == null)
                {
                    return Status(status);
                }
                if (!IsZero(tag.Banks[0].AsSpan(4, 4)))
                {
                    status = CheckPassword(tag, password);
                    if (status != AutoIdOperationStatusEnumeration.SUCCESS)
                    {
                        return Status(status);
                    }
                }
                var area = (LockArea)(int)region;
                LockState current = tag.Locks[(int)area];
                LockState next;
                switch (lockOperation)
                {
                    case RfidLockOperationEnumeration.Lock when current == LockState.Unlocked:
                        next = LockState.Locked;
                        break;
                    case RfidLockOperationEnumeration.Lock when current == LockState.Locked:
                        next = LockState.Locked;
                        break;
                    case RfidLockOperationEnumeration.Unlock when current is LockState.Locked or LockState.Unlocked:
                        next = LockState.Unlocked;
                        break;
                    case RfidLockOperationEnumeration.PermanentLock when current is LockState.Unlocked or LockState.Locked:
                        next = LockState.PermanentlyLocked;
                        break;
                    case RfidLockOperationEnumeration.PermanentUnlock when current is LockState.Unlocked or LockState.Locked:
                        next = LockState.PermanentlyUnlocked;
                        break;
                    default:
                        return Status(AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR);
                }
                tag.Locks[(int)area] = next;
                return Status(AutoIdOperationStatusEnumeration.SUCCESS);
            }
        }

        /// <inheritdoc/>
        public ValueTask<AutoIdOperationStatusEnumeration> SetTagPasswordAsync(
            ScanData identifier,
            string codeType,
            RfidPasswordTypeEnumeration passwordType,
            ByteString accessPassword,
            ByteString newPassword,
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                AutoIdOperationStatusEnumeration status = Resolve(identifier, codeType, out TagState? tag);
                if (tag == null)
                {
                    return Status(status);
                }
                int start;
                LockArea area;
                switch (passwordType)
                {
                    case RfidPasswordTypeEnumeration.Access:
                        start = 4;
                        area = LockArea.Access;
                        break;
                    case RfidPasswordTypeEnumeration.Kill:
                        start = 0;
                        area = LockArea.Kill;
                        break;
                    default:
                        // Gen2 tags have no read or write password.
                        return Status(AutoIdOperationStatusEnumeration.NOT_SUPPORTED_BY_TAG);
                }
                if (newPassword.Length != 4)
                {
                    return Status(AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR);
                }
                LockState state = tag.Locks[(int)area];
                if (state == LockState.PermanentlyLocked)
                {
                    return Status(AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR);
                }
                if (state == LockState.Locked)
                {
                    status = CheckPassword(tag, accessPassword);
                    if (status != AutoIdOperationStatusEnumeration.SUCCESS)
                    {
                        return Status(status);
                    }
                }
                newPassword.Span.CopyTo(tag.Banks[0].AsSpan(start, 4));
                TagState.SyncPasswordsFromBank();
                return Status(AutoIdOperationStatusEnumeration.SUCCESS);
            }
        }

        private ValueTask ReportPresenceAsync(int count, CancellationToken cancellationToken)
        {
            IAutoIdReaderSink? sink = m_engine.Sink;
            return (sink?.ReportPresenceAsync((ushort)Math.Min(count, ushort.MaxValue), cancellationToken)) ?? default;
        }

        /// <summary>
        /// Stops a running scan loop. Detaching the provider stops it in an orderly way; disposing
        /// is the fallback for a provider that was never attached to a reader.
        /// </summary>
        public void Dispose()
        {
            m_engine.Dispose();
        }

        private ArrayOf<ScanResult> Snapshot()
        {
            lock (m_lock)
            {
                var results = new ScanResult[m_tags.Count];
                DateTimeUtc now = DateTimeUtc.Now;
                for (int index = 0; index < results.Length; index++)
                {
                    TagState tag = m_tags[index];
                    results[index] = new RfidScanResult
                    {
                        CodeType = "EPC",
                        ScanData = new ScanData
                        {
                            SwitchField = ScanDataFields.Epc,
                            Epc = new ScanDataEpc { PC = tag.ProtocolControl, UId = tag.Epc }
                        },
                        Timestamp = now,
                        Sighting =
                        [
                            new RfidSighting
                            {
                                Antenna = tag.Antenna,
                                Strength = tag.Strength,
                                Timestamp = now,
                                CurrentPowerLevel = 0
                            }
                        ]
                    };
                }
                return results.ToArrayOf();
            }
        }

        private TagState? FindLocked(ByteString epc)
        {
            foreach (TagState tag in m_tags)
            {
                if (tag.Epc.Span.SequenceEqual(epc.Span))
                {
                    return tag;
                }
            }
            return null;
        }

        /// <summary>
        /// Maps an identifier to a tag. Caller holds the lock.
        /// </summary>
        private AutoIdOperationStatusEnumeration Resolve(ScanData identifier, string codeType, out TagState? tag)
        {
            tag = null;
            ByteString epc;
            switch (codeType)
            {
                case "EPC" when identifier.SwitchField == ScanDataFields.Epc:
                    epc = identifier.Epc.UId;
                    break;
                case "UID" or "EPC" or "RAW:BYTES" when identifier.SwitchField == ScanDataFields.ByteString:
                    epc = identifier.ByteString;
                    break;
                case "RAW:STRING" when identifier.SwitchField == ScanDataFields.String:
                    try
                    {
                        epc = ByteString.From(AutoIdCompat.FromHexString(identifier.String ?? string.Empty));
                    }
                    catch (FormatException)
                    {
                        return AutoIdOperationStatusEnumeration.NO_IDENTIFIER;
                    }
                    break;
                default:
                    return AutoIdOperationStatusEnumeration.NOT_SUPPORTED_BY_DEVICE;
            }
            tag = FindLocked(epc);
            return tag == null
                ? AutoIdOperationStatusEnumeration.NO_IDENTIFIER
                : AutoIdOperationStatusEnumeration.SUCCESS;
        }

        private static AutoIdOperationStatusEnumeration CheckPassword(TagState tag, ByteString password)
        {
            if (IsZero(tag.Banks[0].AsSpan(4, 4)))
            {
                // A tag without access password cannot grant access to protected memory.
                return AutoIdOperationStatusEnumeration.PERMISSON_ERROR;
            }
            if (password.Length == 0)
            {
                return AutoIdOperationStatusEnumeration.PERMISSON_ERROR;
            }
            return password.Span.SequenceEqual(tag.Banks[0].AsSpan(4, 4))
                ? AutoIdOperationStatusEnumeration.SUCCESS
                : AutoIdOperationStatusEnumeration.PASSWORD_ERROR;
        }

        private static AutoIdOperationStatusEnumeration CheckWriteAccess(TagState tag, int region, ByteString password)
        {
            LockState state;
            if (region == 0)
            {
                state = tag.Locks[(int)LockArea.Kill] is LockState.Locked or LockState.PermanentlyLocked
                    ? tag.Locks[(int)LockArea.Kill]
                    : tag.Locks[(int)LockArea.Access];
            }
            else
            {
                state = tag.Locks[region + 1];
            }
            return state switch
            {
                LockState.PermanentlyLocked => AutoIdOperationStatusEnumeration.OP_NOT_POSSIBLE_ERROR,
                LockState.Locked => CheckPassword(tag, password),
                _ => AutoIdOperationStatusEnumeration.SUCCESS
            };
        }

        private static bool IsZero(ReadOnlySpan<byte> bytes)
        {
            foreach (byte value in bytes)
            {
                if (value != 0)
                {
                    return false;
                }
            }
            return true;
        }

        private static ValueTask<AutoIdTagReadOutcome> Read(ByteString data, AutoIdOperationStatusEnumeration status)
        {
            return new ValueTask<AutoIdTagReadOutcome>(new AutoIdTagReadOutcome(data, status));
        }

        private static ValueTask<AutoIdOperationStatusEnumeration> Status(AutoIdOperationStatusEnumeration status)
        {
            return new ValueTask<AutoIdOperationStatusEnumeration>(status);
        }

        /// <summary>
        /// The lockable areas, numbered like <see cref="RfidLockRegionEnumeration"/>.
        /// </summary>
        private enum LockArea
        {
            Kill = 0,
            Access = 1,
            Epc = 2,
            Tid = 3,
            User = 4
        }

        /// <summary>
        /// The lock state of an area.
        /// </summary>
        private enum LockState
        {
            Unlocked,
            Locked,
            PermanentlyLocked,
            PermanentlyUnlocked
        }

        /// <summary>
        /// A tag in range with its memory.
        /// </summary>
        private sealed class TagState
        {
            public TagState(InMemoryRfidTag tag)
            {
                Antenna = tag.Antenna;
                Strength = tag.Strength;
                Banks =
                [
                    new byte[InMemoryRfidTag.ReservedBankSize],
                    new byte[4 + tag.Epc.Length],
                    new byte[InMemoryRfidTag.TidBankSize],
                    new byte[Math.Max(tag.UserMemorySize, 0)]
                ];
                tag.KillPassword.Span.CopyTo(Banks[0].AsSpan(0, Math.Min(4, tag.KillPassword.Length)));
                tag.AccessPassword.Span.CopyTo(Banks[0].AsSpan(4, Math.Min(4, tag.AccessPassword.Length)));
                // A fixed tag identifier: Gen2 class identifier E2 followed by the first EPC bytes.
                Banks[2][0] = 0xE2;
                Banks[2][1] = 0x80;
                tag.Epc.Span[..Math.Min(10, tag.Epc.Length)].CopyTo(Banks[2].AsSpan(2));
                SetEpc(tag.Epc, 0, false);
            }

            public ByteString Epc { get; private set; }

            public ushort ProtocolControl { get; private set; }

            public int Antenna { get; }

            public int Strength { get; }

            public byte[][] Banks { get; }

            public LockState[] Locks { get; } = new LockState[5];

            public bool IsLocked(LockArea area)
            {
                return Locks[(int)area] is LockState.Locked or LockState.PermanentlyLocked;
            }

            public void SetEpc(ByteString epc, byte afi, bool toggle)
            {
                byte[] bank = Banks[1];
                if (bank.Length != 4 + epc.Length)
                {
                    bank = new byte[4 + epc.Length];
                    Banks[1] = bank;
                }
                epc.Span.CopyTo(bank.AsSpan(4));
                ProtocolControl = (ushort)(((epc.Length / 2) << 11) | (toggle ? 0x0100 : 0) | afi);
                bank[2] = (byte)(ProtocolControl >> 8);
                bank[3] = (byte)ProtocolControl;
                Epc = epc;
            }

            public void SyncEpcFromBank()
            {
                // The EPC bytes may have been rewritten in place; the length is fixed by the bank.
                byte[] bank = Banks[1];
                Epc = ByteString.From(bank.AsSpan(4).ToArray());
                ProtocolControl = (ushort)((bank[2] << 8) | bank[3]);
            }

            public static void SyncPasswordsFromBank()
            {
                // The passwords are read from the reserved bank on demand.
            }
        }

        private readonly Lock m_lock = new();
        private readonly List<TagState> m_tags = [];
        private readonly InMemoryScanEngine m_engine;
    }
}
