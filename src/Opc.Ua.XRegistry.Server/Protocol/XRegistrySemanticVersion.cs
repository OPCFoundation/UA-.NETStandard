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
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    internal sealed record XRegistrySemanticVersion(BigInteger Major, BigInteger Minor, BigInteger Patch, string[] Pre)
        : IComparable<XRegistrySemanticVersion>
    {
        public static XRegistrySemanticVersion Parse(string version)
        {
            string[] build = version.Split('+');
            if (build.Length > 2 || (build.Length == 2 && !Identifiers(build[1], false)))
            {
                throw Invalid();
            }
            int dash = build[0].IndexOf('-', StringComparison.Ordinal);
            string core = dash < 0 ? build[0] : build[0][..dash];
            string[] pre = dash < 0 ? [] : build[0][(dash + 1)..].Split('.');
            if (dash >= 0 && !Identifiers(build[0][(dash + 1)..], true))
            {
                throw Invalid();
            }
            string[] numbers = core.Split('.');
            if (numbers.Length != 3 || numbers.Any(number => !Numeric(number)))
            {
                throw Invalid();
            }
            return new XRegistrySemanticVersion(BigInteger.Parse(numbers[0], CultureInfo.InvariantCulture),
                BigInteger.Parse(numbers[1], CultureInfo.InvariantCulture),
                BigInteger.Parse(numbers[2], CultureInfo.InvariantCulture), pre);
        }

        public int CompareTo(XRegistrySemanticVersion? other)
        {
            if (other is null)
            {
                return 1;
            }
            int value = Major.CompareTo(other.Major);
            if (value == 0)
            {
                value = Minor.CompareTo(other.Minor);
            }
            if (value == 0)
            {
                value = Patch.CompareTo(other.Patch);
            }
            if (value != 0)
            {
                return value;
            }
            if (Pre.Length == 0 || other.Pre.Length == 0)
            {
                return Pre.Length == 0 ? other.Pre.Length == 0 ? 0 : 1 : -1;
            }
            for (int index = 0; index < Math.Min(Pre.Length, other.Pre.Length); index++)
            {
                string first = Pre[index];
                string second = other.Pre[index];
                bool firstNumeric = Numeric(first);
                bool secondNumeric = Numeric(second);
                value = firstNumeric && secondNumeric
                    ? XRegistryQueryPath.CompareNumbers(first, second)
                    : firstNumeric != secondNumeric ? firstNumeric ? -1 : 1
                    : StringComparer.Ordinal.Compare(first, second);
                if (value != 0)
                {
                    return value;
                }
            }
            return Pre.Length.CompareTo(other.Pre.Length);
        }

        private static bool Numeric(string value)
        {
            return value.Length != 0 &&
                (value.Length == 1 || value[0] != '0') &&
                value.All(character => character is >= '0' and <= '9');
        }

        private static bool Identifiers(string text, bool prerelease)
        {
            return text.Split('.').All(part => part.Length != 0 &&
                part.All(
                    character => character is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '-') &&
                (!prerelease || !part.All(character => character is >= '0' and <= '9') || Numeric(part)));
        }

        private static XRegistryRejectionException Invalid()
        {
            return new XRegistryRejectionException(
                "invalid_versionid", "The Version ID does not follow Semantic Versioning 2.0.");
        }
    }
}
