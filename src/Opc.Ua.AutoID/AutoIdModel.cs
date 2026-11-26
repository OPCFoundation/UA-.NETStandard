// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.AutoID
{
    /// <summary>
    /// Identifies the released OPC 30010 AutoID Devices model and its dependency closure,
    /// without claiming runtime conformance.
    /// </summary>
    public static class AutoIdModel
    {
        /// <summary>
        /// Returns the non-core model namespaces in dependency order, without duplicates:
        /// Device Integration followed by AutoID.
        /// </summary>
        public static ArrayOf<string> GetNamespaceUris()
        {
            return [Di.Namespaces.OpcUaDi, NamespaceUri];
        }

        /// <summary>The namespace of the released AutoID Devices model.</summary>
        public const string NamespaceUri = "http://opcfoundation.org/UA/AutoID/";

        /// <summary>The model version declared by the released NodeSet.</summary>
        public const string Version = "1.01";

        /// <summary>The immutable OPCFoundation/UA-Nodeset revision containing the publication.</summary>
        public const string SourceCommit = "9f6c7660b94af24d32c4248d4b13bf60adab0301";

        /// <summary>The SHA-256 of the published NodeSet, which uses LF line endings.</summary>
        public const string NodeSetSha256 =
            "A5DB37D8DEEFBAABC279BCB6D50C3976E25E48F501ECA1F5BF9600CDFAA7653F";
    }
}
