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

public sealed partial class CertificateManagerWorkflowTests
{

    internal static Task SelectAsync(CertificateManagerPlugin plugin, CertStoreNode node, int count)
    {
        return DesktopInteraction.ModelChangedAsync(plugin,
            () => plugin.Status == $"● {node.DisplayName}: {count} certificate(s).", () =>
            {
                plugin.SelectedStore = node;
                return Task.CompletedTask;
            });
    }

    private static Task MoveAsync(CertificateManagerPlugin plugin, int destination)
    {
        return destination switch
        {
            0 => plugin.TrustToPeerAsync(),
            1 => plugin.TrustToIssuerAsync(),
            _ => plugin.RejectAsync()
        };
    }

    private static Mock<ICertificateStore> ReleasedStore(List<string> order)
    {
        var store = new Mock<ICertificateStore>(MockBehavior.Strict);
        store.Setup(s => s.Close()).Callback(() => order.Add("close"));
        store.Setup(s => s.Dispose()).Callback(() => order.Add("dispose"));
        return store;
    }

    private static Mock<CertificateStoreIdentifier> StoreIdentifier(
        TemporaryCertificateStores stores, string name, AdministrationWorkflowContext context, ICertificateStore store)
    {
        var identifier = new Mock<CertificateStoreIdentifier>(
            MockBehavior.Strict, Path.Combine(stores.Root, name), CertificateStoreType.Directory, true);
        identifier.Setup(id => id.OpenStore(context.Telemetry)).Returns(store);
        return identifier;
    }
}
