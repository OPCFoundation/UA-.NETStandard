/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using OpenUsd.Rendering.Silk;

namespace Opc.Ua.Vision.OpenUsd.Rendering
{
    /// <summary>
    /// Result of a <see cref="BlankFrameGuard"/> check.
    /// </summary>
    internal readonly record struct BlankFrameCheck(
        bool IsBlank,
        int DrawCount,
        int MeshCount,
        bool IsUniform,
        string? Reason);

    /// <summary>
    /// Detects the "silently rendered nothing" failure mode of the OpenUSD
    /// Silk backend. The probe report calls this out as the worst possible
    /// failure mode for a vision system, so the provider refuses to surface
    /// a blank frame as if it succeeded.
    /// </summary>
    /// <remarks>
    /// The check is defensive in depth: even though every capture uses a
    /// fresh session (which avoids the known session-reuse landmine), the
    /// guard runs anyway so a future backend regression or an unrelated
    /// crash-into-black cannot go unreported.
    /// </remarks>
    internal static class BlankFrameGuard
    {
        /// <summary>
        /// Inspects <paramref name="result"/> and its RGBA8 pixel buffer.
        /// Reports <see cref="BlankFrameCheck.IsBlank"/> <c>true</c> when
        /// either the render pipeline drew nothing, or every pixel in the
        /// buffer has the exact same value.
        /// </summary>
        public static BlankFrameCheck Check(SilkFrameCaptureResult result)
        {
            if (result is null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            int drawCount = result.RenderResult.DrawCount;
            int meshCount = result.RenderResult.Statistics.MeshCount;

            if (drawCount == 0 || meshCount == 0)
            {
                return new BlankFrameCheck(
                    IsBlank: true,
                    DrawCount: drawCount,
                    MeshCount: meshCount,
                    IsUniform: false,
                    Reason: "render pipeline reported no drawn geometry " +
                        $"(drawCount={drawCount}, meshCount={meshCount})");
            }

            bool uniform = IsUniformRgba8(result.Rgba.Span);
            if (uniform)
            {
                return new BlankFrameCheck(
                    IsBlank: true,
                    DrawCount: drawCount,
                    MeshCount: meshCount,
                    IsUniform: true,
                    Reason: "every pixel in the returned RGBA8 buffer has the same value");
            }

            return new BlankFrameCheck(false, drawCount, meshCount, false, null);
        }

        private static bool IsUniformRgba8(ReadOnlySpan<byte> rgba)
        {
            if (rgba.Length < 4)
            {
                return true;
            }
            uint first = ReadRgba(rgba, 0);
            for (int i = 4; i < rgba.Length; i += 4)
            {
                if (ReadRgba(rgba, i) != first)
                {
                    return false;
                }
            }
            return true;
        }

        private static uint ReadRgba(ReadOnlySpan<byte> rgba, int offset)
        {
            return ((uint)rgba[offset] << 24)
                | ((uint)rgba[offset + 1] << 16)
                | ((uint)rgba[offset + 2] << 8)
                | rgba[offset + 3];
        }
    }
}
