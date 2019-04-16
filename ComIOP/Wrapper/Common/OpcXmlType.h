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

#ifndef _OpcXmlType_H_
#define _OpcXmlType_H_

#include "OpcDefs.h"
#include "COpcString.h"

#include "MsXml2.h"

//==============================================================================
// MACRO:   OPCXML_NS_XXX
// PURPOSE: Common XML namespace URIs.

#define OPCXML_NS_SCHEMA             _T("http://www.w3.org/2001/XMLSchema")
#define OPCXML_NS_SCHEMA_INSTANCE    _T("http://www.w3.org/2001/XMLSchema-instance")
#define OPCXML_NS_OPC                _T("http://schemas.opcfoundation.org/OPC/")
#define OPCXML_NS_OPC_SAMPLE         _T("http://schemas.opcfoundation.org/OPCSample/")
#define OPCXML_NS_OPC_DATA_ACCESS    _T("http://opcfoundation.org/webservices/XMLDA/10/")
#define OPCXML_NS_OPC_DATA_EXCHANGE  _T("http://opcfoundation.org/webservices/DX/10/")
#define OPCXML_NS_OPCBINARY          _T("http://opcfoundation.org/OPCBinary/1.0/")

#define OPCXML_DEFAULT_ELEMENT       _T("Element")
#define OPCXML_ARRAY_PREFIX          _T("ArrayOf")
#define OPCXML_TYPE_ATTRIBUTE        _T("type")
#define OPCXML_NAMESPACE_ATTRIBUTE   _T("xmlns")

namespace OpcXml
{

//==============================================================================
// TYPES:   XXX
// PURPOSE: Define C++ types for each XML data type.

typedef CHAR      SByte;
typedef BYTE      Byte;
typedef SHORT     Short;
typedef USHORT    UShort;
typedef INT       Int;
typedef UINT      UInt;
typedef LONGLONG  Long;
typedef ULONGLONG ULong;
typedef FLOAT     Float;
typedef DOUBLE    Double;
typedef CY        Decimal;
typedef bool      Boolean;
typedef LPWSTR    String;
typedef FILETIME  DateTime;

//==============================================================================
// ENUM:    Type
// PURPOSE: Defines the set of possible XML data types.

enum Type
{
    XML_EMPTY    = 0x0000,
    XML_BOOLEAN  = 0x0001,
    XML_SBYTE    = 0x0002,
    XML_BYTE     = 0x0003,
    XML_SHORT    = 0x0004,
    XML_USHORT   = 0x0005,
    XML_INT      = 0x0006,
    XML_UINT     = 0x0007,
    XML_LONG     = 0x0008,
    XML_ULONG    = 0x0009,
    XML_FLOAT    = 0x000A,
    XML_DOUBLE   = 0x000B,   
    XML_DECIMAL  = 0x000C,
    XML_STRING   = 0x000D,
    XML_DATETIME = 0x000E,
    XML_ANY_TYPE = 0x000F
};

//==============================================================================
// CLASS:   QName
// PURPOSE: A fully qualified name in XML.

class QName
{
	OPC_CLASS_NEW_DELETE_ARRAY();

public:
	
	//==========================================================================
	// Public Operators

	// Constructors
	QName() {}
	QName(const QName& cQName) { *this = cQName; }
	QName(const COpcString& cName) : m_cName(cName), m_cNamespace("") {}
	QName(const COpcString& cName, const COpcString& cNamespace) : m_cName(cName), m_cNamespace(cNamespace) {}
	
	// Destructor
	~QName() {}

	// Assignment
	QName& operator=(const QName& cQName) { m_cName = cQName.m_cName; m_cNamespace = cQName.m_cNamespace; return *this; }

	// Equality
	bool operator==(const QName& cQName) { return ((m_cName == cQName.m_cName) && (m_cNamespace == cQName.m_cNamespace)); }
	bool operator!=(const QName& cQName) { return !(*this == cQName); }

	//==========================================================================
	// Public Properties

	// Name
	const COpcString& GetName() const { return m_cName; }
	void SetName(const COpcString& cName) { m_cName = cName; }

	// Namespace
	const COpcString& GetNamespace() const { return m_cNamespace; }
	void SetNamespace(const COpcString& cNamespace) { m_cNamespace = cNamespace; }

private:
	
	//==========================================================================
	// Private Members

	COpcString m_cName;
	COpcString m_cNamespace;
};

//==============================================================================
// FUNCTION: Init
// PURPOSE   Initializes an object with default values.

template<class TYPE> 
void Init(TYPE& cValue)
{
    memset(&cValue, 0, sizeof(TYPE));
}

//==============================================================================
// FUNCTION: Clear
// PURPOSE   Frees memory owned by an object and initializes it.

template<class TYPE> 
void Clear(TYPE& cValue)
{
    Init(cValue);
}

//==============================================================================
// FUNCTION: Copy
// PURPOSE   Creates a deep copy of an object,

template<class TYPE> 
void Copy(TYPE& cDst, TYPE& cSrc)
{
    cDst = cSrc;
}

//==============================================================================
// FUNCTION: Read
// PURPOSE   Reads an object from an string.

template<class TYPE> 
bool Read(const COpcString& cText, TYPE& cValue)
{
    return false;
}

//==============================================================================
// FUNCTION: Read
// PURPOSE   Reads an object from an XML element or attribute.

template<class TYPE> 
bool ReadXml(IXMLDOMNode* ipNode, TYPE& cValue)
{
    Clear(cValue);

    if (ipNode == NULL) return false;

    // get node text property.
    BSTR bstrText = NULL;
    
    if (FAILED(ipNode->get_text(&bstrText)))
    {
        return false;
    }

    // convert from string.
    bool bResult = Read((LPCWSTR)bstrText, cValue);
    SysFreeString(bstrText);

    return bResult;
}

//==============================================================================
// FUNCTION: Write
// PURPOSE   Writes an object with to a string.

template<class TYPE> 
bool Write(const TYPE& cValue, COpcString& cText)
{
    return false;
}

//==============================================================================
// FUNCTION: Write
// PURPOSE   Writes an object to an XML element or attribute.

template<class TYPE> 
bool WriteXml(IXMLDOMNode* ipNode, const TYPE& cValue)
{
    if (ipNode == NULL) return false;

    // convert to string.
    COpcString cText;
    
    if (!Write(cValue, cText))
    {
        return false;
    }

    // set node text property.
    BSTR bstrText = SysAllocString((LPCWSTR)cText);
    HRESULT hResult = ipNode->put_text(bstrText);
    SysFreeString(bstrText);

    return SUCCEEDED(hResult);
}

//==============================================================================
// FUNCTION: Read<TYPE>
// PURPOSE   Explicit template implementations for common data types.

template<> OPCUTILS_API bool Read<SByte>(const COpcString& cText, SByte& cValue);
template<> OPCUTILS_API bool Read<Byte>(const COpcString& cText, Byte& cValue);
template<> OPCUTILS_API bool Read<Short>(const COpcString& cText, Short& cValue);
template<> OPCUTILS_API bool Read<UShort>(const COpcString& cText, UShort& cValue);
template<> OPCUTILS_API bool Read<Int>(const COpcString& cText, Int& cValue);
template<> OPCUTILS_API bool Read<UInt>(const COpcString& cText, UInt& cValue);
template<> OPCUTILS_API bool Read<long>(const COpcString& cText, long& cValue);
template<> OPCUTILS_API bool Read<unsigned long>(const COpcString& cText, unsigned long& cValue);
template<> OPCUTILS_API bool Read<Long>(const COpcString& cText, Long& cValue);
template<> OPCUTILS_API bool Read<ULong>(const COpcString& cText, ULong& cValue);
template<> OPCUTILS_API bool Read<Float>(const COpcString& cText, Float& cValue);
template<> OPCUTILS_API bool Read<Double>(const COpcString& cText, Double& cValue);
template<> OPCUTILS_API bool Read<Decimal>(const COpcString& cText, Decimal& cValue);
template<> OPCUTILS_API bool Read<DateTime>(const COpcString& cText, DateTime& cValue);
//template<> OPCUTILS_API bool Read<Date>(const COpcString& cText, Date& cValue);
//template<> OPCUTILS_API bool Read<Time>(const COpcString& cText, Time& cValue);
//template<> OPCUTILS_API bool Read<Duration>(const COpcString& cText, Duration& cValue);
template<> OPCUTILS_API bool Read<Boolean>(const COpcString& cText, Boolean& cValue);
template<> OPCUTILS_API bool Read<String>(const COpcString& cText, String& cValue);
//template<> OPCUTILS_API bool Read<QName>(const COpcString& cText, QName& cValue);
//template<> OPCUTILS_API bool Read<Binary>(const COpcString& cText, Binary& cValue);
//template<> OPCUTILS_API bool Read<Type>(const COpcString& cText, Type& cValue);

//==============================================================================
// FUNCTION: Write<TYPE>
// PURPOSE   Explicit template implementations for common data types.

template<> OPCUTILS_API bool Write<SByte>(const SByte& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<Byte>(const Byte& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<Short>(const Short& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<UShort>(const UShort& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<Int>(const Int& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<UInt>(const UInt& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<long>(const long& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<unsigned long>(const unsigned long& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<Long>(const Long& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<ULong>(const ULong& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<Float>(const Float& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<Double>(const Double& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<Decimal>(const Decimal& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<DateTime>(const DateTime& cValue, COpcString& cText);
//template<> OPCUTILS_API bool Write<Date>(const Date& cValue, COpcString& cText);
//template<> OPCUTILS_API bool Write<Time>(const Time& cValue, COpcString& cText);
//template<> OPCUTILS_API bool Write<Duration>(const Duration& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<Boolean>(const Boolean& cValue, COpcString& cText);
template<> OPCUTILS_API bool Write<String>(const String& cValue, COpcString& cText);
//template<> OPCUTILS_API bool Write<QName>(const QName& cValue, COpcString& cText);
//template<> OPCUTILS_API bool Write<Binary>(const Binary& cValue, COpcString& cText);
//template<> OPCUTILS_API bool Write<Type>(const Type& cValue, COpcString& cText);

//==============================================================================
// FUNCTION: XXX<COpcString>
// PURPOSE   Template declarations for COpcString.

template<> OPCUTILS_API void Init<COpcString>(COpcString& cValue);
template<> OPCUTILS_API void Clear<COpcString>(COpcString& cValue);
template<> OPCUTILS_API bool Read<COpcString>(const COpcString& cText, COpcString& cValue);
template<> OPCUTILS_API bool Write<COpcString>(const COpcString& cValue, COpcString& cText);

//==============================================================================
// FUNCTION: XXX<GUID>
// PURPOSE   Template declarations for GUID.

template<> OPCUTILS_API void Init<GUID>(GUID& cValue);
template<> OPCUTILS_API void Clear<GUID>(GUID& cValue);
template<> OPCUTILS_API bool Read<GUID>(const COpcString& cText, GUID& cValue);
template<> OPCUTILS_API bool Write<GUID>(const GUID& cValue, COpcString& cText);

}; // OpcXml

#endif // _OpcXmlType_H_
