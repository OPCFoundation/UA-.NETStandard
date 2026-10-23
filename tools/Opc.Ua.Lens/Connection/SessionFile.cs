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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Storage;
using UaLens.Subscriptions;
using UaLens.ViewModels;

namespace UaLens.Connection
{
    /// <summary>
    /// JSON-serializable snapshot of a UaLens session:
    /// connection endpoint + engine kind + every open subscription tab
    /// with its publishing config and monitored items. Used by File →
    /// Save Session / File → Load Session.
    /// </summary>
    internal sealed class SessionFile
    {
        public string Version { get; set; } = "1";
        public string EndpointUrl { get; set; } = string.Empty;
        public string Engine { get; set; } = "ChannelV2";
        public List<TabSnapshot> Tabs { get; set; } = [];
        public ConnectionProfile? Profile { get; set; }
        public SessionPublishingSettings? PublishingPipeline { get; set; }
        public List<DocumentSnapshot> Documents { get; set; } = [];
        public int SelectedDocument { get; set; } = -1;
        public bool ShowAddressSpace { get; set; } = true;
        public SidePanelMode Inspector { get; set; }

        public static Task SaveAsync(SessionFile file, string path, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(file);
            file.Validate();
            return JsonFileStore.WriteAsync(path, file, SessionFileJsonContext.Default.SessionFile, cancellationToken);
        }

        public static async Task<SessionFile?> LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            SessionFile file = await JsonFileStore.ReadAsync(
                path, SessionFileJsonContext.Default.SessionFile, cancellationToken).ConfigureAwait(false);
            file.Validate();
            return file;
        }

        public void Validate()
        {
            if (Version is not ("1" or "2"))
            {
                throw new JsonException($"Workspace version '{Version}' is not supported.");
            }
            if (!Enum.TryParse(Engine, ignoreCase: true, out SubscriptionEngineKind engine) || !Enum.IsDefined(engine))
            {
                throw new JsonException($"Unknown subscription engine '{Engine}'.");
            }
            Profile?.Validate();
            if (Version == "2")
            {
                if (Documents is null ||
                    SelectedDocument < -1 ||
                    SelectedDocument >= Documents.Count ||
                    !Enum.IsDefined(Inspector))
                {
                    throw new JsonException("The workspace document selection or layout is invalid.");
                }
                foreach (DocumentSnapshot document in Documents)
                {
                    if (document is null ||
                        !Enum.TryParse(document.Kind, out PluginKind kind) ||
                        !Enum.IsDefined(kind) ||
                        string.IsNullOrWhiteSpace(document.Title) ||
                        document.Settings.ValueKind != JsonValueKind.Object)
                    {
                        throw new JsonException(
                            "A document has an unknown kind, missing title or invalid configuration.");
                    }
                    _ = PluginRegistry.For(kind);
                }
            }
            else if (Tabs is null)
            {
                throw new JsonException("The legacy workspace has no subscription list.");
            }
        }

        public sealed class DocumentSnapshot
        {
            public string Kind { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public JsonElement Settings { get; set; }
        }

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
            public List<ItemSnapshot> Items { get; set; } = [];

            /// <summary>
            /// Per-tab UI state is additive; older save files default to Dots.
            /// </summary>
            public string AnimationMode { get; set; } = "Dots";
            public double AnimationTimeScale { get; set; } = 1.0;
            public bool ShowResourceOverlay { get; set; }
            public int DisplayModeIndex { get; set; }
            public bool ShowItemStatusGrid { get; set; } = true;
            public bool ShowLegend { get; set; }
            public bool ShowXAxis { get; set; }
            public bool ShowYAxis { get; set; }
        }

        public sealed class ItemSnapshot
        {
            public string DisplayName { get; set; } = string.Empty;
            public string NodeId { get; set; } = string.Empty;
            public uint AttributeId { get; set; } = Attributes.Value;
            public TimeSpanMs SamplingInterval { get; set; } = new TimeSpanMs(1000);
            public uint QueueSize { get; set; } = 1;
            public bool DiscardOldest { get; set; } = true;
            public byte MonitoringMode { get; set; } = (byte)Opc.Ua.MonitoringMode.Reporting;
            public bool IsEvent { get; set; }
            public FilterSnapshot? Filter { get; set; }
        }

        public sealed class FilterSnapshot
        {
            public DataChangeTrigger Trigger { get; set; } = DataChangeTrigger.StatusValue;
            public uint DeadbandType { get; set; }
            public double DeadbandValue { get; set; }
        }

        /// <summary>
        /// Stores milliseconds without truncating fractional sampling intervals.
        /// </summary>
        public readonly record struct TimeSpanMs(double Milliseconds)
        {
            public TimeSpan ToTimeSpan()
            {
                return TimeSpan.FromMilliseconds(Milliseconds);
            }

            public static TimeSpanMs From(TimeSpan ts)
            {
                return new(ts.TotalMilliseconds);
            }
        }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    [JsonSerializable(typeof(SessionFile))]
    [JsonSerializable(typeof(SessionFile.TabSnapshot))]
    internal sealed partial class SessionFileJsonContext : JsonSerializerContext;
}
