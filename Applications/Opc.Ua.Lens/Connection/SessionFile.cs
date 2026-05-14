/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Subscriptions;

namespace UaLens.Connection;

/// <summary>
/// JSON-serializable snapshot of a UaLens session:
/// connection endpoint + engine kind + every open subscription tab
/// with its publishing config and monitored items.  Used by File →
/// Save Session / File → Load Session.
/// </summary>
internal sealed class SessionFile
{
    public string Version { get; set; } = "1";
    public string EndpointUrl { get; set; } = "";
    public string Engine { get; set; } = "ChannelV2";
    public List<TabSnapshot> Tabs { get; set; } = new();

    public sealed class TabSnapshot
    {
        public string Title { get; set; } = "Sub";
        public TimeSpanMs PublishingInterval { get; set; } = new TimeSpanMs(1000);
        public uint LifetimeCount { get; set; } = 1000;
        public uint KeepAliveCount { get; set; } = 10;
        public uint MaxNotificationsPerPublish { get; set; } = 1000;
        public byte Priority { get; set; }
        public bool PublishingEnabled { get; set; } = true;
        public int MinPublishRequestCount { get; set; } = 2;
        public int MaxPublishRequestCount { get; set; } = 15;
        public List<ItemSnapshot> Items { get; set; } = new();

        // Per-tab UI state — additive (older save files default these to
        // Dots / 1.0 / false).
        public string AnimationMode { get; set; } = "Dots";
        public double AnimationTimeScale { get; set; } = 1.0;
        public bool ShowResourceOverlay { get; set; }
    }

    public sealed class ItemSnapshot
    {
        public string DisplayName { get; set; } = "";
        public string NodeId { get; set; } = "";
        public uint AttributeId { get; set; } = Attributes.Value;
        public TimeSpanMs SamplingInterval { get; set; } = new TimeSpanMs(1000);
        public uint QueueSize { get; set; } = 1;
        public bool DiscardOldest { get; set; } = true;
        public byte MonitoringMode { get; set; } = (byte)Opc.Ua.MonitoringMode.Reporting;
        public bool IsEvent { get; set; }
    }

    /// <summary>Wraps a TimeSpan as a JSON-friendly milliseconds integer.</summary>
    public readonly record struct TimeSpanMs(long Milliseconds)
    {
        public TimeSpan ToTimeSpan() => TimeSpan.FromMilliseconds(Milliseconds);
        public static TimeSpanMs From(TimeSpan ts) => new((long)ts.TotalMilliseconds);
    }

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task SaveAsync(SessionFile file, string path)
    {
        FileStream fs = File.Create(path);
        await using (fs.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(fs, file, s_json).ConfigureAwait(false);
        }
    }

    public static async Task<SessionFile?> LoadAsync(string path)
    {
        FileStream fs = File.OpenRead(path);
        await using (fs.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync<SessionFile>(fs, s_json).ConfigureAwait(false);
        }
    }
}
