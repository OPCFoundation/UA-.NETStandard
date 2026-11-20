/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Vision
{
    /// <summary>
    /// Independently selectable released Machine Vision models.
    /// The existing Perception model is not part of this selection.
    /// </summary>
    [Flags]
    public enum MachineVisionParts
    {
        /// <summary>No released models.</summary>
        None = 0,

        /// <summary>OPC 40100-1 control, recipes, configurations, and results.</summary>
        Control = 1,

        /// <summary>OPC 40100-2 asset management and condition monitoring.</summary>
        Assets = 2,

        /// <summary>Both independently namespaced released models.</summary>
        All = Control | Assets
    }

    /// <summary>
    /// Identifies the immutable Machine Vision publications shipped by the Vision package.
    /// </summary>
    public static class MachineVisionModels
    {
        /// <summary>The exact OPC 40100-1 namespace, without a trailing slash.</summary>
        public const string ControlNamespaceUri = "http://opcfoundation.org/UA/MachineVision";

        /// <summary>The exact OPC 40100-2 namespace.</summary>
        public const string AssetNamespaceUri = "http://opcfoundation.org/UA/MachineVision/AMCM/";

        /// <summary>The OPC 40100-1 supplementary-files release commit.</summary>
        public const string ControlSourceCommit = "ba1b53eedd87da6ee9c79ef76e85f9f1d7bff0d0";

        /// <summary>The OPC 40100-2 supplementary-files release commit.</summary>
        public const string AssetSourceCommit = "5cbfb6506684ffba82610ea50a95927389f46b8e";

        /// <summary>The SHA-256 of the unmodified OPC 40100-1 NodeSet.</summary>
        public const string ControlNodeSetSha256 =
            "81B77250E5DE8D4FCCEDA7E05FD32BF199299CAAC32D15DF8F1FE25ED907C053";

        /// <summary>The SHA-256 of the unmodified OPC 40100-2 NodeSet.</summary>
        public const string AssetNodeSetSha256 =
            "C70ED87915AB9A9F72F471C1B852A16EF7596921C0937A93FE2C3F73A6C0EC1F";
    }
}
