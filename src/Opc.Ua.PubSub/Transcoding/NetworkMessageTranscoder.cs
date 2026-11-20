// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Default <see cref="INetworkMessageTranscoder"/>. Runs the
    /// configured transform pipeline over the decoded message and then
    /// projects the result to the target encoding. Stateless apart from
    /// the immutable <see cref="TranscodeSpec"/> and projector, so a
    /// single instance is safe to share.
    /// </summary>
    public sealed class NetworkMessageTranscoder : INetworkMessageTranscoder
    {
        private readonly INetworkMessageProfileProjector m_projector;

        /// <summary>
        /// Initializes a new <see cref="NetworkMessageTranscoder"/>.
        /// </summary>
        /// <param name="spec">Transcode specification.</param>
        /// <param name="projector">
        /// Profile projector, or <see langword="null"/> to use
        /// <see cref="NetworkMessageProfileProjector.Instance"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="spec"/> is <see langword="null"/>.
        /// </exception>
        public NetworkMessageTranscoder(
            TranscodeSpec spec,
            INetworkMessageProfileProjector? projector = null)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            m_projector = projector ?? NetworkMessageProfileProjector.Instance;
        }

        /// <summary>
        /// The specification this transcoder applies.
        /// </summary>
        public TranscodeSpec Spec { get; }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<PubSubNetworkMessage>> TranscodeAsync(
            PubSubNetworkMessage source,
            TranscodeContext context,
            CancellationToken cancellationToken = default)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            PubSubNetworkMessage? current = source;
            ArrayOf<IPubSubMessageTransform> transforms = Spec.Transforms;
            for (int i = 0; i < transforms.Count; i++)
            {
                current = await transforms[i]
                    .TransformAsync(current, context, cancellationToken)
                    .ConfigureAwait(false);
                if (current is null)
                {
                    return [];
                }
            }

            PubSubNetworkMessage projected = m_projector.Project(
                current, Spec.TargetEncoding, Spec.TargetOptions, context);
            return [projected];
        }
    }
}
