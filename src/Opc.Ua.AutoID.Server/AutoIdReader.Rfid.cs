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
    public sealed partial class AutoIdReader
    {
        private const string RawBytes = "RAW:BYTES";

        private async ValueTask<ReadTagMethodStateResult> ReadTagCallAsync(
            ISystemContext context,
            ScanData identifier,
            string codeType,
            ushort region,
            uint offset,
            uint length,
            ByteString password,
            CancellationToken cancellationToken)
        {
            IAutoIdRfidProvider provider = RfidProviderFor(AutoIdRfidOperations.ReadTag);
            ServiceResult invalid = ValidateIdentifier(identifier, codeType);
            if (ServiceResult.IsGood(invalid) && length > Runtime.Options.MaximumTagReadLength)
            {
                invalid = BadResult(StatusCodes.BadInvalidArgument, "The read length exceeds the maximum tag read length of the server.");
            }
            if (ServiceResult.IsBad(invalid))
            {
                return new ReadTagMethodStateResult { ServiceResult = invalid };
            }
            AutoIdTagReadOutcome outcome = await RunOperationAsync(
                token => provider.ReadTagAsync(identifier, codeType, region, offset, length, password, token),
                cancellationToken).ConfigureAwait(false);
            RaiseTagAccess(context, "ReadTag", identifier, codeType, outcome.Status, outcome.Data);
            return new ReadTagMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                ResultData = outcome.Data,
                Status = outcome.Status
            };
        }

        private async ValueTask<WriteTagMethodStateResult> WriteTagCallAsync(
            ISystemContext context,
            ScanData identifier,
            string codeType,
            ushort region,
            uint offset,
            ByteString data,
            ByteString password,
            CancellationToken cancellationToken)
        {
            IAutoIdRfidProvider provider = RfidProviderFor(AutoIdRfidOperations.WriteTag);
            ServiceResult invalid = ValidateIdentifier(identifier, codeType);
            if (ServiceResult.IsBad(invalid))
            {
                return new WriteTagMethodStateResult { ServiceResult = invalid };
            }
            AutoIdOperationStatusEnumeration status = await RunOperationAsync(
                token => provider.WriteTagAsync(identifier, codeType, region, offset, data, password, token),
                cancellationToken).ConfigureAwait(false);
            RaiseTagAccess(context, "WriteTag", identifier, codeType, status, data);
            return new WriteTagMethodStateResult { ServiceResult = ServiceResult.Good, Status = status };
        }

        private async ValueTask<WriteTagIDMethodStateResult> WriteTagIdCallAsync(
            ISystemContext context,
            ScanData identifier,
            string codeType,
            ByteString newUId,
            byte afi,
            bool toggle,
            ByteString password,
            CancellationToken cancellationToken)
        {
            IAutoIdRfidProvider provider = RfidProviderFor(AutoIdRfidOperations.WriteTagId);
            ServiceResult invalid = ValidateIdentifier(identifier, codeType);
            if (ServiceResult.IsBad(invalid))
            {
                return new WriteTagIDMethodStateResult { ServiceResult = invalid };
            }
            AutoIdOperationStatusEnumeration status = await RunOperationAsync(
                token => provider.WriteTagIdAsync(identifier, codeType, newUId, afi, toggle, password, token),
                cancellationToken).ConfigureAwait(false);
            RaiseTagAccess(context, "WriteTagID", identifier, codeType, status, newUId);
            return new WriteTagIDMethodStateResult { ServiceResult = ServiceResult.Good, Status = status };
        }

        private async ValueTask<KillTagMethodStateResult> KillTagCallAsync(
            ISystemContext context,
            ScanData identifier,
            string codeType,
            ByteString killPassword,
            CancellationToken cancellationToken)
        {
            IAutoIdRfidProvider provider = RfidProviderFor(AutoIdRfidOperations.KillTag);
            ServiceResult invalid = ValidateIdentifier(identifier, codeType);
            if (ServiceResult.IsBad(invalid))
            {
                return new KillTagMethodStateResult { ServiceResult = invalid };
            }
            AutoIdOperationStatusEnumeration status = await RunOperationAsync(
                token => provider.KillTagAsync(identifier, codeType, killPassword, token),
                cancellationToken).ConfigureAwait(false);
            RaiseTagAccess(context, "KillTag", identifier, codeType, status, ByteString.Empty);
            return new KillTagMethodStateResult { ServiceResult = ServiceResult.Good, Status = status };
        }

        private async ValueTask<LockTagMethodStateResult> LockTagCallAsync(
            ISystemContext context,
            ScanData identifier,
            string codeType,
            ByteString password,
            RfidLockRegionEnumeration region,
            RfidLockOperationEnumeration lockOperation,
            uint offset,
            uint length,
            CancellationToken cancellationToken)
        {
            IAutoIdRfidProvider provider = RfidProviderFor(AutoIdRfidOperations.LockTag);
            ServiceResult invalid = ValidateIdentifier(identifier, codeType);
            if (ServiceResult.IsGood(invalid) &&
                (!AutoIdCompat.IsDefined(region) ||
                    !AutoIdCompat.IsDefined(lockOperation)))
            {
                invalid = BadResult(StatusCodes.BadInvalidArgument, "The lock region or lock operation is not defined.");
            }
            if (ServiceResult.IsBad(invalid))
            {
                return new LockTagMethodStateResult { ServiceResult = invalid };
            }
            AutoIdOperationStatusEnumeration status = await RunOperationAsync(
                token => provider.LockTagAsync(
                    identifier, codeType, password, region, lockOperation, offset, length, token),
                cancellationToken).ConfigureAwait(false);
            RaiseTagAccess(context, "LockTag", identifier, codeType, status, ByteString.Empty);
            return new LockTagMethodStateResult { ServiceResult = ServiceResult.Good, Status = status };
        }

        private async ValueTask<SetTagPasswordMethodStateResult> SetTagPasswordCallAsync(
            ISystemContext context,
            ScanData identifier,
            string codeType,
            RfidPasswordTypeEnumeration passwordType,
            ByteString accessPassword,
            ByteString newPassword,
            CancellationToken cancellationToken)
        {
            IAutoIdRfidProvider provider = RfidProviderFor(AutoIdRfidOperations.SetTagPassword);
            ServiceResult invalid = ValidateIdentifier(identifier, codeType);
            if (ServiceResult.IsGood(invalid) && !AutoIdCompat.IsDefined(passwordType))
            {
                invalid = BadResult(StatusCodes.BadInvalidArgument, "The password type is not defined.");
            }
            if (ServiceResult.IsBad(invalid))
            {
                return new SetTagPasswordMethodStateResult { ServiceResult = invalid };
            }
            AutoIdOperationStatusEnumeration status = await RunOperationAsync(
                token => provider.SetTagPasswordAsync(
                    identifier, codeType, passwordType, accessPassword, newPassword, token),
                cancellationToken).ConfigureAwait(false);
            RaiseTagAccess(context, "SetTagPassword", identifier, codeType, status, ByteString.Empty);
            return new SetTagPasswordMethodStateResult { ServiceResult = ServiceResult.Good, Status = status };
        }

        /// <summary>
        /// Gets the RFID provider, checking that it supports the operation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private IAutoIdRfidProvider RfidProviderFor(AutoIdRfidOperations operation)
        {
            if (m_rfidProvider == null || (m_rfidProvider.SupportedOperations & operation) != operation)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadMethodInvalid,
                    "The device does not support the {0} method.",
                    operation);
            }
            return m_rfidProvider;
        }

        /// <summary>
        /// Runs a provider operation under the caller's token and the shutdown token, admitted by the
        /// operation tracker so shutdown waits for it.
        /// </summary>
        /// <typeparam name="T">The result type of the operation.</typeparam>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<T> RunOperationAsync<T>(
            Func<CancellationToken, ValueTask<T>> operation,
            CancellationToken cancellationToken)
        {
            if (!m_operations.TryEnter())
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidState, "The reader is shut down.");
            }
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    m_stopping.Token);
                try
                {
                    return await operation(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                    m_stopping.IsCancellationRequested)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadServerHalted,
                        "The reader was shut down during the operation.");
                }
            }
            finally
            {
                m_operations.Exit();
            }
        }

        /// <summary>
        /// Checks the identifier and code type every tag and location method takes.
        /// </summary>
        private static ServiceResult ValidateIdentifier(ScanData? identifier, string? codeType)
        {
            if (string.IsNullOrEmpty(codeType))
            {
                return BadResult(StatusCodes.BadInvalidArgument, "The code type is required.");
            }
            bool hasData = identifier switch
            {
                null => false,
                { SwitchField: ScanDataFields.ByteString } => identifier.ByteString.Length > 0,
                { SwitchField: ScanDataFields.String } => !string.IsNullOrEmpty(identifier.String),
                { SwitchField: ScanDataFields.Epc } => identifier.Epc.UId.Length > 0,
                { SwitchField: ScanDataFields.Custom } => !identifier.Custom.IsNull,
                _ => false
            };
            return hasData
                ? ServiceResult.Good
                : BadResult(StatusCodes.BadInvalidArgument, "The identifier carries no data.");
        }

        /// <summary>
        /// Raises an access event for a tag command when the command reached a tag.
        /// </summary>
        private void RaiseTagAccess(
            ISystemContext context,
            string command,
            ScanData identifier,
            string codeType,
            AutoIdOperationStatusEnumeration status,
            ByteString data)
        {
            if (!TagWasAccessed(status))
            {
                return;
            }
            var access = new RfidAccessResult
            {
                EncodingMask = (uint)(RfidAccessResultFields.CodeType |
                    RfidAccessResultFields.Identifier |
                    RfidAccessResultFields.Timestamp),
                CodeType = codeType,
                Identifier = identifier,
                Timestamp = DateTimeUtc.Now
            };
            if (data.Length > 0)
            {
                access.CodeTypeRWData = RawBytes;
                access.RWData = new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = data };
                access.EncodingMask |= (uint)(RfidAccessResultFields.CodeTypeRWData | RfidAccessResultFields.RWData);
            }
            TryRaiseAccessEvent(ClientOf(context), command, new AccessResult[] { access }.ToArrayOf());
        }

        /// <summary>
        /// Gets whether a command with <paramref name="status"/> reached a tag: the statuses that say
        /// no tag, the device or the command itself was unavailable do not count as an access.
        /// </summary>
        internal static bool TagWasAccessed(AutoIdOperationStatusEnumeration status)
        {
            return status is not (
                AutoIdOperationStatusEnumeration.NO_IDENTIFIER or
                AutoIdOperationStatusEnumeration.MULTIPLE_IDENTIFIERS or
                AutoIdOperationStatusEnumeration.NOT_SUPPORTED_BY_DEVICE or
                AutoIdOperationStatusEnumeration.DEVICE_NOT_READY or
                AutoIdOperationStatusEnumeration.INVALID_CONFIGURATION or
                AutoIdOperationStatusEnumeration.DEVICE_FAULT);
        }
    }
}
