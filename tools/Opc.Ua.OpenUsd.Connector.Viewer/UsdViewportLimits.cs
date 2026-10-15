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
using OpenUsd.Rendering;

namespace Opc.Ua.OpenUsd.Connector.Viewer
{
    /// <summary>
    /// Hard limits checked before allocating presentation, capture, or retained scene resources.
    /// Texture source images must additionally be validated by the host before opening the stage.
    /// </summary>
    public sealed record UsdViewportLimits
    {
        /// <summary>
        /// Gets the maximum physical width or height, independent of desktop scaling.
        /// </summary>
        public int MaximumDimension { get; init; } = 2048;

        /// <summary>
        /// Gets the maximum bytes in one detached RGBA8 capture.
        /// </summary>
        public int MaximumFrameBytes { get; init; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets the maximum live color/depth ring bytes, including a retiring resize generation.
        /// </summary>
        public long MaximumPresentationBytes { get; init; } = 192L * 1024 * 1024;

        /// <summary>
        /// Gets the maximum number of retained meshes.
        /// </summary>
        public int MaximumMeshes { get; init; } = 4096;

        /// <summary>
        /// Gets the conservative retained geometry byte bound checked before page application.
        /// </summary>
        public long MaximumGeometryBytes { get; init; } = 64L * 1024 * 1024;

        /// <summary>
        /// Validates immutable host configuration without opening native resources.
        /// </summary>
        public void Validate()
        {
            if (MaximumDimension is < 1 or > 4096 ||
                MaximumFrameBytes is < 4 or > 64 * 1024 * 1024 ||
                MaximumPresentationBytes is < 16 or > 512L * 1024 * 1024 ||
                MaximumMeshes is < 1 or > 4096 ||
                MaximumGeometryBytes is < 1 or > 256L * 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumDimension), "Invalid viewport resource limits.");
            }
        }

        /// <summary>
        /// Validates physical dimensions before either a native texture or managed readback is allocated.
        /// </summary>
        public int RequireFrame(ViewportDimensions size)
        {
            if (size.Width < 1 || size.Height < 1 ||
                size.Width > MaximumDimension || size.Height > MaximumDimension ||
                (long)size.Width * size.Height * 4 > MaximumFrameBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(size),
                    "The viewport exceeds its physical-pixel or RGBA8 byte limit.");
            }
            return checked(size.Width * size.Height * 4);
        }
    }

    /// <summary>
    /// Detached evidence for the latest retained viewport frame and its active native allocations.
    /// </summary>
    public sealed record UsdViewportStatistics(
        int Width,
        int Height,
        long RenderedFrames,
        ulong SceneRevision,
        int DrawCount,
        int MeshCount,
        int ActiveGenerations,
        int ActiveFrames);
}
