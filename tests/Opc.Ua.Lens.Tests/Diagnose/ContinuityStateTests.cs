// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Continuity;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class ContinuityStateTests
{
    [Test]
    public void PortableTargetsAndRequestedSettingsRoundTripWithoutRuntimeState()
    {
        var dto = new ContinuityStateDto
        {
            Scenario = (int)ContinuityScenario.TransferOnLoad,
            Targets = new List<string> { "nsu=urn:sample:counter;s=Counter" },
            Durable = true,
            DurableLifetimeHours = 4,
            ItemsPerPartition = 2,
            MonotonicSample = true
        };
        ContinuityConfiguration configuration = ContinuityState.Validate(dto);
        JsonElement json = JsonSerializer.SerializeToElement(
            ContinuityState.Capture(configuration), ContinuityStateJsonContext.Default.ContinuityStateDto);
        ContinuityConfiguration restored = ContinuityState.Validate(
            json.Deserialize(ContinuityStateJsonContext.Default.ContinuityStateDto)!);

        Assert.That(restored.Targets[0].NamespaceUri, Is.EqualTo("urn:sample:counter"));
        Assert.That(restored.Scenario, Is.EqualTo(ContinuityScenario.TransferOnLoad));
        Assert.That(restored.DurableLifetimeHours, Is.EqualTo(4));
        Assert.That(restored.ItemsPerPartition, Is.EqualTo(2));
        Assert.That(restored.MonotonicSample, Is.True);
        Assert.That(json.GetRawText(), Does.Not.Contain("serverId"));
        Assert.That(json.GetRawText(), Does.Not.Contain("snapshot"));
        Assert.That(json.GetRawText(), Does.Not.Contain("password"));
        Assert.That(json.GetRawText(), Does.Not.Contain("autostart"));
    }

    [TestCase("ns=2;s=Counter")]
    [TestCase("svr=1;i=2258")]
    [TestCase("not-a-node")]
    [TestCase("i=0")]
    public void NonportableOrInvalidTargetsAreRejected(string target)
    {
        var dto = new ContinuityStateDto { Targets = new List<string> { target } };
        Assert.That(() => ContinuityState.Validate(dto), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void InvalidBoundsVersionsAndDuplicateTargetsAreRejected()
    {
        Assert.That(() => ContinuityState.Validate(new ContinuityStateDto { Version = 2 }),
            Throws.TypeOf<FormatException>());
        Assert.That(() => ContinuityState.Validate(new ContinuityStateDto { Scenario = 99 }),
            Throws.TypeOf<FormatException>());
        Assert.That(() => ContinuityState.Validate(new ContinuityStateDto { PublishingIntervalMs = double.NaN }),
            Throws.TypeOf<FormatException>());
        Assert.That(() => ContinuityState.Validate(new ContinuityStateDto { LifetimeCount = 2 }),
            Throws.TypeOf<FormatException>());
        Assert.That(() => ContinuityState.Validate(new ContinuityStateDto { QueueSize = 10001 }),
            Throws.TypeOf<FormatException>());
        Assert.That(() => ContinuityState.Validate(new ContinuityStateDto
        {
            Targets = new List<string> { "i=2258", "i=2258" }
        }), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void BoundsIncludeExactlyTheConfiguredTargetAndQueueLimits()
    {
        var targets = new List<string>();
        for (uint i = 1; i <= ContinuityState.MaxTargets; i++)
        {
            targets.Add(new NodeId(i).ToString());
        }
        var dto = new ContinuityStateDto
        {
            Targets = targets,
            PublishingIntervalMs = 20,
            SamplingIntervalMs = 0,
            QueueSize = 10000,
            ItemsPerPartition = 64,
            DurableLifetimeHours = 168
        };
        ContinuityConfiguration valid = ContinuityState.Validate(dto);
        Assert.That(valid.Targets.Count, Is.EqualTo(64));
        Assert.That(valid.QueueSize, Is.EqualTo(10000));
        targets.Add("i=2258");
        Assert.That(() => ContinuityState.Validate(dto), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void GracefulRestoreAlwaysRequestsDurability()
    {
        ContinuityConfiguration configuration = ContinuityState.Validate(new ContinuityStateDto
        {
            Scenario = (int)ContinuityScenario.GracefulDurableRestore,
            Durable = false
        });
        Assert.That(configuration.Durable, Is.True);
    }

    [Test]
    public async Task RestoringDocumentConfigurationIsOfflineAndDoesNotStartOrLoadAnything()
    {
        var backend = new ContinuityTestBackend();
        var plugin = new ContinuityPlugin(backend, () => null, () => null);
        await using (plugin.ConfigureAwait(false))
        {
            JsonElement state = JsonSerializer.SerializeToElement(
                new ContinuityStateDto
                {
                    Scenario = (int)ContinuityScenario.GracefulDurableRestore,
                    Targets = new List<string> { "nsu=urn:sample:counter;s=Counter" },
                    DurableLifetimeHours = 3
                },
                ContinuityStateJsonContext.Default.ContinuityStateDto);
            await plugin.RestoreStateAsync(state, CancellationToken.None).ConfigureAwait(false);

            Assert.That(backend.Starts, Is.Zero);
            Assert.That(backend.Steps, Is.Zero);
            Assert.That(plugin.Durable, Is.True);
            Assert.That(plugin.TargetText, Is.EqualTo("nsu=urn:sample:counter;s=Counter"));
            Assert.That(plugin.Status, Does.Contain("offline"));
            Assert.That(plugin.Status, Does.Contain("Start is required"));
            Assert.That(plugin.CaptureState().GetProperty("durableLifetimeHours").GetInt32(), Is.EqualTo(3));
        }
        Assert.That(backend.Disposals, Is.EqualTo(1));
    }

    [Test]
    public async Task CanceledRestoreDoesNotChangeConfiguration()
    {
        var backend = new ContinuityTestBackend();
        var plugin = new ContinuityPlugin(backend, () => null, () => null);
        await using (plugin.ConfigureAwait(false))
        {
            JsonElement state = JsonSerializer.SerializeToElement(
                new ContinuityStateDto { QueueSize = 500 }, ContinuityStateJsonContext.Default.ContinuityStateDto);
            await Assert.ThatAsync(
                () => plugin.RestoreStateAsync(state, new CancellationToken(true)),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(plugin.QueueSize, Is.EqualTo(100));
            Assert.That(backend.Starts, Is.Zero);
        }
    }
}
