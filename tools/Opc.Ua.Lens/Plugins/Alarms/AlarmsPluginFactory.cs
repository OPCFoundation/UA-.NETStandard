// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using UaLens.ViewModels;

namespace UaLens.Plugins.Alarms;

/// <summary>
/// Typed composition hook. A singleton factory creates a fresh document and
/// backend for each host; it never shares an observation or resolves services.
/// </summary>
internal sealed class AlarmsPluginFactory
{
    public AlarmsPluginFactory(Func<PluginHost, IAlarmBackend>? backendFactory = null)
    {
        m_backendFactory = backendFactory ?? (static host => new StackAlarmBackend(host));
    }

    public AlarmsPlugin Create(PluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new AlarmsPlugin(host, m_backendFactory(host));
    }

    private readonly Func<PluginHost, IAlarmBackend> m_backendFactory;
}
