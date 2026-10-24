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
using System.Text.Json.Serialization;
using Opc.Ua;

namespace UaLens.Plugins.Continuity;

internal enum ContinuityScenario
{
    Observe,
    RecreateOwnedSubscription,
    TransferOnLoad,
    RecreateOnLoad,
    GracefulDurableRestore,
    ConfiguredFailover
}

/// <summary>
/// Only portable, non-running intent belongs in workspace JSON. Runtime binary
/// subscription snapshots are deliberately not part of this type.
/// </summary>
internal sealed class ContinuityStateDto
{
    public int Version { get; set; } = 1;

    public int Scenario { get; set; }

    public List<string> Targets { get; set; } = new() { "i=2258" };

    public double PublishingIntervalMs { get; set; } = 1000;

    public double SamplingIntervalMs { get; set; } = 250;

    public uint QueueSize { get; set; } = 100;

    public uint KeepAliveCount { get; set; } = 10;

    public uint LifetimeCount { get; set; } = 300;

    public uint ItemsPerPartition { get; set; } = 16;

    public bool Durable { get; set; }

    public int DurableLifetimeHours { get; set; } = 1;

    public bool MonotonicSample { get; set; }
}

/// <summary>
/// Validated intent; target identities use namespace URIs, never cached server handles.
/// </summary>
internal sealed record ContinuityConfiguration(
    ContinuityScenario Scenario,
    ArrayOf<ExpandedNodeId> Targets,
    double PublishingIntervalMs,
    double SamplingIntervalMs,
    uint QueueSize,
    uint KeepAliveCount,
    uint LifetimeCount,
    uint ItemsPerPartition,
    bool Durable,
    int DurableLifetimeHours,
    bool MonotonicSample);

internal static class ContinuityState
{
    public static ContinuityConfiguration Validate(ContinuityStateDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.Version != 1 || !Enum.IsDefined((ContinuityScenario)dto.Scenario))
        {
            throw new FormatException("Unsupported Continuity configuration version or scenario.");
        }
        if (dto.Targets is null || dto.Targets.Count is < 1 or > MaxTargets)
        {
            throw new FormatException("Select between 1 and 64 variable targets.");
        }
        if (!double.IsFinite(dto.PublishingIntervalMs) || dto.PublishingIntervalMs is < 20 or > 60000 ||
            !double.IsFinite(dto.SamplingIntervalMs) || dto.SamplingIntervalMs is < 0 or > 60000 ||
            dto.QueueSize is < 1 or > 10000 || dto.KeepAliveCount is < 1 or > 1000 ||
            dto.LifetimeCount < 3 * dto.KeepAliveCount || dto.LifetimeCount > 100000 ||
            dto.ItemsPerPartition is < 1 or > MaxTargets || dto.DurableLifetimeHours is < 1 or > 168)
        {
            throw new FormatException("Continuity settings exceed the bounded lab limits.");
        }
        var targets = new ExpandedNodeId[dto.Targets.Count];
        var unique = new HashSet<ExpandedNodeId>();
        for (int i = 0; i < targets.Length; i++)
        {
            string text = dto.Targets[i];
            if (string.IsNullOrWhiteSpace(text) || text.Length > 2048 ||
                !ExpandedNodeId.TryParse(text, out ExpandedNodeId target) || target.IsNull ||
                target.ServerIndex != 0 ||
                (target.NamespaceIndex != 0 && string.IsNullOrEmpty(target.NamespaceUri)))
            {
                throw new FormatException(
                    "Use a local expanded NodeId with a namespace URI (nsu=...), or a namespace-zero NodeId.");
            }
            if (!unique.Add(target))
            {
                throw new FormatException("Duplicate Continuity targets are not supported.");
            }
            targets[i] = target;
        }
        return new ContinuityConfiguration(
            (ContinuityScenario)dto.Scenario,
            new ArrayOf<ExpandedNodeId>(targets),
            dto.PublishingIntervalMs,
            dto.SamplingIntervalMs,
            dto.QueueSize,
            dto.KeepAliveCount,
            dto.LifetimeCount,
            dto.ItemsPerPartition,
            dto.Durable || (ContinuityScenario)dto.Scenario == ContinuityScenario.GracefulDurableRestore,
            dto.DurableLifetimeHours,
            dto.MonotonicSample);
    }

    public static ContinuityStateDto Capture(ContinuityConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var result = new ContinuityStateDto
        {
            Scenario = (int)configuration.Scenario,
            PublishingIntervalMs = configuration.PublishingIntervalMs,
            SamplingIntervalMs = configuration.SamplingIntervalMs,
            QueueSize = configuration.QueueSize,
            KeepAliveCount = configuration.KeepAliveCount,
            LifetimeCount = configuration.LifetimeCount,
            ItemsPerPartition = configuration.ItemsPerPartition,
            Durable = configuration.Durable,
            DurableLifetimeHours = configuration.DurableLifetimeHours,
            MonotonicSample = configuration.MonotonicSample,
            Targets = new List<string>(configuration.Targets.Count)
        };
        foreach (ExpandedNodeId target in configuration.Targets)
        {
            result.Targets.Add(target.ToString());
        }
        return result;
    }

    public const int MaxTargets = 64;
    public const uint MaxNotificationsPerPublish = 1024;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ContinuityStateDto))]
internal sealed partial class ContinuityStateJsonContext : JsonSerializerContext;
