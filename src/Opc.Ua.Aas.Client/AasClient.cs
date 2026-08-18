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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Aas.Client
{
    /// <summary>
    /// High-level client for the OPC UA AAS V3 metamodel.
    /// </summary>
    /// <remarks>
    /// The client computes the deterministic String NodeIds of clause 6.1.3 from AAS identifiers and
    /// <c>idShortPath</c> values instead of browsing for addressed resources.
    /// </remarks>
    /// <example>
    /// <code>
    /// var client = new AasClient(session, aasNamespaceIndex, telemetry);
    /// AASPropertyTypeClient property = client.OpenProperty("urn:submodel", "temperature");
    /// AasValueReadResult value = await client.ReadValueAsync(property.ObjectId, ct);
    /// await client.WriteLexicalValueAsync(property.ObjectId, "42", ct);
    /// </code>
    /// </example>
    public sealed class AasClient
    {
        /// <summary>
        /// Creates a metamodel client over an open OPC UA session.
        /// </summary>
        /// <param name="session">The OPC UA session.</param>
        /// <param name="instanceNamespaceIndex">The namespace index that contains materialized AAS instance nodes.</param>
        /// <param name="telemetry">Telemetry context used by generated proxies.</param>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> or <paramref name="telemetry"/> is <c>null</c>.</exception>
        public AasClient(ISession session, ushort instanceNamespaceIndex, ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            Session = session;
            InstanceNamespaceIndex = instanceNamespaceIndex;
            Telemetry = telemetry;
        }

        /// <summary>
        /// The OPC UA session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Namespace index that contains materialized AAS instance nodes.
        /// </summary>
        public ushort InstanceNamespaceIndex { get; }

        /// <summary>
        /// Telemetry context used by generated proxies.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Computes the NodeId of an AAS shell from its identifier and opens a generated proxy.
        /// </summary>
        /// <example>
        /// <code>
        /// AASTypeClient shell = client.OpenShell("https://example.org/aas/1");
        /// </code>
        /// </example>
        public AASTypeClient OpenShell(string id)
        {
            return new AASTypeClient(Session, CreateShellNodeId(id), Telemetry);
        }

        /// <summary>
        /// Computes the NodeId of a submodel from its identifier and opens a generated proxy.
        /// </summary>
        /// <example>
        /// <code>
        /// AASSubmodelTypeClient submodel = client.OpenSubmodel("urn:example:submodel");
        /// </code>
        /// </example>
        public AASSubmodelTypeClient OpenSubmodel(string id)
        {
            return new AASSubmodelTypeClient(Session, CreateSubmodelNodeId(id), Telemetry);
        }

        /// <summary>
        /// Computes the NodeId of a concept description from its identifier and opens a generated proxy.
        /// </summary>
        public AASConceptDescriptionTypeClient OpenConceptDescription(string id)
        {
            return new AASConceptDescriptionTypeClient(Session, CreateConceptDescriptionNodeId(id), Telemetry);
        }

        /// <summary>
        /// Computes the NodeId of a submodel element from the owning identifier and <c>idShortPath</c>.
        /// </summary>
        /// <example>
        /// <code>
        /// AASSubmodelElementTypeClient element = client.OpenSubmodelElement("urn:submodel", "items[0].name");
        /// </code>
        /// </example>
        public AASSubmodelElementTypeClient OpenSubmodelElement(string ownerId, string idShortPath)
        {
            return new AASSubmodelElementTypeClient(
                Session,
                CreateSubmodelElementNodeId(ownerId, idShortPath),
                Telemetry);
        }

        /// <summary>
        /// Computes the NodeId of a property element and opens a generated proxy.
        /// </summary>
        public AASPropertyTypeClient OpenProperty(string ownerId, string idShortPath)
        {
            return new AASPropertyTypeClient(
                Session,
                CreateSubmodelElementNodeId(ownerId, idShortPath),
                Telemetry);
        }

        /// <summary>
        /// Computes the NodeId of an operation element and opens a generated proxy.
        /// </summary>
        public AASOperationTypeClient OpenOperation(string ownerId, string idShortPath)
        {
            return new AASOperationTypeClient(
                Session,
                CreateSubmodelElementNodeId(ownerId, idShortPath),
                Telemetry);
        }

        /// <summary>
        /// Creates a shell NodeId from an AAS identifier.
        /// </summary>
        public NodeId CreateShellNodeId(string id)
        {
            return CreateIdentifiableNodeId(AasNodeKind.Shell, id);
        }

        /// <summary>
        /// Creates a submodel NodeId from an AAS identifier.
        /// </summary>
        public NodeId CreateSubmodelNodeId(string id)
        {
            return CreateIdentifiableNodeId(AasNodeKind.Submodel, id);
        }

        /// <summary>
        /// Creates a concept description NodeId from an AAS identifier.
        /// </summary>
        public NodeId CreateConceptDescriptionNodeId(string id)
        {
            return CreateIdentifiableNodeId(AasNodeKind.ConceptDescription, id);
        }

        /// <summary>
        /// Creates a submodel element NodeId from an owner identifier and <c>idShortPath</c>.
        /// </summary>
        public NodeId CreateSubmodelElementNodeId(string ownerId, string idShortPath)
        {
            return new NodeId(
                AasNodeIdEncoding.CreateElementId(ownerId, idShortPath),
                InstanceNamespaceIndex);
        }

        /// <summary>
        /// Browses an AAS environment folder for shells, submodels and concept descriptions.
        /// </summary>
        public ValueTask<ArrayOf<AasBrowseEntry>> BrowseEnvironmentAsync(
            NodeId environmentNodeId,
            CancellationToken ct = default)
        {
            return BrowseChildrenAsync(
                environmentNodeId,
                ReferenceTypeIds.Organizes,
                includeSubtypes: true,
                nodeClassMask: (uint)NodeClass.Object,
                ct);
        }

        /// <summary>
        /// Browses a shell for its submodel reference nodes.
        /// </summary>
        public ValueTask<ArrayOf<AasBrowseEntry>> BrowseShellSubmodelReferencesAsync(
            NodeId shellNodeId,
            CancellationToken ct = default)
        {
            return BrowseChildrenAsync(
                shellNodeId,
                ReferenceTypeIds.HasComponent,
                includeSubtypes: true,
                nodeClassMask: 0,
                ct);
        }

        /// <summary>
        /// Browses a submodel for its direct submodel elements.
        /// </summary>
        public ValueTask<ArrayOf<AasBrowseEntry>> BrowseSubmodelElementsAsync(
            NodeId submodelNodeId,
            CancellationToken ct = default)
        {
            return BrowseChildrenAsync(
                submodelNodeId,
                ReferenceTypeIds.HasComponent,
                includeSubtypes: true,
                nodeClassMask: (uint)NodeClass.Object,
                ct);
        }

        /// <summary>
        /// Browses a submodel element list and returns its members ordered by their <c>Index</c> Property.
        /// </summary>
        public async ValueTask<ArrayOf<AasBrowseEntry>> BrowseListElementsAsync(
            NodeId listNodeId,
            CancellationToken ct = default)
        {
            ArrayOf<AasBrowseEntry> entries = await BrowseChildrenAsync(
                listNodeId,
                ReferenceTypeIds.HasComponent,
                includeSubtypes: true,
                nodeClassMask: (uint)NodeClass.Object,
                ct).ConfigureAwait(false);

            var indexed = new List<(AasBrowseEntry Entry, int Index, int BrowseOrder)>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                int index = await ReadIndexAsync(entries[i].NodeId, ct).ConfigureAwait(false);
                indexed.Add((entries[i], index, i));
            }

            indexed.Sort(static (left, right) =>
            {
                int byIndex = left.Index.CompareTo(right.Index);
                return byIndex != 0 ? byIndex : left.BrowseOrder.CompareTo(right.BrowseOrder);
            });

            var ordered = new AasBrowseEntry[indexed.Count];
            for (int i = 0; i < indexed.Count; i++)
            {
                ordered[i] = indexed[i].Entry;
            }

            return ordered.ToArrayOf();
        }

        /// <summary>
        /// Reads an element's <c>Value</c> Variable as both raw OPC UA value and canonical xsd lexical form.
        /// </summary>
        public async ValueTask<AasValueReadResult> ReadValueAsync(
            NodeId elementNodeId,
            CancellationToken ct = default)
        {
            NodeId valueNodeId = await ResolveChildAsync(elementNodeId, "Value", ct).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadAttributesAsync(
                valueNodeId,
                Attributes.Value,
                Attributes.DataType,
                ct).ConfigureAwait(false);

            ThrowIfBad(values[0].StatusCode, "Reading the AAS Value failed.");
            ThrowIfBad(values[1].StatusCode, "Reading the AAS Value DataType failed.");

            if (!values[1].WrappedValue.TryGetValue(out NodeId dataTypeId) ||
                !AasXsdTypeMap.TryGetValueType(dataTypeId, Session.NamespaceUris, out AASDataTypeDefXsdDataType valueType))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    "The Value Variable DataType is not an AAS xsd type mapping.");
            }

            Variant rawValue = values[0].WrappedValue;
            if (!AasLexicalCanonicalizer.TryCanonicalize(rawValue, valueType, out string? lexical, out string? error))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    error ?? "The Value Variable could not be canonicalized.");
            }

            return new AasValueReadResult(elementNodeId, valueNodeId, valueType, rawValue, lexical ?? string.Empty);
        }

        /// <summary>
        /// Writes an element's <c>Value</c> Variable from an xsd lexical form using the declared type.
        /// </summary>
        public async ValueTask<StatusCode> WriteLexicalValueAsync(
            NodeId elementNodeId,
            string lexical,
            CancellationToken ct = default)
        {
            AasValueReadResult current = await ReadValueAsync(elementNodeId, ct).ConfigureAwait(false);
            if (!AasLexicalCanonicalizer.TryParse(lexical, current.ValueType, out Variant value, out string? error))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    error ?? "The lexical value is not valid for the declared xsd type.");
            }

            return await WriteRawValueAsync(current.ValueNodeId, value, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes an element's <c>Value</c> Variable from a raw OPC UA value.
        /// </summary>
        public async ValueTask<StatusCode> WriteValueAsync(
            NodeId elementNodeId,
            Variant value,
            CancellationToken ct = default)
        {
            NodeId valueNodeId = await ResolveChildAsync(elementNodeId, "Value", ct).ConfigureAwait(false);
            return await WriteRawValueAsync(valueNodeId, value, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Invokes an AAS operation and keeps the OPC UA Call StatusCode separate from the AAS success flag.
        /// </summary>
        /// <example>
        /// <code>
        /// AasOperationInvokeResult result = await client.InvokeAsync(
        ///     operation.ObjectId, inputs, inoutputs, 0, ct);
        /// if (StatusCode.IsGood(result.CallStatusCode) &amp;&amp; !result.Success)
        /// {
        ///     Console.WriteLine(result.Diagnostic);
        /// }
        /// </code>
        /// </example>
        public async ValueTask<AasOperationInvokeResult> InvokeAsync(
            NodeId operationNodeId,
            ArrayOf<Variant> inputValues,
            ArrayOf<Variant> inoutputValues,
            double clientTimeout,
            CancellationToken ct = default)
        {
            var operation = new AASOperationTypeClient(Session, operationNodeId, Telemetry);
            try
            {
                (ArrayOf<Variant> outputValues, ArrayOf<Variant> inoutputResults, bool success, string diagnostic) =
                    await operation.InvokeAsync(inputValues, inoutputValues, clientTimeout, ct)
                        .ConfigureAwait(false);
                return new AasOperationInvokeResult(
                    StatusCodes.Good,
                    outputValues,
                    inoutputResults,
                    success,
                    diagnostic ?? string.Empty);
            }
            catch (ServiceResultException ex)
            {
                return new AasOperationInvokeResult(
                    ex.StatusCode,
                    ArrayOf<Variant>.Empty,
                    ArrayOf<Variant>.Empty,
                    false,
                    ex.Message);
            }
        }

        private NodeId CreateIdentifiableNodeId(AasNodeKind kind, string id)
        {
            return new NodeId(
                AasNodeIdEncoding.CreateIdentifiableId(kind, id),
                InstanceNamespaceIndex);
        }

        private async ValueTask<ArrayOf<AasBrowseEntry>> BrowseChildrenAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            CancellationToken ct)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("A NodeId is required.", nameof(nodeId));
            }

            var entries = new List<AasBrowseEntry>();
            ByteString continuationPoint = default;
            do
            {
                ArrayOf<ReferenceDescription> references;
                if (continuationPoint.IsNull)
                {
                    (_, continuationPoint, references) = await Session.BrowseAsync(
                        requestHeader: null,
                        view: null,
                        nodeId,
                        maxResultsToReturn: 0,
                        BrowseDirection.Forward,
                        referenceTypeId,
                        includeSubtypes,
                        nodeClassMask,
                        ct).ConfigureAwait(false);
                }
                else
                {
                    (_, continuationPoint, references) = await Session.BrowseNextAsync(
                        requestHeader: null,
                        releaseContinuationPoint: false,
                        continuationPoint,
                        ct).ConfigureAwait(false);
                }

                for (int i = 0; i < references.Count; i++)
                {
                    ReferenceDescription reference = references[i];
                    NodeId targetId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                    entries.Add(new AasBrowseEntry(targetId, reference.BrowseName, reference.DisplayName));
                }
            }
            while (!continuationPoint.IsNull);

            return entries.ToArrayOf();
        }

        private async ValueTask<NodeId> ResolveChildAsync(
            NodeId parentNodeId,
            string browseName,
            CancellationToken ct)
        {
            // Aggregates covers HasComponent, HasOrderedComponent and
            // HasProperty while excluding HasSubtype. A materialized member is
            // reached by whichever of those the clause 6.1.6 mapping chose, so
            // browsing HasComponent alone misses every Property - including the
            // Value Variable of an element.
            ArrayOf<AasBrowseEntry> children = await BrowseChildrenAsync(
                parentNodeId,
                ReferenceTypeIds.Aggregates,
                includeSubtypes: true,
                nodeClassMask: 0,
                ct).ConfigureAwait(false);
            for (int i = 0; i < children.Count; i++)
            {
                if (string.Equals(children[i].BrowseName.Name, browseName, StringComparison.Ordinal))
                {
                    return children[i].NodeId;
                }
            }

            throw new ServiceResultException(
                StatusCodes.BadNoMatch,
                string.Format(CultureInfo.InvariantCulture, "Child '{0}' was not found.", browseName));
        }

        private async ValueTask<int> ReadIndexAsync(NodeId elementNodeId, CancellationToken ct)
        {
            try
            {
                NodeId indexNodeId = await ResolveChildAsync(elementNodeId, "Index", ct).ConfigureAwait(false);
                ArrayOf<DataValue> values = await ReadAttributesAsync(
                    indexNodeId,
                    Attributes.Value,
                    ct).ConfigureAwait(false);
                if (values.Count == 0 || StatusCode.IsBad(values[0].StatusCode))
                {
                    return int.MaxValue;
                }
                Variant value = values[0].WrappedValue;
                if (value.TryGetValue(out int intValue))
                {
                    return intValue;
                }
                if (value.TryGetValue(out uint uintValue) && uintValue <= int.MaxValue)
                {
                    return (int)uintValue;
                }
                return int.MaxValue;
            }
            catch (ServiceResultException)
            {
                return int.MaxValue;
            }
        }

        private ValueTask<ArrayOf<DataValue>> ReadAttributesAsync(
            NodeId nodeId,
            uint firstAttributeId,
            CancellationToken ct)
        {
            return ReadAttributesAsync(nodeId, firstAttributeId, 0, ct);
        }

        private async ValueTask<ArrayOf<DataValue>> ReadAttributesAsync(
            NodeId nodeId,
            uint firstAttributeId,
            uint secondAttributeId,
            CancellationToken ct)
        {
            var nodesToRead = new List<ReadValueId>
            {
                new() { NodeId = nodeId, AttributeId = firstAttributeId }
            };
            if (secondAttributeId != 0)
            {
                nodesToRead.Add(new ReadValueId { NodeId = nodeId, AttributeId = secondAttributeId });
            }

            ReadResponse response = await Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: nodesToRead.ToArrayOf(),
                ct: ct).ConfigureAwait(false);
            return response.Results;
        }

        private async ValueTask<StatusCode> WriteRawValueAsync(
            NodeId valueNodeId,
            Variant value,
            CancellationToken ct)
        {
            var nodesToWrite = new WriteValue[]
            {
                new()
                {
                    NodeId = valueNodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(value)
                }
            }.ToArrayOf();

            WriteResponse response = await Session.WriteAsync(
                requestHeader: null,
                nodesToWrite: nodesToWrite,
                ct: ct).ConfigureAwait(false);
            return response.Results.Count == 0 ? StatusCodes.BadUnexpectedError : response.Results[0];
        }

        private static void ThrowIfBad(StatusCode statusCode, string message)
        {
            if (StatusCode.IsBad(statusCode))
            {
                throw new ServiceResultException(statusCode, message);
            }
        }
    }
}
