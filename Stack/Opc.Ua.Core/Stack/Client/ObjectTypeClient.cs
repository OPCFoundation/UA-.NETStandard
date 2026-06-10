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
    /// telemetry) and exposes a single <see cref="CallMethodAsync"/>
    /// helper used by every generated wrapper.
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

            ArrayOf<CallMethodResult> results = response.Results;
            ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;

            ClientBase.ValidateResponse(results, requests);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, requests);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw ServiceResultException.Create(
                    results[0].StatusCode,
                    0,
                    diagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            return results[0].OutputArguments;
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
    }
}
