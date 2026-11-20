// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.SourceGeneration
{
    internal readonly record struct NodesetImportedSymbol(
        string SymbolicName,
        uint NumericId,
        NodeClass NodeClass);
}
