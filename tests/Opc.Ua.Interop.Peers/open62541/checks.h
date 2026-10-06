/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

/*
 * The client checks of the open62541 peer against a server with the
 * Quickstarts reference server address space; the same checks as the
 * 1.5.378 peer's LegacyClientChecks. Included by peer.c.
 */

static UA_Client *c_client;
static UA_UInt16 c_ns;
static char c_msg[2048];
static int c_passed, c_failed;

#define REQUIRE(cond, ...)                                  \
    do {                                                    \
        if(!(cond)) {                                       \
            snprintf(c_msg, sizeof(c_msg), __VA_ARGS__);    \
            return c_msg;                                   \
        }                                                   \
    } while(0)

typedef const char *(*CheckFn)(void);

/* The connection settings, for the extra sessions some checks open. */
static const char *c_url, *c_policy, *c_mode, *c_applicationUri, *c_user;
static UA_ByteString c_certificate, c_privateKey;
static EccPolicyFn c_createEcc;

/* A client with the configuration of the command line; with userCertificate
 * the session authenticates with an X509 user identity token. */
static UA_Client *newClient(const UA_ByteString *userCertificate, const UA_ByteString *userKey) {
    UA_ClientConfig config;
    memset(&config, 0, sizeof(config));
    config.logging = &s_logger;
    UA_ClientConfig_setDefault(&config);
    config.timeout = 60000;
    config.requestedSessionTimeout = 120000;
    /* Mirrors the 2.0 reference server fixture: the large-message checks are
     * bounded by the server, not by this client. */
    config.localConnectionConfig.localMaxMessageSize = 16 * 1024 * 1024;
    config.localConnectionConfig.localMaxChunkCount = 0;
    if(atoi(opt("token-lifetime", "0")) > 0)
        config.secureChannelLifeTime = (UA_UInt32)atoi(opt("token-lifetime", "0"));
    if(strcmp(c_mode, "None") != 0)
        UA_ClientConfig_setDefaultEncryption(&config, c_certificate, c_privateKey, NULL, 0, NULL, 0);
    /* open62541 1.5 leaves the (non-AEAD) 1.05 ECC policies out of the client
     * defaults; with OpenSSL the defaults have the AEAD and Edwards curve
     * policies already when the certificate is of their curve. */
    int haveEcc = 0;
    for(size_t i = 0; c_createEcc && i < config.securityPoliciesSize; i++) {
        const UA_String *uri = &config.securityPolicies[i].policyUri;
        haveEcc |= uri->length >= strlen(c_policy) && memcmp(uri->data, c_policy, strlen(c_policy)) == 0 &&
                   (uri->length == strlen(c_policy) || uri->data[strlen(c_policy)] == 0);
    }
    if(c_createEcc && !haveEcc) {
        UA_SecurityPolicy *sp = (UA_SecurityPolicy *)UA_realloc(
            config.securityPolicies, sizeof(UA_SecurityPolicy) * (config.securityPoliciesSize + 1));
        if(sp) {
            config.securityPolicies = sp;
            if(c_createEcc(&sp[config.securityPoliciesSize], UA_APPLICATIONTYPE_CLIENT, c_certificate,
                           c_privateKey, &s_logger) == UA_STATUSCODE_GOOD)
                config.securityPoliciesSize++;
        }
    }
    UA_CertificateGroup_AcceptAll(&config.certificateVerification);
    UA_String_clear(&config.clientDescription.applicationUri);
    config.clientDescription.applicationUri = UA_STRING_ALLOC(c_applicationUri);
    config.securityMode = strcmp(c_mode, "SignAndEncrypt") == 0 ? UA_MESSAGESECURITYMODE_SIGNANDENCRYPT
                          : strcmp(c_mode, "Sign") == 0         ? UA_MESSAGESECURITYMODE_SIGN
                                                                : UA_MESSAGESECURITYMODE_NONE;
    config.securityPolicyUri = UA_STRING_ALLOC(c_policy);
    if(userCertificate)
        UA_ClientConfig_setAuthenticationCert(&config, *userCertificate, *userKey);
    return UA_Client_newWithConfig(&config);
}

/* Reconnects the main session after a check that lost it. */
static void ensureConnected(void) {
    UA_SecureChannelState channel;
    UA_SessionState session;
    UA_StatusCode status;
    UA_Client_getState(c_client, &channel, &session, &status);
    if(session == UA_SESSIONSTATE_ACTIVATED && status == UA_STATUSCODE_GOOD)
        return;
    printf("INFO reconnecting the session (state %d, %s)\n", (int)session, UA_StatusCode_name(status));
    UA_Client_getConfig(c_client)->noNewSession = false;
    UA_Client_disconnect(c_client);
    UA_StatusCode res = c_user ? UA_Client_connectUsername(c_client, c_url, c_user, opt("password", ""))
                               : UA_Client_connect(c_client, c_url);
    if(res != UA_STATUSCODE_GOOD)
        printf("INFO the reconnect failed: %s\n", UA_StatusCode_name(res));
}

static void report(const char *name, const char *failure, UA_DateTime started) {
    if(failure)
        c_failed++;
    else
        c_passed++;
    long long ms = (long long)((UA_DateTime_nowMonotonic() - started) / UA_DATETIME_MSEC);
    printf("RESULT {\"check\":");
    jsonString(stdout, name);
    printf(",\"outcome\":\"%s\",\"message\":", failure ? "Failed" : "Passed");
    jsonString(stdout, failure ? failure : "");
    printf(",\"milliseconds\":%lld}\n", ms);
    fflush(stdout);
}

static UA_NodeId refId(const char *name) {
    return UA_NODEID_STRING(c_ns, (char *)name);
}

/* Reads one attribute of one node; the caller clears the DataValue. */
static UA_DataValue readAttribute(UA_NodeId nodeId, UA_AttributeId attributeId) {
    UA_ReadValueId rv;
    UA_ReadValueId_init(&rv);
    rv.nodeId = nodeId;
    rv.attributeId = attributeId;
    UA_DataValue dv = UA_Client_read(c_client, &rv);
    return dv;
}

static UA_StatusCode writeValue(UA_NodeId nodeId, const UA_Variant *value, UA_StatusCode *serviceResult) {
    UA_WriteValue wv;
    UA_WriteValue_init(&wv);
    wv.nodeId = nodeId;
    wv.attributeId = UA_ATTRIBUTEID_VALUE;
    wv.value.value = *value;
    wv.value.hasValue = true;
    UA_WriteRequest request;
    UA_WriteRequest_init(&request);
    request.nodesToWrite = &wv;
    request.nodesToWriteSize = 1;
    UA_WriteResponse response = UA_Client_Service_write(c_client, request);
    *serviceResult = response.responseHeader.serviceResult;
    UA_StatusCode result = response.resultsSize == 1 ? response.results[0] : response.responseHeader.serviceResult;
    UA_WriteResponse_clear(&response);
    return result;
}

static UA_BrowseDescription browseAll(UA_NodeId nodeId) {
    UA_BrowseDescription bd;
    UA_BrowseDescription_init(&bd);
    bd.nodeId = nodeId;
    bd.browseDirection = UA_BROWSEDIRECTION_FORWARD;
    bd.referenceTypeId = UA_NODEID_NUMERIC(0, UA_NS0ID_HIERARCHICALREFERENCES);
    bd.includeSubtypes = true;
    bd.resultMask = UA_BROWSERESULTMASK_ALL;
    return bd;
}

/* Browses one node following continuation points; returns the references
 * (caller frees with UA_Array_delete) and their count in *count. */
static UA_ReferenceDescription *browse(UA_NodeId nodeId, UA_UInt32 maxReferences, size_t *count,
                                       UA_StatusCode *status) {
    UA_BrowseRequest request;
    UA_BrowseRequest_init(&request);
    UA_BrowseDescription bd = browseAll(nodeId);
    request.nodesToBrowse = &bd;
    request.nodesToBrowseSize = 1;
    request.requestedMaxReferencesPerNode = maxReferences;
    UA_BrowseResponse response = UA_Client_Service_browse(c_client, request);
    *count = 0;
    *status = response.responseHeader.serviceResult;
    if(*status != UA_STATUSCODE_GOOD || response.resultsSize != 1 || response.results[0].statusCode != UA_STATUSCODE_GOOD) {
        if(*status == UA_STATUSCODE_GOOD && response.resultsSize == 1)
            *status = response.results[0].statusCode;
        UA_BrowseResponse_clear(&response);
        return NULL;
    }
    UA_ReferenceDescription *all = NULL;
    for(size_t i = 0; i < response.results[0].referencesSize; i++)
        UA_Array_append((void **)&all, count, &response.results[0].references[i], &UA_TYPES[UA_TYPES_REFERENCEDESCRIPTION]);
    UA_ByteString cp = UA_BYTESTRING_NULL;
    UA_ByteString_copy(&response.results[0].continuationPoint, &cp);
    UA_BrowseResponse_clear(&response);
    while(cp.length > 0) {
        UA_BrowseNextRequest next;
        UA_BrowseNextRequest_init(&next);
        next.continuationPoints = &cp;
        next.continuationPointsSize = 1;
        UA_BrowseNextResponse nr = UA_Client_Service_browseNext(c_client, next);
        UA_ByteString_clear(&cp);
        if(nr.responseHeader.serviceResult != UA_STATUSCODE_GOOD || nr.resultsSize != 1 ||
           nr.results[0].statusCode != UA_STATUSCODE_GOOD) {
            *status = nr.responseHeader.serviceResult != UA_STATUSCODE_GOOD ? nr.responseHeader.serviceResult
                      : nr.resultsSize == 1 ? nr.results[0].statusCode : UA_STATUSCODE_BADUNEXPECTEDERROR;
            UA_BrowseNextResponse_clear(&nr);
            UA_Array_delete(all, *count, &UA_TYPES[UA_TYPES_REFERENCEDESCRIPTION]);
            *count = 0;
            return NULL;
        }
        for(size_t i = 0; i < nr.results[0].referencesSize; i++)
            UA_Array_append((void **)&all, count, &nr.results[0].references[i], &UA_TYPES[UA_TYPES_REFERENCEDESCRIPTION]);
        UA_ByteString_copy(&nr.results[0].continuationPoint, &cp);
        UA_BrowseNextResponse_clear(&nr);
    }
    return all;
}

/* ------------------------------------------------------------------ checks */

static const char *checkNamespaceArray(void) {
    REQUIRE(c_ns > 0, "reference server namespace is missing");
    return NULL;
}

static const char *checkServerStatus(void) {
    UA_DataValue dv = readAttribute(UA_NODEID_NUMERIC(0, UA_NS0ID_SERVER_SERVERSTATUS), UA_ATTRIBUTEID_VALUE);
    const char *failure = NULL;
    if(dv.status != UA_STATUSCODE_GOOD) {
        snprintf(c_msg, sizeof(c_msg), "status %s", UA_StatusCode_name(dv.status));
        failure = c_msg;
    } else if(!UA_Variant_hasScalarType(&dv.value, &UA_TYPES[UA_TYPES_SERVERSTATUSDATATYPE])) {
        snprintf(c_msg, sizeof(c_msg), "ServerStatus did not decode to ServerStatusDataType");
        failure = c_msg;
    } else {
        UA_ServerStatusDataType *s = (UA_ServerStatusDataType *)dv.value.data;
        if(s->state != UA_SERVERSTATE_RUNNING) {
            snprintf(c_msg, sizeof(c_msg), "server state %d", (int)s->state);
            failure = c_msg;
        } else if(s->buildInfo.productUri.length == 0) {
            snprintf(c_msg, sizeof(c_msg), "BuildInfo is empty");
            failure = c_msg;
        }
    }
    UA_DataValue_clear(&dv);
    return failure;
}

static int hasReference(UA_ReferenceDescription *refs, size_t count, const char *name) {
    UA_String n = UA_STRING((char *)name);
    for(size_t i = 0; i < count; i++)
        if(UA_String_equal(&refs[i].browseName.name, &n))
            return 1;
    return 0;
}

static const char *checkBrowseObjectsFolder(void) {
    size_t count;
    UA_StatusCode status;
    UA_ReferenceDescription *refs = browse(UA_NODEID_NUMERIC(0, UA_NS0ID_OBJECTSFOLDER), 0, &count, &status);
    REQUIRE(refs, "browse returned %s", UA_StatusCode_name(status));
    int server = hasReference(refs, count, "Server"), ctt = hasReference(refs, count, "CTT");
    UA_Array_delete(refs, count, &UA_TYPES[UA_TYPES_REFERENCEDESCRIPTION]);
    REQUIRE(server, "Server object not found");
    REQUIRE(ctt, "CTT folder not found");
    return NULL;
}

static const char *checkTranslateBrowsePath(void) {
    const char *names[3] = {"Server", "ServerStatus", "CurrentTime"};
    UA_RelativePathElement elements[3];
    for(int i = 0; i < 3; i++) {
        UA_RelativePathElement_init(&elements[i]);
        elements[i].referenceTypeId = UA_NODEID_NUMERIC(0, UA_NS0ID_HIERARCHICALREFERENCES);
        elements[i].includeSubtypes = true;
        elements[i].targetName = UA_QUALIFIEDNAME(0, (char *)names[i]);
    }
    UA_BrowsePath path;
    UA_BrowsePath_init(&path);
    path.startingNode = UA_NODEID_NUMERIC(0, UA_NS0ID_OBJECTSFOLDER);
    path.relativePath.elements = elements;
    path.relativePath.elementsSize = 3;
    UA_TranslateBrowsePathsToNodeIdsRequest request;
    UA_TranslateBrowsePathsToNodeIdsRequest_init(&request);
    request.browsePaths = &path;
    request.browsePathsSize = 1;
    UA_TranslateBrowsePathsToNodeIdsResponse response =
        UA_Client_Service_translateBrowsePathsToNodeIds(c_client, request);
    UA_StatusCode status = response.resultsSize == 1 ? response.results[0].statusCode : response.responseHeader.serviceResult;
    UA_NodeId expected = UA_NODEID_NUMERIC(0, UA_NS0ID_SERVER_SERVERSTATUS_CURRENTTIME);
    int ok = status == UA_STATUSCODE_GOOD && response.results[0].targetsSize > 0 &&
             UA_NodeId_equal(&response.results[0].targets[0].targetId.nodeId, &expected);
    UA_TranslateBrowsePathsToNodeIdsResponse_clear(&response);
    REQUIRE(ok, "translate returned %s or an unexpected target", UA_StatusCode_name(status));
    return NULL;
}

static const char *checkReadScalars(void) {
    const char *names[12] = {"Scalar_Static_Boolean", "Scalar_Static_Int32", "Scalar_Static_Double",
                             "Scalar_Static_String", "Scalar_Static_DateTime", "Scalar_Static_Guid",
                             "Scalar_Static_ByteString", "Scalar_Static_LocalizedText",
                             "Scalar_Static_QualifiedName", "Scalar_Static_NodeId",
                             "Scalar_Static_Arrays_Int32", "Scalar_Static_Arrays_String"};
    UA_ReadValueId ids[12];
    for(int i = 0; i < 12; i++) {
        UA_ReadValueId_init(&ids[i]);
        ids[i].nodeId = refId(names[i]);
        ids[i].attributeId = UA_ATTRIBUTEID_VALUE;
    }
    UA_ReadRequest request;
    UA_ReadRequest_init(&request);
    request.nodesToRead = ids;
    request.nodesToReadSize = 12;
    request.timestampsToReturn = UA_TIMESTAMPSTORETURN_BOTH;
    UA_ReadResponse response = UA_Client_Service_read(c_client, request);
    const char *failure = NULL;
    if(response.resultsSize != 12) {
        snprintf(c_msg, sizeof(c_msg), "result count %u (%s)", (unsigned)response.resultsSize,
                 UA_StatusCode_name(response.responseHeader.serviceResult));
        failure = c_msg;
    } else {
        for(int i = 0; i < 12 && !failure; i++) {
            if(response.results[i].status != UA_STATUSCODE_GOOD) {
                snprintf(c_msg, sizeof(c_msg), "%s: %s", names[i], UA_StatusCode_name(response.results[i].status));
                failure = c_msg;
            }
        }
    }
    UA_ReadResponse_clear(&response);
    return failure;
}

static const char *checkReadAttributes(void) {
    UA_NodeId nodeId = refId("Scalar_Static_Int32");
    UA_NodeClass nodeClass;
    UA_QualifiedName browseName;
    UA_NodeId dataType;
    REQUIRE(UA_Client_readNodeClassAttribute(c_client, nodeId, &nodeClass) == UA_STATUSCODE_GOOD &&
            nodeClass == UA_NODECLASS_VARIABLE, "NodeClass %d", (int)nodeClass);
    REQUIRE(UA_Client_readBrowseNameAttribute(c_client, nodeId, &browseName) == UA_STATUSCODE_GOOD, "BrowseName");
    UA_String expectedName = UA_STRING("Scalar_Static_Int32");
    int nameOk = UA_String_equal(&browseName.name, &expectedName);
    UA_QualifiedName_clear(&browseName);
    REQUIRE(nameOk, "BrowseName differs");
    REQUIRE(UA_Client_readDataTypeAttribute(c_client, nodeId, &dataType) == UA_STATUSCODE_GOOD, "DataType");
    UA_NodeId int32 = UA_TYPES[UA_TYPES_INT32].typeId;
    int typeOk = UA_NodeId_equal(&dataType, &int32);
    UA_NodeId_clear(&dataType);
    REQUIRE(typeOk, "DataType is not Int32");
    return NULL;
}

static const char *checkWriteAndReadBack(void) {
    UA_Int32 i32 = 1234567;
    UA_String text = UA_STRING("written by open62541 \xC3\xA4\xC3\xB6\xC3\xBC");
    UA_Double doubles[3] = {1.5, -2.25, 1e300};
    UA_Variant v;
    UA_Variant_setScalar(&v, &i32, &UA_TYPES[UA_TYPES_INT32]);
    REQUIRE(UA_Client_writeValueAttribute(c_client, refId("Scalar_Static_Int32"), &v) == UA_STATUSCODE_GOOD, "Int32 write");
    UA_Variant_setScalar(&v, &text, &UA_TYPES[UA_TYPES_STRING]);
    REQUIRE(UA_Client_writeValueAttribute(c_client, refId("Scalar_Static_String"), &v) == UA_STATUSCODE_GOOD, "String write");
    UA_Variant_setArray(&v, doubles, 3, &UA_TYPES[UA_TYPES_DOUBLE]);
    REQUIRE(UA_Client_writeValueAttribute(c_client, refId("Scalar_Static_Arrays_Double"), &v) == UA_STATUSCODE_GOOD, "Double[] write");

    UA_Variant r;
    UA_Variant_init(&r);
    UA_Client_readValueAttribute(c_client, refId("Scalar_Static_Int32"), &r);
    int ok = UA_Variant_hasScalarType(&r, &UA_TYPES[UA_TYPES_INT32]) && *(UA_Int32 *)r.data == i32;
    UA_Variant_clear(&r);
    REQUIRE(ok, "Int32 read back differs");
    UA_Client_readValueAttribute(c_client, refId("Scalar_Static_String"), &r);
    ok = UA_Variant_hasScalarType(&r, &UA_TYPES[UA_TYPES_STRING]) && UA_String_equal((UA_String *)r.data, &text);
    UA_Variant_clear(&r);
    REQUIRE(ok, "String read back differs");
    UA_Client_readValueAttribute(c_client, refId("Scalar_Static_Arrays_Double"), &r);
    ok = UA_Variant_hasArrayType(&r, &UA_TYPES[UA_TYPES_DOUBLE]) && r.arrayLength == 3 &&
         memcmp(r.data, doubles, sizeof(doubles)) == 0;
    UA_Variant_clear(&r);
    REQUIRE(ok, "Double[] read back differs");
    return NULL;
}

static const char *checkCallMethods(void) {
    UA_Variant in[2];
    UA_String who = UA_STRING("open62541");
    UA_Variant_setScalar(&in[0], &who, &UA_TYPES[UA_TYPES_STRING]);
    size_t outSize = 0;
    UA_Variant *out = NULL;
    UA_StatusCode res = UA_Client_call(c_client, refId("Methods"), refId("Methods_Hello"), 1, in, &outSize, &out);
    REQUIRE(res == UA_STATUSCODE_GOOD, "Hello %s", UA_StatusCode_name(res));
    UA_String expected = UA_STRING("hello open62541");
    int ok = outSize == 1 && UA_Variant_hasScalarType(&out[0], &UA_TYPES[UA_TYPES_STRING]) &&
             UA_String_equal((UA_String *)out[0].data, &expected);
    UA_Array_delete(out, outSize, &UA_TYPES[UA_TYPES_VARIANT]);
    REQUIRE(ok, "Hello returned an unexpected value");

    UA_Float a = 1.5f;
    UA_UInt32 b = 2;
    UA_Variant_setScalar(&in[0], &a, &UA_TYPES[UA_TYPES_FLOAT]);
    UA_Variant_setScalar(&in[1], &b, &UA_TYPES[UA_TYPES_UINT32]);
    res = UA_Client_call(c_client, refId("Methods"), refId("Methods_Add"), 2, in, &outSize, &out);
    REQUIRE(res == UA_STATUSCODE_GOOD, "Add %s", UA_StatusCode_name(res));
    ok = outSize == 1 && UA_Variant_hasScalarType(&out[0], &UA_TYPES[UA_TYPES_FLOAT]) &&
         *(UA_Float *)out[0].data == 3.5f;
    UA_Array_delete(out, outSize, &UA_TYPES[UA_TYPES_VARIANT]);
    REQUIRE(ok, "Add returned an unexpected value");
    return NULL;
}

static int s_notifications;

static void onDataChange(UA_Client *client, UA_UInt32 subId, void *subContext, UA_UInt32 monId,
                         void *monContext, UA_DataValue *value) {
    (void)client; (void)subId; (void)subContext; (void)monId; (void)monContext; (void)value;
    s_notifications++;
}

static const char *checkSubscription(void) {
    UA_CreateSubscriptionRequest request = UA_CreateSubscriptionRequest_default();
    request.requestedPublishingInterval = 100;
    request.requestedMaxKeepAliveCount = 10;
    request.requestedLifetimeCount = 100;
    UA_CreateSubscriptionResponse sub = UA_Client_Subscriptions_create(c_client, request, NULL, NULL, NULL);
    REQUIRE(sub.responseHeader.serviceResult == UA_STATUSCODE_GOOD, "CreateSubscription %s",
            UA_StatusCode_name(sub.responseHeader.serviceResult));
    UA_UInt32 subId = sub.subscriptionId;
    UA_MonitoredItemCreateRequest item =
        UA_MonitoredItemCreateRequest_default(UA_NODEID_NUMERIC(0, UA_NS0ID_SERVER_SERVERSTATUS_CURRENTTIME));
    item.requestedParameters.samplingInterval = 100;
    item.requestedParameters.queueSize = 10;
    s_notifications = 0;
    UA_MonitoredItemCreateResult created = UA_Client_MonitoredItems_createDataChange(
        c_client, subId, UA_TIMESTAMPSTORETURN_BOTH, item, NULL, onDataChange, NULL);
    UA_StatusCode itemStatus = created.statusCode;
    UA_MonitoredItemCreateResult_clear(&created);
    UA_DateTime deadline = UA_DateTime_nowMonotonic() + 15 * UA_DATETIME_SEC;
    while(itemStatus == UA_STATUSCODE_GOOD && s_notifications < 3 && UA_DateTime_nowMonotonic() < deadline)
        UA_Client_run_iterate(c_client, 100);
    UA_Client_Subscriptions_deleteSingle(c_client, subId);
    REQUIRE(itemStatus == UA_STATUSCODE_GOOD, "monitored item %s", UA_StatusCode_name(itemStatus));
    REQUIRE(s_notifications >= 3, "only %d data change notifications in 15 s", s_notifications);
    return NULL;
}

/* Loads the server's DataTypeDefinitions, decodes every variable below Objects
 * whose data type is a custom type, and writes up to 50 structures back. */
static const char *checkComplexTypes(void) {
    UA_DataTypeArray *custom = NULL;
    UA_StatusCode res = UA_Client_getRemoteDataTypes(c_client, 0, NULL, &custom);
    REQUIRE(res == UA_STATUSCODE_GOOD && custom, "loading the remote data types failed: %s", UA_StatusCode_name(res));
    UA_ClientConfig *config = UA_Client_getConfig(c_client);
    custom->next = config->customDataTypes;
    config->customDataTypes = custom;
    const char *required[3] = {"ScalarStructureDataType", "VectorUnion", "VectorWithOptionalFields"};
    for(int r = 0; r < 3; r++) {
        int found = 0;
        for(size_t i = 0; i < custom->typesSize && !found; i++)
            found = custom->types[i].typeName && strcmp(custom->types[i].typeName, required[r]) == 0;
        REQUIRE(found, "type %s was not created; created %u types", required[r], (unsigned)custom->typesSize);
    }

    /* Breadth-first browse below Objects (without the Server object). */
    size_t queueSize = 1, head = 0, varCount = 0;
    UA_NodeId *queue = (UA_NodeId *)UA_Array_new(1, &UA_TYPES[UA_TYPES_NODEID]);
    queue[0] = UA_NODEID_NUMERIC(0, UA_NS0ID_OBJECTSFOLDER);
    UA_NodeId *vars = NULL;
    UA_NodeId server = UA_NODEID_NUMERIC(0, UA_NS0ID_SERVER);
    while(head < queueSize && queueSize < 30000) {
        UA_NodeId current = queue[head++];
        if(UA_NodeId_equal(&current, &server))
            continue;
        size_t count;
        UA_StatusCode status;
        UA_ReferenceDescription *refs = browse(current, 0, &count, &status);
        for(size_t i = 0; refs && i < count; i++) {
            if(refs[i].nodeId.serverIndex != 0 || refs[i].nodeId.namespaceUri.length > 0)
                continue;
            int seen = 0;
            for(size_t q = 0; q < queueSize && !seen; q++)
                seen = UA_NodeId_equal(&queue[q], &refs[i].nodeId.nodeId);
            if(seen)
                continue;
            UA_Array_append((void **)&queue, &queueSize, &refs[i].nodeId.nodeId, &UA_TYPES[UA_TYPES_NODEID]);
            if(refs[i].nodeClass == UA_NODECLASS_VARIABLE && refs[i].nodeId.nodeId.namespaceIndex != 0)
                UA_Array_append((void **)&vars, &varCount, &refs[i].nodeId.nodeId, &UA_TYPES[UA_TYPES_NODEID]);
        }
        if(refs)
            UA_Array_delete(refs, count, &UA_TYPES[UA_TYPES_REFERENCEDESCRIPTION]);
    }
    UA_Array_delete(queue, queueSize, &UA_TYPES[UA_TYPES_NODEID]);

    int customCount = 0, structures = 0, undecoded = 0, written = 0, rejected = 0, changed = 0;
    char firstUndecoded[512] = "", firstProblem[512] = "";
    for(size_t v = 0; v < varCount; v++) {
        UA_NodeId dataType;
        if(UA_Client_readDataTypeAttribute(c_client, vars[v], &dataType) != UA_STATUSCODE_GOOD)
            continue;
        UA_UInt16 typeNs = dataType.namespaceIndex;
        UA_NodeId_clear(&dataType);
        if(typeNs == 0)
            continue;
        customCount++;
        UA_DataValue dv = readAttribute(vars[v], UA_ATTRIBUTEID_VALUE);
        UA_Byte access = 0;
        UA_Client_readUserAccessLevelAttribute(c_client, vars[v], &access);
        if(dv.status == UA_STATUSCODE_GOOD && dv.value.type && dv.value.type->typeKind >= UA_DATATYPEKIND_STRUCTURE &&
           dv.value.type->typeKind != UA_DATATYPEKIND_ENUM) {
            size_t n = UA_Variant_isScalar(&dv.value) ? 1 : dv.value.arrayLength;
            structures += (int)n;
            if(dv.value.type == &UA_TYPES[UA_TYPES_EXTENSIONOBJECT]) {
                undecoded += (int)n;
                if(!firstUndecoded[0]) {
                    UA_String id = UA_STRING_NULL;
                    UA_NodeId_print(&vars[v], &id);
                    snprintf(firstUndecoded, sizeof(firstUndecoded), "%.*s", (int)id.length, (char *)id.data);
                    UA_String_clear(&id);
                }
            } else if(written < 50 && (access & UA_ACCESSLEVELMASK_WRITE)) {
                UA_StatusCode serviceResult;
                UA_StatusCode wr = writeValue(vars[v], &dv.value, &serviceResult);
                written++;
                if(wr != UA_STATUSCODE_GOOD) {
                    rejected++;
                    if(!firstProblem[0])
                        snprintf(firstProblem, sizeof(firstProblem), "write of a %s returned %s",
                                 dv.value.type->typeName, UA_StatusCode_name(wr));
                } else {
                    UA_DataValue back = readAttribute(vars[v], UA_ATTRIBUTEID_VALUE);
                    if(!UA_Variant_equal(&back.value, &dv.value)) {
                        changed++;
                        if(!firstProblem[0])
                            snprintf(firstProblem, sizeof(firstProblem), "a %s changed in a write and read round trip",
                                     dv.value.type->typeName);
                    }
                    UA_DataValue_clear(&back);
                }
            }
        }
        UA_DataValue_clear(&dv);
    }
    UA_Array_delete(vars, varCount, &UA_TYPES[UA_TYPES_NODEID]);
    REQUIRE(customCount >= 10, "only %d variables with a custom data type found", customCount);
    REQUIRE(structures > 0, "no structure values were read");
    REQUIRE(undecoded == 0, "%d of %d structures were not decoded, e.g. %s", undecoded, structures, firstUndecoded);
    REQUIRE(written > 0, "no writable structure variable found");
    REQUIRE(rejected == 0 && changed == 0, "%d of %d writes rejected, %d changed: %s", rejected, written, changed,
            firstProblem);
    return NULL;
}

static const char *writeAndCompare(UA_NodeId nodeId, const UA_Variant *value) {
    UA_StatusCode serviceResult;
    UA_StatusCode wr = writeValue(nodeId, value, &serviceResult);
    REQUIRE(wr == UA_STATUSCODE_GOOD, "write returned %s", UA_StatusCode_name(wr));
    UA_DataValue dv = readAttribute(nodeId, UA_ATTRIBUTEID_VALUE);
    int ok = dv.status == UA_STATUSCODE_GOOD && UA_Variant_equal(&dv.value, value);
    UA_StatusCode status = dv.status;
    UA_DataValue_clear(&dv);
    REQUIRE(ok, "read back a different value (%s)", UA_StatusCode_name(status));
    return NULL;
}

static const char *checkLargeArray(void) {
    /* 200 000 Int32 = 800 kB, far more than one 64 kB message chunk. */
    size_t n = 200000;
    UA_Int32 *values = (UA_Int32 *)UA_malloc(n * sizeof(UA_Int32));
    for(size_t i = 0; i < n; i++)
        values[i] = (UA_Int32)((i * 7919) ^ 0x5A5A);
    UA_Variant v;
    UA_Variant_setArray(&v, values, n, &UA_TYPES[UA_TYPES_INT32]);
    const char *failure = writeAndCompare(refId("Scalar_Static_Arrays_Int32"), &v);
    UA_free(values);
    return failure;
}

static const char *checkLargeByteString(void) {
    /* 3 MB, below the 4 MB ByteString limit of the reference server fixture. */
    UA_ByteString bs;
    UA_ByteString_allocBuffer(&bs, 3 * 1024 * 1024);
    UA_UInt32 x = 4711;
    for(size_t i = 0; i < bs.length; i++) {
        x = x * 1103515245u + 12345u;
        bs.data[i] = (UA_Byte)(x >> 24);
    }
    UA_Variant v;
    UA_Variant_setScalar(&v, &bs, &UA_TYPES[UA_TYPES_BYTESTRING]);
    const char *failure = writeAndCompare(refId("Scalar_Static_ByteString"), &v);
    UA_ByteString_clear(&bs);
    return failure;
}

/* A ByteString above the server's MaxByteStringLength but inside its
 * MaxMessageSize must be rejected with a status code; the session stays usable. */
static const char *checkOversizedRequest(void) {
    UA_ByteString bs;
    UA_ByteString_allocBuffer(&bs, 5 * 1024 * 1024);
    memset(bs.data, 0, bs.length);
    UA_Variant v;
    UA_Variant_setScalar(&v, &bs, &UA_TYPES[UA_TYPES_BYTESTRING]);
    UA_StatusCode serviceResult;
    UA_StatusCode wr = writeValue(refId("Scalar_Static_ByteString"), &v, &serviceResult);
    UA_ByteString_clear(&bs);
    REQUIRE(wr != UA_STATUSCODE_GOOD, "a 5 MB ByteString write was accepted");
    UA_DataValue state = readAttribute(UA_NODEID_NUMERIC(0, UA_NS0ID_SERVER_SERVERSTATUS_STATE), UA_ATTRIBUTEID_VALUE);
    UA_StatusCode status = state.status;
    UA_DataValue_clear(&state);
    REQUIRE(status == UA_STATUSCODE_GOOD, "the session is unusable after the rejected write (%s): %s",
            UA_StatusCode_name(wr), UA_StatusCode_name(status));
    printf("INFO OversizedRequestRejected: %s\n", UA_StatusCode_name(wr));
    return NULL;
}

/* Browses a folder three references at a time with BrowseNext, compares with
 * a browse without limit, then releases an open continuation point. */
static const char *checkBrowseContinuationPoints(void) {
    UA_NodeId folder = refId("Scalar_Static");
    size_t allCount, pagedCount;
    UA_StatusCode status;
    UA_ReferenceDescription *all = browse(folder, 0, &allCount, &status);
    REQUIRE(all, "browse returned %s", UA_StatusCode_name(status));
    UA_ReferenceDescription *paged = browse(folder, 3, &pagedCount, &status);
    int same = paged && pagedCount == allCount;
    for(size_t i = 0; same && i < allCount; i++)
        same = UA_NodeId_equal(&all[i].nodeId.nodeId, &paged[i].nodeId.nodeId);
    UA_Array_delete(all, allCount, &UA_TYPES[UA_TYPES_REFERENCEDESCRIPTION]);
    if(paged)
        UA_Array_delete(paged, pagedCount, &UA_TYPES[UA_TYPES_REFERENCEDESCRIPTION]);
    REQUIRE(allCount > 10, "only %u references below the folder", (unsigned)allCount);
    REQUIRE(same, "paged browse returned %u of %u references or a different order (%s)",
            (unsigned)pagedCount, (unsigned)allCount, UA_StatusCode_name(status));

    UA_BrowseRequest request;
    UA_BrowseRequest_init(&request);
    UA_BrowseDescription bd = browseAll(folder);
    request.nodesToBrowse = &bd;
    request.nodesToBrowseSize = 1;
    request.requestedMaxReferencesPerNode = 3;
    UA_BrowseResponse first = UA_Client_Service_browse(c_client, request);
    UA_ByteString cp = UA_BYTESTRING_NULL;
    if(first.resultsSize == 1)
        UA_ByteString_copy(&first.results[0].continuationPoint, &cp);
    UA_BrowseResponse_clear(&first);
    REQUIRE(cp.length > 0, "no continuation point returned");
    UA_BrowseNextRequest next;
    UA_BrowseNextRequest_init(&next);
    next.continuationPoints = &cp;
    next.continuationPointsSize = 1;
    next.releaseContinuationPoints = true;
    UA_BrowseNextResponse released = UA_Client_Service_browseNext(c_client, next);
    UA_StatusCode releasedStatus = released.resultsSize == 1 ? released.results[0].statusCode : released.responseHeader.serviceResult;
    UA_BrowseNextResponse_clear(&released);
    next.releaseContinuationPoints = false;
    UA_BrowseNextResponse reused = UA_Client_Service_browseNext(c_client, next);
    UA_StatusCode reusedStatus = reused.resultsSize == 1 ? reused.results[0].statusCode : reused.responseHeader.serviceResult;
    UA_BrowseNextResponse_clear(&reused);
    UA_ByteString_clear(&cp);
    REQUIRE(releasedStatus == UA_STATUSCODE_GOOD, "releasing the continuation point returned %s",
            UA_StatusCode_name(releasedStatus));
    REQUIRE(reusedStatus == UA_STATUSCODE_BADCONTINUATIONPOINTINVALID, "a released continuation point returned %s",
            UA_StatusCode_name(reusedStatus));
    return NULL;
}

/* Reads 2 * MaxNodesPerRead + 1 nodes split by the server's limit, then in one
 * request, which the server must reject with BadTooManyOperations. */
static const char *checkReadManyNodes(void) {
    UA_Variant v;
    UA_Variant_init(&v);
    UA_Client_readValueAttribute(c_client, UA_NODEID_NUMERIC(0, UA_NS0ID_SERVER_SERVERCAPABILITIES_OPERATIONLIMITS_MAXNODESPERREAD), &v);
    UA_UInt32 limit = UA_Variant_hasScalarType(&v, &UA_TYPES[UA_TYPES_UINT32]) ? *(UA_UInt32 *)v.data : 0;
    UA_Variant_clear(&v);
    REQUIRE(limit > 0 && limit < 100000, "the server published no usable MaxNodesPerRead (%u)", (unsigned)limit);
    size_t n = (size_t)limit * 2 + 1;
    UA_ReadValueId *ids = (UA_ReadValueId *)UA_Array_new(n, &UA_TYPES[UA_TYPES_READVALUEID]);
    for(size_t i = 0; i < n; i++) {
        ids[i].nodeId = UA_NODEID_NUMERIC(0, UA_NS0ID_SERVER_SERVERSTATUS_STATE);
        ids[i].attributeId = UA_ATTRIBUTEID_VALUE;
    }
    size_t good = 0;
    for(size_t offset = 0; offset < n; offset += limit) {
        UA_ReadRequest request;
        UA_ReadRequest_init(&request);
        request.nodesToRead = ids + offset;
        request.nodesToReadSize = n - offset < limit ? n - offset : limit;
        UA_ReadResponse response = UA_Client_Service_read(c_client, request);
        for(size_t i = 0; i < response.resultsSize; i++)
            good += response.results[i].status == UA_STATUSCODE_GOOD;
        UA_ReadResponse_clear(&response);
    }
    UA_ReadRequest request;
    UA_ReadRequest_init(&request);
    request.nodesToRead = ids;
    request.nodesToReadSize = n;
    UA_ReadResponse response = UA_Client_Service_read(c_client, request);
    UA_StatusCode tooMany = response.responseHeader.serviceResult;
    UA_ReadResponse_clear(&response);
    UA_Array_delete(ids, n, &UA_TYPES[UA_TYPES_READVALUEID]);
    REQUIRE(good == n, "%u of %u reads split by MaxNodesPerRead %u succeeded", (unsigned)good, (unsigned)n, (unsigned)limit);
    REQUIRE(tooMany == UA_STATUSCODE_BADTOOMANYOPERATIONS, "reading %u nodes in one request returned %s",
            (unsigned)n, UA_StatusCode_name(tooMany));
    return NULL;
}

/* Keeps reading for longer than the revised token lifetime (renewal at 75 %);
 * with --min-renewals the SecureChannel must have been renewed that often. */
static const char *checkTokenRenewal(void) {
    int seconds = atoi(opt("token-test-seconds", "65"));
    int minRenewals = atoi(opt("min-renewals", "0"));
    UA_DateTime end = UA_DateTime_nowMonotonic() + (UA_DateTime)seconds * UA_DATETIME_SEC;
    int reads = 0;
    s_renewals = 0;
    while(UA_DateTime_nowMonotonic() < end) {
        UA_DataValue dv = readAttribute(UA_NODEID_NUMERIC(0, UA_NS0ID_SERVER_SERVERSTATUS_CURRENTTIME), UA_ATTRIBUTEID_VALUE);
        UA_StatusCode status = dv.status;
        UA_DataValue_clear(&dv);
        REQUIRE(status == UA_STATUSCODE_GOOD, "read %d after %d renewals returned %s", reads, s_renewals,
                UA_StatusCode_name(status));
        reads++;
        UA_Client_run_iterate(c_client, 500);
    }
    printf("INFO %d reads, %d SecureChannel renewals\n", reads, s_renewals);
    REQUIRE(s_renewals >= minRenewals, "the SecureChannel was renewed %d times, expected at least %d", s_renewals,
            minRenewals);
    return NULL;
}

/* ------------------------------------------------------------------ feature helpers */

#define ALARMS_NAMESPACE "http://test.org/UA/Alarms/"

/* Lets the client process publish responses for about ms milliseconds. */
static void pump(UA_Client *client, int ms) {
    UA_DateTime end = UA_DateTime_nowMonotonic() + (UA_DateTime)ms * UA_DATETIME_MSEC;
    while(UA_DateTime_nowMonotonic() < end)
        UA_Client_run_iterate(client, 20);
}

/* A synchronous call of any service through the public generic async API
 * (open62541 has no dedicated client API for Republish and TransferSubscriptions). */
typedef struct {
    int done;
    void *response;
    const UA_DataType *type;
} SyncCall;

static void syncCallDone(UA_Client *client, void *userdata, UA_UInt32 requestId, void *response) {
    (void)client; (void)requestId;
    SyncCall *call = (SyncCall *)userdata;
    if(response)
        UA_copy(response, call->response, call->type);
    call->done = 1;
}

static void callService(UA_Client *client, const void *request, const UA_DataType *requestType,
                        void *response, const UA_DataType *responseType) {
    SyncCall call = {0, response, responseType};
    UA_init(response, responseType);
    UA_StatusCode res = __UA_Client_AsyncService(client, request, requestType, syncCallDone, responseType,
                                                 &call, NULL);
    UA_DateTime end = UA_DateTime_nowMonotonic() + 60 * UA_DATETIME_SEC;
    while(res == UA_STATUSCODE_GOOD && !call.done && UA_DateTime_nowMonotonic() < end)
        UA_Client_run_iterate(client, 20);
    if(res == UA_STATUSCODE_GOOD && !call.done)
        res = UA_STATUSCODE_BADTIMEOUT;
    if(res != UA_STATUSCODE_GOOD)
        ((UA_ResponseHeader *)response)->serviceResult = res;
}

static UA_StatusCode callMethod(UA_NodeId objectId, UA_NodeId methodId, size_t inputSize, const UA_Variant *input) {
    size_t outSize = 0;
    UA_Variant *out = NULL;
    UA_StatusCode res = UA_Client_call(c_client, objectId, methodId, inputSize, input, &outSize, &out);
    UA_Array_delete(out, outSize, &UA_TYPES[UA_TYPES_VARIANT]);
    return res;
}

static UA_StatusCode writeScalar(UA_NodeId nodeId, const void *value, const UA_DataType *type) {
    UA_Variant v;
    UA_Variant_setScalar(&v, (void *)(uintptr_t)value, type);
    UA_StatusCode serviceResult;
    return writeValue(nodeId, &v, &serviceResult);
}

static UA_UInt32 createSubscription(UA_Double publishingInterval, UA_StatusCode *status) {
    UA_CreateSubscriptionRequest request = UA_CreateSubscriptionRequest_default();
    request.requestedPublishingInterval = publishingInterval;
    request.requestedMaxKeepAliveCount = 10;
    request.requestedLifetimeCount = 100;
    UA_CreateSubscriptionResponse response = UA_Client_Subscriptions_create(c_client, request, NULL, NULL, NULL);
    *status = response.responseHeader.serviceResult;
    UA_UInt32 id = response.subscriptionId;
    UA_CreateSubscriptionResponse_clear(&response);
    return id;
}

/* ---- events: the fields of every received event, in select clause order */

#define MAX_EVENTS 512
typedef struct {
    size_t count;
    UA_Variant *fields;
} EventRecord;
static EventRecord s_events[MAX_EVENTS];
static size_t s_eventCount;

static void clearEvents(void) {
    for(size_t i = 0; i < s_eventCount; i++)
        UA_Array_delete(s_events[i].fields, s_events[i].count, &UA_TYPES[UA_TYPES_VARIANT]);
    s_eventCount = 0;
}

static void onEvent(UA_Client *client, UA_UInt32 subId, void *subContext, UA_UInt32 monId, void *monContext,
                    const UA_KeyValueMap eventFields) {
    (void)client; (void)subId; (void)subContext; (void)monId; (void)monContext;
    if(s_eventCount >= MAX_EVENTS)
        return;
    EventRecord *e = &s_events[s_eventCount++];
    e->count = eventFields.mapSize;
    e->fields = (UA_Variant *)UA_Array_new(e->count, &UA_TYPES[UA_TYPES_VARIANT]);
    for(size_t i = 0; i < e->count; i++)
        UA_Variant_copy(&eventFields.map[i].value, &e->fields[i]);
}

typedef int (*EventMatch)(const EventRecord *e);

/* Waits up to seconds for an event that matches; returns its index or -1. */
static int waitForEvent(EventMatch match, int seconds) {
    UA_DateTime end = UA_DateTime_nowMonotonic() + (UA_DateTime)seconds * UA_DATETIME_SEC;
    for(;;) {
        for(size_t i = 0; i < s_eventCount; i++)
            if(match(&s_events[i]))
                return (int)i;
        if(UA_DateTime_nowMonotonic() >= end)
            return -1;
        UA_Client_run_iterate(c_client, 50);
    }
}

static UA_SimpleAttributeOperand selectClause(UA_UInt32 typeId, UA_QualifiedName *path, size_t pathSize,
                                              UA_UInt32 attributeId) {
    UA_SimpleAttributeOperand sao;
    UA_SimpleAttributeOperand_init(&sao);
    sao.typeDefinitionId = UA_NODEID_NUMERIC(0, typeId);
    sao.browsePath = path;
    sao.browsePathSize = pathSize;
    sao.attributeId = attributeId;
    return sao;
}

/* Event fields: 0 EventId, 1 EventType, 2 SourceNode, 3 Time, 4 Message,
 * 5 Severity; with condition also 6 ConditionId, 7 AckedState/Id, 8 Retain. */
static UA_UInt32 createEventSubscription(UA_NodeId notifier, int condition, UA_StatusCode *status) {
    UA_UInt32 subId = createSubscription(100, status);
    if(*status != UA_STATUSCODE_GOOD)
        return 0;
    UA_QualifiedName names[6] = {UA_QUALIFIEDNAME(0, "EventId"), UA_QUALIFIEDNAME(0, "EventType"),
                                 UA_QUALIFIEDNAME(0, "SourceNode"), UA_QUALIFIEDNAME(0, "Time"),
                                 UA_QUALIFIEDNAME(0, "Message"), UA_QUALIFIEDNAME(0, "Severity")};
    UA_QualifiedName acked[2] = {UA_QUALIFIEDNAME(0, "AckedState"), UA_QUALIFIEDNAME(0, "Id")};
    UA_QualifiedName retain = UA_QUALIFIEDNAME(0, "Retain");
    UA_SimpleAttributeOperand select[9];
    size_t selectSize = 6;
    for(size_t i = 0; i < 6; i++)
        select[i] = selectClause(UA_NS0ID_BASEEVENTTYPE, &names[i], 1, UA_ATTRIBUTEID_VALUE);
    if(condition) {
        select[6] = selectClause(UA_NS0ID_CONDITIONTYPE, NULL, 0, UA_ATTRIBUTEID_NODEID);
        select[7] = selectClause(UA_NS0ID_ACKNOWLEDGEABLECONDITIONTYPE, acked, 2, UA_ATTRIBUTEID_VALUE);
        select[8] = selectClause(UA_NS0ID_CONDITIONTYPE, &retain, 1, UA_ATTRIBUTEID_VALUE);
        selectSize = 9;
    }
    UA_LiteralOperand literal;
    UA_LiteralOperand_init(&literal);
    UA_NodeId ofType = UA_NODEID_NUMERIC(0, condition ? UA_NS0ID_ACKNOWLEDGEABLECONDITIONTYPE : UA_NS0ID_BASEEVENTTYPE);
    UA_Variant_setScalar(&literal.value, &ofType, &UA_TYPES[UA_TYPES_NODEID]);
    UA_ExtensionObject operand;
    UA_ExtensionObject_setValue(&operand, &literal, &UA_TYPES[UA_TYPES_LITERALOPERAND]);
    UA_ContentFilterElement element;
    UA_ContentFilterElement_init(&element);
    element.filterOperator = UA_FILTEROPERATOR_OFTYPE;
    element.filterOperands = &operand;
    element.filterOperandsSize = 1;
    UA_EventFilter filter;
    UA_EventFilter_init(&filter);
    filter.selectClauses = select;
    filter.selectClausesSize = selectSize;
    filter.whereClause.elements = &element;
    filter.whereClause.elementsSize = 1;

    UA_MonitoredItemCreateRequest item;
    UA_MonitoredItemCreateRequest_init(&item);
    item.itemToMonitor.nodeId = notifier;
    item.itemToMonitor.attributeId = UA_ATTRIBUTEID_EVENTNOTIFIER;
    item.monitoringMode = UA_MONITORINGMODE_REPORTING;
    item.requestedParameters.samplingInterval = 0;
    item.requestedParameters.queueSize = 100;
    item.requestedParameters.discardOldest = true;
    UA_ExtensionObject_setValue(&item.requestedParameters.filter, &filter, &UA_TYPES[UA_TYPES_EVENTFILTER]);
    clearEvents();
    UA_MonitoredItemCreateResult created = UA_Client_MonitoredItems_createEvent(
        c_client, subId, UA_TIMESTAMPSTORETURN_BOTH, item, NULL, onEvent, NULL);
    *status = created.statusCode;
    UA_MonitoredItemCreateResult_clear(&created);
    if(*status != UA_STATUSCODE_GOOD) {
        UA_Client_Subscriptions_deleteSingle(c_client, subId);
        return 0;
    }
    return subId;
}

static void deleteSubscription(UA_UInt32 subId) {
    if(subId)
        UA_Client_Subscriptions_deleteSingle(c_client, subId);
}

static int fieldIsText(const UA_Variant *v, const char *contains) {
    if(!UA_Variant_hasScalarType(v, &UA_TYPES[UA_TYPES_LOCALIZEDTEXT]))
        return 0;
    const UA_String *text = &((UA_LocalizedText *)v->data)->text;
    size_t n = strlen(contains);
    for(size_t i = 0; i + n <= text->length; i++)
        if(memcmp(text->data + i, contains, n) == 0)
            return 1;
    return 0;
}

static int fieldIsNodeId(const UA_Variant *v, UA_UInt32 numeric) {
    UA_NodeId expected = UA_NODEID_NUMERIC(0, numeric);
    return UA_Variant_hasScalarType(v, &UA_TYPES[UA_TYPES_NODEID]) &&
           UA_NodeId_equal((UA_NodeId *)v->data, &expected);
}

static int fieldIsBoolean(const UA_Variant *v, UA_Boolean value) {
    return UA_Variant_hasScalarType(v, &UA_TYPES[UA_TYPES_BOOLEAN]) && *(UA_Boolean *)v->data == value;
}

static void describeEvent(const EventRecord *e, char *out, size_t outSize) {
    size_t used = 0;
    out[0] = 0;
    for(size_t i = 0; i < e->count && used + 1 < outSize; i++) {
        UA_String s = UA_STRING_NULL;
        UA_print(&e->fields[i], &UA_TYPES[UA_TYPES_VARIANT], &s);
        int n = snprintf(out + used, outSize - used, "%s%.*s", i ? " | " : "", (int)(s.length > 80 ? 80 : s.length),
                         (char *)s.data);
        UA_String_clear(&s);
        if(n < 0)
            break;
        used += (size_t)n;
    }
}

static int isTriggerEvent(const EventRecord *e) {
    return e->count >= 6 && fieldIsText(&e->fields[4], "Trigger event");
}

static int isRefreshEnd(const EventRecord *e) {
    return e->count >= 2 && fieldIsNodeId(&e->fields[1], UA_NS0ID_REFRESHENDEVENTTYPE);
}

static int isRefreshStart(const EventRecord *e) {
    return e->count >= 2 && fieldIsNodeId(&e->fields[1], UA_NS0ID_REFRESHSTARTEVENTTYPE);
}

static int isUnackedCondition(const EventRecord *e) {
    return e->count >= 9 && UA_Variant_hasScalarType(&e->fields[6], &UA_TYPES[UA_TYPES_NODEID]) &&
           fieldIsBoolean(&e->fields[7], false) && fieldIsBoolean(&e->fields[8], true);
}

/* ---- data changes */

#define MAX_VALUES 256
static UA_DataValue s_dataValues[MAX_VALUES];
static int s_valueCount;
static UA_UInt32 s_linkedId;
static int s_linkedReports;

static void clearValues(void) {
    for(int i = 0; i < s_valueCount; i++)
        UA_DataValue_clear(&s_dataValues[i]);
    s_valueCount = 0;
}

static void onValue(UA_Client *client, UA_UInt32 subId, void *subContext, UA_UInt32 monId, void *monContext,
                    UA_DataValue *value) {
    (void)client; (void)subId; (void)subContext; (void)monContext;
    if(s_linkedId && monId == s_linkedId)
        s_linkedReports++;
    if(s_valueCount < MAX_VALUES)
        UA_DataValue_copy(value, &s_dataValues[s_valueCount++]);
}

static UA_StatusCode createDataItem(UA_UInt32 subId, UA_NodeId node, UA_Double samplingInterval, UA_UInt32 queueSize,
                                    UA_MonitoringMode mode, const UA_DataChangeFilter *filter, UA_UInt32 *monId) {
    UA_MonitoredItemCreateRequest item = UA_MonitoredItemCreateRequest_default(node);
    item.monitoringMode = mode;
    item.requestedParameters.samplingInterval = samplingInterval;
    item.requestedParameters.queueSize = queueSize;
    item.requestedParameters.discardOldest = true;
    if(filter)
        UA_ExtensionObject_setValue(&item.requestedParameters.filter, (void *)(uintptr_t)filter,
                                    &UA_TYPES[UA_TYPES_DATACHANGEFILTER]);
    UA_MonitoredItemCreateResult created = UA_Client_MonitoredItems_createDataChange(
        c_client, subId, UA_TIMESTAMPSTORETURN_BOTH, item, NULL, onValue, NULL);
    UA_StatusCode status = created.statusCode;
    if(monId)
        *monId = created.monitoredItemId;
    UA_MonitoredItemCreateResult_clear(&created);
    return status;
}

static int valueIsDouble(const UA_DataValue *dv, UA_Double d) {
    return dv->hasValue && UA_Variant_hasScalarType(&dv->value, &UA_TYPES[UA_TYPES_DOUBLE]) &&
           *(UA_Double *)dv->value.data == d;
}

static int valueIsInt32(const UA_DataValue *dv, UA_Int32 i) {
    return dv->hasValue && UA_Variant_hasScalarType(&dv->value, &UA_TYPES[UA_TYPES_INT32]) &&
           *(UA_Int32 *)dv->value.data == i;
}

static void describeValues(char *out, size_t outSize) {
    size_t used = 0;
    out[0] = 0;
    for(int i = 0; i < s_valueCount && used + 1 < outSize; i++) {
        UA_String s = UA_STRING_NULL;
        UA_print(&s_dataValues[i].value, &UA_TYPES[UA_TYPES_VARIANT], &s);
        int n = snprintf(out + used, outSize - used, "%s%.*s:0x%08X", i ? ", " : "", (int)(s.length > 40 ? 40 : s.length),
                         (char *)s.data, (unsigned)s_dataValues[i].status);
        UA_String_clear(&s);
        if(n < 0)
            break;
        used += (size_t)n;
    }
}

/* ------------------------------------------------------------------ feature checks */

static const char *checkEventSubscription(void) {
    UA_StatusCode status;
    UA_UInt32 subId = createEventSubscription(UA_NODEID_NUMERIC(0, UA_NS0ID_SERVER), 0, &status);
    REQUIRE(status == UA_STATUSCODE_GOOD, "creating the event subscription returned %s", UA_StatusCode_name(status));
    UA_Int32 one = 1;
    UA_StatusCode wr = writeScalar(refId("NodeIds_Events_TriggerNode01"), &one, &UA_TYPES[UA_TYPES_INT32]);
    int found = wr == UA_STATUSCODE_GOOD ? waitForEvent(isTriggerEvent, 10) : -1;
    const char *failure = NULL;
    if(wr != UA_STATUSCODE_GOOD) {
        snprintf(c_msg, sizeof(c_msg), "writing the event trigger returned %s", UA_StatusCode_name(wr));
        failure = c_msg;
    } else if(found < 0) {
        snprintf(c_msg, sizeof(c_msg), "no trigger event in 10 s; received %u events", (unsigned)s_eventCount);
        failure = c_msg;
    } else {
        const EventRecord *e = &s_events[found];
        if(!UA_Variant_hasScalarType(&e->fields[0], &UA_TYPES[UA_TYPES_BYTESTRING]) ||
           ((UA_ByteString *)e->fields[0].data)->length == 0) {
            snprintf(c_msg, sizeof(c_msg), "the event has no EventId");
            failure = c_msg;
        } else if(!UA_Variant_hasScalarType(&e->fields[5], &UA_TYPES[UA_TYPES_UINT16]) ||
                  *(UA_UInt16 *)e->fields[5].data == 0) {
            snprintf(c_msg, sizeof(c_msg), "the event has no Severity");
            failure = c_msg;
        }
    }
    deleteSubscription(subId);
    clearEvents();
    return failure;
}

static const char *checkConditionRefresh(void) {
    UA_StatusCode status;
    UA_UInt32 subId = createEventSubscription(UA_NODEID_NUMERIC(0, UA_NS0ID_SERVER), 0, &status);
    REQUIRE(status == UA_STATUSCODE_GOOD, "creating the event subscription returned %s", UA_StatusCode_name(status));
    UA_Variant in;
    UA_Variant_setScalar(&in, &subId, &UA_TYPES[UA_TYPES_UINT32]);
    UA_StatusCode res = callMethod(UA_NODEID_NUMERIC(0, UA_NS0ID_CONDITIONTYPE),
                                   UA_NODEID_NUMERIC(0, UA_NS0ID_CONDITIONTYPE_CONDITIONREFRESH), 1, &in);
    const char *failure = NULL;
    if(res != UA_STATUSCODE_GOOD) {
        snprintf(c_msg, sizeof(c_msg), "ConditionRefresh returned %s", UA_StatusCode_name(res));
        failure = c_msg;
    } else if(waitForEvent(isRefreshEnd, 10) < 0) {
        snprintf(c_msg, sizeof(c_msg), "no RefreshEndEvent received (%u events)", (unsigned)s_eventCount);
        failure = c_msg;
    } else if(waitForEvent(isRefreshStart, 0) < 0) {
        snprintf(c_msg, sizeof(c_msg), "no RefreshStartEvent received");
        failure = c_msg;
    }
    deleteSubscription(subId);
    clearEvents();
    return failure;
}

static const char *checkAlarmAcknowledge(void) {
    UA_UInt16 alarmsNs = 0;
    UA_String ns = UA_STRING(ALARMS_NAMESPACE);
    REQUIRE(UA_Client_getNamespaceIndex(c_client, ns, &alarmsNs) == UA_STATUSCODE_GOOD, "the Alarms namespace is missing");
    UA_NodeId folder = UA_NODEID_STRING(alarmsNs, "Alarms");
    UA_StatusCode status;
    UA_UInt32 subId = createEventSubscription(folder, 1, &status);
    REQUIRE(status == UA_STATUSCODE_GOOD, "creating the condition event subscription returned %s",
            UA_StatusCode_name(status));
    UA_UInt32 seconds = 30;
    UA_Variant in[2];
    UA_Variant_setScalar(&in[0], &seconds, &UA_TYPES[UA_TYPES_UINT32]);
    UA_StatusCode res = callMethod(folder, UA_NODEID_STRING(alarmsNs, "Alarms.Start"), 1, in);
    const char *failure = NULL;
    if(res != UA_STATUSCODE_GOOD) {
        snprintf(c_msg, sizeof(c_msg), "Alarms.Start returned %s", UA_StatusCode_name(res));
        failure = c_msg;
    } else {
        int found = waitForEvent(isUnackedCondition, 20);
        if(found < 0) {
            char last[1024] = "none";
            if(s_eventCount > 0)
                describeEvent(&s_events[s_eventCount - 1], last, sizeof(last));
            snprintf(c_msg, sizeof(c_msg), "no unacknowledged condition in 20 s; received %u events, last: %s",
                     (unsigned)s_eventCount, last);
            failure = c_msg;
        } else {
            const EventRecord *e = &s_events[found];
            UA_LocalizedText comment = UA_LOCALIZEDTEXT("", "acknowledged by the interop test");
            UA_Variant_setScalar(&in[0], e->fields[0].data, &UA_TYPES[UA_TYPES_BYTESTRING]);
            UA_Variant_setScalar(&in[1], &comment, &UA_TYPES[UA_TYPES_LOCALIZEDTEXT]);
            res = callMethod(*(UA_NodeId *)e->fields[6].data,
                             UA_NODEID_NUMERIC(0, UA_NS0ID_ACKNOWLEDGEABLECONDITIONTYPE_ACKNOWLEDGE), 2, in);
            if(res != UA_STATUSCODE_GOOD) {
                snprintf(c_msg, sizeof(c_msg), "Acknowledge returned %s", UA_StatusCode_name(res));
                failure = c_msg;
            }
        }
    }
    callMethod(folder, UA_NODEID_STRING(alarmsNs, "Alarms.End"), 0, NULL);
    deleteSubscription(subId);
    clearEvents();
    return failure;
}

static const char *checkDeadbandFilter(void) {
    UA_NodeId analog = refId("DataAccess_AnalogType_Double");
    const char *typeNames[3] = {"", "Absolute", "Percent"};
    for(UA_UInt32 type = UA_DEADBANDTYPE_ABSOLUTE; type <= UA_DEADBANDTYPE_PERCENT; type++) {
        UA_Double zero = 0.0;
        UA_StatusCode wr = writeScalar(analog, &zero, &UA_TYPES[UA_TYPES_DOUBLE]);
        REQUIRE(wr == UA_STATUSCODE_GOOD, "write of %s returned %s", "DataAccess_AnalogType_Double", UA_StatusCode_name(wr));
        UA_StatusCode status;
        UA_UInt32 subId = createSubscription(100, &status);
        REQUIRE(status == UA_STATUSCODE_GOOD, "CreateSubscription %s", UA_StatusCode_name(status));
        UA_DataChangeFilter filter;
        UA_DataChangeFilter_init(&filter);
        filter.trigger = UA_DATACHANGETRIGGER_STATUSVALUE;
        filter.deadbandType = type;
        filter.deadbandValue = 10; /* 10 units, or 10 % of the EURange 0..100 */
        clearValues();
        status = createDataItem(subId, analog, 50, 10, UA_MONITORINGMODE_REPORTING, &filter, NULL);
        if(status != UA_STATUSCODE_GOOD) {
            deleteSubscription(subId);
            REQUIRE(0, "%s: monitored item %s", typeNames[type], UA_StatusCode_name(status));
        }
        pump(c_client, 500);
        const UA_Double values[4] = {5.0, 20.0, 25.0, 40.0};
        for(int i = 0; i < 4 && wr == UA_STATUSCODE_GOOD; i++) {
            wr = writeScalar(analog, &values[i], &UA_TYPES[UA_TYPES_DOUBLE]);
            pump(c_client, 400);
        }
        pump(c_client, 800);
        deleteSubscription(subId);
        int has5 = 0, has20 = 0, has25 = 0, has40 = 0;
        for(int i = 0; i < s_valueCount; i++) {
            has5 |= valueIsDouble(&s_dataValues[i], 5.0);
            has20 |= valueIsDouble(&s_dataValues[i], 20.0);
            has25 |= valueIsDouble(&s_dataValues[i], 25.0);
            has40 |= valueIsDouble(&s_dataValues[i], 40.0);
        }
        char list[1024];
        describeValues(list, sizeof(list));
        clearValues();
        REQUIRE(wr == UA_STATUSCODE_GOOD, "%s: a write returned %s", typeNames[type], UA_StatusCode_name(wr));
        REQUIRE(has20 && has40, "%s: 20 and 40 not reported (%s)", typeNames[type], list);
        REQUIRE(!has5 && !has25, "%s: changes inside the deadband reported (%s)", typeNames[type], list);
    }
    return NULL;
}

static const char *checkQueueOverflow(void) {
    UA_NodeId node = refId("Scalar_Static_Int32");
    UA_Int32 v = 0;
    UA_StatusCode wr = writeScalar(node, &v, &UA_TYPES[UA_TYPES_INT32]);
    REQUIRE(wr == UA_STATUSCODE_GOOD, "write of Scalar_Static_Int32 returned %s", UA_StatusCode_name(wr));
    UA_StatusCode status;
    UA_UInt32 subId = createSubscription(2000, &status);
    REQUIRE(status == UA_STATUSCODE_GOOD, "CreateSubscription %s", UA_StatusCode_name(status));
    clearValues();
    status = createDataItem(subId, node, 0, 2, UA_MONITORINGMODE_REPORTING, NULL, NULL);
    if(status != UA_STATUSCODE_GOOD) {
        deleteSubscription(subId);
        REQUIRE(0, "monitored item %s", UA_StatusCode_name(status));
    }
    /* Let the initial value go out first. */
    pump(c_client, 2500);
    clearValues();
    for(v = 1; v <= 5 && wr == UA_STATUSCODE_GOOD; v++)
        wr = writeScalar(node, &v, &UA_TYPES[UA_TYPES_INT32]);
    pump(c_client, 3000);
    deleteSubscription(subId);
    char list[1024];
    describeValues(list, sizeof(list));
    int count = s_valueCount;
    int ordered = count == 2 && valueIsInt32(&s_dataValues[0], 4) && valueIsInt32(&s_dataValues[1], 5);
    int overflow = 0;
    for(int i = 0; i < count; i++)
        overflow |= (s_dataValues[i].status & 0x0480) == 0x0480;
    clearValues();
    REQUIRE(wr == UA_STATUSCODE_GOOD, "a write returned %s", UA_StatusCode_name(wr));
    REQUIRE(count == 2, "expected the last 2 of 5 values, got %d (%s)", count, list);
    REQUIRE(ordered, "expected 4 and 5, got %s", list);
    REQUIRE(overflow, "no value carries the Overflow bit (%s)", list);
    return NULL;
}

static const char *checkTriggering(void) {
    UA_StatusCode status;
    UA_UInt32 subId = createSubscription(100, &status);
    REQUIRE(status == UA_STATUSCODE_GOOD, "CreateSubscription %s", UA_StatusCode_name(status));
    UA_UInt32 triggerId = 0, linkedId = 0;
    status = createDataItem(subId, refId("Scalar_Static_Int32"), 50, 10, UA_MONITORINGMODE_REPORTING, NULL, &triggerId);
    if(status == UA_STATUSCODE_GOOD)
        status = createDataItem(subId, refId("Scalar_Static_String"), 50, 10, UA_MONITORINGMODE_SAMPLING, NULL, &linkedId);
    if(status != UA_STATUSCODE_GOOD) {
        deleteSubscription(subId);
        REQUIRE(0, "monitored item %s", UA_StatusCode_name(status));
    }
    s_linkedId = linkedId;
    s_linkedReports = 0;
    UA_SetTriggeringRequest request;
    UA_SetTriggeringRequest_init(&request);
    request.subscriptionId = subId;
    request.triggeringItemId = triggerId;
    request.linksToAdd = &linkedId;
    request.linksToAddSize = 1;
    UA_SetTriggeringResponse response = UA_Client_MonitoredItems_setTriggering(c_client, request);
    UA_StatusCode added = response.responseHeader.serviceResult != UA_STATUSCODE_GOOD ? response.responseHeader.serviceResult
                          : response.addResultsSize == 1 ? response.addResults[0] : UA_STATUSCODE_BADUNEXPECTEDERROR;
    UA_SetTriggeringResponse_clear(&response);
    const char *failure = NULL;
    if(added != UA_STATUSCODE_GOOD) {
        snprintf(c_msg, sizeof(c_msg), "SetTriggering returned %s", UA_StatusCode_name(added));
        failure = c_msg;
    } else {
        pump(c_client, 500);
        int before = s_linkedReports;
        char text[64];
        snprintf(text, sizeof(text), "linked %lld", (long long)UA_DateTime_now());
        UA_String s = UA_STRING(text);
        UA_StatusCode wr = writeScalar(refId("Scalar_Static_String"), &s, &UA_TYPES[UA_TYPES_STRING]);
        pump(c_client, 500);
        int afterString = s_linkedReports;
        UA_Int32 tick = (UA_Int32)(UA_DateTime_nowMonotonic() / UA_DATETIME_MSEC);
        if(wr == UA_STATUSCODE_GOOD)
            wr = writeScalar(refId("Scalar_Static_Int32"), &tick, &UA_TYPES[UA_TYPES_INT32]);
        pump(c_client, 1000);
        if(wr != UA_STATUSCODE_GOOD) {
            snprintf(c_msg, sizeof(c_msg), "a write returned %s", UA_StatusCode_name(wr));
            failure = c_msg;
        } else if(afterString != before) {
            snprintf(c_msg, sizeof(c_msg), "the sampling item reported without its trigger");
            failure = c_msg;
        } else if(s_linkedReports <= before) {
            snprintf(c_msg, sizeof(c_msg), "the triggered item did not report");
            failure = c_msg;
        }
    }
    s_linkedId = 0;
    deleteSubscription(subId);
    clearValues();
    return failure;
}

static UA_StatusCode republish(UA_UInt32 subId, UA_UInt32 sequence) {
    UA_RepublishRequest request;
    UA_RepublishRequest_init(&request);
    request.subscriptionId = subId;
    request.retransmitSequenceNumber = sequence;
    UA_RepublishResponse response;
    callService(c_client, &request, &UA_TYPES[UA_TYPES_REPUBLISHREQUEST], &response,
                &UA_TYPES[UA_TYPES_REPUBLISHRESPONSE]);
    UA_StatusCode status = response.responseHeader.serviceResult;
    UA_RepublishResponse_clear(&response);
    return status;
}

static const char *checkRepublish(void) {
    UA_StatusCode status;
    UA_UInt32 subId = createSubscription(100, &status);
    REQUIRE(status == UA_STATUSCODE_GOOD, "CreateSubscription %s", UA_StatusCode_name(status));
    clearValues();
    status = createDataItem(subId, refId("Scalar_Static_Int32"), 50, 10, UA_MONITORINGMODE_REPORTING, NULL, NULL);
    if(status != UA_STATUSCODE_GOOD) {
        deleteSubscription(subId);
        REQUIRE(0, "monitored item %s", UA_StatusCode_name(status));
    }
    pump(c_client, 300);
    /* The client API does not expose the last sequence number; the
     * subscription sent at most a handful of messages so far. */
    UA_StatusCode notSent = republish(subId, 1000 + (UA_UInt32)s_valueCount);
    UA_StatusCode unknown = republish(subId + 100000, 1);
    deleteSubscription(subId);
    clearValues();
    REQUIRE(notSent == UA_STATUSCODE_BADMESSAGENOTAVAILABLE, "Republish of an unsent message returned %s",
            UA_StatusCode_name(notSent));
    REQUIRE(unknown == UA_STATUSCODE_BADSUBSCRIPTIONIDINVALID, "Republish of an unknown subscription returned %s",
            UA_StatusCode_name(unknown));
    return NULL;
}

/* Publishes on the target session (which has no client-side subscription)
 * until a data change with the expected value arrives. */
static int publishUntilValue(UA_Client *target, UA_UInt32 subId, UA_Int32 expected, int seconds, char *info,
                             size_t infoSize) {
    UA_DateTime end = UA_DateTime_nowMonotonic() + (UA_DateTime)seconds * UA_DATETIME_SEC;
    UA_SubscriptionAcknowledgement ack;
    int haveAck = 0, found = 0;
    while(!found && UA_DateTime_nowMonotonic() < end) {
        UA_PublishRequest request;
        UA_PublishRequest_init(&request);
        if(haveAck) {
            request.subscriptionAcknowledgements = &ack;
            request.subscriptionAcknowledgementsSize = 1;
        }
        UA_PublishResponse response;
        callService(target, &request, &UA_TYPES[UA_TYPES_PUBLISHREQUEST], &response, &UA_TYPES[UA_TYPES_PUBLISHRESPONSE]);
        if(response.responseHeader.serviceResult != UA_STATUSCODE_GOOD) {
            snprintf(info, infoSize, "Publish returned %s", UA_StatusCode_name(response.responseHeader.serviceResult));
            UA_PublishResponse_clear(&response);
            return 0;
        }
        haveAck = response.notificationMessage.notificationDataSize > 0;
        ack.subscriptionId = response.subscriptionId;
        ack.sequenceNumber = response.notificationMessage.sequenceNumber;
        for(size_t i = 0; i < response.notificationMessage.notificationDataSize; i++) {
            UA_ExtensionObject *eo = &response.notificationMessage.notificationData[i];
            if(response.subscriptionId != subId || eo->encoding != UA_EXTENSIONOBJECT_DECODED ||
               eo->content.decoded.type != &UA_TYPES[UA_TYPES_DATACHANGENOTIFICATION])
                continue;
            UA_DataChangeNotification *dcn = (UA_DataChangeNotification *)eo->content.decoded.data;
            for(size_t m = 0; m < dcn->monitoredItemsSize; m++)
                found |= valueIsInt32(&dcn->monitoredItems[m].value, expected);
        }
        UA_PublishResponse_clear(&response);
    }
    if(!found)
        snprintf(info, infoSize, "no data change with the written value in %d s", seconds);
    return found;
}

static const char *checkTransferSubscription(void) {
    UA_NodeId node = refId("Scalar_Static_Int32");
    UA_StatusCode status;
    UA_UInt32 subId = createSubscription(100, &status);
    REQUIRE(status == UA_STATUSCODE_GOOD, "CreateSubscription %s", UA_StatusCode_name(status));
    clearValues();
    status = createDataItem(subId, node, 50, 10, UA_MONITORINGMODE_REPORTING, NULL, NULL);
    if(status != UA_STATUSCODE_GOOD) {
        deleteSubscription(subId);
        REQUIRE(0, "monitored item %s", UA_StatusCode_name(status));
    }
    UA_Client *target = newClient(NULL, NULL);
    UA_StatusCode res = UA_Client_connect(target, c_url);
    const char *failure = NULL;
    if(res != UA_STATUSCODE_GOOD) {
        snprintf(c_msg, sizeof(c_msg), "opening the second session returned %s", UA_StatusCode_name(res));
        failure = c_msg;
    } else {
        UA_TransferSubscriptionsRequest request;
        UA_TransferSubscriptionsRequest_init(&request);
        request.subscriptionIds = &subId;
        request.subscriptionIdsSize = 1;
        request.sendInitialValues = true;
        UA_TransferSubscriptionsResponse response;
        callService(target, &request, &UA_TYPES[UA_TYPES_TRANSFERSUBSCRIPTIONSREQUEST], &response,
                    &UA_TYPES[UA_TYPES_TRANSFERSUBSCRIPTIONSRESPONSE]);
        UA_StatusCode transferred = response.responseHeader.serviceResult != UA_STATUSCODE_GOOD
                                        ? response.responseHeader.serviceResult
                                    : response.resultsSize == 1 ? response.results[0].statusCode
                                                                : UA_STATUSCODE_BADUNEXPECTEDERROR;
        UA_TransferSubscriptionsResponse_clear(&response);
        if(transferred != UA_STATUSCODE_GOOD) {
            snprintf(c_msg, sizeof(c_msg), "TransferSubscriptions returned %s", UA_StatusCode_name(transferred));
            failure = c_msg;
        } else {
            pump(c_client, 300);
            UA_Int32 value = (UA_Int32)(UA_DateTime_nowMonotonic() / UA_DATETIME_MSEC) & 0x3FFFFFFF;
            UA_Variant v;
            UA_Variant_setScalar(&v, &value, &UA_TYPES[UA_TYPES_INT32]);
            res = UA_Client_writeValueAttribute(target, node, &v);
            char info[256];
            if(res != UA_STATUSCODE_GOOD) {
                snprintf(c_msg, sizeof(c_msg), "write returned %s", UA_StatusCode_name(res));
                failure = c_msg;
            } else if(!publishUntilValue(target, subId, value, 10, info, sizeof(info))) {
                snprintf(c_msg, sizeof(c_msg), "the transferred subscription reported nothing: %s", info);
                failure = c_msg;
            }
            /* The target session deletes the transferred subscription. */
            UA_DeleteSubscriptionsRequest del;
            UA_DeleteSubscriptionsRequest_init(&del);
            del.subscriptionIds = &subId;
            del.subscriptionIdsSize = 1;
            UA_DeleteSubscriptionsResponse delResponse;
            callService(target, &del, &UA_TYPES[UA_TYPES_DELETESUBSCRIPTIONSREQUEST], &delResponse,
                        &UA_TYPES[UA_TYPES_DELETESUBSCRIPTIONSRESPONSE]);
            UA_DeleteSubscriptionsResponse_clear(&delResponse);
        }
    }
    UA_Client_disconnect(target);
    UA_Client_delete(target);
    /* Removes the client-side state of the subscription (gone on the server). */
    deleteSubscription(subId);
    clearValues();
    return failure;
}

static const char *checkWrongPasswordRejected(void) {
    UA_Client *client = newClient(NULL, NULL);
    UA_StatusCode res = UA_Client_connectUsername(client, c_url, "user1", "wrong password");
    UA_Client_disconnect(client);
    UA_Client_delete(client);
    REQUIRE(res != UA_STATUSCODE_GOOD, "a session with a wrong password was activated");
    REQUIRE(res == UA_STATUSCODE_BADUSERACCESSDENIED || res == UA_STATUSCODE_BADIDENTITYTOKENREJECTED,
            "the wrong password was rejected with %s", UA_StatusCode_name(res));
    return NULL;
}

static const char *checkX509UserToken(void) {
    UA_String subject[2] = {UA_STRING_STATIC("CN=InteropUser"), UA_STRING_STATIC("O=OPC Foundation")};
    UA_String san[1] = {UA_STRING_STATIC("DNS:localhost")};
    UA_KeyValueMap *params = UA_KeyValueMap_new();
    UA_UInt16 bits = 2048;
    UA_KeyValueMap_setScalar(params, UA_QUALIFIEDNAME(0, "key-size-bits"), &bits, &UA_TYPES[UA_TYPES_UINT16]);
    UA_ByteString certificate = UA_BYTESTRING_NULL, key = UA_BYTESTRING_NULL;
    UA_StatusCode res = UA_CreateCertificate(&s_logger, subject, 2, san, 1, UA_CERTIFICATEFORMAT_DER, params, &key,
                                             &certificate);
    UA_KeyValueMap_delete(params);
    REQUIRE(res == UA_STATUSCODE_GOOD, "creating the user certificate returned %s", UA_StatusCode_name(res));
    UA_Client *client = newClient(&certificate, &key);
    res = UA_Client_connect(client, c_url);
    const char *failure = NULL;
    if(res != UA_STATUSCODE_GOOD) {
        snprintf(c_msg, sizeof(c_msg), "the X509 user session returned %s", UA_StatusCode_name(res));
        failure = c_msg;
    } else {
        UA_ReadValueId rv;
        UA_ReadValueId_init(&rv);
        rv.nodeId = UA_NODEID_NUMERIC(0, UA_NS0ID_SERVER_SERVERSTATUS_STATE);
        rv.attributeId = UA_ATTRIBUTEID_VALUE;
        UA_DataValue dv = UA_Client_read(client, &rv);
        if(dv.status != UA_STATUSCODE_GOOD) {
            snprintf(c_msg, sizeof(c_msg), "reading with the X509 user returned %s", UA_StatusCode_name(dv.status));
            failure = c_msg;
        }
        UA_DataValue_clear(&dv);
    }
    UA_Client_disconnect(client);
    UA_Client_delete(client);
    UA_ByteString_clear(&certificate);
    UA_ByteString_clear(&key);
    return failure;
}

static const char *checkRegisterNodes(void) {
    UA_NodeId nodes[2] = {refId("Scalar_Static_Int32"), refId("Scalar_Static_String")};
    UA_RegisterNodesRequest request;
    UA_RegisterNodesRequest_init(&request);
    request.nodesToRegister = nodes;
    request.nodesToRegisterSize = 2;
    UA_RegisterNodesResponse registered = UA_Client_Service_registerNodes(c_client, request);
    if(registered.responseHeader.serviceResult != UA_STATUSCODE_GOOD || registered.registeredNodeIdsSize != 2) {
        snprintf(c_msg, sizeof(c_msg), "RegisterNodes returned %s with %u ids",
                 UA_StatusCode_name(registered.responseHeader.serviceResult), (unsigned)registered.registeredNodeIdsSize);
        UA_RegisterNodesResponse_clear(&registered);
        return c_msg;
    }
    UA_ReadValueId ids[2];
    for(int i = 0; i < 2; i++) {
        UA_ReadValueId_init(&ids[i]);
        ids[i].nodeId = registered.registeredNodeIds[i];
        ids[i].attributeId = UA_ATTRIBUTEID_VALUE;
    }
    UA_ReadRequest read;
    UA_ReadRequest_init(&read);
    read.nodesToRead = ids;
    read.nodesToReadSize = 2;
    read.timestampsToReturn = UA_TIMESTAMPSTORETURN_NEITHER;
    UA_ReadResponse response = UA_Client_Service_read(c_client, read);
    UA_StatusCode s0 = response.resultsSize == 2 ? response.results[0].status : response.responseHeader.serviceResult;
    UA_StatusCode s1 = response.resultsSize == 2 ? response.results[1].status : response.responseHeader.serviceResult;
    UA_ReadResponse_clear(&response);
    UA_UnregisterNodesRequest unregister;
    UA_UnregisterNodesRequest_init(&unregister);
    unregister.nodesToUnregister = registered.registeredNodeIds;
    unregister.nodesToUnregisterSize = registered.registeredNodeIdsSize;
    UA_UnregisterNodesResponse unregistered = UA_Client_Service_unregisterNodes(c_client, unregister);
    UA_StatusCode u = unregistered.responseHeader.serviceResult;
    UA_UnregisterNodesResponse_clear(&unregistered);
    UA_RegisterNodesResponse_clear(&registered);
    REQUIRE(s0 == UA_STATUSCODE_GOOD && s1 == UA_STATUSCODE_GOOD, "reading registered nodes returned %s, %s",
            UA_StatusCode_name(s0), UA_StatusCode_name(s1));
    REQUIRE(u == UA_STATUSCODE_GOOD, "UnregisterNodes returned %s", UA_StatusCode_name(u));
    return NULL;
}

static const char *checkHistoryReadRaw(void) {
    UA_ReadRawModifiedDetails details;
    UA_ReadRawModifiedDetails_init(&details);
    details.endTime = UA_DateTime_now();
    details.startTime = details.endTime - 3 * 3600 * (UA_DateTime)UA_DATETIME_SEC;
    details.numValuesPerNode = 10;
    details.returnBounds = false;
    UA_HistoryReadValueId id;
    UA_HistoryReadValueId_init(&id);
    id.nodeId = refId("Scalar_Static_Double");
    UA_HistoryReadRequest request;
    UA_HistoryReadRequest_init(&request);
    UA_ExtensionObject_setValue(&request.historyReadDetails, &details, &UA_TYPES[UA_TYPES_READRAWMODIFIEDDETAILS]);
    request.timestampsToReturn = UA_TIMESTAMPSTORETURN_SOURCE;
    request.nodesToRead = &id;
    request.nodesToReadSize = 1;
    UA_HistoryReadResponse response = UA_Client_Service_historyRead(c_client, request);
    UA_StatusCode status = response.responseHeader.serviceResult != UA_STATUSCODE_GOOD ? response.responseHeader.serviceResult
                           : response.resultsSize == 1 ? response.results[0].statusCode : UA_STATUSCODE_BADUNEXPECTEDERROR;
    size_t values = 0;
    int isHistoryData = 0;
    UA_ByteString cp = UA_BYTESTRING_NULL;
    if(status == UA_STATUSCODE_GOOD) {
        UA_ExtensionObject *data = &response.results[0].historyData;
        isHistoryData = data->encoding == UA_EXTENSIONOBJECT_DECODED &&
                        data->content.decoded.type == &UA_TYPES[UA_TYPES_HISTORYDATA];
        if(isHistoryData)
            values = ((UA_HistoryData *)data->content.decoded.data)->dataValuesSize;
        UA_ByteString_copy(&response.results[0].continuationPoint, &cp);
    }
    UA_HistoryReadResponse_clear(&response);
    if(cp.length > 0) {
        id.continuationPoint = cp;
        request.releaseContinuationPoints = true;
        UA_HistoryReadResponse released = UA_Client_Service_historyRead(c_client, request);
        UA_HistoryReadResponse_clear(&released);
        UA_ByteString_clear(&cp);
    }
    REQUIRE(status == UA_STATUSCODE_GOOD, "HistoryRead returned %s", UA_StatusCode_name(status));
    REQUIRE(isHistoryData && values > 0, "HistoryRead returned no values");
    REQUIRE(values <= 10, "%u values despite NumValuesPerNode 10", (unsigned)values);
    return NULL;
}

static const char *checkNodeManagement(void) {
    char name[64];
    srand((unsigned)UA_DateTime_now());
    snprintf(name, sizeof(name), "InteropAdded_%04x%04x", rand() & 0xFFFF, rand() & 0xFFFF);
    UA_ObjectAttributes attributes;
    UA_ObjectAttributes_init(&attributes);
    attributes.displayName = UA_LOCALIZEDTEXT("", name);
    attributes.specifiedAttributes = UA_NODEATTRIBUTESMASK_DISPLAYNAME;
    UA_AddNodesItem item;
    UA_AddNodesItem_init(&item);
    item.parentNodeId.nodeId = UA_NODEID_NUMERIC(0, UA_NS0ID_OBJECTSFOLDER);
    item.referenceTypeId = UA_NODEID_NUMERIC(0, UA_NS0ID_ORGANIZES);
    item.requestedNewNodeId.nodeId = UA_NODEID_STRING(c_ns, name);
    item.browseName = UA_QUALIFIEDNAME(c_ns, name);
    item.nodeClass = UA_NODECLASS_OBJECT;
    UA_ExtensionObject_setValue(&item.nodeAttributes, &attributes, &UA_TYPES[UA_TYPES_OBJECTATTRIBUTES]);
    item.typeDefinition.nodeId = UA_NODEID_NUMERIC(0, UA_NS0ID_BASEOBJECTTYPE);
    UA_AddNodesRequest request;
    UA_AddNodesRequest_init(&request);
    request.nodesToAdd = &item;
    request.nodesToAddSize = 1;
    UA_AddNodesResponse added = UA_Client_Service_addNodes(c_client, request);
    UA_StatusCode status = added.responseHeader.serviceResult != UA_STATUSCODE_GOOD ? added.responseHeader.serviceResult
                           : added.resultsSize == 1 ? added.results[0].statusCode : UA_STATUSCODE_BADUNEXPECTEDERROR;
    UA_NodeId nodeId = UA_NODEID_NULL;
    if(status == UA_STATUSCODE_GOOD)
        UA_NodeId_copy(&added.results[0].addedNodeId, &nodeId);
    UA_AddNodesResponse_clear(&added);
    REQUIRE(status == UA_STATUSCODE_GOOD, "AddNodes returned %s", UA_StatusCode_name(status));
    size_t count;
    UA_ReferenceDescription *refs = browse(UA_NODEID_NUMERIC(0, UA_NS0ID_OBJECTSFOLDER), 0, &count, &status);
    int found = refs && hasReference(refs, count, name);
    if(refs)
        UA_Array_delete(refs, count, &UA_TYPES[UA_TYPES_REFERENCEDESCRIPTION]);
    UA_DeleteNodesItem del;
    UA_DeleteNodesItem_init(&del);
    del.nodeId = nodeId;
    del.deleteTargetReferences = true;
    UA_DeleteNodesRequest delRequest;
    UA_DeleteNodesRequest_init(&delRequest);
    delRequest.nodesToDelete = &del;
    delRequest.nodesToDeleteSize = 1;
    UA_DeleteNodesResponse deleted = UA_Client_Service_deleteNodes(c_client, delRequest);
    UA_StatusCode delStatus = deleted.responseHeader.serviceResult != UA_STATUSCODE_GOOD ? deleted.responseHeader.serviceResult
                              : deleted.resultsSize == 1 ? deleted.results[0] : UA_STATUSCODE_BADUNEXPECTEDERROR;
    UA_DeleteNodesResponse_clear(&deleted);
    UA_NodeId_clear(&nodeId);
    REQUIRE(found, "the added node is not organized by the Objects folder");
    REQUIRE(delStatus == UA_STATUSCODE_GOOD, "DeleteNodes returned %s", UA_StatusCode_name(delStatus));
    return NULL;
}

static const char *checkIndexRange(void) {
    UA_NodeId node = refId("Scalar_Static_Arrays_Int32");
    UA_Int32 all[10] = {0, 1, 2, 3, 4, 5, 6, 7, 8, 9};
    UA_Variant v;
    UA_Variant_setArray(&v, all, 10, &UA_TYPES[UA_TYPES_INT32]);
    UA_StatusCode serviceResult;
    UA_StatusCode wr = writeValue(node, &v, &serviceResult);
    REQUIRE(wr == UA_STATUSCODE_GOOD, "write of Scalar_Static_Arrays_Int32 returned %s", UA_StatusCode_name(wr));

    UA_Int32 part[2] = {20, 30};
    UA_WriteValue wv;
    UA_WriteValue_init(&wv);
    wv.nodeId = node;
    wv.attributeId = UA_ATTRIBUTEID_VALUE;
    wv.indexRange = UA_STRING("2:3");
    UA_Variant_setArray(&wv.value.value, part, 2, &UA_TYPES[UA_TYPES_INT32]);
    wv.value.hasValue = true;
    UA_WriteRequest write;
    UA_WriteRequest_init(&write);
    write.nodesToWrite = &wv;
    write.nodesToWriteSize = 1;
    UA_WriteResponse written = UA_Client_Service_write(c_client, write);
    wr = written.resultsSize == 1 ? written.results[0] : written.responseHeader.serviceResult;
    UA_WriteResponse_clear(&written);
    REQUIRE(wr == UA_STATUSCODE_GOOD, "writing the index range 2:3 returned %s", UA_StatusCode_name(wr));

    UA_ReadValueId rv;
    UA_ReadValueId_init(&rv);
    rv.nodeId = node;
    rv.attributeId = UA_ATTRIBUTEID_VALUE;
    rv.indexRange = UA_STRING("1:4");
    UA_DataValue dv = UA_Client_read(c_client, &rv);
    UA_StatusCode status = dv.status;
    const UA_Int32 expected[4] = {1, 20, 30, 4};
    int ok = UA_Variant_hasArrayType(&dv.value, &UA_TYPES[UA_TYPES_INT32]) && dv.value.arrayLength == 4 &&
             memcmp(dv.value.data, expected, sizeof(expected)) == 0;
    UA_String printed = UA_STRING_NULL;
    UA_print(&dv.value, &UA_TYPES[UA_TYPES_VARIANT], &printed);
    snprintf(c_msg, sizeof(c_msg), "index range 1:4 read %.*s", (int)(printed.length > 200 ? 200 : printed.length),
             (char *)printed.data);
    UA_String_clear(&printed);
    UA_DataValue_clear(&dv);
    REQUIRE(status == UA_STATUSCODE_GOOD, "reading the index range 1:4 returned %s", UA_StatusCode_name(status));
    if(!ok)
        return c_msg;
    return NULL;
}

static const char *checkFindServers(void) {
    /* The server's own ApplicationUri is the first entry of its ServerArray. */
    UA_Variant serverArray;
    UA_Variant_init(&serverArray);
    UA_StatusCode res = UA_Client_readValueAttribute(c_client, UA_NODEID_NUMERIC(0, UA_NS0ID_SERVER_SERVERARRAY),
                                                     &serverArray);
    UA_String serverUri = UA_STRING_NULL;
    if(res == UA_STATUSCODE_GOOD && UA_Variant_hasArrayType(&serverArray, &UA_TYPES[UA_TYPES_STRING]) &&
       serverArray.arrayLength > 0)
        UA_String_copy((UA_String *)serverArray.data, &serverUri);
    UA_Variant_clear(&serverArray);
    REQUIRE(serverUri.length > 0, "reading Server_ServerArray returned %s", UA_StatusCode_name(res));

    size_t serversSize = 0;
    UA_ApplicationDescription *servers = NULL;
    res = UA_Client_findServers(c_client, c_url, 0, NULL, 0, NULL, &serversSize, &servers);
    int listed = 0;
    char uris[1024] = "";
    size_t used = 0;
    for(size_t i = 0; res == UA_STATUSCODE_GOOD && i < serversSize; i++) {
        listed |= UA_String_equal(&servers[i].applicationUri, &serverUri);
        int n = snprintf(uris + used, sizeof(uris) - used, "%s%.*s", i ? ", " : "", (int)servers[i].applicationUri.length,
                         (char *)servers[i].applicationUri.data);
        if(n > 0 && used + (size_t)n < sizeof(uris))
            used += (size_t)n;
    }
    UA_Array_delete(servers, serversSize, &UA_TYPES[UA_TYPES_APPLICATIONDESCRIPTION]);
    snprintf(c_msg, sizeof(c_msg), "FindServers (%s) does not list %.*s: %s", UA_StatusCode_name(res),
             (int)serverUri.length, (char *)serverUri.data, uris);
    UA_String_clear(&serverUri);
    if(!listed)
        return c_msg;

    size_t endpointsSize = 0;
    UA_EndpointDescription *endpoints = NULL;
    res = UA_Client_getEndpoints(c_client, c_url, &endpointsSize, &endpoints);
    UA_String policy = UA_STRING((char *)c_policy);
    UA_MessageSecurityMode mode = UA_Client_getConfig(c_client)->securityMode;
    int offered = 0;
    for(size_t i = 0; res == UA_STATUSCODE_GOOD && i < endpointsSize; i++)
        offered |= UA_String_equal(&endpoints[i].securityPolicyUri, &policy) && endpoints[i].securityMode == mode;
    UA_Array_delete(endpoints, endpointsSize, &UA_TYPES[UA_TYPES_ENDPOINTDESCRIPTION]);
    REQUIRE(offered, "GetEndpoints (%s) does not offer the session's endpoint", UA_StatusCode_name(res));
    return NULL;
}

static const char *checkSessionReconnect(void) {
    UA_NodeId before = UA_NODEID_NULL, after = UA_NODEID_NULL;
    UA_ByteString nonce = UA_BYTESTRING_NULL;
    UA_StatusCode res = UA_Client_getSessionAuthenticationToken(c_client, &before, &nonce);
    UA_ByteString_clear(&nonce);
    REQUIRE(res == UA_STATUSCODE_GOOD, "getting the session's authentication token returned %s", UA_StatusCode_name(res));
    /* Closing only the SecureChannel keeps the Session; the next connect opens
     * a new channel and re-activates the Session there. No new Session may be
     * created instead. */
    UA_Client_getConfig(c_client)->noNewSession = true;
    UA_Client_disconnectSecureChannel(c_client);
    res = c_user ? UA_Client_connectUsername(c_client, c_url, c_user, opt("password", ""))
                 : UA_Client_connect(c_client, c_url);
    UA_Client_getConfig(c_client)->noNewSession = false;
    if(res != UA_STATUSCODE_GOOD) {
        UA_NodeId_clear(&before);
        REQUIRE(0, "re-activating the session on a new channel returned %s", UA_StatusCode_name(res));
    }
    UA_Client_getSessionAuthenticationToken(c_client, &after, &nonce);
    UA_ByteString_clear(&nonce);
    int same = UA_NodeId_equal(&before, &after);
    UA_NodeId_clear(&before);
    UA_NodeId_clear(&after);
    REQUIRE(same, "the session changed (a new session was created)");
    UA_DataValue state = readAttribute(UA_NODEID_NUMERIC(0, UA_NS0ID_SERVER_SERVERSTATUS_STATE), UA_ATTRIBUTEID_VALUE);
    UA_StatusCode status = state.status;
    UA_DataValue_clear(&state);
    REQUIRE(status == UA_STATUSCODE_GOOD, "reading after the reconnect returned %s", UA_StatusCode_name(status));
    return NULL;
}

static const struct {
    const char *name;
    CheckFn fn;
} s_checks[] = {
    {"NamespaceArray", checkNamespaceArray},
    {"ReadServerStatusStructure", checkServerStatus},
    {"BrowseObjectsFolder", checkBrowseObjectsFolder},
    {"TranslateBrowsePath", checkTranslateBrowsePath},
    {"ReadScalars", checkReadScalars},
    {"ReadAttributes", checkReadAttributes},
    {"WriteAndReadBack", checkWriteAndReadBack},
    {"CallMethods", checkCallMethods},
    {"Subscription", checkSubscription},
    {"ComplexTypes", checkComplexTypes},
    {"LargeArrayRoundTrip", checkLargeArray},
    {"LargeByteStringRoundTrip", checkLargeByteString},
    {"OversizedRequestRejected", checkOversizedRequest},
    {"BrowseContinuationPoints", checkBrowseContinuationPoints},
    {"ReadManyNodes", checkReadManyNodes},
    {"TokenRenewal", checkTokenRenewal},
    {"EventSubscription", checkEventSubscription},
    {"ConditionRefresh", checkConditionRefresh},
    {"AlarmAcknowledge", checkAlarmAcknowledge},
    {"DeadbandFilter", checkDeadbandFilter},
    {"QueueOverflow", checkQueueOverflow},
    {"Triggering", checkTriggering},
    {"Republish", checkRepublish},
    {"TransferSubscription", checkTransferSubscription},
    {"WrongPasswordRejected", checkWrongPasswordRejected},
    {"X509UserToken", checkX509UserToken},
    {"RegisterNodes", checkRegisterNodes},
    {"HistoryReadRaw", checkHistoryReadRaw},
    {"NodeManagement", checkNodeManagement},
    {"IndexRange", checkIndexRange},
    {"FindServers", checkFindServers},
    {"SessionReconnect", checkSessionReconnect},
};
#define CHECK_COUNT (sizeof(s_checks) / sizeof(s_checks[0]))

static int selected(const char *checks, const char *name) {
    size_t len = strlen(name);
    for(const char *p = checks; (p = strstr(p, name)) != NULL; p += len) {
        if((p == checks || p[-1] == ',') && (p[len] == 0 || p[len] == ','))
            return 1;
    }
    return 0;
}

static int runClient(void) {
    const char *url = opt("url", NULL), *pki = opt("pki", NULL);
    if(!url || !pki) {
        fprintf(stderr, "error: client needs --url and --pki\n");
        return EXIT_USAGE;
    }
    const char *policy = opt("policy", POLICY_PREFIX "None");
    const char *mode = opt("mode", "None");
    const char *checks = opt("checks", "NamespaceArray,ReadServerStatusStructure,BrowseObjectsFolder,"
                                       "TranslateBrowsePath,ReadScalars,ReadAttributes,WriteAndReadBack,"
                                       "CallMethods,Subscription");
    const char *expected = opt("expect-connect-error", "");
    const char *curve = NULL;
    EccPolicyFn createEcc = NULL;
    for(int i = 0; i < ECC_POLICIES; i++) {
        if(strcmp(policy, s_ecc[i].policy) == 0) {
            curve = s_ecc[i].curve;
            createEcc = s_ecc[i].create;
        }
    }
    const char *name = "Open62541InteropClient";
    char applicationUri[256];
    snprintf(applicationUri, sizeof(applicationUri), "urn:localhost:opcfoundation.org:%s", name);
    UA_ByteString certificate, privateKey;
    UA_StatusCode res = loadOrCreateIdentity(pki, name, applicationUri, curve, &certificate, &privateKey);
    if(res != UA_STATUSCODE_GOOD) {
        fprintf(stderr, "FATAL creating the certificate failed: %s\n", UA_StatusCode_name(res));
        return EXIT_FATAL;
    }
    if(flag("init-only", 0)) {
        printf("PEER-PKI-READY %s\n", applicationUri);
        return EXIT_SUCCESS;
    }

    c_url = url;
    c_policy = policy;
    c_mode = mode;
    c_applicationUri = applicationUri;
    c_certificate = certificate;
    c_privateKey = privateKey;
    c_createEcc = createEcc;
    c_client = newClient(NULL, NULL);

    UA_DateTime started = UA_DateTime_nowMonotonic();
    UA_DateTime deadline = started + (UA_DateTime)atoi(opt("timeout-seconds", "300")) * UA_DATETIME_SEC;
    const char *user = opt("user", NULL);
    c_user = user;
    res = user ? UA_Client_connectUsername(c_client, url, user, opt("password", ""))
               : UA_Client_connect(c_client, url);
    int connected = res == UA_STATUSCODE_GOOD;
    if(expected[0]) {
        const char *actual = UA_StatusCode_name(res);
        if(!connected && selected(expected, actual))
            report("Connect", NULL, started);
        else {
            snprintf(c_msg, sizeof(c_msg), "expected %s, the connect returned %s", expected, actual);
            report("Connect", c_msg, started);
        }
        connected = 0;
    } else if(!connected) {
        snprintf(c_msg, sizeof(c_msg), "%s connecting to %s with %s/%s", UA_StatusCode_name(res), url, policy, mode);
        report("Connect", c_msg, started);
    } else {
        report("Connect", NULL, started);
    }

    if(connected) {
        UA_String ns = UA_STRING(REFERENCE_NAMESPACE);
        if(UA_Client_getNamespaceIndex(c_client, ns, &c_ns) != UA_STATUSCODE_GOOD)
            c_ns = 0;
        for(size_t i = 0; i < CHECK_COUNT; i++) {
            if(!selected(checks, s_checks[i].name))
                continue;
            if(UA_DateTime_nowMonotonic() > deadline) {
                printf("INFO the checks did not complete in time\n");
                break;
            }
            UA_DateTime t = UA_DateTime_nowMonotonic();
            report(s_checks[i].name, s_checks[i].fn(), t);
            ensureConnected();
        }
        UA_DateTime t = UA_DateTime_nowMonotonic();
        res = UA_Client_disconnect(c_client);
        if(res != UA_STATUSCODE_GOOD)
            snprintf(c_msg, sizeof(c_msg), "CloseSession returned %s", UA_StatusCode_name(res));
        report("CloseSession", res == UA_STATUSCODE_GOOD ? NULL : c_msg, t);
    }
    UA_Client_delete(c_client);
    UA_ByteString_clear(&certificate);
    UA_ByteString_clear(&privateKey);
    printf("SUMMARY passed=%d failed=%d\n", c_passed, c_failed);
    return c_failed == 0 ? EXIT_SUCCESS : EXIT_CHECKS_FAILED;
}
