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
            CanExportValue: connected && isVariable);
    }
}
