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
using Opc.Ua;
using Ai = Opc.Ua.AI;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Uses the transfer's standard FileType proxies with bounded handle cleanup.
    /// The AI client's convenience streaming methods close with CancellationToken.None,
    /// which cannot provide the desktop task's cancellation and cleanup bounds.
    /// </summary>
    internal static class AITaskFileTransfer
    {
        public static async ValueTask WriteRequestAsync(
            CompanionContext context, NodeId transfer, ByteString payload, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (payload.IsNull || payload.IsEmpty || payload.Length > AIResponseTaskBuffer.MaximumBytes)
            {
                throw new ArgumentException("The transfer request must contain 1 through 1048576 bytes.",
                    nameof(payload));
            }
            NodeId fileId = await IndustrialCompanionAccess.ResolveChildAsync(
                context, transfer, Ai.Namespaces.AI, Ai.BrowseNames.Request, false, token).ConfigureAwait(false);
            await IndustrialCompanionAccess.RequireTargetAsync(
                context, new CompanionTarget("ai", fileId, "Request file", "Request file"), "ai",
                [new(ObjectTypeIds.FileType, "Request file")], token).ConfigureAwait(false);
            await AICompanionTaskAccess.RequireExecutableAsync(
                context, fileId, BrowseNames.Open, Namespaces.OpcUa, token).ConfigureAwait(false);
            await AICompanionTaskAccess.RequireExecutableAsync(
                context, fileId, BrowseNames.Write, Namespaces.OpcUa, token).ConfigureAwait(false);
            await AICompanionTaskAccess.RequireExecutableAsync(
                context, fileId, BrowseNames.Close, Namespaces.OpcUa, token).ConfigureAwait(false);
            var file = new FileTypeClient(context.Session, fileId, context.Telemetry);
            uint handle = await file.OpenAsync(6, token).ConfigureAwait(false);
            Exception? failure = null;
            try
            {
                for (int offset = 0; offset < payload.Length; offset += ChunkBytes)
                {
                    token.ThrowIfCancellationRequested();
                    int count = Math.Min(ChunkBytes, payload.Length - offset);
                    await file.WriteAsync(handle, ByteString.From(payload.Span.Slice(offset, count)), token)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception error) when (IsFileFailure(error))
            {
                failure = error;
                throw;
            }
            finally
            {
                await CloseAsync(file, handle, failure).ConfigureAwait(false);
            }
        }

        public static async ValueTask ReadResponseAsync(
            CompanionContext context, NodeId fileId, AIResponseTaskBuffer destination, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(destination);
            var file = new FileTypeClient(context.Session, fileId, context.Telemetry);
            uint handle = await file.OpenAsync(1, token).ConfigureAwait(false);
            Exception? failure = null;
            try
            {
                for (int read = 0; read < MaximumReadRequests; read++)
                {
                    token.ThrowIfCancellationRequested();
                    ByteString chunk = await file.ReadAsync(handle, ChunkBytes, token).ConfigureAwait(false);
                    if (chunk.IsNull || chunk.Length > ChunkBytes)
                    {
                        throw AICompanionTaskAccess.InvalidData("The response file returned an invalid chunk.");
                    }
                    if (chunk.IsEmpty)
                    {
                        return;
                    }
                    await destination.WriteAsync(chunk.Memory, token).ConfigureAwait(false);
                }
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The response exceeded the bounded file-read budget.");
            }
            catch (Exception error) when (IsFileFailure(error))
            {
                failure = error;
                throw;
            }
            finally
            {
                await CloseAsync(file, handle, failure).ConfigureAwait(false);
            }
        }

        private static async ValueTask CloseAsync(FileTypeClient file, uint handle, Exception? failure)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await file.CloseAsync(handle, cleanup.Token).ConfigureAwait(false);
            }
            catch (Exception cleanupFailure) when (failure is not null && IsFileFailure(cleanupFailure))
            {
                throw new AggregateException(
                    "The file operation and cleanup of its owned handle both failed.", failure, cleanupFailure);
            }
        }

        private static bool IsFileFailure(Exception error)
        {
            return error is ServiceResultException or OperationCanceledException or IOException or
                InvalidOperationException or ArgumentException or TimeoutException;
        }

        private const int ChunkBytes = 4096;
        private const int MaximumReadRequests = (AIResponseTaskBuffer.MaximumBytes / ChunkBytes) + 2;
    }
}
