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
using System.Runtime.Serialization;
using Opc.Ua;

namespace Quickstarts.AlarmConditionServer
{
    /// <summary>
    /// Stores the configuration the Alarm Condition server.
    /// </summary>
    [DataContract(Namespace = Namespaces.AlarmCondition)]
    public class AlarmConditionServerConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public AlarmConditionServerConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing()]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_areas = new AreaConfigurationCollection();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets or sets the list of top level Areas exposed by the server.
        /// </summary>
        [DataMember(Order = 1)]
        public AreaConfigurationCollection Areas
        {
            get { return m_areas; }
            set { m_areas = value; }
        }
        #endregion

        #region Private Members
        private AreaConfigurationCollection m_areas;
        #endregion
    }

    /// <summary>
    /// Stores the configuration for a Area within the Alarm Condition server.
    /// </summary>
    [DataContract(Namespace = Namespaces.AlarmCondition)]
    public class AreaConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public AreaConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing()]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_name = null;
            m_subAreas = null;
            m_sourcePaths = null;
        }
        #endregion
        
        #region Public Properties
        /// <summary>
        /// The browse name for the instance.
        /// </summary>
        [DataMember(Order = 1)]
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        /// <summary>
        /// Gets or set the list of sub-areas.
        /// </summary>
        [DataMember(Order = 2)]
        public AreaConfigurationCollection SubAreas
        {
            get { return m_subAreas; }
            set { m_subAreas = value; }
        }

        /// <summary>
        /// Gets or set the list of sources.
        /// </summary>
        [DataMember(Order = 3)]
        public StringCollection SourcePaths
        {
            get { return m_sourcePaths; }
            set { m_sourcePaths = value; }
        }
        #endregion

        #region Private Members
        private string m_name;
        private AreaConfigurationCollection m_subAreas;
        private StringCollection m_sourcePaths;
        #endregion
    }
    
    #region AreaConfigurationCollection Class
    /// <summary>
    /// A collection of AreaConfiguration objects.
    /// </summary>
    [CollectionDataContract(Name = "ListOfAreaConfiguration", Namespace = Namespaces.AlarmCondition, ItemName = "AreaConfiguration")]
    public partial class AreaConfigurationCollection : List<AreaConfiguration>
    {
    }
    #endregion
}
