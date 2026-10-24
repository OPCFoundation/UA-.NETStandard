/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Server.DataChannels
{
    /// <summary>
    /// Declares one exact application resource and its independently enforced admission policy.
    /// A source can be used without publishing an AddressSpace or creating a Session.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelSource
    {
        /// <summary>
        /// Creates a resource declaration with an explicit application policy.
        /// </summary>
        public DataChannelSource(string resourceUri, IDataChannelSourcePolicy policy)
        {
            ValidateUri(resourceUri, nameof(resourceUri));
            ResourceUri = resourceUri;
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        /// <summary>
        /// Gets the exact resource URI; URI normalization is not used for authorization.
        /// </summary>
        public string ResourceUri { get; }

        /// <summary>
        /// Gets the optional source Object identity, resolved through the host NamespaceArray.
        /// </summary>
        public ExpandedNodeId SourceNodeId { get; init; }

        /// <summary>
        /// Gets content Nodes whose access restrictions also apply to the source.
        /// </summary>
        public ArrayOf<ExpandedNodeId> ContentNodeIds
        {
            get => m_contentNodeIds;
            init => m_contentNodeIds = value.ToArray() ?? [];
        }

        /// <summary>
        /// Gets supported payload protocols. The policy still validates their combinations.
        /// </summary>
        public ArrayOf<string> SupportedProtocols
        {
            get => m_protocols;
            init => m_protocols = value.ToArray() ?? [];
        }

        /// <summary>
        /// Gets supported opener-relative payload directions.
        /// </summary>
        public ArrayOf<DataChannelDirection> SupportedDirections
        {
            get => m_directions;
            init => m_directions = value.ToArray() ?? [];
        }

        /// <summary>
        /// Gets supported complete-message delivery modes.
        /// </summary>
        public ArrayOf<DataChannelDeliveryMode> SupportedDeliveryModes
        {
            get => m_delivery;
            init => m_delivery = value.ToArray() ?? [];
        }

        /// <summary>
        /// Gets independently permitted protection modes.
        /// </summary>
        public ArrayOf<DataChannelProtectionMode> SupportedProtectionModes
        {
            get => m_protection;
            init => m_protection = value.ToArray() ?? [];
        }

        /// <summary>
        /// Gets the exact content type, or an empty string when the resource policy selects it.
        /// </summary>
        public string ContentType { get; init; } = string.Empty;

        /// <summary>
        /// Gets the maximum complete application message size.
        /// </summary>
        public uint MaxMessageSize { get; init; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets the maximum encoded receive capacity of each flow direction.
        /// </summary>
        public uint MaxReceiveBytes { get; init; } = 32 * 1024 * 1024;

        /// <summary>
        /// Gets the finite maximum message rate; zero permits unbounded ordered rate only.
        /// </summary>
        public double MaxRate { get; init; }

        /// <summary>
        /// Gets the maximum simultaneous or provisionally granted flows of this resource.
        /// </summary>
        public int MaxFlows { get; init; } = 128;

        /// <summary>
        /// Gets whether this resource permits pre-commit queue expiry.
        /// </summary>
        public bool AllowQueueExpiry { get; init; }

        /// <summary>
        /// Gets the policy that admits application, user, direction and descriptor combinations.
        /// </summary>
        public IDataChannelSourcePolicy Policy { get; }

        /// <summary>
        /// Validates the complete proposal without silently revising its selections or bounds.
        /// </summary>
        public DataChannelResult Validate(DataChannelParametersDataType parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            if (parameters.ResourceUri != ResourceUri)
            {
                return DataChannelResult.NotAuthorized;
            }
            if (parameters.Direction is not (DataChannelDirection.OpenerToAcceptor or
                DataChannelDirection.AcceptorToOpener or DataChannelDirection.Duplex) ||
                parameters.Delivery is not (DataChannelDeliveryMode.Ordered or DataChannelDeliveryMode.Datagram) ||
                parameters.Protection is not (
                    DataChannelProtectionMode.Confidential or DataChannelProtectionMode.Integrity))
            {
                return DataChannelResult.InvalidArgument;
            }
            try
            {
                FlowWireText.ValidateUri(parameters.Protocol ?? string.Empty);
                if (parameters.ContentType == null || FlowWireText.Utf8Length(parameters.ContentType) > 255 ||
                    (parameters.Protocol == DataChannelProfiles.OpaquePayload && parameters.ContentType.Length == 0))
                {
                    return DataChannelResult.InvalidArgument;
                }
            }
            catch (FlowCodecException)
            {
                return DataChannelResult.InvalidArgument;
            }
            if (!Contains(SupportedProtocols, parameters.Protocol ?? string.Empty) ||
                !Contains(SupportedDirections, parameters.Direction) ||
                !Contains(SupportedDeliveryModes, parameters.Delivery) ||
                !Contains(SupportedProtectionModes, parameters.Protection) ||
                (ContentType.Length != 0 && ContentType != parameters.ContentType) ||
                (parameters.AllowQueueExpiry && !AllowQueueExpiry))
            {
                return DataChannelResult.Unsupported;
            }
            if (parameters.MaxMessageSize == 0 || parameters.MaxReceiveBytes == 0 ||
                parameters.MaxMessageSize > uint.MaxValue - 13 ||
                parameters.MaxReceiveBytes < parameters.MaxMessageSize + 13 ||
                double.IsNaN(parameters.MaxRate) || double.IsInfinity(parameters.MaxRate) ||
                parameters.MaxRate < 0 ||
                (parameters.Delivery == DataChannelDeliveryMode.Datagram && parameters.MaxRate <= 0))
            {
                return DataChannelResult.InvalidArgument;
            }
            if (parameters.MaxMessageSize > MaxMessageSize || parameters.MaxReceiveBytes > MaxReceiveBytes ||
                (MaxRate > 0 && (parameters.MaxRate == 0 || parameters.MaxRate > MaxRate)))
            {
                return DataChannelResult.TooLarge;
            }
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Rejects inconsistent declarations before they become discoverable.
        /// </summary>
        internal void Validate()
        {
            if (MaxFlows <= 0 || MaxFlows > 128 || MaxMessageSize == 0 ||
                MaxMessageSize > uint.MaxValue - 13 || MaxReceiveBytes < MaxMessageSize + 13 ||
                double.IsNaN(MaxRate) || double.IsInfinity(MaxRate) || MaxRate < 0 ||
                SupportedProtocols.Count == 0 || SupportedDirections.Count == 0 ||
                SupportedDeliveryModes.Count == 0 || SupportedProtectionModes.Count == 0 ||
                ContentType == null || FlowWireText.Utf8Length(ContentType) > 255)
            {
                throw new ArgumentException("Source capabilities must have finite, positive bounds.");
            }
            foreach (string protocol in SupportedProtocols)
            {
                ValidateUri(protocol, nameof(SupportedProtocols));
            }
            foreach (DataChannelDirection direction in SupportedDirections)
            {
                if (direction is not (DataChannelDirection.OpenerToAcceptor or
                    DataChannelDirection.AcceptorToOpener or DataChannelDirection.Duplex))
                {
                    throw new ArgumentException("Unknown payload direction.", nameof(SupportedDirections));
                }
            }
            foreach (DataChannelDeliveryMode delivery in SupportedDeliveryModes)
            {
                if (delivery is not (DataChannelDeliveryMode.Ordered or DataChannelDeliveryMode.Datagram))
                {
                    throw new ArgumentException("Unknown delivery mode.", nameof(SupportedDeliveryModes));
                }
            }
            foreach (DataChannelProtectionMode protection in SupportedProtectionModes)
            {
                if (protection is not (DataChannelProtectionMode.Confidential or DataChannelProtectionMode.Integrity))
                {
                    throw new ArgumentException("Unknown protection mode.", nameof(SupportedProtectionModes));
                }
            }
        }

        /// <summary>
        /// Compares advertised values without coercion or normalization.
        /// </summary>
        /// <typeparam name="T">The advertised value type.</typeparam>
        internal static bool Contains<T>(ArrayOf<T> values, T value)
        {
            foreach (T candidate in values)
            {
                if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(candidate, value))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Validates a bounded absolute URI without resolving it.
        /// </summary>
        private static void ValidateUri(string uri, string parameter)
        {
            if (string.IsNullOrEmpty(uri) || Encoding.UTF8.GetByteCount(uri) > 4096 ||
                !Uri.TryCreate(uri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("An absolute URI of at most 4096 UTF-8 bytes is required.", parameter);
            }
        }

        private readonly ArrayOf<ExpandedNodeId> m_contentNodeIds;
        private readonly ArrayOf<string> m_protocols = [DataChannelProfiles.OpaquePayload];
        private readonly ArrayOf<DataChannelDirection> m_directions = [DataChannelDirection.AcceptorToOpener];
        private readonly ArrayOf<DataChannelDeliveryMode> m_delivery = [DataChannelDeliveryMode.Ordered];
        private readonly ArrayOf<DataChannelProtectionMode> m_protection = [DataChannelProtectionMode.Confidential];
    }

    /// <summary>
    /// Application-specific permission checks, in addition to mandatory model and Session checks.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public interface IDataChannelSourcePolicy
    {
        /// <summary>
        /// Admits the exact resource, application pair, user, direction and complete proposal.
        /// A null Session denotes configured application authorization, not anonymous Session access.
        /// </summary>
        ValueTask<DataChannelResult> AuthorizeAsync(
            DataChannelSourceAccess access,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Immutable inputs to a source policy, with a defensive descriptor snapshot.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelSourceAccess
    {
        /// <summary>
        /// Creates a policy request. A null context permits source discovery or a non-authorizing offer only.
        /// </summary>
        internal DataChannelSourceAccess(
            DataChannelSource source,
            DataChannelContextIdentity? context,
            ISession? session,
            bool isLocalOpener,
            DataChannelAuthorizationPurpose purpose,
            DataChannelParametersDataType parameters)
        {
            Source = source;
            Context = context;
            Session = session;
            IsLocalOpener = isLocalOpener;
            Purpose = purpose;
            m_parameters = DataChannelControlModel.Copy(parameters);
        }

        /// <summary>
        /// Gets the exact registered source.
        /// </summary>
        public DataChannelSource Source { get; }

        /// <summary>
        /// Gets verified data-context evidence, or null when checking source discovery or an offer only.
        /// </summary>
        public DataChannelContextIdentity? Context { get; }

        /// <summary>
        /// Gets the activated authorizing Session, or null for configured authorization.
        /// </summary>
        public ISession? Session { get; }

        /// <summary>
        /// Gets the local flow-opener role, independently of connection and Publisher roles.
        /// </summary>
        public bool IsLocalOpener { get; }

        /// <summary>
        /// Gets the admission phase.
        /// </summary>
        public DataChannelAuthorizationPurpose Purpose { get; }

        /// <summary>
        /// Gets the proposed complete descriptor.
        /// </summary>
        public DataChannelParametersDataType Parameters => DataChannelControlModel.Copy(m_parameters);

        private readonly DataChannelParametersDataType m_parameters;
    }

    /// <summary>
    /// Explicit application allow-list policy for a resource. Session node permissions still apply.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelApplicationPolicy : IDataChannelSourcePolicy
    {
        /// <summary>
        /// Creates a policy for exact authenticated peer application URIs.
        /// </summary>
        public DataChannelApplicationPolicy(ArrayOf<string> peerApplicationUris)
        {
            if (peerApplicationUris.Count == 0)
            {
                throw new ArgumentException("At least one peer application is required.", nameof(peerApplicationUris));
            }
            m_peers = peerApplicationUris.ToArray() ?? [];
        }

        /// <inheritdoc/>
        public ValueTask<DataChannelResult> AuthorizeAsync(
            DataChannelSourceAccess access,
            CancellationToken cancellationToken = default)
        {
            if (access == null)
            {
                throw new ArgumentNullException(nameof(access));
            }
            cancellationToken.ThrowIfCancellationRequested();
            string? peer = access.Context?.Peer.PeerApplicationUri ?? access.Session?.ClientApplicationUri;
            return new ValueTask<DataChannelResult>(
                peer != null && DataChannelSource.Contains(m_peers, peer)
                    ? access.Source.Validate(access.Parameters)
                    : DataChannelResult.NotAuthorized);
        }

        private readonly ArrayOf<string> m_peers;
    }
}
