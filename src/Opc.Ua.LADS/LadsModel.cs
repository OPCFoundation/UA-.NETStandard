// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.LADS
{
    /// <summary>
    /// Identifies the released OPC 30500-1 LADS model and its dependency closure,
    /// without claiming runtime conformance.
    /// </summary>
    public static class LadsModel
    {
        /// <summary>
        /// Returns the non-core model namespaces in dependency order, without duplicates:
        /// Device Integration, Industrial Automation, Machinery, Asset Management Basics and LADS.
        /// </summary>
        public static ArrayOf<string> GetNamespaceUris()
        {
            return
            [
                Di.Namespaces.OpcUaDi,
                IA.Namespaces.IA,
                Machinery.Namespaces.Machinery,
                AMB.Namespaces.AMB,
                NamespaceUri
            ];
        }

        /// <summary>The namespace of the released LADS model.</summary>
        public const string NamespaceUri = "http://opcfoundation.org/UA/LADS/";

        /// <summary>The model version declared by the released NodeSet.</summary>
        public const string Version = "1.0.0";

        /// <summary>The immutable OPCFoundation/UA-Nodeset revision containing the publication.</summary>
        public const string SourceCommit = "9f529354fc05a261295b4ce3e937a47410f7cced";

        /// <summary>The SHA-256 of the published NodeSet, which uses LF line endings.</summary>
        public const string NodeSetSha256 =
            "75A45B39A19F37B2FE8ACDF3449CD5B6373A6C16068F5BFD8CA87EE0F2D253D6";
    }
}
