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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Client;
using Opc.Ua.XRegistry;
using WotTypes = Opc.Ua.WotCon.ObjectTypeIds;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Asset information to enumerate during inspection.
    /// </summary>
    public enum WotAssetFacet
    {
        /// <summary>
        /// Property nodes exposed by the asset.
        /// </summary>
        Properties,

        /// <summary>
        /// Action methods and their argument metadata.
        /// </summary>
        Actions,

        /// <summary>
        /// Event types declared by the asset's object type.
        /// </summary>
        Events
    }

    /// <summary>
    /// WoT-specific root resolution and typed client construction on the current session.
    /// </summary>
    internal static class WotAccess
    {
        /// <summary>
        /// Gets the selected session without creating another connection.
        /// </summary>
        internal static ISession Session(OpcUaSessionManager manager, string? name)
        {
            ISession session = manager.GetSessionOrThrow(name);
            _ = McpModelAccess.Name(session, WotCon.Namespaces.WotCon, "WoTRegistry");
            RegisterTypes(session);
            return session;
        }

        /// <summary>
        /// Resolves the two independent entry points, including explicitly supplied renamed roots.
        /// </summary>
        internal static async ValueTask<NodeId> RootAsync(
            ISession session, bool registry, string? explicitNodeId, CancellationToken ct)
        {
            if (session.NamespaceUris.GetIndex(WotCon.Namespaces.WotCon) < 0)
            {
                return NodeId.Null;
            }
            RegisterTypes(session);
            NodeId node = explicitNodeId is not null
                ? Node(explicitNodeId)
                : await McpModelAccess.ChildAsync(session, registry ? ObjectIds.Server : ObjectIds.ObjectsFolder,
                    McpModelAccess.Name(session, WotCon.Namespaces.WotCon,
                        registry ? "WoTRegistry" : "WoTAssetConnectionManagement"), ct).ConfigureAwait(false);
            if (!node.IsNull)
            {
                await McpModelAccess.RequireTypeAsync(session, node,
                    registry ? WotTypes.WoTRegistryType : WotTypes.WoTAssetConnectionManagementType, ct)
                    .ConfigureAwait(false);
            }
            return node;
        }

        /// <summary>
        /// Creates the legacy asset-management client after checking the actual root.
        /// </summary>
        internal static async ValueTask<WotConnectivityClient> ManagementAsync(
            OpcUaSessionManager manager, string? sessionName, string? managementNodeId, CancellationToken ct)
        {
            ISession session = Session(manager, sessionName);
            NodeId root = await RootAsync(session, false, managementNodeId, ct).ConfigureAwait(false);
            if (root.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The server does not expose legacy WoT asset management.");
            }
            return new WotConnectivityClient(session, root, manager.Telemetry);
        }

        /// <summary>
        /// Reads WoT-qualified properties on a known node.
        /// </summary>
        internal static ValueTask<JsonObject> PropertiesAsync(
            ISession session, NodeId node, ArrayOf<string> names, CancellationToken ct)
        {
            return McpModelAccess.PropertiesAsync(session, node,
                names.ConvertAll(name => McpModelAccess.Name(session, WotCon.Namespaces.WotCon, name)), ct);
        }

        /// <summary>
        /// Enumerates an asset facet with enough identity/argument information for the standard core tools.
        /// </summary>
        internal static async IAsyncEnumerable<JsonObject> AffordancesAsync(
            WotAssetClient asset, WotAssetFacet facet, [EnumeratorCancellation] CancellationToken ct)
        {
            if (facet == WotAssetFacet.Events)
            {
                NodeId type = await McpModelAccess.TypeDefinitionAsync(asset.Session, asset.AssetId, ct)
                    .ConfigureAwait(false);
                await foreach (ReferenceDescription reference in asset.Session.EnumerateReferencesAsync(
                    type, ReferenceTypeIds.GeneratesEvent, nodeClassMask: (uint)NodeClass.ObjectType, ct: ct)
                    .ConfigureAwait(false))
                {
                    yield return McpModelAccess.Reference(asset.Session, reference);
                }
                yield break;
            }
            IAsyncEnumerable<WotAssetVariableEntry> entries = facet switch
            {
                WotAssetFacet.Properties => asset.EnumeratePropertiesAsync(ct),
                WotAssetFacet.Actions => asset.EnumerateActionsAsync(ct),
                _ => throw new ArgumentOutOfRangeException(nameof(facet))
            };
            await foreach (WotAssetVariableEntry entry in entries.WithCancellation(ct).ConfigureAwait(false))
            {
                var result = new JsonObject
                {
                    ["nodeId"] = entry.NodeId.ToString(),
                    ["browseName"] = entry.BrowseName
                };
                if (facet == WotAssetFacet.Actions)
                {
                    result["arguments"] = await McpModelAccess.PropertiesAsync(asset.Session, entry.NodeId,
                        [new QualifiedName("InputArguments"), new QualifiedName("OutputArguments")], ct)
                        .ConfigureAwait(false);
                    result["error"] = McpModelAccess.HasError(result["arguments"]!.AsObject());
                }
                else
                {
                    DataValue value = await McpModelAccess.ReadDataValueAsync(asset.Session, entry.NodeId, ct)
                        .ConfigureAwait(false);
                    result["dataValue"] = McpCompanionJson.DataValue(value, asset.Session.MessageContext);
                    result["error"] = StatusCode.IsBad(value.StatusCode);
                }
                yield return result;
            }
        }

        /// <summary>
        /// Parses a required non-null node identity.
        /// </summary>
        internal static NodeId Node(string text)
        {
            NodeId node = OpcUaJsonHelper.ParseNodeId(text);
            return node.IsNull ? throw new ArgumentException("A non-null NodeId is required.", nameof(text)) : node;
        }

        /// <summary>
        /// Registers the generated encodings before structured metadata or event fields are received.
        /// </summary>
        private static void RegisterTypes(ISession session)
        {
            IEncodeableFactory factory = session.MessageContext.Factory;
            IEncodeableFactoryBuilder builder = factory.Builder;
            bool changed = false;
            if (!factory.ContainsEncodeableType(XRegistry.DataTypeIds.RegistryCapabilitiesDataType))
            {
                builder = builder.AddOpcUaXRegistry();
                changed = true;
            }
            if (!factory.ContainsEncodeableType(WotCon.DataTypeIds.WoTValidationOutcomeDataType))
            {
                builder = builder.AddOpcUaWotCon();
                changed = true;
            }
            if (changed)
            {
                builder.Commit();
            }
        }
    }
}
