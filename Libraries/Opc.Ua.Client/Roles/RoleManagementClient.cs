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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Client.Roles
{
    /// <summary>
    /// Async client over the OPC UA Part 18 §4 role-management methods.
    /// Wraps an <see cref="ISession"/> and dispatches against the standard
    /// <c>Server.ServerCapabilities.RoleSet</c> object.
    /// </summary>
    public sealed class RoleManagementClient : IRoleManagementClient
    {
        /// <summary>
        /// Creates a new client rooted at the server's
        /// <c>Server.ServerCapabilities.RoleSet</c> (NodeId i=15606).
        /// </summary>
        public RoleManagementClient(ISession session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <summary>The session used for all service calls.</summary>
        public ISession Session { get; }

        private static NodeId RoleSetId => Opc.Ua.ObjectIds.Server_ServerCapabilities_RoleSet;
        private static NodeId AddRoleMethodId
            => Opc.Ua.MethodIds.Server_ServerCapabilities_RoleSet_AddRole;
        private static NodeId RemoveRoleMethodId
            => Opc.Ua.MethodIds.Server_ServerCapabilities_RoleSet_RemoveRole;

        /// <inheritdoc/>
        public async ValueTask<IReadOnlyList<RoleInfo>> ListRolesAsync(
            CancellationToken cancellationToken = default)
        {
            // Browse the RoleSet for its HasComponent role children (RoleType
            // instances). Each child's NodeId + browse name is returned;
            // properties are then read in a single call per role to keep the
            // round-trip count proportional to the number of roles.
            var browseDescriptions = new[]
            {
                new BrowseDescription
                {
                    NodeId = RoleSetId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)NodeClass.Object,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };
            BrowseResponse browseResponse = await Session.BrowseAsync(
                null,
                null,
                requestedMaxReferencesPerNode: 0,
                ArrayOf.Wrapped(browseDescriptions),
                cancellationToken).ConfigureAwait(false);

            ClientBase.ValidateResponse<BrowseDescription, BrowseResult>(
                browseResponse.Results, browseDescriptions);
            ArrayOf<ReferenceDescription> references = browseResponse.Results[0].References;

            var roles = new List<RoleInfo>(references.Count);
            // Materialize references to a regular list so the async loop below
            // does not capture a span enumerator across await boundaries.
            var materializedRefs = new List<ReferenceDescription>(references.Count);
            foreach (ReferenceDescription reference in references)
            {
                materializedRefs.Add(reference);
            }
            foreach (ReferenceDescription reference in materializedRefs)
            {
                NodeId roleId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                if (roleId.IsNull)
                {
                    continue;
                }
                if (!reference.TypeDefinition.IsNull
                    && !ExpandedNodeId.Equals(reference.TypeDefinition, Opc.Ua.ObjectTypeIds.RoleType))
                {
                    continue;
                }
                RoleInfo info = await ReadRoleAsync(roleId, cancellationToken).ConfigureAwait(false);
                roles.Add(info);
            }
            return roles;
        }

        /// <inheritdoc/>
        public async ValueTask<RoleInfo> ReadRoleAsync(
            NodeId roleId,
            CancellationToken cancellationToken = default)
        {
            if (roleId.IsNull)
            {
                throw new ArgumentNullException(nameof(roleId));
            }

            // Resolve the property NodeIds via a single TranslateBrowsePath batch.
            string[] propertyNames =
            [
                BrowseNames.Identities,
                BrowseNames.Applications,
                BrowseNames.ApplicationsExclude,
                BrowseNames.Endpoints,
                BrowseNames.EndpointsExclude,
                BrowseNames.CustomConfiguration,
            ];
            var browsePaths = new List<BrowsePath>(propertyNames.Length);
            foreach (string name in propertyNames)
            {
                browsePaths.Add(new BrowsePath
                {
                    StartingNode = roleId,
                    RelativePath = new RelativePath(new QualifiedName(name))
                });
            }
            TranslateBrowsePathsToNodeIdsResponse pathResponse = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    ArrayOf.Wrapped(browsePaths.ToArray()),
                    cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse<BrowsePath, BrowsePathResult>(pathResponse.Results, browsePaths);

            // Build the Read batch — BrowseName plus each resolved property
            // NodeId. Properties that don't exist (optional) are skipped.
            var nodesToRead = new List<ReadValueId>
            {
                new() { NodeId = roleId, AttributeId = Attributes.BrowseName }
            };
            var resolved = new NodeId[propertyNames.Length];
            for (int i = 0; i < propertyNames.Length; i++)
            {
                BrowsePathResult pathResult = pathResponse.Results[i];
                if (StatusCode.IsGood(pathResult.StatusCode)
                    && pathResult.Targets.Count > 0
                    && !pathResult.Targets[0].TargetId.IsNull)
                {
                    resolved[i] = ExpandedNodeId.ToNodeId(
                        pathResult.Targets[0].TargetId, Session.NamespaceUris);
                    nodesToRead.Add(new ReadValueId
                    {
                        NodeId = resolved[i],
                        AttributeId = Attributes.Value
                    });
                }
            }

            ReadResponse readResponse = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                ArrayOf.Wrapped(nodesToRead.ToArray()),
                cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse<ReadValueId, DataValue>(readResponse.Results, nodesToRead);

            ArrayOf<DataValue> results = readResponse.Results;
            QualifiedName browseName = results[0].WrappedValue.TryGetValue(out QualifiedName qn)
                ? qn
                : new QualifiedName(string.Empty);

            int readIndex = 1;
            IReadOnlyList<IdentityMappingRuleType> identities = [];
            IReadOnlyList<string> applications = [];
            bool applicationsExclude = true;
            IReadOnlyList<EndpointType> endpoints = [];
            bool endpointsExclude = true;
            bool customConfiguration = false;

            for (int i = 0; i < propertyNames.Length; i++)
            {
                if (resolved[i].IsNull)
                {
                    continue;
                }
                DataValue dv = results[readIndex++];
                switch (propertyNames[i])
                {
                    case BrowseNames.Identities:
                        identities = ReadStructureArray<IdentityMappingRuleType>(dv);
                        break;
                    case BrowseNames.Applications:
                        applications = ReadStringArray(dv);
                        break;
                    case BrowseNames.ApplicationsExclude:
                        applicationsExclude = ReadOptionalBool(dv, defaultValue: true);
                        break;
                    case BrowseNames.Endpoints:
                        endpoints = ReadStructureArray<EndpointType>(dv);
                        break;
                    case BrowseNames.EndpointsExclude:
                        endpointsExclude = ReadOptionalBool(dv, defaultValue: true);
                        break;
                    case BrowseNames.CustomConfiguration:
                        customConfiguration = ReadOptionalBool(dv, defaultValue: false);
                        break;
                }
            }

            return new RoleInfo(
                roleId,
                browseName,
                identities,
                applications,
                applicationsExclude,
                endpoints,
                endpointsExclude,
                customConfiguration);
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> AddRoleAsync(
            string roleName,
            string? namespaceUri = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(roleName))
            {
                throw new ArgumentException("RoleName is required.", nameof(roleName));
            }

            ArrayOf<Variant> output = await Session.CallAsync(
                RoleSetId,
                AddRoleMethodId,
                cancellationToken,
                Variant.From(roleName),
                Variant.From(namespaceUri ?? string.Empty)).ConfigureAwait(false);

            if (output.Count == 0 || !output[0].TryGetValue(out NodeId newRoleId))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "AddRole did not return a NodeId.");
            }
            return newRoleId;
        }

        /// <inheritdoc/>
        public async ValueTask RemoveRoleAsync(NodeId roleId, CancellationToken cancellationToken = default)
        {
            if (roleId.IsNull)
            {
                throw new ArgumentNullException(nameof(roleId));
            }
            await Session.CallAsync(
                RoleSetId,
                RemoveRoleMethodId,
                cancellationToken,
                Variant.From(roleId)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask AddIdentityAsync(
            NodeId roleId,
            IdentityMappingRuleType rule,
            CancellationToken cancellationToken = default)
        {
            if (rule == null)
            {
                throw new ArgumentNullException(nameof(rule));
            }
            NodeId methodId = await ResolveMethodIdAsync(roleId, BrowseNames.AddIdentity, cancellationToken)
                .ConfigureAwait(false);
            await Session.CallAsync(
                roleId,
                methodId,
                cancellationToken,
                Variant.FromStructure(rule)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask RemoveIdentityAsync(
            NodeId roleId,
            IdentityMappingRuleType rule,
            CancellationToken cancellationToken = default)
        {
            if (rule == null)
            {
                throw new ArgumentNullException(nameof(rule));
            }
            NodeId methodId = await ResolveMethodIdAsync(roleId, BrowseNames.RemoveIdentity, cancellationToken)
                .ConfigureAwait(false);
            await Session.CallAsync(
                roleId,
                methodId,
                cancellationToken,
                Variant.FromStructure(rule)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask AddApplicationAsync(
            NodeId roleId,
            string applicationUri,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(applicationUri))
            {
                throw new ArgumentException("ApplicationUri is required.", nameof(applicationUri));
            }
            NodeId methodId = await ResolveMethodIdAsync(roleId, BrowseNames.AddApplication, cancellationToken)
                .ConfigureAwait(false);
            await Session.CallAsync(
                roleId,
                methodId,
                cancellationToken,
                Variant.From(applicationUri)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask RemoveApplicationAsync(
            NodeId roleId,
            string applicationUri,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(applicationUri))
            {
                throw new ArgumentException("ApplicationUri is required.", nameof(applicationUri));
            }
            NodeId methodId = await ResolveMethodIdAsync(roleId, BrowseNames.RemoveApplication, cancellationToken)
                .ConfigureAwait(false);
            await Session.CallAsync(
                roleId,
                methodId,
                cancellationToken,
                Variant.From(applicationUri)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask AddEndpointAsync(
            NodeId roleId,
            EndpointType endpoint,
            CancellationToken cancellationToken = default)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            NodeId methodId = await ResolveMethodIdAsync(roleId, BrowseNames.AddEndpoint, cancellationToken)
                .ConfigureAwait(false);
            await Session.CallAsync(
                roleId,
                methodId,
                cancellationToken,
                Variant.FromStructure(endpoint)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask RemoveEndpointAsync(
            NodeId roleId,
            EndpointType endpoint,
            CancellationToken cancellationToken = default)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            NodeId methodId = await ResolveMethodIdAsync(roleId, BrowseNames.RemoveEndpoint, cancellationToken)
                .ConfigureAwait(false);
            await Session.CallAsync(
                roleId,
                methodId,
                cancellationToken,
                Variant.FromStructure(endpoint)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask SetApplicationsExcludeAsync(
            NodeId roleId,
            bool value,
            CancellationToken cancellationToken = default)
        {
            return WritePropertyAsync(roleId, BrowseNames.ApplicationsExclude, Variant.From(value), cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask SetEndpointsExcludeAsync(
            NodeId roleId,
            bool value,
            CancellationToken cancellationToken = default)
        {
            return WritePropertyAsync(roleId, BrowseNames.EndpointsExclude, Variant.From(value), cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask SetCustomConfigurationAsync(
            NodeId roleId,
            bool value,
            CancellationToken cancellationToken = default)
        {
            return WritePropertyAsync(roleId, BrowseNames.CustomConfiguration, Variant.From(value), cancellationToken);
        }

        private ValueTask<NodeId> ResolveMethodIdAsync(
            NodeId roleId,
            string browseName,
            CancellationToken cancellationToken)
        {
            return ResolveChildAsync(roleId, browseName, NodeClass.Method, cancellationToken);
        }

        private async ValueTask<NodeId> ResolveChildAsync(
            NodeId parentId,
            string browseName,
            NodeClass expectedClass,
            CancellationToken cancellationToken)
        {
            var browsePaths = new[]
            {
                new BrowsePath
                {
                    StartingNode = parentId,
                    RelativePath = new RelativePath(new QualifiedName(browseName))
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    ArrayOf.Wrapped(browsePaths),
                    cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse<BrowsePath, BrowsePathResult>(response.Results, browsePaths);

            BrowsePathResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode,
                    $"Cannot resolve {browseName} on {parentId}: {result.StatusCode}");
            }
            if (result.Targets.Count == 0 || result.Targets[0].TargetId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound,
                    $"Child '{browseName}' not found on {parentId}.");
            }
            return ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris);
        }

        private async ValueTask WritePropertyAsync(
            NodeId parentId,
            string browseName,
            Variant value,
            CancellationToken cancellationToken)
        {
            NodeId propertyId = await ResolveChildAsync(parentId, browseName, NodeClass.Variable, cancellationToken)
                .ConfigureAwait(false);
            var writes = new[]
            {
                new WriteValue
                {
                    NodeId = propertyId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(value)
                }
            };
            WriteResponse response = await Session.WriteAsync(
                null,
                ArrayOf.Wrapped(writes),
                cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse<WriteValue, StatusCode>(response.Results, writes);
            if (StatusCode.IsBad(response.Results[0]))
            {
                throw new ServiceResultException(response.Results[0],
                    $"Write to {browseName} failed: {response.Results[0]}");
            }
        }

        private static IReadOnlyList<T> ReadStructureArray<T>(DataValue dv) where T : class, IEncodeable, new()
        {
            if (StatusCode.IsBad(dv.StatusCode))
            {
                return [];
            }
            if (dv.WrappedValue.TryGetStructure(out ArrayOf<T> typed))
            {
                if (typed.Count == 0)
                {
                    return [];
                }
                var list = new List<T>(typed.Count);
                foreach (T item in typed)
                {
                    list.Add(item);
                }
                return list;
            }
            if (dv.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> extArr))
            {
                var list = new List<T>(extArr.Count);
                foreach (ExtensionObject ext in extArr)
                {
                    if (ext.TryGetValue(out T? item) && item != null)
                    {
                        list.Add(item);
                    }
                }
                return list;
            }
            return [];
        }

        private static IReadOnlyList<string> ReadStringArray(DataValue dv)
        {
            if (StatusCode.IsBad(dv.StatusCode))
            {
                return [];
            }
            if (!dv.WrappedValue.TryGetValue(out ArrayOf<string> arr) || arr.Count == 0)
            {
                return [];
            }
            var list = new List<string>(arr.Count);
            foreach (string s in arr)
            {
                list.Add(s);
            }
            return list;
        }

        private static bool ReadOptionalBool(DataValue dv, bool defaultValue)
        {
            if (StatusCode.IsBad(dv.StatusCode))
            {
                return defaultValue;
            }
            return dv.WrappedValue.TryGetValue(out bool b) ? b : defaultValue;
        }
    }
}
