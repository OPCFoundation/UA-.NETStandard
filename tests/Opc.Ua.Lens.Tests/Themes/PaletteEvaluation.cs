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
using Avalonia.Media;

namespace UaLens.Tests.Themes
{
    internal enum ColorVisionModel
    {
        Normal,
        Protanopia,
        Deuteranopia,
        Tritanopia
    }

    /// <summary>
    /// W3C source-color contrast and Machado et al. (2009) severity-1 simulation.
    /// Simulated ratios and linear-RGB distances are diagnostics, not perceptual conformance tests.
    /// </summary>
    internal static class PaletteEvaluation
    {
        internal static Color Simulate(Color color, ColorVisionModel model)
        {
            double red = Linear(color.R);
            double green = Linear(color.G);
            double blue = Linear(color.B);
            // Authors' supplementary Table 1; operate in linear RGB, then clip and encode sRGB.
            // https://www.inf.ufrgs.br/~oliveira/pubs_files/CVD_Simulation/CVD_Simulation.html
            return model switch
            {
                ColorVisionModel.Normal => color,
                ColorVisionModel.Protanopia => Encode(
                    (0.152286 * red) + (1.052583 * green) - (0.204868 * blue),
                    (0.114503 * red) + (0.786281 * green) + (0.099216 * blue),
                    (-0.003882 * red) - (0.048116 * green) + (1.051998 * blue)),
                ColorVisionModel.Deuteranopia => Encode(
                    (0.367322 * red) + (0.860646 * green) - (0.227968 * blue),
                    (0.280085 * red) + (0.672501 * green) + (0.047413 * blue),
                    (-0.011820 * red) + (0.042940 * green) + (0.968881 * blue)),
                ColorVisionModel.Tritanopia => Encode(
                    (1.255528 * red) - (0.076749 * green) - (0.178779 * blue),
                    (-0.078411 * red) + (0.930809 * green) + (0.147602 * blue),
                    (0.004733 * red) + (0.691367 * green) + (0.303900 * blue)),
                _ => throw new ArgumentOutOfRangeException(nameof(model))
            };
        }

        internal static double Contrast(Color first, Color second)
        {
            double firstLuminance = Luminance(first);
            double secondLuminance = Luminance(second);
            return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
                (Math.Min(firstLuminance, secondLuminance) + 0.05);
        }

        internal static double LinearRgbDistance(Color first, Color second)
        {
            double red = Linear(first.R) - Linear(second.R);
            double green = Linear(first.G) - Linear(second.G);
            double blue = Linear(first.B) - Linear(second.B);
            return Math.Sqrt((red * red) + (green * green) + (blue * blue));
        }

        private static Color Encode(double red, double green, double blue)
        {
            return Color.FromRgb(EncodeChannel(red), EncodeChannel(green), EncodeChannel(blue));
        }

        private static byte EncodeChannel(double value)
        {
            value = Math.Clamp(value, 0, 1);
            double encoded = value <= 0.0031308 ? 12.92 * value : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055;
            return (byte)Math.Round(encoded * 255, MidpointRounding.AwayFromZero);
        }

        private static double Luminance(Color color)
        {
            return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
        }

        private static double Linear(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }
}
