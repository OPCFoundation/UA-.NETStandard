/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
