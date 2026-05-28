/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Di.Client
{
    /// <summary>
    /// Client-side wrapper for any
    /// <c>FiniteStateMachineState</c> instance accessible on the
    /// connected server. Reads the current state via the
    /// <c>CurrentState.Id</c> property; drives transitions by invoking
    /// the server-side cause methods bound to the state machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Event-based observation (<c>TransitionEventType</c> subscription)
    /// is not surfaced by this MVP — applications that need
    /// transition events can subscribe to <c>TransitionEventType</c>
    /// directly on the existing
    /// <see cref="ISession"/> and decode the
    /// <c>FromState</c> / <c>ToState</c> / <c>Transition</c> select
    /// clauses themselves.
    /// </para>
    /// </remarks>
    public sealed class StateMachineClient
    {
        /// <summary>
        /// Creates a new client rooted at the supplied state machine
        /// NodeId.
        /// </summary>
        public StateMachineClient(
            ISession session,
            NodeId stateMachineNodeId,
            ITelemetryContext telemetry)
        {
            if (session is null) { throw new ArgumentNullException(nameof(session)); }
            if (stateMachineNodeId.IsNull)
            {
                throw new ArgumentException(
                    "State machine NodeId is required.",
                    nameof(stateMachineNodeId));
            }
            if (telemetry is null) { throw new ArgumentNullException(nameof(telemetry)); }

            Session = session;
            StateMachineNodeId = stateMachineNodeId;
            Telemetry = telemetry;
        }

        /// <summary>The owning session.</summary>
        public ISession Session { get; }

        /// <summary>The NodeId of the state machine instance.</summary>
        public NodeId StateMachineNodeId { get; }

        /// <summary>Telemetry context.</summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Reads the current state identifier by translating the
        /// browse path <c>"CurrentState/Id"</c> below the state
        /// machine and reading the resulting Value attribute.
        /// </summary>
        /// <returns>
        /// The numeric portion of the current state NodeId (matches
        /// the <c>Objects.{TypeName}_{StateName}</c> constants the
        /// generator emits).
        /// </returns>
        public async ValueTask<uint> ReadCurrentStateAsync(CancellationToken ct = default)
        {
            BrowsePath path = new BrowsePath
            {
                StartingNode = StateMachineNodeId,
                RelativePath = new RelativePath
                {
                    Elements = new[]
                    {
                        new RelativePathElement
                        {
                            ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName("CurrentState")
                        },
                        new RelativePathElement
                        {
                            ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasProperty,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName("Id")
                        }
                    }
                }
            };

            TranslateBrowsePathsToNodeIdsResponse translate = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null, new[] { path }.ToArrayOf(), ct)
                .ConfigureAwait(false);

            if (translate.Results.Count == 0 ||
                StatusCode.IsBad(translate.Results[0].StatusCode) ||
                translate.Results[0].Targets.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "Could not resolve CurrentState/Id below the state machine.");
            }

            NodeId targetId = ExpandedNodeId.ToNodeId(
                translate.Results[0].Targets[0].TargetId,
                Session.NamespaceUris);

            ReadResponse read = await Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: new[]
                {
                    new ReadValueId
                    {
                        NodeId = targetId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                ct: ct).ConfigureAwait(false);

            if (read.Results.Count == 0 || StatusCode.IsBad(read.Results[0].StatusCode))
            {
                StatusCode code = read.Results.Count == 0
                    ? StatusCodes.BadUnexpectedError
                    : read.Results[0].StatusCode;
                throw new ServiceResultException(
                    code,
                    "Failed to read CurrentState/Id.");
            }

            NodeId stateNodeId = read.Results[0].WrappedValue.GetNodeId(NodeId.Null);
            if (stateNodeId.IsNull || !stateNodeId.TryGetValue(out uint numericId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    "CurrentState.Id was not a numeric NodeId.");
            }
            return numericId;
        }

        /// <summary>
        /// Invokes the cause method identified by
        /// <paramref name="methodNodeId"/> with no input arguments.
        /// Used to drive a state-machine transition from the client
        /// side.
        /// </summary>
        public async ValueTask InvokeCauseAsync(
            NodeId methodNodeId,
            CancellationToken ct = default)
        {
            if (methodNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Method NodeId is required.", nameof(methodNodeId));
            }

            CallMethodRequest request = new CallMethodRequest
            {
                ObjectId = StateMachineNodeId,
                MethodId = methodNodeId,
                InputArguments = Array.Empty<Variant>().ToArrayOf()
            };

            CallResponse response = await Session
                .CallAsync(
                    requestHeader: null,
                    methodsToCall: new[] { request }.ToArrayOf(),
                    ct: ct)
                .ConfigureAwait(false);

            if (response.Results.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode))
            {
                StatusCode code = response.Results.Count == 0
                    ? StatusCodes.BadUnexpectedError
                    : response.Results[0].StatusCode;
                throw new ServiceResultException(
                    code,
                    $"Cause method invocation failed with status {code}.");
            }
        }
    }
}
