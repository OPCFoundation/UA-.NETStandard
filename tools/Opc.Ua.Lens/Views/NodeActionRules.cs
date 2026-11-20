/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua;

namespace UaLens.Views;

/// <summary>
/// Pure availability rules for the selected node's actions and context menu.
/// Isolated from Avalonia and the view-model so the "why is this unavailable"
/// behaviour is deterministic and unit-testable.
/// </summary>
internal static class NodeActionRules
{
    /// <summary>
    /// Nodes we can browse into recursively or scale-test with the bench.
    /// </summary>
    public static bool IsContainer(NodeClass nodeClass)
        => nodeClass is NodeClass.Object or NodeClass.ObjectType or NodeClass.View
            or NodeClass.Variable or NodeClass.VariableType;

    /// <summary>
    /// Evaluates address-space context-menu availability from live selection flags.
    /// </summary>
    public static ContextMenuVisibility Evaluate(
        bool connected,
        NodeClass nodeClass,
        bool canAddSelected,
        bool canCall,
        bool canWrite,
        bool selectionHasEvents)
    {
        bool container = IsContainer(nodeClass);
        bool isVariable = nodeClass == NodeClass.Variable;
        return new ContextMenuVisibility(
            CanAdd: canAddSelected,
            CanAddRecursive: connected && container,
            CanCall: canCall,
            CanWrite: canWrite,
            CanReadHistory: connected && isVariable,
            CanShowEvents: connected && selectionHasEvents,
            CanPerf: connected && (canCall || canWrite),
            CanAddToBench: connected && container,
            CanExportValue: connected && isVariable,
            CanInspectModel: connected && nodeClass is NodeClass.Variable or NodeClass.DataType or NodeClass.Method);
    }
}
