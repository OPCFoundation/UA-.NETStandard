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
using System.Text;
using System.Xml;
using System.IO;
using System.Reflection;
using System.Xml.Schema;

namespace Opc.Ua.Schema.Xml
{
    /// <summary>
    /// Generates files used to describe data types.
    /// </summary>
    public class XmlSchemaValidator : SchemaValidator
    {       
        #region Constructors
		/// <summary>
		/// Intializes the object with default values.
		/// </summary>
		public XmlSchemaValidator()
		{
            SetResourcePaths(WellKnownDictionaries);
		}

		/// <summary>
		/// Intializes the object with a file table.
		/// </summary>
		public XmlSchemaValidator(Dictionary<string,string> fileTable) : base(fileTable)
		{
            SetResourcePaths(WellKnownDictionaries);
		}
        #endregion      
        
        #region Public Members
        /// <summary>
        /// The schema that was validated.
        /// </summary>
        public XmlDocument TargetSchema
        {
            get { return m_schema; }
        }
        
		/// <summary>
		/// Generates the code from the contents of the address space.
		/// </summary>
		public void Validate(string inputPath)
		{
            using (Stream istrm = File.OpenRead(inputPath))
            {
                Validate(istrm);
            }
        }

		/// <summary>
		/// Generates the code from the contents of the address space.
		/// </summary>
		public void Validate(Stream stream)
		{
            m_schema.Load(stream);
            
            foreach (XmlNode import in m_schema.ChildNodes)
            {                    
                if (import.NamespaceURI == Namespaces.OpcUa)
                {
                    StreamReader strm = new StreamReader(Assembly.Load(new AssemblyName("Opc.Ua.Core")).GetManifestResourceStream("Opc.Ua.Model.Opc.Ua.Types.xsd"));
                    m_schema.Load(strm);
                    continue;
                }

                string location = null;

                if (!KnownFiles.TryGetValue(import.NamespaceURI, out location))
                { 
                    location = import.NamespaceURI;
                }
                
                FileInfo fileInfo = new FileInfo(location);
                if (!fileInfo.Exists)
                {
                    StreamReader strm = new StreamReader(Assembly.Load(new AssemblyName("Opc.Ua.Core")).GetManifestResourceStream(location));
                    m_schema.Load(strm);
                }
                else
                {
                    Stream strm = File.OpenRead(location);
                    m_schema.Load(strm);
                }                
            }
		}       

        /// <summary>
        /// Returns the schema for the specified type (returns the entire schema if null).
        /// </summary>
        public override string GetSchema(string typeName)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            
            settings.Encoding    = Encoding.UTF8;
            settings.Indent      = true;
            settings.IndentChars = "    ";

            MemoryStream ostrm = new MemoryStream();
            XmlWriter writer = XmlWriter.Create(ostrm, settings);
            
            try
            {
                if (typeName == null || m_schema.ChildNodes.Count == 0)
                {
                    m_schema.WriteTo(writer);
                }
                else
                {
                    foreach (XmlNode current in m_schema.ChildNodes)
                    {       
                        XmlElement element = current as XmlElement;
                        if (element != null)
                        {
                            if (element.Name == typeName)
                            {                                
                                XmlDocument schema = new XmlDocument();
                                schema.AppendChild(element);
                                schema.WriteTo(writer);
                                break;
                            }
                        }
                    }
                }
            }
            finally
            {
                writer.Flush();
                writer.Dispose();
            }

            return new UTF8Encoding().GetString(ostrm.ToArray());
        } 
        #endregion
        
        #region Private Fields
        private readonly string[][] WellKnownDictionaries = new string[][]
        {
            new string[] {  Namespaces.OpcUaBuiltInTypes, "Opc.Ua.Types.Schemas.BuiltInTypes.xsd" }
        };

        private XmlDocument m_schema = new XmlDocument();
        #endregion
    }
}
