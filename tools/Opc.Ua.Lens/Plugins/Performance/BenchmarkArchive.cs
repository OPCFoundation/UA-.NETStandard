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
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace UaLens.Plugins.Performance;

internal sealed record BenchmarkArchive(
    ArrayOf<BenchmarkRun> Runs,
    Guid? BaselineId = null,
    Guid? SelectedId = null,
    bool IsLegacyCsv = false)
{
    public void Validate()
    {
        if (Runs.IsNull || Runs.Count > BenchmarkArchiveCodec.MaxImportedRuns)
        {
            throw new FormatException("The benchmark archive has no run array or exceeds the import run limit.");
        }
        var ids = new HashSet<Guid>();
        foreach (BenchmarkRun run in Runs)
        {
            if (run is null)
            {
                throw new FormatException("A benchmark archive cannot contain a null run.");
            }
            run.Validate();
            if (!ids.Add(run.Id))
            {
                throw new FormatException("A benchmark archive contains duplicate run identities.");
            }
        }
        if ((BaselineId.HasValue && !ids.Contains(BaselineId.Value)) ||
            (SelectedId.HasValue && !ids.Contains(SelectedId.Value)))
        {
            throw new FormatException("The selected run or baseline is not present in the benchmark archive.");
        }
    }
}

/// <summary>
/// Strict, bounded result-file codec. JSON preserves comparison data; legacy
/// CSV preserves only aggregates. Invalid imports never produce partial success.
/// </summary>
internal static class BenchmarkArchiveCodec
{
    public static string Serialize(BenchmarkArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        archive.Validate();
        var runs = new List<BenchmarkRunDto>(archive.Runs.Count);
        foreach (BenchmarkRun run in archive.Runs)
        {
            runs.Add(ToDto(run));
        }
        var dto = new BenchmarkArchiveDto
        {
            Format = FormatName,
            Version = CurrentVersion,
            Runs = runs,
            BaselineId = archive.BaselineId,
            SelectedId = archive.SelectedId
        };
        string json = JsonSerializer.Serialize(dto, BenchmarkArchiveJsonContext.Default.BenchmarkArchiveDto);
        CheckLength(json.Length);
        return json;
    }

    public static string SerializeCsv(BenchmarkArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        archive.Validate();
        var csv = new StringBuilder(BenchmarkRun.CsvHeader).Append("\r\n");
        foreach (BenchmarkRun run in archive.Runs)
        {
            csv.Append(run.ToCsvRow()).Append("\r\n");
            CheckLength(csv.Length);
        }
        return csv.ToString();
    }

    public static BenchmarkArchive Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        CheckLength(text.Length);
        string content = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (content.Length == 0)
        {
            throw new FormatException("The benchmark result file is empty.");
        }
        if (content[0] != '{')
        {
            return ParseCsv(content);
        }

        BenchmarkArchiveDto dto = JsonSerializer.Deserialize(
            content, BenchmarkArchiveJsonContext.Default.BenchmarkArchiveDto)
            ?? throw new JsonException("A benchmark archive cannot be null.");
        if (dto.Format != FormatName || dto.Version != CurrentVersion)
        {
            throw new FormatException($"Unsupported benchmark archive format or version '{dto.Version}'.");
        }
        if (dto.Runs is null || dto.Runs.Count > MaxImportedRuns)
        {
            throw new FormatException("The benchmark archive run array is missing or too large.");
        }
        var runs = new BenchmarkRun[dto.Runs.Count];
        for (int i = 0; i < runs.Length; i++)
        {
            runs[i] = FromDto(dto.Runs[i]
                ?? throw new FormatException("The benchmark archive contains a null run."));
        }
        var archive = new BenchmarkArchive(new ArrayOf<BenchmarkRun>(runs), dto.BaselineId, dto.SelectedId);
        archive.Validate();
        return archive;
    }

    public static async Task<BenchmarkArchive> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096, leaveOpen: true);
        var text = new StringBuilder();
        char[] buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            CheckLength(text.Length + count);
            text.Append(buffer, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Parse(text.ToString());
    }

    private static BenchmarkArchive ParseCsv(string text)
    {
        var runs = new List<BenchmarkRun>();
        bool inQuotes = false;
        bool first = true;
        int start = 0;
        int record = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (!inQuotes && text[i] is '\r' or '\n')
            {
                AddCsvRecord(text[start..i], ++record, runs, ref first);
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                start = i + 1;
            }
        }
        if (inQuotes)
        {
            throw new FormatException($"CSV record {record + 1} contains an unterminated quoted field.");
        }
        if (start < text.Length)
        {
            AddCsvRecord(text[start..], record + 1, runs, ref first);
        }
        return new BenchmarkArchive(new ArrayOf<BenchmarkRun>(runs.ToArray()), IsLegacyCsv: true);
    }

    private static void AddCsvRecord(string text, int number, List<BenchmarkRun> runs, ref bool first)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        if (first)
        {
            first = false;
            if (string.Equals(text, BenchmarkRun.CsvHeader, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, BenchmarkRun.CsvHeader[..^6], StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        BenchmarkRun run = BenchmarkRun.TryParseCsvRow(text)
            ?? throw new FormatException($"CSV record {number} is malformed; no history was loaded.");
        if (runs.Count >= MaxImportedRuns)
        {
            throw new FormatException("The CSV file exceeds the benchmark import run limit.");
        }
        runs.Add(run);
    }

    private static BenchmarkRunDto ToDto(BenchmarkRun run)
    {
        return new BenchmarkRunDto
        {
            Id = run.Id,
            TimestampUtc = run.TimestampUtc,
            TargetRate = run.TargetRate,
            AchievedRate = run.AchievedRate,
            TotalOps = run.TotalOps,
            MeanLatencyMs = run.MeanLatencyMs,
            P50Ms = run.P50Ms,
            P90Ms = run.P90Ms,
            P99Ms = run.P99Ms,
            ErrorCount = run.ErrorCount,
            ElapsedSeconds = run.ElapsedSeconds,
            Completion = (int)run.Completion,
            Notes = run.Notes,
            Configuration = run.Configuration is { } configuration ? ToDto(configuration) : null,
            Distribution = run.Distribution is { } distribution ? new BenchmarkDistributionDto
            {
                SchemaVersion = BenchmarkDistribution.SchemaVersion,
                Counts = new List<long>(distribution.BucketCounts.Span.ToArray()),
                MeanMs = distribution.MeanMs,
                MaximumMs = distribution.MaximumMs
            } : null
        };
    }

    private static BenchmarkConfigurationDto ToDto(BenchmarkConfiguration configuration)
    {
        List<BenchmarkArgumentDto>? arguments = null;
        if (!configuration.InputArguments.IsNull)
        {
            arguments = new List<BenchmarkArgumentDto>(configuration.InputArguments.Count);
            foreach (BenchmarkArgumentConfiguration argument in configuration.InputArguments)
            {
                arguments.Add(new BenchmarkArgumentDto
                {
                    DataTypeId = argument.DataTypeId,
                    GeneratedType = (int)argument.GeneratedType,
                    ValueRank = argument.ValueRank
                });
            }
        }
        return new BenchmarkConfigurationDto
        {
            Mode = (int)configuration.Mode,
            Generator = (int)configuration.Generator,
            TargetRate = configuration.TargetRate,
            UnboundedBurst = configuration.UnboundedBurst,
            DurationSeconds = configuration.DurationSeconds,
            MaxConcurrency = configuration.MaxConcurrency,
            TargetNodeId = configuration.TargetNodeId,
            ObjectNodeId = configuration.ObjectNodeId,
            TargetType = (int)configuration.TargetType,
            TargetValueRank = configuration.TargetValueRank,
            InputArguments = arguments,
            EndpointUrl = configuration.EndpointUrl,
            ServerApplicationUri = configuration.ServerApplicationUri,
            SecurityMode = configuration.SecurityMode is { } mode ? (int)mode : null,
            SecurityPolicyUri = configuration.SecurityPolicyUri,
            UserTokenType = configuration.UserTokenType is { } token ? (int)token : null
        };
    }

    private static BenchmarkRun FromDto(BenchmarkRunDto dto)
    {
        BenchmarkDistribution? distribution = null;
        if (dto.Distribution is { } d)
        {
            if (d.SchemaVersion != BenchmarkDistribution.SchemaVersion || d.Counts is null)
            {
                throw new FormatException("The benchmark distribution schema is unsupported or missing counts.");
            }
            try
            {
                distribution = new BenchmarkDistribution(new ArrayOf<long>(d.Counts.ToArray()), d.MeanMs, d.MaximumMs);
            }
            catch (ArgumentException ex)
            {
                throw new FormatException("The benchmark distribution is corrupt: " + ex.Message, ex);
            }
        }
        return new BenchmarkRun
        {
            Id = dto.Id,
            TimestampUtc = dto.TimestampUtc,
            TargetRate = dto.TargetRate,
            AchievedRate = dto.AchievedRate,
            TotalOps = dto.TotalOps,
            MeanLatencyMs = dto.MeanLatencyMs,
            P50Ms = dto.P50Ms,
            P90Ms = dto.P90Ms,
            P99Ms = dto.P99Ms,
            ErrorCount = dto.ErrorCount,
            ElapsedSeconds = dto.ElapsedSeconds,
            Completion = (BenchmarkCompletion)dto.Completion,
            Notes = dto.Notes,
            Configuration = dto.Configuration is { } configuration ? FromDto(configuration) : null,
            Distribution = distribution
        };
    }

    private static BenchmarkConfiguration FromDto(BenchmarkConfigurationDto dto)
    {
        ArrayOf<BenchmarkArgumentConfiguration> arguments = default;
        if (dto.InputArguments is { } input)
        {
            if (input.Count > BenchmarkConfiguration.MaxInputArguments)
            {
                throw new FormatException("The benchmark input signature is too large.");
            }
            var converted = new BenchmarkArgumentConfiguration[input.Count];
            for (int i = 0; i < input.Count; i++)
            {
                BenchmarkArgumentDto argument = input[i]
                    ?? throw new FormatException("The benchmark input signature contains a null argument.");
                converted[i] = new BenchmarkArgumentConfiguration(
                    argument.DataTypeId, (BuiltInType)argument.GeneratedType, argument.ValueRank);
            }
            arguments = new ArrayOf<BenchmarkArgumentConfiguration>(converted);
        }
        return new BenchmarkConfiguration
        {
            Mode = (BenchmarkMode)dto.Mode,
            Generator = (ValueGenerator)dto.Generator,
            TargetRate = dto.TargetRate,
            UnboundedBurst = dto.UnboundedBurst,
            DurationSeconds = dto.DurationSeconds,
            MaxConcurrency = dto.MaxConcurrency,
            TargetNodeId = dto.TargetNodeId,
            ObjectNodeId = dto.ObjectNodeId,
            TargetType = (BuiltInType)dto.TargetType,
            TargetValueRank = dto.TargetValueRank,
            InputArguments = arguments,
            EndpointUrl = dto.EndpointUrl,
            ServerApplicationUri = dto.ServerApplicationUri,
            SecurityMode = dto.SecurityMode is { } mode ? (MessageSecurityMode)mode : null,
            SecurityPolicyUri = dto.SecurityPolicyUri,
            UserTokenType = dto.UserTokenType is { } token ? (UserTokenType)token : null
        };
    }

    private static void CheckLength(int length)
    {
        if (length > MaxImportCharacters)
        {
            throw new FormatException("The benchmark result file exceeds the 4 Mi-character import limit.");
        }
    }

    public const int CurrentVersion = 1;
    public const int MaxImportedRuns = 4096;
    public const int MaxImportCharacters = 4 * 1024 * 1024;
    public const string FormatName = "ualens-performance";
}

internal sealed class BenchmarkArchiveDto
{
    public required string Format { get; init; }
    public required int Version { get; init; }
    public required List<BenchmarkRunDto> Runs { get; init; }
    public Guid? BaselineId { get; init; }
    public Guid? SelectedId { get; init; }
}

internal sealed class BenchmarkRunDto
{
    public required Guid Id { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public required double TargetRate { get; init; }
    public required double AchievedRate { get; init; }
    public required long TotalOps { get; init; }
    public required double MeanLatencyMs { get; init; }
    public required double P50Ms { get; init; }
    public required double P90Ms { get; init; }
    public required double P99Ms { get; init; }
    public required long ErrorCount { get; init; }
    public required string Notes { get; init; }
    public double? ElapsedSeconds { get; init; }
    public int Completion { get; init; }
    public BenchmarkConfigurationDto? Configuration { get; init; }
    public BenchmarkDistributionDto? Distribution { get; init; }
}

internal sealed class BenchmarkConfigurationDto
{
    public required int Mode { get; init; }
    public required int Generator { get; init; }
    public required double TargetRate { get; init; }
    public required bool UnboundedBurst { get; init; }
    public required double DurationSeconds { get; init; }
    public required int MaxConcurrency { get; init; }
    public required int TargetType { get; init; }
    public required int TargetValueRank { get; init; }
    public string? TargetNodeId { get; init; }
    public string? ObjectNodeId { get; init; }
    public List<BenchmarkArgumentDto>? InputArguments { get; init; }
    public string? EndpointUrl { get; init; }
    public string? ServerApplicationUri { get; init; }
    public int? SecurityMode { get; init; }
    public string? SecurityPolicyUri { get; init; }
    public int? UserTokenType { get; init; }
}

internal sealed class BenchmarkArgumentDto
{
    public string? DataTypeId { get; init; }
    public required int GeneratedType { get; init; }
    public required int ValueRank { get; init; }
}

internal sealed class BenchmarkDistributionDto
{
    public required int SchemaVersion { get; init; }
    public required List<long> Counts { get; init; }
    public required double MeanMs { get; init; }
    public required double MaximumMs { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BenchmarkArchiveDto))]
internal sealed partial class BenchmarkArchiveJsonContext : JsonSerializerContext;
