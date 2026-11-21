// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// Selects the standalone application namespace. Per-instance providers and optional features belong to GmsBuilder.
    /// </summary>
    public sealed class GmsServerOptions
    {
        /// <summary>
        /// Gets or sets the standalone instance namespace; shared hosting retains its existing owner's namespace.
        /// </summary>
        public string InstanceNamespaceUri { get; set; } = "urn:opcfoundation:gms:instances";

        /// <summary>
        /// Rejects invalid application namespace selection before registering a namespace owner.
        /// </summary>
        internal void Validate()
        {
            if (!Uri.TryCreate(InstanceNamespaceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("GMS instances require an absolute application-owned namespace URI.");
            }
            foreach (string namespaceUri in GmsModel.GetNamespaceUris())
            {
                if (string.Equals(InstanceNamespaceUri, namespaceUri, StringComparison.Ordinal))
                {
                    throw new ArgumentException("GMS instances cannot use a standard model namespace.");
                }
            }
        }
    }
}
