// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Security.Certificates;
using UaLens.Plugins.CertificateManager;
using UaLens.Tests.Desktop;
using ReferenceEqualityComparer = System.Collections.Generic.ReferenceEqualityComparer;

namespace UaLens.Tests.Administration;

internal sealed class CertificateRowsScope : IDisposable
{
    public CertificateRowsScope(CertificateManagerPlugin plugin)
    {
        m_plugin = plugin;
        m_plugin.Certificates.CollectionChanged += Changed;
        foreach (CertItemRow row in m_plugin.Certificates)
        {
            m_certificates.Add(row.Certificate);
        }
    }

    public void Dispose()
    {
        m_plugin.Certificates.CollectionChanged -= Changed;
        foreach (X509Certificate2 certificate in m_certificates)
        {
            certificate.Dispose();
        }
    }

    private void Changed(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null)
        {
            foreach (CertItemRow row in args.NewItems)
            {
                m_certificates.Add(row.Certificate);
            }
        }
    }

    private readonly CertificateManagerPlugin m_plugin;
    private readonly HashSet<X509Certificate2> m_certificates = new(ReferenceEqualityComparer.Instance);
}
