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

namespace Quickstarts.DataTypes.Types
{
    #region VehicleType Class
    #if (!OPCUA_EXCLUDE_VehicleType)
    /// <summary>
    /// A description for the VehicleType DataType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [DataContract(Namespace = Quickstarts.DataTypes.Types.Namespaces.DataTypes)]
    public partial class VehicleType : IEncodeable
    {
    	#region Constructors
    	/// <summary>
    	/// The default constructor.
    	/// </summary>
    	public VehicleType()
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
    		m_make = null;
    		m_model = null;
    	}
    	#endregion

    	#region Public Properties
    	/// <summary>
    	/// A description for the Make field.
    	/// </summary>
    	[DataMember(Name = "Make", IsRequired = false, Order = 1)]
    	public string Make
    	{
    		get { return m_make;  }
    		set { m_make = value; }
    	}

    	/// <summary>
    	/// A description for the Model field.
    	/// </summary>
    	[DataMember(Name = "Model", IsRequired = false, Order = 2)]
    	public string Model
    	{
    		get { return m_model;  }
    		set { m_model = value; }
    	}
    	#endregion

        #region IEncodeable Members
        /// <summary cref="IEncodeable.TypeId" />
        public virtual ExpandedNodeId TypeId
        {
            get { return DataTypeIds.VehicleType; }
        }

        /// <summary cref="IEncodeable.BinaryEncodingId" />
        public virtual ExpandedNodeId BinaryEncodingId
        {
            get { return ObjectIds.VehicleType_Encoding_DefaultBinary; }
        }
        
        /// <summary cref="IEncodeable.XmlEncodingId" />
        public virtual ExpandedNodeId XmlEncodingId
        {
            get { return ObjectIds.VehicleType_Encoding_DefaultXml; }
        }
        
        /// <summary cref="IEncodeable.Encode(IEncoder)" />
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Quickstarts.DataTypes.Types.Namespaces.DataTypes);

            encoder.WriteString("Make", Make);
            encoder.WriteString("Model", Model);

            encoder.PopNamespace();
        }
        
        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Quickstarts.DataTypes.Types.Namespaces.DataTypes);

            Make = decoder.ReadString("Make");
            Model = decoder.ReadString("Model");

            decoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.IsEqual(IEncodeable)" />
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }
            
            VehicleType value = encodeable as VehicleType;
            
            if (value == null)
            {
                return false;
            }

            if (!Utils.IsEqual(m_make, value.m_make)) return false;
            if (!Utils.IsEqual(m_model, value.m_model)) return false;

            return true;
        }
        
        /// <summary cref="ICloneable.Clone" />
        public virtual object Clone()
        {
            VehicleType clone = (VehicleType)this.MemberwiseClone();

            clone.m_make = (string)Utils.Clone(this.m_make);
            clone.m_model = (string)Utils.Clone(this.m_model);

            return clone;
        }
        #endregion
        
    	#region Private Fields
    	private string m_make;
    	private string m_model;
    	#endregion
    }

    #region VehicleTypeCollection Class
    /// <summary>
    /// A collection of VehicleType objects.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [CollectionDataContract(Name = "ListOfVehicleType", Namespace = Quickstarts.DataTypes.Types.Namespaces.DataTypes, ItemName = "VehicleType")]
    public partial class VehicleTypeCollection : List<VehicleType>, ICloneable
    {
    	#region Constructors
    	/// <summary>
    	/// Initializes the collection with default values.
    	/// </summary>
    	public VehicleTypeCollection() {}
    	
    	/// <summary>
    	/// Initializes the collection with an initial capacity.
    	/// </summary>
    	public VehicleTypeCollection(int capacity) : base(capacity) {}
    	
    	/// <summary>
    	/// Initializes the collection with another collection.
    	/// </summary>
    	public VehicleTypeCollection(IEnumerable<VehicleType> collection) : base(collection) {}
    	#endregion
                    
        #region Static Operators
        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        public static implicit operator VehicleTypeCollection(VehicleType[] values)
        {
            if (values != null)
            {
                return new VehicleTypeCollection(values);
            }

            return new VehicleTypeCollection();
        }
        
        /// <summary>
        /// Converts a collection to an array.
        /// </summary>
        public static explicit operator VehicleType[](VehicleTypeCollection values)
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
            VehicleTypeCollection clone = new VehicleTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((VehicleType)Utils.Clone(this[ii]));
            }

            return clone;
        }
        #endregion
    }
    #endregion
    #endif
    #endregion

    #region CarType Class
    #if (!OPCUA_EXCLUDE_CarType)
    /// <summary>
    /// A description for the CarType DataType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [DataContract(Namespace = Quickstarts.DataTypes.Types.Namespaces.DataTypes)]
    public partial class CarType : VehicleType
    {
    	#region Constructors
    	/// <summary>
    	/// The default constructor.
    	/// </summary>
    	public CarType()
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
    		m_noOfPassengers = (uint)0;
    	}
    	#endregion

    	#region Public Properties
    	/// <summary>
    	/// A description for the NoOfPassengers field.
    	/// </summary>
    	[DataMember(Name = "NoOfPassengers", IsRequired = false, Order = 1)]
    	public uint NoOfPassengers
    	{
    		get { return m_noOfPassengers;  }
    		set { m_noOfPassengers = value; }
    	}
    	#endregion

        #region IEncodeable Members
        /// <summary cref="IEncodeable.TypeId" />
        public override ExpandedNodeId TypeId
        {
            get { return DataTypeIds.CarType; }
        }

        /// <summary cref="IEncodeable.BinaryEncodingId" />
        public override ExpandedNodeId BinaryEncodingId
        {
            get { return ObjectIds.CarType_Encoding_DefaultBinary; }
        }
        
        /// <summary cref="IEncodeable.XmlEncodingId" />
        public override ExpandedNodeId XmlEncodingId
        {
            get { return ObjectIds.CarType_Encoding_DefaultXml; }
        }
        
        /// <summary cref="IEncodeable.Encode(IEncoder)" />
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.PushNamespace(Quickstarts.DataTypes.Types.Namespaces.DataTypes);

            encoder.WriteUInt32("NoOfPassengers", NoOfPassengers);

            encoder.PopNamespace();
        }
        
        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);

            decoder.PushNamespace(Quickstarts.DataTypes.Types.Namespaces.DataTypes);

            NoOfPassengers = decoder.ReadUInt32("NoOfPassengers");

            decoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.IsEqual(IEncodeable)" />
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }
            
            CarType value = encodeable as CarType;
            
            if (value == null)
            {
                return false;
            }

            if (!base.IsEqual(encodeable)) return false;
            if (!Utils.IsEqual(m_noOfPassengers, value.m_noOfPassengers)) return false;

            return true;
        }
        
        /// <summary cref="ICloneable.Clone" />
        public override object Clone()
        {
            CarType clone = (CarType)base.Clone();

            clone.m_noOfPassengers = (uint)Utils.Clone(this.m_noOfPassengers);

            return clone;
        }
        #endregion
        
    	#region Private Fields
    	private uint m_noOfPassengers;
    	#endregion
    }

    #region CarTypeCollection Class
    /// <summary>
    /// A collection of CarType objects.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [CollectionDataContract(Name = "ListOfCarType", Namespace = Quickstarts.DataTypes.Types.Namespaces.DataTypes, ItemName = "CarType")]
    public partial class CarTypeCollection : List<CarType>, ICloneable
    {
    	#region Constructors
    	/// <summary>
    	/// Initializes the collection with default values.
    	/// </summary>
    	public CarTypeCollection() {}
    	
    	/// <summary>
    	/// Initializes the collection with an initial capacity.
    	/// </summary>
    	public CarTypeCollection(int capacity) : base(capacity) {}
    	
    	/// <summary>
    	/// Initializes the collection with another collection.
    	/// </summary>
    	public CarTypeCollection(IEnumerable<CarType> collection) : base(collection) {}
    	#endregion
                    
        #region Static Operators
        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        public static implicit operator CarTypeCollection(CarType[] values)
        {
            if (values != null)
            {
                return new CarTypeCollection(values);
            }

            return new CarTypeCollection();
        }
        
        /// <summary>
        /// Converts a collection to an array.
        /// </summary>
        public static explicit operator CarType[](CarTypeCollection values)
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
            CarTypeCollection clone = new CarTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((CarType)Utils.Clone(this[ii]));
            }

            return clone;
        }
        #endregion
    }
    #endregion
    #endif
    #endregion

    #region TruckType Class
    #if (!OPCUA_EXCLUDE_TruckType)
    /// <summary>
    /// A description for the TruckType DataType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [DataContract(Namespace = Quickstarts.DataTypes.Types.Namespaces.DataTypes)]
    public partial class TruckType : VehicleType
    {
    	#region Constructors
    	/// <summary>
    	/// The default constructor.
    	/// </summary>
    	public TruckType()
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
    		m_cargoCapacity = (uint)0;
    	}
    	#endregion

    	#region Public Properties
    	/// <summary>
    	/// A description for the CargoCapacity field.
    	/// </summary>
    	[DataMember(Name = "CargoCapacity", IsRequired = false, Order = 1)]
    	public uint CargoCapacity
    	{
    		get { return m_cargoCapacity;  }
    		set { m_cargoCapacity = value; }
    	}
    	#endregion

        #region IEncodeable Members
        /// <summary cref="IEncodeable.TypeId" />
        public override ExpandedNodeId TypeId
        {
            get { return DataTypeIds.TruckType; }
        }

        /// <summary cref="IEncodeable.BinaryEncodingId" />
        public override ExpandedNodeId BinaryEncodingId
        {
            get { return ObjectIds.TruckType_Encoding_DefaultBinary; }
        }
        
        /// <summary cref="IEncodeable.XmlEncodingId" />
        public override ExpandedNodeId XmlEncodingId
        {
            get { return ObjectIds.TruckType_Encoding_DefaultXml; }
        }
        
        /// <summary cref="IEncodeable.Encode(IEncoder)" />
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.PushNamespace(Quickstarts.DataTypes.Types.Namespaces.DataTypes);

            encoder.WriteUInt32("CargoCapacity", CargoCapacity);

            encoder.PopNamespace();
        }
        
        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);

            decoder.PushNamespace(Quickstarts.DataTypes.Types.Namespaces.DataTypes);

            CargoCapacity = decoder.ReadUInt32("CargoCapacity");

            decoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.IsEqual(IEncodeable)" />
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }
            
            TruckType value = encodeable as TruckType;
            
            if (value == null)
            {
                return false;
            }

            if (!base.IsEqual(encodeable)) return false;
            if (!Utils.IsEqual(m_cargoCapacity, value.m_cargoCapacity)) return false;

            return true;
        }
        
        /// <summary cref="ICloneable.Clone" />
        public override object Clone()
        {
            TruckType clone = (TruckType)base.Clone();

            clone.m_cargoCapacity = (uint)Utils.Clone(this.m_cargoCapacity);

            return clone;
        }
        #endregion
        
    	#region Private Fields
    	private uint m_cargoCapacity;
    	#endregion
    }

    #region TruckTypeCollection Class
    /// <summary>
    /// A collection of TruckType objects.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [CollectionDataContract(Name = "ListOfTruckType", Namespace = Quickstarts.DataTypes.Types.Namespaces.DataTypes, ItemName = "TruckType")]
    public partial class TruckTypeCollection : List<TruckType>, ICloneable
    {
    	#region Constructors
    	/// <summary>
    	/// Initializes the collection with default values.
    	/// </summary>
    	public TruckTypeCollection() {}
    	
    	/// <summary>
    	/// Initializes the collection with an initial capacity.
    	/// </summary>
    	public TruckTypeCollection(int capacity) : base(capacity) {}
    	
    	/// <summary>
    	/// Initializes the collection with another collection.
    	/// </summary>
    	public TruckTypeCollection(IEnumerable<TruckType> collection) : base(collection) {}
    	#endregion
                    
        #region Static Operators
        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        public static implicit operator TruckTypeCollection(TruckType[] values)
        {
            if (values != null)
            {
                return new TruckTypeCollection(values);
            }

            return new TruckTypeCollection();
        }
        
        /// <summary>
        /// Converts a collection to an array.
        /// </summary>
        public static explicit operator TruckType[](TruckTypeCollection values)
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
            TruckTypeCollection clone = new TruckTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((TruckType)Utils.Clone(this[ii]));
            }

            return clone;
        }
        #endregion
    }
    #endregion
    #endif
    #endregion
}
