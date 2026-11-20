/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
    /// Retained and offscreen capture callers both run this guard so a backend
    /// regression or a scene without visible geometry cannot go unreported.
    /// </remarks>
    internal static class BlankFrameGuard
    {
        /// <summary>
        /// Inspects <paramref name="result"/> and its RGBA8 pixel buffer.
        /// Reports <see cref="BlankFrameCheck.IsBlank"/> <c>true</c> when
        /// either the render pipeline drew nothing, or every pixel in the
        /// buffer has the exact same value.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static BlankFrameCheck Check(SilkFrameCaptureResult result)
        {
            if (result is null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return Check(result.RenderResult.DrawCount, result.RenderResult.Statistics.MeshCount, result.Rgba.Span);
        }

        /// <summary>
        /// Checks a detached retained-frame readback without manufacturing a renderer capture result.
        /// </summary>
        public static BlankFrameCheck Check(int drawCount, int meshCount, ReadOnlySpan<byte> rgba)
        {
            if (drawCount <= 0 || meshCount <= 0)
            {
                return new BlankFrameCheck(
                    IsBlank: true,
                    DrawCount: drawCount,
                    MeshCount: meshCount,
                    IsUniform: false,
                    Reason: "render pipeline reported no drawn geometry " +
                        $"(drawCount={drawCount}, meshCount={meshCount})");
            }

            bool uniform = IsUniformRgba8(rgba);
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
            if (rgba.Length < 4 || rgba.Length % 4 != 0)
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
            return ((uint)rgba[offset] << 24) |
                ((uint)rgba[offset + 1] << 16) |
                ((uint)rgba[offset + 2] << 8) |
                rgba[offset + 3];
        }
    }
}
