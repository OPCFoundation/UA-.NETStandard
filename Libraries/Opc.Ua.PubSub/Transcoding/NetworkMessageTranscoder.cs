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
        private readonly TranscodeSpec m_spec;
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
            m_spec = spec ?? throw new ArgumentNullException(nameof(spec));
            m_projector = projector ?? NetworkMessageProfileProjector.Instance;
        }

        /// <summary>
        /// The specification this transcoder applies.
        /// </summary>
        public TranscodeSpec Spec => m_spec;

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
            ArrayOf<IPubSubMessageTransform> transforms = m_spec.Transforms;
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
                current, m_spec.TargetEncoding, m_spec.TargetOptions, context);
            return [projected];
        }
    }
}
