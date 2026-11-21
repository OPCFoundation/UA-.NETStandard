// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Tests.Presentation;

[TestFixture]
public sealed class NodeActionRulesTests
{
    [TestCase(NodeClass.Object, true)]
    [TestCase(NodeClass.ObjectType, true)]
    [TestCase(NodeClass.View, true)]
    [TestCase(NodeClass.Variable, true)]
    [TestCase(NodeClass.VariableType, true)]
    [TestCase(NodeClass.Method, false)]
    [TestCase(NodeClass.DataType, false)]
    [TestCase(NodeClass.ReferenceType, false)]
    public void ContainerClassificationMatchesBrowsableKinds(NodeClass nodeClass, bool expected)
    {
        Assert.That(NodeActionRules.IsContainer(nodeClass), Is.EqualTo(expected));
    }

    [Test]
    public void ConnectedWritableVariableExposesTheFullActionSet()
    {
        ContextMenuVisibility visibility = NodeActionRules.Evaluate(
            connected: true, NodeClass.Variable,
            canAddSelected: true, canCall: false, canWrite: true, selectionHasEvents: false);

        Assert.That(visibility.CanAdd, Is.True);
        Assert.That(visibility.CanWrite, Is.True);
        Assert.That(visibility.CanReadHistory, Is.True);
        Assert.That(visibility.CanExportValue, Is.True);
        Assert.That(visibility.CanAddRecursive, Is.True);
        Assert.That(visibility.CanAddToBench, Is.True);
        Assert.That(visibility.CanPerf, Is.True);
        Assert.That(visibility.CanCall, Is.False);
        Assert.That(visibility.CanShowEvents, Is.False);
        Assert.That(visibility.CanInspectModel, Is.True);
    }

    [Test]
    public void DisconnectedSelectionDisablesEverySessionAction()
    {
        ContextMenuVisibility visibility = NodeActionRules.Evaluate(
            connected: false, NodeClass.Variable,
            canAddSelected: false, canCall: false, canWrite: false, selectionHasEvents: false);

        Assert.That(visibility.CanAddRecursive, Is.False);
        Assert.That(visibility.CanReadHistory, Is.False);
        Assert.That(visibility.CanShowEvents, Is.False);
        Assert.That(visibility.CanPerf, Is.False);
        Assert.That(visibility.CanAddToBench, Is.False);
        Assert.That(visibility.CanExportValue, Is.False);
        Assert.That(visibility.CanInspectModel, Is.False);
    }

    [Test]
    public void EventEmittingObjectEnablesEventsButNotHistoryOrExport()
    {
        ContextMenuVisibility visibility = NodeActionRules.Evaluate(
            connected: true, NodeClass.Object,
            canAddSelected: true, canCall: false, canWrite: false, selectionHasEvents: true);

        Assert.That(visibility.CanShowEvents, Is.True);
        Assert.That(visibility.CanAddRecursive, Is.True);
        Assert.That(visibility.CanReadHistory, Is.False);
        Assert.That(visibility.CanExportValue, Is.False);
        Assert.That(visibility.CanPerf, Is.False);
    }

    [Test]
    public void MethodSelectionEnablesCallAndPerformance()
    {
        ContextMenuVisibility visibility = NodeActionRules.Evaluate(
            connected: true, NodeClass.Method,
            canAddSelected: false, canCall: true, canWrite: false, selectionHasEvents: false);

        Assert.That(visibility.CanCall, Is.True);
        Assert.That(visibility.CanPerf, Is.True);
        Assert.That(visibility.CanAddRecursive, Is.False);
        Assert.That(visibility.CanReadHistory, Is.False);
        Assert.That(visibility.CanAddToBench, Is.False);
        Assert.That(visibility.CanInspectModel, Is.True);
    }

    [Test]
    public void DataTypeSelectionAllowsInspectionWithoutWriteOrCall()
    {
        ContextMenuVisibility visibility = NodeActionRules.Evaluate(
            connected: true, NodeClass.DataType,
            canAddSelected: false, canCall: false, canWrite: false, selectionHasEvents: false);

        Assert.That(visibility.CanInspectModel, Is.True);
        Assert.That(visibility.CanWrite, Is.False);
        Assert.That(visibility.CanCall, Is.False);
        Assert.That(visibility.CanShowEvents, Is.False);
    }
}
