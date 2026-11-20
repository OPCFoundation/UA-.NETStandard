/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.Plugins.Continuity;
using UaLens.Telemetry;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;
using SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

namespace UaLens.Tests.Diagnose;

internal sealed class ContinuityTestSessions : IContinuitySessionFactory
{
    public ContinuityTestSessions(ContinuityStackTestContext source, ContinuityStackTestContext target)
    {
        SourceLease = new ContinuityTestLease(source);
        TargetLease = new ContinuityTestLease(target);
    }

    public ContinuityTestLease SourceLease { get; }

    public ContinuityTestLease TargetLease { get; }

    public List<ContinuitySessionPurpose> Opened { get; } = new();

    public ContinuitySetup CheckSetup(ContinuityScenario scenario)
    {
        return new(ContinuityAvailability.Supported, "Test fixture.");
    }

    public Task<IContinuitySessionLease> OpenAsync(ContinuitySessionPurpose purpose, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Opened.Add(purpose);
        return Task.FromResult<IContinuitySessionLease>(
            purpose == ContinuitySessionPurpose.Source ? SourceLease : TargetLease);
    }
}
