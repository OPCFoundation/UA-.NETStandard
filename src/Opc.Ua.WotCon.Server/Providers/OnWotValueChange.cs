// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Delegate invoked by an <see cref="IWotAssetProvider"/> when an
    /// observed WoT property changes value.
    /// </summary>
    /// <param name="tag">The property whose value changed.</param>
    /// <param name="value">The new value as an OPC UA <see cref="Variant"/>.</param>
    /// <param name="status">The status code reported by the binding.</param>
    /// <param name="timestamp">The source timestamp reported by the binding.</param>
    public delegate void OnWotValueChange(
        WotPropertyTag tag,
        Variant value,
        StatusCode status,
        DateTime timestamp);
}
