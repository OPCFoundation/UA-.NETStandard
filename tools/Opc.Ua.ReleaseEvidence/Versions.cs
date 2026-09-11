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
using System.IO;
using NuGet.Versioning;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Validates release versions and source or producer identities used by release-evidence contracts.
    /// </summary>
    internal static class Versions
    {
        /// <summary>
        /// Parses a NuGet release version or rejects an invalid version string.
        /// </summary>
        public static NuGetVersion Parse(string version)
        {
            return NuGetVersion.TryParse(version, out NuGetVersion? parsed)
                ? parsed
                : throw new InvalidDataException("The release version is not a valid NuGet version.");
        }

        /// <summary>
        /// Compares normalized NuGet versions and release labels without build-metadata differences.
        /// </summary>
        public static bool Equal(string first, string second)
        {
            return VersionComparer.VersionRelease.Equals(Parse(first), Parse(second));
        }

        /// <summary>
        /// Determines whether required-stage controls apply to a stable release of the current major version.
        /// </summary>
        public static bool RequiresStableControls(PolicyConfiguration policy, ReleaseRecord release)
        {
            return policy.Stage == "required" &&
                release.Channel == "stable" &&
                Parse(release.Version).Major == policy.CurrentMajor;
        }

        /// <summary>
        /// Rejects incomplete or malformed checkout and producer identities before processing release evidence.
        /// </summary>
        public static void ValidateIdentity(SourceRecord source, ProducerRecord producer)
        {
            if (source == null ||
                producer == null ||
                string.IsNullOrWhiteSpace(source.Repository) ||
                !ValidSha(source.ActualSha) ||
                string.IsNullOrWhiteSpace(source.ActualRef) ||
                !source.ActualRef.StartsWith("refs/", StringComparison.Ordinal) ||
                !ValidSha(producer.DefinitionSha) ||
                string.IsNullOrWhiteSpace(producer.RunId) ||
                producer.RunId == "0" ||
                producer.Attempt < 1 ||
                string.IsNullOrWhiteSpace(producer.Job) ||
                producer.System is not ("github-actions" or "azure-pipelines"))
            {
                throw new InvalidDataException(
                    "Source and producer identities must be real, nonzero, complete records.");
            }
            EvidenceFiles.ValidateRelative(producer.Workflow);
        }

        private static bool ValidSha(string value)
        {
            if (value == null || value.Length is not (40 or 64) || value.Trim('0').Length == 0)
            {
                return false;
            }
            foreach (char character in value)
            {
                if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
