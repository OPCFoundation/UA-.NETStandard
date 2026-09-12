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
using Opc.Ua;
using UaLens.Capabilities;
using UaLens.Connection;
using UaLens.ViewModels;

namespace UaLens.Views;

/// <summary>
/// One catalog entry projected from a <see cref="PluginRegistration"/>. The
/// catalog never invents tools; it only reshapes the registry for presentation.
/// </summary>
internal sealed record ToolCatalogEntry(
    PluginKind Kind,
    string DisplayName,
    string Description,
    string Glyph,
    string ConnectionRequirement,
    string? Shortcut,
    CapabilityResult Availability,
    string? CapabilityLabel,
    CapabilityRequest? Capability)
{
    public string AvailabilityText
    {
        get
        {
            string state = Availability.State == CapabilityState.RequiresConfiguration
                ? "Requires configuration" : Availability.State.ToString();
            string target = CapabilityLabel is null ? string.Empty : $"{CapabilityLabel}: ";
            return $"{target}{state}. {Availability.Reason}";
        }
    }
}

/// <summary>
/// A named catalog group with its ordered tool entries.
/// </summary>
internal sealed record ToolCatalogGroup(string Name, ArrayOf<ToolCatalogEntry> Tools);

/// <summary>
/// Builds the searchable, grouped Add-Tool catalog from
/// <see cref="PluginRegistry.All"/>. Group and item order follow the registry
/// metadata; nothing here duplicates the tool definitions or shortcut list.
/// </summary>
internal static class ToolCatalog
{
    /// <summary>
    /// Returns the catalog grouped per registry metadata, optionally filtered by
    /// a case-insensitive query over the name, description and group. Empty
    /// groups are omitted; a blank query returns the whole catalog.
    /// </summary>
    public static ArrayOf<ToolCatalogGroup> Build(string? search = null, PluginHost? host = null)
    {
        string query = (search ?? string.Empty).Trim();
        var groups = new List<ToolCatalogGroup>();
        foreach (ToolGroup group in Enum.GetValues<ToolGroup>())
        {
            var entries = new List<ToolCatalogEntry>();
            foreach (PluginRegistration registration in PluginRegistry.All)
            {
                if (registration.Group != group || !Matches(registration, query))
                {
                    continue;
                }
                entries.Add(new ToolCatalogEntry(
                    registration.Kind,
                    registration.DisplayName,
                    registration.Description,
                    registration.Glyph,
                    registration.ConnectionRequirement,
                    registration.InputGesture,
                    GetAvailability(registration, host),
                    registration.CapabilityLabel,
                    registration.ConnectionScope == ToolConnectionScope.Primary ? registration.Capability : null));
            }
            if (entries.Count > 0)
            {
                groups.Add(new ToolCatalogGroup(GroupName(group), [.. entries]));
            }
        }
        return [.. groups];
    }

    /// <summary>
    /// Reads availability without probing or preventing document creation. A secondary session or an independent
    /// runtime cannot be classified from primary-session evidence.
    /// </summary>
    public static CapabilityResult GetAvailability(PluginRegistration registration, PluginHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (registration.ConnectionScope == ToolConnectionScope.Primary
            && host?.Connection.Snapshot.Phase is ConnectionPhase.Connecting or ConnectionPhase.Reconnecting)
        {
            return new CapabilityResult(CapabilityState.Unknown,
                "The primary session is recovering. Configure offline or wait, then check availability again.");
        }
        return GetAvailability(registration, host?.Connection.IsConnected == true, host?.Capabilities);
    }

    /// <summary>
    /// Projects headless or desktop availability from explicit primary-connection state and cached operation evidence.
    /// </summary>
    public static CapabilityResult GetAvailability(
        PluginRegistration registration,
        bool primaryConnected,
        ICapabilityService? capabilities)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return registration.ConnectionScope switch
        {
            ToolConnectionScope.Local => new CapabilityResult(CapabilityState.Supported,
                "Local configuration is available. Discovery requests still require an explicit endpoint."),
            ToolConnectionScope.Secondary => new CapabilityResult(CapabilityState.RequiresConfiguration,
                "Select a suitable primary or secondary server in the document; its operation checks are independent."),
            ToolConnectionScope.IndependentNetwork => new CapabilityResult(CapabilityState.RequiresConfiguration,
                "Configure the document's network prerequisites, then explicitly Start its runtime."),
            ToolConnectionScope.Primary when !primaryConnected =>
                new CapabilityResult(CapabilityState.RequiresConfiguration,
                    "Configure offline, then connect the primary server to check live operations."),
            ToolConnectionScope.Primary when capabilities is not null && registration.Capability is { } request =>
                capabilities.GetCached(request),
            ToolConnectionScope.Primary => new CapabilityResult(CapabilityState.Unknown,
                "The primary server is connected. Select a target in the document to check its operations."),
            _ => throw new ArgumentOutOfRangeException(nameof(registration))
        };
    }

    private static string GroupName(ToolGroup group)
    {
        foreach (PluginRegistration registration in PluginRegistry.All)
        {
            if (registration.Group == group)
            {
                return registration.GroupName;
            }
        }
        return group.ToString();
    }

    private static bool Matches(PluginRegistration registration, string query)
    {
        return query.Length == 0
            || registration.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || registration.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || registration.GroupName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
