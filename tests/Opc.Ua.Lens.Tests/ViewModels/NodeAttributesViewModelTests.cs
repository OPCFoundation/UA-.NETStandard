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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Subscriptions;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;

namespace UaLens.Tests.ViewModels;

[TestFixture]
public sealed class NodeAttributesViewModelTests
{
    [TestCase(NodeClass.Object, "12:EventNotifier")]
    [TestCase(NodeClass.Variable,
        "13:Value|14:DataType|15:ValueRank|16:ArrayDimensions|17:AccessLevel|18:UserAccessLevel|" +
        "19:MinimumSamplingInterval|20:Historizing|27:AccessLevelEx")]
    [TestCase(NodeClass.Method, "21:Executable|22:UserExecutable")]
    [TestCase(NodeClass.ObjectType, "8:IsAbstract")]
    [TestCase(NodeClass.VariableType, "13:Value|14:DataType|15:ValueRank|16:ArrayDimensions|8:IsAbstract")]
    [TestCase(NodeClass.ReferenceType, "8:IsAbstract|9:Symmetric|10:InverseName")]
    [TestCase(NodeClass.DataType, "8:IsAbstract|23:DataTypeDefinition")]
    [TestCase(NodeClass.View, "11:ContainsNoLoops|12:EventNotifier")]
    [TestCase(NodeClass.Unspecified, "")]
    public void AttributeSetIncludesOrderedCommonSecurityAttributesAndOnlyTheSpecificClass(
        NodeClass nodeClass, string tail)
    {
        string common = "1:NodeId|2:NodeClass|3:BrowseName|4:DisplayName|5:Description|6:WriteMask|7:UserWriteMask|" +
            "24:RolePermissions|25:UserRolePermissions|26:AccessRestrictions";
        string[] expected = (common + (tail.Length == 0 ? string.Empty : "|" + tail)).Split('|');

        var actual = NodeAttributeSets.SupportedAttributes(nodeClass);

        Assert.That(actual.Select(entry => $"{entry.AttributeId}:{entry.Name}"), Is.EqualTo(expected));
        Assert.That(actual.Select(entry => entry.AttributeId).Distinct().Count(), Is.EqualTo(expected.Length));
        Assert.That(
            actual.Take(10).Select(entry => entry.Name),
            Is.EqualTo(s_attributeSetIncludesOrderedCommonSecurityAttributesAndOnlyTheExpected));
        Assert.That(NodeAttributeSets.SupportedAttributes(nodeClass), Is.Not.SameAs(actual));
    }

    [Test]
    public async Task OfflineLoadReplacesPreviousSelectionAndClearRemovesDisconnectedPresentation()
    {
        await using var context = new DesktopConnectionContext();
        using var model = new NodeAttributesViewModel(context.Telemetry, context.Connection);
        await model.LoadAsync(new NodeId("Temperature", 2), NodeClass.Variable).ConfigureAwait(false);
        Assert.That(model.Header, Is.EqualTo("○ ns=2;s=Temperature  (Variable)"));
        Assert.That(model.Rows.Single(), Is.EqualTo(new AttributeRow("(disconnected)", string.Empty)));

        await model.LoadAsync(new NodeId("Calibrate", 2), NodeClass.Method).ConfigureAwait(false);

        Assert.That(model.Header, Is.EqualTo("▶ ns=2;s=Calibrate  (Method)"));
        Assert.That(model.Rows.Single(), Is.EqualTo(new AttributeRow("(disconnected)", string.Empty)));
        model.Clear();
        Assert.That(model.Header, Is.EqualTo("(no node selected)"));
        Assert.That(model.Rows, Is.Empty);
        Assert.That(context.ConfigurationsCreated, Is.Zero);
    }

    private static readonly string[] s_attributeSetIncludesOrderedCommonSecurityAttributesAndOnlyTheExpected =
    [
        "NodeId",
        "NodeClass",
        "BrowseName",
        "DisplayName",
        "Description",
        "WriteMask",
        "UserWriteMask",
        "RolePermissions",
        "UserRolePermissions",
        "AccessRestrictions",
    ];
}
