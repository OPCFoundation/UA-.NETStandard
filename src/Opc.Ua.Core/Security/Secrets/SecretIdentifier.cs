// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// A lightweight handle that identifies a secret in an
    /// <see cref="ISecretStore"/>. The identifier itself is non-sensitive
    /// — it carries no plaintext — and is safe to log, persist, or pass
    /// across boundaries.
    /// </summary>
    /// <remarks>
    /// The triple <c>(Name, StoreType, StorePath)</c> is the routing key:
    /// <see cref="ISecretRegistry"/> dispatches to the
    /// <see cref="ISecretStore"/> whose
    /// <see cref="ISecretStore.StoreType"/> matches, and the store uses
    /// <see cref="Name"/> (and optionally <see cref="StorePath"/>) to
    /// locate the actual byte material.
    /// </remarks>
    /// <param name="Name">
    /// Logical name within the store (opaque to the registry — a key,
    /// secret-name, GUID, etc.).
    /// </param>
    /// <param name="StoreType">
    /// Store-type discriminator (e.g. <c>"InMemory"</c>, <c>"DPAPI"</c>,
    /// <c>"AzureKeyVault"</c>, <c>"KubernetesSecret"</c>).
    /// </param>
    /// <param name="StorePath">
    /// Optional store-specific path or sub-scope (e.g. a vault URL, a
    /// Kubernetes namespace, a DPAPI scope identifier).
    /// </param>
    public sealed record SecretIdentifier(
        string Name,
        string StoreType,
        string? StorePath = null);
}
