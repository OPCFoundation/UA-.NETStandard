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
using System.Collections.Generic;
using System.IO;

namespace Vision.VisualInspectionCell
{
    /// <summary>
    /// Measures the bracket geometry from pixels instead of from fixture names.
    /// </summary>
    internal sealed class FixtureImageAnalyzer
    {
        public IReadOnlyList<MeasuredCharacteristic> Measure(string fixturePath)
        {
            if (string.IsNullOrWhiteSpace(fixturePath))
            {
                throw new ArgumentException("A fixture path is required.", nameof(fixturePath));
            }

            byte[] png = File.ReadAllBytes(fixturePath);
            (byte[] rgb, int width, int height) = PngDecoder.Decode(png);
            if (width != ImageWidth || height != ImageHeight)
            {
                throw new InvalidDataException($"Expected {ImageWidth}x{ImageHeight}, got {width}x{height}.");
            }

            int borePixels = CountDarkRunThrough(rgb, width, BoreCenterY, BoreCenterX);
            PixelRun slot = FindDarkRunRightOf(rgb, width, SlotCenterY, minX: 400);
            double bore = borePixels / ScalePixelsPerMillimetre;
            double slotWidth = slot.Length / ScalePixelsPerMillimetre;
            double edgeOffset = (BracketRightX - slot.StartX) / ScalePixelsPerMillimetre;
            return
            [
                new MeasuredCharacteristic("BoreDiameter", bore, PixelPitchMillimetres),
                new MeasuredCharacteristic("SlotWidth", slotWidth, PixelPitchMillimetres),
                new MeasuredCharacteristic("EdgeOffset", edgeOffset, PixelPitchMillimetres)
            ];
        }

        private static int CountDarkRunThrough(byte[] rgb, int width, int y, int centerX)
        {
            int start = centerX;
            while (start > 0 && IsDark(rgb, width, start - 1, y))
            {
                start--;
            }
            int end = centerX;
            while (end + 1 < width && IsDark(rgb, width, end + 1, y))
            {
                end++;
            }
            return end - start + 1;
        }

        private static PixelRun FindDarkRunRightOf(byte[] rgb, int width, int y, int minX)
        {
            int bestStart = -1;
            int bestLength = 0;
            int x = minX;
            while (x < BracketRightX)
            {
                while (x < BracketRightX && !IsDark(rgb, width, x, y))
                {
                    x++;
                }
                int start = x;
                while (x < BracketRightX && IsDark(rgb, width, x, y))
                {
                    x++;
                }
                int length = x - start;
                if (length > bestLength)
                {
                    bestStart = start;
                    bestLength = length;
                }
            }
            if (bestStart < 0)
            {
                throw new InvalidDataException("Could not find the slot in the fixture image.");
            }
            return new PixelRun(bestStart, bestLength);
        }

        private static bool IsDark(byte[] rgb, int width, int x, int y)
        {
            int offset = ((y * width) + x) * 3;
            return rgb[offset] < 64 && rgb[offset + 1] < 64 && rgb[offset + 2] < 64;
        }

        private const int ImageWidth = 800;
        private const int ImageHeight = 600;
        private const int BoreCenterX = 290;
        private const int BoreCenterY = 300;
        private const int SlotCenterY = 300;
        private const int BracketRightX = 650;
        private const double ScalePixelsPerMillimetre = 10.0;
        private const double PixelPitchMillimetres = 0.10;

        private readonly record struct PixelRun(int StartX, int Length);
    }
}
