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

namespace UaLens.ViewModels;

/// <summary>
/// Typed creation supplied by a feature's composition code, without changing catalog metadata or document ownership.
/// </summary>
internal sealed record PluginFactoryRegistration(PluginKind Kind, Func<PluginHost, IPlugin> Create);

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
            if (!m_factories.TryAdd(registration.Kind, registration.Create))
            {
                throw new ArgumentException(
                    $"A factory for {registration.Kind} is already registered.", nameof(registrations));
            }
        }
    }

    public static PluginFactory Default { get; } = new();

    public IPlugin Create(PluginKind kind, PluginHost host, Func<PluginHost, IPlugin> fallback)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(fallback);
        return m_factories.TryGetValue(kind, out Func<PluginHost, IPlugin>? create)
            ? create(host)
            : fallback(host);
    }

    private readonly Dictionary<PluginKind, Func<PluginHost, IPlugin>> m_factories = [];
}
