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
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;

namespace UaLens.Diagnostics;

internal sealed record DiagnosticMetric(string Name, string Value);

/// <summary>
/// Local correlation labels are independent of server-assigned identities. No
/// endpoint, authentication token, identity object or data value is retained here.
/// </summary>
internal static class DiagnosticCorrelation
{
    public static Guid Session(ISession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return s_sessions.GetValue(session, static _ => new SessionIdentity(Guid.NewGuid())).Id;
    }

    public static long Subscription(ISubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        return s_subscriptions.GetValue(
            subscription, static _ => new SubscriptionIdentity(Interlocked.Increment(ref s_nextSubscription))).Id;
    }

    private sealed record SessionIdentity(Guid Id);

    private sealed record SubscriptionIdentity(long Id);

    private static readonly ConditionalWeakTable<ISession, SessionIdentity> s_sessions = new();
    private static readonly ConditionalWeakTable<ISubscription, SubscriptionIdentity> s_subscriptions = new();
    private static long s_nextSubscription;
}

/// <summary>
/// A bounded view of effective client limits and the V2 pipeline, not a replacement
/// for permission-sensitive server diagnostics. Zero limits remain unspecified.
/// </summary>
internal static class CorrelatedDiagnostics
{
    public static ArrayOf<DiagnosticMetric> Capture(ISession? session, PublishLogObserver? publishLog = null)
    {
        var rows = new List<DiagnosticMetric>
        {
            new("Packet capture", "Unconfigured / optional; callback counters require no capture driver."),
            new("Clock interpretation", "Source / publish / callback / UI clocks are distinct; latency is unproven.")
        };
        if (publishLog is not null)
        {
            rows.Add(new("Publish display drops", Number(publishLog.DroppedDisplayEntries)));
            rows.Add(new("Publish display evictions", Number(publishLog.EvictedDisplayEntries)));
        }
        if (session is null)
        {
            rows.Add(new("Session", "Unavailable (not connected)."));
            return new ArrayOf<DiagnosticMetric>(rows.ToArray());
        }
        rows.Add(new("Client session correlation", DiagnosticCorrelation.Session(session).ToString("D")));
        if (!session.Connected)
        {
            rows.Add(new("Session counters", "Unavailable while reconnecting / disconnected; not zero."));
            return new ArrayOf<DiagnosticMetric>(rows.ToArray());
        }
        rows.Add(new("Server session id", session.SessionId.IsNull ? "Unknown" : session.SessionId.ToString()));
        rows.Add(new("Outstanding requests", Number(session.OutstandingRequestCount)));
        rows.Add(new("Defunct requests", Number(session.DefunctRequestCount)));
        OperationLimits limits = session.OperationLimits;
        rows.Add(new("Effective MaxNodesPerRead", Limit(limits.MaxNodesPerRead)));
        rows.Add(new("Effective MaxNodesPerWrite", Limit(limits.MaxNodesPerWrite)));
        rows.Add(new("Effective MaxNodesPerBrowse", Limit(limits.MaxNodesPerBrowse)));
        rows.Add(new("Effective MaxNodesPerMethodCall", Limit(limits.MaxNodesPerMethodCall)));
        rows.Add(new("Effective MaxMonitoredItemsPerCall", Limit(limits.MaxMonitoredItemsPerCall)));
        if (session.TryGetSubscriptionManager(out ISubscriptionManager? manager) && manager is not null)
        {
            rows.Add(new("V2 logical subscriptions", Number(manager.Count)));
            rows.Add(new("V2 publish workers", Number(manager.PublishWorkerCount)));
            rows.Add(new("V2 configured worker min / max",
                $"{Number(manager.MinPublishWorkerCount)} / {Number(manager.MaxPublishWorkerCount)}"));
            rows.Add(new("V2 good / bad publish requests",
                $"{Number(manager.GoodPublishRequestCount)} / {Number(manager.BadPublishRequestCount)}"));
            rows.Add(new("V2 missing message slots", Number(manager.MissingMessageCount)));
            rows.Add(new("V2 republish attempts", Number(manager.RepublishMessageCount)));
            long partitions = 0;
            int unreported = 0;
            int sampled = 0;
            foreach (ISubscription subscription in manager.Items)
            {
                if (++sampled > MaxSubscriptions)
                {
                    break;
                }
                if (subscription is IPartitionedSubscription partitioned)
                {
                    partitions += partitioned.PartitionCount;
                }
                else
                {
                    unreported++;
                }
            }
            rows.Add(new("V2 partition count",
                $"{Number(partitions)} reported; {Number(unreported)} unknown; scan bounded to 256 logicals."));
            rows.Add(new("Republish success", "Not inferred from attempt count; correlate recovery callbacks."));
            rows.Add(new("Internal queue backlog", "Not exposed by public V2 API."));
        }
        else
        {
            rows.Add(new("V2 pipeline", "Unavailable for the selected subscription engine."));
        }
        return new ArrayOf<DiagnosticMetric>(rows.ToArray());
    }

    public static string Failure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is ServiceResultException service
            ? service.StatusCode.ToString()
            : exception is OperationCanceledException ? "Canceled" : exception.GetType().Name;
    }

    public static string Value(in DataValue value)
    {
        if (StatusCode.IsBad(value.StatusCode))
        {
            string category = value.StatusCode == StatusCodes.BadUserAccessDenied ||
                value.StatusCode == StatusCodes.BadSecurityModeInsufficient ||
                value.StatusCode == StatusCodes.BadIdentityTokenRejected
                ? "Denied"
                : "Unavailable";
            return $"{category}: {value.StatusCode}";
        }
        if (StatusCode.IsUncertain(value.StatusCode))
        {
            return $"Uncertain: {value.StatusCode}";
        }
        Variant variant = value.WrappedValue;
        if (variant.TryGetValue(out uint number))
        {
            return Number(number);
        }
        if (variant.TryGetValue(out int signed))
        {
            return Number(signed);
        }
        if (variant.TryGetValue(out DateTimeUtc time))
        {
            return time.IsNull ? "Unknown timestamp" : time.ToString("u", CultureInfo.InvariantCulture);
        }
        if (variant.TryGetValue(out string text))
        {
            return text.Length > 256 ? text[..256] + "…" : text;
        }
        if (variant.TryGetValue(out LocalizedText localized))
        {
            string textValue = localized.IsNull ? string.Empty : localized.Text ?? string.Empty;
            return textValue.Length > 256 ? textValue[..256] + "…" : textValue;
        }
        return variant.IsNull ? "Unknown (null value)" : "Value type not projected by this panel.";
    }

    private static string Limit(uint value)
    {
        return value == 0
            ? "Unspecified client limit (0); server support/permissions remain unknown."
            : Number(value);
    }

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private const int MaxSubscriptions = 256;
}
