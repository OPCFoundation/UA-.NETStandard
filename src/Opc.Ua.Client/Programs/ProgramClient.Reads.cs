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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Programs
{
    public sealed partial class ProgramClient
    {
        /// <summary>
        /// Reads type-level lifetime properties from the supplied concrete type.
        /// Missing properties, bad read quality and malformed values remain distinguishable.
        /// </summary>
        public async ValueTask<ProgramTypeCapabilities> ReadTypeCapabilitiesAsync(
            NodeId typeId,
            CancellationToken ct = default)
        {
            ValidateNodeId(typeId, nameof(typeId));
            ArrayOf<ReferenceDescription> children = await BrowseChildrenAsync(typeId, ct).ConfigureAwait(false);
            ProgramValue creatable = await ReadChildAsync(children, BrowseNames.Creatable, ct).ConfigureAwait(false);
            ProgramValue count = await ReadChildAsync(children, BrowseNames.InstanceCount, ct).ConfigureAwait(false);
            ProgramValue maximum = await ReadChildAsync(children, BrowseNames.MaxInstanceCount, ct)
                .ConfigureAwait(false);
            ProgramValue recycle = await ReadChildAsync(children, BrowseNames.MaxRecycleCount, ct)
                .ConfigureAwait(false);
            ValidateValueType(creatable, BuiltInType.Boolean);
            ValidateValueType(count, BuiltInType.UInt32);
            ValidateValueType(maximum, BuiltInType.UInt32);
            ValidateValueType(recycle, BuiltInType.UInt32);
            return new ProgramTypeCapabilities(creatable, count, maximum, recycle);
        }

        /// <summary>
        /// Reads invocation lifetime properties and discovers published method signatures, including
        /// vendor arguments. A missing mandatory property is reported as BadNotFound.
        /// </summary>
        public async ValueTask<ProgramCapabilities> ReadCapabilitiesAsync(
            NodeId invocationId,
            CancellationToken ct = default)
        {
            ValidateNodeId(invocationId, nameof(invocationId));
            ArrayOf<ReferenceDescription> children = await BrowseChildrenAsync(invocationId, ct).ConfigureAwait(false);
            ProgramValue deletable = await ReadChildAsync(children, BrowseNames.Deletable, ct, true)
                .ConfigureAwait(false);
            ProgramValue autoDelete = await ReadChildAsync(children, BrowseNames.AutoDelete, ct, true)
                .ConfigureAwait(false);
            ProgramValue recycleCount = await ReadChildAsync(children, BrowseNames.RecycleCount, ct, true)
                .ConfigureAwait(false);
            ValidateValueType(deletable, BuiltInType.Boolean);
            ValidateValueType(autoDelete, BuiltInType.Boolean);
            ValidateValueType(recycleCount, BuiltInType.Int32);
            var methods = new List<ProgramMethod>();
            for (int index = 0; index < children.Count; index++)
            {
                ReferenceDescription child = children[index];
                if (child.NodeClass != NodeClass.Method)
                {
                    continue;
                }
                NodeId methodId = LocalNodeId(child.NodeId);
                ArrayOf<ReferenceDescription> methodChildren = await BrowseChildrenAsync(methodId, ct)
                    .ConfigureAwait(false);
                DataValue executable = await ReadAttributeAsync(methodId, Attributes.Executable, ct)
                    .ConfigureAwait(false);
                DataValue userExecutable = await ReadAttributeAsync(methodId, Attributes.UserExecutable, ct)
                    .ConfigureAwait(false);
                ProgramValue input = await ReadChildAsync(methodChildren, BrowseNames.InputArguments, ct)
                    .ConfigureAwait(false);
                ProgramValue output = await ReadChildAsync(methodChildren, BrowseNames.OutputArguments, ct)
                    .ConfigureAwait(false);
                var executableValue = new ProgramValue(methodId, executable);
                var userExecutableValue = new ProgramValue(methodId, userExecutable);
                ValidateValueType(executableValue, BuiltInType.Boolean);
                ValidateValueType(userExecutableValue, BuiltInType.Boolean);
                ValidateArguments(input);
                ValidateArguments(output);
                methods.Add(new ProgramMethod(
                    methodId, child.BrowseName, executableValue, userExecutableValue, input, output));
            }
            return new ProgramCapabilities(
                deletable, autoDelete, recycleCount,
                FindChild(children, BrowseNames.ProgramDiagnostic, NodeClass.Variable),
                FindChild(children, BrowseNames.FinalResultData, NodeClass.Object),
                ArrayOf.Wrapped(methods.ToArray()));
        }

        /// <summary>
        /// Reads ProgramDiagnostic2 directly with both timestamps. Bad quality is retained without
        /// inventing a decoded value; a readable legacy or malformed structure throws BadTypeMismatch.
        /// </summary>
        public async ValueTask<ProgramDiagnosticResult> ReadDiagnosticsAsync(
            NodeId invocationId,
            CancellationToken ct = default)
        {
            ValidateNodeId(invocationId, nameof(invocationId));
            ArrayOf<ReferenceDescription> children = await BrowseChildrenAsync(invocationId, ct).ConfigureAwait(false);
            ProgramValue value = await ReadChildAsync(children, BrowseNames.ProgramDiagnostic, ct)
                .ConfigureAwait(false);
            if (!value.IsAvailable || StatusCode.IsBad(value.DataValue.StatusCode))
            {
                return new ProgramDiagnosticResult(value, null);
            }
            if (!value.DataValue.WrappedValue.TryGetStructure<ProgramDiagnostic2DataType>(
                Session.MessageContext, out ProgramDiagnostic2DataType? diagnostic))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "ProgramDiagnostic is not a readable ProgramDiagnostic2DataType.");
            }
            return new ProgramDiagnosticResult(value, diagnostic);
        }

        /// <summary>
        /// Browses the published FinalResultData hierarchy and reads each variable's value, DataType
        /// and ValueRank. Status and timestamps are retained; no cross-node atomicity is promised.
        /// </summary>
        public async ValueTask<ProgramFinalResults> ReadFinalResultsAsync(
            NodeId invocationId,
            CancellationToken ct = default)
        {
            ValidateNodeId(invocationId, nameof(invocationId));
            ArrayOf<ReferenceDescription> children = await BrowseChildrenAsync(invocationId, ct).ConfigureAwait(false);
            NodeId root = FindChild(children, BrowseNames.FinalResultData, NodeClass.Object);
            if (root.IsNull)
            {
                return new ProgramFinalResults(root, []);
            }
            var pending = new Queue<(NodeId Id, ArrayOf<QualifiedName> Path)>();
            var visited = new HashSet<NodeId> { root };
            var values = new List<ProgramResultValue>();
            pending.Enqueue((root, []));
            while (pending.Count > 0)
            {
                (NodeId id, ArrayOf<QualifiedName> path) = pending.Dequeue();
                ArrayOf<ReferenceDescription> results = await BrowseChildrenAsync(id, ct).ConfigureAwait(false);
                for (int index = 0; index < results.Count; index++)
                {
                    ReferenceDescription result = results[index];
                    if (result.NodeClass is not (NodeClass.Object or NodeClass.Variable))
                    {
                        continue;
                    }
                    NodeId resultId = LocalNodeId(result.NodeId);
                    if (!visited.Add(resultId))
                    {
                        continue;
                    }
                    ArrayOf<QualifiedName> resultPath = [.. path, result.BrowseName];
                    if (result.NodeClass == NodeClass.Variable)
                    {
                        ArrayOf<ReadValueId> requests =
                        [
                            new ReadValueId { NodeId = resultId, AttributeId = Attributes.Value },
                            new ReadValueId { NodeId = resultId, AttributeId = Attributes.DataType },
                            new ReadValueId { NodeId = resultId, AttributeId = Attributes.ValueRank }
                        ];
                        ReadResponse response = await ReadAsync(requests, ct).ConfigureAwait(false);
                        values.Add(new ProgramResultValue(resultId, resultPath,
                            response.Results[0], response.Results[1], response.Results[2]));
                    }
                    pending.Enqueue((resultId, resultPath));
                }
            }
            return new ProgramFinalResults(root, ArrayOf.Wrapped(values.ToArray()));
        }

        /// <summary>
        /// Browses components and properties without assuming numeric or string NodeId allocation.
        /// </summary>
        private ValueTask<ArrayOf<ReferenceDescription>> BrowseChildrenAsync(NodeId id, CancellationToken ct)
        {
            return BrowseAllAsync(id, ReferenceTypeIds.Aggregates, BrowseDirection.Forward, NodeClass.Unspecified, ct);
        }

        /// <summary>
        /// Resolves a standard namespace-qualified child and rejects ambiguous or malformed matches.
        /// </summary>
        private NodeId FindChild(
            ArrayOf<ReferenceDescription> children,
            string name,
            NodeClass expectedClass,
            bool required = false)
        {
            NodeId found = default;
            foreach (ReferenceDescription child in children)
            {
                if (child.BrowseName != new QualifiedName(name, 0))
                {
                    continue;
                }
                NodeId id = LocalNodeId(child.NodeId);
                if (child.NodeClass != expectedClass)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeClassInvalid,
                        $"The published {name} node has the wrong NodeClass.");
                }
                if (!found.IsNull && found != id)
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated,
                        $"Multiple children have the standard BrowseName {name}.");
                }
                found = id;
            }
            if (required && found.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound, $"Required Program child {name} is absent.");
            }
            return found;
        }

        /// <summary>
        /// Reads a child without converting access failures or bad quality into absence.
        /// </summary>
        private async ValueTask<ProgramValue> ReadChildAsync(
            ArrayOf<ReferenceDescription> children,
            string name,
            CancellationToken ct,
            bool required = false)
        {
            NodeId id = FindChild(children, name, NodeClass.Variable, required);
            return id.IsNull
                ? new ProgramValue(id, DataValue.FromStatusCode(StatusCodes.BadNotFound))
                : new ProgramValue(id, await ReadAttributeAsync(id, Attributes.Value, ct).ConfigureAwait(false));
        }

        /// <summary>
        /// Reads one attribute while retaining its quality.
        /// </summary>
        private async ValueTask<DataValue> ReadAttributeAsync(NodeId id, uint attribute, CancellationToken ct)
        {
            ReadResponse response = await ReadAsync(
                [new ReadValueId { NodeId = id, AttributeId = attribute }], ct).ConfigureAwait(false);
            return response.Results[0];
        }

        /// <summary>
        /// Validates the Read service envelope and cardinality.
        /// </summary>
        private async ValueTask<ReadResponse> ReadAsync(ArrayOf<ReadValueId> requests, CancellationToken ct)
        {
            ReadResponse response = await Session.ReadAsync(null, 0, TimestampsToReturn.Both, requests, ct)
                .ConfigureAwait(false);
            ValidateService(response.ResponseHeader);
            ClientBase.ValidateResponse(response.Results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
            return response;
        }

        /// <summary>
        /// Rejects malformed scalar capabilities rather than interpreting them as false or zero.
        /// </summary>
        private static void ValidateValueType(ProgramValue value, BuiltInType expected)
        {
            if (!value.IsAvailable || StatusCode.IsBad(value.DataValue.StatusCode))
            {
                return;
            }
            Variant variant = value.DataValue.WrappedValue;
            bool valid = expected switch
            {
                BuiltInType.Boolean => variant.TryGetValue(out bool _),
                BuiltInType.Int32 => variant.TryGetValue(out int _),
                BuiltInType.UInt32 => variant.TryGetValue(out uint _),
                _ => false
            };
            if (!valid)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "A Program capability is malformed.");
            }
        }

        /// <summary>
        /// Validates readable argument metadata without manufacturing a zero-argument signature.
        /// </summary>
        private void ValidateArguments(ProgramValue value)
        {
            if (value.IsAvailable && !StatusCode.IsBad(value.DataValue.StatusCode) &&
                !value.DataValue.WrappedValue.TryGetStructure(Session.MessageContext, out ArrayOf<Argument> _))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "Program method arguments are malformed.");
            }
        }
    }
}
