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
using Opc.Ua.AI.Inference;
using Opc.Ua;
using Opc.Ua.AI;
using AIRefs = Opc.Ua.AI.ReferenceTypeIds;
using BrowseNames = Opc.Ua.AI.BrowseNames;
using ObjectIds = Opc.Ua.ObjectIds;
using ReferenceTypeIds = Opc.Ua.ReferenceTypeIds;

namespace Opc.Ua.AI.Server
{
    public sealed partial class AINodeManager
    {
        private ModelSourceState? m_source;

        /// <summary>
        /// Publishes the source this Server consumes models from.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A source is how the address space says "the models here are not mine".
        /// It names an endpoint, the dialect that endpoint speaks and - by
        /// reference, never by value - the credential the Server presents. Without
        /// it a client browsing a deployment could not tell a model running on this
        /// machine from one running in somebody else's data centre, which is the
        /// distinction most operational questions turn on.
        /// </para>
        /// <para>
        /// <c>TestConnection</c> exists so that a commissioning engineer can
        /// establish that the endpoint, the credential and the network policy are
        /// right before a deployment depends on them, rather than learning it from
        /// the first inference that mattered.
        /// </para>
        /// </remarks>
        private void BuildCatalogue()
        {
            m_source = new ModelSourceState(null);
            m_source.Create(
                SystemContext,
                NodeId.Null,
                new QualifiedName("ModelSource", NamespaceIndex),
                new LocalizedText(m_backendOptions.EndpointUri),
                true);

            Child<PropertyState<string>>(m_source, BrowseNames.SourceId).Value =
                m_options.SourceId;
            Child<PropertyState<string>>(m_source, BrowseNames.EndpointUri).Value =
                m_backendOptions.EndpointUri;
            Child<PropertyState<ApiDialectEnum>>(m_source, BrowseNames.ApiDialect).Value =
                ApiDialectEnum.RestChatCompletions;
            Child<PropertyState<AuthenticationKindEnum>>(
                m_source, BrowseNames.AuthenticationKind).Value =
                ToAuthenticationKind(m_backendOptions.Authentication);
            Child<PropertyState<ReachabilityEnum>>(m_source, BrowseNames.Reachability).Value =
                ReachabilityEnum.Unknown;

            // The reference, never the secret. A client is entitled to know which
            // credential a Server uses so it can tell whether the right one is
            // configured; it is not entitled to the credential, and an address space
            // that carried one would hand it to everyone who could browse.
            if (!string.IsNullOrEmpty(m_backendOptions.CredentialReference))
            {
                Child<PropertyState<string>>(m_source, BrowseNames.CredentialReference).Value =
                    m_backendOptions.CredentialReference;
            }

            Child<TestConnectionMethodState>(m_source, BrowseNames.TestConnection).OnCallAsync =
                (context, method, objectId, ct) => TestConnectionAsync(ct);

            Child<ListModelsMethodState>(m_source, BrowseNames.ListModels).OnCallAsync =
                (context, method, objectId, filter, maxResults, continuationPoint, ct) =>
                    ListModelsAsync(filter, maxResults, continuationPoint, ct);

            Child<FolderState>(m_root!, BrowseNames.Sources).AddChild(m_source);

            // Every model this Server publishes came from that source, and saying so
            // is what makes the provenance walk terminate somewhere meaningful
            // instead of at a name.
            NodeId importedFrom = RefType(AIRefs.ImportedFrom);

            foreach (ModelState? model in new[] { m_primaryModel, m_fallbackModel })
            {
                if (model is not null)
                {
                    model.AddReference(importedFrom, false, m_source.NodeId);
                    m_source.AddReference(importedFrom, true, model.NodeId);
                }
            }
        }

        /// <summary>
        /// Probes the source and records what it found.
        /// </summary>
        private async ValueTask<TestConnectionMethodStateResult> TestConnectionAsync(
            CancellationToken ct)
        {
            BackendProbe probe = await m_backends.Primary.ProbeAsync(ct).ConfigureAwait(false);

            lock (m_sync)
            {
                if (m_source is not null)
                {
                    Child<PropertyState<ReachabilityEnum>>(m_source, BrowseNames.Reachability)
                        .Value = probe.Reachable
                            ? ReachabilityEnum.Reachable
                            : ReachabilityEnum.Unreachable;

                    if (probe.Reachable)
                    {
                        Child<PropertyState<DateTimeUtc>>(m_source, BrowseNames.LastSuccessAt)
                            .Value = DateTime.UtcNow;
                        Child<PropertyState<uint>>(
                            m_source, BrowseNames.ConsecutiveFailures).Value = 0;
                    }
                    else
                    {
                        PropertyState<uint> failures = Child<PropertyState<uint>>(
                            m_source, BrowseNames.ConsecutiveFailures);
                        failures.Value++;
                    }

                    m_source.ClearChangeMasks(SystemContext, true);
                }
            }

            return new TestConnectionMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Reachable = probe.Reachable,
                Detail = new LocalizedText(probe.Detail ?? string.Empty)
            };
        }

        /// <summary>
        /// Lists what the source offers.
        /// </summary>
        /// <remarks>
        /// Answered from the source itself rather than from what this Server has
        /// already imported. The question is what could be deployed, and answering
        /// it from local state would only ever return what already had been.
        /// </remarks>
        private async ValueTask<ListModelsMethodStateResult> ListModelsAsync(
            string filter,
            uint maxResults,
            ByteString continuationPoint,
            CancellationToken ct)
        {
            IReadOnlyList<BackendModel> models = await m_backends.Primary
                .ListModelsAsync(
                    string.IsNullOrEmpty(filter) ? null : filter,
                    0,
                    ct)
                .ConfigureAwait(false);

            // Clause 8.2. The continuation point carries the offset reached so far,
            // so an enumeration that MaxResults truncated can be resumed rather than
            // permanently losing everything past the bound - which against a public
            // catalogue is most of it. An empty point starts at the beginning, and an
            // empty point returned means the enumeration is complete.
            int offset = ReadContinuationOffset(continuationPoint);
            if (offset < 0 || offset > models.Count)
            {
                return new ListModelsMethodStateResult
                {
                    ServiceResult = StatusCodes.BadContinuationPointInvalid,
                    Models = ArrayOf<ModelReferenceDataType>.Empty,
                    ContinuationPointOut = ByteString.Empty
                };
            }

            int take = maxResults == 0 ? models.Count - offset : (int)Math.Min(maxResults, (uint)(models.Count - offset));
            var references = new ModelReferenceDataType[take];

            for (int index = 0; index < take; index++)
            {
                BackendModel model = models[offset + index];
                references[index] = new ModelReferenceDataType
                {
                    Publisher = model.Publisher,
                    Name = model.Name,
                    Version = model.Version
                };
            }

            int next = offset + take;
            return new ListModelsMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Models = new ArrayOf<ModelReferenceDataType>(references),
                ContinuationPointOut = next < models.Count
                    ? ByteString.From(BitConverter.GetBytes(next))
                    : ByteString.Empty
            };
        }

        /// <summary>
        /// Reads the offset a continuation point carries, or -1 when it is malformed.
        /// </summary>
        private static int ReadContinuationOffset(ByteString continuationPoint)
        {
            ReadOnlyMemory<byte> bytes = continuationPoint.Memory;
            if (bytes.Length == 0)
            {
                return 0;
            }
            if (bytes.Length != sizeof(int))
            {
                return -1;
            }
            return BitConverter.ToInt32(bytes.Span);
        }

        private static AuthenticationKindEnum ToAuthenticationKind(
            BackendAuthentication authentication)
        {
            return authentication switch
            {
                BackendAuthentication.ApiKey => AuthenticationKindEnum.ApiKey,
                BackendAuthentication.BearerToken => AuthenticationKindEnum.BearerToken,
                BackendAuthentication.WorkloadIdentity =>
                    AuthenticationKindEnum.WorkloadIdentity,
                _ => AuthenticationKindEnum.Anonymous
            };
        }
    }
}
