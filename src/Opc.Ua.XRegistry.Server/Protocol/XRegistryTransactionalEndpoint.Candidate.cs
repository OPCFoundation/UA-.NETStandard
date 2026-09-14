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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    public sealed partial class XRegistryTransactionalEndpoint
    {
        private XRegistryEndpointDescription DescribeSnapshot(JsonObject snapshot, string? generation, bool canWrite)
        {
            RequireShortLinks(snapshot);
            var model = new XRegistryModelRules(SnapshotModel(snapshot));
            return new XRegistryEndpointDescription(m_options.RegistryId)
            {
                Model = Element(model.CompleteModel()),
                Capabilities = Element(ValidationCapabilities(canWrite)),
                Profile = "experimental-transactional-v1",
                PublicRoot = m_options.PublicRoot,
                ShortLinkPrefix = snapshot["shortlinks"] is JsonObject aliases
                    ? XRegistryModelRules.Text(aliases["prefix"]) : null,
                SupportsAtomicMutations = true,
                SupportsConditionalMutations = true,
                SupportsWriteTouch = true,
                SupportsOperationReplay = m_store.SupportsDurableReplay &&
                    (m_options.DocumentStore is null || m_options.DocumentStore.IsDurable),
                SupportsPreparedMutations = true,
                SupportsPreparedSnapshots = true,
                SupportsVersionIncarnationGuards = true,
                SupportsGenerationGuards = true,
                Generation = generation
            };
        }

        private async ValueTask<XRegistryEndpointDescription> InspectCandidateAsync(
            PreparedOperation operation, CancellationToken ct)
        {
            if (!await AuthorizeAsync(operation.Context!, false, ct).ConfigureAwait(false))
            {
                throw new UnauthorizedAccessException("Candidate inspection is no longer authorized.");
            }
            operation.RequireCandidate();
            ct.ThrowIfCancellationRequested();
            bool canWrite = await AuthorizeAsync(operation.Context!, true, ct).ConfigureAwait(false);
            return DescribeSnapshot(operation.Candidate!, operation.CandidateGeneration, canWrite);
        }

        private async ValueTask<XRegistryResponse> ReadCandidateAsync(
            PreparedOperation operation, XRegistryRequest request, CancellationToken ct)
        {
            request.ThrowIfNull(nameof(request));
            if (!await AuthorizeAsync(operation.Context!, false, ct).ConfigureAwait(false))
            {
                return Error("unauthorized", "Candidate access is no longer authorized.", 403);
            }
            operation.RequireCandidate();
            ct.ThrowIfCancellationRequested();
            if (request.IsMutation || request.OperationId is not null)
            {
                return Error("action_not_supported", "A candidate is an immutable read view.", 405);
            }
            if (request.ExpectedGeneration is not null && request.ExpectedGeneration != operation.CandidateGeneration)
            {
                return Error("concurrent_change", "The request does not address this candidate generation.", 409);
            }
            JsonObject snapshot = operation.Candidate!;
            bool canWrite = await AuthorizeAsync(operation.Context!, true, ct).ConfigureAwait(false);
            var model = new XRegistryModelRules(SnapshotModel(snapshot));
            var transaction = new Transaction(snapshot, snapshot, request with
            {
                Context = operation.Context!,
                ExpectedGeneration = operation.CandidateGeneration
            },
                m_time.GetUtcNow().UtcDateTime)
            { CanWrite = canWrite };
            try
            {
                transaction.Request = ResolveShortLink(snapshot, model, transaction.Request);
                request = transaction.Request;
                XRegistryTarget target = model.Resolve(request.Path);
                ValidateFlags(model, target, request);
                if (request.ExpectedVersionIncarnation is not null &&
                    (target.Kind != XRegistryEntityKind.Version ||
                        transaction.Entries[target.Path] is not JsonObject version ||
                        GetVersionIncarnation(version) != request.ExpectedVersionIncarnation))
                {
                    return Error("version_incarnation_changed", "The requested candidate Version does not match.", 409);
                }
                XRegistryResponse response = request.Action == XRegistryAction.Describe
                    ? new XRegistryResponse(204) { AllowedActions = Allowed(target, canWrite) }
                    : await ReadAsync(transaction, model, target, ct).ConfigureAwait(false);
                response = response with { Generation = operation.CandidateGeneration };
                _ = m_codec.EncodeResponse(response);
                return response;
            }
            catch (XRegistryRejectionException exception)
            {
                return Error(exception.Code, exception.Message, exception.StatusCode);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                return Error("bad_request", exception.Message);
            }
        }
    }
}
