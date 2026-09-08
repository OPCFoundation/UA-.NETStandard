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
using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua;

namespace UaLens.Plugins.Performance;

/// <summary>
/// Validated, non-live Performance configuration restored from a persisted snapshot.
/// Applying it prepares the workload but never starts a run.
/// </summary>
internal sealed record PerformanceRestoredState(
    BenchmarkMode Mode,
    double TargetRate,
    bool UnboundedBurst,
    int DurationValue,
    DurationUnit DurationUnit,
    ValueGenerator Generator,
    bool CompareLast3,
    BenchmarkTarget? Target);

/// <summary>
/// Serializable, versioned snapshot of the Performance tab's safe configuration:
/// the workload mode, rate, duration, value generator, the comparison display toggle
/// and the chosen target (as address-space NodeIds and per-argument built-in types).
/// It excludes collected run history, live handles and credentials. Restoring it
/// never starts a run.
/// </summary>
internal sealed class PerformanceStateDto
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public int Mode { get; set; } = (int)BenchmarkMode.Write;
    public double TargetRate { get; set; } = 200;
    public bool UnboundedBurst { get; set; }
    public int DurationValue { get; set; } = 1;
    public int DurationUnit { get; set; } = (int)Performance.DurationUnit.Hours;
    public int Generator { get; set; } = (int)ValueGenerator.Random;
    public bool CompareLast3 { get; set; }
    public PerformanceTargetDto? Target { get; set; }
}

internal sealed class PerformanceTargetDto
{
    public int Mode { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public int BuiltInType { get; set; }
    public int ValueRank { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public List<PerformanceArgumentDto> InputArguments { get; set; } = new();
}

internal sealed class PerformanceArgumentDto
{
    public string Name { get; set; } = string.Empty;
    public int BuiltInType { get; set; }
    public int ValueRank { get; set; }
}

/// <summary>
/// Builds and validates <see cref="PerformanceStateDto"/> snapshots. Validation
/// surfaces unknown versions and invalid payloads as exceptions rather than silently
/// substituting defaults.
/// </summary>
internal static class PerformanceState
{
    private const double MaxRate = 1_000_000_000;
    private const int MaxDuration = 1_000_000;

    public static PerformanceStateDto CreateDto(
        BenchmarkMode mode,
        double targetRate,
        bool unboundedBurst,
        int durationValue,
        DurationUnit durationUnit,
        ValueGenerator generator,
        bool compareLast3,
        BenchmarkTarget? target)
    {
        var dto = new PerformanceStateDto
        {
            Version = PerformanceStateDto.CurrentVersion,
            Mode = (int)mode,
            TargetRate = targetRate,
            UnboundedBurst = unboundedBurst,
            DurationValue = durationValue,
            DurationUnit = (int)durationUnit,
            Generator = (int)generator,
            CompareLast3 = compareLast3
        };
        if (target is { } t)
        {
            var targetDto = new PerformanceTargetDto
            {
                Mode = (int)t.Mode,
                NodeId = t.NodeId.ToString() ?? string.Empty,
                ObjectId = t.ObjectId.IsNull ? string.Empty : t.ObjectId.ToString() ?? string.Empty,
                BuiltInType = (int)t.BuiltInType,
                ValueRank = t.ValueRank,
                DisplayName = t.DisplayName
            };
            if (t.InputArguments is { Length: > 0 })
            {
                foreach (Argument argument in t.InputArguments)
                {
                    targetDto.InputArguments.Add(new PerformanceArgumentDto
                    {
                        Name = argument.Name ?? string.Empty,
                        BuiltInType = (int)ValueFactory.BuiltInForArgument(argument),
                        ValueRank = argument.ValueRank
                    });
                }
            }
            dto.Target = targetDto;
        }
        return dto;
    }

    public static PerformanceRestoredState Validate(PerformanceStateDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.Version != PerformanceStateDto.CurrentVersion)
        {
            throw new FormatException($"Unsupported Performance state version '{dto.Version}'.");
        }
        if (!Enum.IsDefined((BenchmarkMode)dto.Mode))
        {
            throw new FormatException("The saved workload mode is invalid.");
        }
        if (!double.IsFinite(dto.TargetRate) || dto.TargetRate < 0 || dto.TargetRate > MaxRate)
        {
            throw new FormatException("The saved target rate is out of range.");
        }
        if (dto.DurationValue < 1 || dto.DurationValue > MaxDuration)
        {
            throw new FormatException("The saved duration is out of range.");
        }
        if (!Enum.IsDefined((DurationUnit)dto.DurationUnit))
        {
            throw new FormatException("The saved duration unit is invalid.");
        }
        if (!Enum.IsDefined((ValueGenerator)dto.Generator))
        {
            throw new FormatException("The saved value generator is invalid.");
        }

        BenchmarkTarget? target = null;
        if (dto.Target is { } t)
        {
            target = ValidateTarget(t);
        }
        return new PerformanceRestoredState(
            (BenchmarkMode)dto.Mode,
            dto.TargetRate,
            dto.UnboundedBurst,
            dto.DurationValue,
            (DurationUnit)dto.DurationUnit,
            (ValueGenerator)dto.Generator,
            dto.CompareLast3,
            target);
    }

    private static BenchmarkTarget ValidateTarget(PerformanceTargetDto t)
    {
        if (!Enum.IsDefined((BenchmarkMode)t.Mode))
        {
            throw new FormatException("The saved target mode is invalid.");
        }
        if (!NodeId.TryParse(t.NodeId, out NodeId nodeId) || nodeId.IsNull)
        {
            throw new FormatException($"The saved target has an invalid NodeId '{t.NodeId}'.");
        }
        NodeId objectId = NodeId.Null;
        if (!string.IsNullOrEmpty(t.ObjectId))
        {
            if (!NodeId.TryParse(t.ObjectId, out NodeId parsedObject) || parsedObject.IsNull)
            {
                throw new FormatException($"The saved target has an invalid object NodeId '{t.ObjectId}'.");
            }
            objectId = parsedObject;
        }
        if (!Enum.IsDefined((BuiltInType)t.BuiltInType))
        {
            throw new FormatException("The saved target has an invalid built-in type.");
        }
        Argument[]? arguments = null;
        if (t.InputArguments is { Count: > 0 })
        {
            arguments = new Argument[t.InputArguments.Count];
            for (int i = 0; i < arguments.Length; i++)
            {
                PerformanceArgumentDto a = t.InputArguments[i];
                if (!Enum.IsDefined((BuiltInType)a.BuiltInType))
                {
                    throw new FormatException("A saved target argument has an invalid built-in type.");
                }
                arguments[i] = new Argument
                {
                    Name = a.Name,
                    DataType = new NodeId((uint)a.BuiltInType),
                    ValueRank = a.ValueRank
                };
            }
        }
        return new BenchmarkTarget(
            (BenchmarkMode)t.Mode,
            nodeId,
            objectId,
            (BuiltInType)t.BuiltInType,
            t.ValueRank,
            arguments,
            t.DisplayName);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(PerformanceStateDto))]
internal sealed partial class PerformanceStateJsonContext : JsonSerializerContext;
