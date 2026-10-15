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

[TestFixture]
[NonParallelizable]
public sealed partial class CertificateStoreWorkflowTests
{
    [TestCase(-1, "EXPIRED", "2001-01-01", "2002-01-01")]
    [TestCase(0, "OK", "2020-01-01", "2080-01-01")]
    [TestCase(1, "NOT YET VALID", "2090-01-01", "2091-01-01")]
    public void CertificateRowsProjectCommonNameUtcValidityAndDoNotConsumeTheInput(
        int validity, string status, string before, string after)
    {
        using Certificate certificate =
            TemporaryCertificateStores.CreateCertificate("CN=Row candidate,O=Fixture", validity);
        using var x509 = certificate.AsX509Certificate2();

        CertItemRow workbench = CertItemRow.From(x509);
        CertRow modal = CertRow.From(x509);

        Assert.That(workbench.Subject, Is.EqualTo("Row candidate"));
        Assert.That(workbench.Issuer, Is.EqualTo("Row candidate"));
        Assert.That(workbench.NotBefore, Is.EqualTo(before));
        Assert.That(workbench.NotAfter, Is.EqualTo(after));
        Assert.That(workbench.Status, Is.EqualTo(status));
        Assert.That(workbench.Thumbprint, Is.EqualTo(certificate.Thumbprint));
        Assert.That(workbench.Certificate, Is.SameAs(x509));
        Assert.That(modal.Subject, Is.EqualTo("Row candidate"));
        Assert.That(modal.Issuer, Is.EqualTo("Row candidate"));
        Assert.That(modal.NotBefore, Is.EqualTo(before));
        Assert.That(modal.NotAfter, Is.EqualTo(after));
        Assert.That(modal.Status, Is.EqualTo(status));
        Assert.That(modal.Thumbprint, Is.EqualTo(certificate.Thumbprint));
        Assert.That(x509.RawData, Is.EqualTo(certificate.RawData));
    }

    [Test]
    public void StoreRoleDescriptionAndCertificateWithoutCommonNameRetainTheirExactDisplayMeaning()
    {
        using var temporary = new TemporaryCertificateStores();
        string[] glyphs = ["app", "trust", "ca", "rej", "dir"];
        for (int i = 0; i < glyphs.Length; i++)
        {
            var node = new CertStoreNode((CertStoreRole)i, "Commissioning", temporary.Identifier("role-" + i));
            Assert.That(node.Glyph, Is.EqualTo(glyphs[i]));
            Assert.That(node.Description, Is.EqualTo("[Directory]" + Path.Combine(temporary.Root, "role-" + i)));
            Assert.That(node.DisplayName, Is.EqualTo("Commissioning"));
        }
        var unspecified = new CertStoreNode((CertStoreRole)999, "Unspecified",
            new CertificateStoreIdentifier("relative-store", string.Empty));
        Assert.That(unspecified.Description, Is.EqualTo("relative-store"));
        Assert.That(unspecified.Glyph, Is.EqualTo("dir"));
        using Certificate certificate = TemporaryCertificateStores.CreateCertificate("O=No common name");
        using var x509 = certificate.AsX509Certificate2();
        Assert.That(CertItemRow.From(x509).Subject, Is.EqualTo("O=No common name"));
        Assert.That(CertRow.From(x509).Issuer, Is.EqualTo("O=No common name"));
    }
}
