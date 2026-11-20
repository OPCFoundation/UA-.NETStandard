// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Client.Historian
{
    /// <summary>
    /// A modified historical value and the audit information describing
    /// how, when, and by whom it was changed.
    /// </summary>
    public readonly record struct ModifiedHistoryValue(
        DataValue Value,
        ModificationInfo Info);
}
