/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

/*
 * open62541 interop peer: the same command line and stdout contract as
 * tests/Opc.Ua.Interop.LegacyPeer, so the 2.0 interop tests can drive it.
 *
 *   server --port <p> --pki <dir> [--kind interop] [--ecc true] [--init-only true]
 *   client --url <u> --pki <dir> [--policy <uri>] [--mode <m>] [--user u --password p]
 *          [--checks a,b] [--expect-connect-error A,B] [--ecc true]
 *          [--token-lifetime ms] [--token-test-seconds s] [--timeout-seconds s]
 *
 * PKI layout (shared with the .NET peers): <pki>/own/certs/<name>.der and
 * <pki>/own/private/<name>.der; with --ecc one ECC certificate per curve is
 * added as <name>-<curve>.der.
 */

#include <open62541/client.h>
#include <open62541/client_config_default.h>
#include <open62541/client_highlevel.h>
#include <open62541/client_highlevel_async.h>
#include <open62541/client_subscriptions.h>
#include <open62541/plugin/accesscontrol_default.h>
#include <open62541/plugin/certificategroup_default.h>
#include <open62541/plugin/create_certificate.h>
#include <open62541/plugin/log_stdout.h>
#include <open62541/plugin/securitypolicy_default.h>
#include <open62541/server.h>
#include <open62541/server_config_default.h>

#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#ifdef _WIN32
#include <direct.h>
#include <windows.h>
#define MKDIR(p) _mkdir(p)
#else
#include <pthread.h>
#include <sys/stat.h>
#include <unistd.h>
#define MKDIR(p) mkdir(p, 0755)
#endif

#define EXIT_CHECKS_FAILED 1
#define EXIT_USAGE 2
#define EXIT_FATAL 3

#define INTEROP_NAMESPACE "urn:opcfoundation.org:interop:legacy"
#define REFERENCE_NAMESPACE "http://opcfoundation.org/Quickstarts/ReferenceServer"
#define USER_NAME "interop"
#define PASSWORD "interop-password"
#define POLICY_PREFIX "http://opcfoundation.org/UA/SecurityPolicy#"

/* ------------------------------------------------------------------ options */

#define MAX_OPTIONS 32
static const char *s_names[MAX_OPTIONS];
static const char *s_values[MAX_OPTIONS];
static int s_count;

static const char *opt(const char *name, const char *defaultValue) {
    for(int i = 0; i < s_count; i++)
        if(strcmp(s_names[i], name) == 0)
            return s_values[i];
    return defaultValue;
}

static int flag(const char *name, int defaultValue) {
    const char *v = opt(name, NULL);
    if(!v)
        return defaultValue;
    return strcmp(v, "true") == 0 || strcmp(v, "True") == 0;
}

/* ------------------------------------------------------------------ logging */

/* SecurityToken renewals of the client's SecureChannel, counted from the
 * client's log, since open62541 has no public API for the token. */
static int s_renewals;

/* stdout carries the protocol; warnings and errors go to stderr. */
static void logToStderr(void *context, UA_LogLevel level, UA_LogCategory category,
                        const char *msg, va_list args) {
    (void)context;
    if(category == UA_LOGCATEGORY_SECURECHANNEL && strstr(msg, "SecureChannel renewed"))
        s_renewals++;
    if(level < UA_LOGLEVEL_WARNING)
        return;
    vfprintf(stderr, msg, args);
    fputc('\n', stderr);
}

static UA_Logger s_logger = {logToStderr, NULL, NULL};

/* ------------------------------------------------------------------ files */

static void makeDirs(const char *path) {
    char buf[1024];
    snprintf(buf, sizeof(buf), "%s", path);
    for(char *p = buf + 1; *p; p++) {
        if(*p == '/' || *p == '\\') {
            char c = *p;
            *p = 0;
            MKDIR(buf);
            *p = c;
        }
    }
    MKDIR(buf);
}

static UA_ByteString readFile(const char *path) {
    UA_ByteString result = UA_BYTESTRING_NULL;
    FILE *f = fopen(path, "rb");
    if(!f)
        return result;
    fseek(f, 0, SEEK_END);
    long size = ftell(f);
    fseek(f, 0, SEEK_SET);
    if(size > 0 && UA_ByteString_allocBuffer(&result, (size_t)size) == UA_STATUSCODE_GOOD) {
        if(fread(result.data, 1, (size_t)size, f) != (size_t)size)
            UA_ByteString_clear(&result);
    }
    fclose(f);
    return result;
}

static int writeFile(const char *path, const UA_ByteString *data) {
    FILE *f = fopen(path, "wb");
    if(!f)
        return 0;
    size_t written = fwrite(data->data, 1, data->length, f);
    fclose(f);
    return written == data->length;
}

/* Loads or creates the application certificate in the .NET PKI layout. */
/* curve: NULL for RSA 2048, else a curve name of UA_CreateCertificate
 * (prime256v1, secp384r1, brainpoolP256r1, brainpoolP384r1 and, with
 * OpenSSL, ed25519 and ed448); one certificate per curve. */
static UA_StatusCode
loadOrCreateIdentity(const char *pki, const char *name, const char *applicationUri,
                     const char *curve, UA_ByteString *certificate, UA_ByteString *privateKey) {
    char certs[1024], keys[1024], certPath[1200], keyPath[1200];
    snprintf(certs, sizeof(certs), "%s/own/certs", pki);
    snprintf(keys, sizeof(keys), "%s/own/private", pki);
    makeDirs(certs);
    makeDirs(keys);
    snprintf(certPath, sizeof(certPath), "%s/%s%s%s.der", certs, name, curve ? "-" : "", curve ? curve : "");
    snprintf(keyPath, sizeof(keyPath), "%s/%s%s%s.der", keys, name, curve ? "-" : "", curve ? curve : "");
    *certificate = readFile(certPath);
    *privateKey = readFile(keyPath);
    if(certificate->length > 0 && privateKey->length > 0)
        return UA_STATUSCODE_GOOD;
    UA_ByteString_clear(certificate);
    UA_ByteString_clear(privateKey);

    char cn[256], uri[512];
    snprintf(cn, sizeof(cn), "CN=%s", name);
    snprintf(uri, sizeof(uri), "URI:%s", applicationUri);
    UA_String subject[2] = {UA_STRING_STATIC("O=OPC Foundation"), UA_STRING(cn)};
    UA_String san[2] = {UA_STRING(uri), UA_STRING_STATIC("DNS:localhost")};
    UA_KeyValueMap *params = UA_KeyValueMap_new();
    UA_UInt16 bits = 2048;
    UA_KeyValueMap_setScalar(params, UA_QUALIFIEDNAME(0, "key-size-bits"), &bits, &UA_TYPES[UA_TYPES_UINT16]);
    if(curve) {
        UA_String keyType = UA_STRING_STATIC("EC");
        UA_String curveName = UA_STRING((char *)curve);
        UA_KeyValueMap_setScalar(params, UA_QUALIFIEDNAME(0, "key-type"), &keyType, &UA_TYPES[UA_TYPES_STRING]);
        UA_KeyValueMap_setScalar(params, UA_QUALIFIEDNAME(0, "ecc-curve"), &curveName, &UA_TYPES[UA_TYPES_STRING]);
    }
    UA_StatusCode res = UA_CreateCertificate(&s_logger, subject, 2, san, 2,
                                             UA_CERTIFICATEFORMAT_DER, params,
                                             privateKey, certificate);
    UA_KeyValueMap_delete(params);
    if(res != UA_STATUSCODE_GOOD)
        return res;
    if(!writeFile(certPath, certificate) || !writeFile(keyPath, privateKey))
        return UA_STATUSCODE_BADINTERNALERROR;
    return UA_STATUSCODE_GOOD;
}

/* The ECC policies the peer offers with --ecc, and the curves of their
 * certificates: the four ECC policies of OPC UA 1.05 with both crypto
 * backends, and the AEAD and Edwards curve policies that open62541 1.5
 * implements only with OpenSSL. */
typedef UA_StatusCode (*EccPolicyFn)(UA_SecurityPolicy *, const UA_ApplicationType, const UA_ByteString,
                                     const UA_ByteString, const UA_Logger *);
typedef UA_StatusCode (*EccServerFn)(UA_ServerConfig *, const UA_ByteString *, const UA_ByteString *);
static const struct {
    const char *policy;
    const char *curve;
    EccPolicyFn create; /* for the client, whose defaults have none of the 1.05 ECC policies */
    EccServerFn add;
} s_ecc[] = {
    {POLICY_PREFIX "ECC_nistP256", "prime256v1", UA_SecurityPolicy_EccNistP256,
     UA_ServerConfig_addSecurityPolicyEccNistP256},
    {POLICY_PREFIX "ECC_nistP384", "secp384r1", UA_SecurityPolicy_EccNistP384,
     UA_ServerConfig_addSecurityPolicyEccNistP384},
    {POLICY_PREFIX "ECC_brainpoolP256r1", "brainpoolP256r1", UA_SecurityPolicy_EccBrainpoolP256r1,
     UA_ServerConfig_addSecurityPolicyEccBrainpoolP256r1},
    {POLICY_PREFIX "ECC_brainpoolP384r1", "brainpoolP384r1", UA_SecurityPolicy_EccBrainpoolP384r1,
     UA_ServerConfig_addSecurityPolicyEccBrainpoolP384r1},
#ifdef UA_ENABLE_ENCRYPTION_OPENSSL
    {POLICY_PREFIX "ECC_nistP256_AesGcm", "prime256v1", UA_SecurityPolicy_EccNistP256AesGcm,
     UA_ServerConfig_addSecurityPolicyEccNistP256AesGcm},
    {POLICY_PREFIX "ECC_nistP256_ChaChaPoly", "prime256v1", UA_SecurityPolicy_EccNistP256ChaChaPoly,
     UA_ServerConfig_addSecurityPolicyEccNistP256ChaChaPoly},
    {POLICY_PREFIX "ECC_curve25519", "ed25519", UA_SecurityPolicy_EccCurve25519,
     UA_ServerConfig_addSecurityPolicyEccCurve25519},
    {POLICY_PREFIX "ECC_curve448", "ed448", UA_SecurityPolicy_EccCurve448,
     UA_ServerConfig_addSecurityPolicyEccCurve448},
#endif
};
#define ECC_POLICIES ((int)(sizeof(s_ecc) / sizeof(s_ecc[0])))

/* ------------------------------------------------------------------ JSON */

static void jsonString(FILE *out, const char *s) {
    fputc('"', out);
    for(; *s; s++) {
        unsigned char c = (unsigned char)*s;
        if(c == '"' || c == '\\')
            fprintf(out, "\\%c", c);
        else if(c == '\n')
            fputs("\\n", out);
        else if(c == '\r')
            fputs("\\r", out);
        else if(c == '\t')
            fputs("\\t", out);
        else if(c < 0x20)
            fprintf(out, "\\u%04x", c);
        else
            fputc(c, out);
    }
    fputc('"', out);
}

/* ------------------------------------------------------------------ stdin */

static volatile UA_Boolean s_running = true;

#ifdef _WIN32
static DWORD WINAPI watchStdin(LPVOID arg) {
#else
static void *watchStdin(void *arg) {
#endif
    (void)arg;
    char line[256];
    while(fgets(line, sizeof(line), stdin)) {
        if(strncmp(line, "stop", 4) == 0)
            break;
    }
    s_running = false;
    return 0;
}

static void startStdinWatcher(void) {
#ifdef _WIN32
    CreateThread(NULL, 0, watchStdin, NULL, 0, NULL);
#else
    pthread_t thread;
    pthread_create(&thread, NULL, watchStdin, NULL);
    pthread_detach(thread);
#endif
}

/* ------------------------------------------------------------------ server */

static UA_UInt16 s_ns;
static UA_Int32 s_counter;

static UA_StatusCode
addMethodCallback(UA_Server *server, const UA_NodeId *sessionId, void *sessionContext,
                  const UA_NodeId *methodId, void *methodContext, const UA_NodeId *objectId,
                  void *objectContext, size_t inputSize, const UA_Variant *input,
                  size_t outputSize, UA_Variant *output) {
    (void)server; (void)sessionId; (void)sessionContext; (void)methodId;
    (void)methodContext; (void)objectId; (void)objectContext; (void)inputSize; (void)outputSize;
    UA_Int32 sum = *(UA_Int32 *)input[0].data + *(UA_Int32 *)input[1].data;
    return UA_Variant_setScalarCopy(output, &sum, &UA_TYPES[UA_TYPES_INT32]);
}

/* RaiseEvent(): reports one BaseEventType event with the Interop folder as
 * SourceNode. open62541 emits every event also through the Server object. */
static UA_StatusCode
raiseEventCallback(UA_Server *server, const UA_NodeId *sessionId, void *sessionContext,
                   const UA_NodeId *methodId, void *methodContext, const UA_NodeId *objectId,
                   void *objectContext, size_t inputSize, const UA_Variant *input,
                   size_t outputSize, UA_Variant *output) {
    (void)sessionId; (void)sessionContext; (void)methodId; (void)methodContext;
    (void)objectId; (void)objectContext; (void)inputSize; (void)input; (void)outputSize; (void)output;
    return UA_Server_createEvent(server, UA_NODEID_STRING(s_ns, "Interop"),
                                 UA_NODEID_NUMERIC(0, UA_NS0ID_BASEEVENTTYPE), 500,
                                 UA_LOCALIZEDTEXT("", "interop event"), NULL, NULL, NULL);
}

static void addVariable(UA_Server *server, const char *name, const UA_DataType *type,
                        const void *value, size_t arrayLength, UA_Boolean writable) {
    UA_VariableAttributes attr = UA_VariableAttributes_default;
    attr.displayName = UA_LOCALIZEDTEXT("en", (char *)name);
    attr.dataType = type->typeId;
    attr.accessLevel = UA_ACCESSLEVELMASK_READ | (writable ? UA_ACCESSLEVELMASK_WRITE : 0);
    attr.userAccessLevel = attr.accessLevel;
    if(arrayLength > 0) {
        attr.valueRank = UA_VALUERANK_ONE_DIMENSION;
        UA_UInt32 dims[1] = {0};
        attr.arrayDimensions = dims;
        attr.arrayDimensionsSize = 1;
        UA_Variant_setArray(&attr.value, (void *)(uintptr_t)value, arrayLength, type);
    } else {
        attr.valueRank = UA_VALUERANK_SCALAR;
        UA_Variant_setScalar(&attr.value, (void *)(uintptr_t)value, type);
    }
    UA_Server_addVariableNode(server, UA_NODEID_STRING(s_ns, (char *)name),
                              UA_NODEID_STRING(s_ns, "Interop"),
                              UA_NODEID_NUMERIC(0, UA_NS0ID_ORGANIZES),
                              UA_QUALIFIEDNAME(s_ns, (char *)name),
                              UA_NODEID_NUMERIC(0, UA_NS0ID_BASEDATAVARIABLETYPE), attr, NULL, NULL);
}

static void tick(UA_Server *server, void *data) {
    (void)data;
    s_counter++;
    UA_Variant v;
    UA_Variant_setScalar(&v, &s_counter, &UA_TYPES[UA_TYPES_INT32]);
    UA_Server_writeValue(server, UA_NODEID_STRING(s_ns, "Counter"), v);
}

static void createAddressSpace(UA_Server *server) {
    s_ns = UA_Server_addNamespace(server, INTEROP_NAMESPACE);
    UA_ObjectAttributes folder = UA_ObjectAttributes_default;
    folder.displayName = UA_LOCALIZEDTEXT("en", "Interop");
    UA_Server_addObjectNode(server, UA_NODEID_STRING(s_ns, "Interop"),
                            UA_NODEID_NUMERIC(0, UA_NS0ID_OBJECTSFOLDER),
                            UA_NODEID_NUMERIC(0, UA_NS0ID_ORGANIZES),
                            UA_QUALIFIEDNAME(s_ns, "Interop"),
                            UA_NODEID_NUMERIC(0, UA_NS0ID_FOLDERTYPE), folder, NULL, NULL);

    UA_Boolean b = true;
    UA_Int32 i32 = 42;
    UA_UInt64 u64 = UA_UINT64_MAX;
    UA_Double d = 3.25;
    UA_String s = UA_STRING("legacy");
    UA_DateTimeStruct dts = {0};
    dts.year = 2024; dts.month = 1; dts.day = 2; dts.hour = 3; dts.min = 4; dts.sec = 5;
    UA_DateTime dt = UA_DateTime_fromStruct(dts);
    UA_Guid guid;
    UA_Guid_parse(&guid, UA_STRING("8d2b5c6e-6c0e-4a4c-9f2b-0b6a3a6b1e01"));
    UA_Byte bytes[5] = {1, 2, 3, 4, 5};
    UA_ByteString bs = {5, bytes};
    UA_LocalizedText lt = UA_LOCALIZEDTEXT("de", "Hallo");
    UA_QualifiedName qn = UA_QUALIFIEDNAME(s_ns, "Name");
    UA_NodeId nid = UA_NODEID_STRING(s_ns, "Interop");
    UA_Int32 ints[3] = {1, 2, 3};
    UA_String strings[3] = {UA_STRING_STATIC("a"), UA_STRING_STATIC("b"), UA_STRING_STATIC("c")};
    UA_Range range = {0.0, 100.0};
    UA_Int32 zero = 0;

    addVariable(server, "Boolean", &UA_TYPES[UA_TYPES_BOOLEAN], &b, 0, true);
    addVariable(server, "Int32", &UA_TYPES[UA_TYPES_INT32], &i32, 0, true);
    addVariable(server, "UInt64", &UA_TYPES[UA_TYPES_UINT64], &u64, 0, true);
    addVariable(server, "Double", &UA_TYPES[UA_TYPES_DOUBLE], &d, 0, true);
    addVariable(server, "String", &UA_TYPES[UA_TYPES_STRING], &s, 0, true);
    addVariable(server, "DateTime", &UA_TYPES[UA_TYPES_DATETIME], &dt, 0, true);
    addVariable(server, "Guid", &UA_TYPES[UA_TYPES_GUID], &guid, 0, true);
    addVariable(server, "ByteString", &UA_TYPES[UA_TYPES_BYTESTRING], &bs, 0, true);
    addVariable(server, "LocalizedText", &UA_TYPES[UA_TYPES_LOCALIZEDTEXT], &lt, 0, true);
    addVariable(server, "QualifiedName", &UA_TYPES[UA_TYPES_QUALIFIEDNAME], &qn, 0, true);
    addVariable(server, "NodeId", &UA_TYPES[UA_TYPES_NODEID], &nid, 0, true);
    addVariable(server, "Int32Array", &UA_TYPES[UA_TYPES_INT32], ints, 3, true);
    addVariable(server, "StringArray", &UA_TYPES[UA_TYPES_STRING], strings, 3, true);
    addVariable(server, "Range", &UA_TYPES[UA_TYPES_RANGE], &range, 0, true);
    addVariable(server, "Counter", &UA_TYPES[UA_TYPES_INT32], &zero, 0, false);

    UA_Argument in[2], out;
    UA_Argument_init(&in[0]);
    in[0].name = UA_STRING("a");
    in[0].dataType = UA_TYPES[UA_TYPES_INT32].typeId;
    in[0].valueRank = UA_VALUERANK_SCALAR;
    in[1] = in[0];
    in[1].name = UA_STRING("b");
    UA_Argument_init(&out);
    out.name = UA_STRING("sum");
    out.dataType = UA_TYPES[UA_TYPES_INT32].typeId;
    out.valueRank = UA_VALUERANK_SCALAR;
    UA_MethodAttributes method = UA_MethodAttributes_default;
    method.displayName = UA_LOCALIZEDTEXT("en", "Add");
    method.executable = true;
    method.userExecutable = true;
    UA_Server_addMethodNode(server, UA_NODEID_STRING(s_ns, "Add"), UA_NODEID_STRING(s_ns, "Interop"),
                            UA_NODEID_NUMERIC(0, UA_NS0ID_HASCOMPONENT),
                            UA_QUALIFIEDNAME(s_ns, "Add"), method, addMethodCallback,
                            2, in, 1, &out, NULL, NULL);

    UA_MethodAttributes raise = UA_MethodAttributes_default;
    raise.displayName = UA_LOCALIZEDTEXT("en", "RaiseEvent");
    raise.executable = true;
    raise.userExecutable = true;
    UA_Server_addMethodNode(server, UA_NODEID_STRING(s_ns, "RaiseEvent"), UA_NODEID_STRING(s_ns, "Interop"),
                            UA_NODEID_NUMERIC(0, UA_NS0ID_HASCOMPONENT),
                            UA_QUALIFIEDNAME(s_ns, "RaiseEvent"), raise, raiseEventCallback,
                            0, NULL, 0, NULL, NULL, NULL);
    UA_Server_addRepeatedCallback(server, tick, NULL, 100, NULL);
}

static int runServer(void) {
    int port = atoi(opt("port", "0"));
    const char *pki = opt("pki", NULL);
    if(!pki || port <= 0) {
        fprintf(stderr, "error: server needs --port and --pki\n");
        return EXIT_USAGE;
    }
    if(strcmp(opt("kind", "interop"), "interop") != 0) {
        fprintf(stderr, "error: --kind %s is not supported by the open62541 peer\n", opt("kind", ""));
        return EXIT_USAGE;
    }
    const char *name = "Open62541InteropServer";
    char applicationUri[256];
    snprintf(applicationUri, sizeof(applicationUri), "urn:localhost:opcfoundation.org:%s", name);
    int ecc = flag("ecc", 0);
    UA_ByteString certificate, privateKey;
    UA_ByteString eccCertificates[ECC_POLICIES], eccKeys[ECC_POLICIES];
    memset(eccCertificates, 0, sizeof(eccCertificates));
    memset(eccKeys, 0, sizeof(eccKeys));
    UA_StatusCode res = loadOrCreateIdentity(pki, name, applicationUri, NULL, &certificate, &privateKey);
    for(int i = 0; ecc && i < ECC_POLICIES && res == UA_STATUSCODE_GOOD; i++)
        res = loadOrCreateIdentity(pki, name, applicationUri, s_ecc[i].curve, &eccCertificates[i], &eccKeys[i]);
    if(res != UA_STATUSCODE_GOOD) {
        fprintf(stderr, "FATAL creating the certificate failed: %s\n", UA_StatusCode_name(res));
        return EXIT_FATAL;
    }
    if(flag("init-only", 0)) {
        printf("PEER-PKI-READY %s\n", applicationUri);
        fflush(stdout);
        return EXIT_SUCCESS;
    }

    UA_ServerConfig serverConfig;
    memset(&serverConfig, 0, sizeof(serverConfig));
    UA_ServerConfig *config = &serverConfig;
    config->logging = &s_logger;
    UA_ServerConfig_setBasics_withPort(config, (UA_UInt16)port);
    UA_ServerConfig_addSecurityPolicyNone(config, &certificate);
    UA_ServerConfig_addSecurityPolicyBasic256Sha256(config, &certificate, &privateKey);
    UA_ServerConfig_addSecurityPolicyAes128Sha256RsaOaep(config, &certificate, &privateKey);
    UA_ServerConfig_addSecurityPolicyAes256Sha256RsaPss(config, &certificate, &privateKey);
    for(int i = 0; ecc && i < ECC_POLICIES; i++) {
        res = s_ecc[i].add(config, &eccCertificates[i], &eccKeys[i]);
        if(res != UA_STATUSCODE_GOOD)
            fprintf(stderr, "adding %s failed: %s\n", s_ecc[i].policy, UA_StatusCode_name(res));
    }
    UA_ServerConfig_addAllEndpoints(config);

    /* Peer certificates are accepted; trust tests are not run against this peer.
     * The default access control advertises and accepts X509 user tokens when
     * the session PKI can verify certificates, so any user certificate passes. */
    UA_CertificateGroup_AcceptAll(&config->secureChannelPKI);
    UA_CertificateGroup_AcceptAll(&config->sessionPKI);

    UA_UsernamePasswordLogin login = {UA_STRING_STATIC(USER_NAME), UA_STRING_STATIC(PASSWORD)};
    if(config->accessControl.clear)
        config->accessControl.clear(&config->accessControl);
    /* User name tokens are encrypted with Basic256Sha256, also on None endpoints. */
    UA_String userTokenPolicy = UA_STRING_STATIC(POLICY_PREFIX "Basic256Sha256");
    UA_AccessControl_default(config, true, &userTokenPolicy, 1, &login);

    UA_String_clear(&config->applicationDescription.applicationUri);
    config->applicationDescription.applicationUri = UA_STRING_ALLOC(applicationUri);
    UA_LocalizedText_clear(&config->applicationDescription.applicationName);
    config->applicationDescription.applicationName = UA_LOCALIZEDTEXT_ALLOC("en", name);
    char version[64];
    snprintf(version, sizeof(version), "open62541 %s", UA_OPEN62541_VER_COMMIT);
    UA_BuildInfo_clear(&config->buildInfo);
    config->buildInfo.productUri = UA_STRING_ALLOC("http://opcfoundation.org/UA/Interop/Open62541Server");
    config->buildInfo.manufacturerName = UA_STRING_ALLOC("open62541");
    config->buildInfo.productName = UA_STRING_ALLOC("open62541 Interop Server");
    config->buildInfo.softwareVersion = UA_STRING_ALLOC(version);
    config->buildInfo.buildNumber = UA_STRING_ALLOC("0");
    config->buildInfo.buildDate = UA_DateTime_now();

    /* InteropLimits of the 1.5.378 interop server. */
    config->tcpMaxMsgSize = 4 * 1024 * 1024;
    config->maxNodesPerRead = 100;
    config->maxNodesPerWrite = 100;
    config->maxNodesPerMethodCall = 100;
    config->maxNodesPerBrowse = 100;
    config->maxNodesPerTranslateBrowsePathsToNodeIds = 100;
    config->maxMonitoredItemsPerCall = 100;

    UA_Server *server = UA_Server_newWithConfig(config);
    if(!server) {
        fprintf(stderr, "FATAL creating the server failed\n");
        return EXIT_FATAL;
    }
    createAddressSpace(server);
    res = UA_Server_run_startup(server);
    if(res != UA_STATUSCODE_GOOD) {
        fprintf(stderr, "FATAL starting the server failed: %s\n", UA_StatusCode_name(res));
        UA_Server_delete(server);
        return EXIT_FATAL;
    }

    /* policies: the security policies of the server, which the client of
     * this build implements as well. */
    printf("PEER-INFO {\"stack\":\"open62541\",\"version\":\"%s\",\"applicationUri\":\"%s\",\"softwareVersion\":\"%s\","
           "\"policies\":[",
           UA_OPEN62541_VER_COMMIT, applicationUri, version);
    UA_ServerConfig *running = UA_Server_getConfig(server);
    for(size_t i = 0; i < running->securityPoliciesSize; i++) {
        const UA_String *uri = &running->securityPolicies[i].policyUri;
        size_t length = uri->length;
        while(length > 0 && uri->data[length - 1] == 0) /* some URIs count their terminator */
            length--;
        printf("%s\"%.*s\"", i > 0 ? "," : "", (int)length, (const char *)uri->data);
    }
    printf("]}\n");
    printf("PEER-SERVER-READY opc.tcp://localhost:%d\n", port);
    printf("LEGACY-SERVER-READY opc.tcp://localhost:%d\n", port);
    fflush(stdout);
    startStdinWatcher();
    while(s_running)
        UA_Server_run_iterate(server, true);
    UA_Server_run_shutdown(server);
    UA_Server_delete(server);
    UA_ByteString_clear(&certificate);
    UA_ByteString_clear(&privateKey);
    for(int i = 0; i < ECC_POLICIES; i++) {
        UA_ByteString_clear(&eccCertificates[i]);
        UA_ByteString_clear(&eccKeys[i]);
    }
    printf("PEER-SERVER-STOPPED\n");
    return EXIT_SUCCESS;
}

/* ------------------------------------------------------------------ client */

#include "checks.h"

int main(int argc, char **argv) {
    setvbuf(stdout, NULL, _IOLBF, 0);
    if(argc < 2) {
        fprintf(stderr, "error: missing command\n");
        return EXIT_USAGE;
    }
    for(int i = 2; i < argc; i += 2) {
        if(strncmp(argv[i], "--", 2) != 0 || i + 1 >= argc || s_count >= MAX_OPTIONS) {
            fprintf(stderr, "error: expected --name value, got %s\n", argv[i]);
            return EXIT_USAGE;
        }
        s_names[s_count] = argv[i] + 2;
        s_values[s_count++] = argv[i + 1];
    }
    if(strcmp(argv[1], "server") == 0)
        return runServer();
    if(strcmp(argv[1], "client") == 0)
        return runClient();
    fprintf(stderr, "error: unknown command %s\n", argv[1]);
    return EXIT_USAGE;
}
