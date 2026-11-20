/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using Opc.Ua;

namespace UaLens.ViewModels;

/// <summary>
/// Typed creation supplied by a feature's composition code, without changing catalog metadata or document ownership.
/// </summary>
internal sealed record PluginFactoryRegistration(
    PluginKind Kind, Func<PluginHost, IPlugin> Create, bool IsDefault = false);

/// <summary>
/// Selects an injected feature factory or the registry's directly constructible fallback.
/// </summary>
internal interface IPluginFactory
{
    IPlugin Create(PluginKind kind, PluginHost host, Func<PluginHost, IPlugin> fallback);
}

/// <summary>
/// Small immutable dispatch table. Factories return fresh documents; the workspace owns their lifetime.
/// </summary>
internal sealed class PluginFactory : IPluginFactory
{
    public PluginFactory(ArrayOf<PluginFactoryRegistration> registrations = default)
    {
        foreach (PluginFactoryRegistration registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            ArgumentNullException.ThrowIfNull(registration.Create);
            if (m_factories.TryGetValue(registration.Kind, out PluginFactoryRegistration? existing))
            {
                if (existing.IsDefault == registration.IsDefault)
                {
                    throw new ArgumentException(
                        $"A factory for {registration.Kind} is already registered.", nameof(registrations));
                }
                if (registration.IsDefault)
                {
                    continue;
                }
            }
            m_factories[registration.Kind] = registration;
        }
    }

    public static PluginFactory Default { get; } = new();

    public IPlugin Create(PluginKind kind, PluginHost host, Func<PluginHost, IPlugin> fallback)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(fallback);
        return m_factories.TryGetValue(kind, out PluginFactoryRegistration? registration)
            ? registration.Create(host)
            : fallback(host);
    }

    private readonly Dictionary<PluginKind, PluginFactoryRegistration> m_factories = [];
}
