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
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Plugins.Performance;

internal sealed record BenchmarkArgumentConfiguration(
    string? DataTypeId,
    BuiltInType GeneratedType,
    int ValueRank);

/// <summary>
/// Workload and session evidence frozen at Run, not read from editable controls
/// at completion. Null evidence means unknown, never an inferred match.
/// Only authentication type is captured; identities and credentials are excluded.
/// </summary>
internal sealed record BenchmarkConfiguration
{
    public BenchmarkMode Mode { get; init; }
    public ValueGenerator Generator { get; init; }
    public double TargetRate { get; init; }
    public bool UnboundedBurst { get; init; }
    public double DurationSeconds { get; init; }
    public int MaxConcurrency { get; init; }
    public string? TargetNodeId { get; init; }
    public string? ObjectNodeId { get; init; }
    public BuiltInType TargetType { get; init; }
    public int TargetValueRank { get; init; } = ValueRanks.Scalar;
    public ArrayOf<BenchmarkArgumentConfiguration> InputArguments { get; init; }
    public string? EndpointUrl { get; init; }
    public string? ServerApplicationUri { get; init; }
    public MessageSecurityMode? SecurityMode { get; init; }
    public string? SecurityPolicyUri { get; init; }
    public UserTokenType? UserTokenType { get; init; }

    public static BenchmarkConfiguration Capture(
        ISession session,
        BenchmarkTarget target,
        ValueGenerator generator,
        double targetRate,
        bool unboundedBurst,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(target);

        NamespaceTable? namespaces = session.NamespaceUris;
        ArrayOf<BenchmarkArgumentConfiguration> arguments = target.Mode == BenchmarkMode.Write
            ? ArrayOf<BenchmarkArgumentConfiguration>.Empty
            : ArrayOf<BenchmarkArgumentConfiguration>.Null;
        if (target.Mode == BenchmarkMode.Call && target.InputArguments is { } input)
        {
            var frozen = new BenchmarkArgumentConfiguration[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                frozen[i] = new BenchmarkArgumentConfiguration(
                    CaptureNodeId(input[i].DataType, namespaces),
                    ValueFactory.BuiltInForArgument(input[i]),
                    input[i].ValueRank);
            }
            arguments = new ArrayOf<BenchmarkArgumentConfiguration>(frozen);
        }

        EndpointDescription? endpoint = session.ConfiguredEndpoint?.Description;
        MessageSecurityMode? securityMode = endpoint?.SecurityMode;
        var configuration = new BenchmarkConfiguration
        {
            Mode = target.Mode,
            Generator = generator,
            TargetRate = unboundedBurst ? 0 : targetRate,
            UnboundedBurst = unboundedBurst,
            DurationSeconds = duration.TotalSeconds,
            MaxConcurrency = unboundedBurst
                ? BenchmarkRunner.MaxConcurrencyCap
                : BenchmarkRunner.RecommendConcurrency(targetRate),
            TargetNodeId = CaptureNodeId(target.NodeId, namespaces),
            ObjectNodeId = CaptureNodeId(target.ObjectId, namespaces),
            TargetType = target.BuiltInType,
            TargetValueRank = target.ValueRank,
            InputArguments = arguments,
            EndpointUrl = CaptureEndpoint(endpoint?.EndpointUrl),
            ServerApplicationUri = KnownText(endpoint?.Server?.ApplicationUri),
            SecurityMode = securityMode is { } mode && mode != MessageSecurityMode.Invalid && Enum.IsDefined(mode)
                ? mode
                : null,
            SecurityPolicyUri = KnownText(endpoint?.SecurityPolicyUri),
            UserTokenType = session.Identity?.TokenType
        };
        configuration.Validate();
        return configuration;
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(Generator) || !Enum.IsDefined(TargetType))
        {
            throw new FormatException("The benchmark configuration contains an invalid workload or value type.");
        }
        if (!double.IsFinite(TargetRate) || TargetRate > 1_000_000_000 ||
            (UnboundedBurst ? TargetRate != 0 : TargetRate < 1))
        {
            throw new FormatException("The benchmark target rate is invalid.");
        }
        if (!double.IsFinite(DurationSeconds) || DurationSeconds <= 0 ||
            DurationSeconds > TimeSpan.MaxValue.TotalSeconds)
        {
            throw new FormatException("The benchmark duration is invalid.");
        }
        if (MaxConcurrency < 1 || MaxConcurrency > BenchmarkRunner.MaxConcurrencyCap)
        {
            throw new FormatException("The benchmark concurrency is outside the runner capacity.");
        }
        if (TargetValueRank < ValueRanks.ScalarOrOneDimension)
        {
            throw new FormatException("The benchmark target value rank is invalid.");
        }
        ValidateNodeId(TargetNodeId);
        ValidateNodeId(ObjectNodeId);
        if (InputArguments.Count > MaxInputArguments)
        {
            throw new FormatException("The benchmark input signature exceeds the retained argument limit.");
        }
        foreach (BenchmarkArgumentConfiguration argument in InputArguments)
        {
            if (argument is null || !Enum.IsDefined(argument.GeneratedType) ||
                argument.ValueRank < ValueRanks.ScalarOrOneDimension)
            {
                throw new FormatException("The benchmark input signature is invalid.");
            }
            ValidateNodeId(argument.DataTypeId);
        }
        if (EndpointUrl is not null &&
            (!Uri.TryCreate(EndpointUrl, UriKind.Absolute, out Uri? endpoint) ||
                !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) ||
                !string.IsNullOrEmpty(endpoint.Fragment)))
        {
            throw new FormatException("The benchmark endpoint must be an absolute URL without credentials or query.");
        }
        if (SecurityMode is { } security && (security == MessageSecurityMode.Invalid || !Enum.IsDefined(security)))
        {
            throw new FormatException("The benchmark security mode is invalid.");
        }
        if (UserTokenType is { } token && !Enum.IsDefined(token))
        {
            throw new FormatException("The benchmark authentication type is invalid.");
        }
        ValidateKnownText(ServerApplicationUri);
        ValidateKnownText(SecurityPolicyUri);
    }

    private static string? CaptureNodeId(NodeId nodeId, NamespaceTable? namespaces)
    {
        if (nodeId.IsNull)
        {
            return null;
        }
        if (nodeId.NamespaceIndex == 0)
        {
            return nodeId.ToString();
        }
        if (namespaces is null || string.IsNullOrEmpty(namespaces.GetString(nodeId.NamespaceIndex)))
        {
            return null;
        }
        return NodeId.ToExpandedNodeId(nodeId, namespaces).ToString();
    }

    private static string? CaptureEndpoint(string? endpointUrl)
    {
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? endpoint) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            return null;
        }
        return endpoint.AbsoluteUri;
    }

    private static string? KnownText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void ValidateKnownText(string? value)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("Empty benchmark evidence must be represented as missing, not as a known value.");
        }
    }

    private static void ValidateNodeId(string? value)
    {
        if (value is not null &&
            (!ExpandedNodeId.TryParse(value, out ExpandedNodeId id) || id.IsNull || id.ServerIndex != 0 ||
                (id.NamespaceIndex != 0 && string.IsNullOrEmpty(id.NamespaceUri))))
        {
            throw new FormatException("Benchmark target evidence must use a namespace URI or the standard namespace.");
        }
    }

    public const int MaxInputArguments = 256;
}
