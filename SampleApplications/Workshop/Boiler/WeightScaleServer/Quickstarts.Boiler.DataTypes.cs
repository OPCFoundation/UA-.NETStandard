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
using System.Reflection;
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

namespace Quickstarts.Boiler
{
    #region ControllerDataType Class
    #if (!OPCUA_EXCLUDE_ControllerDataType)
    /// <summary>
    /// A description for the ControllerDataType DataType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [DataContract(Namespace = Quickstarts.Boiler.Namespaces.Boiler)]
    public partial class ControllerDataType : IEncodeable
    {
    	#region Constructors
    	/// <summary>
    	/// The default constructor.
    	/// </summary>
    	public ControllerDataType()
    	{
    		Initialize();
    	}
        
    	/// <summary>
    	/// Called by the .NET framework during deserialization.
    	/// </summary>
        [OnDeserializing]
    	private void Initialize(StreamingContext context)
    	{
    		Initialize();
    	}

    	/// <summary>
    	/// Sets private members to default values.
    	/// </summary>
    	private void Initialize()
    	{
    		m_setpoint = (double)0;
    		m_controllerOut = (double)0;
    		m_processVariable = (double)0;
    	}
    	#endregion

    	#region Public Properties
    	/// <summary>
    	/// A description for the Setpoint field.
    	/// </summary>
    	[DataMember(Name = "Setpoint", IsRequired = false, Order = 1)]
    	public double Setpoint
    	{
    		get { return m_setpoint;  }
    		set { m_setpoint = value; }
    	}

    	/// <summary>
    	/// A description for the ControllerOut field.
    	/// </summary>
    	[DataMember(Name = "ControllerOut", IsRequired = false, Order = 2)]
    	public double ControllerOut
    	{
    		get { return m_controllerOut;  }
    		set { m_controllerOut = value; }
    	}

    	/// <summary>
    	/// A description for the ProcessVariable field.
    	/// </summary>
    	[DataMember(Name = "ProcessVariable", IsRequired = false, Order = 3)]
    	public double ProcessVariable
    	{
    		get { return m_processVariable;  }
    		set { m_processVariable = value; }
    	}
    	#endregion

        #region IEncodeable Members
        /// <summary cref="IEncodeable.TypeId" />
        public virtual ExpandedNodeId TypeId
        {
            get { return DataTypeIds.ControllerDataType; }
        }

        /// <summary cref="IEncodeable.BinaryEncodingId" />
        public virtual ExpandedNodeId BinaryEncodingId
        {
            get { return ObjectIds.ControllerDataType_Encoding_DefaultBinary; }
        }
        
        /// <summary cref="IEncodeable.XmlEncodingId" />
        public virtual ExpandedNodeId XmlEncodingId
        {
            get { return ObjectIds.ControllerDataType_Encoding_DefaultXml; }
        }
        
        /// <summary cref="IEncodeable.Encode(IEncoder)" />
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Quickstarts.Boiler.Namespaces.Boiler);

            encoder.WriteDouble("Setpoint", Setpoint);
            encoder.WriteDouble("ControllerOut", ControllerOut);
            encoder.WriteDouble("ProcessVariable", ProcessVariable);

            encoder.PopNamespace();
        }
        
        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Quickstarts.Boiler.Namespaces.Boiler);

            Setpoint = decoder.ReadDouble("Setpoint");
            ControllerOut = decoder.ReadDouble("ControllerOut");
            ProcessVariable = decoder.ReadDouble("ProcessVariable");

            decoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.IsEqual(IEncodeable)" />
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }
            
            ControllerDataType value = encodeable as ControllerDataType;
            
            if (value == null)
            {
                return false;
            }

            if (!Utils.IsEqual(m_setpoint, value.m_setpoint)) return false;
            if (!Utils.IsEqual(m_controllerOut, value.m_controllerOut)) return false;
            if (!Utils.IsEqual(m_processVariable, value.m_processVariable)) return false;

            return true;
        }
        
        /// <summary cref="ICloneable.Clone" />
        public virtual object Clone()
        {
            ControllerDataType clone = (ControllerDataType)this.MemberwiseClone();

            clone.m_setpoint = (double)Utils.Clone(this.m_setpoint);
            clone.m_controllerOut = (double)Utils.Clone(this.m_controllerOut);
            clone.m_processVariable = (double)Utils.Clone(this.m_processVariable);

            return clone;
        }
        #endregion
        
    	#region Private Fields
    	private double m_setpoint;
    	private double m_controllerOut;
    	private double m_processVariable;
    	#endregion
    }

    #region ControllerDataTypeCollection Class
    /// <summary>
    /// A collection of ControllerDataType objects.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [CollectionDataContract(Name = "ListOfControllerDataType", Namespace = Quickstarts.Boiler.Namespaces.Boiler, ItemName = "ControllerDataType")]
    public partial class ControllerDataTypeCollection : List<ControllerDataType>, ICloneable
    {
    	#region Constructors
    	/// <summary>
    	/// Initializes the collection with default values.
    	/// </summary>
    	public ControllerDataTypeCollection() {}
    	
    	/// <summary>
    	/// Initializes the collection with an initial capacity.
    	/// </summary>
    	public ControllerDataTypeCollection(int capacity) : base(capacity) {}
    	
    	/// <summary>
    	/// Initializes the collection with another collection.
    	/// </summary>
    	public ControllerDataTypeCollection(IEnumerable<ControllerDataType> collection) : base(collection) {}
    	#endregion
                    
        #region Static Operators
        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        public static implicit operator ControllerDataTypeCollection(ControllerDataType[] values)
        {
            if (values != null)
            {
                return new ControllerDataTypeCollection(values);
            }

            return new ControllerDataTypeCollection();
        }
        
        /// <summary>
        /// Converts a collection to an array.
        /// </summary>
        public static explicit operator ControllerDataType[](ControllerDataTypeCollection values)
        {
            if (values != null)
            {
                return values.ToArray();
            }

            return null;
        }
        #endregion

        #region ICloneable Methods
        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public object Clone()
        {
            ControllerDataTypeCollection clone = new ControllerDataTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((ControllerDataType)Utils.Clone(this[ii]));
            }

            return clone;
        }
        #endregion
    }
    #endregion
    #endif
    #endregion
}
