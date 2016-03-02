/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.ServiceModel.Channels;

namespace Opc.Ua
{
    /// <summary>
    /// A class that manages a mapping between a URL scheme and a binding.
    /// </summary>
    public class BindingFactory
    {
        #region Constructors
        /// <summary>
        /// Creates an empty factory.
        /// </summary>
        private BindingFactory()
        {
            m_bindings = new Dictionary<string, Type>();
            AddDefaultBindings(m_bindings);
            m_namespaceUris = ServiceMessageContext.GlobalContext.NamespaceUris;
            m_factory = ServiceMessageContext.GlobalContext.Factory;
        }

        /// <summary>
        /// Creates an empty factory.
        /// </summary>
        /// <param name="messageContext">The message context.</param>
        public BindingFactory(ServiceMessageContext messageContext)
        {
            m_bindings = new Dictionary<string, Type>();
            AddDefaultBindings(m_bindings);
            m_namespaceUris = ServiceMessageContext.GlobalContext.NamespaceUris;
            m_factory = ServiceMessageContext.GlobalContext.Factory;
            
            if (messageContext != null)
            {
                m_namespaceUris = messageContext.NamespaceUris;
                m_factory = messageContext.Factory;
            }
        }

        /// <summary>
        /// Creates an empty factory.
        /// </summary>
        /// <param name="namespaceUris">The namespace uris.</param>
        /// <param name="factory">The factory.</param>
        public BindingFactory(NamespaceTable namespaceUris, EncodeableFactory factory)
        {
            m_bindings = new Dictionary<string, Type>();
            AddDefaultBindings(m_bindings);
            m_namespaceUris = namespaceUris;
            m_factory = factory;
        }

        /// <summary>
        /// Copys an existing factory.
        /// </summary>
        /// <param name="factory">The factory.</param>
        public BindingFactory(BindingFactory factory)
        {
            m_bindings = new Dictionary<string, Type>();
            AddDefaultBindings(m_bindings);
            m_namespaceUris = ServiceMessageContext.GlobalContext.NamespaceUris;
            m_factory = ServiceMessageContext.GlobalContext.Factory;

            if (factory != null)
            {
                foreach (KeyValuePair<string,Type> entry in factory.m_bindings)
                {
                    m_bindings[entry.Key] = entry.Value;
                }

                m_namespaceUris = factory.m_namespaceUris;
                m_factory = factory.m_factory;
            }
        }
        #endregion
                        
        #region Public Interface
        /// <summary>
        /// Returns true if a binding exists for the specified schema.
        /// </summary>
        /// <param name="uriScheme">The URI scheme.</param>
        /// <returns>
        /// 	<c>true</c> if [contains] [the specified URI scheme]; otherwise, <c>false</c>.
        /// </returns>
        public virtual bool Contains(string uriScheme)
        { 
            return m_bindings.ContainsKey(uriScheme);
        }

        /// <summary>
        /// Adds a binding type to the factory.
        /// </summary>
        /// <param name="uriScheme">The URI scheme.</param>
        /// <param name="bindingType">Type of the binding.</param>
        public virtual void Add(string uriScheme, Type bindingType)
        { 
            m_bindings[uriScheme] = bindingType;
        }

        /// <summary>
        /// Removes a binding type from the factory.
        /// </summary>
        /// <param name="uriScheme">The URI scheme.</param>
        public virtual void Remove(string uriScheme)
        { 
            m_bindings.Remove(uriScheme);
        }

        /// <summary>
        /// Creates a discovery binding for the specified URI scheme.
        /// </summary>
        /// <param name="uriScheme">The URI scheme.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns></returns>
        public virtual Binding Create(
            string                uriScheme, 
            EndpointConfiguration configuration)
        {        
            if (uriScheme == null) throw new ArgumentNullException("uriScheme");

            Type bindingType = null;

            if (!m_bindings.TryGetValue(uriScheme, out bindingType))
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidArgument, "Could not find binding type for scheme: '{0}'.", uriScheme);
            }

            try
            {
                return (Binding)Activator.CreateInstance(bindingType, m_namespaceUris, m_factory, configuration, (EndpointDescription)null);
            }
            catch (Exception e)
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidArgument, e, "A dicovery binding for type '{0}' could not be created from the EndpointConfiguration.", bindingType.FullName);
            }
        }

        /// <summary>
        /// Creates a session binding for the specified URI scheme.
        /// </summary>
        /// <param name="uriScheme">The URI scheme.</param>
        /// <param name="descriptions">The descriptions.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns></returns>
        public virtual Binding Create(
            string                    uriScheme,
            List<EndpointDescription> descriptions, 
            EndpointConfiguration     configuration)
        {           
            if (uriScheme == null) throw new ArgumentNullException("uriScheme");

            Type bindingType = null;

            if (!m_bindings.TryGetValue(uriScheme, out bindingType))
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidArgument, "Could not find binding type for scheme: '{0}'.", uriScheme);
            }

            try
            {
                return (Binding)Activator.CreateInstance(bindingType, m_namespaceUris, m_factory, configuration, descriptions.ToArray());
            }
            catch (Exception e)
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidArgument, e, "An session binding for type '{0}' could not be created from the EndpointDescription and the EndpointConfiguration.", bindingType.FullName);
            }
        }

        /// <summary>
        /// Creates a session binding for the specified URI scheme.
        /// </summary>
        /// <param name="uriScheme">The URI scheme.</param>
        /// <param name="description">The description.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns></returns>
        public virtual Binding Create(
            string                uriScheme,
            EndpointDescription   description, 
            EndpointConfiguration configuration)
        {        
            if (uriScheme == null) throw new ArgumentNullException("uriScheme");

            Type bindingType = null;

            if (!m_bindings.TryGetValue(uriScheme, out bindingType))
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidArgument, "Could not find binding type for scheme: '{0}'.", uriScheme);
            }

            try
            {
                return (Binding)Activator.CreateInstance(bindingType, m_namespaceUris, m_factory, configuration, description);
            }
            catch (Exception e)
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidArgument, e, "An session binding for type '{0}' could not be created from the EndpointDescription and the EndpointConfiguration.", bindingType.FullName);
            }
        }

        /// <summary>
        /// Returns the default binding table.
        /// </summary>
        /// <value>The default.</value>
        public static BindingFactory Default
        {
            get { return s_Default; }
        }

        /// <summary>
        /// Creates a binding table from the bindings specified in the application configuration.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <returns></returns>
        [Obsolete("Use Create(ApplicationConfiguration, ServiceMessageContext) to avoid accidental sharing namespace tables.")]
        public static BindingFactory Create(ApplicationConfiguration configuration)
        {
            return Create(configuration, configuration.CreateMessageContext());
        }

        /// <summary>
        /// Creates a binding table from the bindings specified in the application configuration.
        /// </summary>
        public static BindingFactory Create(ApplicationConfiguration configuration, ServiceMessageContext context)
        {
            if (configuration == null || configuration.TransportConfigurations == null || configuration.TransportConfigurations.Count == 0)
            {
                return new BindingFactory(context.NamespaceUris, context.Factory);
            }

            BindingFactory table = new BindingFactory(context.NamespaceUris, context.Factory);

            foreach (TransportConfiguration entry in configuration.TransportConfigurations)
            {
                if (entry.TypeName == Utils.UaTcpBindingDefault)
                {
                    continue;
                }

                Type type = Type.GetType(entry.TypeName);

                if (type == null)
                {                
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "Could not find binding type '{0}'.", entry.TypeName);
                }

                table.Add(entry.UriScheme, type);
            }

            return table;
        }
        #endregion
        
        #region Private Members
        private static BindingFactory s_Default = new BindingFactory();

        private static void AddDefaultBindings(Dictionary<string, Type> table)
        {
            table.Add(Utils.UriSchemeHttp,    typeof(Opc.Ua.Bindings.UaSoapXmlBinding));
            table.Add(Utils.UriSchemeNetTcp,  typeof(Opc.Ua.Bindings.UaSoapXmlOverTcpBinding));
        }

        private Dictionary<string,Type> m_bindings;
        private NamespaceTable m_namespaceUris;
        private EncodeableFactory m_factory;
        #endregion
    }
}
