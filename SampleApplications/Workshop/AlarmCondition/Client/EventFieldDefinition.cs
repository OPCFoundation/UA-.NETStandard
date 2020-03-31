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
using Opc.Ua;

namespace AlarmConditionClient
{
    /// <summary>
    /// Stores information about the event fields used in a subscription.
    /// </summary>
    public class EventFieldDefinition
    {
        /// <summary>
        /// A display name for the field.
        /// </summary>
        public string DisplayName;

        /// <summary>
        /// The instance declartion for the field in the server's address space.
        /// </summary>
        public ReferenceDescription DeclarationNode;

        /// <summary>
        /// The operand used in the select clause.
        /// </summary>
        public SimpleAttributeOperand Operand;

        /// <summary>
        /// Initializes a new instance of the <see cref="EventFieldDefinition"/> class.
        /// </summary>
        /// <param name="parentField">The parent field.</param>
        /// <param name="reference">The reference to the instance declaration node.</param>
        public EventFieldDefinition(EventFieldDefinition parentField, ReferenceDescription reference)
        {
            DeclarationNode = reference;

            Operand = new SimpleAttributeOperand();

            // setting the typedefinition id to null ignores the event type when evaluating the operand.
            Operand.TypeDefinitionId = null;

            // event filters only support the NodeId attribute for objects and the Value attribute for Variables.
            Operand.AttributeId = (reference.NodeClass == NodeClass.Variable) ? Attributes.Value : Attributes.NodeId;

            // prefix the browse path with the parent browse path.
            if (parentField != null)
            {
                Operand.BrowsePath = new QualifiedNameCollection(parentField.Operand.BrowsePath);
            }

            // add the child browse name.
            Operand.BrowsePath.Add(reference.BrowseName);

            // may select sub-sets of array values. Not used in this sample.
            Operand.IndexRange = null;

            // construct the display name.
            StringBuilder buffer = new StringBuilder();

            for (int ii = 0; ii < Operand.BrowsePath.Count; ii++)
            {
                if (buffer.Length > 0)
                {
                    buffer.Append('/');
                }

                buffer.Append(Operand.BrowsePath[ii].Name);
            }

            DisplayName = buffer.ToString();
        }
    }
}
