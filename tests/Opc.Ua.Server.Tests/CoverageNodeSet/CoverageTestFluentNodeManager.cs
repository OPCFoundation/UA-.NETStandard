/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua.Server.Fluent;

namespace Opc.Ua.CoverageTest
{
    /// <summary>
    /// The third coverage pipeline: a fully source-generated
    /// <see cref="FluentNodeManagerBase"/>. The <c>[NodeManager]</c> attribute
    /// opts this partial class in to source generation, so the generator emits
    /// the sibling partial that owns the predefined-node load (from the same
    /// <c>AddOpcUaCoverageTest</c> composer) plus the matching
    /// <c>CoverageTestFluentNodeManagerFactory</c>. Nothing else is required —
    /// the generated manager wires the fluent builder itself.
    /// </summary>
    /// <remarks>
    /// The class is declared in the generated model namespace
    /// (<c>Opc.Ua.CoverageTest</c>) so the generated <c>AddOpcUaCoverageTest</c>
    /// composer is in scope for the emitted <c>LoadPredefinedNodes</c>.
    /// </remarks>
    [NodeManager(NamespaceUri = "http://opcfoundation.org/UA/CoverageTest/")]
    public partial class CoverageTestFluentNodeManager
    {
    }
}
