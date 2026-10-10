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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Di.Client;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Adapts generated DI proxies for the few variable and AddIn accessors they do not expose.
    /// </summary>
    internal static class DiModelAccess
    {
        /// <summary>
        /// Resolves the current named session and requires the actual DI namespace.
        /// </summary>
        internal static ISession Session(OpcUaSessionManager manager, string? sessionName)
        {
            ISession session = manager.GetSessionOrThrow(sessionName);
            if (session.NamespaceUris.GetIndex(Di.Namespaces.OpcUaDi) < 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The server does not expose DI.");
            }
            return session;
        }

        /// <summary>
        /// Verifies a topology root before calling helpers that treat a vanished root as an empty list.
        /// </summary>
        internal static async ValueTask RequireObjectAsync(ISession session, NodeId nodeId, CancellationToken ct)
        {
            Node node = await session.ReadNodeAsync(nodeId, NodeClass.Unspecified, false, ct).ConfigureAwait(false);
            if (node.NodeClass != NodeClass.Object)
            {
                throw new ServiceResultException(StatusCodes.BadNodeClassInvalid, "A DI object NodeId is required.");
            }
        }

        /// <summary>
        /// Resolves an actual local instance child, preserving denied access and malformed responses.
        /// Generated DI proxies do not provide variable accessors or the TransferServices/SoftwareUpdate AddIns.
        /// </summary>
        internal static async ValueTask<NodeId> ResolveChildAsync(
            ObjectTypeClient proxy,
            string browseName,
            CancellationToken ct)
        {
            int namespaceIndex = proxy.Session.MessageContext.NamespaceUris.GetIndex(Di.Namespaces.OpcUaDi);
            if (namespaceIndex < 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The server does not expose DI.");
            }
            ArrayOf<BrowsePath> paths =
            [
                new BrowsePath
                {
                    StartingNode = proxy.ObjectId,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(browseName, (ushort)namespaceIndex)
                            }
                        ]
                    }
                }
            ];
            TranslateBrowsePathsToNodeIdsResponse response = await proxy.Session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths, ct).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, paths);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, paths);
            BrowsePathResult result = response.Results[0];
            if (result.StatusCode == StatusCodes.BadNoMatch || result.StatusCode == StatusCodes.BadNotFound)
            {
                return NodeId.Null;
            }
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }
            if (result.Targets.Count == 0)
            {
                return NodeId.Null;
            }
            if (result.Targets.Count != 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadBrowseNameDuplicated, $"The DI child '{browseName}' is ambiguous.");
            }
            BrowsePathTarget target = result.Targets[0];
            NodeId child = ExpandedNodeId.ToNodeId(target.TargetId, proxy.Session.MessageContext.NamespaceUris);
            if (target.RemainingPathIndex != uint.MaxValue || target.TargetId.ServerIndex != 0 || child.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "A fully resolved local DI child is required.");
            }
            return child;
        }

        /// <summary>
        /// Reads a known variable on a typed proxy, retaining its UA value type, quality and timestamps.
        /// The generic DiDeviceClient property helper discards bad quality; this adapter does not.
        /// </summary>
        internal static async ValueTask<JsonObject> ReadValueAsync(
            ObjectTypeClient proxy,
            string browseName,
            CancellationToken ct)
        {
            NodeId nodeId = await ResolveChildAsync(proxy, browseName, ct).ConfigureAwait(false);
            JsonObject result = DiJson.Facet(nodeId);
            if (!nodeId.IsNull)
            {
                DataValue value = await proxy.Session.ReadValueAsync(nodeId, ct).ConfigureAwait(false);
                if (StatusCode.IsBad(value.StatusCode))
                {
                    throw new ServiceResultException(value.StatusCode);
                }
                result["dataValue"] = McpCompanionJson.DataValue(value, proxy.Session.MessageContext);
            }
            return result;
        }

        /// <summary>
        /// Dispatches a finite state-machine selector through the existing software-update client.
        /// </summary>
        internal static ValueTask<FiniteStateSnapshot?> StateAsync(
            SoftwareUpdateClient client,
            DiSoftwareStateMachine stateMachine,
            CancellationToken ct)
        {
            return stateMachine switch
            {
                DiSoftwareStateMachine.PrepareForUpdate => client.GetPrepareForUpdateStateAsync(ct),
                DiSoftwareStateMachine.Installation => client.GetInstallationStateAsync(ct),
                DiSoftwareStateMachine.Confirmation => client.GetConfirmationStateAsync(ct),
                DiSoftwareStateMachine.PowerCycle => client.GetPowerCycleStateAsync(ct),
                _ => throw new ArgumentOutOfRangeException(nameof(stateMachine))
            };
        }
    }
}
