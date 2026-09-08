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
    string? Shortcut);

/// <summary>
/// A named catalog group with its ordered tool entries. The collection is a
/// read-only list so it can be data-bound directly by the catalog dialog.
/// </summary>
internal sealed record ToolCatalogGroup(string Name, IReadOnlyList<ToolCatalogEntry> Tools);

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
    public static IReadOnlyList<ToolCatalogGroup> Build(string? search = null)
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
                    registration.InputGesture));
            }
            if (entries.Count > 0)
            {
                groups.Add(new ToolCatalogGroup(GroupName(group), entries));
            }
        }
        return groups;
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
