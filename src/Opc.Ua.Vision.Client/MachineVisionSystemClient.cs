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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Vision.MachineVision;

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// Complete typed OPC 40100-1 interface plus atomic recipe/configuration upload
    /// and result download workflows over the stack's existing file-transfer client.
    /// </summary>
    public sealed class MachineVisionSystemClient
    {
        /// <summary>
        /// Opens a system using its instance NodeId.
        /// </summary>
        public MachineVisionSystemClient(ISession session, NodeId system, ITelemetryContext telemetry)
        {
            m_operations = new VisionClientOperations(session, telemetry);
            if (system.IsNull)
            {
                throw new ArgumentNullException(nameof(system));
            }
            Proxy = new VisionSystemTypeClient(session, system, telemetry);
        }

        /// <summary>
        /// Gets the generated system proxy, including all optional management accessors.
        /// </summary>
        public VisionSystemTypeClient Proxy { get; }

        /// <summary>
        /// Gets the required state-machine proxy, including state and transition streaming.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<VisionStateMachineTypeClient> ControlAsync(CancellationToken ct = default)
        {
            return await Proxy.GetVisionStateMachineAsync(m_operations.Telemetry, ct).ConfigureAwait(false)
                ?? throw Missing("VisionStateMachine");
        }

        /// <summary>
        /// Gets automatic mode with all control methods and optional step-model accessors.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<VisionAutomaticModeStateMachineTypeClient> AutomaticAsync(CancellationToken ct = default)
        {
            VisionStateMachineTypeClient control = await ControlAsync(ct).ConfigureAwait(false);
            return await control.GetAutomaticModeStateMachineAsync(m_operations.Telemetry, ct).ConfigureAwait(false)
                ?? throw Missing("AutomaticModeStateMachine");
        }

        /// <summary>
        /// Gets all recipe/product methods and the optional folder and transfer accessors.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<RecipeManagementTypeClient> RecipesAsync(CancellationToken ct = default)
        {
            return await Proxy.GetRecipeManagementAsync(m_operations.Telemetry, ct).ConfigureAwait(false)
                ?? throw Missing("RecipeManagement");
        }

        /// <summary>
        /// Gets the complete configuration-management interface.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<ConfigurationManagementTypeClient> ConfigurationsAsync(CancellationToken ct = default)
        {
            return await Proxy.GetConfigurationManagementAsync(m_operations.Telemetry, ct).ConfigureAwait(false)
                ?? throw Missing("ConfigurationManagement");
        }

        /// <summary>
        /// Gets all result retrieval, filtering, component, and handle-release methods.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<ResultManagementTypeClient> ResultsAsync(CancellationToken ct = default)
        {
            return await Proxy.GetResultManagementAsync(m_operations.Telemetry, ct).ConfigureAwait(false)
                ?? throw Missing("ResultManagement");
        }

        /// <summary>
        /// Gets the optional safety reporting interface.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<SafetyStateManagementTypeClient> SafetyAsync(CancellationToken ct = default)
        {
            return await Proxy.GetSafetyStateManagementAsync(m_operations.Telemetry, ct).ConfigureAwait(false)
                ?? throw Missing("SafetyStateManagement");
        }

        /// <summary>
        /// Registers recipe metadata and, when required by the server, transfers and commits its file.
        /// The caller retains ownership of the source stream.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="content"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<RecipeIdInternalDataType> UploadRecipeAsync(
            RecipeIdExternalDataType externalId,
            ProductIdDataType productId,
            Stream content,
            CancellationToken ct = default)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            RecipeManagementTypeClient recipes = await RecipesAsync(ct).ConfigureAwait(false);
            var added = await recipes.AddRecipeAsync(externalId, productId, ct).ConfigureAwait(false);
            ThrowIfDomainError(added.error, "AddRecipe");
            if (added.transferRequired)
            {
                RecipeTransferTypeClient transfer = await recipes
                    .GetRecipeTransferAsync(m_operations.Telemetry, ct).ConfigureAwait(false)
                    ?? throw Missing("RecipeTransfer");
                await UploadAsync(
                    transfer.ObjectId,
                    Variant.FromStructure(new RecipeTransferOptions { InternalId = added.internalId }),
                    content,
                    ct).ConfigureAwait(false);
            }
            return added.internalId;
        }

        /// <summary>
        /// Registers and transfers a configuration without implicitly activating it.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="content"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<ConfigurationIdDataType> UploadConfigurationAsync(
            ConfigurationIdDataType externalId,
            Stream content,
            CancellationToken ct = default)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            ConfigurationManagementTypeClient configurations = await ConfigurationsAsync(ct).ConfigureAwait(false);
            var added = await configurations.AddConfigurationAsync(externalId, ct).ConfigureAwait(false);
            ThrowIfDomainError(added.error, "AddConfiguration");
            if (added.transferRequired)
            {
                ConfigurationTransferTypeClient transfer = await configurations
                    .GetConfigurationTransferAsync(m_operations.Telemetry, ct).ConfigureAwait(false)
                    ?? throw Missing("ConfigurationTransfer");
                await UploadAsync(
                    transfer.ObjectId,
                    Variant.FromStructure(new ConfigurationTransferOptions { InternalId = added.internalId }),
                    content,
                    ct).ConfigureAwait(false);
            }
            return added.internalId;
        }

        /// <summary>
        /// Downloads result file content to a caller-owned stream and releases the metadata handle.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="AggregateException"></exception>
        public async ValueTask DownloadResultAsync(
            ResultIdDataType resultId, Stream destination, CancellationToken ct = default)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            ResultManagementTypeClient results = await ResultsAsync(ct).ConfigureAwait(false);
            var found = await results.GetResultByIdAsync(resultId, -1, ct).ConfigureAwait(false);
            ThrowIfDomainError(found.error, "GetResultById");
            try
            {
                ResultTransferTypeClient transfer = await results
                    .GetResultTransferAsync(m_operations.Telemetry, ct).ConfigureAwait(false)
                    ?? throw Missing("ResultTransfer");
                var files = new TemporaryFileTransferClient(m_operations.Session, transfer.ObjectId);
                UaFileStream stream = await files.GenerateFileForReadAsync(
                    Variant.FromStructure(new ResultTransferOptions { Id = resultId }), ct).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    await stream.CopyToAsync(destination, 81920, ct).ConfigureAwait(false);
                }
            }
            catch (Exception transferError)
            {
                try
                {
                    ThrowIfDomainError(
                        await results.ReleaseResultHandleAsync(found.resultHandle, CancellationToken.None)
                            .ConfigureAwait(false), "ReleaseResultHandle");
                }
                catch (Exception releaseError)
                {
                    throw new AggregateException("The result download and metadata-handle release both failed.",
                        transferError, releaseError);
                }
                throw;
            }
            ThrowIfDomainError(
                await results.ReleaseResultHandleAsync(found.resultHandle, CancellationToken.None)
                    .ConfigureAwait(false),
                "ReleaseResultHandle");
        }

        /// <summary>
        /// Reports a nonzero OPC 40100 domain error without confusing it with service success.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static void ThrowIfDomainError(int error, string method)
        {
            if (error != 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Machine Vision method '{0}' returned domain error {1}.", method, error);
            }
        }

        private async ValueTask UploadAsync(NodeId transfer, Variant options, Stream content, CancellationToken ct)
        {
            var files = new TemporaryFileTransferClient(m_operations.Session, transfer);
            UaTemporaryWriteFile write = await files.GenerateFileForWriteAsync(options, ct)
                .ConfigureAwait(false);
            await using (write.ConfigureAwait(false))
            {
                await content.CopyToAsync(write.Stream, 81920, ct).ConfigureAwait(false);
                NodeId completion = await write.CommitAsync(ct).ConfigureAwait(false);
                if (!completion.IsNull)
                {
                    await WaitForCommitAsync(completion, ct).ConfigureAwait(false);
                }
            }
        }

        private async ValueTask WaitForCommitAsync(NodeId completion, CancellationToken ct)
        {
            var stateMachine = new FileTransferStateMachineTypeClient(
                m_operations.Session, completion, m_operations.Telemetry);
            while (true)
            {
                FiniteStateSnapshot state = await stateMachine.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false);
                if (StatusCode.IsBad(state.Status))
                {
                    throw new ServiceResultException(state.Status);
                }
                if (state.CurrentStateId.IsNull)
                {
                    throw ServiceResultException.Create(StatusCodes.BadNoData,
                        "The transfer completion state is unavailable.");
                }
                if (state.CurrentStateId == global::Opc.Ua.ObjectIds.FileTransferStateMachineType_Idle)
                {
                    return;
                }
                if (state.CurrentStateId == global::Opc.Ua.ObjectIds.FileTransferStateMachineType_Error)
                {
                    throw new IOException("The server failed to apply the uploaded vision document.");
                }
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
            }
        }

        private static ServiceResultException Missing(string member)
        {
            return ServiceResultException.Create(
                StatusCodes.BadNotSupported, "The vision system does not expose '{0}'.", member);
        }

        private readonly VisionClientOperations m_operations;
    }
}
