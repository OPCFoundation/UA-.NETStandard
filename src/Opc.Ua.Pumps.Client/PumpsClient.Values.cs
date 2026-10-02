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
using UaBrowseNames = Opc.Ua.BrowseNames;

namespace Opc.Ua.Pumps.Client
{
    public sealed partial class PumpsClient
    {
        /// <summary>
        /// Reads every value one OPC 40223 group publishes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The group's children are discovered by browsing rather than taken
        /// from a fixed list, so this covers the whole group whatever the
        /// server publishes - all 78 of <c>DesignType</c> or the three a small
        /// pump bothers with.
        /// </para>
        /// <para>
        /// Two shapes appear below an OPC 40223 group and both are handled.
        /// Most children are variables and are read directly. The signal and
        /// actuation groups instead hold <c>DiscreteInputObjectType</c> and
        /// <c>DiscreteOutputObjectType</c> <em>objects</em>, whose reading
        /// lives one level down in <c>DiscreteInputValue</c> or
        /// <c>DiscreteOutputValue</c>; those are followed automatically and
        /// reported under the object's name, which is the signal name a caller
        /// is looking for.
        /// </para>
        /// </remarks>
        /// <param name="group">
        /// The group object, as returned by one of the <c>Resolve…</c>
        /// methods. <see cref="NodeId.Null"/> yields
        /// <see cref="PumpValueSet.Empty"/>.
        /// </param>
        /// <param name="options">
        /// What to fetch. Defaults to <see cref="PumpReadOptions.Default"/>.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<PumpValueSet> ReadValueSetAsync(
            NodeId group,
            PumpReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return ReadValueSetAsync(group, options ?? PumpReadOptions.Default, 0, cancellationToken);
        }

        private async ValueTask<PumpValueSet> ReadValueSetAsync(
            NodeId group,
            PumpReadOptions options,
            int depth,
            CancellationToken cancellationToken)
        {
            if (group.IsNull)
            {
                return PumpValueSet.Empty;
            }

            (List<ValueTarget> targets, List<ValueTarget> nested) = await CollectValueTargetsAsync(
                group,
                cancellationToken).ConfigureAwait(false);

            PumpValueSet values;
            if (targets.Count == 0)
            {
                values = new PumpValueSet(group, []);
            }
            else
            {
                if (options.IncludeMetadata)
                {
                    await ResolveMetadataAsync(targets, options, cancellationToken)
                        .ConfigureAwait(false);
                }
                values = await ReadTargetsAsync(group, targets, options, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Nested OPC 40223 objects - the <Vibration> instances below
            // Measurements, most prominently - are groups of their own. They are
            // read one level deep; no OPC 40223 group nests further.
            if (nested.Count == 0 || depth >= kMaxNestedDepth)
            {
                return values;
            }
            var groups = new List<KeyValuePair<string, PumpValueSet>>(nested.Count);
            foreach (ValueTarget target in nested)
            {
                PumpValueSet child = await ReadValueSetAsync(
                    target.NodeId,
                    options,
                    depth + 1,
                    cancellationToken).ConfigureAwait(false);
                if (!child.IsEmpty || child.Groups.Count > 0)
                {
                    groups.Add(new(target.Name, child));
                }
            }
            return new PumpValueSet(group, values, groups);
        }

        private const int kMaxNestedDepth = 1;

        /// <summary>
        /// Browses a group and resolves the node that actually carries each
        /// child's value.
        /// </summary>
        private async ValueTask<(List<ValueTarget> Targets, List<ValueTarget> Nested)> CollectValueTargetsAsync(
            NodeId group,
            CancellationToken cancellationToken)
        {
            var targets = new List<ValueTarget>();
            var discreteObjects = new List<ValueTarget>();
            var nested = new List<ValueTarget>();
            ushort pumpsNamespace = NamespaceIndexOf(Namespaces.Pumps);

            await foreach (ReferenceDescription reference in BrowseChildrenAsync(
                    group,
                    NodeClass.Variable | NodeClass.Object,
                    cancellationToken).ConfigureAwait(false))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                if (nodeId.IsNull)
                {
                    continue;
                }
                string name = NameOf(reference.BrowseName);
                if (name.Length == 0)
                {
                    continue;
                }
                var target = new ValueTarget(name, nodeId)
                {
                    TypeDefinitionId = ExpandedNodeId.ToNodeId(
                        reference.TypeDefinition,
                        Session.NamespaceUris)
                };
                if (reference.NodeClass == NodeClass.Variable)
                {
                    target.ValueNodeId = nodeId;
                    targets.Add(target);
                }
                else
                {
                    // A discrete signal object carries its reading one level
                    // down; any other object is resolved below.
                    discreteObjects.Add(target);
                }
            }

            if (discreteObjects.Count > 0)
            {
                await ResolveDiscreteValuesAsync(discreteObjects, cancellationToken)
                    .ConfigureAwait(false);
                foreach (ValueTarget target in discreteObjects)
                {
                    if (!target.ValueNodeId.IsNull)
                    {
                        targets.Add(target);
                    }
                    else if (target.TypeDefinitionId.NamespaceIndex == pumpsNamespace &&
                        !target.TypeDefinitionId.IsNull)
                    {
                        // Not a signal, but an OPC 40223 object with values of
                        // its own, such as a VibrationMeasurementType. FileType
                        // documents and other base-model objects are left out.
                        nested.Add(target);
                    }
                }
            }
            return (targets, nested);
        }

        /// <summary>
        /// Resolves <c>DiscreteInputValue</c>/<c>DiscreteOutputValue</c> for a
        /// batch of signal objects in one call.
        /// </summary>
        private async ValueTask ResolveDiscreteValuesAsync(
            List<ValueTarget> discreteObjects,
            CancellationToken cancellationToken)
        {
            ushort pumpsIndex = NamespaceIndexOf(Namespaces.Pumps);
            var input = new QualifiedName(BrowseNames.DiscreteInputValue, pumpsIndex);
            var output = new QualifiedName(BrowseNames.DiscreteOutputValue, pumpsIndex);

            var paths = new List<BrowsePath>(discreteObjects.Count * 2);
            foreach (ValueTarget target in discreteObjects)
            {
                paths.Add(ChildPath(target.NodeId, input));
                paths.Add(ChildPath(target.NodeId, output));
            }

            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    paths.ToArray().ToArrayOf(),
                    cancellationToken).ConfigureAwait(false);

            for (int ii = 0; ii < discreteObjects.Count; ii++)
            {
                // An input signal resolves the first path, an output signal the
                // second; a PumpKickObject is an output object and resolves the
                // second as well.
                NodeId resolved = TargetAt(response, ii * 2);
                if (resolved.IsNull)
                {
                    resolved = TargetAt(response, (ii * 2) + 1);
                }
                discreteObjects[ii].ValueNodeId = resolved;
            }
        }

        /// <summary>
        /// Resolves the <c>EngineeringUnits</c> and <c>EURange</c> properties
        /// of every target in one call.
        /// </summary>
        private async ValueTask ResolveMetadataAsync(
            List<ValueTarget> targets,
            PumpReadOptions options,
            CancellationToken cancellationToken)
        {
            var units = new QualifiedName(UaBrowseNames.EngineeringUnits, 0);
            var range = new QualifiedName(UaBrowseNames.EURange, 0);

            var paths = new List<BrowsePath>(targets.Count * 2);
            foreach (ValueTarget target in targets)
            {
                if (options.IncludeEngineeringUnits)
                {
                    paths.Add(ChildPath(target.ValueNodeId, units));
                }
                if (options.IncludeRanges)
                {
                    paths.Add(ChildPath(target.ValueNodeId, range));
                }
            }
            if (paths.Count == 0)
            {
                return;
            }

            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    paths.ToArray().ToArrayOf(),
                    cancellationToken).ConfigureAwait(false);

            int cursor = 0;
            foreach (ValueTarget target in targets)
            {
                if (options.IncludeEngineeringUnits)
                {
                    target.UnitsNodeId = TargetAt(response, cursor++);
                }
                if (options.IncludeRanges)
                {
                    target.RangeNodeId = TargetAt(response, cursor++);
                }
            }
        }

        /// <summary>
        /// Reads every resolved value and metadata node in one
        /// <c>Read</c> and assembles the set.
        /// </summary>
        private async ValueTask<PumpValueSet> ReadTargetsAsync(
            NodeId group,
            List<ValueTarget> targets,
            PumpReadOptions options,
            CancellationToken cancellationToken)
        {
            var reads = new List<ReadValueId>(targets.Count * 3);
            foreach (ValueTarget target in targets)
            {
                target.ValueIndex = reads.Count;
                reads.Add(ValueRead(target.ValueNodeId));
                if (!target.UnitsNodeId.IsNull)
                {
                    target.UnitsIndex = reads.Count;
                    reads.Add(ValueRead(target.UnitsNodeId));
                }
                if (!target.RangeNodeId.IsNull)
                {
                    target.RangeIndex = reads.Count;
                    reads.Add(ValueRead(target.RangeNodeId));
                }
            }

            ReadResponse response = await Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: options.IncludeTimestamps
                    ? TimestampsToReturn.Source
                    : TimestampsToReturn.Neither,
                nodesToRead: reads.ToArray().ToArrayOf(),
                ct: cancellationToken).ConfigureAwait(false);

            var values = new List<PumpValue>(targets.Count);
            foreach (ValueTarget target in targets)
            {
                if (!TryResultAt(response, target.ValueIndex, out DataValue value))
                {
                    continue;
                }
                DateTimeUtc timestamp = value.SourceTimestamp;
                values.Add(new PumpValue
                {
                    NodeId = target.ValueNodeId,
                    Name = target.Name,
                    Value = value.WrappedValue,
                    StatusCode = value.StatusCode,
                    SourceTimestamp = options.IncludeTimestamps &&
                        timestamp.ToDateTime() != System.DateTime.MinValue
                            ? timestamp.ToDateTime()
                            : null,
                    EngineeringUnits = Decode<EUInformation>(response, target.UnitsIndex),
                    EuRange = Decode<Range>(response, target.RangeIndex)
                });
            }
            return new PumpValueSet(group, values);
        }

        private static BrowsePath ChildPath(NodeId parent, QualifiedName browseName)
        {
            return new BrowsePath
            {
                StartingNode = parent,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = browseName
                        }
                    ]
                }
            };
        }

        private static ReadValueId ValueRead(NodeId nodeId)
        {
            return new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value };
        }

        private NodeId TargetAt(TranslateBrowsePathsToNodeIdsResponse response, int index)
        {
            if (index < 0 || index >= response.Results.Count)
            {
                return NodeId.Null;
            }
            BrowsePathResult result = response.Results[index];
            if (!StatusCode.IsGood(result.StatusCode) || result.Targets.Count == 0)
            {
                return NodeId.Null;
            }
            return ExpandedNodeId.ToNodeId(
                result.Targets[0].TargetId,
                Session.NamespaceUris);
        }

        private static bool TryResultAt(
            ReadResponse response,
            int index,
            out DataValue value)
        {
            if (index >= 0 && index < response.Results.Count)
            {
                value = response.Results[index];
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Decodes a structured metadata value - an <c>EUInformation</c> or a
        /// <c>Range</c> - from a read result.
        /// </summary>
        /// <typeparam name="T">The structured type to decode.</typeparam>
        private T? Decode<T>(ReadResponse response, int index)
            where T : class, IEncodeable
        {
            if (!TryResultAt(response, index, out DataValue value) ||
                StatusCode.IsBad(value.StatusCode))
            {
                return null;
            }
#pragma warning disable CS8600 // TryGetValue is [MaybeNullWhen(false)]
            bool decodedOk = value.WrappedValue.TryGetValue(
                out T decoded,
                Session.MessageContext);
#pragma warning restore CS8600
            return decodedOk ? decoded : null;
        }

        /// <summary>
        /// One child of a group, and the node that carries its value - itself
        /// for a variable, its <c>Discrete…Value</c> for a signal object.
        /// </summary>
        private sealed class ValueTarget(string name, NodeId nodeId)
        {
            public string Name { get; } = name;

            public NodeId NodeId { get; } = nodeId;

            public NodeId ValueNodeId { get; set; } = NodeId.Null;

            public NodeId TypeDefinitionId { get; init; } = NodeId.Null;

            public NodeId UnitsNodeId { get; set; } = NodeId.Null;

            public NodeId RangeNodeId { get; set; } = NodeId.Null;

            public int ValueIndex { get; set; } = -1;

            public int UnitsIndex { get; set; } = -1;

            public int RangeIndex { get; set; } = -1;
        }
    }
}
