// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Security.Certificates;
using UaLens.Plugins.CertificateManager;
using UaLens.Tests.Desktop;
using UaLens.Views;

namespace UaLens.Tests.Administration;

public sealed partial class CertificateStoreWorkflowTests
{

    private static Task ReadyAsync(CertificateStoreDialog dialog, int trusted, int issuer, int rejected)
    {
        TextBlock label = DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel");
        return DesktopInteraction.ChangedAsync(label,
            () => dialog.Trusted.Count == trusted && dialog.Issuer.Count == issuer && dialog.Rejected.Count == rejected,
            () => Task.CompletedTask);
    }

    private static Task ActionAsync(CertificateStoreDialog dialog, string status, Func<bool> complete, Action action)
    {
        TextBlock label = DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel");
        return DesktopInteraction.ChangedAsync(label, () => label.Text == status && complete(), () =>
        {
            action();
            return Task.CompletedTask;
        });
    }

    private static void Click(CertificateStoreDialog dialog, string name)
    {
        DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, name));
    }
}
