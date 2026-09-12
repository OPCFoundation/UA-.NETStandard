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
    /// Parent-owned UaLens modules reserve event IDs 500-999.
    /// IDs 550-999 remain available within this allocation.
    /// </summary>
    internal static partial class UaLensEventIds
    {
        /// <summary>
        /// ChannelV2EngineAdapter reserves event IDs 500-509.
        /// </summary>
        public const int ChannelV2EngineAdapter = 500;

        /// <summary>
        /// ClassicEngineAdapter reserves event IDs 510-519.
        /// </summary>
        public const int ClassicEngineAdapter = 510;

        /// <summary>
        /// DiscoveryService reserves event IDs 520-529.
        /// </summary>
        public const int DiscoveryService = 520;

        /// <summary>
        /// SubscriptionViewModel reserves event IDs 530-549.
        /// </summary>
        public const int SubscriptionViewModel = 530;
        public const int CertificateStoreService = 550;
        public const int NodeSetExporter = 560;
        public const int BrowserViewModel = 570;
        public const int NodeAttributesViewModel = 580;
        public const int ReferencesViewModel = 590;
    }
}
