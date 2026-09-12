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
using System.Linq;
using System.Text;
using System.Text.Json;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using UaLens.Diagnostics;
using UaLens.Plugins.Continuity;
using SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class ContinuityDiagnosticsTests
{
    [Test]
    public void ExportIsBoundedAndStructurallyRedactsTargetsSamplesAndFreeText()
    {
        var timeline = new ContinuityTimeline(capacity: 500);
        var callbacks = new Queue<Action>();
        var publish = new PublishLogObserver(callbacks.Enqueue);
        for (uint i = 1; i <= 800; i++)
        {
            timeline.Record(ContinuityEvidenceKind.Error, "PRIVATE-FREE-TEXT",
                partitionServerId: 42, sequenceNumber: i, numericSample: 12345678901234567890m);
            publish.Record(42, i, DateTime.UtcNow, 1, PublishLogKind.Data);
        }
        ContinuityConfiguration configuration = ContinuityState.Validate(new ContinuityStateDto
        {
            Targets = new List<string> { "nsu=urn:PRIVATE-NAMESPACE;s=PRIVATE-NODE" }
        });

        ByteString exported = DiagnosticEvidenceExport.Create(null, publish, configuration, timeline.Snapshot());
        string json = Encoding.UTF8.GetString(exported.Memory.Span);
        using JsonDocument document = JsonDocument.Parse(exported.Memory);
        JsonElement root = document.RootElement;

        Assert.That(exported.Memory.Length, Is.LessThan(DiagnosticEvidenceExport.MaxBytes));
        Assert.That(json, Does.Not.Contain("PRIVATE-"));
        Assert.That(json, Does.Not.Contain("12345678901234567890"));
        Assert.That(root.GetProperty("configuration").GetProperty("targetCount").GetInt32(), Is.EqualTo(1));
        Assert.That(root.GetProperty("timeline").GetArrayLength(), Is.EqualTo(200));
        Assert.That(root.GetProperty("publishes").GetArrayLength(), Is.EqualTo(200));
        Assert.That(root.GetProperty("timelineEntriesOmitted").GetInt32(), Is.EqualTo(300));
        Assert.That(root.GetProperty("counters").GetProperty("evictedEvidence").GetInt64(), Is.EqualTo(300));
        Assert.That(root.GetProperty("displayDrops").GetInt64(), Is.EqualTo(300));
        Assert.That(root.GetProperty("timeline")[0].GetProperty("partitionServerId").GetUInt32(), Is.EqualTo(42));
    }

    [Test]
    public void DisconnectedCountersAndUnknownIdsExportAsNullNotFabricatedZeros()
    {
        var session = new Mock<ISession>();
        session.SetupGet(value => value.Connected).Returns(false);
        var timeline = new ContinuityTimeline();
        timeline.Record(ContinuityEvidenceKind.KeepAlive, "Unknown partition");
        ByteString exported = DiagnosticEvidenceExport.Create(session.Object, null, timeline: timeline.Snapshot());
        using JsonDocument document = JsonDocument.Parse(exported.Memory);
        JsonElement root = document.RootElement;

        Assert.That(root.GetProperty("clientSessionCorrelation").GetGuid(), Is.Not.EqualTo(Guid.Empty));
        Assert.That(root.GetProperty("serverSessionIdKnown").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(root.GetProperty("missingMessageSlots").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(root.GetProperty("publishWorkers").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(root.GetProperty("timeline")[0].GetProperty("partitionServerId").ValueKind,
            Is.EqualTo(JsonValueKind.Null));
        session.VerifyGet(value => value.OperationLimits, Times.Never);
    }

    [Test]
    public void ExportRetainsTypedLifecycleAndPublishFlagsWithoutExportingMessages()
    {
        var timeline = new ContinuityTimeline();
        timeline.ObserveState(Guid.Empty, 1, SubscriptionState.Created, PublishState.None, 0, 0);
        timeline.ObserveState(Guid.Empty, 1, SubscriptionState.Opened, PublishState.Stopped, 0, 0);
        ByteString exported = DiagnosticEvidenceExport.Create(null, null, timeline: timeline.Snapshot());
        using JsonDocument document = JsonDocument.Parse(exported.Memory);
        JsonElement entries = document.RootElement.GetProperty("timeline");

        Assert.That(entries[0].GetProperty("lifecycleState").GetString(), Is.EqualTo("Created"));
        Assert.That(entries[1].GetProperty("lifecycleState").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(entries[1].GetProperty("publishFlags").GetString(), Is.EqualTo("Stopped"));
        Assert.That(entries[0].TryGetProperty("detail", out _), Is.False);
    }

    [Test]
    public void PermissionDeniedIsNotShownAsAnEmptySuccessfulRead()
    {
        var denied = new DataValue(Variant.From(0u), StatusCodes.BadUserAccessDenied);
        var unsupported = new DataValue(Variant.From(0u), StatusCodes.BadNodeIdUnknown);
        var good = new DataValue(Variant.From(27u));

        Assert.That(CorrelatedDiagnostics.Value(denied), Does.Contain("Denied"));
        Assert.That(CorrelatedDiagnostics.Value(denied), Does.Contain("BadUserAccessDenied"));
        Assert.That(CorrelatedDiagnostics.Value(unsupported), Does.Contain("Unavailable"));
        Assert.That(CorrelatedDiagnostics.Value(good), Is.EqualTo("27"));
    }

    [Test]
    public void ClientDiagnosticsShowEffectiveLimitsActualPartitionsAndUnknownBacklog()
    {
        var session = new Mock<ISession>();
        var manager = new Mock<ISubscriptionManager>();
        ISubscriptionManager? managerInstance = manager.Object;
        session.Setup(value => value.TryGetSubscriptionManager(out managerInstance)).Returns(true);
        session.SetupGet(value => value.Connected).Returns(true);
        session.SetupGet(value => value.SessionId).Returns(new NodeId(123u));
        session.SetupGet(value => value.OperationLimits).Returns(new OperationLimits { MaxNodesPerRead = 5 });
        var partitioned = new Mock<IPartitionedSubscription>();
        partitioned.SetupGet(value => value.PartitionCount).Returns(3);
        var unknown = new Mock<ISubscription>();
        manager.SetupGet(value => value.Items).Returns(new[] { partitioned.Object, unknown.Object });
        manager.SetupGet(value => value.Count).Returns(2);
        manager.SetupGet(value => value.MissingMessageCount).Returns(4);
        manager.SetupGet(value => value.RepublishMessageCount).Returns(6);

        List<DiagnosticMetric> rows = CorrelatedDiagnostics.Capture(session.Object).ToList();

        Assert.That(rows.Single(row => row.Name == "Effective MaxNodesPerRead").Value, Is.EqualTo("5"));
        Assert.That(rows.Single(row => row.Name == "Effective MaxNodesPerWrite").Value, Does.Contain("Unspecified"));
        Assert.That(rows.Single(row => row.Name == "V2 partition count").Value,
            Does.StartWith("3 reported; 1 unknown"));
        Assert.That(rows.Single(row => row.Name == "V2 republish attempts").Value, Is.EqualTo("6"));
        Assert.That(rows.Single(row => row.Name == "Internal queue backlog").Value, Does.Contain("Not exposed"));
    }

    [Test]
    public void DisplayEvictionsAndUnrenderedHistoryRemainAccountedSeparately()
    {
        var observer = new PublishLogObserver(action => action());
        for (uint i = 1; i <= 700; i++)
        {
            observer.Record(9, i, DateTime.UtcNow, 1, PublishLogKind.Data);
        }

        Assert.That(observer.Entries, Has.Count.EqualTo(PublishLogObserver.MaxEntries));
        Assert.That(observer.DroppedDisplayEntries, Is.Zero);
        Assert.That(observer.EvictedDisplayEntries, Is.EqualTo(200));
        Assert.That(observer.CaptureSnapshot().Count, Is.EqualTo(PublishLogObserver.MaxEntries));
        Assert.That(observer.CaptureSnapshot()[0].SequenceNumber, Is.EqualTo(201));
    }
}
