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
using System.Numerics;
using OpenUsd;
using OpenUsd.Geom;
using OpenUsd.Rendering;

namespace Opc.Ua.Vision.OpenUsd.Rendering
{
    /// <summary>
    /// Resolves a <see cref="CameraState"/> from a <c>UsdGeomCamera</c>
    /// prim on an already-opened USD stage. This is the port of the probe's
    /// <c>ResolveCameraStateFromPrim</c>: the OpenUSD Silk API has no
    /// by-prim-path camera overload, so the rendering component has to
    /// build the view / projection matrices itself.
    /// </summary>
    internal static class CameraStateResolver
    {
        /// <summary>
        /// Reads the camera prim at <paramref name="primPath"/> from
        /// <paramref name="stage"/> and returns the matrix-based
        /// <see cref="CameraState"/> ready for
        /// <c>SilkFrameCapture.Capture</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="stage"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The prim does not exist, is not a <c>UsdGeomCamera</c>, or its
        /// world transform is singular.
        /// </exception>
        public static CameraState ResolveFromPrim(UsdStage stage, string primPath, double timeCode)
        {
            if (stage is null)
            {
                throw new ArgumentNullException(nameof(stage));
            }
            if (string.IsNullOrWhiteSpace(primPath))
            {
                throw new ArgumentException("Prim path must not be empty.", nameof(primPath));
            }

            UsdPrim prim = stage.GetPrim(primPath);
            if (!UsdGeomCamera.TryWrap(prim, out UsdGeomCamera cam))
            {
                throw new InvalidOperationException(
                    $"Prim '{primPath}' is not a UsdGeomCamera.");
            }

            UsdGeomCameraState optics = cam.GetState(timeCode);
            Matrix4x4 world = ToMatrix4x4(cam.GetTransform(timeCode));
            if (!Matrix4x4.Invert(world, out Matrix4x4 view))
            {
                throw new InvalidOperationException(
                    $"Camera '{primPath}' world transform is not invertible.");
            }

            Matrix4x4 projection = optics.Projection == UsdGeomCameraProjection.Perspective
                ? PerspectiveOffCenterRH(
                    (float)optics.WindowLeft,
                    (float)optics.WindowRight,
                    (float)optics.WindowBottom,
                    (float)optics.WindowTop,
                    (float)optics.ClippingNear,
                    (float)optics.ClippingFar)
                : OrthographicOffCenterRH(
                    (float)optics.WindowLeft,
                    (float)optics.WindowRight,
                    (float)optics.WindowBottom,
                    (float)optics.WindowTop,
                    (float)optics.ClippingNear,
                    (float)optics.ClippingFar);

            return new CameraState(view, projection);
        }

        private static Matrix4x4 ToMatrix4x4(UsdMatrix4d m)
        {
            return new Matrix4x4(
                (float)m.M00, (float)m.M01, (float)m.M02, (float)m.M03,
                (float)m.M10, (float)m.M11, (float)m.M12, (float)m.M13,
                (float)m.M20, (float)m.M21, (float)m.M22, (float)m.M23,
                (float)m.M30, (float)m.M31, (float)m.M32, (float)m.M33);
        }

        private static Matrix4x4 PerspectiveOffCenterRH(
            float l, float r, float bt, float t, float n, float f)
        {
            float nl = l * n;
            float nr = r * n;
            float nb = bt * n;
            float nt = t * n;
            Matrix4x4 p = default;
            p.M11 = 2f * n / (nr - nl);
            p.M22 = 2f * n / (nt - nb);
            p.M31 = (nr + nl) / (nr - nl);
            p.M32 = (nt + nb) / (nt - nb);
            p.M33 = -(f + n) / (f - n);
            p.M34 = -1f;
            p.M43 = -(2f * f * n) / (f - n);
            return p;
        }

        private static Matrix4x4 OrthographicOffCenterRH(
            float l, float r, float bt, float t, float n, float f)
        {
            Matrix4x4 p = Matrix4x4.Identity;
            p.M11 = 2f / (r - l);
            p.M22 = 2f / (t - bt);
            p.M33 = -2f / (f - n);
            p.M41 = -(r + l) / (r - l);
            p.M42 = -(t + bt) / (t - bt);
            p.M43 = -(f + n) / (f - n);
            return p;
        }
    }
}
