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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Shared abstract base class for all source-generated OPC UA
    /// <c>ObjectType</c> client proxies.
    /// </summary>
    /// <remarks>
    /// Each generated <c>*TypeClient</c> derives from the proxy of its
    /// parent ObjectType (forming a chain that mirrors the OPC UA
    /// inheritance tree); proxies for types that derive directly from
    /// <c>BaseObjectType</c> ultimately inherit from this class. The base
    /// class holds the per-instance plumbing (session, object NodeId,
    /// telemetry) and exposes the <c>CallMethodAsync</c> helpers used by
    /// every generated wrapper.
    /// </remarks>
    public abstract class ObjectTypeClient
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="ObjectTypeClient"/> class.
        /// </summary>
        /// <param name="session">
        /// The OPC UA session used to invoke methods on the wrapped
        /// object. Must not be <c>null</c>.
        /// </param>
        /// <param name="objectId">
        /// The NodeId of the Object instance whose methods this proxy
        /// forwards. May be <c>null</c> if the derived class resolves the
        /// NodeId lazily.
        /// </param>
        /// <param name="telemetry">
        /// Telemetry context for diagnostics. Must not be <c>null</c>.
        /// </param>
        protected ObjectTypeClient(
            ISessionClient session,
            NodeId objectId,
            ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            ObjectId = objectId;
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Gets the underlying session used to invoke methods.
        /// </summary>
        public ISessionClient Session { get; }

        /// <summary>
        /// Gets the NodeId of the wrapped Object instance.
        /// </summary>
        public NodeId ObjectId { get; }

        /// <summary>
        /// Gets the telemetry context used for diagnostics.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Calls the method identified by <paramref name="methodId"/> on
        /// the wrapped object and returns the raw output arguments. Used
        /// by every generated proxy method.
        /// </summary>
        /// <param name="methodId">The NodeId of the method to invoke.</param>
        /// <param name="ct">Cancellation token for the request.</param>
        /// <param name="args">The boxed input arguments.</param>
        /// <returns>The output arguments returned by the server.</returns>
        /// <exception cref="ServiceResultException">
        /// Thrown if the call fails or returns a Bad status.
        /// </exception>
        protected async ValueTask<ArrayOf<Variant>> CallMethodAsync(
            NodeId methodId,
            CancellationToken ct,
            params Variant[] args)
        {
            CallResponse response = await CallOnceAsync(methodId, ct, args)
                .ConfigureAwait(false);
            return ThrowOrGetOutputArguments(response);
        }

        /// <summary>
        /// Calls the method identified by <paramref name="methodId"/> on
        /// the wrapped object and returns the raw output arguments, with
        /// an interoperability fallback for non-conformant servers.
        /// </summary>
        /// <remarks>
        /// The <paramref name="methodId"/> is the type-declaration
        /// MethodId (the Method on the ObjectType). Per OPC UA Part 4
        /// (v1.04 §5.11.2.2 / v1.05.07 §5.12.2.2) a Call on an Object
        /// instance may use <b>either</b> the instance MethodId <b>or</b>
        /// the type-declaration MethodId, so this is the spec-conformant
        /// happy path and conformant servers (including this stack's own
        /// server) accept it. Some non-conformant servers only bind the
        /// method handler on the instance and reject the type-declaration
        /// MethodId with <see cref="StatusCodes.BadMethodInvalid"/>. To
        /// interoperate with those servers, the instance MethodId is
        /// resolved once via a <c>HasComponent</c> browse path, cached on
        /// this proxy, and the call is retried. Subsequent calls reuse the
        /// cached instance MethodId, so conformant servers pay no extra
        /// cost.
        /// </remarks>
        /// <param name="methodId">The type-declaration NodeId of the
        /// method to invoke.</param>
        /// <param name="methodNamespaceUri">The namespace URI of the
        /// method's browse name, used for the fallback resolution.</param>
        /// <param name="methodBrowseName">The unqualified browse name of
        /// the method, used for the fallback resolution.</param>
        /// <param name="ct">Cancellation token for the request.</param>
        /// <param name="args">The boxed input arguments.</param>
        /// <returns>The output arguments returned by the server.</returns>
        /// <exception cref="ServiceResultException">
        /// Thrown if the call fails or returns a Bad status.
        /// </exception>
        protected async ValueTask<ArrayOf<Variant>> CallMethodAsync(
            NodeId methodId,
            string methodNamespaceUri,
            string methodBrowseName,
            CancellationToken ct,
            params Variant[] args)
        {
            // Reuse a previously resolved instance MethodId if we already
            // had to fall back for this method against a non-conformant
            // server; otherwise start with the type-declaration MethodId.
            bool usedTypeMethodId = !m_instanceMethodIdCache.TryGetValue(
                methodId,
                out NodeId callMethodId);
            if (usedTypeMethodId)
            {
                callMethodId = methodId;
            }

            CallResponse response = await CallOnceAsync(callMethodId, ct, args)
                .ConfigureAwait(false);

            // Interoperability fallback: a conformant server accepts the
            // type-declaration MethodId, but a non-conformant one returns
            // Bad_MethodInvalid. Resolve the instance MethodId via
            // HasComponent, cache it, and retry once.
            if (usedTypeMethodId &&
                response.Results[0].StatusCode.CodeBits == StatusCodes.BadMethodInvalid)
            {
                NodeId instanceMethodId = await ResolveChildNodeIdAsync(
                    methodNamespaceUri,
                    methodBrowseName,
                    ct).ConfigureAwait(false);

                if (!instanceMethodId.IsNull && !instanceMethodId.Equals(methodId))
                {
                    m_instanceMethodIdCache[methodId] = instanceMethodId;
                    response = await CallOnceAsync(instanceMethodId, ct, args)
                        .ConfigureAwait(false);
                }
            }

            return ThrowOrGetOutputArguments(response);
        }

        /// <summary>
        /// Resolves the NodeId of a HasComponent-referenced Object
        /// child of the wrapped object via
        /// <c>TranslateBrowsePathsToNodeIds</c>. Used by every
        /// source-generated typed Object-child accessor (e.g.
        /// <c>AlarmConditionTypeClient.GetShelvingStateAsync</c>).
        /// Returns <see cref="NodeId.Null"/> when the server does not
        /// expose the child (Optional children, unknown namespace,
        /// BadNotFound).
        /// </summary>
        /// <param name="namespaceUri">The namespace URI in which the
        /// child's browse name lives.</param>
        /// <param name="browseName">The unqualified child browse
        /// name.</param>
        /// <param name="ct">Cancellation token.</param>
        protected async ValueTask<NodeId> ResolveChildNodeIdAsync(
            string namespaceUri,
            string browseName,
            CancellationToken ct = default)
        {
            int nsIdx = Session.MessageContext.NamespaceUris.GetIndex(namespaceUri);
            if (nsIdx < 0)
            {
                return NodeId.Null;
            }
            var paths = ArrayOf.Wrapped(
            [
                new BrowsePath
                {
                    StartingNode = ObjectId,
                    RelativePath = new RelativePath
                    {
                        Elements = ArrayOf.Wrapped(
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(browseName, (ushort)nsIdx)
                            }
                        ])
                    }
                }
            ]);

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(null, paths, ct)
                    .ConfigureAwait(false);
            if (response.Results.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode) ||
                response.Results[0].Targets.Count == 0)
            {
                return NodeId.Null;
            }
            return ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId,
                Session.MessageContext.NamespaceUris);
        }

        /// <summary>
        /// Issues a single <c>Call</c> for the wrapped object and returns
        /// the validated response without throwing on a Bad operation
        /// status, so callers can inspect the status and optionally retry.
        /// </summary>
        /// <param name="methodId">The NodeId of the method to invoke.</param>
        /// <param name="ct">Cancellation token for the request.</param>
        /// <param name="args">The boxed input arguments.</param>
        private async ValueTask<CallResponse> CallOnceAsync(
            NodeId methodId,
            CancellationToken ct,
            params Variant[] args)
        {
            var request = new CallMethodRequest
            {
                ObjectId = ObjectId,
                MethodId = methodId,
                InputArguments = args
            };

            ArrayOf<CallMethodRequest> requests = [request];

            CallResponse response = await Session.CallAsync(
                null,
                requests,
                ct).ConfigureAwait(false);

            ClientBase.ValidateResponse(response.Results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);

            return response;
        }

        /// <summary>
        /// Returns the output arguments of the first (and only) Call
        /// result, or throws a <see cref="ServiceResultException"/> when
        /// the operation status is Bad.
        /// </summary>
        /// <param name="response">The validated Call response.</param>
        private static ArrayOf<Variant> ThrowOrGetOutputArguments(CallResponse response)
        {
            CallMethodResult result = response.Results[0];

            if (StatusCode.IsBad(result.StatusCode))
            {
                throw ServiceResultException.Create(
                    result.StatusCode,
                    0,
                    response.DiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            return result.OutputArguments;
        }

        /// <summary>
        /// Per-proxy cache mapping a type-declaration MethodId to the
        /// instance MethodId resolved via the interoperability fallback.
        /// </summary>
        private readonly ConcurrentDictionary<NodeId, NodeId> m_instanceMethodIdCache = new();
    }
}
