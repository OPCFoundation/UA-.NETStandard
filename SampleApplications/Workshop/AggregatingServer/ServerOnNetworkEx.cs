using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua;

namespace AggregatingServer.Core
{
    public class ServerOnNetworkEx : ServerOnNetwork
    {
        public bool isConnected { get; set; }
        public bool isAggregated { get; set; }
        //public uint ID { get; }

        public ServerOnNetworkEx()
        {

        }

        /// <summary>
        /// Converts a collection to an array.
        /// </summary>
        public ServerOnNetworkEx(ref ServerOnNetwork value, uint ID)
        {
            
            //this.BinaryEncodingId = value.BinaryEncodingId;
            this.DiscoveryUrl = value.DiscoveryUrl;
            this.isConnected = false;
            isAggregated = false;
            this.RecordId = value.RecordId;
            this.ServerCapabilities = value.ServerCapabilities;
            this.ServerName = value.ServerName;
            //this.ID = ID;
  


            /*
            Type sourceType = this.GetType();
            Type destinationType = value.GetType();

            foreach (PropertyInfo sourceProperty in sourceType.GetProperties())
            {
                PropertyInfo destinationProperty = destinationType.GetProperty(sourceProperty.Name);
                if (destinationProperty != null && destinationProperty.CanWrite)
                {
                    destinationProperty.SetValue(value, sourceProperty.GetValue(this, null), null);
                }


            }
            */
        }

    }


    public partial class ServerOnNetworkCollectionEx : List<ServerOnNetworkEx>

    {
        #region Constructors
        /// <summary>
        /// Initializes the collection with default values.
        /// </summary>
        public ServerOnNetworkCollectionEx() { }

        /// <summary>
        /// Initializes the collection with an initial capacity.
        /// </summary>
        public ServerOnNetworkCollectionEx(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection with another collection.
        /// </summary>
        public ServerOnNetworkCollectionEx(IEnumerable<ServerOnNetworkEx> collection) : base(collection) { }
        #endregion

        #region Static Operators
        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        public static implicit operator ServerOnNetworkCollectionEx(ServerOnNetworkEx[] values)
        {
            if (values != null)
            {
                return new ServerOnNetworkCollectionEx(values);
            }

            return new ServerOnNetworkCollectionEx();
        }

        /// <summary>
        /// Converts a collection to an array.
        /// </summary>
        public static explicit operator ServerOnNetworkEx[] (ServerOnNetworkCollectionEx values)
        {
            if (values != null)
            {
                return values.ToArray();
            }

            return null;
        }

        /// <summary>
        /// Converts a collection to an array.
        /// </summary>
        public static explicit operator ServerOnNetworkCollectionEx(ServerOnNetworkCollection serverOnNetworksCollection)
        {
            uint ID= 0;
            ServerOnNetworkCollectionEx myCollection = new ServerOnNetworkCollectionEx();
            foreach(ServerOnNetwork serverOnNetwork in serverOnNetworksCollection)
            {
                ServerOnNetwork tmp = serverOnNetwork;
                myCollection.Add(new  ServerOnNetworkEx(ref tmp, ++ID));
            }

            return myCollection;
        }
        #endregion

#if !NET_STANDARD
        #region ICloneable Methods
        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public object Clone()
        {
            return (ServerOnNetworkCollection)this.MemberwiseClone();
        }
        #endregion
#endif

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            ServerOnNetworkCollectionEx clone = new ServerOnNetworkCollectionEx(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((ServerOnNetworkEx)Utils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
