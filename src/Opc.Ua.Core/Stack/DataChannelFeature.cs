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

namespace Opc.Ua
{
    /// <summary>
    /// Marks the DataChannel Service Set as experimental.
    /// </summary>
    /// <remarks>
    /// The Services themselves are generated into the standard type surface
    /// and are reachable from any Client, while the engine that carries the
    /// frames ships separately in
    /// <c>OPCFoundation.NetStandard.Opc.Ua.Core.Channels</c>. The marker
    /// therefore lives here, where both the Services and their callers can see
    /// it: every identifier the feature uses is provisional and will change if
    /// and when the OPC Foundation assigns final values.
    /// </remarks>
    public static class DataChannelFeature
    {
        /// <summary>
        /// The diagnostic id reported for the experimental DataChannel API.
        /// </summary>
        public const string ExperimentalDiagnosticId = "DataChannels";

        /// <summary>
        /// The documentation url reported alongside
        /// <see cref="ExperimentalDiagnosticId"/>.
        /// </summary>
        public const string ExperimentalUrlFormat =
            "https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/DataChannels.md";
    }
}
