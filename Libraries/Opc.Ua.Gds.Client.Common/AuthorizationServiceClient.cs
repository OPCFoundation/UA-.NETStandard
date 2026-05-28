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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Client surface for the OPC 10000-12 §9 AuthorizationService.
    /// Wraps the generated <see cref="AuthorizationServiceTypeClient"/>
    /// proxy and provides a session-connected experience.
    /// </summary>
    public sealed class AuthorizationServiceClient
    {
        private readonly ISession m_session;
        private readonly NodeId m_serviceNodeId;
        private readonly AuthorizationServiceTypeClient m_proxy;
        private readonly Dictionary<string, NodeId> m_methodIds = new(StringComparer.Ordinal);

        /// <summary>
        /// Creates a client targeting a specific AuthorizationService
        /// instance on the connected session.
        /// </summary>
        /// <param name="session">A connected OPC UA session.</param>
        /// <param name="serviceNodeId">
        /// The NodeId of the AuthorizationService object instance.
        /// </param>
        public AuthorizationServiceClient(ISession session, NodeId serviceNodeId)
        {
            m_session = session;
            m_serviceNodeId = serviceNodeId;
            m_proxy = new AuthorizationServiceTypeClient(
                session,
                serviceNodeId,
                session.MessageContext.Telemetry);
        }

        /// <summary>Returns the service description.</summary>
        public ValueTask<(
            string serviceUri,
            ByteString serviceCertificate,
            ArrayOf<UserTokenPolicy> userTokenPolicies)> GetServiceDescriptionAsync(
            CancellationToken ct = default)
        {
            return m_proxy.GetServiceDescriptionAsync(ct);
        }

        /// <summary>Requests an access token using the legacy single-call flow.</summary>
        [Obsolete("Use StartRequestTokenAsync + FinishRequestTokenAsync for Part 12 v1.05 compliance.")]
        public ValueTask<string> RequestAccessTokenAsync(
            UserIdentityToken identityToken,
            string resourceId,
            CancellationToken ct = default)
        {
            return m_proxy.RequestAccessTokenAsync(identityToken, resourceId, ct);
        }

        /// <summary>Starts the Part 12 v1.05 two-phase token request flow.</summary>
        public async ValueTask<(ByteString serviceData, Guid requestId)> StartRequestTokenAsync(
            string resourceId,
            string policyId,
            ByteString requestorData,
            CancellationToken ct = default)
        {
            ArrayOf<Variant> outputArguments = await CallInstanceMethodAsync(
                "StartRequestToken",
                ct,
                new Variant(resourceId),
                new Variant(policyId),
                new Variant(requestorData)).ConfigureAwait(false);

            return (GetByteStringOutput(outputArguments, 0), GetGuidOutput(outputArguments, 1));
        }

        /// <summary>Completes the Part 12 v1.05 two-phase token request flow.</summary>
        public async ValueTask<(
            string accessToken,
            DateTime accessTokenExpiryTime,
            string? refreshToken,
            DateTime refreshTokenExpiryTime)> FinishRequestTokenAsync(
                Guid requestId,
                ArrayOf<string> requestedRoles,
                UserIdentityToken userIdentityToken,
                SignatureData userTokenSignature,
                CancellationToken ct = default)
        {
            ArrayOf<Variant> outputArguments = await CallInstanceMethodAsync(
                "FinishRequestToken",
                ct,
                new Variant((Uuid)requestId),
                new Variant(requestedRoles),
                Variant.FromStructure(userIdentityToken),
                Variant.FromStructure(userTokenSignature)).ConfigureAwait(false);

            string refreshToken = GetOutput<string>(outputArguments, 2);
            return (
                GetOutput<string>(outputArguments, 0),
                GetDateTimeOutput(outputArguments, 1),
                string.IsNullOrEmpty(refreshToken) ? null : refreshToken,
                GetDateTimeOutput(outputArguments, 3));
        }

        /// <summary>Refreshes the access token using a previously-issued refresh token (RC).</summary>
        public async ValueTask<(
            string accessToken,
            DateTime accessTokenExpiryTime,
            string? newRefreshToken,
            DateTime newRefreshTokenExpiryTime)> RefreshTokenAsync(
                string resourceId,
                string currentRefreshToken,
                CancellationToken ct = default)
        {
            ArrayOf<Variant> outputArguments = await CallInstanceMethodAsync(
                "RefreshToken",
                ct,
                new Variant(resourceId),
                new Variant(currentRefreshToken)).ConfigureAwait(false);

            string newRefreshToken = GetOutput<string>(outputArguments, 2);
            return (
                GetOutput<string>(outputArguments, 0),
                GetDateTimeOutput(outputArguments, 1),
                string.IsNullOrEmpty(newRefreshToken) ? null : newRefreshToken,
                GetDateTimeOutput(outputArguments, 3));
        }

        private async ValueTask<ArrayOf<Variant>> CallInstanceMethodAsync(
            string browseName,
            CancellationToken ct,
            params Variant[] inputArguments)
        {
            NodeId methodId = await GetMethodIdAsync(browseName, ct).ConfigureAwait(false);
            var request = new CallMethodRequest
            {
                ObjectId = m_serviceNodeId,
                MethodId = methodId,
                InputArguments = inputArguments
            };
            ArrayOf<CallMethodRequest> requests = [request];
            CallResponse response = await m_session.CallAsync(null, requests, ct).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);

            if (StatusCode.IsBad(response.Results[0].StatusCode))
            {
                throw ServiceResultException.Create(
                    response.Results[0].StatusCode,
                    0,
                    response.DiagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            return response.Results[0].OutputArguments;
        }

        private async ValueTask<NodeId> GetMethodIdAsync(string browseName, CancellationToken ct)
        {
            if (m_methodIds.TryGetValue(browseName, out NodeId methodId))
            {
                return methodId;
            }

            BrowseResponse response = await m_session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[] {
                    new() {
                        NodeId = m_serviceNodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Method,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);

            ArrayOf<BrowseDescription> browseDescriptions = new BrowseDescription[] {
                new() { NodeId = m_serviceNodeId }
            }.ToArrayOf();
            ClientBase.ValidateResponse(response.Results, browseDescriptions);
            ReferenceDescription[] references = response.Results[0].References.ToArray()!;
            ReferenceDescription? method = references.FirstOrDefault(reference => string.Equals(
                reference.BrowseName.Name,
                browseName,
                StringComparison.Ordinal));
            if (method == null)
            {
                methodId = GetKnownMethodId(browseName);
                m_methodIds[browseName] = methodId;
                return methodId;
            }

            methodId = ExpandedNodeId.ToNodeId(method.NodeId, m_session.NamespaceUris);
            if (methodId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "AuthorizationService method '{0}' could not be resolved.",
                    browseName);
            }

            m_methodIds[browseName] = methodId;
            return methodId;
        }

        private NodeId GetKnownMethodId(string browseName)
        {
            ushort namespaceIndex = m_serviceNodeId.NamespaceIndex;
            return browseName switch
            {
                "GetServiceDescription" => new NodeId(Methods.AuthorizationServiceType_GetServiceDescription, namespaceIndex),
                "RequestAccessToken" => new NodeId(Methods.AuthorizationServiceType_RequestAccessToken, namespaceIndex),
                "StartRequestToken" => new NodeId(Methods.AuthorizationServiceType_StartRequestToken, namespaceIndex),
                "FinishRequestToken" => new NodeId(Methods.AuthorizationServiceType_FinishRequestToken, namespaceIndex),
                "RefreshToken" => new NodeId(Methods.AuthorizationServiceType_RefreshToken, namespaceIndex),
                _ => throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "AuthorizationService method '{0}' was not found.",
                    browseName)
            };
        }

        private static T GetOutput<T>(ArrayOf<Variant> outputArguments, int index)
        {
            object? boxed = outputArguments[index].AsBoxedObject(Variant.BoxingBehavior.Legacy);
            if (boxed is T typed)
            {
                return typed;
            }
            if (boxed == null)
            {
                return default!;
            }

            return (T)Convert.ChangeType(boxed, typeof(T), CultureInfo.InvariantCulture)!;
        }

        private static ByteString GetByteStringOutput(ArrayOf<Variant> outputArguments, int index)
        {
            if (outputArguments[index].TryGetValue(out ByteString byteString))
            {
                return byteString;
            }

            object? boxed = outputArguments[index].AsBoxedObject(Variant.BoxingBehavior.Legacy);
            return boxed is byte[] bytes ? ByteString.From(bytes) : default;
        }

        private static Guid GetGuidOutput(ArrayOf<Variant> outputArguments, int index)
        {
            if (outputArguments[index].TryGetValue(out Uuid requestId))
            {
                return requestId;
            }
            object? boxed = outputArguments[index].AsBoxedObject(Variant.BoxingBehavior.Legacy);
            return boxed is Guid guid ? guid : GetOutput<Guid>(outputArguments, index);
        }

        private static DateTime GetDateTimeOutput(ArrayOf<Variant> outputArguments, int index)
        {
            if (outputArguments[index].TryGetValue(out DateTimeUtc dateTimeUtc))
            {
                return (DateTime)dateTimeUtc;
            }

            object? boxed = outputArguments[index].AsBoxedObject(Variant.BoxingBehavior.Legacy);
            return boxed is DateTime dateTime ? dateTime : GetOutput<DateTime>(outputArguments, index);
        }
    }
}
