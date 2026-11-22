// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua;

namespace MemoryBuffer
{
    /// <summary>
    /// Stores the configuration the test node manager
    /// </summary>
    // Namespaces.MemoryBuffer is generated in the same pass and is unavailable to the [DataType] generator.
    [DataType(Namespace = "http://samples.org/UA/MemoryBuffer")]
    public partial class MemoryBufferConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public MemoryBufferConfiguration()
        {
        }

        /// <summary>
        /// The buffers exposed by the memory
        /// </summary>
        [DataTypeField(Order = 1, StructureHandling = StructureHandling.Inline)]
        public ArrayOf<MemoryBufferInstance> Buffers { get; set; }
    }

    /// <summary>
    /// Stores the configuration for a memory buffer instance.
    /// </summary>
    // Namespaces.MemoryBuffer is generated in the same pass and is unavailable to the [DataType] generator.
    [DataType(Namespace = "http://samples.org/UA/MemoryBuffer")]
    public partial class MemoryBufferInstance
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public MemoryBufferInstance()
        {
        }

        /// <summary>
        /// The browse name for the instance.
        /// </summary>
        [DataTypeField(Order = 1)]
        public string Name { get; set; } = null!;

        /// <summary>
        /// The number of tags in the buffer.
        /// </summary>
        [DataTypeField(Order = 2)]
        public int TagCount { get; set; }

        /// <summary>
        /// The data type of the tags in the buffer.
        /// </summary>
        [DataTypeField(Order = 3)]
        public string DataType { get; set; } = null!;
    }
}
