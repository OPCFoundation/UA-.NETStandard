/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;
using Opc.Ua.Client.Roles;

namespace UaLens.Plugins.RoleManagement;

/// <summary>
/// View-model wrapper around <see cref="RoleInfo"/> used by the
/// Role Management plug-in.  The role identity (<see cref="RoleId"/> and
/// <see cref="BrowseName"/>) is immutable, while the per-collection
/// properties (<see cref="Identities"/>, <see cref="Applications"/>,
/// <see cref="Endpoints"/>) and the three booleans are mutable: the
/// plug-in's commands mutate them in place after a successful server
/// round-trip so the right-hand detail pane updates without a full
/// refresh of every role.
/// </summary>
internal sealed partial class RoleVm : ObservableObject
{
    /// <summary>Initializes the view-model from a server snapshot.</summary>
    public RoleVm(RoleInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        RoleId = info.RoleId;
        BrowseName = info.BrowseName;

        Identities = new ObservableCollection<IdentityMappingRuleType>(info.Identities);
        Applications = new ObservableCollection<string>(info.Applications);
        Endpoints = new ObservableCollection<EndpointType>(info.Endpoints);

        m_applicationsExclude = info.ApplicationsExclude;
        m_endpointsExclude = info.EndpointsExclude;
        m_customConfiguration = info.CustomConfiguration;
    }

    /// <summary>The role's NodeId (immutable).</summary>
    public NodeId RoleId { get; }

    /// <summary>The role's browse name (immutable).</summary>
    public QualifiedName BrowseName { get; }

    /// <summary>Identity-mapping rules currently configured on the role.</summary>
    public ObservableCollection<IdentityMappingRuleType> Identities { get; }

    /// <summary>ApplicationUris currently configured on the role.</summary>
    public ObservableCollection<string> Applications { get; }

    /// <summary>Endpoints currently configured on the role.</summary>
    public ObservableCollection<EndpointType> Endpoints { get; }

    [ObservableProperty]
    private bool m_applicationsExclude;

    [ObservableProperty]
    private bool m_endpointsExclude;

    [ObservableProperty]
    private bool m_customConfiguration;

    /// <summary>Convenience: the role's display name (BrowseName.Name).</summary>
    public string DisplayName => BrowseName.Name ?? string.Empty;

    /// <summary>Replaces the three collections in place from a server snapshot.</summary>
    public void UpdateFrom(RoleInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        Identities.Clear();
        foreach (IdentityMappingRuleType rule in info.Identities)
        {
            Identities.Add(rule);
        }
        Applications.Clear();
        foreach (string app in info.Applications)
        {
            Applications.Add(app);
        }
        Endpoints.Clear();
        foreach (EndpointType ep in info.Endpoints)
        {
            Endpoints.Add(ep);
        }
        ApplicationsExclude = info.ApplicationsExclude;
        EndpointsExclude = info.EndpointsExclude;
        CustomConfiguration = info.CustomConfiguration;
    }

    /// <summary>Snapshot helper used by listbox templates / status strings.</summary>
    public IReadOnlyList<IdentityMappingRuleType> IdentitiesSnapshot => Identities;
}
