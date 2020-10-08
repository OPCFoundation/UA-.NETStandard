/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

using System.Windows.Forms;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A class that provide various common utility functions and shared resources.
    /// </summary>
    public partial class ClientUtils : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClientUtils"/> class.
        /// </summary>
        public ClientUtils()
        {
            InitializeComponent();
        }

        private const int Attribute = 0;
        private const int Property = 1;
        private const int Variable = 2;
        private const int Method = 3;
        private const int Object = 4;
        private const int OpenFolder = 5;
        private const int ClosedFolder = 6;
        private const int ObjectType = 7;
        private const int View = 8;
        private const int Reference = 9;
        private const int NumberValue = 10;
        private const int StringValue = 11;
        private const int ByteStringValue = 12;
        private const int StructureValue = 13;
        private const int ArrayValue = 14;
        private const int InputArgument = 15;
        private const int OutputArgument = 16;

        /// <summary>
        /// Returns an image index for the specified method argument.
        /// </summary>
        public static int GetImageIndex(bool isOutputArgument, object value)
        {
            if (isOutputArgument)
            {
                return OutputArgument;
            }

            return InputArgument;
        }

        /// <summary>
        /// Returns an image index for the specified attribute.
        /// </summary>
        #pragma warning disable 0162
        public static int GetImageIndex(uint attributeId, object value)
        {
            // Workaround to avoid exception when accessing ImageList
            // Original ImageStream has been removed a long time ago
            // now there is only a single image for all types.
            // TODO: add license free images back in
            return 0;

            if (attributeId == Attributes.Value)
            {
                TypeInfo typeInfo = TypeInfo.Construct(value);

                if (typeInfo.ValueRank >= 0)
                {
                    return ClientUtils.ArrayValue;
                }

                if (typeInfo.BuiltInType == BuiltInType.Variant)
                {
                    typeInfo = ((Variant)value).TypeInfo;

                    if (typeInfo == null)
                    {
                        typeInfo = TypeInfo.Construct(((Variant)value).Value);
                    }
                }

                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.Number:
                    case BuiltInType.SByte:
                    case BuiltInType.Byte:
                    case BuiltInType.Int16:
                    case BuiltInType.UInt16:
                    case BuiltInType.Int32:
                    case BuiltInType.UInt32:
                    case BuiltInType.Int64:
                    case BuiltInType.UInt64:
                    case BuiltInType.Float:
                    case BuiltInType.Double:
                    case BuiltInType.Enumeration:
                    case BuiltInType.UInteger:
                    case BuiltInType.Integer:
                    case BuiltInType.Boolean:
                    {
                        return ClientUtils.NumberValue;
                    }

                    case BuiltInType.ByteString:
                    {
                        return ClientUtils.ByteStringValue;
                    }

                    case BuiltInType.ExtensionObject:
                    case BuiltInType.DiagnosticInfo:
                    case BuiltInType.DataValue:
                    {
                        return ClientUtils.StructureValue;
                    }
                }

                return ClientUtils.StringValue;
            }

            return ClientUtils.Attribute;
        }

        /// <summary>
        /// Returns an image index for the specified attribute.
        /// </summary>
        public static int GetImageIndex(Session session, NodeClass nodeClass, ExpandedNodeId typeDefinitionId, bool selected)
        {
            if (nodeClass == NodeClass.Variable)
            {
                if (session.NodeCache.IsTypeOf(typeDefinitionId, Opc.Ua.VariableTypeIds.PropertyType))
                {
                    return ClientUtils.Property;
                }

                return ClientUtils.Variable;
            }

            if (nodeClass == NodeClass.Object)
            {
                if (session.NodeCache.IsTypeOf(typeDefinitionId, Opc.Ua.ObjectTypeIds.FolderType))
                {
                    if (selected)
                    {
                        return ClientUtils.OpenFolder;
                    }
                    else
                    {
                        return ClientUtils.ClosedFolder;
                    }
                }

                return ClientUtils.Object;
            }

            if (nodeClass == NodeClass.Method)
            {
                return ClientUtils.Method;
            }

            if (nodeClass == NodeClass.View)
            {
                return ClientUtils.View;
            }

            return ClientUtils.ObjectType;
        }
    }
}
