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

#ifndef _OPCUA_LIST_H
#define _OPCUA_LIST_H 1

OPCUA_BEGIN_EXTERN_C

/*============================================================================
 * List Element
 *===========================================================================*/

typedef struct _OpcUa_ListElement OpcUa_ListElement;

/** 
 * @brief A list element (control structure) in a double linked list. (rear- and forward linked elements)
 */
struct _OpcUa_ListElement
{
    /*! @brief Pointer to the following element in the list. OpcUa_Null if current element is the last. */
    OpcUa_ListElement*  nextElement;
    /*! @brief Pointer to the previous element in the list. OpcUa_Null if current element is the first. */
    OpcUa_ListElement*  prevElement;
    /*! @brief Pointer to the data associated with this list element. */
    OpcUa_Void*         data;
};

/*============================================================================
 * List Element Management
 *===========================================================================*/

/**
  @brief Creates and initializes a new list element

  @return OpcUa_Good on success

  @param a_ppNewElement [in/out]    Location of a pointer to the new List Element
*/
OPCUA_EXPORT
OpcUa_StatusCode    OpcUa_ListElement_Create(       OpcUa_ListElement** ppElement);

/**
  @brief Initializes all element members.

  Sets all members of a List Element to OpcUa_Null
  Takes no action if pElement is null

  @param pElement   [in]    Location of a List Element to initialize
*/
OPCUA_EXPORT
OpcUa_Void          OpcUa_ListElement_Initialize(   OpcUa_ListElement*  pElement);

/**
  @brief Removes an element from the list

  The user data is not deleted! This has to be done by the user.
  (no list management)

  Takes no action if a_pElement is null

  @param a_pElement [in]    Location of the List Element to remove
*/
OPCUA_EXPORT
OpcUa_Void          OpcUa_ListElement_Clear(        OpcUa_ListElement*  pElement);

/**
  @brief Deletes the given List Element (no list management).

  Takes no action if a_ppElement is null
  Takes no action if *a_ppElement is null

  @param a_ppElement    [in]    Location of a pointer to the List Element to delete
*/
OPCUA_EXPORT
OpcUa_Void          OpcUa_ListElement_Delete(       OpcUa_ListElement** ppElement);


/*============================================================================
 * List
 *===========================================================================*/

/** 
 * @brief The main handle for a list. 
 */
struct _OpcUa_List
{
    /*! @brief Pointer to the first element in the list. */
    OpcUa_ListElement* firstElement;
    /*! @brief Pointer to the last element in the list. */
    OpcUa_ListElement* lastElement;
    /*! @brief Cursor to the current element in the list. */
    OpcUa_ListElement* currtElement;
    /*! @brief The current number of elements in the list. */
    OpcUa_UInt32       uintNbElements;
    /*! @brief For synchronizing access to the list. */
    OpcUa_Mutex        pMutex;
};

typedef struct _OpcUa_List OpcUa_List;

/*============================================================================
 * list create, init, clear, delete
 *===========================================================================*/
/**
  @brief Creates a new empty list.

  Crashes if a_ppNewList is null
  @return OpcUa_Good on success

  @param a_ppNewList    [in/out]    Location of a pointer to the new list
*/
OPCUA_EXPORT
OpcUa_StatusCode   OpcUa_List_Create(               OpcUa_List** ppList);

/**
  @brief Initializes all internal variables of the list. 
  
  Do not call this function twice. Memory leaks will be created.

  @return OpcUa_BadInvalidArgument if a_pList is null
  @return OpcUa_Good on success

  @param a_pList    [in]    Location of the list
*/
OPCUA_EXPORT
OpcUa_StatusCode    OpcUa_List_Initialize(          OpcUa_List*     pList);

/**
  @brief Removes all elements from the list.
  
  User data is not deallocated and must be delete manually
  Takes no action if a_pList is null

  Because this function deletes the mutex, OpcUa_List_Initialize
  must be called before the list can be used again

  @param a_pList    [in]    Location of the list to clear
*/
OPCUA_EXPORT
OpcUa_Void          OpcUa_List_Clear(               OpcUa_List*     pList);

/**
  @brief Deletes the list.

  Takes no action if a_ppList is null
  Takes no action if *a_ppList is null

  @param a_ppList   [in]    Location of a pointer to the list to delete
*/
OPCUA_EXPORT
OpcUa_Void          OpcUa_List_Delete(              OpcUa_List**    ppList);

/*============================================================================
 * list management
 *===========================================================================*/
/**
  @brief Locks the internal mutex and prevents other threads from entering the list.

  Takes no action if a_pList is null
  Takes no action if the mutex is null

  @param a_pList    [in]    Location of the list to enter
*/
OPCUA_EXPORT
OpcUa_Void          OpcUa_List_Enter(               OpcUa_List*     pList);

/**
  @brief Unlocks the internal mutex and allows other threads to enter the list.

  Takes no action if a_pList is null
  Takes no action if the mutex is null

  @param a_pList    [in]    Location of the list to leave
*/
OPCUA_EXPORT
OpcUa_Void          OpcUa_List_Leave(               OpcUa_List*     pList);

/*============================================================================
 * List Operations
 *===========================================================================*/
/**
  @brief Adds a new element with the given data into the List. The element is inserted as
  the first element. 
 
  @return OpcUa_Good on success
  @return OpcUa_BadOutOfMemory if the allocation of the new element fails
  @return OpcUa_BadInvalidArgument if a_pList is null
  @return OpcUa_BadInvalidArgument if a_pElementData is null

  @param a_pList            [in]    Location of list
  @param a_pElementData     [in]    Location of user data
*/
OPCUA_EXPORT
OpcUa_StatusCode    OpcUa_List_AddElement(          OpcUa_List*     pList, 
                                                    OpcUa_Void*     pElementData);

/**
  @brief Add an element to the end of the list
  This function is used along with OpcUa_List_RemoveFirstElement to make the list
  behave like a queue

  @return OpcUa_BadOutOfMemory if the allocation of the new element fails
  @return OpcUa_BadInvalidArgument if a_pElementData is null
  @return OpcUa_Good otherwise

  @param a_pList            [in]    Location of the list
  @param a_pElementData     [in]    Location of user data to add to the list
*/
OPCUA_EXPORT
OpcUa_StatusCode    OpcUa_List_AddElementToEnd(     OpcUa_List*     pList, 
                                                    OpcUa_Void*     pElementData);

/**
  @brief Moves the cursor to the first element in list.

  @return OpcUa_Good on success
  @return OpcUa_BadInvalidArgument if a_pList is null

  @param a_pList    [in]    Location of the list
*/
OPCUA_EXPORT
OpcUa_StatusCode    OpcUa_List_ResetCurrent(        OpcUa_List*     pList);

/**
  @brief Moves the cursor to the next element if existing and returns the userdata.
 
  @return OpcUa_Null if the cursor was pointing to the last element
  @return OpcUa_Null if a_pList is null
  @return OpcUa_Null if a_pList->currtElement is null
  @return the data from the next element otherwise

  @param a_pList    [in]    Location of the list
*/
OPCUA_EXPORT
OpcUa_Void*         OpcUa_List_GetNextElement(      OpcUa_List*     pList);

/**
  @brief Get the user data from the current element.
 
  @return OpcUa_Null if a_pList is null
  @return OpcUa_Null if the cursor is pointing to null
  @return the user data of the current element otherwise

  @param a_pList    [in]    Location of the list
*/
OPCUA_EXPORT
OpcUa_Void*         OpcUa_List_GetCurrentElement(OpcUa_List*     pList);

/**
  @brief Moves the cursor to the previous element if existing and returns the userdata.
 
  @return OpcUa_Null if the cursor was pointing to the first element
  @return OpcUa_Null if a_pList is null
  @return OpcUa_Null if a_pList->currtElement is null
  @return the data from the previous element otherwise

  @param a_pList    [in]    Location of the list
*/
OPCUA_EXPORT
OpcUa_Void*         OpcUa_List_GetPrevElement(      OpcUa_List*     pList);

/**
  @brief Set the cursor to the element with the given data
 
  @return OpcUa_Null if no element is found
  @return OpcUa_Null if a_pList is null
  @return OpcUa_Null if a_pElementData is null
  @return a pointer to the user data that was found

  This function should use currtElement instead of tempElement so that
  when it returns the cursor is left pointing at the found element

  @param a_plist            [in]    Location of the list
  @param a_pElementData     [in]    Data to find
*/
OPCUA_EXPORT
OpcUa_Void*         OpcUa_List_GetElement(          OpcUa_List*     pList, 
                                                    OpcUa_Void*     pElementData);
/**
  @brief Deletes the current element performing all necessary list management
  However, user data is not deleted and must be removed manually

  Takes no action if a_pList is null
  Takes no action if a_pList->currtElement is null

  @param a_pList    [in]    Location of the list
*/
OPCUA_EXPORT
OpcUa_Void          OpcUa_List_DeleteCurrentElement(OpcUa_List*     pList);

/**
  @brief Removes the element containing the specified user data from the list.
  The data itself is not deleted.
 
  @return OpcUa_BadInternalError if a_pList is null
  @return OpcUa_BadInternalError if a_pElementData is null
  @return OpcUa_Good on success;

  @param a_pList            [in]    Location of the list
  @param a_pElementData     [in]    Location of the data to remove from the list
*/
OPCUA_EXPORT
OpcUa_StatusCode    OpcUa_List_DeleteElement(       OpcUa_List*     pList, 
                                                    OpcUa_Void*     pElementData);

/**
  @brief Returns the number of elements currently in the list.

  @return OpcUa_BadInvalidArgument if a_pList is null
  @return OpcUa_BadInvalidArgument if a_uintElementCount is null
  @return OpcUa_Good otherwise

  @param a_pList            [in]    Location of the list
  @param a_uintElementCount [out]   Location of an OpcUa_UInt32 to store the size of the list
*/
OPCUA_EXPORT
OpcUa_StatusCode    OpcUa_List_GetNumberOfElements( OpcUa_List*     pList, 
                                                    OpcUa_UInt32*   uintElementCount);

/**
  @brief Remove the first element, return its data, and reset the cursor
  This function is used along with OpcUa_List_AddElementToEnd to make the list
  behave like a queue
  
  @return OpcUa_Null if a_pList is null
  @return OpcUa_Null if the list is empty
  @return the user data from the first element otherwise

  @param a_pList    [in]    Location of the list
*/
OPCUA_EXPORT
OpcUa_Void*         OpcUa_List_RemoveFirstElement(  OpcUa_List*     pList);

OPCUA_END_EXTERN_C

#endif /*_OPCUA_LIST_H*/
