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
