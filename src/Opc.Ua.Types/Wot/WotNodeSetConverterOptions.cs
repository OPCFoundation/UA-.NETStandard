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
 *
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

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Controls whether a converter emits the opaque byte-exact
    /// <c>uav:nodeSet</c> preservation envelope.
    /// </summary>
    public enum WotNodeSetPreservationMode
    {
        /// <summary>
        /// Emit the envelope only if the structured native projection cannot
        /// reproduce the source NodeSet.
        /// </summary>
        WhenRequired,

        /// <summary>Always emit the byte-exact preservation envelope.</summary>
        Always,

        /// <summary>
        /// Never emit the envelope; report an error if native projection is not
        /// complete. This mode is intended for conformance and completeness tests.
        /// </summary>
        Never
    }

    /// <summary>
    /// Resource limits and behavioural switches used while reading and
    /// writing WoT documents, preservation envelopes and NodeSet2 payloads.
    /// </summary>
    /// <remarks>
    /// All limits are enforced deliberately so that a malformed or hostile
    /// document cannot exhaust memory or stack. The defaults are generous
    /// enough for real companion specifications yet bounded.
    /// </remarks>
    public sealed class WotNodeSetConverterOptions
    {
        /// <summary>
        /// Gets or sets the preservation-envelope policy. The default is
        /// native-first and emits an envelope only when required.
        /// </summary>
        public WotNodeSetPreservationMode PreservationMode { get; set; } =
            WotNodeSetPreservationMode.WhenRequired;

        /// <summary>
        /// Gets or sets the maximum accepted WoT JSON document size in bytes.
        /// </summary>
        public int MaxJsonDocumentSize { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum accepted or decoded NodeSet2 XML size in bytes.
        /// </summary>
        public int MaxNodeSetSize { get; set; } = 64 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum JSON nesting depth.
        /// </summary>
        public int MaxJsonDepth { get; set; } = 128;

        /// <summary>
        /// Gets or sets the maximum XML nesting depth accepted when reading a
        /// decoded or synthesized NodeSet2 document.
        /// </summary>
        public int MaxXmlDepth { get; set; } = 256;

        /// <summary>
        /// Gets or sets the maximum number of UANode records projected into or
        /// reconstructed from a native <c>uav:nodes</c> projection.
        /// </summary>
        public int MaxNodeCount { get; set; } = 1_000_000;

        /// <summary>
        /// Gets or sets the maximum number of affordances (properties, actions
        /// and events combined) processed for a single Thing.
        /// </summary>
        public int MaxAffordanceCount { get; set; } = 100_000;

        /// <summary>
        /// Gets or sets the maximum external-document resolution depth used
        /// when following contexts, schemas and referenced TD/TM documents.
        /// </summary>
        public int MaxResolverDepth { get; set; } = 16;

        /// <summary>
        /// Gets or sets the maximum number of external documents (contexts,
        /// schemas and referenced TD/TM documents combined) resolved for a
        /// single top-level conversion.
        /// </summary>
        public int MaxResolverDocuments { get; set; } = 256;

        /// <summary>
        /// Gets or sets the maximum accepted size of a single externally
        /// resolved document.
        /// </summary>
        public int MaxResolverDocumentBytes { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum cumulative size of all documents
        /// externally resolved for a single top-level conversion.
        /// </summary>
        public long MaxResolverTotalBytes { get; set; } = 128L * 1024 * 1024;

        /// <summary>
        /// Validates the option values and throws when a limit is not positive.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when any configured limit is not strictly positive.
        /// </exception>
        public void Validate()
        {
            if (PreservationMode is not (
                WotNodeSetPreservationMode.WhenRequired or
                WotNodeSetPreservationMode.Always or
                WotNodeSetPreservationMode.Never))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(PreservationMode),
                    PreservationMode,
                    "The preservation mode is not defined.");
            }
            EnsurePositive(MaxJsonDocumentSize, nameof(MaxJsonDocumentSize));
            EnsurePositive(MaxNodeSetSize, nameof(MaxNodeSetSize));
            EnsurePositive(MaxJsonDepth, nameof(MaxJsonDepth));
            EnsurePositive(MaxXmlDepth, nameof(MaxXmlDepth));
            EnsurePositive(MaxNodeCount, nameof(MaxNodeCount));
            EnsurePositive(MaxAffordanceCount, nameof(MaxAffordanceCount));
            EnsurePositive(MaxResolverDepth, nameof(MaxResolverDepth));
            EnsurePositive(MaxResolverDocuments, nameof(MaxResolverDocuments));
            EnsurePositive(MaxResolverDocumentBytes, nameof(MaxResolverDocumentBytes));
            if (MaxResolverTotalBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxResolverTotalBytes),
                    MaxResolverTotalBytes,
                    "The configured limit must be a positive value.");
            }
        }

        /// <summary>
        /// Projects the aggregate resolver limits configured on this instance
        /// onto a <see cref="WotResolverOptions"/> suitable for seeding a
        /// single <see cref="WotResolutionContext"/> per top-level conversion.
        /// </summary>
        /// <returns>The equivalent bounded resolution options.</returns>
        public WotResolverOptions ToResolverOptions()
        {
            return new WotResolverOptions
            {
                MaxDepth = MaxResolverDepth,
                MaxDocuments = MaxResolverDocuments,
                MaxDocumentBytes = MaxResolverDocumentBytes,
                MaxTotalBytes = MaxResolverTotalBytes
            };
        }

        private static void EnsurePositive(int value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "The configured limit must be a positive value.");
            }
        }
    }
}
