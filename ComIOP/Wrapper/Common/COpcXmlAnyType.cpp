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

#include "StdAfx.h"
#include "COpcXmlAnyType.h"
#include "COpcVariant.h"

using namespace OpcXml;

//==============================================================================
// Local Functions

#define TAG_HEX_BINARY _T("hexBinary")

struct TOpcXmlTypeMap
{
	LPCWSTR      szName;
	OpcXml::Type eType;
};

static const TOpcXmlTypeMap g_pTypes[] =
{
	{ L"byte",          OpcXml::XML_SBYTE    },
	{ L"unsignedByte",  OpcXml::XML_BYTE     },
	{ L"short",         OpcXml::XML_SHORT    },
	{ L"unsignedShort", OpcXml::XML_USHORT   },
	{ L"int",           OpcXml::XML_INT      },
	{ L"unsignedInt",   OpcXml::XML_UINT     },
	{ L"long",          OpcXml::XML_LONG     },
	{ L"unsignedLong",  OpcXml::XML_ULONG    },
	{ L"float",         OpcXml::XML_FLOAT    },
	{ L"double",        OpcXml::XML_DOUBLE   },
	{ L"decimal",       OpcXml::XML_DECIMAL  },
	{ L"boolean",       OpcXml::XML_BOOLEAN  },
	{ L"string",        OpcXml::XML_STRING   },
	{ L"dateTime",      OpcXml::XML_DATETIME },
	{ L"anyType",       OpcXml::XML_ANY_TYPE },
	{ NULL,             OpcXml::XML_EMPTY    }
};

static OpcXml::Type GetType(const COpcString& cType)
{
	for (UINT ii = 0; g_pTypes[ii].szName != NULL; ii++)
	{
		if (cType == g_pTypes[ii].szName)
		{
			return g_pTypes[ii].eType;
		}
	}

	return OpcXml::XML_EMPTY;
}

static LPCWSTR GetType(OpcXml::Type cType)
{
	for (UINT ii = 0; g_pTypes[ii].szName != NULL; ii++)
	{
		if (cType == g_pTypes[ii].eType)
		{
			return g_pTypes[ii].szName;
		}
	}

	return NULL;
}

//==============================================================================
// Schema

bool OpcXml::Schema::Read(COpcXmlElement& cElement)
{
	// save the fully qualified element name.
	SetName(cElement.GetQualifiedName());

	// fetch the element attributes.
	COpcXmlAttributeList cAttributes;
	
	if (cElement.GetAttributes(cAttributes) == 0)
	{
		return true;
	}

	// save the attributes in the schema.
	OpcXml::QName cTypeAttribute(OPCXML_TYPE_ATTRIBUTE, OPCXML_NS_SCHEMA_INSTANCE);

	for (UINT ii = 0; ii < cAttributes.GetSize(); ii++)
	{
		COpcXmlAttribute cAttribute = cAttributes[ii];

		// the type is specified with the 'xsi:type' attribute.

		if (cAttribute.GetQualifiedName() == cTypeAttribute)
		{
			// lookup any namespace prefix contained in the attribute value.
			COpcString cType = cAttribute.GetValue();
			COpcString cNamespace = cElement.ResolvePrefix(_T(""));

			int iIndex = cType.Find(_T(":"));

			if (iIndex != -1)
			{
				cNamespace = cElement.ResolvePrefix(cType.SubStr(0, iIndex));
				cType = cType.SubStr(iIndex+1);
			}	

			// save the fully qualified type name.
			OpcXml::QName cName(cType, cNamespace);
			SetType(cName);
		}

		// save other attributes (excluding namespace attributes).
		else
		{
			if (cAttribute.GetPrefix() != OPCXML_NAMESPACE_ATTRIBUTE)
			{				
				this->Set(cAttribute.GetQualifiedName(), cAttribute.GetValue());
			}
		}
	}

	return true;
}

// Write
bool OpcXml::Schema::Write(COpcXmlElement& cElement) const
{
	// set the type.
	cElement.SetType(GetType());

	// write additional attributes.
	OPC_POS pos = m_cAttributes.GetStartPosition();

	while (pos != NULL)
	{
		OpcXml::QName cName;
		COpcString    cValue;
		m_cAttributes.GetNextAssoc(pos, cName, cValue);

		// lookup attribute namespace prefix.
		if (!cName.GetNamespace().IsEmpty())
		{
			COpcString cFullName = cElement.ResolveNamespace(cName.GetNamespace());

			if (!cFullName.IsEmpty())
			{
				cFullName += _T(":");
			}

			cFullName += cName.GetName();

			// add attribute qualified with namespace prefix.
			cElement.SetAttribute(cFullName, cValue);
		}
		else
		{
			// add attribute.
			cElement.SetAttribute(cName.GetName(), cValue);
		}
	}
	
	return true;
}

//==============================================================================
// AnyType

// Init
template<> void OpcXml::Init(AnyType& cValue) 
{ 
    cValue.Clear(); 
}

// Clear
template<> void OpcXml::Clear(AnyType& cValue) 
{ 
    cValue.Clear(); 
}

// ReadSimpleArray
static bool ReadSimpleArray(COpcXmlElementList& cElements, OpcXml::AnyType& cValue)
{
	switch (cValue.eType)
	{
		case OpcXml::XML_SBYTE:    
		{ 	
			cValue.psbyteValue = OpcArrayAlloc(OpcXml::SByte, cValue.iLength);
			memset(cValue.psbyteValue, 0, sizeof(OpcXml::SByte)*cValue.iLength);

			for (int ii = 0; ii < cValue.iLength; ii++)
			{
				if (!OpcXml::ReadXml(cElements[ii], cValue.psbyteValue[ii])) break; 
			}

			return (ii >= cValue.iLength);
		}

		case OpcXml::XML_BYTE:    
		{ 	
			cValue.pbyteValue = OpcArrayAlloc(OpcXml::Byte, cValue.iLength);
			memset(cValue.pbyteValue, 0, sizeof(OpcXml::Byte)*cValue.iLength);

			for (int ii = 0; ii < cValue.iLength; ii++)
			{
				if (!OpcXml::ReadXml(cElements[ii], cValue.pbyteValue[ii])) break; 
			}

			return (ii >= cValue.iLength);
		}

		case OpcXml::XML_SHORT:    
		{ 	
			cValue.pshortValue = OpcArrayAlloc(OpcXml::Short, cValue.iLength);
			memset(cValue.pshortValue, 0, sizeof(OpcXml::Short)*cValue.iLength);

			for (int ii = 0; ii < cValue.iLength; ii++)
			{
				if (!OpcXml::ReadXml(cElements[ii], cValue.pshortValue[ii])) break; 
			}

			return (ii >= cValue.iLength);
		}

		case OpcXml::XML_USHORT:    
		{ 	
			cValue.pushortValue = OpcArrayAlloc(OpcXml::UShort, cValue.iLength);
			memset(cValue.pushortValue, 0, sizeof(OpcXml::UShort)*cValue.iLength);

			for (int ii = 0; ii < cValue.iLength; ii++)
			{
				if (!OpcXml::ReadXml(cElements[ii], cValue.pushortValue[ii])) break; 
			}

			return (ii >= cValue.iLength);
		}

		case OpcXml::XML_INT:    
		{ 	
			cValue.pintValue = OpcArrayAlloc(OpcXml::Int, cValue.iLength);
			memset(cValue.pintValue, 0, sizeof(OpcXml::Int)*cValue.iLength);

			for (int ii = 0; ii < cValue.iLength; ii++)
			{
				if (!OpcXml::ReadXml(cElements[ii], cValue.pintValue[ii])) break; 
			}

			return (ii >= cValue.iLength);
		}

		case OpcXml::XML_UINT:    
		{ 	
			cValue.puintValue = OpcArrayAlloc(OpcXml::UInt, cValue.iLength);
			memset(cValue.puintValue, 0, sizeof(OpcXml::UInt)*cValue.iLength);

			for (int ii = 0; ii < cValue.iLength; ii++)
			{
				if (!OpcXml::ReadXml(cElements[ii], cValue.puintValue[ii])) break;  
			}

			return (ii >= cValue.iLength);
		}

		case OpcXml::XML_LONG:    
		{ 	
			cValue.plongValue = OpcArrayAlloc(OpcXml::Long, cValue.iLength);
			memset(cValue.plongValue, 0, sizeof(OpcXml::Long)*cValue.iLength);

			for (int ii = 0; ii < cValue.iLength; ii++)
			{
				if (!OpcXml::ReadXml(cElements[ii], cValue.plongValue[ii])) break;
			}

			return (ii >= cValue.iLength);
		}

		case OpcXml::XML_ULONG:    
		{ 	
			cValue.pulongValue = OpcArrayAlloc(OpcXml::ULong, cValue.iLength);
			memset(cValue.pulongValue, 0, sizeof(OpcXml::ULong)*cValue.iLength);

			for (int ii = 0; ii < cValue.iLength; ii++)
			{
				if (!OpcXml::ReadXml(cElements[ii], cValue.pulongValue[ii])) break; 
			}

			return (ii >= cValue.iLength);
		}

		case OpcXml::XML_FLOAT:    
		{ 	
			cValue.pfloatValue = OpcArrayAlloc(OpcXml::Float, cValue.iLength);
			memset(cValue.pfloatValue, 0, sizeof(OpcXml::Float)*cValue.iLength);

			for (int ii = 0; ii < cValue.iLength; ii++)
			{
				if (!OpcXml::ReadXml(cElements[ii], cValue.pfloatValue[ii])) break; 
			}

			return (ii >= cValue.iLength);
		}

		case OpcXml::XML_DOUBLE:    
		{ 	
			cValue.pdoubleValue = OpcArrayAlloc(OpcXml::Double, cValue.iLength);
			memset(cValue.pdoubleValue, 0, sizeof(OpcXml::Double)*cValue.iLength);

			for (int ii = 0; ii < cValue.iLength; ii++)
			{
				if (!OpcXml::ReadXml(cElements[ii], cValue.pdoubleValue[ii])) break;
			}

			return (ii >= cValue.iLength);
		}

		case OpcXml::XML_DECIMAL:    
		{ 	
			cValue.pdecimalValue = OpcArrayAlloc(OpcXml::Decimal, cValue.iLength);
			memset(cValue.pdecimalValue, 0, sizeof(OpcXml::Decimal)*cValue.iLength);

			for (int ii = 0; ii < cValue.iLength; ii++)
			{
				if (!OpcXml::ReadXml(cElements[ii], cValue.pdecimalValue[ii])) break;
			}

			return (ii >= cValue.iLength);
		}

		case OpcXml::XML_BOOLEAN:    
		{ 	
			cValue.pboolValue = OpcArrayAlloc(OpcXml::Boolean, cValue.iLength);
			memset(cValue.pboolValue, 0, sizeof(OpcXml::Boolean)*cValue.iLength);

			for (int ii = 0; ii < cValue.iLength; ii++)
			{
				if (!OpcXml::ReadXml(cElements[ii], cValue.pboolValue[ii])) break;
			}

			return (ii >= cValue.iLength);
		}

		case OpcXml::XML_DATETIME:    
		{ 	
			cValue.pdateTimeValue = OpcArrayAlloc(OpcXml::DateTime, cValue.iLength);
			memset(cValue.pdateTimeValue, 0, sizeof(OpcXml::DateTime)*cValue.iLength);

			for (int ii = 0; ii < cValue.iLength; ii++)
			{
				if (!OpcXml::ReadXml(cElements[ii], cValue.pdateTimeValue[ii])) break;
			}

			return (ii >= cValue.iLength);
		}

		case OpcXml::XML_STRING:    
		{ 	
			cValue.pstringValue = OpcArrayAlloc(OpcXml::String, cValue.iLength);
			memset(cValue.pstringValue, 0, sizeof(OpcXml::String)*cValue.iLength);

			for (int ii = 0; ii < cValue.iLength; ii++)
			{
				if (!OpcXml::ReadXml(cElements[ii], cValue.pstringValue[ii])) break;
			}

			return (ii >= cValue.iLength);
		}
	}

	return false;
}

// ReadHexBinary
static bool ReadHexBinary(LPCWSTR wszBuffer, BYTE* pBuffer, UINT uLength)
{
	memset(pBuffer, 0, uLength);

	UINT uStrLen = wcslen(wszBuffer);

	for (UINT ii = 0; ii < uLength && ii*2 < uStrLen; ii++)
	{
		pBuffer[ii] = 0;

		for (UINT jj = 0; jj < 2 && ii*2+jj < uStrLen; jj++)
		{
			WCHAR wzBuffer = wszBuffer[ii*2+jj];

			if (!iswxdigit(wzBuffer))
			{
				return false;
			}

			if (iswlower(wzBuffer))
			{
				wzBuffer = towupper(wzBuffer);
			}
				
			pBuffer[ii] <<= 4;

			if (isdigit(wzBuffer))
			{ 
				pBuffer[ii] += (BYTE)wzBuffer - 0x30;
			}
			else
			{
				pBuffer[ii] += (BYTE)wzBuffer - 0x41 + 0x0A;
			}
		}
	}

	return true;
}

// WriteHexBinary
static bool WriteHexBinary(COpcString& cBuffer, BYTE* pBuffer, UINT uLength)
{
	LPWSTR szBuffer = OpcArrayAlloc(WCHAR, (uLength+1)*2);

	for (UINT ii = 0; ii < uLength; ii++)
	{
		swprintf(szBuffer+ii*2, L"%02X", pBuffer[ii]);
	}

	szBuffer[ii*2] = 0;

	cBuffer = szBuffer;
	OpcFree(szBuffer);

	return true;
}

// ReadXml<AnyType>
template<> bool OpcXml::ReadXml(IXMLDOMNode* ipNode, AnyType& cValue)
{ 
	cValue.Clear();

	COpcXmlElement cElement(ipNode);

	// can only read AnyType values from XML elements.
	if (cElement == NULL)
	{
		return false;
	}

	// read schema.
	if (!cValue.cSchema.Read(cElement))
	{
		cValue.Clear();
		return false;
	}

	COpcXmlElementList cChildren;
	cElement.GetChildren(cChildren);

	// read simple value.
	if (cChildren.GetSize() == 0)
	{
		// initialize type from schema.
		COpcString cTypeName = cValue.cSchema.GetType().GetName();
		
		cValue.eType   = ::GetType(cTypeName);
		cValue.iLength = -1;

		// get the element value.
		COpcString cText = cElement.GetValue();

		// check for empty element.
		if (cText.IsEmpty())
		{
			cValue.eType   = XML_EMPTY;
			cValue.iLength = -1;
			return true;
		}

		// handle hex binary as a special case.
		if (cTypeName == TAG_HEX_BINARY)
		{
			cValue.Alloc(XML_BYTE, cText.GetLength()/2);

			if (!ReadHexBinary((LPCWSTR)cText, cValue.pbyteValue, cValue.iLength))
			{
				return false;
			}

			return true;
		}
		
		// read simple value.
		switch (cValue.eType)
		{
			case XML_SBYTE:    { return Read(cText, cValue.sbyteValue);    }
			case XML_BYTE:     { return Read(cText, cValue.byteValue);     }
			case XML_SHORT:    { return Read(cText, cValue.shortValue);    }
			case XML_USHORT:   { return Read(cText, cValue.ushortValue);   } 
			case XML_INT:      { return Read(cText, cValue.intValue);      }
			case XML_UINT:     { return Read(cText, cValue.uintValue);     }
			case XML_LONG:     { return Read(cText, cValue.longValue);     }
			case XML_ULONG:    { return Read(cText, cValue.ulongValue);    }
			case XML_FLOAT:    { return Read(cText, cValue.floatValue);    }
			case XML_DOUBLE:   { return Read(cText, cValue.doubleValue);   }
			case XML_DECIMAL:  { return Read(cText, cValue.decimalValue);  }
			case XML_BOOLEAN:  { return Read(cText, cValue.boolValue);     }
			case XML_DATETIME: { return Read(cText, cValue.dateTimeValue); }

			default:
			{
				cValue.eType = XML_STRING;
			}
		}

		// store unrecognized types as strings.
		if (!Read(cText, cValue.stringValue))
		{
			cValue.Clear();
			return false;
		}

		return true;
	}
	
	// read complex value.
	cValue.iLength = cChildren.GetSize();

	// check for array of simple types.
	COpcString cType = cValue.cSchema.GetType().GetName();

	int iIndex = cType.Find(OPCXML_ARRAY_PREFIX);

	if (iIndex == 0)
	{
		cType = cType.SubStr(_tcslen(OPCXML_ARRAY_PREFIX)).ToLower(0);

		cValue.eType = ::GetType(cType);

		if (cValue.eType != XML_EMPTY && cValue.eType != XML_ANY_TYPE)
		{
			if (!ReadSimpleArray(cChildren, cValue))
			{
				cValue.Clear();
				return false;
			}

			return true;
		}
	}

	// read array of complex values.
	cValue.eType         = XML_ANY_TYPE; 
	cValue.panyTypeValue = new OpcXml::AnyType[cValue.iLength];

	for (int ii = 0; ii < cValue.iLength; ii++)
	{
		if (!ReadXml(cChildren[ii], cValue.panyTypeValue[ii]))
		{
			cValue.Clear();
			return false;
		}
	}

	return true;
}

// WriteSimpleArray
static bool WriteSimpleArray(COpcXmlElement& cElement, const OpcXml::AnyType& cValue)
{			
	COpcString cElementName = ::GetType(cValue.eType);

	for (int ii = 0; ii < cValue.iLength; ii++)
	{
		COpcString cText;

		switch (cValue.eType)
		{
			case OpcXml::XML_SBYTE:    
			{ 	
				if (!OpcXml::Write(cValue.psbyteValue[ii], cText)) false; 
				break;
			}

			case OpcXml::XML_BYTE:    
			{ 		
				if (!OpcXml::Write(cValue.pbyteValue[ii], cText)) return false; 
				break;
			}

			case OpcXml::XML_SHORT:    
			{ 	
				if (!OpcXml::Write(cValue.pshortValue[ii], cText)) return false; 
				break;
			}

			case OpcXml::XML_USHORT:    
			{ 	
				if (!OpcXml::Write(cValue.pushortValue[ii], cText)) return false; 
				break;
			}

			case OpcXml::XML_INT:    
			{ 	
				if (!OpcXml::Write(cValue.pintValue[ii], cText)) return false; 
				break;
			}

			case OpcXml::XML_UINT:    
			{ 	
				if (!OpcXml::Write(cValue.puintValue[ii], cText)) return false; 
				break;
			}

			case OpcXml::XML_LONG:    
			{ 	
				if (!OpcXml::Write(cValue.plongValue[ii], cText)) return false; 
				break;
			}

			case OpcXml::XML_ULONG:    
			{ 	
				if (!OpcXml::Write(cValue.pulongValue[ii], cText)) return false; 
				break;
			}

			case OpcXml::XML_FLOAT:    
			{ 	
				if (!OpcXml::Write(cValue.pfloatValue[ii], cText)) return false; 
				break;
			}

			case OpcXml::XML_DOUBLE:    
			{ 	
				if (!OpcXml::Write(cValue.pdoubleValue[ii], cText)) return false; 
				break;
			}

			case OpcXml::XML_DECIMAL:    
			{ 	
				if (!OpcXml::Write(cValue.pdecimalValue[ii], cText)) return false; 
				break;
			}

			case OpcXml::XML_BOOLEAN:    
			{ 	
				if (!OpcXml::Write(cValue.pboolValue[ii], cText)) return false; 
				break;
			}

			case OpcXml::XML_DATETIME:    
			{ 		
				if (!OpcXml::Write(cValue.pdateTimeValue[ii], cText)) return false; 
				break;
			}

			case OpcXml::XML_STRING:    
			{ 		
				if (!OpcXml::Write(cValue.pstringValue[ii], cText)) return false; 
				break;
			}
		}

		COpcXmlElement cChild = cElement.AppendChild(cElementName);

		if (cChild != NULL)
		{
			cChild.SetValue(cText);
		}
	}

	return true;
}

// WriteXml<AnyType>
template<> bool OpcXml::WriteXml(IXMLDOMNode* ipNode, const AnyType& cValue)
{
	COpcXmlElement cElement(ipNode);

	// can only write AnyType values to XML elements.
	if (cElement == NULL)
	{
		return false;
	}

	// write schema.
	if (!cValue.cSchema.Write(cElement))
	{
		return false;
	}

	if (cValue.iLength < 0)
	{
		// write simple value.
		COpcString cText;

		switch (cValue.eType)
		{
			case XML_SBYTE:    { if (!Write(cValue.sbyteValue, cText)) return false;     break; }
			case XML_BYTE:     { if (!Write(cValue.byteValue, cText)) return false;     break; }
			case XML_SHORT:    { if (!Write(cValue.shortValue, cText)) return false;    break; }
			case XML_USHORT:   { if (!Write(cValue.ushortValue, cText)) return false;   break; } 
			case XML_INT:      { if (!Write(cValue.intValue, cText)) return false;      break; }
			case XML_UINT:     { if (!Write(cValue.uintValue, cText)) return false;     break; }
			case XML_LONG:     { if (!Write(cValue.longValue, cText)) return false;     break; }
			case XML_ULONG:    { if (!Write(cValue.ulongValue, cText)) return false;    break; }
			case XML_FLOAT:    { if (!Write(cValue.floatValue, cText)) return false;    break; }
			case XML_DOUBLE:   { if (!Write(cValue.doubleValue, cText)) return false;   break; }
			case XML_DECIMAL:  { if (!Write(cValue.decimalValue, cText)) return false;  break; }
			case XML_BOOLEAN:  { if (!Write(cValue.boolValue, cText)) return false;     break; }
			case XML_DATETIME: { if (!Write(cValue.dateTimeValue, cText)) return false; break; }
			case XML_STRING:   { if (!Write(cValue.stringValue, cText)) return false;   break; }
		}

		cElement.SetValue(cText);
		return true;
	}

	// check for array of simple types.
	if (cValue.eType != XML_ANY_TYPE)
	{
		if (cValue.eType == XML_BYTE)
		{
			COpcString cText;

			if (!WriteHexBinary(cText, cValue.pbyteValue, cValue.iLength))
			{
				return false;
			}

			cElement.SetValue(cText);
		}
		else
		{
			if (!WriteSimpleArray(cElement, cValue))
			{
				return false;
			}
		}

		return true;
	}

	// check for array of complex types.
	COpcString cTypeName = cValue.cSchema.GetType().GetName();

	int iIndex = cTypeName.Find(OPCXML_ARRAY_PREFIX);

	if (iIndex == 0)
	{
		cTypeName = cTypeName.SubStr(_tcslen(OPCXML_ARRAY_PREFIX));

		for (int ii = 0; ii < cValue.iLength; ii++)
		{
			COpcXmlElement cChild = cElement.AppendChild(cTypeName);

			// add the data type to the value schema if not already there.
			QName cType = cValue.panyTypeValue[ii].cSchema.GetType();

			if (cType.GetName().IsEmpty())
			{
				COpcString cName = GetType(cValue.panyTypeValue[ii].eType);

				if (cValue.panyTypeValue[ii].iLength >= 0)
				{
					if (cValue.panyTypeValue[ii].eType == XML_BYTE)
					{
						cType.SetName(TAG_HEX_BINARY);
						cType.SetNamespace(OPCXML_NS_SCHEMA);
					}
					else
					{
						COpcString cFullName;
						
						cFullName += OPCXML_ARRAY_PREFIX;
						cFullName += cName.ToUpper(0);

						cType.SetName(cFullName);
						cType.SetNamespace(cElement.ResolvePrefix((LPCWSTR)NULL));
					}
				}
				else
				{
					cType.SetName(cName);
					cType.SetNamespace(OPCXML_NS_SCHEMA);
				}

				cValue.panyTypeValue[ii].cSchema.SetType(cType);
			}

			// write the value.
			if (!WriteXml(cChild, cValue.panyTypeValue[ii]))
			{
				return false;
			}
		}

		return true;
	}

	// write complex type.
	for (int ii = 0; ii < cValue.iLength; ii++)
	{
		OpcXml::QName cName = cValue.panyTypeValue[ii].cSchema.GetName();

		if (!WriteXml(cElement.AppendChild(cName), cValue.panyTypeValue[ii]))
		{
			return false;
		}
	}

	return true;
}

// Assignment
AnyType& AnyType::operator=(const AnyType& cValue)
{
	Clear();

	eType   = cValue.eType;
	cSchema = cValue.cSchema;
	iLength = cValue.iLength;

	if (iLength < 0)
	{
		switch (eType)
		{
			case XML_BOOLEAN:  { boolValue     = cValue.boolValue;              break; }
			case XML_SBYTE:    { psbyteValue   = cValue.psbyteValue;            break; }
			case XML_BYTE:     { byteValue     = cValue.byteValue;              break; }
			case XML_SHORT:    { shortValue    = cValue.shortValue;             break; }
			case XML_USHORT:   { ushortValue   = cValue.ushortValue;            break; }
			case XML_INT:      { intValue      = cValue.intValue;               break; }
			case XML_UINT:     { uintValue     = cValue.uintValue;              break; }
			case XML_LONG:     { longValue     = cValue.longValue;              break; }
			case XML_ULONG:    { ulongValue    = cValue.ulongValue;             break; }
			case XML_FLOAT:    { floatValue    = cValue.floatValue;             break; }
			case XML_DOUBLE:   { doubleValue   = cValue.doubleValue;            break; }
			case XML_DECIMAL:  { decimalValue  = cValue.decimalValue;           break; }
			case XML_DATETIME: { dateTimeValue = cValue.dateTimeValue;          break; }
			case XML_STRING:   { stringValue   = OpcStrDup(cValue.stringValue); break; }
		}

		return *this;
	}

	Alloc(eType, iLength);

	for (int ii = 0; ii < cValue.iLength; ii++)
	{
		switch (eType)
		{
			case XML_BOOLEAN:  { pboolValue[ii]     = cValue.pboolValue[ii];              break; }
			case XML_SBYTE:    { psbyteValue[ii]    = cValue.psbyteValue[ii];               break; }
			case XML_BYTE:     { pbyteValue[ii]     = cValue.pbyteValue[ii];              break; }
			case XML_SHORT:    { pshortValue[ii]    = cValue.pshortValue[ii];             break; }
			case XML_USHORT:   { pushortValue[ii]   = cValue.pushortValue[ii];            break; }
			case XML_INT:      { pintValue[ii]      = cValue.pintValue[ii];               break; }
			case XML_UINT:     { puintValue[ii]     = cValue.puintValue[ii];              break; }
			case XML_LONG:     { plongValue[ii]     = cValue.plongValue[ii];              break; }
			case XML_ULONG:    { pulongValue[ii]    = cValue.pulongValue[ii];             break; }
			case XML_FLOAT:    { pfloatValue[ii]    = cValue.pfloatValue[ii];             break; }
			case XML_DOUBLE:   { pdoubleValue[ii]   = cValue.pdoubleValue[ii];            break; }
			case XML_DECIMAL:  { pdecimalValue[ii]  = cValue.pdecimalValue[ii];           break; }
			case XML_DATETIME: { pdateTimeValue[ii] = cValue.pdateTimeValue[ii];          break; }
			case XML_STRING:   { pstringValue[ii]   = OpcStrDup(cValue.pstringValue[ii]); break; }
			case XML_ANY_TYPE: { panyTypeValue[ii]  = cValue.panyTypeValue[ii];           break; }
		}
	}
	
	return *this;
}

// Compare
int AnyType::Compare(const AnyType& cValue)
{
	if (eType != cValue.eType)     return (eType < cValue.eType)?-1:+1;
	if (iLength != cValue.iLength) return (iLength < cValue.iLength)?-1:+1;

	if (iLength < 0)
	{
		switch (eType)
		{
			case XML_BOOLEAN:  { if (boolValue != cValue.boolValue)         return (boolValue < cValue.boolValue)?-1:+1;         break; }
			case XML_SBYTE:    { if (sbyteValue != cValue.sbyteValue)       return (sbyteValue < cValue.sbyteValue)?-1:+1;       break; }
			case XML_BYTE:     { if (byteValue != cValue.byteValue)         return (byteValue < cValue.byteValue)?-1:+1;         break; }
			case XML_SHORT:    { if (shortValue != cValue.shortValue)       return (shortValue < cValue.shortValue)?-1:+1;       break; }
			case XML_USHORT:   { if (ushortValue != cValue.ushortValue)     return (ushortValue < cValue.ushortValue)?-1:+1;     break; }
			case XML_INT:      { if (intValue != cValue.intValue)           return (intValue < cValue.intValue)?-1:+1;           break; }
			case XML_UINT:     { if (uintValue != cValue.uintValue)         return (uintValue < cValue.uintValue)?-1:+1;         break; }
			case XML_LONG:     { if (longValue != cValue.longValue)         return (longValue < cValue.longValue)?-1:+1;         break; }
			case XML_ULONG:    { if (ulongValue != cValue.ulongValue)       return (ulongValue < cValue.ulongValue)?-1:+1;       break; }
			case XML_FLOAT:    { if (floatValue != cValue.floatValue)       return (floatValue < cValue.floatValue)?-1:+1;       break; }
			case XML_DOUBLE:   { if (doubleValue != cValue.doubleValue)     return (doubleValue < cValue.doubleValue)?-1:+1;     break; }
			case XML_DECIMAL:  { if (decimalValue != cValue.decimalValue)   return (decimalValue < cValue.decimalValue)?-1:+1;   break; }
			case XML_DATETIME: { if (dateTimeValue != cValue.dateTimeValue) return (dateTimeValue < cValue.dateTimeValue)?-1:+1; break; }
			
			case XML_STRING:   
			{ 
				int iResult = wcscmp(stringValue, cValue.stringValue);
				
				if (iResult != 0)
				{
					return iResult;
				}

				break;
			}
		}

		return 0;
	}

	for (int ii = 0; ii < iLength; ii++)
	{
		switch (eType)
		{
			case XML_BOOLEAN:  { if (pboolValue[ii] != cValue.pboolValue[ii])         return (pboolValue[ii] < cValue.pboolValue[ii])?-1:+1;         break; }
			case XML_SBYTE:    { if (psbyteValue[ii] != cValue.psbyteValue[ii])       return (psbyteValue[ii] < cValue.psbyteValue[ii])?-1:+1;       break; }
			case XML_BYTE:     { if (pbyteValue[ii] != cValue.pbyteValue[ii])         return (pbyteValue[ii] < cValue.pbyteValue[ii])?-1:+1;         break; }
			case XML_SHORT:    { if (pshortValue[ii] != cValue.pshortValue[ii])       return (pshortValue[ii] < cValue.pshortValue[ii])?-1:+1;       break; }
			case XML_USHORT:   { if (pushortValue[ii] != cValue.pushortValue[ii])     return (pushortValue[ii] < cValue.pushortValue[ii])?-1:+1;     break; }
			case XML_INT:      { if (pintValue[ii] != cValue.pintValue[ii])           return (pintValue[ii] < cValue.pintValue[ii])?-1:+1;           break; }
			case XML_UINT:     { if (puintValue[ii] != cValue.puintValue[ii])         return (puintValue[ii] < cValue.puintValue[ii])?-1:+1;         break; }
			case XML_LONG:     { if (plongValue[ii] != cValue.plongValue[ii])         return (plongValue[ii] < cValue.plongValue[ii])?-1:+1;         break; }
			case XML_ULONG:    { if (pulongValue[ii] != cValue.pulongValue[ii])       return (pulongValue[ii] < cValue.pulongValue[ii])?-1:+1;       break; }
			case XML_FLOAT:    { if (pfloatValue[ii] != cValue.pfloatValue[ii])       return (pfloatValue[ii] < cValue.pfloatValue[ii])?-1:+1;       break; }
			case XML_DOUBLE:   { if (pdoubleValue[ii] != cValue.pdoubleValue[ii])     return (pdoubleValue[ii] < cValue.pdoubleValue[ii])?-1:+1;     break; }
			case XML_DECIMAL:  { if (pdecimalValue[ii] != cValue.pdecimalValue[ii])   return (pdecimalValue[ii] < cValue.pdecimalValue[ii])?-1:+1;   break; }
			case XML_DATETIME: { if (pdateTimeValue[ii] != cValue.pdateTimeValue[ii]) return (pdateTimeValue[ii] < cValue.pdateTimeValue[ii])?-1:+1; break; }
			
			case XML_STRING:   
			{ 
				int iResult = wcscmp(pstringValue[ii], cValue.pstringValue[ii]);
				
				if (iResult != 0)
				{
					return iResult;
				}

				break;
			}
		}
	}

	return 0;
}

// Clear
void AnyType::Clear()
{
	if (iLength >= 0)
	{
		switch (eType)
		{
			case XML_BOOLEAN:  { OpcFree(pboolValue);     break; }
			case XML_SBYTE:    { OpcFree(psbyteValue);     break; }
			case XML_BYTE:     { OpcFree(pbyteValue);     break; }
			case XML_SHORT:    { OpcFree(pshortValue);    break; }
			case XML_USHORT:   { OpcFree(pushortValue);   break; }
			case XML_INT:      { OpcFree(pintValue);      break; }
			case XML_UINT:     { OpcFree(puintValue);     break; }
			case XML_LONG:     { OpcFree(plongValue);     break; }
			case XML_ULONG:    { OpcFree(pulongValue);    break; }
			case XML_FLOAT:    { OpcFree(pfloatValue);    break; }
			case XML_DOUBLE:   { OpcFree(pdoubleValue);   break; }
			case XML_DECIMAL:  { OpcFree(pdecimalValue);  break; }
			case XML_DATETIME: { OpcFree(pdateTimeValue); break; }
								
			case XML_STRING:   
			{ 
				for (int ii = 0; ii < iLength; ii++) 
				{
					OpcFree(pstringValue[ii]);
				}

				OpcFree(pstringValue);     
				break; 
			}

			case XML_ANY_TYPE:   
			{ 
				delete [] panyTypeValue;    
				break; 
			}
		}
	}
	else
	{
		switch (eType)
		{
			case XML_STRING: { OpcFree(stringValue); break;	}
		}
	}

	
	eType   = XML_EMPTY;
	iLength = -1;

	memset(&dateTimeValue, 0, sizeof(dateTimeValue));
}

// Alloc
void AnyType::Alloc(Type eElement, UINT uLength)
{
	Clear();

	eType   = eElement;
	iLength = uLength;

	switch (eType)
	{
		case XML_BOOLEAN:  { pboolValue     = OpcArrayAlloc(Boolean,  iLength); break; }
		case XML_SBYTE:    { psbyteValue    = OpcArrayAlloc(SByte,    iLength); break; }
		case XML_BYTE:     { pbyteValue     = OpcArrayAlloc(Byte,     iLength); break; }
		case XML_SHORT:    { pshortValue    = OpcArrayAlloc(Short,    iLength); break; }
		case XML_USHORT:   { pushortValue   = OpcArrayAlloc(UShort,   iLength); break; }
		case XML_INT:      { pintValue      = OpcArrayAlloc(Int,      iLength); break; }
		case XML_UINT:     { puintValue     = OpcArrayAlloc(UInt,     iLength); break; }
		case XML_LONG:     { plongValue     = OpcArrayAlloc(Long,     iLength); break; }
		case XML_ULONG:    { pulongValue    = OpcArrayAlloc(ULong,    iLength); break; }
		case XML_FLOAT:    { pfloatValue    = OpcArrayAlloc(Float,    iLength); break; }
		case XML_DOUBLE:   { pdoubleValue   = OpcArrayAlloc(Double,   iLength); break; }
		case XML_DECIMAL:  { pdecimalValue  = OpcArrayAlloc(Decimal,  iLength); break; }
		case XML_DATETIME: { pdateTimeValue = OpcArrayAlloc(DateTime, iLength); break; }
		case XML_STRING:   { pstringValue   = OpcArrayAlloc(String,   iLength); break; }
		case XML_ANY_TYPE: { panyTypeValue  = new AnyType[iLength];             break; }
	}
}

// MoveTo
void AnyType::MoveTo(AnyType& cTarget)
{
	cTarget.Clear();

	cTarget.eType   = eType;
	cTarget.iLength = iLength;
	cTarget.cSchema = cSchema;
	
	memcpy(&cTarget.dateTimeValue, &dateTimeValue, sizeof(dateTimeValue));

	Init();
}

// CopyTo
bool AnyType::CopyTo(AnyType& cValue, Type eNewType)
{
	if (eType == eNewType)
	{
		cValue = *this;
		return true;
	}

	COpcVariant cSrc;
	
	if (!Get(cSrc.GetRef()))
	{
		return false;
	}

	COpcVariant cDst;

	HRESULT hResult = COpcVariant::ChangeType(cDst.GetRef(), cSrc.GetRef(), NULL, GetVarType(eNewType));

	if (FAILED(hResult))
	{
		return false;
	}

	cValue.Set(cDst.GetRef());

	return true;
}

// GetElement
bool AnyType::GetElement(int iIndex, AnyType& cElement) const
{
	if (iLength < 0 || iIndex < 0 || iIndex >= iLength)
	{
		return false;
	}

	switch (eType)
	{
		default:           { return false; }
		case XML_BOOLEAN:  { cElement.Set(pboolValue[iIndex]);     break; }
		case XML_SBYTE:    { cElement.Set(psbyteValue[iIndex]);    break; }
		case XML_BYTE:     { cElement.Set(pbyteValue[iIndex]);     break; }
		case XML_SHORT:    { cElement.Set(pshortValue[iIndex]);    break; }
		case XML_USHORT:   { cElement.Set(pushortValue[iIndex]);   break; }
		case XML_INT:      { cElement.Set(pintValue[iIndex]);      break; }
		case XML_UINT:     { cElement.Set(puintValue[iIndex]);     break; }
		case XML_LONG:     { cElement.Set(plongValue[iIndex]);     break; }
		case XML_ULONG:    { cElement.Set(pulongValue[iIndex]);    break; }
		case XML_FLOAT:    { cElement.Set(pfloatValue[iIndex]);    break; }
		case XML_DOUBLE:   { cElement.Set(pdoubleValue[iIndex]);   break; }
		case XML_DECIMAL:  { cElement.Set(pdecimalValue[iIndex]);  break; }
		case XML_DATETIME: { cElement.Set(pdateTimeValue[iIndex]); break; }
		case XML_STRING:   { cElement.Set(pstringValue[iIndex]);   break; }
		case XML_ANY_TYPE: { cElement = panyTypeValue[iIndex];     break; }
	}

	return true;
}

// SetElement
bool AnyType::SetElement(int iIndex, const AnyType& cElement)
{
	if (iLength < 0 || iIndex < 0 || iIndex >= iLength)
	{
		return false;
	}

	switch (eType)
	{
		default:           { return false; }
		case XML_BOOLEAN:  { return cElement.Get(pboolValue[iIndex]);        }
		case XML_SBYTE:    { return cElement.Get(psbyteValue[iIndex]);       }
		case XML_BYTE:     { return cElement.Get(pbyteValue[iIndex]);        }
		case XML_SHORT:    { return cElement.Get(pshortValue[iIndex]);       }
		case XML_USHORT:   { return cElement.Get(pushortValue[iIndex]);      }
		case XML_INT:      { return cElement.Get(pintValue[iIndex]);         }
		case XML_UINT:     { return cElement.Get(puintValue[iIndex]);        }
		case XML_LONG:     { return cElement.Get(plongValue[iIndex]);        }
		case XML_ULONG:    { return cElement.Get(pulongValue[iIndex]);       }
		case XML_FLOAT:    { return cElement.Get(pfloatValue[iIndex]);       }
		case XML_DOUBLE:   { return cElement.Get(pdoubleValue[iIndex]);      }
		case XML_DECIMAL:  { return cElement.Get(pdecimalValue[iIndex]);     }
		case XML_DATETIME: { return cElement.Get(pdateTimeValue[iIndex]);    }
		case XML_STRING:   { return cElement.Get(pstringValue[iIndex]);      }
		case XML_ANY_TYPE: { panyTypeValue[iIndex] = cElement;  return true; }
	}

	return false;
}

// Get
bool AnyType::Get(Boolean& value) const
{
	if (eType == XML_BOOLEAN && iLength < 0)
	{
		value = boolValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(SByte& value) const
{
	if (eType == XML_SBYTE && iLength < 0)
	{
		value = sbyteValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(Byte& value) const
{
	if (eType == XML_BYTE && iLength < 0)
	{
		value = byteValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(Short& value) const
{
	if (eType == XML_SHORT && iLength < 0)
	{
		value = shortValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(UShort& value) const
{
	if (eType == XML_USHORT && iLength < 0)
	{
		value = ushortValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(Int& value) const
{
	if (eType == XML_INT && iLength < 0)
	{
		value = intValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(UInt& value) const
{
	if (eType == XML_UINT && iLength < 0)
	{
		value = uintValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(long& value) const
{
	if (eType == XML_INT && iLength < 0)
	{
		value = intValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(unsigned long& value) const
{
	if (eType == XML_UINT && iLength < 0)
	{
		value = uintValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(Long& value) const
{
	if (eType == XML_LONG && iLength < 0)
	{
		value = longValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(ULong& value) const
{
	if (eType == XML_ULONG && iLength < 0)
	{
		value = ulongValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(Float& value) const
{
	if (eType == XML_FLOAT && iLength < 0)
	{
		value = floatValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(Double& value) const
{
	if (eType == XML_DOUBLE && iLength < 0)
	{
		value = doubleValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(Decimal& value) const
{
	if (eType == XML_DECIMAL && iLength < 0)
	{
		value = decimalValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(DateTime& value) const
{
	if (eType == XML_DATETIME && iLength < 0)
	{
		value = dateTimeValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(String& value) const
{
	if (eType == XML_STRING && iLength < 0)
	{
		value = OpcStrDup(stringValue);
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(LPSTR& value) const
{
	if (eType == XML_STRING && iLength < 0)
	{
		value = OpcStrDup((LPCSTR)(COpcString)stringValue);
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(COpcString& value) const
{
	if (eType == XML_STRING && iLength < 0)
	{
		value = stringValue;
		return true;
	}

	return false;
}

// Get
bool AnyType::Get(COpcStringArray& value) const
{
	if (eType == XML_STRING && iLength >= 0)
	{
		value.SetSize(iLength);

		for (int ii = 0; ii < iLength; ii++)
		{
			value[ii] = pstringValue[ii];
		}

		return true;
	}

	return false;
}

// Get
bool AnyType::Get(VARIANT& cVariant) const
{
	OpcVariantClear(&cVariant);

	VARTYPE vtType = GetVarType(eType);

	if (iLength < 0)
	{
		cVariant.vt = vtType;

		switch (eType)
		{			
			case XML_SBYTE:    { cVariant.cVal    = sbyteValue;                              break; }
			case XML_BYTE:     { cVariant.bVal    = byteValue;                              break; }
			case XML_SHORT:    { cVariant.iVal    = shortValue;                             break; }
			case XML_USHORT:   { cVariant.uiVal   = ushortValue;                            break; }
			case XML_INT:      { cVariant.lVal    = intValue;                               break; }
			case XML_UINT:     { cVariant.ulVal   = uintValue;                              break; }
			case XML_LONG:     { cVariant.llVal   = longValue;                              break; }
			case XML_ULONG:    { cVariant.ullVal  = ulongValue;                             break; }
			case XML_FLOAT:    { cVariant.fltVal  = floatValue;                             break; }
			case XML_DOUBLE:   { cVariant.dblVal  = doubleValue;                            break; }
			case XML_DECIMAL:  { cVariant.cyVal   = decimalValue;                           break; }
			case XML_BOOLEAN:  { cVariant.boolVal = (boolValue)?VARIANT_TRUE:VARIANT_FALSE; break; }
			case XML_DATETIME: { cVariant.date    = GetVarDate(dateTimeValue);              break; }
			case XML_STRING:   { cVariant.bstrVal = SysAllocString(stringValue);            break; }
		}

		return true;
	}

	COpcSafeArray cArray(cVariant);

	cArray.Alloc(vtType, iLength);
	cArray.Lock();

	void* pData = cArray.GetData();

	for (int ii = 0; ii < iLength; ii++)
	{
		switch (eType)
		{			
			case XML_SBYTE:    { ((CHAR*)pData)[ii]         = psbyteValue[ii];                   break; }
			case XML_BYTE:     { ((BYTE*)pData)[ii]         = pbyteValue[ii];                   break; }
			case XML_SHORT:    { ((SHORT*)pData)[ii]        = pshortValue[ii];                  break; }
			case XML_USHORT:   { ((USHORT*)pData)[ii]       = pushortValue[ii];                 break; }
			case XML_INT:      { ((LONG*)pData)[ii]         = pintValue[ii];                    break; }
			case XML_UINT:     { ((ULONG*)pData)[ii]        = puintValue[ii];                   break; }
			case XML_LONG:     { ((LONGLONG*)pData)[ii]     = plongValue[ii];                   break; }
			case XML_ULONG:    { ((ULONGLONG*)pData)[ii]    = pulongValue[ii];                  break; }
			case XML_FLOAT:    { ((FLOAT*)pData)[ii]        = pfloatValue[ii];                  break; }
			case XML_DOUBLE:   { ((DOUBLE*)pData)[ii]       = pdoubleValue[ii];                 break; }
			case XML_DECIMAL:  { ((CY*)pData)[ii]           = pdecimalValue[ii];                break; }
			case XML_DATETIME: { ((DATE*)pData)[ii]         = GetVarDate(pdateTimeValue[ii]);   break; }
			
			case XML_STRING:   
			{ 
				BSTR* pbstrData = (BSTR*)pData;
				pbstrData[ii] = SysAllocString(pstringValue[ii]); 
				break; 
			}
			
			case XML_BOOLEAN:  
			{ 
				((VARIANT_BOOL*)pData)[ii] = (pboolValue[ii])?VARIANT_TRUE:VARIANT_FALSE; 
				break; 
			}
			
			case XML_ANY_TYPE:  
			{ 
				panyTypeValue[ii].Get(((VARIANT*)pData)[ii]); 
				break;
			}
		}
	}

	cArray.Unlock();
	return true;
}

// Set
void AnyType::Set(Boolean value)
{
	Clear();

	eType     = XML_BOOLEAN;
	iLength   = -1;
	boolValue = value;
}

// Set
void AnyType::Set(SByte value)
{
	Clear();

	eType      = XML_SBYTE;
	iLength    = -1;
	sbyteValue = value;
}

// Set
void AnyType::Set(Byte value)
{
	Clear();

	eType     = XML_BYTE;
	iLength   = -1;
	byteValue = value;
}

// Set
void AnyType::Set(Short value)
{
	Clear();

	eType      = XML_SHORT;
	iLength    = -1;
	shortValue = value;
}

// Set
void AnyType::Set(UShort value)
{
	Clear();

	eType       = XML_USHORT;
	iLength     = -1;
	ushortValue = value;
}

// Set
void AnyType::Set(Int value)
{
	Clear();

	eType    = XML_INT;
	iLength  = -1;
	intValue = value;
}

// Set
void AnyType::Set(UInt value)
{
	Clear();

	eType     = XML_UINT;
	iLength   = -1;
	uintValue = value;
}

// Set
void AnyType::Set(long value)
{
	Clear();

	eType    = XML_INT;
	iLength  = -1;
	intValue = (Int)value;
}

// Set
void AnyType::Set(unsigned long value)
{
	Clear();

	eType     = XML_UINT;
	iLength   = -1;
	uintValue = (UInt)value;
}

// Set
void AnyType::Set(Long value)
{
	Clear();

	eType     = XML_LONG;
	iLength   = -1;
	longValue = value;
}

// Set
void AnyType::Set(ULong value)
{
	Clear();

	eType      = XML_ULONG;
	iLength    = -1;
	ulongValue = value;
}

// Set
void AnyType::Set(Float value)
{
	Clear();

	eType      = XML_FLOAT;
	iLength    = -1;
	floatValue = value;
}

// Set
void AnyType::Set(Double value)
{
	Clear();
 
	eType       = XML_DOUBLE;
	iLength     = -1;
	doubleValue = value;
}

// Set
void AnyType::Set(Decimal value)
{
	Clear();
 
	eType        = XML_DECIMAL;
	iLength      = -1;
	decimalValue = value;
}

// Set
void AnyType::Set(DateTime value)
{
	Clear();
 
	eType         = XML_DATETIME;
	iLength       = -1;
	dateTimeValue = value;
}

// Set
void AnyType::Set(const String value)
{
	Clear();
 
	eType       = XML_STRING;
	iLength     = -1;
	stringValue = OpcStrDup(value);
}

// Set
void AnyType::Set(LPCSTR value)
{
	Clear();
 
	eType       = XML_STRING;
	iLength     = -1;
	stringValue = OpcStrDup((LPCWSTR)(COpcString)value);
}

// Set
void AnyType::Set(const COpcString& value)
{
	Clear();
 
	eType       = XML_STRING;
	iLength     = -1;
	stringValue = OpcStrDup((LPCWSTR)value);
}

// Set
void AnyType::Set(const COpcStringArray& value)
{
	Clear();
 
	eType   = XML_STRING;
	iLength = value.GetSize();

	pstringValue = OpcArrayAlloc(String, iLength);
	memset(pstringValue, 0, sizeof(String)*iLength);

	for (int ii = 0; ii < iLength; ii++)
	{
		pstringValue[ii] = OpcStrDup((LPCWSTR)value[ii]);
	}
}

// Set
void AnyType::Set(const VARIANT& cVariant)
{
	Clear();

	eType = OpcXml::GetXmlType(cVariant.vt & VT_TYPEMASK);

	if ((cVariant.vt & VT_ARRAY) == 0)
	{
		switch (eType)
		{			
			case XML_SBYTE:    { sbyteValue     = cVariant.cVal;                      break; }
			case XML_BYTE:     { byteValue     = cVariant.bVal;                      break; }
			case XML_SHORT:    { shortValue    = cVariant.iVal;                      break; }
			case XML_USHORT:   { ushortValue   = cVariant.uiVal;                     break; }
			case XML_INT:      { intValue      = cVariant.lVal;                      break; }
			case XML_UINT:     { uintValue     = cVariant.ulVal;                     break; }
			case XML_LONG:     { longValue     = cVariant.llVal;                     break; }
			case XML_ULONG:    { ulongValue    = cVariant.ullVal;                    break; }
			case XML_FLOAT:    { floatValue    = cVariant.fltVal;                    break; }
			case XML_DOUBLE:   { doubleValue   = cVariant.dblVal;                    break; }
			case XML_DECIMAL:  { decimalValue  = cVariant.cyVal;                     break; }
			case XML_BOOLEAN:  { boolValue     = (cVariant.boolVal == VARIANT_TRUE); break; }
			case XML_DATETIME: { dateTimeValue = GetXmlDateTime(cVariant.date);      break; }
			case XML_STRING:   { stringValue   = OpcStrDup(cVariant.bstrVal);        break; }
		}

		return;
	}

	COpcSafeArray cArray((VARIANT&)cVariant);

	Alloc(eType, cArray.GetLength());

	cArray.Lock();

	void* pData = cArray.GetData();

	for (int ii = 0; ii < iLength; ii++)
	{
		switch (eType)
		{			
			case XML_SBYTE:    { psbyteValue[ii]     = ((CHAR*)pData)[ii];                           break; }
			case XML_BYTE:     { pbyteValue[ii]     = ((BYTE*)pData)[ii];                           break; }
			case XML_SHORT:    { pshortValue[ii]    = ((SHORT*)pData)[ii];                          break; }
			case XML_USHORT:   { pushortValue[ii]   = ((USHORT*)pData)[ii];                         break; }
			case XML_INT:      { pintValue[ii]      = ((LONG*)pData)[ii];                           break; }
			case XML_UINT:     { puintValue[ii]     = ((ULONG*)pData)[ii];                          break; }
			case XML_LONG:     { plongValue[ii]     = ((LONGLONG*)pData)[ii];                       break; }
			case XML_ULONG:    { pulongValue[ii]    = ((ULONGLONG*)pData)[ii];                      break; }
			case XML_FLOAT:    { pfloatValue[ii]    = ((FLOAT*)pData)[ii];                          break; }
			case XML_DOUBLE:   { pdoubleValue[ii]   = ((DOUBLE*)pData)[ii];                         break; }
			case XML_DECIMAL:  { pdecimalValue[ii]  = ((CY*)pData)[ii];                             break; }
			case XML_DATETIME: { pdateTimeValue[ii] = GetXmlDateTime(((DATE*)pData)[ii]);           break; }
			case XML_BOOLEAN:  { pboolValue[ii]     = (((VARIANT_BOOL*)pData)[ii] == VARIANT_TRUE); break; }
			
			case XML_STRING:   
			{ 
				BSTR* pbstrData = (BSTR*)pData;
				pstringValue[ii] = OpcStrDup(pbstrData[ii]);
				break; 
			}

			case XML_ANY_TYPE:  
			{ 
				panyTypeValue[ii].Set(((VARIANT*)pData)[ii]); 
				break;
			}
		}
	}

	cArray.Unlock();
}
