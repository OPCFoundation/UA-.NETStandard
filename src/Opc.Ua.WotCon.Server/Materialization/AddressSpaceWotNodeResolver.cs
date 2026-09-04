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
using Opc.Ua.Server;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// The loaded-AddressSpace part of the WoT Binding Section 5.1.5 local
    /// context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 5.1.5 makes the AddressSpace the fallback half of the local
    /// context, consulted after the sibling documents of the conversion. It is
    /// what lets a document bind to a type a companion model defines and the
    /// Server has loaded - the primary use of Section 5.2.1 - so without it
    /// every companion-model type binding is unresolvable and, because Section
    /// 5.2.1 forbids falling back to <c>BaseObjectType</c>, fails the
    /// projection.
    /// </para>
    /// <para>
    /// Compose it behind <see cref="SnapshotWotNodeResolver"/> with
    /// <see cref="WotCompositeNodeResolver"/> to get the specified order.
    /// </para>
    /// <para>
    /// The BrowseName index is built once, on first use. Types are loaded when
    /// a node manager starts, so the type hierarchy is settled by the time any
    /// document is converted. The ReferenceType index of Section 5.3, which
    /// carries both of the names OPC 10000-3 gives a ReferenceType, is built
    /// the same way and independently, so a conversion that names no relation
    /// never pays for it.
    /// </para>
    /// </remarks>
    public sealed class AddressSpaceWotNodeResolver
        : IWotNodeResolver, IWotReferenceTypeResolver, IWotTypeDeclarationResolver
    {
        /// <summary>
        /// Initializes a resolver over a Server's AddressSpace.
        /// </summary>
        /// <param name="server">The Server whose AddressSpace is consulted.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="server"/> is <c>null</c>.
        /// </exception>
        public AddressSpaceWotNodeResolver(IServerInternal server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
        }

        /// <inheritdoc/>
        public ValueTask<bool> HoldsNamespaceAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A namespace in the Server's table is one it has loaded as an
            // information model, which is exactly what Section 5.2.1 means by
            // a namespace the local context holds.
            return new ValueTask<bool>(
                !string.IsNullOrEmpty(namespaceUri) &&
                m_server.NamespaceUris.GetIndex(namespaceUri) >= 0);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
            string namespaceUri,
            string browseName,
            WotExpectedNodeClass expected,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(namespaceUri) || string.IsNullOrEmpty(browseName))
            {
                return ArrayOf<WotResolvedNode>.Empty;
            }

            IReadOnlyDictionary<string, List<WotResolvedNode>> index =
                await IndexAsync(cancellationToken).ConfigureAwait(false);
            if (!index.TryGetValue(
                Key(namespaceUri, browseName), out List<WotResolvedNode>? found))
            {
                return ArrayOf<WotResolvedNode>.Empty;
            }

            var matches = new List<WotResolvedNode>(found.Count);
            foreach (WotResolvedNode node in found)
            {
                if (expected == WotExpectedNodeClass.Any || node.NodeClass == expected)
                {
                    matches.Add(node);
                }
            }
            return new ArrayOf<WotResolvedNode>(matches.ToArray());
        }

        /// <inheritdoc/>
        public async ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
            string expandedNodeId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(expandedNodeId))
            {
                return null;
            }

            // NodeId implements INullable, so it is never wrapped in
            // System.Nullable; NodeId.Null signals "not translatable".
            NodeId nodeId = TryToLocalNodeId(expandedNodeId);
            if (nodeId.IsNull)
            {
                return null;
            }

            NodeClass? nodeClass = await TryGetNodeClassAsync(nodeId, cancellationToken)
                .ConfigureAwait(false);
            return nodeClass is null
                ? null
                : new WotResolvedNode(expandedNodeId, ToExpectedNodeClass(nodeClass.Value));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// The Server's own ReferenceTypes are the definitive answer for a
        /// relation a companion model states: a Server that has loaded the
        /// model holds every ReferenceType it defines, with the BrowseName and
        /// the InverseName OPC 10000-3 gives each. Nothing here is restricted
        /// to a fixed table - any ReferenceType the AddressSpace holds resolves
        /// by the same rules the base-namespace ones do.
        /// </para>
        /// <para>
        /// A name that is one ReferenceType's BrowseName and another's
        /// InverseName matches both; the caller settles that with
        /// <c>uav:refId</c> or reports it, because choosing here would assert a
        /// relation the document never chose.
        /// </para>
        /// </remarks>
        public async ValueTask<ArrayOf<WotResolvedReferenceType>> ResolveReferenceTypesAsync(
            string namespaceUri,
            string name,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(namespaceUri) || string.IsNullOrEmpty(name))
            {
                return ArrayOf<WotResolvedReferenceType>.Empty;
            }

            IReadOnlyDictionary<string, List<WotResolvedReferenceType>> index =
                await ReferenceTypeIndexAsync(cancellationToken).ConfigureAwait(false);
            return index.TryGetValue(
                Key(namespaceUri, name), out List<WotResolvedReferenceType>? found)
                ? new ArrayOf<WotResolvedReferenceType>(found.ToArray())
                : ArrayOf<WotResolvedReferenceType>.Empty;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// The declarations of a loaded type are read the way a Client would
        /// read them: browse the type's hierarchical children, read the
        /// Attributes each declaration carries, and follow <c>HasSubtype</c>
        /// upwards for the effective closure. Nothing is taken from a node
        /// manager's internal state, so any node manager implementation
        /// answers.
        /// </para>
        /// <para>
        /// The upward walk is bounded by
        /// <see cref="WotTypeDeclarations.MaxSupertypeDepth"/> and refuses to
        /// visit a type twice. A walk that is cut short reports an incomplete
        /// closure rather than a partial one presented as whole.
        /// </para>
        /// </remarks>
        public async ValueTask<WotTypeDeclarationSet?> ResolveDeclarationsAsync(
            string typeNodeId,
            WotDeclarationScope scope,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(typeNodeId))
            {
                return null;
            }
            NodeId typeId = TryToLocalNodeId(typeNodeId);
            if (typeId.IsNull ||
                await TryGetNodeClassAsync(typeId, cancellationToken).ConfigureAwait(false)
                    is not (NodeClass.ObjectType or NodeClass.VariableType))
            {
                return null;
            }

            var byName = new Dictionary<string, WotTypeDeclaration>(StringComparer.Ordinal);
            var supertypes = new List<string>();
            var visited = new HashSet<NodeId> { typeId };
            var faults = new List<string>();
            string? detail = null;
            NodeId current = typeId;
            bool inherited = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WotDeclarationRead read = await ReadDeclarationsAsync(
                    current, inherited, cancellationToken).ConfigureAwait(false);
                foreach (string fault in read.Faults)
                {
                    faults.Add(fault);
                }
                foreach (WotTypeDeclaration declaration in read.Declarations)
                {
                    string key = declaration.NamespaceUri + "\u0000" + declaration.BrowseName +
                        "\u0000" + ((int)declaration.Kind).ToString(
                            System.Globalization.CultureInfo.InvariantCulture);
                    if (!byName.ContainsKey(key))
                    {
                        byName[key] = declaration;
                    }
                }
                if (scope == WotDeclarationScope.Direct)
                {
                    break;
                }
                NodeId superType = m_server.TypeTree.FindSuperType(current);
                if (superType.IsNull)
                {
                    break;
                }
                if (!visited.Add(superType))
                {
                    detail =
                        $"The supertype chain revisits '{ToPortable(superType)}', so it is a " +
                        "cycle rather than a hierarchy.";
                    break;
                }
                if (supertypes.Count >= WotTypeDeclarations.MaxSupertypeDepth)
                {
                    detail =
                        "The supertype chain exceeded the maximum of " +
                        $"{WotTypeDeclarations.MaxSupertypeDepth} types.";
                    break;
                }
                supertypes.Add(ToPortable(superType));
                current = superType;
                inherited = true;
            }

            // A browse or a read the Server refused says nothing about what the
            // type declares. Reporting the part that answered as the whole
            // closure is what lets a member the type already declares be
            // projected as a second, differently-reached Node, and lets
            // uav:additionalProperties: false pass on the strength of nothing
            // having been consulted.
            if (faults.Count != 0)
            {
                detail = Combine(detail, faults);
            }

            var ordered = new List<WotTypeDeclaration>(byName.Values);
            ordered.Sort(WotTypeDeclarations.Compare);
            return new WotTypeDeclarationSet
            {
                TypeNodeId = typeNodeId,
                Declarations = ordered.ToArrayOf(),
                Supertypes = supertypes.ToArrayOf(),
                IsComplete = detail is null,
                Detail = detail
            };
        }

        /// <summary>
        /// Joins the reason a walk stopped and the faults it collected into one
        /// sentence, bounded so a Server failing every read cannot grow a
        /// detail without limit.
        /// </summary>
        private static string Combine(string? detail, List<string> faults)
        {
            var builder = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(detail))
            {
                builder.Append(detail).Append(' ');
            }
            int reported = Math.Min(faults.Count, MaxReportedFaults);
            for (int ii = 0; ii < reported; ii++)
            {
                builder.Append(faults[ii]).Append(' ');
            }
            if (faults.Count > reported)
            {
                builder
                    .Append('(')
                    .Append((faults.Count - reported).ToString(
                        System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" further failure(s) not listed.)");
            }
            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// Reads the instance declarations one type states itself, and every
        /// failure that kept one from being read.
        /// </summary>
        private async ValueTask<WotDeclarationRead> ReadDeclarationsAsync(
            NodeId typeId,
            bool inherited,
            CancellationToken cancellationToken)
        {
            var declarations = new List<WotTypeDeclaration>();
            var faults = new List<string>();
            string declaringType = ToPortable(typeId);

            // Materialised before the awaits below: ArrayOf<T> enumerates as a
            // span, which cannot be preserved across an await boundary.
            var children = new List<ReferenceDescription>();
            WotBrowseOutcome outcome = await BrowseChildrenAsync(typeId, cancellationToken)
                .ConfigureAwait(false);
            if (outcome.Failure is { } browseFailure)
            {
                faults.Add(
                    $"Browsing the children of '{declaringType}' failed: {browseFailure}");
            }
            foreach (ReferenceDescription reference in outcome.References)
            {
                children.Add(reference);
            }
            foreach (ReferenceDescription reference in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WotDeclarationKind kind = ToDeclarationKind(reference.NodeClass);
                if (kind == WotDeclarationKind.Unknown ||
                    reference.BrowseName.IsNull ||
                    string.IsNullOrEmpty(reference.BrowseName.Name))
                {
                    continue;
                }
                string? namespaceUri = m_server.NamespaceUris
                    .GetString(reference.BrowseName.NamespaceIndex);
                if (string.IsNullOrEmpty(namespaceUri))
                {
                    faults.Add(
                        $"The BrowseName of a child of '{declaringType}' names namespace " +
                        $"index {reference.BrowseName.NamespaceIndex}, which the Server's " +
                        "namespace table does not hold.");
                    continue;
                }
                NodeId declarationId = ExpandedNodeId.ToNodeId(
                    reference.NodeId, m_server.NamespaceUris);
                if (declarationId.IsNull)
                {
                    faults.Add(
                        $"The child '{reference.BrowseName.Name}' of '{declaringType}' has " +
                        "a NodeId this Server cannot translate.");
                    continue;
                }
                (WotTypeDeclaration? declaration, string? fault) = await ReadDeclarationAsync(
                    declarationId,
                    kind,
                    namespaceUri!,
                    reference.BrowseName.Name!,
                    ReferenceTypeName(reference.ReferenceTypeId),
                    reference.TypeDefinition,
                    declaringType,
                    inherited,
                    cancellationToken).ConfigureAwait(false);
                if (fault is not null)
                {
                    faults.Add(fault);
                }
                if (declaration is not null)
                {
                    declarations.Add(declaration);
                }
            }
            return new WotDeclarationRead(declarations, faults);
        }

        /// <summary>
        /// Reads the Attributes one declaration carries, and says what could
        /// not be read.
        /// </summary>
        /// <remarks>
        /// Only a Variable declaration carries a DataType, a ValueRank and
        /// ArrayDimensions, so only a Variable is asked for them. Asking a
        /// Method or an Object for a DataType would be answered
        /// <c>BadAttributeIdInvalid</c> - correctly - and reading that as a
        /// failure would make every Method declaration unreadable.
        /// </remarks>
        private async ValueTask<(WotTypeDeclaration? Declaration, string? Fault)>
            ReadDeclarationAsync(
            NodeId declarationId,
            WotDeclarationKind kind,
            string namespaceUri,
            string browseName,
            string referenceTypeName,
            ExpandedNodeId typeDefinition,
            string declaringType,
            bool inherited,
            CancellationToken cancellationToken)
        {
            string dataType = string.Empty;
            int valueRank = ValueRanks.Scalar;
            ArrayOf<uint> arrayDimensions = ArrayOf<uint>.Empty;
            string? attributeFault = null;
            if (kind == WotDeclarationKind.Variable)
            {
                (ArrayOf<DataValue> values, string? readFault) = await ReadAttributesAsync(
                    declarationId,
                    [
                        Opc.Ua.Attributes.DataType,
                        Opc.Ua.Attributes.ValueRank,
                        Opc.Ua.Attributes.ArrayDimensions
                    ],
                    cancellationToken,
                    Opc.Ua.Attributes.ArrayDimensions).ConfigureAwait(false);
                attributeFault = readFault is null
                    ? null
                    : $"Reading the Attributes of '{browseName}' on '{declaringType}' failed: " +
                        readFault;
                if (attributeFault is null && values.Count != 3)
                {
                    attributeFault =
                        $"The Server answered {values.Count} of the 3 Attributes of " +
                        $"'{browseName}' on '{declaringType}'.";
                }
                if (attributeFault is null)
                {
                    if (values[0].WrappedValue.TryGetValue(out NodeId declaredType) &&
                        !declaredType.IsNull)
                    {
                        dataType = ToPortable(declaredType);
                    }
                    if (values[1].WrappedValue.TryGetValue(out int rank))
                    {
                        valueRank = rank;
                    }
                    if (values[2].WrappedValue.TryGetValue(out ArrayOf<uint> dimensions))
                    {
                        arrayDimensions = dimensions;
                    }
                }
            }

            if (attributeFault is not null)
            {
                // What the declaration says a member holds is exactly what was
                // not read, so there is no declaration to populate: reporting
                // one would write the values a refused read leaves behind - no
                // DataType at all, and the scalar rank - onto the member as
                // though the type had stated them.
                return (null, attributeFault);
            }

            (WotModellingRule rule, string? ruleFault) = await ReadModellingRuleAsync(
                declarationId, cancellationToken).ConfigureAwait(false);
            string? fault = ruleFault is null
                ? null

                // The ModellingRule is what says whether an instance has to
                // carry the declaration at all, so not knowing it makes the
                // closure incomplete rather than optional by default. The
                // declaration is still reported: its ReferenceType, type
                // definition, DataType and ValueRank were read, and those are
                // what a member populates.
                : $"Reading the ModellingRule of '{browseName}' on '{declaringType}' " +
                    $"failed: {ruleFault}";

            return (
                new WotTypeDeclaration
                {
                    NamespaceUri = namespaceUri,
                    BrowseName = browseName,
                    Kind = kind,
                    DeclaringTypeNodeId = declaringType,
                    NodeId = ToPortable(declarationId),
                    ReferenceTypeName = referenceTypeName,
                    TypeDefinitionNodeId = typeDefinition.IsNull
                        ? string.Empty
                        : ToPortable(
                            ExpandedNodeId.ToNodeId(typeDefinition, m_server.NamespaceUris)),
                    MethodDeclarationNodeId = kind == WotDeclarationKind.Method
                        ? ToPortable(declarationId)
                        : string.Empty,
                    DataType = dataType,
                    ValueRank = valueRank,
                    ArrayDimensions = arrayDimensions,
                    ModellingRule = rule,
                    IsInherited = inherited
                },
                fault);
        }

        /// <summary>
        /// Reads the ModellingRule a declaration carries, which is what decides
        /// whether an instance has to have it.
        /// </summary>
        private async ValueTask<(WotModellingRule Rule, string? Fault)> ReadModellingRuleAsync(
            NodeId declarationId,
            CancellationToken cancellationToken)
        {
            WotBrowseOutcome outcome = await BrowseAsync(
                declarationId,
                Opc.Ua.ReferenceTypeIds.HasModellingRule,
                includeSubtypes: false,
                NodeClass.Object,
                cancellationToken).ConfigureAwait(false);
            if (outcome.Failure is { } failure)
            {
                return (WotModellingRule.None, failure);
            }
            foreach (ReferenceDescription rule in outcome.References)
            {
                NodeId ruleId = ExpandedNodeId.ToNodeId(rule.NodeId, m_server.NamespaceUris);
                if (ruleId.NamespaceIndex != 0 ||
                    !ruleId.TryGetValue(out uint identifier))
                {
                    continue;
                }
                WotModellingRule mapped = WotTypeDeclarations.FromModellingRuleId(
                    "i=" + identifier.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (mapped != WotModellingRule.None)
                {
                    return (mapped, null);
                }
            }
            return (WotModellingRule.None, null);
        }

        private ValueTask<WotBrowseOutcome> BrowseChildrenAsync(
            NodeId typeId,
            CancellationToken cancellationToken)
        {
            return BrowseAsync(
                typeId,
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                includeSubtypes: true,
                NodeClass.Object | NodeClass.Variable | NodeClass.Method,
                cancellationToken);
        }

        /// <summary>
        /// Browses one Node through the Server's own browse path, reporting the
        /// difference between "declares nothing" and "would not answer".
        /// </summary>
        private async ValueTask<WotBrowseOutcome> BrowseAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            bool includeSubtypes,
            NodeClass nodeClassMask,
            CancellationToken cancellationToken)
        {
            ArrayOf<BrowseDescription> nodesToBrowse =
            [
                new BrowseDescription
                {
                    NodeId = nodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = referenceTypeId,
                    IncludeSubtypes = includeSubtypes,
                    NodeClassMask = (uint)nodeClassMask,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ];
            try
            {
                using OperationContext context = CreateContext();
                (ArrayOf<BrowseResult> results, _) = await m_server.NodeManager.BrowseAsync(
                    context,
                    new ViewDescription(),
                    0,
                    nodesToBrowse,
                    cancellationToken).ConfigureAwait(false);
                if (results.Count == 0)
                {
                    return WotBrowseOutcome.Failed("the Server returned no browse result.");
                }
                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    return WotBrowseOutcome.Failed(
                        "the Server answered " + results[0].StatusCode.SymbolicId + ".");
                }
                return WotBrowseOutcome.Succeeded(results[0].References);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A node manager that refuses the browse said nothing about
                // what the Node has under it, which is not the same answer as
                // "nothing".
                return WotBrowseOutcome.Failed(ex.Message);
            }
        }

        /// <summary>
        /// Names the ReferenceType a declaration is reached through, in the
        /// form a NodeSet writes it.
        /// </summary>
        private string ReferenceTypeName(NodeId referenceTypeId)
        {
            QualifiedName browseName = m_server.TypeTree.FindReferenceTypeName(referenceTypeId);
            return browseName.IsNull || string.IsNullOrEmpty(browseName.Name)
                ? "HasComponent"
                : browseName.Name!;
        }

        private static WotDeclarationKind ToDeclarationKind(NodeClass nodeClass)
        {
            return nodeClass switch
            {
                NodeClass.Object => WotDeclarationKind.Object,
                NodeClass.Variable => WotDeclarationKind.Variable,
                NodeClass.Method => WotDeclarationKind.Method,
                _ => WotDeclarationKind.Unknown
            };
        }

        /// <summary>
        /// Builds the ReferenceType index on first use, keyed by each of the
        /// two names a ReferenceType answers to.
        /// </summary>
        /// <remarks>
        /// Two concurrent first callers may both build it, which is harmless
        /// for the same reason the type index is built this way: the hierarchy
        /// is settled before any document is converted, so both produce the
        /// same content, and no lock is held across the awaits the walk needs.
        /// </remarks>
        private async ValueTask<IReadOnlyDictionary<string, List<WotResolvedReferenceType>>>
            ReferenceTypeIndexAsync(CancellationToken cancellationToken)
        {
            Dictionary<string, List<WotResolvedReferenceType>>? index = m_referenceTypes;
            if (index is not null)
            {
                return index;
            }

            var built = new Dictionary<string, List<WotResolvedReferenceType>>(
                StringComparer.Ordinal);
            var pending = new Queue<NodeId>();
            var seen = new HashSet<NodeId>();
            pending.Enqueue(Opc.Ua.ReferenceTypeIds.References);
            seen.Add(Opc.Ua.ReferenceTypeIds.References);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NodeId current = pending.Dequeue();

                // Materialised before the await: ArrayOf<T> enumerates as a
                // span, which cannot be preserved across an await boundary.
                var subTypes = new List<NodeId>();
                foreach (NodeId subType in m_server.TypeTree.FindSubTypes(current))
                {
                    subTypes.Add(subType);
                }
                foreach (NodeId subType in subTypes)
                {
                    if (subType.IsNull || !seen.Add(subType))
                    {
                        continue;
                    }
                    pending.Enqueue(subType);
                    await IndexReferenceTypeAsync(subType, built, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            m_referenceTypes = built;
            return built;
        }

        /// <summary>
        /// Adds one ReferenceType to the index under its BrowseName and, unless
        /// it is symmetric, under its InverseName.
        /// </summary>
        private async ValueTask IndexReferenceTypeAsync(
            NodeId referenceTypeId,
            Dictionary<string, List<WotResolvedReferenceType>> index,
            CancellationToken cancellationToken)
        {
            ArrayOf<DataValue> values = (await ReadAttributesAsync(
                referenceTypeId,
                [
                    Opc.Ua.Attributes.NodeClass,
                    Opc.Ua.Attributes.BrowseName,
                    Opc.Ua.Attributes.InverseName,
                    Opc.Ua.Attributes.Symmetric
                ],
                cancellationToken).ConfigureAwait(false)).Values;
            if (values.Count != 4 ||
                !values[0].WrappedValue.TryGetValue(out int nodeClass) ||
                (NodeClass)nodeClass != NodeClass.ReferenceType ||
                !values[1].WrappedValue.TryGetValue(out QualifiedName browseName) ||
                browseName.IsNull ||
                string.IsNullOrEmpty(browseName.Name))
            {
                return;
            }
            string? namespaceUri = m_server.NamespaceUris.GetString(browseName.NamespaceIndex);
            if (string.IsNullOrEmpty(namespaceUri))
            {
                return;
            }

            string portable = ToPortable(referenceTypeId);
            AddReferenceTypeName(
                index, namespaceUri!, browseName.Name!, portable, browseName.Name!, true);

            bool isSymmetric =
                values[3].WrappedValue.TryGetValue(out bool symmetric) && symmetric;
            string? inverseName =
                values[2].WrappedValue.TryGetValue(out LocalizedText inverse)
                    ? inverse.Text
                    : null;
            if (!isSymmetric &&
                !string.IsNullOrEmpty(inverseName) &&
                !string.Equals(inverseName, browseName.Name, StringComparison.Ordinal))
            {
                AddReferenceTypeName(
                    index, namespaceUri!, inverseName!, portable, inverseName!, false);
            }
        }

        private static void AddReferenceTypeName(
            Dictionary<string, List<WotResolvedReferenceType>> index,
            string namespaceUri,
            string name,
            string nodeId,
            string matchedName,
            bool isForward)
        {
            string key = Key(namespaceUri, name);
            if (!index.TryGetValue(key, out List<WotResolvedReferenceType>? matches))
            {
                matches = [];
                index[key] = matches;
            }
            matches.Add(new WotResolvedReferenceType(nodeId, matchedName, isForward));
        }

        /// <summary>
        /// Reads a Node's attributes through the Server's own read path.
        /// </summary>
        /// <remarks>
        /// The InverseName and Symmetric Attributes are not part of the browse
        /// metadata, so they are read as Attributes. The Server's asynchronous
        /// read is used rather than a node manager's state objects, so the
        /// resolver holds no lock, never blocks on an asynchronous call, and
        /// works for any node manager implementation.
        /// </remarks>
        private async ValueTask<(ArrayOf<DataValue> Values, string? Fault)> ReadAttributesAsync(
            NodeId nodeId,
            uint[] attributes,
            CancellationToken cancellationToken,
            uint optionalAttribute = 0)
        {
            var nodesToRead = new ReadValueId[attributes.Length];
            for (int ii = 0; ii < attributes.Length; ii++)
            {
                nodesToRead[ii] = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = attributes[ii]
                };
            }
            try
            {
                using OperationContext context = CreateContext();
                (ArrayOf<DataValue> values, _) = await m_server.NodeManager.ReadAsync(
                    context,
                    0,
                    TimestampsToReturn.Neither,
                    new ArrayOf<ReadValueId>(nodesToRead),
                    cancellationToken).ConfigureAwait(false);

                // A Read answers per value, so a Server that would not state an
                // Attribute says so in that value's StatusCode rather than by
                // failing the call. The value it hands back with a Bad status
                // is the default of its type, which is indistinguishable from
                // an Attribute the Node really carries with that value.
                for (int ii = 0; ii < values.Count && ii < attributes.Length; ii++)
                {
                    if (StatusCode.IsBad(values[ii].StatusCode))
                    {
                        if (attributes[ii] == optionalAttribute &&
                            values[ii].StatusCode == StatusCodes.BadAttributeIdInvalid)
                        {
                            continue;
                        }
                        return (
                            values,
                            $"the Server answered the " +
                                $"{Opc.Ua.Attributes.GetBrowseName(attributes[ii])} Attribute " +
                                $"with {values[ii].StatusCode}.");
                    }
                }
                return (values, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A node manager that refuses the read said nothing about the
                // Node's Attributes, which is not the same answer as "they are
                // absent".
                return (ArrayOf<DataValue>.Empty, ex.Message);
            }
        }

        /// <summary>
        /// Builds the BrowseName index on first use.
        /// </summary>
        private async ValueTask<IReadOnlyDictionary<string, List<WotResolvedNode>>> IndexAsync(
            CancellationToken cancellationToken)
        {
            Dictionary<string, List<WotResolvedNode>>? index = m_index;
            if (index is not null)
            {
                return index;
            }

            // Two concurrent first callers may both build the index. That is
            // harmless - the type hierarchy is settled before any document is
            // converted, so both produce the same content - and it avoids
            // holding a lock across the awaits the walk needs, which in turn
            // keeps the resolver free of a disposable field whose ownership
            // the IWotNodeResolver contract does not model.
            var built = new Dictionary<string, List<WotResolvedNode>>(StringComparer.Ordinal);
            await AddSubTypesAsync(
                Opc.Ua.ObjectTypeIds.BaseObjectType, built, cancellationToken)
                .ConfigureAwait(false);
            await AddSubTypesAsync(
                Opc.Ua.VariableTypeIds.BaseVariableType, built, cancellationToken)
                .ConfigureAwait(false);
            m_index = built;
            return built;
        }

        /// <summary>
        /// Walks the subtypes of a root type, indexing each by its
        /// NamespaceUri-qualified BrowseName.
        /// </summary>
        private async ValueTask AddSubTypesAsync(
            NodeId rootTypeId,
            Dictionary<string, List<WotResolvedNode>> index,
            CancellationToken cancellationToken)
        {
            var pending = new Queue<NodeId>();
            var seen = new HashSet<NodeId>();
            pending.Enqueue(rootTypeId);
            seen.Add(rootTypeId);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NodeId current = pending.Dequeue();

                // Materialised before the await: ArrayOf<T> enumerates as a
                // span, which cannot be preserved across an await boundary.
                var subTypes = new List<NodeId>();
                foreach (NodeId subType in m_server.TypeTree.FindSubTypes(current))
                {
                    subTypes.Add(subType);
                }

                foreach (NodeId subType in subTypes)
                {
                    if (subType.IsNull || !seen.Add(subType))
                    {
                        continue;
                    }
                    pending.Enqueue(subType);
                    await IndexTypeAsync(subType, index, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Adds one type to the index, keyed by its qualified BrowseName.
        /// </summary>
        private async ValueTask IndexTypeAsync(
            NodeId typeId,
            Dictionary<string, List<WotResolvedNode>> index,
            CancellationToken cancellationToken)
        {
            NodeMetadata? metadata = await TryGetMetadataAsync(typeId, cancellationToken)
                .ConfigureAwait(false);
            if (metadata is null || metadata.BrowseName.IsNull)
            {
                return;
            }

            string? namespaceUri = m_server.NamespaceUris
                .GetString(metadata.BrowseName.NamespaceIndex);
            if (string.IsNullOrEmpty(namespaceUri) ||
                string.IsNullOrEmpty(metadata.BrowseName.Name))
            {
                return;
            }

            string key = Key(namespaceUri!, metadata.BrowseName.Name!);
            if (!index.TryGetValue(key, out List<WotResolvedNode>? bucket))
            {
                bucket = [];
                index[key] = bucket;
            }
            bucket.Add(new WotResolvedNode(
                ToPortable(typeId), ToExpectedNodeClass(metadata.NodeClass)));
        }

        /// <summary>
        /// Reads a node's metadata, or <c>null</c> when the Server does not
        /// hold it.
        /// </summary>
        private async ValueTask<NodeMetadata?> TryGetMetadataAsync(
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            try
            {
                (object? handle, IAsyncNodeManager? manager) = await m_server.NodeManager
                    .GetManagerHandleAsync(nodeId, cancellationToken).ConfigureAwait(false);
                if (handle is null || manager is null)
                {
                    return null;
                }
                using OperationContext context = CreateContext();
                return await manager.GetNodeMetadataAsync(
                    context,
                    handle,
                    BrowseResultMask.NodeClass | BrowseResultMask.BrowseName,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A node manager that refuses the lookup contributes no name
                // rather than failing every other resolution.
                return null;
            }
        }

        private async ValueTask<NodeClass?> TryGetNodeClassAsync(
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            NodeMetadata? metadata = await TryGetMetadataAsync(nodeId, cancellationToken)
                .ConfigureAwait(false);
            return metadata?.NodeClass;
        }

        /// <summary>
        /// Translates a portable ExpandedNodeId to this Server's local NodeId.
        /// </summary>
        private NodeId TryToLocalNodeId(string expandedNodeId)
        {
            try
            {
                var parsed = ExpandedNodeId.Parse(expandedNodeId, m_server.NamespaceUris);
                if (parsed.IsNull)
                {
                    return NodeId.Null;
                }
                return ExpandedNodeId.ToNodeId(parsed, m_server.NamespaceUris);
            }
            catch (Exception ex) when (ex is ServiceResultException or FormatException)
            {
                return NodeId.Null;
            }
        }

        /// <summary>
        /// Renders a local NodeId as the portable form of Section 5.1.1, which
        /// is what a type binding carries and what the converter parses back.
        /// Namespace 0 keeps its canonical form and needs no
        /// <c>nsu=</c> prefix.
        /// </summary>
        private string ToPortable(NodeId nodeId)
        {
            var buffer = new System.Text.StringBuilder();
            if (nodeId.NamespaceIndex != 0)
            {
                string? namespaceUri = m_server.NamespaceUris.GetString(nodeId.NamespaceIndex);
                if (!string.IsNullOrEmpty(namespaceUri))
                {
                    buffer.Append("nsu=")
                        .Append(CoreUtils.EscapeUri(namespaceUri!))
                        .Append(';');
                }
            }
            NodeId.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                buffer,
                nodeId.IdentifierAsString,
                nodeId.IdType,
                0);
            return buffer.ToString();
        }

        private static WotExpectedNodeClass ToExpectedNodeClass(NodeClass nodeClass)
        {
            return nodeClass switch
            {
                NodeClass.ObjectType => WotExpectedNodeClass.ObjectType,
                NodeClass.VariableType => WotExpectedNodeClass.VariableType,
                NodeClass.ReferenceType => WotExpectedNodeClass.ReferenceType,
                _ => WotExpectedNodeClass.Any
            };
        }

        private static OperationContext CreateContext()
        {
            return new OperationContext(
                new RequestHeader(), null, RequestType.Browse, RequestLifetime.None);
        }

        private static string Key(string namespaceUri, string browseName)
        {
            return namespaceUri + "\u0000" + browseName;
        }

        /// <summary>
        /// The result of one browse: the references, or the reason the Server
        /// would not answer. The two are different facts, and reporting the
        /// second as an empty first is what makes a Server that refuses a
        /// browse look like a type that declares nothing.
        /// </summary>
        private readonly struct WotBrowseOutcome
        {
            private WotBrowseOutcome(ArrayOf<ReferenceDescription> references, string? failure)
            {
                References = references;
                Failure = failure;
            }

            /// <summary>
            /// Gets the references the browse returned.
            /// </summary>
            public ArrayOf<ReferenceDescription> References { get; }

            /// <summary>
            /// Gets why the browse did not answer, or <c>null</c> when it did.
            /// </summary>
            public string? Failure { get; }

            public static WotBrowseOutcome Succeeded(ArrayOf<ReferenceDescription> references)
            {
                return new WotBrowseOutcome(references, null);
            }

            public static WotBrowseOutcome Failed(string failure)
            {
                return new WotBrowseOutcome(ArrayOf<ReferenceDescription>.Empty, failure);
            }
        }

        /// <summary>
        /// What one type in the walk contributed: the declarations that were
        /// read, and every failure that kept one from being read.
        /// </summary>
        private readonly struct WotDeclarationRead
        {
            public WotDeclarationRead(
                List<WotTypeDeclaration> declarations, List<string> faults)
            {
                Declarations = declarations;
                Faults = faults;
            }

            /// <summary>
            /// Gets the declarations that were read.
            /// </summary>
            public List<WotTypeDeclaration> Declarations { get; }

            /// <summary>
            /// Gets the failures that make the closure incomplete.
            /// </summary>
            public List<string> Faults { get; }
        }

        /// <summary>
        /// The number of failures a detail names before it summarizes the
        /// rest, so a Server refusing every read cannot grow one without
        /// bound.
        /// </summary>
        private const int MaxReportedFaults = 5;

        private readonly IServerInternal m_server;
        private volatile Dictionary<string, List<WotResolvedNode>>? m_index;
        private volatile Dictionary<string, List<WotResolvedReferenceType>>? m_referenceTypes;
    }
}
