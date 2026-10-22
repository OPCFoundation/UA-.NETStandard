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

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The channel and restriction inputs of an exact-source read check, captured before the
    /// check so a change while it is suspended (or since a verdict was cached) is detected.
    /// </summary>
    /// <param name="Endpoint">The subscriber's accepted endpoint.</param>
    /// <param name="SecurityMode">The endpoint's security mode when captured.</param>
    /// <param name="Transport">The endpoint's transport profile when captured.</param>
    /// <param name="Restrictions">The node's own access restrictions when captured.</param>
    internal readonly record struct MonitoredReadInputs(
        EndpointDescription? Endpoint,
        MessageSecurityMode? SecurityMode,
        string? Transport,
        AccessRestrictionType? Restrictions)
    {
        /// <summary>
        /// Captures the current inputs of the subscriber's session and the node.
        /// </summary>
        public static MonitoredReadInputs Capture(ISession? session, NodeState node)
        {
            EndpointDescription? endpoint = MonitoredReadAuthorization.GetEndpoint(session);
            return new MonitoredReadInputs(
                endpoint, endpoint?.SecurityMode, endpoint?.TransportProfileUri, node.AccessRestrictions);
        }

        /// <summary>
        /// Returns <c>true</c> when the session's endpoint and the node's restrictions are still
        /// the captured ones. The endpoint is compared by reference, its mode and transport by value.
        /// </summary>
        public bool IsCurrent(ISession? session, NodeState node)
        {
            EndpointDescription? current = MonitoredReadAuthorization.GetEndpoint(session);
            return ReferenceEquals(Endpoint, current) &&
                SecurityMode == current?.SecurityMode &&
                Transport == current?.TransportProfileUri &&
                Restrictions == node.AccessRestrictions;
        }

        /// <summary>
        /// Compares two captures, the endpoint by reference.
        /// </summary>
        public bool Matches(in MonitoredReadInputs other)
        {
            return ReferenceEquals(Endpoint, other.Endpoint) &&
                SecurityMode == other.SecurityMode &&
                Transport == other.Transport &&
                Restrictions == other.Restrictions;
        }
    }

    /// <summary>
    /// A good exact-source read verdict of a monitored item (see
    /// <see cref="MonitoredReadAuthorization.ValidateAsync"/>) and the channel, restriction and
    /// namespace-default inputs it was computed with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exact-source recheck runs per item and per delivery or sample. Without a registered
    /// Value read authorizer (whose policy is never cached) and without read callbacks for the
    /// node's restrictions or permissions or the namespace defaults, its verdict depends only on
    /// the inputs kept here and on the role-permission version, the session's identity and
    /// identity generation and the owner's permission generation, which the caller keys the
    /// verdict on. A good verdict is reused until one of them moves; a bad one is never kept.
    /// </para>
    /// <para>
    /// The namespace metadata node (or its absence) is kept so metadata linked later, a read
    /// callback attached to one of its defaults later, or a revoked read access on one (none of
    /// which moves a generation) retires the verdict.
    /// </para>
    /// </remarks>
    internal sealed class MonitoredReadVerdict
    {
        private MonitoredReadVerdict(
            long defaultsGeneration,
            NamespaceMetadataState? defaults,
            in MonitoredReadInputs inputs,
            ServiceResult result)
        {
            m_defaultsGeneration = defaultsGeneration;
            m_defaults = defaults;
            m_inputs = inputs;
            Result = result;
        }

        /// <summary>
        /// The cached good verdict.
        /// </summary>
        public ServiceResult Result { get; }

        /// <summary>
        /// Returns <c>true</c> when the exact-source read check of the attribute involves no
        /// registered Value read authorizer, whose policy must run on every delivery, and no
        /// read callback for the node's restrictions or permissions, whose answer may change
        /// without moving any version a cached verdict is keyed on.
        /// </summary>
        public static bool CanCache(IAsyncNodeManager manager, NodeState node, uint attributeId)
        {
            if (node.OnReadAccessRestrictions != null ||
                node.OnReadRolePermissions != null ||
                node.OnReadUserRolePermissions != null)
            {
                return false;
            }
            return manager switch
            {
                AsyncCustomNodeManager custom => attributeId != Attributes.Value ||
                    node is not BaseVariableState variable ||
                    !custom.HasValueReadAuthorization(variable),
                AsyncNodeManagerAdapter { SyncNodeManager: CustomNodeManager2 } => true,
                _ => false
            };
        }

        /// <summary>
        /// Returns the result of <paramref name="verdict"/> when it may still be used for the
        /// node, attribute and current <paramref name="inputs"/>.
        /// </summary>
        public static bool TryReuse(
            MonitoredReadVerdict? verdict,
            IServerInternal server,
            IAsyncNodeManager manager,
            NodeState node,
            uint attributeId,
            in MonitoredReadInputs inputs,
            [NotNullWhen(true)] out ServiceResult? result)
        {
            if (verdict != null &&
                CanCache(manager, node, attributeId) &&
                verdict.Matches(server, node.NodeId.NamespaceIndex, inputs))
            {
                result = verdict.Result;
                return true;
            }
            result = null;
            return false;
        }

        /// <summary>
        /// Runs the exact-source read check and returns a verdict that may be reused when the
        /// result is good and nothing it depends on is uncached.
        /// </summary>
        public static async ValueTask<(ServiceResult Result, MonitoredReadVerdict? Verdict)> ValidateAsync(
            IServerInternal server,
            IAsyncNodeManager manager,
            NodeState node,
            ServerSystemContext context,
            uint attributeId,
            MonitoredReadInputs inputs,
            CancellationToken cancellationToken)
        {
            // Namespace defaults behind read callbacks or access levels are not keyed by any
            // version, so a verdict is only kept while the defaults are static.
            long defaultsGeneration = NamespaceDefaultPermissionReader.Generation;
            NamespaceMetadataState? defaults = null;
            bool cacheable = CanCache(manager, node, attributeId) &&
                NamespaceDefaultPermissionReader.TryGetStaticDefaults(
                    server, node.NodeId.NamespaceIndex, out defaults);
            ServiceResult result = await MonitoredReadAuthorization.ValidateAsync(
                manager, node, context, attributeId, cancellationToken).ConfigureAwait(false);
            if (cacheable &&
                ServiceResult.IsGood(result) &&
                defaultsGeneration == NamespaceDefaultPermissionReader.Generation &&
                NamespaceDefaultPermissionReader.AreDefaultsStatic(defaults))
            {
                return (result, new MonitoredReadVerdict(defaultsGeneration, defaults, inputs, result));
            }
            return (result, null);
        }

        private bool Matches(IServerInternal server, ushort namespaceIndex, in MonitoredReadInputs inputs)
        {
            // The metadata node (or its absence) is resolved again on every reuse: metadata linked
            // later through a reference alone moves no generation, and the registry only notices
            // it on lookup. A cached lookup, hit or miss, does not enumerate the namespaces.
            return m_defaultsGeneration == NamespaceDefaultPermissionReader.Generation &&
                m_inputs.Matches(inputs) &&
                NamespaceDefaultPermissionReader.TryGetStaticDefaults(
                    server, namespaceIndex, out NamespaceMetadataState? current) &&
                ReferenceEquals(current, m_defaults);
        }

        private readonly long m_defaultsGeneration;
        private readonly NamespaceMetadataState? m_defaults;
        private readonly MonitoredReadInputs m_inputs;
    }
}
