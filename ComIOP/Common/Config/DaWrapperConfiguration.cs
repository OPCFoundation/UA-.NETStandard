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
using System.ServiceModel;
using System.Runtime.Serialization;
using System.Collections.Generic;

using Opc.Ua.Server;

namespace Opc.Ua.Com.Client
{
	/// <summary>
	/// Stores the configuration for the DA wrapper.
	/// </summary>
    [KnownType(typeof(Da.DaWrapperConfiguration))]
    [KnownType(typeof(Ae.AeWrapperConfiguration))]
    [KnownType(typeof(Hda.HdaWrapperConfiguration))]
    [DataContract(Namespace=Opc.Ua.Namespaces.OpcUaSdk + "COM/Configuration.xsd")]
	public class WrapperConfiguration
	{	
		#region Constructors
		/// <summary>
		/// The default constructor.
		/// </summary>
		public WrapperConfiguration()
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
            m_namespaceUri         = null;
            m_browseName           = m_DefaultBrowseName;
            m_url                  = null;
            m_minMetadataLifetime  = m_DefaultMinMetadataLifetime;
            m_seperatorChars       = m_DefaultSeperatorChars;
            m_maxQueueSize         = m_DefaultMaxQueueSize;
            m_minReconnectWait     = m_DefaultMinReconnectWait;
            m_maxReconnectWait     = m_DefaultMaxReconnectWait;
            m_maxReconnectAttempts = m_DefaultMaxReconnectAttempts;
            m_defaultSamplingRates = new List<SamplingRateGroup>();
		}
		#endregion

        #region Public Properties
        /// <summary>
        /// The URI that qualifies all nodes within the server address space.
        /// </summary>
        [DataMember(Order = 1)]
        public string NamespaceUri
        {
            get { return m_namespaceUri; }
            set { m_namespaceUri = value; }
        }

        /// <summary>
        /// The browse name for the node that represents the root of the server address space.
        /// </summary>
        [DataMember(Order = 2)]
        public string BrowseName
        {
            get { return m_browseName; }
            set { m_browseName = value; }
        }          

		/// <summary>
		/// The URL that identifies the COM server to connect to.
		/// </summary>
		[DataMember(Order = 3)]
		public string Url
		{
			get { return m_url;  }
		    set { m_url = value; }
		}          

		/// <summary>
		/// The minimum sampling rate for metadata (browse names and properties).
		/// </summary>
		[DataMember(Order = 4)]
		public int MinMetadataLifetime
		{
			get { return m_minMetadataLifetime;  }
			set { m_minMetadataLifetime = value; }
		}       
     
		/// <summary>
		/// The characters that can be used to extract browse names from the end of item ids.
		/// </summary>
        /// <remarks>
        /// A null or empty string indicates that it is not possible to parse item ids for the server.
        /// </remarks>
		[DataMember(Order = 6)]
		public string SeperatorChars
		{
			get { return m_seperatorChars;  }
			set { m_seperatorChars = value; }
		}

        /// <summary>
        /// The maximum number of messages that can be saved in the publish queue.
        /// </summary>
        [DataMember(Order = 7)]
        public uint MaxQueueSize
        {
            get { return m_maxQueueSize;  }
            set { m_maxQueueSize = value; }
        }

        /// <summary>
        /// The maximum number of messages that can be saved in the publish queue.
        /// </summary>
        [DataMember(Order = 8)]
        public int MinReconnectWait
        {
            get { return m_minReconnectWait;  }
            set { m_minReconnectWait = value; }
        }

        /// <summary>
        /// The maximum number of messages that can be saved in the publish queue.
        /// </summary>
        [DataMember(Order = 9)]
        public int MaxReconnectWait
        {
            get { return m_maxReconnectWait;  }
            set { m_maxReconnectWait = value; }
        }

        /// <summary>
        /// The maximum number of messages that can be saved in the publish queue.
        /// </summary>
        [DataMember(Order = 10)]
        public int MaxReconnectAttempts
        {
            get { return m_maxReconnectAttempts;  }
            set { m_maxReconnectAttempts = value; }
        }

        /// <summary>
        /// The default set of sampling rates which the server uses.
        /// </summary>
        [DataMember(Order = 11)]
        public List<SamplingRateGroup> DefaultSamplingRates
        {
            get 
            { 
                return m_defaultSamplingRates;  
            }
            
            set 
            { 
                if (value != null)
                {
                    m_defaultSamplingRates = value;
                }
                else
                {
                    m_defaultSamplingRates.Clear();
                }
            }
        }
		#endregion
        
        #region Private Members
        private string m_namespaceUri;
        private string m_browseName;
        private string m_url;
        private int m_minMetadataLifetime;
        private string m_seperatorChars;
        private uint m_maxQueueSize;
        private int m_minReconnectWait;
        private int m_maxReconnectWait;
        private int m_maxReconnectAttempts;
        private List<SamplingRateGroup> m_defaultSamplingRates;
        
        private const string m_DefaultBrowseName = "DA";
        private const int m_DefaultMinMetadataLifetime = 10000;
        private const int m_DefaultMaxQueueSize = 100;
        private const int m_DefaultMinReconnectWait = 1000;
        private const int m_DefaultMaxReconnectWait = 30000;
        private const int m_DefaultMaxReconnectAttempts = 10;
        private const string m_DefaultSeperatorChars = null;
		#endregion
	}

    /// <summary>
    /// A collection of COM WrapperConfiguration objects.
    /// </summary>
    [CollectionDataContract(Name = "ListOfWrapperConfiguration", Namespace = Opc.Ua.Namespaces.OpcUaSdk + "COM/Configuration.xsd", ItemName = "WrapperConfiguration")]
    public partial class WrapperConfigurationCollection : List<WrapperConfiguration>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public WrapperConfigurationCollection() {}
        
        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        public WrapperConfigurationCollection(int capacity) : base(capacity) {}

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of <see cref="WrapperConfiguration"/> used to pre-populate the collection.</param>
        public WrapperConfigurationCollection(IEnumerable<WrapperConfiguration> collection) : base(collection) {}
    }
}

namespace Opc.Ua.Com.Client.Da
{
	/// <summary>
	/// Stores the configuration for the DA wrapper.
	/// </summary>
    [DataContract(Namespace=Opc.Ua.Namespaces.OpcUaSdk + "COM/Configuration.xsd")]
	public class DaWrapperConfiguration : WrapperConfiguration
	{	
		#region Constructors
		/// <summary>
		/// The default constructor.
		/// </summary>
		public DaWrapperConfiguration()
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
		}
		#endregion

        #region Public Properties
		#endregion
        
        #region Private Members
		#endregion
	}
}

namespace Opc.Ua.Com.Client.Ae
{
	/// <summary>
	/// Stores the configuration for the DA wrapper.
	/// </summary>
    [DataContract(Namespace=Opc.Ua.Namespaces.OpcUaSdk + "COM/Configuration.xsd")]
	public class AeWrapperConfiguration : WrapperConfiguration
	{	
		#region Constructors
		/// <summary>
		/// The default constructor.
		/// </summary>
		public AeWrapperConfiguration()
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
		}
		#endregion

        #region Public Properties
		#endregion
        
        #region Private Members
		#endregion
	}
}

namespace Opc.Ua.Com.Client.Hda
{
    /// <summary>
    /// Stores the configuration for the HDA wrapper.
    /// </summary>
    [DataContract(Namespace = Opc.Ua.Namespaces.OpcUaSdk + "COM/Configuration.xsd")]
    public class HdaWrapperConfiguration : WrapperConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public HdaWrapperConfiguration()
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
            //m_treatUncertainAsBad = false;
            //m_percentDataBad = ;
            //m_percentDataGood;
            //m_steppedSlopedExtrapolation;
            m_stepped = false;
            //m_definition = "";
            //m_maxTimeInterval;
            //m_minTimeInterval;
            //m_exceptionDeviation;
            //m_exceptionDeviationFormat;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// TreatUncertainAsBad.
        /// </summary>
        [DataMember(Order = 12)]

        public bool TreatUncertainAsBad
        {
            get { return m_treatUncertainAsBad; }
            set { m_treatUncertainAsBad = value; }
        }

        /// <summary>
        /// PercentDataBad.
        /// </summary>
        [DataMember(Order = 13)]
        public byte PercentDataBad
        {
            get { return m_percentDataBad; }
            set { m_percentDataBad = value; }
        }

        /// <summary>
        /// PercentDataGood.
        /// </summary>
        [DataMember(Order = 14)]
        public byte PercentDataGood
        {
            get { return m_percentDataGood; }
            set { m_percentDataGood = value; }
        }

        /// <summary>
        /// SteppedSlopedExtrapolation.
        /// </summary>
        [DataMember(Order = 15)]
        public bool SteppedSlopedExtrapolation
        {
            get { return m_steppedSlopedExtrapolation; }
            set { m_steppedSlopedExtrapolation = value; }
        }

        /// <summary>
        /// Stepped.
        /// </summary>
        [DataMember(Order = 16)]
        public bool Stepped
        {
            get { return m_stepped; }
            set { m_stepped = value; }
        }

        /// <summary>
        /// Definition.
        /// </summary>
        [DataMember(Order = 17)]
        public string Definition
        {
            get { return m_definition; }
            set { m_definition = value; }
        }

        /// <summary>
        /// MaxTimeInterval.
        /// </summary>
        [DataMember(Order = 18)]
        public double MaxTimeInterval
        {
            get { return m_maxTimeInterval; }
            set { m_maxTimeInterval = value; }
        }

        /// <summary>
        /// MinTimeInterval
        /// </summary>
        [DataMember(Order = 19)]
        public double MinTimeInterval
        {
            get { return m_minTimeInterval; }
            set { m_minTimeInterval = value; }
        }

        /// <summary>
        /// ExceptionDeviation
        /// </summary>
        [DataMember(Order = 20)]
        public double ExceptionDeviation
        {
            get { return m_exceptionDeviation; }
            set { m_exceptionDeviation = value; }
        }

        /// <summary>
        /// ExceptionDeviationFormat
        /// </summary>
        [DataMember(Order = 21)]
        public ExceptionDeviationFormat ExceptionDeviationFormat
        {
            get { return m_exceptionDeviationFormat; }
            set { m_exceptionDeviationFormat = value; }
        } 
        #endregion

        #region Private Members
        //Ravil 23 Sep 2008: Valuies to initialize HistoricalConfiguration members:
        private bool m_treatUncertainAsBad;
        private byte m_percentDataBad;
        private byte m_percentDataGood;
        private bool m_steppedSlopedExtrapolation;
        private bool m_stepped;
        private string m_definition;
        private double m_maxTimeInterval;
        private double m_minTimeInterval;
        private double m_exceptionDeviation;
        private ExceptionDeviationFormat m_exceptionDeviationFormat;
        #endregion
    }
}
