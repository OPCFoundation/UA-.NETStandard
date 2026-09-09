// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.IO;
using NuGet.Versioning;

namespace Opc.Ua.ReleaseEvidence
{
    internal static class Versions
    {
        public static NuGetVersion Parse(string version)
        {
            return NuGetVersion.TryParse(version, out NuGetVersion? parsed)
                ? parsed
                : throw new InvalidDataException("The release version is not a valid NuGet version.");
        }

        public static bool Equal(string first, string second)
        {
            return VersionComparer.VersionRelease.Equals(Parse(first), Parse(second));
        }

        public static bool RequiresStableControls(PolicyConfiguration policy, ReleaseRecord release)
        {
            return policy.Stage == "required" &&
                release.Channel == "stable" &&
                Parse(release.Version).Major == policy.CurrentMajor;
        }

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
