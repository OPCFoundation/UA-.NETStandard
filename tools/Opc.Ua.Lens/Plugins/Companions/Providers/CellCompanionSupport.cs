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
using System.Threading;
using Opc.Ua;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// Operation-local limits for the read-only cell and scene adapters.
/// </summary>
internal static class CellCompanionSupport
{
    public static CancellationTokenSource BeginOperation(
        CompanionContext context,
        string modelUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Session.NamespaceUris.GetIndex(modelUri) < 0)
        {
            throw new ServiceResultException(
                StatusCodes.BadNotSupported, "The connected server does not expose this companion model.");
        }
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        return lifetime;
    }

    public static void ValidateTarget(CompanionContext context, CompanionTarget target, string providerId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);
        if (target.ProviderId != providerId || target.NodeId.IsNull)
        {
            throw new ArgumentException("Select an instance discovered by this companion provider.", nameof(target));
        }
    }

    public static void RequireNoInput(string? input)
    {
        if (!string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("This read-only operation does not accept input.", nameof(input));
        }
    }

    public static void CheckCount(int count, int maximum, string description)
    {
        if (count > maximum)
        {
            throw new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded, $"{description} exceeds the limit of {maximum}.");
        }
    }

    public static void AddTarget(
        List<CompanionTarget> targets,
        HashSet<NodeId> visited,
        CompanionTarget target,
        int maximum)
    {
        if (target.NodeId.IsNull)
        {
            throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "A discovered instance has no NodeId.");
        }
        if (!visited.Add(target.NodeId))
        {
            return;
        }
        CheckCount(targets.Count + 1, maximum, "Discovered instances");
        targets.Add(target);
    }
}

/// <summary>
/// A bounded field buffer that preserves value quality instead of disguising bad telemetry as a successful read.
/// </summary>
internal sealed class CellCompanionFields
{
    public CellCompanionFields(int maximum)
    {
        m_maximum = maximum;
    }

    public void Add(string name, Variant value)
    {
        CellCompanionSupport.CheckCount(m_values.Count + 1, m_maximum, "Inspection fields");
        if (value.TryGetValue(out string? text) && text is { Length: > 4096 })
        {
            throw new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded, "An inspection string exceeds 4096 characters.");
        }
        if (value.TryGetValue(out LocalizedText localized) && localized.Text is { Length: > 4096 })
        {
            throw new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded, "An inspection label exceeds 4096 characters.");
        }
        m_values.Add(new CompanionValue(name, value));
    }

    public void AddText(string name, string? value)
    {
        Add(name, Variant.From(value ?? string.Empty));
    }

    public void AddDataValue(string name, in DataValue value)
    {
        if (!value.WrappedValue.IsNull &&
            (value.WrappedValue.TypeInfo.ValueRank != ValueRanks.Scalar ||
                value.WrappedValue.TypeInfo.BuiltInType is not (BuiltInType.Float or BuiltInType.Double)))
        {
            throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Robot telemetry must be a numeric scalar.");
        }
        Add(name, value.WrappedValue);
        Add($"{name} status", Variant.From(value.StatusCode));
        Add($"{name} source time", Variant.From(value.SourceTimestamp));
    }

    public ArrayOf<CompanionValue> ToArray()
    {
        return [.. m_values];
    }

    private readonly int m_maximum;
    private readonly List<CompanionValue> m_values = [];
}
