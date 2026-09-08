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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Plugins.Companions;

/// <summary>
/// A typed companion-model adapter. The host owns operation cancellation and the
/// borrowed primary session; an adapter never changes connection or document ownership.
/// </summary>
internal interface ICompanionProvider
{
    CompanionDescriptor Descriptor { get; }

    ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
        CompanionContext context,
        CancellationToken cancellationToken);

    ValueTask<CompanionInspection> InspectAsync(
        CompanionContext context,
        CompanionTarget target,
        CancellationToken cancellationToken);

    ValueTask<CompanionOperationResult> ExecuteAsync(
        CompanionContext context,
        CompanionTarget target,
        string operationId,
        string? input,
        CancellationToken cancellationToken);
}

/// <summary>
/// Borrowed operation context with explicit discovery and result bounds.
/// </summary>
internal sealed class CompanionContext
{
    public CompanionContext(ISession session, ITelemetryContext telemetry, int maxTargets = 128, int maxFields = 256)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        if (maxTargets is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTargets));
        }
        if (maxFields is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFields));
        }
        MaxTargets = maxTargets;
        MaxFields = maxFields;
    }

    public ISession Session { get; }

    public ITelemetryContext Telemetry { get; }

    public int MaxTargets { get; }

    public int MaxFields { get; }
}

/// <summary>
/// Model identity and maturity shown before running a guided task.
/// </summary>
internal sealed record CompanionDescriptor(string Id, string DisplayName, string ModelUri, string Maturity);

/// <summary>
/// A discovered, actionable instance rather than a namespace-presence claim.
/// </summary>
internal sealed record CompanionTarget(string ProviderId, NodeId NodeId, string DisplayName, string TypeName)
{
    public string Identifier => NodeId.ToString();
}

/// <summary>
/// A typed inspection field. No boxed values or credential material cross this interface.
/// </summary>
internal sealed record CompanionValue(string Name, Variant Value)
{
    public string Text => Value.ToString();
}

internal enum CompanionOperationSafety
{
    ReadOnly,
    LocalFile,
    SampleMutation
}

/// <summary>
/// An operation supplied with the selected instance's inspection.
/// </summary>
internal sealed record CompanionOperation(
    string Id,
    string DisplayName,
    CompanionOperationSafety Safety,
    string? InputHint = null);

/// <summary>
/// Current values and permitted guided operations for an instance.
/// </summary>
internal sealed record CompanionInspection(
    ArrayOf<CompanionValue> Values,
    ArrayOf<CompanionOperation> Operations,
    string Summary);

/// <summary>
/// Explicit operation outcome; failures are propagated rather than encoded as success.
/// </summary>
internal sealed record CompanionOperationResult(string Summary, ArrayOf<CompanionValue> Values);
