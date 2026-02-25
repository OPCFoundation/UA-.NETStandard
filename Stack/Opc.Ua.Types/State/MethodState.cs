/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// The base class for all method nodes.
    /// </summary>
    public class MethodState : BaseInstanceState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        public MethodState(NodeState parent)
            : base(NodeClass.Method, parent)
        {
            m_executable = true;
            m_userExecutable = true;
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new MethodState(parent);
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);

            Executable = true;
            UserExecutable = true;
        }

        /// <summary>
        /// Initializes the instance from another instance.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            if (source is MethodState method)
            {
                m_executable = method.m_executable;
                m_userExecutable = method.m_userExecutable;
            }

            base.Initialize(context, source);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = (MethodState)Activator.CreateInstance(GetType(), Parent);
            CopyTo(clone);
            return clone;
        }

        /// <inheritdoc/>
        public override bool DeepEquals(NodeState node)
        {
            if (node is not MethodState state)
            {
                return false;
            }
            return
                base.DeepEquals(state) &&
                state.OutputArguments == OutputArguments &&
                state.InputArguments == InputArguments &&
                state.MethodDeclarationId == MethodDeclarationId &&
                state.Executable == Executable &&
                state.UserExecutable == UserExecutable
                ;
        }

        /// <inheritdoc/>
        public override int DeepGetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.DeepGetHashCode());
            hash.Add(OutputArguments);
            hash.Add(OutputArguments);
            hash.Add(MethodDeclarationId);
            hash.Add(Executable);
            hash.Add(UserExecutable);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        protected override void CopyTo(NodeState target)
        {
            if (target is MethodState state)
            {
                state.OutputArguments = OutputArguments;
                state.InputArguments = InputArguments;
                state.MethodDeclarationId = MethodDeclarationId;
                state.Executable = Executable;
                state.UserExecutable = UserExecutable;
            }
            base.CopyTo(target);
        }

        /// <summary>
        /// The identifier for the declaration of the method in the type model.
        /// </summary>
        public NodeId MethodDeclarationId
        {
            get => TypeDefinitionId;
            set => TypeDefinitionId = value;
        }

        /// <summary>
        /// Whether the method can be called.
        /// </summary>
        public bool Executable
        {
            get => m_executable;
            set
            {
                if (m_executable != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_executable = value;
            }
        }

        /// <summary>
        /// Whether the method can be called by the current user.
        /// </summary>
        public bool UserExecutable
        {
            get => m_userExecutable;
            set
            {
                if (m_userExecutable != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_userExecutable = value;
            }
        }

        /// <summary>
        /// Raised when the Executable attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnReadExecutable;

        /// <summary>
        /// Raised when the Executable attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnWriteExecutable;

        /// <summary>
        /// Raised when the UserExecutable attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnReadUserExecutable;

        /// <summary>
        /// Raised when the UserExecutable attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnWriteUserExecutable;

        /// <summary>
        /// Raised when the method is called.
        /// </summary>
        public GenericMethodCalledEventHandler OnCallMethod;

        /// <summary>
        /// Raised when the method is called.
        /// </summary>
        public GenericMethodCalledEventHandler2 OnCallMethod2;

        /// <summary>
        /// Raised when the method is called.
        /// Takes a Task delegate to allow for asynchronous processing.
        /// Only works if the server / node managers supports async method calls.
        /// </summary>
        public GenericMethodCalledEventHandler2Async OnCallMethod2Async;

        /// <summary>
        /// Exports a copy of the node to a node table.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        protected override void Export(ISystemContext context, Node node)
        {
            base.Export(context, node);

            if (node is MethodNode methodNode)
            {
                methodNode.Executable = Executable;
                methodNode.UserExecutable = UserExecutable;
            }
        }

        /// <summary>
        /// Saves the attributes from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public override void Save(ISystemContext context, XmlEncoder encoder)
        {
            base.Save(context, encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            if (m_executable)
            {
                encoder.WriteBoolean("Executable", m_executable);
            }

            if (m_userExecutable)
            {
                encoder.WriteBoolean("UserExecutable", m_executable);
            }

            encoder.PopNamespace();
        }

        /// <summary>
        /// Updates the attributes from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder wrapping the stream to read.</param>
        public override void Update(ISystemContext context, XmlDecoder decoder)
        {
            base.Update(context, decoder);

            decoder.PushNamespace(Namespaces.OpcUaXsd);

            if (decoder.Peek("Executable"))
            {
                Executable = decoder.ReadBoolean("Executable");
            }

            if (decoder.Peek("UserExecutable"))
            {
                UserExecutable = decoder.ReadBoolean("UserExecutable");
            }

            decoder.PopNamespace();
        }

        /// <summary>
        /// Returns a mask which indicates which attributes have non-default value.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <returns>A mask the specifies the available attributes.</returns>
        public override AttributesToSave GetAttributesToSave(ISystemContext context)
        {
            AttributesToSave attributesToSave = base.GetAttributesToSave(context);

            if (m_executable)
            {
                attributesToSave |= AttributesToSave.Executable;
            }

            if (m_userExecutable)
            {
                attributesToSave |= AttributesToSave.UserExecutable;
            }

            return attributesToSave;
        }

        /// <summary>
        /// Saves object in an binary stream.
        /// </summary>
        /// <param name="context">The context user.</param>
        /// <param name="encoder">The encoder to write to.</param>
        /// <param name="attributesToSave">The masks indicating what attributes to write.</param>
        public override void Save(
            ISystemContext context,
            BinaryEncoder encoder,
            AttributesToSave attributesToSave)
        {
            base.Save(context, encoder, attributesToSave);

            if ((attributesToSave & AttributesToSave.Executable) != 0)
            {
                encoder.WriteBoolean(null, m_executable);
            }

            if ((attributesToSave & AttributesToSave.UserExecutable) != 0)
            {
                encoder.WriteBoolean(null, m_userExecutable);
            }
        }

        /// <summary>
        /// Updates the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="decoder">The decoder.</param>
        /// <param name="attributesToLoad">The attributes to load.</param>
        public override void Update(
            ISystemContext context,
            BinaryDecoder decoder,
            AttributesToSave attributesToLoad)
        {
            base.Update(context, decoder, attributesToLoad);

            if ((attributesToLoad & AttributesToSave.Executable) != 0)
            {
                m_executable = decoder.ReadBoolean(null);
            }

            if ((attributesToLoad & AttributesToSave.UserExecutable) != 0)
            {
                m_userExecutable = decoder.ReadBoolean(null);
            }
        }

        /// <summary>
        /// Reads the value for any non-value attribute.
        /// </summary>
        protected override ServiceResult ReadNonValueAttribute(
            ISystemContext context,
            uint attributeId,
            ref Variant value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.Executable:
                    bool executable = m_executable;

                    NodeAttributeEventHandler<bool> onReadExecutable = OnReadExecutable;

                    if (onReadExecutable != null)
                    {
                        result = onReadExecutable(context, this, ref executable);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = executable;
                    }

                    return result;
                case Attributes.UserExecutable:
                    bool userExecutable = m_userExecutable;

                    NodeAttributeEventHandler<bool> onReadUserExecutable = OnReadUserExecutable;

                    if (onReadUserExecutable != null)
                    {
                        result = onReadUserExecutable(context, this, ref userExecutable);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = userExecutable;
                    }

                    return result;
                default:
                    return base.ReadNonValueAttribute(context, attributeId, ref value);
            }
        }

        /// <summary>
        /// Write the value for any non-value attribute.
        /// </summary>
        protected override ServiceResult WriteNonValueAttribute(
            ISystemContext context,
            uint attributeId,
            Variant value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.Executable:
                    if (!value.TryGet(out bool executable))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.Executable) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<bool> onWriteExecutable = OnWriteExecutable;

                    if (onWriteExecutable != null)
                    {
                        result = onWriteExecutable(context, this, ref executable);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        Executable = executable;
                    }

                    return result;
                case Attributes.UserExecutable:
                    if (!value.TryGet(out bool userExecutable))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.UserExecutable) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<bool> onWriteUserExecutable = OnWriteUserExecutable;

                    if (onWriteUserExecutable != null)
                    {
                        result = onWriteUserExecutable(context, this, ref userExecutable);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        UserExecutable = userExecutable;
                    }

                    return result;
                default:
                    return base.WriteNonValueAttribute(context, attributeId, value);
            }
        }

        /// <summary>
        /// The input arguments for the method.
        /// </summary>
        public PropertyState<ArrayOf<Argument>> InputArguments
        {
            get => m_inputArguments;
            set
            {
                if (!ReferenceEquals(m_inputArguments, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_inputArguments = value;
            }
        }

        /// <summary>
        /// The output arguments for the method.
        /// </summary>
        public PropertyState<ArrayOf<Argument>> OutputArguments
        {
            get => m_outputArguments;
            set
            {
                if (!ReferenceEquals(m_outputArguments, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_outputArguments = value;
            }
        }

        /// <inheritdoc/>
        public override void GetChildren(ISystemContext context, IList<BaseInstanceState> children)
        {
            PropertyState<ArrayOf<Argument>> inputArguments = m_inputArguments;

            if (inputArguments != null)
            {
                children.Add(inputArguments);
            }

            PropertyState<ArrayOf<Argument>> outputArguments = m_outputArguments;

            if (outputArguments != null)
            {
                children.Add(outputArguments);
            }

            base.GetChildren(context, children);
        }

        /// <inheritdoc/>
        protected override BaseInstanceState FindChild(
            ISystemContext context,
            QualifiedName browseName,
            bool createOrReplace,
            BaseInstanceState replacement)
        {
            if (browseName.IsNull)
            {
                return null;
            }
            BaseInstanceState instance = null;
            switch (browseName.Name)
            {
                case BrowseNames.InputArguments:
                    instance = !createOrReplace ?
                        OutputArguments : CreateOrReplaceInputArguments(context, replacement);
                    break;
                case BrowseNames.OutputArguments:
                    instance = !createOrReplace ?
                        OutputArguments : CreateOrReplaceOutputArguments(context, replacement);
                    break;
            }
            return instance ?? base.FindChild(context, browseName, createOrReplace, replacement);
        }

        /// <summary>
        /// Create or replace output arguments
        /// </summary>
        public PropertyState<ArrayOf<Argument>> CreateOrReplaceOutputArguments(
            ISystemContext context,
            BaseInstanceState replacement)
        {
            if (OutputArguments == null)
            {
                if (replacement is not PropertyState<ArrayOf<Argument>> child)
                {
                    child = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(this);
                    if (replacement != null)
                    {
                        child.Create(context, replacement);
                    }
                }
                OutputArguments = child;
            }
            return OutputArguments;
        }

        /// <summary>
        /// Create or replace input arguments
        /// </summary>
        public PropertyState<ArrayOf<Argument>> CreateOrReplaceInputArguments(
            ISystemContext context,
            BaseInstanceState replacement)
        {
            if (InputArguments == null)
            {
                if (replacement is not PropertyState<ArrayOf<Argument>> child)
                {
                    child = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(this);
                    if (replacement != null)
                    {
                        child.Create(context, replacement);
                    }
                }
                InputArguments = child;
            }
            return InputArguments;
        }

        /// <summary>
        /// Invokes the methods and returns the output parameters.
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="objectId">The object being called.</param>
        /// <param name="inputArguments">The input arguments.</param>
        /// <param name="argumentErrors">Any errors for the input arguments.</param>
        /// <param name="outputArguments">The output arguments.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result of the method call.</returns>
        public virtual ValueTask<ServiceResult> CallAsync(
            ISystemContext context,
            NodeId objectId,
            VariantCollection inputArguments,
            IList<ServiceResult> argumentErrors,
            VariantCollection outputArguments,
            CancellationToken cancellationToken = default)
        {
            return CallInternalSyncOrAsync(
                context,
                objectId,
                inputArguments,
                argumentErrors,
                outputArguments,
                sync: false,
                cancellationToken);
        }

        /// <summary>
        /// Invokes the methods and returns the output parameters.
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="objectId">The object being called.</param>
        /// <param name="inputArguments">The input arguments.</param>
        /// <param name="argumentErrors">Any errors for the input arguments.</param>
        /// <param name="outputArguments">The output arguments.</param>
        /// <returns>The result of the method call.</returns>
        public virtual ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            VariantCollection inputArguments,
            IList<ServiceResult> argumentErrors,
            VariantCollection outputArguments)
        {
            // safe to access result directly as sync = true
#pragma warning disable CA2012 // Use ValueTasks correctly
            ValueTask<ServiceResult> syncResult = CallInternalSyncOrAsync(
                context,
                objectId,
                inputArguments,
                argumentErrors,
                outputArguments,
                sync: true);
#pragma warning restore CA2012 // Use ValueTasks correctly
            return syncResult.Result;
        }

        /// <summary>
        /// Invokes the methods and returns the output parameters.
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="objectId">The object being called.</param>
        /// <param name="inputArguments">The input arguments.</param>
        /// <param name="argumentErrors">Any errors for the input arguments.</param>
        /// <param name="outputArguments">The output arguments.</param>
        /// <param name="sync">If the method shall execute synchronously.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result of the method call.</returns>
        protected virtual async ValueTask<ServiceResult> CallInternalSyncOrAsync(
            ISystemContext context,
            NodeId objectId,
            VariantCollection inputArguments,
            IList<ServiceResult> argumentErrors,
            VariantCollection outputArguments,
            bool sync,
            CancellationToken cancellationToken = default)
        {
            // check if executable.
            Variant executable = default;
            ReadNonValueAttribute(context, Attributes.Executable, ref executable);

            if (executable.TryGet(out bool exec) && !exec)
            {
                return StatusCodes.BadNotExecutable;
            }

            // check if user executable.
            Variant userExecutable = default;
            ReadNonValueAttribute(context, Attributes.UserExecutable, ref userExecutable);

            if (userExecutable.TryGet(out bool userExec) && !userExec)
            {
                return StatusCodes.BadUserAccessDenied;
            }

            // validate input arguments.
            var inputs = new VariantCollection();

            // check for too few or too many arguments.
            int expectedCount = 0;

            PropertyState<ArrayOf<Argument>> expectedInputArguments = InputArguments;

            if (expectedInputArguments != null)
            {
                expectedCount = expectedInputArguments.Value.Count;
            }

            if (expectedCount > inputArguments.Count)
            {
                return StatusCodes.BadArgumentsMissing;
            }

            if (expectedCount < inputArguments.Count)
            {
                return StatusCodes.BadTooManyArguments;
            }

            // validate individual arguements.
            bool error = false;

            for (int ii = 0; ii < inputArguments.Count; ii++)
            {
                ServiceResult argumentError = ValidateInputArgument(
                    context,
                    inputArguments[ii],
                    ii);

                if (ServiceResult.IsBad(argumentError))
                {
                    error = true;
                }

                inputs.Add(inputArguments[ii]);
                argumentErrors.Add(argumentError);
            }

            // return good - caller must check argument errors.
            if (error)
            {
                return ServiceResult.Good;
            }

            // set output arguments to default values.
            var outputs = new VariantCollection();

            PropertyState<ArrayOf<Argument>> expectedOutputArguments = OutputArguments;

            if (expectedOutputArguments != null)
            {
                ArrayOf<Argument> arguments = expectedOutputArguments.Value;
                for (int ii = 0; ii < arguments.Count; ii++)
                {
                    outputs.Add(GetArgumentDefaultValue(context, arguments[ii]));
                }
            }

            // invoke method.
            ServiceResult result;
            try
            {
                if (sync)
                {
                    result = Call(context, objectId, inputs, outputs);
                }
                else
                {
                    result = await CallAsync(context, objectId, inputs, outputs, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                result = new ServiceResult(e);
            }

            // copy out arguments.
            if (ServiceResult.IsGoodOrUncertain(result))
            {
                for (int ii = 0; ii < outputs.Count; ii++)
                {
                    outputArguments.Add(outputs[ii]);
                }
            }

            return result;
        }

        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        protected virtual ServiceResult Call(
            ISystemContext context,
            VariantCollection inputArguments,
            VariantCollection outputArguments)
        {
            return Call(context, default, inputArguments, outputArguments);
        }

        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        protected virtual ValueTask<ServiceResult> CallAsync(
            ISystemContext context,
            VariantCollection inputArguments,
            VariantCollection outputArguments,
            CancellationToken cancellationToken = default)
        {
            return CallAsync(context, default, inputArguments, outputArguments, cancellationToken);
        }

        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        protected virtual ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            VariantCollection inputArguments,
            VariantCollection outputArguments)
        {
            GenericMethodCalledEventHandler2 onCallMethod2 = OnCallMethod2;

            if (onCallMethod2 != null)
            {
                return onCallMethod2(context, this, objectId, inputArguments, outputArguments);
            }

            GenericMethodCalledEventHandler onCallMethod = OnCallMethod;

            if (onCallMethod != null)
            {
                return onCallMethod(context, this, inputArguments, outputArguments);
            }

            if (Executable && UserExecutable)
            {
                return StatusCodes.BadNotImplemented;
            }

            return StatusCodes.BadUserAccessDenied;
        }

        /// <summary>
        /// Asynchonously invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns></returns>
        protected virtual async ValueTask<ServiceResult> CallAsync(
            ISystemContext context,
            NodeId objectId,
            VariantCollection inputArguments,
            VariantCollection outputArguments,
            CancellationToken cancellationToken = default)
        {
            GenericMethodCalledEventHandler2Async onCallMethod2Async = OnCallMethod2Async;

            if (OnCallMethod2Async != null)
            {
                return await onCallMethod2Async(
                    context,
                    this,
                    objectId,
                    inputArguments,
                    outputArguments,
                    cancellationToken).ConfigureAwait(false);
            }

            return Call(context, default, inputArguments, outputArguments);
        }

        /// <summary>
        /// Validates the input argument.
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="inputArgument">The input argument.</param>
        /// <param name="index">The index in the list of input argument.</param>
        /// <returns>Any error.</returns>
        protected ServiceResult ValidateInputArgument(
            ISystemContext context,
            Variant inputArgument,
            int index)
        {
            PropertyState<ArrayOf<Argument>> inputArguments = InputArguments;

            if (inputArguments == null)
            {
                return StatusCodes.BadInvalidArgument;
            }

            ArrayOf<Argument> arguments = inputArguments.Value;

            if (index < 0 || index >= arguments.Count)
            {
                return StatusCodes.BadInvalidArgument;
            }

            Argument expectedArgument = arguments[index];

            var typeInfo = TypeInfo.IsInstanceOfDataType(
                inputArgument,
                expectedArgument.DataType,
                expectedArgument.ValueRank,
                context.NamespaceUris,
                context.TypeTable);

            if (typeInfo.IsUnknown)
            {
                return StatusCodes.BadTypeMismatch;
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns the default value for the output argument.
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="outputArgument">The output argument description.</param>
        /// <returns>The default value.</returns>
        protected Variant GetArgumentDefaultValue(ISystemContext context, Argument outputArgument)
        {
            return TypeInfo.GetDefaultVariantValue(
                outputArgument.DataType,
                outputArgument.ValueRank,
                context.TypeTable);
        }

        private bool m_executable;
        private bool m_userExecutable;
        private PropertyState<ArrayOf<Argument>> m_inputArguments;
        private PropertyState<ArrayOf<Argument>> m_outputArguments;
    }

    /// <summary>
    /// Used to process a method call.
    /// </summary>
    public delegate ServiceResult GenericMethodCalledEventHandler(
        ISystemContext context,
        MethodState method,
        VariantCollection inputArguments,
        VariantCollection outputArguments);

    /// <summary>
    /// Used to process a method call.
    /// </summary>
    public delegate ServiceResult GenericMethodCalledEventHandler2(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        VariantCollection inputArguments,
        VariantCollection outputArguments);

    /// <summary>
    /// Used to process a method call.
    /// </summary>
    public delegate ValueTask<ServiceResult> GenericMethodCalledEventHandler2Async(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        VariantCollection inputArguments,
        VariantCollection outputArguments,
        CancellationToken cancellationToken = default);
}
