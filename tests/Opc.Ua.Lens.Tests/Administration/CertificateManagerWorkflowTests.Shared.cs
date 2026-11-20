/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
