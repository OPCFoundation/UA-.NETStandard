// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua;

namespace TestData
{
    /// <summary>
    /// Stores the configuration the test node manager
    /// </summary>
    // Namespaces.TestData is generated in the same pass and is unavailable to the [DataType] generator.
    [DataType(Namespace = "http://test.org/UA/Data/")]
    public partial class TestDataNodeManagerConfiguration
    {
        /// <summary>
        /// The path to the file that stores state of the node manager.
        /// </summary>
        [DataTypeField(Order = 1)]
        public string? SaveFilePath { get; set; }

        /// <summary>
        /// The maximum length for a monitored item sampling queue.
        /// </summary>
        [DataTypeField(Order = 2)]
        public uint MaxQueueSize { get; set; } = 100;

        /// <summary>
        /// The next unused value that can be assigned to new nodes.
        /// </summary>
        [DataTypeField(Order = 3)]
        public uint NextUnusedId { get; set; }
    }
}
