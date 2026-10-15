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

namespace Opc.Ua.Machinery.Client
{
    public sealed partial class MachineryClient
    {
        private async ValueTask<NodeId> ResolveStrictPathAsync(
            NodeId parent,
            ArrayOf<QualifiedName> path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (parent.IsNull || path.Count == 0)
            {
                return NodeId.Null;
            }
            ArrayOf<MachineryReadValue> fields = await ResolveStrictFieldsAsync(
                parent, [path], ReferenceTypeIds.HierarchicalReferences, cancellationToken)
                .ConfigureAwait(false);
            return fields[0].SourceNodeId;
        }

        private async ValueTask<ArrayOf<MachineryReadValue>> ResolveStrictFieldsAsync(
            NodeId parent,
            ArrayOf<ArrayOf<QualifiedName>> paths,
            NodeId referenceTypeId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requests = new BrowsePath[paths.Count];
            for (int ii = 0; ii < paths.Count; ii++)
            {
                var elements = new RelativePathElement[paths[ii].Count];
                for (int jj = 0; jj < elements.Length; jj++)
                {
                    elements[jj] = new RelativePathElement
                    {
                        ReferenceTypeId = referenceTypeId,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = paths[ii][jj]
                    };
                }
                requests[ii] = new BrowsePath
                {
                    StartingNode = parent,
                    RelativePath = new RelativePath { Elements = elements.ToArrayOf() }
                };
            }
            if (requests.Length == 0)
            {
                return [];
            }
            MachineryReadValue request = UnresolvedField(parent, paths[0], StatusCodes.Good);
            ValidateLocalNode(parent, request);
            TranslateBrowsePathsToNodeIdsResponse response;
            try
            {
                response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, requests.ToArrayOf(), cancellationToken).ConfigureAwait(false) ??
                    throw ReadFailure(StatusCodes.BadDecodingError, request, "The translation response is missing.");
            }
            catch (ServiceResultException exception) when (exception is not MachineryReadException)
            {
                throw ReadFailure(
                    exception.StatusCode,
                    UnresolvedField(parent, paths[0], exception.StatusCode),
                    exception.Message,
                    exception);
            }
            ValidateStrictResponse(
                response.ResponseHeader, response.Results.Count,
                requests.Length, response.DiagnosticInfos.Count, request);

            var fields = new MachineryReadValue[paths.Count];
            for (int ii = 0; ii < fields.Length; ii++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BrowsePathResult result = response.Results[ii] ??
                    throw ReadFailure(
                        StatusCodes.BadDecodingError,
                        UnresolvedField(parent, paths[ii], StatusCodes.BadDecodingError),
                        "A translated path result is missing.");
                MachineryReadValue unresolved = UnresolvedField(parent, paths[ii], result.StatusCode);
                if (result.StatusCode == StatusCodes.BadNoMatch)
                {
                    if (result.Targets.Count != 0)
                    {
                        throw ReadFailure(
                            StatusCodes.BadDecodingError, unresolved, "BadNoMatch returned translation targets.");
                    }
                    fields[ii] = UnresolvedField(parent, paths[ii], result.StatusCode, isAbsent: true);
                    continue;
                }
                RequireGood(result.StatusCode, unresolved);
                if (result.Targets.Count != 1 ||
                    result.Targets[0] is not BrowsePathTarget target ||
                    target.RemainingPathIndex != uint.MaxValue)
                {
                    throw ReadFailure(
                        StatusCodes.BadDecodingError, unresolved, "A path must have exactly one complete target.");
                }
                NodeId nodeId = StrictLocalNodeId(target.TargetId, unresolved);
                fields[ii] = new MachineryReadValue(
                    nodeId, parent, paths[ii], DataValue.FromStatusCode(StatusCodes.Good));
            }
            return fields.ToArrayOf();
        }

        private async ValueTask<ArrayOf<MachineryReadValue>> ReadStrictFieldsAsync(
            NodeId parent,
            ArrayOf<ArrayOf<QualifiedName>> paths,
            NodeId referenceTypeId,
            CancellationToken cancellationToken)
        {
            ArrayOf<MachineryReadValue> fields = await ResolveStrictFieldsAsync(
                parent, paths, referenceTypeId, cancellationToken).ConfigureAwait(false);
            return await ReadStrictSourcesAsync(fields, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<ArrayOf<MachineryReadValue>> ReadStrictSourcesAsync(
            ArrayOf<MachineryReadValue> sources,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requests = new List<ReadValueId>(sources.Count);
            var indices = new List<int>(sources.Count);
            for (int ii = 0; ii < sources.Count; ii++)
            {
                if (!sources[ii].IsAbsent)
                {
                    ValidateLocalNode(sources[ii].SourceNodeId, sources[ii]);
                    requests.Add(new ReadValueId
                    {
                        NodeId = sources[ii].SourceNodeId,
                        AttributeId = Attributes.Value
                    });
                    indices.Add(ii);
                }
            }
            if (requests.Count == 0)
            {
                return sources;
            }
            ReadResponse response;
            try
            {
                response = await Session.ReadAsync(
                    null, 0, TimestampsToReturn.Both, requests.ToArrayOf(), cancellationToken).ConfigureAwait(false) ??
                    throw ReadFailure(
                        StatusCodes.BadDecodingError,
                        WithValue(sources[indices[0]], DataValue.FromStatusCode(StatusCodes.BadDecodingError)),
                        "The read response is missing.");
            }
            catch (ServiceResultException exception) when (exception is not MachineryReadException)
            {
                throw ReadFailure(
                    exception.StatusCode,
                    WithValue(sources[indices[0]], DataValue.FromStatusCode(exception.StatusCode)),
                    exception.Message,
                    exception);
            }
            MachineryReadValue evidence = response.Results.Count == 0
                ? WithValue(sources[indices[0]], DataValue.FromStatusCode(StatusCodes.BadDecodingError))
                : WithValue(sources[indices[0]], response.Results[0]);
            if (response.ResponseHeader is not null &&
                StatusCode.IsGood(response.ResponseHeader.ServiceResult) &&
                response.Results.Count < indices.Count)
            {
                evidence = WithValue(
                    sources[indices[response.Results.Count]], DataValue.FromStatusCode(StatusCodes.BadDecodingError));
            }
            ValidateStrictResponse(
                response.ResponseHeader, response.Results.Count,
                requests.Count, response.DiagnosticInfos.Count, evidence);
            var fields = new MachineryReadValue[sources.Count];
            sources.Span.CopyTo(fields);
            for (int ii = 0; ii < indices.Count; ii++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MachineryReadValue field = WithValue(sources[indices[ii]], response.Results[ii]);
                RequireGood(field.Value.StatusCode, field);
                fields[indices[ii]] = field;
            }
            return fields.ToArrayOf();
        }

        private NodeId StrictLocalNodeId(ExpandedNodeId expanded, MachineryReadValue evidence)
        {
            if (expanded.ServerIndex != 0)
            {
                throw ReadFailure(StatusCodes.BadNotSupported, evidence, "A remote target is not a local read source.");
            }
            if (!string.IsNullOrEmpty(expanded.NamespaceUri) &&
                Session.NamespaceUris.GetIndex(expanded.NamespaceUri) < 0)
            {
                throw ReadFailure(StatusCodes.BadNodeIdUnknown, evidence, "The target namespace is not published.");
            }
            var nodeId = ExpandedNodeId.ToNodeId(expanded, Session.NamespaceUris);
            ValidateLocalNode(nodeId, evidence);
            return nodeId;
        }

        private void ValidateLocalNode(NodeId nodeId, MachineryReadValue evidence)
        {
            if (nodeId.IsNull || Session.NamespaceUris.GetString(nodeId.NamespaceIndex) is null)
            {
                throw ReadFailure(
                    StatusCodes.BadNodeIdUnknown, evidence,
                    "A non-null source in the session namespace table is required.");
            }
        }

        private static void ValidateStrictResponse(
            ResponseHeader? header,
            int actual,
            int expected,
            int diagnostics,
            MachineryReadValue evidence)
        {
            if (header is null)
            {
                throw ReadFailure(StatusCodes.BadDecodingError, evidence, "The response header is missing.");
            }
            RequireGood(header.ServiceResult, evidence);
            if (actual != expected || (diagnostics != 0 && diagnostics != expected))
            {
                throw ReadFailure(
                    StatusCodes.BadDecodingError, evidence, "The response counts do not match the request.");
            }
        }

        private static void RequireGood(StatusCode status, MachineryReadValue evidence)
        {
            if (!StatusCode.IsGood(status))
            {
                throw ReadFailure(status, evidence, "The Machinery service, operation or value status is not Good.");
            }
        }

        private static MachineryReadException ReadFailure(
            StatusCode status,
            MachineryReadValue evidence,
            string message,
            Exception? innerException = null)
        {
            return innerException is null
                ? new MachineryReadException(status, evidence, message)
                : new MachineryReadException(status, evidence, message, innerException);
        }

        private static MachineryReadValue UnresolvedField(
            NodeId parent,
            ArrayOf<QualifiedName> path,
            StatusCode status,
            bool isAbsent = false)
        {
            return new MachineryReadValue(NodeId.Null, parent, path, DataValue.FromStatusCode(status), isAbsent);
        }

        private static MachineryReadValue WithValue(MachineryReadValue source, in DataValue value)
        {
            return new MachineryReadValue(source.SourceNodeId, source.StartingNodeId, source.BrowsePath, value);
        }

        private static MachineryReadValue MissingNamespaceField(NodeId parent, string namespaceUri)
        {
            return UnresolvedField(parent, [], StatusCodes.BadNoMatch, isAbsent: true) with
            {
                MissingNamespaceUri = namespaceUri
            };
        }

        private static void RequireFieldType(MachineryReadValue field, bool valid)
        {
            if (!field.IsAbsent && !field.Value.WrappedValue.IsNull && !valid)
            {
                throw ReadFailure(
                    StatusCodes.BadTypeMismatch, field, "The Machinery field has an incompatible wire type.");
            }
        }

        private static bool HasType(Variant value, BuiltInType type, int rank = ValueRanks.Scalar)
        {
            return value.TypeInfo.BuiltInType == type && value.TypeInfo.ValueRank == rank;
        }

        private static void RequirePresent(MachineryReadValue field)
        {
            if (field.IsAbsent)
            {
                throw ReadFailure(StatusCodes.BadNoMatch, field, "A required Machinery field is absent.");
            }
        }

        private static LocalizedText AsLocalizedText(ReadValueSet values, QualifiedName name)
        {
            values.TryGetValue(name, out Variant value);
            bool valid = value.TryGetValue(out LocalizedText text);
            values.RequireType(name, valid && HasType(value, BuiltInType.LocalizedText));
            return valid ? text : default;
        }

        private void ValidateArrayCount(ReadValueSet values, QualifiedName name, int count)
        {
            if (m_options is not null && count > m_options.MaximumReadArrayItems)
            {
                throw ReadFailure(
                    StatusCodes.BadEncodingLimitsExceeded,
                    values.EvidenceOf(name)[0],
                    "The Machinery read array limit was exceeded.");
            }
        }

        private static bool IsNumber(Variant value)
        {
            return value.TypeInfo.ValueRank == ValueRanks.Scalar &&
                value.TypeInfo.BuiltInType is
                    BuiltInType.SByte or BuiltInType.Byte or BuiltInType.Int16 or BuiltInType.UInt16 or
                    BuiltInType.Int32 or BuiltInType.UInt32 or BuiltInType.Int64 or BuiltInType.UInt64 or
                    BuiltInType.Float or BuiltInType.Double;
        }

        private static ArrayOf<MachineryReadValue> CombineEvidence(
            ArrayOf<MachineryReadValue> first,
            ArrayOf<MachineryReadValue> second)
        {
            var fields = new MachineryReadValue[first.Count + second.Count];
            first.Span.CopyTo(fields);
            second.Span.CopyTo(fields.AsSpan(first.Count));
            return fields.ToArrayOf();
        }

        /// <summary>
        /// Keeps one operation's legacy projections separate from strict source evidence.
        /// </summary>
        internal sealed class ReadValueSet
        {
            /// <summary>
            /// Creates a legacy projection without changing its validation behavior.
            /// </summary>
            public ReadValueSet()
            {
            }

            /// <summary>
            /// Creates strict projections after preserving every field, including absence.
            /// </summary>
            public ReadValueSet(ArrayOf<MachineryReadValue> fields)
            {
                Values = fields;
                foreach (MachineryReadValue field in fields)
                {
                    QualifiedName name = field.BrowsePath[^1];
                    m_fields.Add(name, field);
                    if (!field.IsAbsent)
                    {
                        m_values.Add(name, field.Value.WrappedValue);
                    }
                }
            }

            /// <summary>
            /// Gets this batch's strict evidence; empty for legacy reads.
            /// </summary>
            public ArrayOf<MachineryReadValue> Values { get; }

            /// <summary>
            /// Adds a legacy value with its original overwrite behavior.
            /// </summary>
            public void Add(QualifiedName name, Variant value)
            {
                m_values[name] = value;
            }

            /// <summary>
            /// Gets a projected value when a source was read successfully.
            /// </summary>
            public bool TryGetValue(QualifiedName name, out Variant value)
            {
                return m_values.TryGetValue(name, out value);
            }

            /// <summary>
            /// Rejects a wrong type only when strict evidence is available.
            /// </summary>
            public void RequireType(QualifiedName name, bool valid)
            {
                if (m_fields.TryGetValue(name, out MachineryReadValue? field))
                {
                    RequireFieldType(field, valid);
                }
            }

            /// <summary>
            /// Rejects a missing mandatory field in strict mode.
            /// </summary>
            public void RequirePresent(QualifiedName name)
            {
                if (m_fields.TryGetValue(name, out MachineryReadValue? field))
                {
                    MachineryClient.RequirePresent(field);
                }
            }

            /// <summary>
            /// Gets one field's evidence without losing the batch's other fields.
            /// </summary>
            public ArrayOf<MachineryReadValue> EvidenceOf(QualifiedName name)
            {
                return m_fields.TryGetValue(name, out MachineryReadValue? field) ? [field] : [];
            }

            private readonly Dictionary<QualifiedName, Variant> m_values = [];
            private readonly Dictionary<QualifiedName, MachineryReadValue> m_fields = [];
        }
    }
}
