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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// The RFID tag access methods of <c>RfidReaderDeviceType</c> (OPC 30010 6.5.3).
    /// </summary>
    [Flags]
    public enum AutoIdRfidOperations
    {
        /// <summary>No tag access method.</summary>
        None = 0,

        /// <summary>The <c>ReadTag</c> method.</summary>
        ReadTag = 1,

        /// <summary>The <c>WriteTag</c> method.</summary>
        WriteTag = 2,

        /// <summary>The <c>WriteTagID</c> method.</summary>
        WriteTagId = 4,

        /// <summary>The <c>KillTag</c> method.</summary>
        KillTag = 8,

        /// <summary>The <c>LockTag</c> method.</summary>
        LockTag = 16,

        /// <summary>The <c>SetTagPassword</c> method.</summary>
        SetTagPassword = 32,

        /// <summary>Every tag access method.</summary>
        All = ReadTag | WriteTag | WriteTagId | KillTag | LockTag | SetTagPassword
    }

    /// <summary>
    /// The outcome of a tag read: the data and the operation status.
    /// </summary>
    public readonly struct AutoIdTagReadOutcome : IEquatable<AutoIdTagReadOutcome>
    {
        /// <summary>
        /// Creates an outcome.
        /// </summary>
        /// <param name="data">The data read; empty unless <paramref name="status"/> is <c>SUCCESS_0</c>.</param>
        /// <param name="status">The operation status.</param>
        public AutoIdTagReadOutcome(ByteString data, AutoIdOperationStatusEnumeration status)
        {
            Data = data;
            Status = status;
        }

        /// <summary>Gets the data read.</summary>
        public ByteString Data { get; }

        /// <summary>Gets the operation status.</summary>
        public AutoIdOperationStatusEnumeration Status { get; }

        /// <inheritdoc/>
        public bool Equals(AutoIdTagReadOutcome other)
        {
            return Status == other.Status && Data.Equals(other.Data);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is AutoIdTagReadOutcome other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Data, Status);
        }

        /// <summary>Compares two outcomes.</summary>
        public static bool operator ==(AutoIdTagReadOutcome left, AutoIdTagReadOutcome right)
        {
            return left.Equals(right);
        }

        /// <summary>Compares two outcomes.</summary>
        public static bool operator !=(AutoIdTagReadOutcome left, AutoIdTagReadOutcome right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// A provider for an RFID reader: it executes the tag access methods of OPC 30010 6.5.3.
    /// </summary>
    /// <remarks>
    /// <see cref="IAutoIdReaderProvider.Kind"/> must be <see cref="AutoIdReaderKind.Rfid"/>. The
    /// runtime creates exactly the methods named by <see cref="SupportedOperations"/>; the
    /// provider is called only for those. Every call returns a status of
    /// <see cref="AutoIdOperationStatusEnumeration"/> (Table 33) rather than throwing for a tag
    /// related failure: a missing tag is <c>NO_IDENTIFIER_8</c>, a wrong password is
    /// <c>PASSWORD_ERROR_4</c>, an address beyond the memory is <c>OUT_OF_RANGE_ERROR_7</c>.
    /// The runtime has verified that the identifier carries data and that the lengths are within
    /// <see cref="AutoIdServerOptions.MaximumTagReadLength"/>.
    /// </remarks>
    public interface IAutoIdRfidProvider : IAutoIdReaderProvider
    {
        /// <summary>
        /// Gets the tag access methods the hardware supports. Must not change.
        /// </summary>
        AutoIdRfidOperations SupportedOperations { get; }

        /// <summary>
        /// Reads an area of the tag memory (<c>ReadTag</c>, OPC 30010 6.5.3.7).
        /// </summary>
        /// <param name="identifier">The identifier of the tag.</param>
        /// <param name="codeType">The format of <paramref name="identifier"/> (Table 32).</param>
        /// <param name="region">The memory bank; 0 when the tag has no banks.</param>
        /// <param name="offset">The start address in bytes.</param>
        /// <param name="length">The number of bytes to read.</param>
        /// <param name="password">The read password, empty if none.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        ValueTask<AutoIdTagReadOutcome> ReadTagAsync(
            ScanData identifier,
            string codeType,
            ushort region,
            uint offset,
            uint length,
            ByteString password,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes data to the tag memory (<c>WriteTag</c>, OPC 30010 6.5.3.8).
        /// </summary>
        /// <param name="identifier">The identifier of the tag.</param>
        /// <param name="codeType">The format of <paramref name="identifier"/>.</param>
        /// <param name="region">The memory bank; 0 when the tag has no banks.</param>
        /// <param name="offset">The start address in bytes.</param>
        /// <param name="data">The data to write.</param>
        /// <param name="password">The write password, empty if none.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        ValueTask<AutoIdOperationStatusEnumeration> WriteTagAsync(
            ScanData identifier,
            string codeType,
            ushort region,
            uint offset,
            ByteString data,
            ByteString password,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Initialises the EPC and protocol control word of a tag (<c>WriteTagID</c>, OPC 30010 6.5.3.9).
        /// </summary>
        /// <param name="identifier">The identifier of the tag.</param>
        /// <param name="codeType">The format of <paramref name="identifier"/>.</param>
        /// <param name="newUId">
        /// The new identifier. Its byte length must be even, otherwise the status is
        /// <c>OP_NOT_POSSIBLE_ERROR_6</c>.
        /// </param>
        /// <param name="afi">The application family identifier.</param>
        /// <param name="toggle">The numbering system identifier toggle.</param>
        /// <param name="password">The write password, empty if none.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        ValueTask<AutoIdOperationStatusEnumeration> WriteTagIdAsync(
            ScanData identifier,
            string codeType,
            ByteString newUId,
            byte afi,
            bool toggle,
            ByteString password,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Permanently disables a tag (<c>KillTag</c>, OPC 30010 6.5.3.4).
        /// </summary>
        /// <param name="identifier">The identifier of the tag.</param>
        /// <param name="codeType">The format of <paramref name="identifier"/>.</param>
        /// <param name="killPassword">The kill password of the tag.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        ValueTask<AutoIdOperationStatusEnumeration> KillTagAsync(
            ScanData identifier,
            string codeType,
            ByteString killPassword,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Protects an area of the tag memory (<c>LockTag</c>, OPC 30010 6.5.3.5).
        /// </summary>
        /// <param name="identifier">The identifier of the tag.</param>
        /// <param name="codeType">The format of <paramref name="identifier"/>.</param>
        /// <param name="password">The access password of the tag.</param>
        /// <param name="region">The memory area to protect.</param>
        /// <param name="lockOperation">The lock action.</param>
        /// <param name="offset">The start address of the area in bytes.</param>
        /// <param name="length">The length of the area in bytes.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        ValueTask<AutoIdOperationStatusEnumeration> LockTagAsync(
            ScanData identifier,
            string codeType,
            ByteString password,
            RfidLockRegionEnumeration region,
            RfidLockOperationEnumeration lockOperation,
            uint offset,
            uint length,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Changes a password of a tag (<c>SetTagPassword</c>, OPC 30010 6.5.3.6).
        /// </summary>
        /// <param name="identifier">The identifier of the tag.</param>
        /// <param name="codeType">The format of <paramref name="identifier"/>.</param>
        /// <param name="passwordType">The password to change.</param>
        /// <param name="accessPassword">The old password.</param>
        /// <param name="newPassword">The new password.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        ValueTask<AutoIdOperationStatusEnumeration> SetTagPasswordAsync(
            ScanData identifier,
            string codeType,
            RfidPasswordTypeEnumeration passwordType,
            ByteString accessPassword,
            ByteString newPassword,
            CancellationToken cancellationToken = default);
    }
}
