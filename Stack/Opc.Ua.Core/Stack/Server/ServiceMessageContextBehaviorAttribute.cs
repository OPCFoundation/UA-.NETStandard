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
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;

namespace Opc.Ua
{
    #region ServiceMessageContextBehaviorAttribute Class
    /// <summary>
    /// Uses to indicate that a service endpoint uses the UA stack.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ServiceMessageContextBehaviorAttribute : Attribute
    {
    }
    #endregion
    
    #region ServiceMessageContextMessageInspector Class
    /// <summary>
    /// Ensures the operation context is set up correctly.
    /// </summary>
    public class ServiceMessageContextMessageInspector : IClientMessageInspector, IEndpointBehavior
    {
        /// <summary>
        /// Initializes the object with the message context to use.
        /// </summary>
        /// <param name="messageContext">The message context.</param>
        public ServiceMessageContextMessageInspector(ServiceMessageContext messageContext)
        {
            m_messageContext = messageContext;
        }

        /// <summary>
        /// Initializes the object with the binding to use.
        /// </summary>
        /// <param name="binding">The binding.</param>
        public ServiceMessageContextMessageInspector(Binding binding)
        {
            Opc.Ua.Bindings.BaseBinding instance = binding as Opc.Ua.Bindings.BaseBinding;

            if (instance != null)
            {
                m_messageContext = instance.MessageContext;
            }
        }

        #region IClientMessageInspector Members
        /// <summary>
        /// Enables inspection or modification of a message after a reply message is received but prior to passing it back to the client application.
        /// </summary>
        /// <param name="reply">The message to be transformed into types and handed back to the client application.</param>
        /// <param name="correlationState">Correlation state data.</param>
        public void AfterReceiveReply(ref Message reply, object correlationState)
        {
        }

        /// <summary>
        /// Enables inspection or modification of a message before a request message is sent to a service.
        /// </summary>
        /// <param name="request">The message to be sent to the service.</param>
        /// <param name="channel">The WCF client object channel.</param>
        /// <returns>
        /// The object that is returned as the <paramref name="correlationState "/>argument of the <see cref="M:System.ServiceModel.Dispatcher.IClientMessageInspector.AfterReceiveReply(System.ServiceModel.Channels.Message@,System.Object)"/> method. This is null if no correlation state is used.The best practice is to make this a <see cref="T:System.Guid"/> to ensure that no two <paramref name="correlationState"/> objects are the same.
        /// </returns>
        public object BeforeSendRequest(ref Message request, IClientChannel channel)
        {
            OperationContext context = OperationContext.Current;

            if (context != null)
            {
                context.Extensions.Add(new MessageContextExtension(m_messageContext));
            }
            
            ServiceMessageContext.ThreadContext = m_messageContext;
            return request.Headers.MessageId;
        }
        #endregion

        private ServiceMessageContext m_messageContext;

        #region IEndpointBehavior Members
        /// <summary>
        /// Implement to pass data at runtime to bindings to support custom behavior.
        /// </summary>
        /// <param name="endpoint">The endpoint to modify.</param>
        /// <param name="bindingParameters">The objects that binding elements require to support the behavior.</param>
        public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters)
        {
        }

        /// <summary>
        /// Implements a modification or extension of the client across an endpoint.
        /// </summary>
        /// <param name="endpoint">The endpoint that is to be customized.</param>
        /// <param name="clientRuntime">The client runtime to be customized.</param>
        public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime)
        {
            clientRuntime.ClientMessageInspectors.Add(this);
        }

        /// <summary>
        /// Implements a modification or extension of the service across an endpoint.
        /// </summary>
        /// <param name="endpoint">The endpoint that exposes the contract.</param>
        /// <param name="endpointDispatcher">The endpoint dispatcher to be modified or extended.</param>
        public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher)
        {
        }

        /// <summary>
        /// Implement to confirm that the endpoint meets some intended criteria.
        /// </summary>
        /// <param name="endpoint">The endpoint to validate.</param>
        public void Validate(ServiceEndpoint endpoint)
        {
        }
        #endregion
    }
    #endregion
}
