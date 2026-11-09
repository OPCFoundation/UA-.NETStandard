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
 * WHETHER IN CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR
 * IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NUnit.Framework;
using Opc.Ua;

namespace UaLens.Tests.Desktop
{
    /// <summary>
    /// Captures a framed, live native window's client visual; excludes operating-system chrome.
    /// Pixel checks reject blank plot captures and missing or clipped semantic labels.
    /// </summary>
    internal static class DesktopCapture
    {
        internal static async Task SaveAsync(
            Window window,
            string path,
            ArrayOf<Color> expectedColors,
            Control? region = null)
        {
            Assert.That(window.IsVisible, Is.True);
            Assert.That(window.TryGetPlatformHandle()?.Handle, Is.Not.Null.And.Not.EqualTo(IntPtr.Zero));
            await DesktopWindowScope.FrameAsync(window).ConfigureAwait(true);
            double scale = Math.Min(window.RenderScaling,
                Math.Min(1600 / window.ClientSize.Width, 1200 / window.ClientSize.Height));
            var size = new PixelSize(
                (int)Math.Ceiling(window.ClientSize.Width * scale),
                (int)Math.Ceiling(window.ClientSize.Height * scale));
            Assert.That(size.Width, Is.InRange(1, 1600));
            Assert.That(size.Height, Is.InRange(1, 1200));
            using var bitmap = new RenderTargetBitmap(size, new Avalonia.Vector(96 * scale, 96 * scale));
            var textOptions = TextOptions.GetTextOptions(window);
            try
            {
                // An exported PNG has no LCD subpixel layout; preserve its semantic colors without RGB fringes.
                TextOptions.SetTextRenderingMode(window, TextRenderingMode.Antialias);
                bitmap.Render(window);
            }
            finally
            {
                TextOptions.SetTextOptions(window, textOptions);
            }
            using var encoded = new MemoryStream();
            bitmap.Save(encoded, PngBitmapEncoderOptions.Default);
            Assert.That(encoded.Length, Is.InRange(1, 8 * 1024 * 1024), "Capture exceeds its 8 MiB bound.");
            encoded.Position = 0;
            using var decoded = new Bitmap(encoded);
            Rect bounds = new(window.ClientSize);
            if (region is not null)
            {
                Assert.That(region.IsEffectivelyVisible, Is.True);
                Point? origin = region.TranslatePoint(default, window);
                Assert.That(origin, Is.Not.Null);
                bounds = new Rect(origin!.Value, region.Bounds.Size);
                Assert.That(new Rect(window.ClientSize).Contains(bounds), Is.True,
                    "The expected semantic label is clipped by the window.");
            }
            await File.WriteAllBytesAsync(path, encoded.ToArray()).ConfigureAwait(true);
            TestContext.AddTestAttachment(path,
                $"Candidate native client visual; native scale {window.RenderScaling}; capture scale {scale}; {size}. " +
                "The associated test must pass pixel verification before this is accepted as capture evidence.");
            VerifyPixels(decoded, expectedColors, bounds, scale);
        }

        private static void VerifyPixels(Bitmap bitmap, ArrayOf<Color> expectedColors, Rect bounds, double scale)
        {
            Assert.That(bitmap.Format == PixelFormat.Bgra8888 || bitmap.Format == PixelFormat.Rgba8888, Is.True,
                "The PNG decoder must expose a supported 32-bit pixel format.");
            int stride = checked(bitmap.PixelSize.Width * 4);
            byte[] pixels = new byte[checked(stride * bitmap.PixelSize.Height)];
            var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                    pin.AddrOfPinnedObject(), pixels.Length, stride);
            }
            finally
            {
                pin.Free();
            }
            bool bgra = bitmap.Format == PixelFormat.Bgra8888;
            int right = Math.Min(bitmap.PixelSize.Width, (int)Math.Ceiling(bounds.Right * scale));
            int bottom = Math.Min(bitmap.PixelSize.Height, (int)Math.Ceiling(bounds.Bottom * scale));
            foreach (Color expected in expectedColors)
            {
                int found = 0;
                int closestDistance = int.MaxValue;
                Color closest = Colors.Transparent;
                for (int y = Math.Max(0, (int)(bounds.Y * scale)); y < bottom && found < 8; y++)
                {
                    for (int x = Math.Max(0, (int)(bounds.X * scale)); x < right && found < 8; x++)
                    {
                        int offset = (y * stride) + (x * 4);
                        if (pixels[offset + 3] != 255)
                        {
                            continue;
                        }
                        byte red = pixels[offset + (bgra ? 2 : 0)];
                        byte green = pixels[offset + 1];
                        byte blue = pixels[offset + (bgra ? 0 : 2)];
                        int distance = Math.Max(Math.Abs(red - expected.R),
                            Math.Max(Math.Abs(green - expected.G), Math.Abs(blue - expected.B)));
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closest = Color.FromRgb(red, green, blue);
                        }
                        if (distance <= 2)
                        {
                            found++;
                        }
                    }
                }
                Assert.That(found, Is.GreaterThanOrEqualTo(8),
                    $"The rendered PNG lacks visible pixels of {expected} in {bounds}. " +
                    $"Closest opaque color: {closest}; maximum channel difference: {closestDistance}; " +
                    $"bitmap: {bitmap.PixelSize}, {bitmap.Format}, {bitmap.AlphaFormat}; capture scale: {scale}.");
            }
        }
    }
}
