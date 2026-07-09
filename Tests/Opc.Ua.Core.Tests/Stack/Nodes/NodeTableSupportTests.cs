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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Nodes
{
    [TestFixture]
    [Category("Nodes")]
    [Parallelizable]
    public sealed class NodeTableSupportTests
    {
        [Test]
        public void ViewTableDefaultViewAcceptsAnyNodeAndReference()
        {
            var table = new ViewTable();
            var defaultView = new ViewDescription();

            Assert.That(table.IsValid(defaultView), Is.True);
            Assert.That(table.IsNodeInView(defaultView, new NodeId(123)), Is.True);
            Assert.That(table.IsReferenceInView(defaultView, new ReferenceDescription()), Is.True);
        }

        [Test]
        public void ViewTableAddValidatesNodeIdAndDuplicates()
        {
            var table = new ViewTable();
            var invalid = new ViewNode { NodeId = NodeId.Null };
            var view = new ViewNode { NodeId = new NodeId(5000) };

            Assert.That(() => table.Add(null), Throws.ArgumentNullException);
            ServiceResultException invalidEx = Assert.Throws<ServiceResultException>(() => table.Add(invalid));
            Assert.That(invalidEx.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));

            table.Add(view);

            Assert.That(table.IsValid(new ViewDescription { ViewId = view.NodeId }), Is.True);
            ServiceResultException duplicateEx = Assert.Throws<ServiceResultException>(() => table.Add(view));
            Assert.That(duplicateEx.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
        }

        [Test]
        public void ViewTableRemoveValidatesNullUnknownAndExistingView()
        {
            var table = new ViewTable();
            NodeId viewId = new(6000);
            table.Add(new ViewNode { NodeId = viewId });

            Assert.That(() => table.Remove(NodeId.Null), Throws.ArgumentNullException);
            ServiceResultException unknownEx = Assert.Throws<ServiceResultException>(
                () => table.Remove(new NodeId(7000)));
            Assert.That(unknownEx.StatusCode, Is.EqualTo(StatusCodes.BadViewIdUnknown));

            table.Remove(viewId);

            Assert.That(table.IsValid(new ViewDescription { ViewId = viewId }), Is.False);
        }

        [Test]
        public void ViewTableKnownViewOperationsThrowBadViewIdUnknown()
        {
            var table = new ViewTable();
            NodeId viewId = new(8000);
            var description = new ViewDescription { ViewId = viewId };
            table.Add(new ViewNode { NodeId = viewId });

            ServiceResultException nodeEx = Assert.Throws<ServiceResultException>(
                () => table.IsNodeInView(description, new NodeId(1)));
            ServiceResultException referenceEx = Assert.Throws<ServiceResultException>(
                () => table.IsReferenceInView(description, new ReferenceDescription()));

            Assert.That(nodeEx.StatusCode, Is.EqualTo(StatusCodes.BadViewIdUnknown));
            Assert.That(referenceEx.StatusCode, Is.EqualTo(StatusCodes.BadViewIdUnknown));
        }

        [Test]
        public void FilterContextWithoutOperationContextReturnsDefaults()
        {
            NamespaceTable namespaces = new();
            ITypeTable typeTree = new TypeTable(namespaces);
            var preferredLocales = new ArrayOf<string>(s_preferredLocales);

            var context = new FilterContext(
                namespaces,
                typeTree,
                preferredLocales,
                NUnitTelemetryContext.Create());

            Assert.That(context.NamespaceUris, Is.SameAs(namespaces));
            Assert.That(context.TypeTree, Is.SameAs(typeTree));
            Assert.That(context.PreferredLocales, Is.EqualTo(preferredLocales));
            Assert.That(context.DiagnosticsMask, Is.EqualTo(DiagnosticsMasks.SymbolicId));
            Assert.That(context.OperationDeadline, Is.EqualTo(DateTime.MaxValue));
            Assert.That(context.OperationStatus, Is.EqualTo(StatusCodes.Good));
            Assert.That(context.SessionId, Is.Null);
            Assert.That(context.UserIdentity, Is.Null);
            Assert.That(context.StringTable, Is.Null);
            Assert.That(context.AuditEntryId, Is.Null);
            Assert.That(context.Telemetry, Is.Not.Null);
        }

        [Test]
        public void FilterContextValidatesRequiredTables()
        {
            NamespaceTable namespaces = new();
            ITypeTable typeTree = new TypeTable(namespaces);

            Assert.That(
                () => _ = new FilterContext(null, typeTree, NUnitTelemetryContext.Create()),
                Throws.ArgumentNullException);
            Assert.That(
                () => _ = new FilterContext(namespaces, null, NUnitTelemetryContext.Create()),
                Throws.ArgumentNullException);
        }

        private static readonly string[] s_preferredLocales = ["en-US", "de-DE"];
    }
}
