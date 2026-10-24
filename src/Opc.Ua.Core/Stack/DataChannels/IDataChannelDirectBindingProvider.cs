// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Supplies transport-session facts and exporter material for optional direct binding.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public interface IDataChannelDirectBindingProvider
    {
        /// <summary>
        /// Gets whether the selected carrier can compute a session-specific exporter.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Gets the exact selected transport profile URI.
        /// </summary>
        string TransportProfileUri { get; }

        /// <summary>
        /// Gets the explicitly selected direct-delivery capability.
        /// </summary>
        string Capability { get; }

        /// <summary>
        /// Gets the exact configured endpoint authority.
        /// </summary>
        string Authority { get; }

        /// <summary>
        /// Gets the exact configured endpoint path, including its query.
        /// </summary>
        string Path { get; }

        /// <summary>
        /// Computes 32 bytes of exporter material using the transcript digest as context.
        /// The returned value must use an independently owned array buffer; the receiver clears it after use.
        /// </summary>
        ValueTask<ByteString> ExportKeyingMaterialAsync(
            ByteString transcriptDigest,
            CancellationToken cancellationToken = default);
    }
}
