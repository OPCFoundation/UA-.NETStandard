// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.XRegistry.Client
{
    /// <summary>
    /// The outcome of registering a resource version in a group.
    /// </summary>
    /// <param name="ResourceNodeId">The NodeId of the resource version.</param>
    /// <param name="AssignedVersionId">The version id the server assigned.</param>
    /// <param name="Created">
    /// <c>true</c> when this call created the version, <c>false</c> when an existing version with
    /// the same resource and version id was reused. A strict registration only returns when it
    /// created the version, so this is always <c>true</c> there; it is the idempotent registration
    /// that can report <c>false</c>.
    /// </param>
    public readonly record struct ResourceRegistrationResult(
        NodeId ResourceNodeId,
        string AssignedVersionId,
        bool Created);
}
