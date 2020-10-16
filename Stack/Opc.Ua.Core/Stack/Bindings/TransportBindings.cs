/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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
    /// The bindings for the transport listeners.
    /// </summary>
    public static class TransportBindings
    {
        static TransportBindings()
        {
            TransportListeners = new Dictionary<string, Type>();
            TransportChannels = new Dictionary<string, ITransportChannelBinding>();
            AddTransportListeners(typeof(TransportBindings).Assembly);
            AddTransportChannels(typeof(TransportBindings).Assembly);
        }

        #region Public Properties
        /// <summary>
        /// Dictionary of transport listeners (server).
        /// </summary>
        public static Dictionary<string, Type> TransportListeners { get; private set; }

        /// <summary>
        /// Dictionary of transport channels (client).
        /// </summary>
        public static Dictionary<string, ITransportChannelBinding> TransportChannels { get; private set; }

        /// <summary>
        /// Get a transport listener for a uri scheme.
        /// </summary>
        /// <param name="uriScheme">The uri scheme.</param>
        public static ITransportListener GetTransportListener(string uriScheme)
        {
            Type listenerType;
            if (!TransportListeners.TryGetValue(uriScheme, out listenerType))
            {
                if (TryAddWellKnownTransportBindings(uriScheme))
                {
                    if (!TransportListeners.TryGetValue(uriScheme, out listenerType))
                    {
                        return null;
                    }
                }
            }
            return Activator.CreateInstance(listenerType) as ITransportListener;
        }

        /// <summary>
        /// Get a transport channel for a uri scheme.
        /// </summary>
        /// <param name="uriScheme">The uri scheme.</param>
        public static ITransportChannel GetTransportChannel(string uriScheme)
        {
            ITransportChannelBinding channelBinding;
            if (!TransportChannels.TryGetValue(uriScheme, out channelBinding))
            {
                if (TryAddWellKnownTransportBindings(uriScheme))
                {
                    if (!TransportChannels.TryGetValue(uriScheme, out channelBinding))
                    {
                        return null;
                    }
                }
            }
            return channelBinding.Create();
        }

        /// <summary>
        /// Return if there is a transport listener for a uri scheme.
        /// </summary>
        /// <param name="uriScheme">The uri scheme.</param>
        public static bool HaveTransportListener(string uriScheme)
        {
            Type listenerType;
            if (TransportListeners.TryGetValue(uriScheme, out listenerType))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Return if there is a transport listener for a uri scheme.
        /// </summary>
        /// <param name="uriScheme">The uri scheme.</param>
        public static bool HaveTransportChannel(string uriScheme)
        {
            ITransportChannelBinding channelType;
            if (TransportChannels.TryGetValue(uriScheme, out channelType))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Add a transport to the listener bindings.
        /// </summary>
        public static void Add(Type bindingType)
        {
            if (IsTransportListenerType(bindingType))
            {
                var listener = Activator.CreateInstance(bindingType) as ITransportListener;
                TransportListeners[listener.UriScheme] = bindingType;
                listener.Dispose();
            }
        }


        /// <summary>
        /// Add a transport to the channel bindings.
        /// </summary>
        public static void Add(ITransportChannelBinding channelBinding)
        {
            TransportChannels[channelBinding.UriScheme] = channelBinding;
        }

        /// <summary>
        /// Add all ITransportListener in a assembly to the list of transport bindings.
        /// </summary>
        /// <param name="assembly">The assembly with the ITransportListener.</param>
        public static IEnumerable<Type> AddTransportListeners(Assembly assembly)
        {
            var listeners = assembly.GetExportedTypes().Where(type => IsTransportListenerType(type));
            foreach (var listenerType in listeners)
            {
                Add(listenerType);
            }
            return listeners;
        }

        /// <summary>
        /// Add all ITransportChannels in a assembly to the list of transport bindings.
        /// </summary>
        /// <param name="assembly">The assembly with the ITransportChannel.</param>
        public static IEnumerable<Type> AddTransportChannels(Assembly assembly)
        {
            var channels = assembly.GetExportedTypes().Where(type => IsTransportChannelType(type));
            foreach (var channelType in channels)
            {
                var channelBinding = Activator.CreateInstance(channelType) as ITransportChannelBinding;
                Add(channelBinding);
            }
            return channels;
        }
        #endregion

        #region Private Method
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

        /// <summary>
        /// Validate the type is a transport listener.
        /// </summary>
        private static bool IsTransportChannelType(System.Type systemType)
        {
            if (systemType == null)
            {
                return false;
            }

            var systemTypeInfo = systemType.GetTypeInfo();
            if (systemTypeInfo.IsAbstract ||
                !typeof(ITransportChannelBinding).GetTypeInfo().IsAssignableFrom(systemTypeInfo))
            {
                return false;
            }

            var channel = Activator.CreateInstance(systemType) as ITransportChannelBinding;
            if (channel == null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Try to load a binding from well known assemblies at runtime.
        /// </summary>
        /// <param name="scheme">The uri scheme of the binding.</param>
        private static bool TryAddWellKnownTransportBindings(string scheme)
        {
            Dictionary<string, string> wellKnownBindings = new Dictionary<string, string>() {
                { Utils.UriSchemeHttps, "Opc.Ua.Bindings.Https"}
            };

            string assemblyName;
            if (wellKnownBindings.TryGetValue(scheme, out assemblyName))
            {
                Assembly assembly = null;
                string fullName = typeof(TransportBindings).Assembly.FullName.Replace("Opc.Ua.Core", assemblyName);
                try
                {
                    assembly = Assembly.Load(fullName);
                }
                catch (Exception e)
                {
                    Utils.Trace(Utils.TraceMasks.Error, "Failed to load the assembly {0} for transport binding {1}.",
                        fullName, scheme
                        );
                }

                if (assembly != null)
                {
                    var listeners = AddTransportListeners(assembly);
                    return listeners.Count() > 0;
                }
            }
            return false;
        }
        #endregion
    }
}
