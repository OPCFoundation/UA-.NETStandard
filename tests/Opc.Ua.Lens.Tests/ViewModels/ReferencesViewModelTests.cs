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

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;

namespace UaLens.Tests.ViewModels;

[TestFixture]
public sealed class ReferencesViewModelTests
{
    [Test]
    public async Task OfflineSelectionAndClearReplaceTheEntireReferencePresentation()
    {
        await using var context = new DesktopConnectionContext();
        using var model = new ReferencesViewModel(context.Telemetry, context.Connection);
        await model.LoadAsync(new NodeId("Boiler", 2), NodeClass.Object).ConfigureAwait(false);
        Assert.That(model.Header, Is.EqualTo("◉ ns=2;s=Boiler  (Object)"));
        Assert.That(model.Rows.Single(), Is.EqualTo(
            new ReferenceRow("·", "(disconnected)", string.Empty, string.Empty, string.Empty)));

        await model.LoadAsync(new NodeId("Temperature", 2), NodeClass.Variable).ConfigureAwait(false);

        Assert.That(model.Header, Is.EqualTo("○ ns=2;s=Temperature  (Variable)"));
        Assert.That(model.Rows.Single().ReferenceType, Is.EqualTo("(disconnected)"));
        Assert.That(model.Rows[0].TargetNodeId, Is.Empty);
        model.Clear();
        Assert.That(model.Header, Is.EqualTo("(no node selected)"));
        Assert.That(model.Rows, Is.Empty);
        Assert.That(context.ConfigurationsCreated, Is.Zero);
    }
}
