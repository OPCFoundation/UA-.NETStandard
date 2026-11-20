// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Root configuration for declarative, reloadable transcoding routes.
    /// Binds from an <c>IConfiguration</c> section and is watched through an
    /// <c>IOptionsMonitor</c> so route changes reconfigure only the affected
    /// bridges at runtime.
    /// </summary>
    public sealed class PubSubTranscodingOptions
    {
        /// <summary>
        /// The configured transcoding routes, each producing one in-process
        /// subscriber-to-publisher bridge keyed by its
        /// <see cref="TranscodeRouteOptions.Name"/>.
        /// </summary>
        public IList<TranscodeRouteOptions> Routes { get; init; }
            = [];
    }
}
