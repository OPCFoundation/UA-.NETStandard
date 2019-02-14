/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Data;
using System.Reflection;
using Opc.Ua;

namespace Quickstarts.HistoricalAccessServer
{
    /// <summary>
    /// Stores the metadata for a node representing a folder on a file system.
    /// </summary>
    public class ArchiveItem
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public ArchiveItem(string uniquePath, FileInfo file)
        {
            UniquePath = uniquePath;
            FileInfo = file;
            Name = string.Empty;

            if (FileInfo != null)
            {
                Name = FileInfo.Name;

                int index = Name.LastIndexOf('.');

                if (index > 0)
                {
                    Name = Name.Substring(0, index);
                }
            }
        }

        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public ArchiveItem(string uniquePath, Assembly assembly, string resourcePath)
        {
            UniquePath = uniquePath;
            ResourceInfo = new ResourceInfoType() { Assembly = assembly, ResourcePath = resourcePath };
            Name = string.Empty;

            Name = ResourceInfo.ResourcePath;

            int index = Name.LastIndexOf('.');

            if (index > 0)
            {
                Name = Name.Substring(0, index);
            }

            index = Name.LastIndexOf('.');

            if (index > 0)
            {
                Name = Name.Substring(index+1);
            }
        }

        /// <summary>
        /// Returns the parent folder.
        /// </summary>
        public ArchiveFolder GetParentFolder()
        {
            string parentPath = String.Empty;

            if (FileInfo == null)
            {
                return new ArchiveFolder(parentPath, null);
            }

            if (!FileInfo.Exists)
            {
                return null;
            }

            int index = UniquePath.LastIndexOf('/');

            if (index > 0)
            {
                parentPath = UniquePath.Substring(0, index);
            }

            return new ArchiveFolder(parentPath, FileInfo.Directory);
        }

        /// <summary>
        /// Returns a stream that can be used to read the archive.
        /// </summary>
        public StreamReader OpenArchive()
        {
            if (FileInfo != null)
            {
                return new StreamReader(FileInfo.FullName, Encoding.UTF8);
            }

            if (ResourceInfo.Assembly != null)
            {
                return new StreamReader(ResourceInfo.Assembly.GetManifestResourceStream(ResourceInfo.ResourcePath), Encoding.UTF8); 
            }

            return null;
        }

        /// <summary>
        /// A name for the item.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// The unique path to the item in the archive.
        /// </summary>
        public string UniquePath { get; private set; }

        /// <summary>
        /// The data type for the item.
        /// </summary>
        public BuiltInType DataType;

        /// <summary>
        /// The value rank for the item.
        /// </summary>
        public int ValueRank { get; set; }

        /// <summary>
        /// The type of simulated data.
        /// </summary>
        public int SimulationType { get; set; }

        /// <summary>
        /// The amplitude of the simulated data.
        /// </summary>
        public double Amplitude { get; set; }

        /// <summary>
        /// The period of the simulated data.
        /// </summary>
        public double Period { get; set; }
        
        /// <summary>
        /// Whether the simulation is running.
        /// </summary>
        public bool Archiving { get; set; }

        /// <summary>
        /// Whether the data requires stepped interpolation.
        /// </summary>
        public bool Stepped { get; set; }

        /// <summary>
        /// The sampling interval for the simulation.
        /// </summary>
        public double SamplingInterval { get; set; }

        /// <summary>
        /// The history for the item.
        /// </summary>
        public DataSet DataSet { get; set; }

        /// <summary>
        /// The last the dataset was loaded from its source.
        /// </summary>
        public DateTime LastLoadTime { get; set; }
                
        /// <summary>
        /// Whether the source is perisistent and needs to be reloaded.
        /// </summary>
        public bool Persistent { get; set; }

        /// <summary>
        /// The aggregate configuration for the item.
        /// </summary>
        public AggregateConfiguration AggregateConfiguration { get; set; }

        /// <summary>
        /// The physical file containing the item history.
        /// </summary>
        private FileInfo FileInfo { get; set; }

        /// <summary>
        /// An embeddded resource in containing the item history.
        /// </summary>
        private ResourceInfoType ResourceInfo { get; set; }

        /// <summary>
        /// Stores information about an embedded resource.
        /// </summary>
        private struct ResourceInfoType
        {
            public Assembly Assembly { get; set; }
            public string ResourcePath { get; set; }
        }
    }
    
    /// <summary>
    /// The types of simulations.
    /// </summary>
    public static class SimulationTypes
    {
        const int Random = 0;
    }
}
