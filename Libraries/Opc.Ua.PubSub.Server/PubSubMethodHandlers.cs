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
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// Hosts the synchronous method-handler delegates the
    /// <see cref="PubSubNodeManager"/> attaches to the standard
    /// <c>PublishSubscribe</c> Method nodes (Part 14 §9.1.3,
    /// §9.1.10 and §8.3.1).
    /// </summary>
    /// <remarks>
    /// All entry-points adhere to the legacy synchronous
    /// <c>GenericMethodCalledEventHandler</c> contract; every async
    /// call is forwarded via <c>.AsTask().GetAwaiter().GetResult()</c>
    /// — the single sanctioned sync-over-async bridge, matching the
    /// rationale documented on
    /// <see cref="SksMethodHandler.HandleGetSecurityKeys"/>.
    /// Configuration-mutation entry-points return
    /// <see cref="StatusCodes.BadNotImplemented"/> because the
    /// Phase 9 <see cref="IPubSubApplication"/> runtime is
    /// immutable: configuration is owned by the
    /// <see cref="Configuration.IPubSubConfigurationStore"/> and the
    /// host process must restart the application to apply a new
    /// snapshot. The contract is documented per-method.
    /// </remarks>
    internal sealed class PubSubMethodHandlers
    {
        private const string DefaultSecurityPolicyUri =
            "http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes256-CTR";

        private readonly IPubSubApplication m_application;
        private readonly IPubSubKeyServiceServer? m_keyService;
        private readonly PubSubServerOptions m_options;
        private readonly SksMethodHandler? m_sks;
        private readonly ILogger m_logger;
        private readonly Dictionary<NodeId, string> m_securityGroupNodeIds = new();
        private readonly System.Threading.Lock m_gate = new();
        private uint m_nextSecurityGroupHandle;

        /// <summary>
        /// Creates a new <see cref="PubSubMethodHandlers"/>.
        /// </summary>
        /// <param name="application">Runtime application.</param>
        /// <param name="keyService">
        /// SKS server, or <see langword="null"/> when the host is
        /// not acting as an SKS.
        /// </param>
        /// <param name="options">PubSub server options.</param>
        /// <param name="telemetry">Telemetry context.</param>
        public PubSubMethodHandlers(
            IPubSubApplication application,
            IPubSubKeyServiceServer? keyService,
            PubSubServerOptions options,
            ITelemetryContext telemetry)
        {
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_application = application;
            m_keyService = keyService;
            m_options = options;
            m_sks = keyService is null ? null : new SksMethodHandler(keyService, telemetry);
            m_logger = telemetry.CreateLogger<PubSubMethodHandlers>();
        }

        /// <summary>
        /// Implements Part 14 §9.1.10.2 <c>Status.Enable</c>.
        /// </summary>
        /// <param name="context">System context.</param>
        /// <param name="method">Calling method node.</param>
        /// <param name="inputArguments">Input arguments (none).</param>
        /// <param name="outputArguments">Output arguments (none).</param>
        public ServiceResult OnEnable(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = inputArguments;
            _ = outputArguments;
            try
            {
                m_application.StartAsync().AsTask().GetAwaiter().GetResult();
                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "PublishSubscribe Enable failed.");
                return new ServiceResult(StatusCodes.BadInvalidState, new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.10.3 <c>Status.Disable</c>.
        /// </summary>
        /// <param name="context">System context.</param>
        /// <param name="method">Calling method node.</param>
        /// <param name="inputArguments">Input arguments (none).</param>
        /// <param name="outputArguments">Output arguments (none).</param>
        public ServiceResult OnDisable(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = inputArguments;
            _ = outputArguments;
            try
            {
                m_application.StopAsync().AsTask().GetAwaiter().GetResult();
                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "PublishSubscribe Disable failed.");
                return new ServiceResult(StatusCodes.BadInvalidState, new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Implements Part 14 §9.1.3.4 <c>AddConnection</c>. Returns
        /// <see cref="StatusCodes.BadNotImplemented"/> because the
        /// Phase 9 runtime is immutable.
        /// </summary>
        /// <param name="context">System context.</param>
        /// <param name="method">Calling method node.</param>
        /// <param name="inputArguments">Input arguments.</param>
        /// <param name="outputArguments">Output arguments.</param>
        public ServiceResult OnAddConnection(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = inputArguments;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            outputArguments.Add(Variant.From(NodeId.Null));
            return new ServiceResult(
                StatusCodes.BadNotImplemented,
                new LocalizedText("Runtime PubSub configuration mutation is not supported by the immutable Phase 9 application surface."));
        }

        /// <summary>
        /// Implements Part 14 §9.1.3.5 <c>RemoveConnection</c>.
        /// Returns <see cref="StatusCodes.BadNotImplemented"/>.
        /// </summary>
        /// <param name="context">System context.</param>
        /// <param name="method">Calling method node.</param>
        /// <param name="inputArguments">Input arguments.</param>
        /// <param name="outputArguments">Output arguments.</param>
        public ServiceResult OnRemoveConnection(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = inputArguments;
            _ = outputArguments;
            if (!m_options.ExposeConfigurationMethods)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            return new ServiceResult(
                StatusCodes.BadNotImplemented,
                new LocalizedText("Runtime PubSub configuration mutation is not supported by the immutable Phase 9 application surface."));
        }

        /// <summary>
        /// Implements Part 14 §8.3.4 <c>AddSecurityGroup</c>.
        /// Delegates to
        /// <see cref="IPubSubKeyServiceServer.AddSecurityGroupAsync"/>.
        /// </summary>
        /// <param name="context">System context.</param>
        /// <param name="method">Calling method node.</param>
        /// <param name="inputArguments">Input arguments.</param>
        /// <param name="outputArguments">Output arguments.</param>
        public ServiceResult OnAddSecurityGroup(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            if (m_keyService is null)
            {
                return new ServiceResult(StatusCodes.BadServiceUnsupported);
            }
            if (inputArguments.Count < 5)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText($"AddSecurityGroup expects 5 input arguments; got {inputArguments.Count}."));
            }
            if (!inputArguments[0].TryGetValue(out string? name) || string.IsNullOrEmpty(name))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddSecurityGroup argument 0 (SecurityGroupName) is missing or empty."));
            }
            if (!inputArguments[1].TryGetValue(out double keyLifetimeMs) || keyLifetimeMs <= 0)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddSecurityGroup argument 1 (KeyLifetime) must be a positive Duration."));
            }
            if (!inputArguments[2].TryGetValue(out string? policyUri) || string.IsNullOrEmpty(policyUri))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddSecurityGroup argument 2 (SecurityPolicyUri) is missing or empty."));
            }
            if (!inputArguments[3].TryGetValue(out uint maxFuture))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddSecurityGroup argument 3 (MaxFutureKeyCount) is not a UInt32."));
            }
            if (!inputArguments[4].TryGetValue(out uint maxPast))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("AddSecurityGroup argument 4 (MaxPastKeyCount) is not a UInt32."));
            }

            var group = new SksSecurityGroup(
                securityGroupId: name,
                securityPolicyUri: policyUri,
                keyLifetime: TimeSpan.FromMilliseconds(keyLifetimeMs),
                maxFutureKeyCount: (int)Math.Min(maxFuture, int.MaxValue),
                maxPastKeyCount: (int)Math.Min(maxPast, int.MaxValue),
                keys: Array.Empty<PubSubSecurityKey>());

            try
            {
                m_keyService
                    .AddSecurityGroupAsync(group)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OpcUaSksException ex)
            {
                m_logger.LogDebug(ex, "AddSecurityGroup {Name} rejected with {Status}.", name, ex.Status);
                return new ServiceResult(ex.Status, new LocalizedText(ex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "AddSecurityGroup {Name} threw unexpectedly.", name);
                return new ServiceResult(StatusCodes.BadInternalError, new LocalizedText(ex.Message));
            }

            NodeId groupNodeId = AllocateSecurityGroupNodeId(name);
            outputArguments.Add(Variant.From(name));
            outputArguments.Add(Variant.From(groupNodeId));
            return ServiceResult.Good;
        }

        /// <summary>
        /// Implements Part 14 §8.3.5 <c>RemoveSecurityGroup</c>.
        /// </summary>
        /// <param name="context">System context.</param>
        /// <param name="method">Calling method node.</param>
        /// <param name="inputArguments">Input arguments.</param>
        /// <param name="outputArguments">Output arguments (none).</param>
        public ServiceResult OnRemoveSecurityGroup(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = context;
            _ = method;
            _ = outputArguments;
            if (m_keyService is null)
            {
                return new ServiceResult(StatusCodes.BadServiceUnsupported);
            }
            if (inputArguments.Count < 1)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("RemoveSecurityGroup expects 1 input argument."));
            }
            if (!inputArguments[0].TryGetValue(out NodeId groupNodeId) || groupNodeId.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    new LocalizedText("RemoveSecurityGroup argument 0 (SecurityGroupNodeId) is missing or not a NodeId."));
            }
            string? id = LookupSecurityGroupId(groupNodeId);
            if (id is null)
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown);
            }
            try
            {
                m_keyService
                    .RemoveSecurityGroupAsync(id)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OpcUaSksException ex)
            {
                m_logger.LogDebug(ex, "RemoveSecurityGroup {Id} rejected with {Status}.", id, ex.Status);
                return new ServiceResult(ex.Status, new LocalizedText(ex.Message));
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "RemoveSecurityGroup {Id} threw unexpectedly.", id);
                return new ServiceResult(StatusCodes.BadInternalError, new LocalizedText(ex.Message));
            }
            lock (m_gate)
            {
                m_securityGroupNodeIds.Remove(groupNodeId);
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Implements Part 14 §8.3.2 <c>GetSecurityKeys</c>.
        /// Delegates to <see cref="SksMethodHandler.HandleGetSecurityKeys"/>.
        /// </summary>
        /// <param name="context">System context.</param>
        /// <param name="method">Calling method node.</param>
        /// <param name="objectId">Object the method is called on.</param>
        /// <param name="inputArguments">Input arguments.</param>
        /// <param name="outputArguments">Output arguments.</param>
        public ServiceResult OnGetSecurityKeys(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            _ = method;
            if (m_sks is null)
            {
                return new ServiceResult(StatusCodes.BadServiceUnsupported);
            }
            return m_sks.HandleGetSecurityKeys(context, objectId, inputArguments.ToList(), outputArguments);
        }

        /// <summary>
        /// Returns the NodeId previously allocated for the
        /// SecurityGroup identified by <paramref name="securityGroupId"/>,
        /// or <see langword="null"/> when the id is unknown to this
        /// handler.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        public NodeId? TryGetSecurityGroupNodeId(string securityGroupId)
        {
            if (string.IsNullOrEmpty(securityGroupId))
            {
                return null;
            }
            lock (m_gate)
            {
                foreach (KeyValuePair<NodeId, string> kvp in m_securityGroupNodeIds)
                {
                    if (string.Equals(kvp.Value, securityGroupId, StringComparison.Ordinal))
                    {
                        return kvp.Key;
                    }
                }
                return null;
            }
        }

        private string? LookupSecurityGroupId(NodeId groupNodeId)
        {
            lock (m_gate)
            {
                if (m_securityGroupNodeIds.TryGetValue(groupNodeId, out string? id))
                {
                    return id;
                }
            }
            if (groupNodeId.IdType == IdType.String &&
                groupNodeId.TryGetValue(out string identifier) &&
                !string.IsNullOrEmpty(identifier))
            {
                return identifier;
            }
            return null;
        }

        private NodeId AllocateSecurityGroupNodeId(string securityGroupId)
        {
            uint handle;
            lock (m_gate)
            {
                handle = ++m_nextSecurityGroupHandle;
            }
            var nodeId = new NodeId($"SecurityGroups/{securityGroupId}/{handle}", 0);
            lock (m_gate)
            {
                m_securityGroupNodeIds[nodeId] = securityGroupId;
            }
            return nodeId;
        }

        /// <summary>
        /// Returns the default SecurityPolicyUri for the SKS host.
        /// </summary>
        public string DefaultPolicyUri => m_options.DefaultSecurityPolicyUri ?? DefaultSecurityPolicyUri;
    }
}
