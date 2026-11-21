// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Opc.Ua;
using Opc.Ua.Vision.Perception;

namespace Vision.VisualInspectionCell
{
    /// <summary>
    /// Coordinates fixture selection, image measurement and deterministic recipe judging.
    /// </summary>
    internal sealed class VisualInspectionAnalysisService
    {
        public VisualInspectionAnalysisService(FixtureImageAnalyzer analyzer, InspectionVerdictPolicy policy)
        {
            m_analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            m_policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public IReadOnlyList<string> FixtureNames { get; } =
        [
            "bracket-ok.png",
            "bracket-not-ok.png",
            "bracket-ambiguous.png"
        ];

        public string FixtureDirectory
        {
            get
            {
                string output = Path.Combine(AppContext.BaseDirectory, "Fixtures");
                if (Directory.Exists(output))
                {
                    return output;
                }

                string project = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "Fixtures"));
                if (Directory.Exists(project))
                {
                    return project;
                }

                return Path.Combine(
                    Environment.CurrentDirectory,
                    "samples",
                    "Vision",
                    "VisualInspectionCell",
                    "Fixtures");
            }
        }

        public InspectionAnalysis AnalyzeByName(string fixtureName)
        {
            string selected = ResolveFixtureName(fixtureName);
            return m_policy.Judge(selected, MeasureByName(selected));
        }

        public InspectionAnalysis AnalyzeForCycle(DateTimeUtc timestamp)
        {
            long cycle = timestamp.IsNull
                ? DateTimeUtc.From(DateTime.UnixEpoch).Value
                : timestamp.Value;
            int index = (int)(Math.Abs(cycle) % FixtureNames.Count);
            return AnalyzeByName(FixtureNames[index % FixtureNames.Count]);
        }

        public IReadOnlyList<MeasuredCharacteristic> MeasureByName(string fixtureName)
        {
            string selected = ResolveFixtureName(fixtureName);
            string path = Path.Combine(FixtureDirectory, selected);
            return m_analyzer.Measure(path);
        }

        public VisionImageReferenceDataType CreateImageReference(string fixtureName, DateTimeUtc timestamp)
        {
            string selected = ResolveFixtureName(fixtureName);
            string path = Path.Combine(FixtureDirectory, selected);
            var png = ByteString.From(File.ReadAllBytes(path));
            return new VisionImageReferenceDataType
            {
                Uri = FormattableString.Invariant($"opcua-inline://visual-inspection-cell/fixtures/{selected}"),
                Digest = ByteString.From(SHA256.HashData(png.Span)),
                DigestAlgorithm = "SHA-256",
                Format = VisionClipFormatEnum.Png,
                PixelFormat = VisualInspectionMediaProvider.PixelFormat,
                Width = VisualInspectionMediaProvider.Width,
                Height = VisualInspectionMediaProvider.Height,
                SizeBytes = (uint)png.Length,
                Timestamp = timestamp
            };
        }

        public bool TryResolveFixtureName(string? requested, out string fixtureName)
        {
            if (!string.IsNullOrWhiteSpace(requested))
            {
                string candidate = requested.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? requested
                    : string.Create(CultureInfo.InvariantCulture, $"{requested}.png");
                foreach (string fixture in FixtureNames)
                {
                    if (string.Equals(candidate, fixture, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(requested, Path.GetFileNameWithoutExtension(fixture),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        fixtureName = fixture;
                        return true;
                    }
                }
            }

            fixtureName = string.Empty;
            return false;
        }

        public bool TryResolveFixtureFromUri(string? uri, out string fixtureName)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                fixtureName = string.Empty;
                return false;
            }
            return TryResolveFixtureName(Path.GetFileName(uri), out fixtureName);
        }

        public string ResolveFixtureName(string? requested)
        {
            if (TryResolveFixtureName(requested, out string fixtureName))
            {
                return fixtureName;
            }

            throw new FileNotFoundException(
                string.Create(CultureInfo.InvariantCulture, $"Unknown visual-inspection fixture '{requested}'."));
        }

        private readonly FixtureImageAnalyzer m_analyzer;
        private readonly InspectionVerdictPolicy m_policy;
    }
}
