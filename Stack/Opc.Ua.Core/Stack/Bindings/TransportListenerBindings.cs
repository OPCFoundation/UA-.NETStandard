/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// The binding for the transport listeners.
    /// </summary>
    public static class TransportListenerBindings 
    {
        static TransportListenerBindings()
        {
            TransportListeners = new Dictionary<string, Type>();
            var listeners = typeof(TransportListenerBindings).Assembly.GetExportedTypes().Where(type => IsTransportListenerType(type));
            foreach (var listenerType in listeners)
            {
                Add(listenerType);
            }
        }

        #region Public Properties
        public static Dictionary<string, Type> TransportListeners { get; private set; }

        public static ITransportListener GetTransportListener(string uriScheme)
        {
            Type listenerType;
            if (TransportListeners.TryGetValue(uriScheme, out listenerType))
            {
                return Activator.CreateInstance(listenerType) as ITransportListener;
            }
            return null;
        }

        /// <summary>
        /// Add a transport listener to the binding.
        /// </summary>
        public static void Add(Type listenerType)
        {
            if (IsTransportListenerType(listenerType))
            {
                var listener = Activator.CreateInstance(listenerType) as ITransportListener;
                TransportListeners.Add(listener.UriScheme, listenerType);
            }
        }

        /// <summary>
        /// Validate the type is a transport listener.
        /// </summary>
        private static bool IsTransportListenerType(System.Type systemType)
        {
            if (systemType == null)
            {
                return false;
            }

            var systemTypeInfo = systemType.GetTypeInfo();
            if (systemTypeInfo.IsAbstract ||
                !typeof(ITransportListener).GetTypeInfo().IsAssignableFrom(systemTypeInfo))
            {
                return false;
            }

            var listener = Activator.CreateInstance(systemType) as ITransportListener;
            if (listener == null)
            {
                return false;
            }

            listener.Dispose();

            return true;
        }
        #endregion
    }
}
