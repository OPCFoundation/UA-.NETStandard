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

namespace BinPickingClient
{
    internal static class BinPickingClientEventIds
    {
        public const int Connected = 7700;
        public const int McpCatalogueSize = 7701;
        public const int McpHostStarted = 7702;
        public const int DemoStageStarted = 7703;
        public const int DemoDetections = 7704;
        public const int DemoPoseComposed = 7705;
        public const int DemoPickSubmitted = 7706;
        public const int DemoPickCompleted = 7707;
        public const int DemoPickRefused = 7708;
        public const int DemoPlaceSubmitted = 7709;
        public const int DemoPlaceCompleted = 7710;
        public const int DemoPlaceRefused = 7711;
        public const int DemoPostPickDetections = 7712;
        public const int DemoLoopComplete = 7713;
        public const int DemoUnknownClass = 7714;
        public const int DemoAuthorityNotGranted = 7715;
        public const int DemoWorldStateUnchanged = 7716;
        public const int DemoWorldStateChanged = 7717;
        public const int DemoPipelineUnavailable = 7718;
    }
}
