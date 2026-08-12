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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.Aas.Client.Registry
{
    /// <summary>
    /// High-level client for the AAS registry root exposed below the standard Server object.
    /// </summary>
    /// <remarks>
    /// The AAS registry subtypes the xRegistry base model, so this client derives from
    /// <see cref="XRegistryClient"/> and inherits the generic registry lifecycle. It adds the
    /// AAS-specific discovery Methods and typed wrappers for the AAS group/resource subtypes.
    /// </remarks>
    /// <example>
    /// <code>
    /// AasRegistryClient registry = await AasRegistryClient.ForServerAsync(session, telemetry, ct);
    /// ArrayOf&lt;NodeId&gt; shells = await registry.LookupShellsByAssetLinkAsync("serial", "42", ct);
    /// AasGetSubmodelDocumentResult submodel = await registry.GetSubmodelAsync("urn:submodel", ct);
    /// </code>
    /// </example>
    public sealed class AasRegistryClient : XRegistryClient
    {
        /// <summary>
        /// Creates a registry client rooted at the resolved <c>AASRegistry</c> Object.
        /// </summary>
        public AasRegistryClient(ISession session, NodeId registryObjectId, ITelemetryContext telemetry)
            : base(session, EnsureRegistryNamespace(session), ValidateRegistryObjectId(registryObjectId), telemetry)
        {
            Proxy = new AASRegistryTypeClient(session, registryObjectId, telemetry);
        }

        /// <summary>
        /// Source-generated AAS registry proxy.
        /// </summary>
        public AASRegistryTypeClient Proxy { get; }

        /// <summary>
        /// Resolves the well-known <c>AASRegistry</c> Object below the standard <c>Server</c> Object.
        /// </summary>
        /// <example>
        /// <code>
        /// AasRegistryClient registry = await AasRegistryClient.ForServerAsync(session, telemetry, ct);
        /// </code>
        /// </example>
        public static async ValueTask<AasRegistryClient> ForServerAsync(
            ISession session,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            ushort ns = session.NamespaceUris.GetIndexOrAppend(Opc.Ua.Aas.V3.Namespaces.AasV3);
            NodeId registryId = await AasRegistryBrowsePathResolver.ResolveChildAsync(
                session,
                global::Opc.Ua.ObjectIds.Server,
                global::Opc.Ua.ReferenceTypeIds.HasComponent,
                ns,
                "AASRegistry",
                StatusCodes.BadNodeIdUnknown,
                "AASRegistry entry point not found on the connected server.",
                ct).ConfigureAwait(false);
            return new AasRegistryClient(session, registryId, telemetry);
        }

        /// <summary>
        /// Calls <c>LookupShellsByAssetLink</c> and returns matching shell group NodeIds.
        /// </summary>
        public ValueTask<ArrayOf<NodeId>> LookupShellsByAssetLinkAsync(
            string name,
            string value,
            CancellationToken ct = default)
        {
            return Proxy.LookupShellsByAssetLinkAsync(name, value, ct);
        }

        /// <summary>
        /// Calls <c>GetSubmodel</c> and returns the Method status without hiding normal not-found
        /// or user-access-denied outcomes.
        /// </summary>
        public async ValueTask<AasGetSubmodelDocumentResult> GetSubmodelAsync(
            string submodelIdentifier,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(submodelIdentifier))
            {
                throw new ArgumentException("A submodel identifier is required.", nameof(submodelIdentifier));
            }

            CallResponse response = await Session.CallAsync(
                null,
                new[]
                {
                    new CallMethodRequest
                    {
                        ObjectId = RegistryNodeId,
                        MethodId = ExpandedNodeId.ToNodeId(Opc.Ua.Aas.V3.MethodIds.AASRegistryType_GetSubmodel, Session.NamespaceUris),
                        InputArguments = [new Variant(submodelIdentifier)]
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);
            if (response.Results.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "GetSubmodel returned no method result.");
            }

            CallMethodResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode))
            {
                return new AasGetSubmodelDocumentResult(result.StatusCode, default, string.Empty, string.Empty);
            }
            ArrayOf<Variant> output = result.OutputArguments.IsNull ? [] : result.OutputArguments;
            if (output.Count < 3 ||
                !output[0].TryGetValue(out ByteString document) ||
                !output[1].TryGetValue(out string? format) ||
                !output[2].TryGetValue(out string? contentType))
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "GetSubmodel returned unexpected output arguments.");
            }
            return new AasGetSubmodelDocumentResult(
                result.StatusCode,
                document,
                format ?? string.Empty,
                contentType ?? string.Empty);
        }

        /// <summary>
        /// Opens an existing shell group by NodeId.
        /// </summary>
        public AasShellGroupClient OpenShellGroup(NodeId groupNodeId)
        {
            return new AasShellGroupClient(Session, groupNodeId, Telemetry);
        }

        /// <summary>
        /// Opens an existing submodel template group by NodeId.
        /// </summary>
        public AasSubmodelTemplateGroupClient OpenSubmodelTemplateGroup(NodeId groupNodeId)
        {
            return new AasSubmodelTemplateGroupClient(Session, groupNodeId, Telemetry);
        }

        /// <summary>
        /// Opens an existing concept dictionary group by NodeId.
        /// </summary>
        public AasConceptDictionaryGroupClient OpenConceptDictionaryGroup(NodeId groupNodeId)
        {
            return new AasConceptDictionaryGroupClient(Session, groupNodeId, Telemetry);
        }

        /// <summary>
        /// Opens an existing package store group by NodeId.
        /// </summary>
        public AasPackageStoreGroupClient OpenPackageStoreGroup(NodeId groupNodeId)
        {
            return new AasPackageStoreGroupClient(Session, groupNodeId, Telemetry);
        }

        /// <summary>
        /// Browses environment export documents organized directly by the registry root.
        /// </summary>
        public async ValueTask<ArrayOf<AasEnvironmentFileClient>> ListEnvironmentDocumentsAsync(
            CancellationToken ct = default)
        {
            ArrayOf<NodeId> nodes = await AasRegistryNodeReader.BrowseOrganizedObjectsAsync(
                Session,
                RegistryNodeId,
                ct).ConfigureAwait(false);
            var resources = new AasEnvironmentFileClient[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                resources[i] = new AasEnvironmentFileClient(Session, RegistryNodeId, nodes[i], Telemetry);
            }
            return resources.ToArrayOf();
        }

        private static string EnsureRegistryNamespace(ISession session)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            session.NamespaceUris.GetIndexOrAppend(Opc.Ua.Aas.V3.Namespaces.AasV3);
            return Opc.Ua.Aas.V3.Namespaces.AasV3;
        }

        private static NodeId ValidateRegistryObjectId(NodeId registryObjectId)
        {
            if (registryObjectId.IsNull)
            {
                throw new ArgumentException(
                    "Registry object NodeId is required.",
                    nameof(registryObjectId));
            }
            return registryObjectId;
        }
    }
}
