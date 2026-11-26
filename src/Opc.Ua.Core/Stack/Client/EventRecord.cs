// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Abstract anchor record for all source-generated event records
    /// emitted by the <c>EventRecordGenerator</c>. The generated
    /// <c>BaseEventTypeRecord</c> derives directly from this type so
    /// every model's record hierarchy shares the same root and can
    /// be passed around as <c>EventRecord</c> by consumers that do
    /// not care about the specific event subtype.
    /// </summary>
    /// <remarks>
    /// Lives in <c>Opc.Ua.Core</c> rather than <c>Opc.Ua.Client</c>
    /// because the standard UA NodeSet's generated records (which
    /// sit alongside the <c>*State</c> classes in
    /// <c>Opc.Ua.Core.Types</c>) need to derive from it. Vendor
    /// extensions in higher-level assemblies inherit through the
    /// generated record chain transparently.
    /// </remarks>
    public abstract record EventRecord;
}
