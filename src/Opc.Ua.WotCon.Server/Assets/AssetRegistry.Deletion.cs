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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Server.Assets
{
    internal sealed partial class AssetRegistry
    {
        internal async ValueTask RestorePendingDeletionsAsync(CancellationToken cancellationToken)
        {
            string? folder = m_options.ThingDescriptionStorageFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ||
                m_options.MaxPersistedThingDescriptionFiles <= 0)
            {
                return;
            }
            int count = 0;
            foreach (string path in Directory.EnumerateFiles(folder, "*.jsonld" + DeletionIntentSuffix))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++count > m_options.MaxPersistedThingDescriptionFiles)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "Pending legacy deletions exceed the configured persisted asset limit.");
                }
                string fileName = Path.GetFileName(path);
                string name = Path.GetFileNameWithoutExtension(fileName[..^DeletionIntentSuffix.Length]);
                if (ServiceResult.IsBad(WotAssetNameValidator.Validate(name)))
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "A pending legacy deletion has an invalid asset name.");
                }
                IWotRegistryService registry = m_options.RegistryBridge ??
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "Pending legacy deletions require their configured backing registry.");
                AssetRegistryDeletionIntent intent;
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                        4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (stream.Length > MaximumDeletionIntentBytes)
                    {
                        throw new ServiceResultException(StatusCodes.BadConfigurationError,
                            "A pending legacy deletion exceeds the recovery record size limit.");
                    }
                    intent = await JsonSerializer.DeserializeAsync(
                        stream, ThingDescriptionJsonContext.Default.AssetRegistryDeletionIntent, cancellationToken)
                        .ConfigureAwait(false) ?? throw new JsonException("The pending deletion record is empty.");
                    if (string.IsNullOrEmpty(intent.GroupId) || string.IsNullOrEmpty(intent.ResourceId) ||
                        intent.Generation < 0)
                    {
                        throw new JsonException("The pending deletion record has invalid identity or generation fields.");
                    }
                }
                catch (JsonException exception)
                {
                    m_logger.RegistryBridgeDeleteFailed(exception, name);
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "A pending legacy deletion has an invalid recovery record.");
                }
                (ServiceResult status, NodeId assetId) = await CreateAssetAsync(name, cancellationToken)
                    .ConfigureAwait(false);
                if (ServiceResult.IsBad(status))
                {
                    throw new ServiceResultException(status);
                }
                AssetEntry entry = FindByNodeId(assetId)!;
                entry.RegistryMirror = new AssetRegistryMirror(
                    registry, intent.GroupId, intent.ResourceId, intent.Generation);
                entry.RegistryDeleteRequiresRecovery = true;
                m_logger.RestoredPendingAssetDeletion(name);
            }
        }

        private ValueTask PersistDeletionIntentAsync(AssetEntry entry, CancellationToken cancellationToken)
        {
            if (entry.RegistryMirror is not { } mirror)
            {
                return default;
            }
            var intent = new AssetRegistryDeletionIntent(mirror.GroupId, mirror.ResourceId, mirror.Generation);
            ByteString content = ByteString.From(JsonSerializer.SerializeToUtf8Bytes(
                intent, ThingDescriptionJsonContext.Default.AssetRegistryDeletionIntent));
            if (content.Length > MaximumDeletionIntentBytes)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The backing Resource identity exceeds the legacy deletion recovery record size limit.");
            }
            return PersistTdToDiskAsync(entry.Name, content, cancellationToken, DeletionIntentSuffix);
        }

        private const string DeletionIntentSuffix = ".delete-pending";
        private const int MaximumDeletionIntentBytes = 16384;
    }

    internal sealed record AssetRegistryDeletionIntent(string GroupId, string ResourceId, long Generation);
}
