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
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Subscriptions.Streaming;
using StreamingMonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A typed observation that retains the original quality and both timestamps.
    /// </summary>
    /// <typeparam name="T">The exact generated/native UA value representation.</typeparam>
    /// <param name="Value">The generated-codec-decoded value; inspect HasValue before using it.</param>
    /// <param name="HasValue">Whether a non-null wire value could be decoded.</param>
    /// <param name="DataValue">The unmodified value, quality and source/server timestamps.</param>
    public readonly record struct ClientRead<T>(T Value, bool HasValue, DataValue DataValue);

    /// <summary>
    /// Typed variable operations attached to an existing generated ObjectType proxy.
    /// </summary>
    /// <remarks>
    /// Model-derived accessors supply exact codecs. No reflected conversion, copied wire types or
    /// second session are involved. The managed session remains responsible for transport, retry
    /// safety and subscriptions. Optional absence is reported as BadNotFound, not a fabricated default.
    /// </remarks>
    /// <typeparam name="T">The exact generated/native UA value representation.</typeparam>
    public sealed class ClientVariable<T>
    {
        /// <summary>
        /// Creates a model-derived variable view.
        /// </summary>
        public ClientVariable(
            ObjectTypeClient owner,
            string namespaceUri,
            string browseName,
            bool writable,
            Func<Variant, IServiceMessageContext, T> decode,
            Func<T, Variant> encode)
        {
            m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
            m_namespaceUri = namespaceUri ?? throw new ArgumentNullException(nameof(namespaceUri));
            m_browseName = browseName ?? throw new ArgumentNullException(nameof(browseName));
            Writable = writable;
            m_decode = decode ?? throw new ArgumentNullException(nameof(decode));
            m_encode = encode ?? throw new ArgumentNullException(nameof(encode));
        }

        /// <summary>
        /// Gets whether the model permits writes; the server still authorizes each actual request.
        /// </summary>
        public bool Writable { get; }

        /// <summary>
        /// Resolves this standard member in the current namespace table.
        /// </summary>
        public ValueTask<NodeId> ResolveAsync(CancellationToken cancellationToken = default)
        {
            return ClientModelAccess.ResolveAsync(m_owner, m_namespaceUri, m_browseName, cancellationToken);
        }

        /// <summary>
        /// Reads the value without throwing away bad quality or the authoritative source timestamp.
        /// </summary>
        public ValueTask<ClientRead<T>> ReadAsync(CancellationToken cancellationToken = default)
        {
            return ClientOperationContext.ExecuteAsync(m_owner.Session, ReadCoreAsync, cancellationToken);
        }

        /// <summary>
        /// Writes a model-permitted typed value through the caller's existing session.
        /// </summary>
        public ValueTask WriteAsync(T value, CancellationToken cancellationToken = default)
        {
            return ClientOperationContext.ExecuteAsync(
                m_owner.Session, token => WriteCoreAsync(value, token), cancellationToken);
        }

        /// <summary>
        /// Reads the native EUInformation property of an analog member.
        /// </summary>
        public ValueTask<EUInformation> ReadEngineeringUnitsAsync(CancellationToken cancellationToken = default)
        {
            return ClientOperationContext.ExecuteAsync(m_owner.Session, ReadEngineeringUnitsCoreAsync, cancellationToken);
        }

        /// <summary>
        /// Observes typed values through the existing bounded streaming subscription.
        /// Disposing the enumerator removes its monitored item, not the shared session.
        /// </summary>
        public async IAsyncEnumerable<ClientRead<T>> ObserveAsync(
            IStreamingSubscription? streaming = null,
            StreamingMonitoredItemOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId node = await RequireNodeAsync(cancellationToken).ConfigureAwait(false);
            IStreamingSubscription effective = streaming ??
                (m_owner.Session as ManagedSession)?.DefaultStreaming ??
                throw new InvalidOperationException("Supply a streaming subscription or use a ManagedSession.");
            await foreach (DataValueChange change in effective.SubscribeDataChangesAsync(
                node, options, cancellationToken).ConfigureAwait(false))
            {
                yield return Decode(change.Value);
            }
        }

        /// <summary>
        /// Validates the model-derived value before accepting the final observation.
        /// </summary>
        private async ValueTask<ClientRead<T>> ReadCoreAsync(CancellationToken cancellationToken)
        {
            NodeId node = await RequireNodeAsync(cancellationToken).ConfigureAwait(false);
            (ArrayOf<DataValue> values, ArrayOf<ServiceResult> _) = await m_owner.Session
                .ReadValuesAsync([node], cancellationToken).ConfigureAwait(false);
            if (values.Count != 1)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                    "A variable read returned no value.");
            }
            return Decode(values[0]);
        }

        /// <summary>
        /// Accepts a model-derived Write only after the ordinary status validation.
        /// </summary>
        private async ValueTask WriteCoreAsync(T value, CancellationToken cancellationToken)
        {
            if (!Writable)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotWritable,
                    "The model declares this value read-only.");
            }
            NodeId node = await RequireNodeAsync(cancellationToken).ConfigureAwait(false);
            ArrayOf<WriteValue> writes =
            [
                new WriteValue
                {
                    NodeId = node,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(m_encode(value))
                }
            ];
            WriteResponse response = await m_owner.Session.WriteAsync(null, writes,
                cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, writes);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, writes);
            if (StatusCode.IsBad(response.Results[0]))
            {
                throw ClientOperationContext.OriginalFailure(
                    m_owner.Session, new ServiceResultException(response.Results[0]));
            }
        }

        /// <summary>
        /// Validates the engineering unit structure within the caller's completion context.
        /// </summary>
        private async ValueTask<EUInformation> ReadEngineeringUnitsCoreAsync(CancellationToken cancellationToken)
        {
            NodeId node = await RequireNodeAsync(cancellationToken).ConfigureAwait(false);
            ISession session = ClientModelAccess.RequireSession(m_owner);
            NodeId units = await PrepareAsync(token => ClientModelAccess.ResolveAsync(session, node,
                Opc.Ua.Namespaces.OpcUa, Opc.Ua.BrowseNames.EngineeringUnits, token), cancellationToken)
                .ConfigureAwait(false);
            if (units.IsNull)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotFound, "The variable has no EngineeringUnits.");
            }
            DataValue value = await session.ReadValueAsync(units, cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                throw ClientOperationContext.OriginalFailure(session, new ServiceResultException(value.StatusCode));
            }
            if (!value.WrappedValue.TryGetValue<EUInformation>(out EUInformation? result, session.MessageContext) ||
                result == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadTypeMismatch,
                    "EngineeringUnits is not EUInformation.");
            }
            return result;
        }

        /// <summary>
        /// Applies the statically generated datatype decoder without reflecting over values.
        /// </summary>
        private ClientRead<T> Decode(in DataValue value)
        {
            if (value.WrappedValue.IsNull)
            {
                return new ClientRead<T>(default!, false, value);
            }
            try
            {
                return new ClientRead<T>(
                    m_decode(value.WrappedValue, m_owner.Session.MessageContext), true, value);
            }
            catch (ServiceResultException exception) when (
                StatusCode.IsBad(value.StatusCode) && exception.StatusCode == StatusCodes.BadTypeMismatch)
            {
                return new ClientRead<T>(default!, false, value);
            }
        }

        /// <summary>
        /// Distinguishes an omitted optional value from a successfully read default.
        /// </summary>
        private async ValueTask<NodeId> RequireNodeAsync(CancellationToken cancellationToken)
        {
            NodeId node = await PrepareAsync(ResolveAsync, cancellationToken).ConfigureAwait(false);
            return !node.IsNull ? node : throw ServiceResultException.Create(
                StatusCodes.BadNotFound, "The server does not expose optional member '{0}'.", m_browseName);
        }

        /// <summary>
        /// Resolves metadata through the normal session without creating a separate application receipt.
        /// </summary>
        private ValueTask<NodeId> PrepareAsync(
            Func<CancellationToken, ValueTask<NodeId>> prepare, CancellationToken ct)
        {
            return m_owner.Session is ManagedSession managed
                ? managed.PrepareClientOperationAsync(prepare, ct)
                : prepare(ct);
        }

        /// <summary>The model-generated parent proxy.</summary>
        private readonly ObjectTypeClient m_owner;

        /// <summary>The standard member namespace URI.</summary>
        private readonly string m_namespaceUri;

        /// <summary>The exact standard BrowseName.</summary>
        private readonly string m_browseName;

        /// <summary>The generated, statically typed decoder.</summary>
        private readonly Func<Variant, IServiceMessageContext, T> m_decode;

        /// <summary>The generated, statically typed encoder.</summary>
        private readonly Func<T, Variant> m_encode;
    }
}
