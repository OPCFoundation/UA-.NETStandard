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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.AutoID.Client
{
    /// <summary>
    /// Client-side helpers for the OPC 30010 AutoID Devices companion specification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A reader is a Device Integration device below <c>DeviceSet</c>.
    /// <see cref="EnumerateReadersAsync"/> finds the devices whose type derives from one of the five
    /// concrete reader types; the other members take the resulting <see cref="AutoIdReaderEntry"/>
    /// and run the typed methods on the generated ObjectType proxies.
    /// </para>
    /// <para>
    /// Failures are reported, not hidden: a browse the server rejects throws a
    /// <see cref="ServiceResultException"/> rather than yielding an empty list, a method call the
    /// server rejects throws with the server's status (for example <c>Bad_InvalidState</c> while a
    /// scan is active), and cancellation is honoured by every asynchronous member. The status a
    /// device reports for a tag command (for example <c>PASSWORD_ERROR</c>) is data, not an
    /// exception: it is returned to the caller.
    /// </para>
    /// <para>
    /// The client borrows the session; it neither opens nor closes it.
    /// </para>
    /// </remarks>
    public sealed partial class AutoIdClient
    {
        /// <summary>
        /// Creates an AutoID client over a connected session and registers the AutoID encodeables
        /// with the session's factory.
        /// </summary>
        /// <param name="session">The connected session, borrowed by the client.</param>
        /// <param name="telemetry">The telemetry context used for logging.</param>
        public AutoIdClient(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            IServiceMessageContext messageContext = session.MessageContext ??
                throw new ArgumentException("The session must provide a message context.", nameof(session));
            // Two clients created at once on one session must not both register the types.
            lock (s_registrationLock)
            {
                if (!messageContext.Factory.ContainsEncodeableType(DataTypeIds.ScanSettings))
                {
                    messageContext.Factory.Builder.AddAutoIdTypeSystem().Commit();
                }
            }
        }

        /// <summary>
        /// Gets the connected session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets whether the server publishes the OPC 30010 namespace.
        /// </summary>
        public bool IsSupported => Session.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri) >= 0;

        /// <summary>
        /// Gets the NodeId of the DI <c>DeviceSet</c>, or <see cref="NodeId.Null"/> when the server
        /// does not publish the Device Integration namespace.
        /// </summary>
        public NodeId DeviceSetId
        {
            get
            {
                int index = Session.NamespaceUris.GetIndex(Di.Namespaces.OpcUaDi);
                return index < 0 ? NodeId.Null : new NodeId(Di.Objects.DeviceSet, (ushort)index);
            }
        }

        /// <summary>
        /// Enumerates the readers directly below the DI <c>DeviceSet</c>.
        /// </summary>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <exception cref="ServiceResultException">The server rejected the browse.</exception>
        public IAsyncEnumerable<AutoIdReaderEntry> EnumerateReadersAsync(
            CancellationToken cancellationToken = default)
        {
            return EnumerateReadersUnderAsync(DeviceSetId, cancellationToken);
        }

        /// <summary>
        /// Enumerates the readers directly below <paramref name="root"/>.
        /// </summary>
        /// <remarks>
        /// A child qualifies when its type definition is, or derives from, one of
        /// <c>RfidReaderDeviceType</c>, <c>OcrReaderDeviceType</c>, <c>OpticalReaderDeviceType</c>,
        /// <c>OpticalVerifierDeviceType</c> and <c>RtlsDeviceType</c>, so vendor subtypes are found
        /// too. A vendor type that derives from the abstract <c>AutoIdDeviceType</c> directly is not
        /// enumerated; use <see cref="Device"/> for it.
        /// </remarks>
        /// <param name="root">The folder or device to look in.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <exception cref="ServiceResultException">The server rejected the browse.</exception>
        public async IAsyncEnumerable<AutoIdReaderEntry> EnumerateReadersUnderAsync(
            NodeId root,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (root.IsNull || !IsSupported)
            {
                yield break;
            }
            ArrayOf<ReferenceDescription> references = await BrowseChildrenAsync(
                root,
                NodeClass.Object,
                cancellationToken).ConfigureAwait(false);
            for (int index = 0; index < references.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReferenceDescription reference = references[index];
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                var typeId = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
                if (nodeId.IsNull || typeId.IsNull)
                {
                    continue;
                }
                AutoIdReaderKind? kind = await KindOfAsync(typeId, cancellationToken).ConfigureAwait(false);
                if (kind is { } found)
                {
                    yield return new AutoIdReaderEntry(
                        nodeId,
                        reference.BrowseName,
                        reference.DisplayName.Text ?? string.Empty,
                        typeId,
                        found);
                }
            }
        }

        /// <summary>
        /// Resolves a reader by its NodeId: reads its type definition and the reader kind it derives from.
        /// </summary>
        /// <param name="readerId">The NodeId of the device.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <exception cref="ServiceResultException">
        /// The server rejected the browse, or the node is not one of the five reader kinds
        /// (<c>Bad_TypeMismatch</c>).
        /// </exception>
        /// <exception cref="ArgumentException"></exception>
        public async ValueTask<AutoIdReaderEntry> GetReaderAsync(
            NodeId readerId,
            CancellationToken cancellationToken = default)
        {
            if (readerId.IsNull)
            {
                throw new ArgumentException("A reader NodeId is required.", nameof(readerId));
            }
            ArrayOf<ReferenceDescription> references = await BrowseReferencesAsync(
                readerId,
                BrowseDirection.Forward,
                Types.ReferenceTypeIds.HasTypeDefinition,
                NodeClass.ObjectType,
                cancellationToken).ConfigureAwait(false);
            if (references.Count == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "The node {0} has no type definition.",
                    readerId);
            }
            var typeId = ExpandedNodeId.ToNodeId(references[0].NodeId, Session.NamespaceUris);
            AutoIdReaderKind? kind = await KindOfAsync(typeId, cancellationToken).ConfigureAwait(false);
            if (kind is not { } found)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "The node {0} is not an RFID, OCR, optical, optical verifier or RTLS reader.",
                    readerId);
            }
            ReadResponse names = await Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: new[]
                {
                    new ReadValueId { NodeId = readerId, AttributeId = Attributes.BrowseName },
                    new ReadValueId { NodeId = readerId, AttributeId = Attributes.DisplayName }
                }.ToArrayOf(),
                ct: cancellationToken).ConfigureAwait(false);
            if (names.Results.Count != 2)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "The server returned {0} results for two attributes.",
                    names.Results.Count);
            }
            if (StatusCode.IsBad(names.Results[0].StatusCode))
            {
                throw ServiceResultException.Create(
                    names.Results[0].StatusCode,
                    "Reading the browse name of {0} failed.",
                    readerId);
            }
            names.Results[0].WrappedValue.TryGetValue(out QualifiedName browseName);
            names.Results[1].WrappedValue.TryGetValue(out LocalizedText displayName);
            return new AutoIdReaderEntry(readerId, browseName, displayName.Text ?? string.Empty, typeId, found);
        }

        /// <summary>
        /// Creates the generated proxy of the abstract <c>AutoIdDeviceType</c> for a device,
        /// for the base methods <c>Scan</c> (with the abstract result type), <c>ScanStart</c>,
        /// <c>ScanStop</c> and <c>GetDeviceLocation</c>, and for vendor types.
        /// </summary>
        /// <param name="readerId">The NodeId of the device.</param>
        public AutoIdDeviceTypeClient Device(NodeId readerId)
        {
            return new AutoIdDeviceTypeClient(Session, readerId, Telemetry);
        }

        /// <summary>
        /// Gets the NodeId of an ObjectType of the AutoID namespace as the session knows it.
        /// </summary>
        private NodeId TypeId(ExpandedNodeId typeId)
        {
            return ExpandedNodeId.ToNodeId(typeId, Session.NamespaceUris);
        }

        /// <summary>
        /// Finds the reader kind a type definition derives from, most specific first.
        /// </summary>
        private async ValueTask<AutoIdReaderKind?> KindOfAsync(NodeId typeId, CancellationToken cancellationToken)
        {
            // The verifier derives from the optical reader, so it has to be asked first.
            foreach (AutoIdReaderKind kind in s_kindsMostSpecificFirst)
            {
                NodeId candidate = TypeId(AutoIdReaderKinds.TypeIdOf(kind));
                if (candidate.IsNull)
                {
                    continue;
                }
                if (await Session.NodeCache.IsTypeOfAsync(typeId, candidate, cancellationToken)
                    .ConfigureAwait(false))
                {
                    return kind;
                }
            }
            return null;
        }

        /// <summary>
        /// Browses the hierarchical children of a node. A bad status for the node is thrown, not
        /// reported as an empty list.
        /// </summary>
        private Task<ArrayOf<ReferenceDescription>> BrowseChildrenAsync(
            NodeId parent,
            NodeClass nodeClassMask,
            CancellationToken cancellationToken)
        {
            return BrowseReferencesAsync(
                parent,
                BrowseDirection.Forward,
                Types.ReferenceTypeIds.HierarchicalReferences,
                nodeClassMask,
                cancellationToken);
        }

        private async Task<ArrayOf<ReferenceDescription>> BrowseReferencesAsync(
            NodeId node,
            BrowseDirection direction,
            NodeId referenceType,
            NodeClass nodeClassMask,
            CancellationToken cancellationToken)
        {
            (ArrayOf<ArrayOf<ReferenceDescription>> results, ArrayOf<ServiceResult> errors) =
                await Session.ManagedBrowseAsync(
                    null,
                    null,
                    new[] { node }.ToArrayOf(),
                    0,
                    direction,
                    referenceType,
                    true,
                    (uint)nodeClassMask,
                    cancellationToken).ConfigureAwait(false);
            if (errors.Count > 0 && ServiceResult.IsBad(errors[0]))
            {
                throw new ServiceResultException(errors[0]);
            }
            if (results.Count == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "The browse of {0} returned no result.",
                    node);
            }
            return results[0];
        }

        private static ArrayOf<ScanResult> Upcast<T>(ArrayOf<T> results)
            where T : ScanResult
        {
            var all = new ScanResult[results.Count];
            for (int index = 0; index < all.Length; index++)
            {
                all[index] = results[index];
            }
            return all.ToArrayOf();
        }

        private static readonly Lock s_registrationLock = new();

        private static readonly AutoIdReaderKind[] s_kindsMostSpecificFirst =
        [
            AutoIdReaderKind.OpticalVerifier,
            AutoIdReaderKind.Optical,
            AutoIdReaderKind.Ocr,
            AutoIdReaderKind.Rfid,
            AutoIdReaderKind.Rtls
        ];
    }
}
