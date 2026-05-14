/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;
using Opc.Ua.Gds;

namespace UaLens.Plugins.GdsManagement;

/// <summary>
/// Display-friendly view-model wrapping a single
/// <see cref="ApplicationRecordDataType"/> as returned by the GDS
/// directory.  Used as a row in the registered-applications list in the
/// GDS Management plugin.  Kept tiny and free of any control references so
/// it stays AOT-clean and re-bindable across refresh cycles.
/// </summary>
internal sealed partial class RegisteredApp : ObservableObject
{
    /// <summary>The underlying GDS application record (may be null for
    /// query rows that don't yet have a resolved NodeId).</summary>
    public ApplicationRecordDataType? Record { get; init; }

    /// <summary>The application's GDS-assigned id.  When
    /// <see cref="Record"/> is null this is <see cref="NodeId.Null"/>.</summary>
    public NodeId ApplicationId { get; init; } = NodeId.Null;

    [ObservableProperty]
    private string m_applicationName = string.Empty;

    [ObservableProperty]
    private string m_applicationUri = string.Empty;

    [ObservableProperty]
    private string m_productUri = string.Empty;

    [ObservableProperty]
    private string m_applicationType = string.Empty;

    [ObservableProperty]
    private string m_discoveryUrls = string.Empty;

    [ObservableProperty]
    private string m_serverCapabilities = string.Empty;

    /// <summary>Display string for the "Registered" column — taken from
    /// the underlying record's NodeId.  GDS records don't carry a
    /// registration timestamp, so we surface the NodeId as a stable
    /// identifier suffix for the user.</summary>
    [ObservableProperty]
    private string m_identifier = string.Empty;

    /// <summary>
    /// Builds a <see cref="RegisteredApp"/> from a resolved
    /// <see cref="ApplicationRecordDataType"/> (i.e. one obtained via
    /// <c>FindApplication</c>, which carries the NodeId).
    /// </summary>
    public static RegisteredApp FromRecord(ApplicationRecordDataType record)
    {
        ArgumentNullException.ThrowIfNull(record);
        string name = string.Empty;
        if (record.ApplicationNames is { } names && names.Count > 0)
        {
            name = names[0].Text ?? string.Empty;
        }
        string disco = JoinNonEmpty(record.DiscoveryUrls, "\n");
        string caps = JoinNonEmpty(record.ServerCapabilities, ", ");
        return new RegisteredApp
        {
            Record = record,
            ApplicationId = record.ApplicationId,
            ApplicationName = string.IsNullOrEmpty(name)
                ? (record.ApplicationUri ?? "(unnamed)")
                : name,
            ApplicationUri = record.ApplicationUri ?? string.Empty,
            ProductUri = record.ProductUri ?? string.Empty,
            ApplicationType = record.ApplicationType.ToString(),
            DiscoveryUrls = disco,
            ServerCapabilities = caps,
            Identifier = record.ApplicationId.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// Builds a <see cref="RegisteredApp"/> from a
    /// <see cref="ApplicationDescription"/> returned by <c>QueryApplications</c>.
    /// The NodeId is not present in this surface — callers should later
    /// resolve via <c>FindApplication(applicationUri)</c> to populate
    /// <see cref="ApplicationId"/> for management operations.
    /// </summary>
    public static RegisteredApp FromDescription(ApplicationDescription desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        string name = desc.ApplicationName.IsNull
            ? (desc.ApplicationUri ?? "(unnamed)")
            : (desc.ApplicationName.Text ?? "(unnamed)");
        string disco = JoinNonEmpty(desc.DiscoveryUrls, "\n");
        return new RegisteredApp
        {
            Record = null,
            ApplicationId = NodeId.Null,
            ApplicationName = name,
            ApplicationUri = desc.ApplicationUri ?? string.Empty,
            ProductUri = desc.ProductUri ?? string.Empty,
            ApplicationType = desc.ApplicationType.ToString(),
            DiscoveryUrls = disco,
            ServerCapabilities = string.Empty,
            Identifier = "(unresolved)"
        };
    }

    /// <summary>
    /// Joins a possibly-null <c>ArrayOf&lt;string&gt;</c> with the given
    /// separator, skipping empty entries.  Avoids the LINQ
    /// <c>.Where()</c> path which doesn't compile for OPC UA's
    /// <c>ArrayOf&lt;T&gt;</c> (it isn't <see cref="IEnumerable{T}"/>).
    /// </summary>
    private static string JoinNonEmpty(Opc.Ua.ArrayOf<string>? source, string sep)
    {
        if (source is not { } arr || arr.Count == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        bool first = true;
        for (int i = 0; i < arr.Count; i++)
        {
            string s = arr[i];
            if (string.IsNullOrEmpty(s))
            {
                continue;
            }

            if (!first)
            {
                sb.Append(sep);
            }

            sb.Append(s);
            first = false;
        }
        return sb.ToString();
    }
}
