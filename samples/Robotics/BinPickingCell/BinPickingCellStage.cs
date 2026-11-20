/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Vision.BinPickingCell
{
    /// <summary>
    /// Materialises the embedded USD assets to a stable per-user directory so
    /// the offscreen renderer can open the stage by file path. The cell layer
    /// (<c>Cell.usda</c>) references <c>palletizer-arm.usda</c> and <c>gripper.usda</c>
    /// by relative path, so both sublayers must be extracted next to the root.
    /// </summary>
    /// <remarks>
    /// The output directory sits under
    /// <c>{LocalApplicationData}/OPCFoundation/UA-.NETStandard/BinPickingCell/&lt;hash&gt;/stage</c>
    /// where <c>hash</c> is derived from the sample assembly location. That is
    /// intentional: two side-by-side builds do not overwrite each other's
    /// assets, and the assets survive across restarts so the renderer plug-in
    /// cache can be reused. The class overwrites existing files only when
    /// their content differs, so subsequent runs are cheap.
    /// </remarks>
    internal sealed class BinPickingCellStage
    {
        public const string CellStageAsset = "Cell.usda";
        public const string ArmAsset = "palletizer-arm.usda";
        public const string PalletizerGripperAsset = "palletizer-gripper.usda";
        public const string GripperAsset = "gripper.usda";

        /// <summary>
        /// Gets the absolute path to the cell root layer on disk. Only valid
        /// after <see cref="Extract"/> has returned.
        /// </summary>
        public string CellStagePath { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the directory the assets were extracted to.
        /// </summary>
        public string StageDirectory { get; private set; } = string.Empty;

        /// <summary>
        /// Extracts the embedded USD sublayers to disk and returns the cell
        /// stage path.
        /// </summary>
        public string Extract()
        {
            Assembly assembly = typeof(BinPickingCellStage).Assembly;
            string root = ResolveRootDirectory(assembly);
            Directory.CreateDirectory(root);
            StageDirectory = root;
            WriteAssetIfChanged(assembly, CellStageAsset, Path.Combine(root, CellStageAsset));
            WriteAssetIfChanged(assembly, ArmAsset, Path.Combine(root, ArmAsset));
            WriteAssetIfChanged(
                assembly,
                PalletizerGripperAsset,
                Path.Combine(root, PalletizerGripperAsset));
            WriteAssetIfChanged(assembly, GripperAsset, Path.Combine(root, GripperAsset));
            CellStagePath = Path.Combine(root, CellStageAsset);
            return CellStagePath;
        }

        private static void WriteAssetIfChanged(Assembly assembly, string resourceName, string outputPath)
        {
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException(
                    $"Embedded resource '{resourceName}' is missing from the BinPickingCell assembly.");
            }
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            byte[] bytes = memory.ToArray();
            if (File.Exists(outputPath))
            {
                byte[] existing = File.ReadAllBytes(outputPath);
                if (BytesEqual(existing, bytes))
                {
                    return;
                }
            }
            File.WriteAllBytes(outputPath, bytes);
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }
            for (int ii = 0; ii < left.Length; ii++)
            {
                if (left[ii] != right[ii])
                {
                    return false;
                }
            }
            return true;
        }

        private static string ResolveRootDirectory(Assembly assembly)
        {
            string localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
            {
                localAppData = Path.GetTempPath();
            }
            string location = assembly.Location;
            string discriminator = string.IsNullOrEmpty(location)
                ? AppContext.BaseDirectory
                : location;
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(discriminator));
            var builder = new StringBuilder(16);
            for (int ii = 0; ii < 8; ii++)
            {
                builder.Append(hash[ii].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return Path.Combine(
                localAppData,
                "OPCFoundation",
                "UA-.NETStandard",
                "BinPickingCell",
                builder.ToString(),
                "stage");
        }
    }
}
