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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Adapts an <see cref="IPubSubKeyServiceServer"/> to the
    /// classic synchronous OPC UA NodeManager method-handler
    /// signature so it can be mounted on the
    /// <c>PubSubKeyServiceType.GetSecurityKeys</c> method node.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3.2">
    /// Part 14 §8.3.2 GetSecurityKeys</see>. The adapter and its
    /// tests are provided so the pipeline can be wired onto the
    /// address-space node without further changes to this class.
    /// </remarks>
    public sealed class SksMethodHandler
    {
        private readonly IPubSubKeyServiceServer m_keyService;
        private readonly ILogger m_logger;

        /// <summary>
        /// Initializes a new <see cref="SksMethodHandler"/>.
        /// </summary>
        /// <param name="keyService">Key-service implementation.</param>
        /// <param name="telemetry">Telemetry context.</param>
        public SksMethodHandler(
            IPubSubKeyServiceServer keyService,
            ITelemetryContext telemetry)
        {
            if (keyService is null)
            {
                throw new ArgumentNullException(nameof(keyService));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_keyService = keyService;
            m_logger = telemetry.CreateLogger<SksMethodHandler>();
        }

        /// <summary>
        /// Synchronously invokes
        /// <see cref="IPubSubKeyServiceServer.GetSecurityKeysAsync"/>
        /// and projects the result onto the spec-defined output
        /// argument vector
        /// <c>[SecurityPolicyUri, FirstTokenId, Keys, TimeToNextKey, KeyLifetime]</c>.
        /// </summary>
        /// <remarks>
        /// This is the single sanctioned sync-over-async bridge in
        /// the SKS surface: the legacy OPC UA NodeManager
        /// method-handler contract is synchronous. A future async
        /// node-manager API will replace this with a fully
        /// asynchronous handler.
        /// </remarks>
        /// <param name="context">System context.</param>
        /// <param name="objectId">
        /// NodeId of the Object the method is being called on.
        /// </param>
        /// <param name="inputArguments">Input argument list.</param>
        /// <param name="outputArguments">Output argument list.</param>
        /// <returns>Service result.</returns>
        public ServiceResult HandleGetSecurityKeys(
            ISystemContext context,
            NodeId objectId,
            IList<Variant> inputArguments,
            IList<Variant> outputArguments)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (inputArguments is null)
            {
                throw new ArgumentNullException(nameof(inputArguments));
            }
            if (outputArguments is null)
            {
                throw new ArgumentNullException(nameof(outputArguments));
            }
            _ = objectId;

            if (inputArguments.Count < 3)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText($"GetSecurityKeys expects 3 input arguments; got {inputArguments.Count}."));
            }
            if (!inputArguments[0].TryGetValue(out string? securityGroupId) ||
                string.IsNullOrEmpty(securityGroupId))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("GetSecurityKeys argument 0 (SecurityGroupId) is missing or not a String."));
            }
            if (!inputArguments[1].TryGetValue(out uint startingTokenId))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("GetSecurityKeys argument 1 (StartingTokenId) is missing or not a UInt32."));
            }
            if (!inputArguments[2].TryGetValue(out uint requestedKeyCount))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("GetSecurityKeys argument 2 (RequestedKeyCount) is missing or not a UInt32."));
            }

            string? callerIdentity = context.UserId;
            ArrayOf<NodeId> callerRoleIds = GetCallerRoleIds(context);
            var request = new SksKeyRequest(securityGroupId, startingTokenId, requestedKeyCount);

            SksKeyResponse response;
            try
            {
                response = m_keyService
                    .GetSecurityKeysAsync(callerIdentity ?? string.Empty, request, callerRoleIds)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OpcUaSksException ex)
            {
                m_logger.LogDebug(
                    ex,
                    "GetSecurityKeys for group {GroupId} returned {Status}.",
                    securityGroupId,
                    ex.Status);
                return new ServiceResult(ex.Status, new LocalizedText(ex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "GetSecurityKeys for group {GroupId} threw unexpectedly.",
                    securityGroupId);
                return new ServiceResult(
                    StatusCodes.BadInternalError,
                    new LocalizedText(ex.Message));
            }

            ByteString[] keys = new ByteString[response.Keys.Count];
            for (int i = 0; i < response.Keys.Count; i++)
            {
                byte[] entry = response.Keys[i] ?? Array.Empty<byte>();
                keys[i] = new ByteString(entry);
            }
            outputArguments.Add(Variant.From(response.SecurityPolicyUri));
            outputArguments.Add(Variant.From(response.FirstTokenId));
            outputArguments.Add(Variant.From((ArrayOf<ByteString>)keys));
            outputArguments.Add(Variant.From(response.TimeToNextKey.TotalMilliseconds));
            outputArguments.Add(Variant.From(response.KeyLifetime.TotalMilliseconds));
            return ServiceResult.Good;
        }

        private static ArrayOf<NodeId> GetCallerRoleIds(ISystemContext context)
        {
            if (context is ISessionSystemContext sessionSystemContext &&
                sessionSystemContext.UserIdentity is not null)
            {
                return sessionSystemContext.UserIdentity.GrantedRoleIds;
            }

            if (context is ISessionOperationContext sessionOperationContext)
            {
                return sessionOperationContext.UserIdentity.GrantedRoleIds;
            }

            return [];
        }
    }
}
