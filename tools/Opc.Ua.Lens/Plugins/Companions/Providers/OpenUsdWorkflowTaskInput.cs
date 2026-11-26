// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Security.Cryptography;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.OpenUsd.Client;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Captured input, not a recipe restored from workspace state. Session ownership remains with the host.
    /// </summary>
    internal sealed class OpenUsdWorkflowTaskInput : CompanionTaskInput
    {
        public OpenUsdWorkflowTaskInput(
            CompanionContext context,
            CompanionTarget target,
            string operationId,
            ByteString metadataDigest,
            int maximumSamples,
            TimeSpan duration,
            DateTime start,
            DateTime end,
            string? destination,
            string review,
            ArrayOf<OpenUsdWorkflowOrigin> origins = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(target);
            if (metadataDigest.Length != 32 ||
                maximumSamples is < 1 or > 128 ||
                duration < TimeSpan.Zero ||
                duration > TimeSpan.FromSeconds(15) ||
                origins.Count > 16)
            {
                throw new ArgumentException("Invalid bounded OpenUSD preparation.");
            }
            m_session = context.Session;
            SessionId = context.Session.SessionId;
            m_source = new CompanionOperationDraft(
                target, new CompanionOperation(operationId, operationId, CompanionOperationSafety.ReadOnly),
                null, context.Session, DateTimeOffset.MaxValue);
            m_serverCertificate = context.Session.Endpoint.ServerCertificate.Copy();
            m_metadataDigest = metadataDigest.Copy();
            Target = target;
            OperationId = operationId;
            MaximumSamples = maximumSamples;
            Duration = duration;
            Start = start;
            End = end;
            Destination = destination;
            CorrelationId = Guid.NewGuid();
            m_origins = SnapshotOrigins(origins);
            Review = $"{review}\nSource: {target.NodeId}; session generation: {SessionId}.\n" +
                $"Metadata SHA-256: {Convert.ToHexString(metadataDigest.Span)}.\n" +
                $"Correlation: {CorrelationId}. No command, renderer or external URI resolver is used.";
        }

        public override string Review { get; }

        public CompanionTarget Target { get; }

        public string OperationId { get; }

        public NodeId SessionId { get; }

        public ByteString MetadataDigest => m_metadataDigest.Copy();

        public int MaximumSamples { get; }

        public TimeSpan Duration { get; }

        public DateTime Start { get; }

        public DateTime End { get; }

        public string? Destination { get; }

        public Guid CorrelationId { get; }

        public ArrayOf<OpenUsdWorkflowOrigin> Origins => SnapshotOrigins(m_origins);

        public void RequireMatch(CompanionContext context, CompanionTarget target, string operationId)
        {
            if (!ReferenceEquals(context.Session, m_session) ||
                context.Session.SessionId != SessionId ||
                target != Target ||
                operationId != OperationId ||
                !m_source.Matches(context.Session, TimeProvider.System.GetUtcNow()) ||
                context.Session.Endpoint.ServerCertificate != m_serverCertificate)
            {
                throw new InvalidOperationException("The OpenUSD source generation changed. Prepare again.");
            }
            CellCompanionSupport.CheckCount(
                MaximumSamples, OpenUsdWorkflowTasks.SampleLimit(context), "Prepared OpenUSD samples");
        }

        public void RequireMetadata(CompanionContext context, OpenUsdConnector.RepresentationInfo representation)
        {
            ByteString current = OpenUsdWorkflowMetadata.Digest(context, representation);
            if (!CryptographicOperations.FixedTimeEquals(current.Span, m_metadataDigest.Span))
            {
                throw new InvalidOperationException("OpenUSD binding or dependency metadata changed. Prepare again.");
            }
        }

        private static ArrayOf<OpenUsdWorkflowOrigin> SnapshotOrigins(ArrayOf<OpenUsdWorkflowOrigin> origins)
        {
            return origins.ConvertAll(origin => origin with
            {
                MetadataDigest = origin.MetadataDigest.Copy(),
                SecurityDigest = origin.SecurityDigest.Copy()
            });
        }

        private readonly ISession m_session;
        private readonly CompanionOperationDraft m_source;
        private readonly ByteString m_serverCertificate;
        private readonly ArrayOf<OpenUsdWorkflowOrigin> m_origins;
        private readonly ByteString m_metadataDigest;
    }

    /// <summary>
    /// Fixed-size native-safe encoding captures every connector field, including conversion and command metadata.
    /// </summary>
    internal static class OpenUsdWorkflowMetadata
    {
        public static ByteString Digest(CompanionContext context, OpenUsdConnector.RepresentationInfo representation)
        {
            using var buffer = new IndustrialDocumentBuffer(MaximumBytes);
            using var encoder = new BinaryEncoder(buffer, context.Session.MessageContext, leaveOpen: true);
            encoder.SaveStringTable(context.Session.NamespaceUris);
            encoder.WriteNodeId(null, representation.NodeId);
            encoder.WriteNodeId(null, representation.StageNodeId);
            encoder.WriteString(null, representation.PrimPath);
            encoder.WriteString(null, representation.RootLayerIdentifier);
            encoder.WriteByteString(null, representation.RootLayerDigest);
            encoder.WriteInt32(null, (int)representation.DigestAlgorithm);
            encoder.WriteInt32(null, representation.Bindings.Count);
            foreach (OpenUsdConnector.BindingInfo binding in representation.Bindings)
            {
                encoder.WriteNodeId(null, binding.SourceNodeId);
                encoder.WriteString(null, binding.PrimPath);
                encoder.WriteString(null, binding.PropertyName);
                encoder.WriteInt32(null, (int)binding.Kind);
                encoder.WriteDouble(null, binding.Scale);
                encoder.WriteDouble(null, binding.Offset);
                encoder.WriteInt32(null, (int)binding.Intent);
                encoder.WriteInt32(null, (int)binding.SignalRole);
                encoder.WriteString(null, binding.SourceSemanticId);
                encoder.WriteInt32(null, binding.AlarmAspect.HasValue ? (int)binding.AlarmAspect.Value : -1);
                encoder.WriteBoolean(null, binding.TimeSampled);
                encoder.WriteBoolean(null, binding.Enabled);
                encoder.WriteGuid(null, binding.BindingDefinitionId);
                encoder.WriteNodeId(null, binding.CommandTargetNodeId);
                encoder.WriteNodeId(null, binding.CommandMethodId);
                encoder.WriteString(null, binding.CommandTriggerPropertyName);
                encoder.WriteBoolean(null, binding.SourceBrowsePath is not null);
                if (binding.SourceBrowsePath is not null)
                {
                    encoder.WriteEncodeable(null, binding.SourceBrowsePath);
                }
                encoder.WriteBoolean(null, binding.SourceEngineeringUnits is not null);
                if (binding.SourceEngineeringUnits is not null)
                {
                    encoder.WriteEncodeable(null, binding.SourceEngineeringUnits);
                }
                encoder.WriteBoolean(null, binding.TargetEngineeringUnits is not null);
                if (binding.TargetEngineeringUnits is not null)
                {
                    encoder.WriteEncodeable(null, binding.TargetEngineeringUnits);
                }
            }
            encoder.WriteInt32(null, representation.Components.Count);
            foreach (OpenUsdConnector.ComponentInfo component in representation.Components)
            {
                encoder.WriteNodeId(null, component.NodeId);
                encoder.WriteInt32(null, (int)component.Cardinality);
                encoder.WriteInt32(null, (int)component.Arc);
                encoder.WriteNodeId(null, component.ComponentReferenceType);
                encoder.WriteNodeId(null, component.ComponentTypeDefinition);
                encoder.WriteString(null, component.TargetPrimPath);
                encoder.WriteString(null, component.TargetPrimNameSource);
                encoder.WriteString(null, component.ComponentAssetReference);
                encoder.WriteNodeId(null, component.ComponentRepresentation);
                encoder.WriteBoolean(null, component.Dynamic);
                encoder.WriteNodeId(null, component.ChangeEventSource);
                encoder.WriteString(null, component.ComponentServerUri);
                encoder.WriteString(null, component.ComponentEndpointUrl);
                encoder.WriteBoolean(null, component.Enabled);
                encoder.WriteGuid(null, component.BindingDefinitionId);
            }
            byte[] bytes = encoder.CloseAndReturnBuffer() ??
                throw new InvalidOperationException("OpenUSD metadata encoding returned no bytes.");
            return new ByteString(SHA256.HashData(bytes));
        }

        public static ByteString EncodeSample(
            CompanionContext context, in DataValue source, Variant converted)
        {
            using var buffer = new IndustrialDocumentBuffer(MaximumSampleBytes);
            using var encoder = new BinaryEncoder(buffer, context.Session.MessageContext, leaveOpen: true);
            encoder.WriteDataValue(null, source);
            encoder.WriteVariant(null, converted);
            return ByteString.From(encoder.CloseAndReturnBuffer());
        }

        internal const int MaximumBytes = 128 * 1024;
        internal const int MaximumSampleBytes = 16 * 1024;
        internal const int MaximumTotalSampleBytes = 1024 * 1024;
    }
}
