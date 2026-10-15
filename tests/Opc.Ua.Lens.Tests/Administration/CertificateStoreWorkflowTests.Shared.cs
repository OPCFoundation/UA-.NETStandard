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
