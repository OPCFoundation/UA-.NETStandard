/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Declarative description of a transcode: the target encoding, the
    /// ordered transform pipeline applied to every message, and the
    /// target format options. A spec is pure data — it carries no
    /// security keys or transport bindings, so it can be reused across
    /// connections and unit-tested in isolation.
    /// </summary>
    public sealed record TranscodeSpec
    {
        /// <summary>
        /// Target NetworkMessage encoding produced by the transcode.
        /// Defaults to <see cref="TranscodeEncoding.Uadp"/>.
        /// </summary>
        public TranscodeEncoding TargetEncoding { get; init; } = TranscodeEncoding.Uadp;

        /// <summary>
        /// Ordered transform pipeline applied to each source message
        /// before the profile projection. An empty pipeline is an
        /// identity transform (a candidate for the raw-frame fast path).
        /// </summary>
        public ArrayOf<IPubSubMessageTransform> Transforms { get; init; } = [];

        /// <summary>
        /// Format-level options controlling how the target concrete
        /// record is materialised.
        /// </summary>
        public TranscodeTargetOptions TargetOptions { get; init; }
            = TranscodeTargetOptions.Default;

        /// <summary>
        /// Selection of DataSet fields to promote into target transport
        /// message properties (e.g. MQTT User Properties). When
        /// <see langword="null"/> or empty no fields are promoted.
        /// </summary>
        public TranscodePromotion? Promotion { get; init; }

        /// <summary>
        /// Returns <see langword="true"/> when the pipeline performs no
        /// structural transformation (no transforms and default target
        /// options), so a same-encoding transcode can take the raw-frame
        /// passthrough fast path.
        /// </summary>
        public bool IsIdentity
            => Transforms.Count == 0 &&
                TargetOptions.FieldEncoding is null &&
                !TargetOptions.JsonSingleMessageMode &&
                !(Promotion?.HasFields ?? false);
    }
}
