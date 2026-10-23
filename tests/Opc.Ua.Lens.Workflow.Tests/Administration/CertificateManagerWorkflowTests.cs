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

[TestFixture]
[NonParallelizable]
public sealed partial class CertificateManagerWorkflowTests
{

    [TestCase(-1, "EXPIRED")]
    [TestCase(0, "")]
    [TestCase(1, "NOT YET VALID")]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    public Task DetailsShowsTheSelectedCertificateAndClosingDoesNotInvalidateItsBorrowedHandle(
        int validity, string warning)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var stores = new TemporaryCertificateStores();
            using Certificate certificate =
                TemporaryCertificateStores.CreateCertificate("CN=Details candidate", validity);
            await stores.AddAsync(stores.Peer, certificate).ConfigureAwait(true);
            await using var context = new AdministrationWorkflowContext();
            await using var plugin = new CertificateManagerPlugin(context.Host);
            using var ownership = new CertificateRowsScope(plugin);
            await SelectAsync(plugin, stores.Peer, 1).ConfigureAwait(true);
            plugin.SelectedCertificate = plugin.Certificates.Single();
            Task operation = Task.CompletedTask;
            Window dialog = await DesktopInteraction.OpenedAsync<Window>(() => operation = plugin.ViewDetailsAsync())
                .ConfigureAwait(true);
            try
            {
                Assert.That(dialog.Title, Is.EqualTo("Certificate details"));
                Assert.That(dialog.Owner, Is.SameAs(DesktopInteraction.Owner));
                var grid = (Grid)dialog.Content!;
                string Row(int row) => grid.Children.OfType<TextBlock>()
                    .Single(c => Grid.GetRow(c) == row && Grid.GetColumn(c) == 1).Text!;
                Assert.That(Row(0), Is.EqualTo("CN=Details candidate"));
                Assert.That(Row(1), Is.EqualTo("CN=Details candidate"));
                Assert.That(Row(5), Is.EqualTo(certificate.Thumbprint));
                Assert.That(Row(7), Is.EqualTo("no"));
                Assert.That(Row(3).Contains('⚠', StringComparison.Ordinal), Is.EqualTo(validity != 0));
                if (validity != 0)
                {
                    Assert.That(Row(3), Does.Contain(warning));
                }
                TextBox pem = grid.Children.OfType<TextBox>().Single();
                Assert.That(pem.IsReadOnly, Is.True);
                string body = string.Concat(pem.Text!.Split('\n')
                    .Where(line => !line.StartsWith("-----", StringComparison.Ordinal)));
                Assert.That(Convert.FromBase64String(body), Is.EqualTo(certificate.RawData));
                DesktopInteraction.Click(grid.Children.OfType<Button>().Single());
                await operation.ConfigureAwait(true);
                Assert.That(plugin.SelectedCertificate!.Certificate.RawData, Is.EqualTo(certificate.RawData));
            }
            finally
            {
                dialog.Close();
                await operation.ConfigureAwait(true);
            }
        });
    }
}
