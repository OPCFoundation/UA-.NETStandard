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
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

namespace Quickstarts.DataTypes.Types
{
    #region EngineType Enumeration
    #if (!OPCUA_EXCLUDE_EngineType)
    /// <summary>
    /// 
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [DataContract(Namespace = Quickstarts.DataTypes.Types.Namespaces.DataTypes)]
    public enum EngineType
    {
        /// <remarks />
        [EnumMember(Value = "Petrol_1")]
        Petrol = 1,

        /// <remarks />
        [EnumMember(Value = "Diesel_2")]
        Diesel = 2,

        /// <remarks />
        [EnumMember(Value = "Electric_3")]
        Electric = 3,

        /// <remarks />
        [EnumMember(Value = "Hybrid_4")]
        Hybrid = 4,

        /// <remarks />
        [EnumMember(Value = "Manual_5")]
        Manual = 5,
    }

    #region EngineTypeCollection Class
    /// <summary>
    /// A collection of EngineType objects.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [CollectionDataContract(Name = "ListOfEngineType", Namespace = Quickstarts.DataTypes.Types.Namespaces.DataTypes, ItemName = "EngineType")]
    #if !NET_STANDARD
    public partial class EngineTypeCollection : List<EngineType>, ICloneable
    #else
    public partial class EngineTypeCollection : List<EngineType>
    #endif
    {
        #region Constructors
        /// <summary>
        /// Initializes the collection with default values.
        /// </summary>
        public EngineTypeCollection() {}

        /// <summary>
        /// Initializes the collection with an initial capacity.
        /// </summary>
        public EngineTypeCollection(int capacity) : base(capacity) {}

        /// <summary>
        /// Initializes the collection with another collection.
        /// </summary>
        public EngineTypeCollection(IEnumerable<EngineType> collection) : base(collection) {}
        #endregion

        #region Static Operators
        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        public static implicit operator EngineTypeCollection(EngineType[] values)
        {
            if (values != null)
            {
                return new EngineTypeCollection(values);
            }

            return new EngineTypeCollection();
        }

        /// <summary>
        /// Converts a collection to an array.
        /// </summary>
        public static explicit operator EngineType[](EngineTypeCollection values)
        {
            if (values != null)
            {
                return values.ToArray();
            }

            return null;
        }
        #endregion

        #if !NET_STANDARD
        #region ICloneable Methods
        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public object Clone()
        {
            return (EngineTypeCollection)this.MemberwiseClone();
        }
        #endregion
        #endif

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            EngineTypeCollection clone = new EngineTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((EngineType)Utils.Clone(this[ii]));
            }

            return clone;
        }
    }
    #endregion
    #endif
    #endregion

    #region VehicleType Class
    #if (!OPCUA_EXCLUDE_VehicleType)
    /// <summary>
    /// 
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
            m_engine = EngineType.Petrol;
        }
        #endregion

        #region Public Properties
        /// <remarks />
        [DataMember(Name = "Make", IsRequired = false, Order = 1)]
        public string Make
        {
            get { return m_make;  }
            set { m_make = value; }
        }

        /// <remarks />
        [DataMember(Name = "Model", IsRequired = false, Order = 2)]
        public string Model
        {
            get { return m_model;  }
            set { m_model = value; }
        }

        /// <remarks />
        [DataMember(Name = "Engine", IsRequired = false, Order = 3)]
        public EngineType Engine
        {
            get { return m_engine;  }
            set { m_engine = value; }
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
            encoder.WriteEnumerated("Engine", Engine);

            encoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Quickstarts.DataTypes.Types.Namespaces.DataTypes);

            Make = decoder.ReadString("Make");
            Model = decoder.ReadString("Model");
            Engine = (EngineType)decoder.ReadEnumerated("Engine", typeof(EngineType));

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
            if (!Utils.IsEqual(m_engine, value.m_engine)) return false;

            return true;
        }

        #if !NET_STANDARD
        /// <summary cref="ICloneable.Clone" />
        public virtual object Clone()
        {
            return (VehicleType)this.MemberwiseClone();
        }
        #endif

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            VehicleType clone = (VehicleType)base.MemberwiseClone();

            clone.m_make = (string)Utils.Clone(this.m_make);
            clone.m_model = (string)Utils.Clone(this.m_model);
            clone.m_engine = (EngineType)Utils.Clone(this.m_engine);

            return clone;
        }
        #endregion

        #region Private Fields
        private string m_make;
        private string m_model;
        private EngineType m_engine;
        #endregion
    }

    #region VehicleTypeCollection Class
    /// <summary>
    /// A collection of VehicleType objects.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [CollectionDataContract(Name = "ListOfVehicleType", Namespace = Quickstarts.DataTypes.Types.Namespaces.DataTypes, ItemName = "VehicleType")]
    #if !NET_STANDARD
    public partial class VehicleTypeCollection : List<VehicleType>, ICloneable
    #else
    public partial class VehicleTypeCollection : List<VehicleType>
    #endif
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

        #if !NET_STANDARD
        #region ICloneable Methods
        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public object Clone()
        {
            return (VehicleTypeCollection)this.MemberwiseClone();
        }
        #endregion
        #endif

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            VehicleTypeCollection clone = new VehicleTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((VehicleType)Utils.Clone(this[ii]));
            }

            return clone;
        }
    }
    #endregion
    #endif
    #endregion

    #region CarType Class
    #if (!OPCUA_EXCLUDE_CarType)
    /// <summary>
    /// 
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
        /// <remarks />
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

        #if !NET_STANDARD
        /// <summary cref="ICloneable.Clone" />
        public override object Clone()
        {
            return (CarType)this.MemberwiseClone();
        }
        #endif

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            CarType clone = (CarType)base.MemberwiseClone();

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
    #if !NET_STANDARD
    public partial class CarTypeCollection : List<CarType>, ICloneable
    #else
    public partial class CarTypeCollection : List<CarType>
    #endif
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

        #if !NET_STANDARD
        #region ICloneable Methods
        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public object Clone()
        {
            return (CarTypeCollection)this.MemberwiseClone();
        }
        #endregion
        #endif

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            CarTypeCollection clone = new CarTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((CarType)Utils.Clone(this[ii]));
            }

            return clone;
        }
    }
    #endregion
    #endif
    #endregion

    #region TruckType Class
    #if (!OPCUA_EXCLUDE_TruckType)
    /// <summary>
    /// 
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
        /// <remarks />
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

        #if !NET_STANDARD
        /// <summary cref="ICloneable.Clone" />
        public override object Clone()
        {
            return (TruckType)this.MemberwiseClone();
        }
        #endif

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            TruckType clone = (TruckType)base.MemberwiseClone();

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
    #if !NET_STANDARD
    public partial class TruckTypeCollection : List<TruckType>, ICloneable
    #else
    public partial class TruckTypeCollection : List<TruckType>
    #endif
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

        #if !NET_STANDARD
        #region ICloneable Methods
        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public object Clone()
        {
            return (TruckTypeCollection)this.MemberwiseClone();
        }
        #endregion
        #endif

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            TruckTypeCollection clone = new TruckTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((TruckType)Utils.Clone(this[ii]));
            }

            return clone;
        }
    }
    #endregion
    #endif
    #endregion
}