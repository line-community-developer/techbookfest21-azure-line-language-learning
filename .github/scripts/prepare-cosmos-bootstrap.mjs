#!/usr/bin/env node
"use strict";

import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";

const root = process.cwd();
const require = createRequire(path.join(root, "package.json"));
const ts = require("typescript");
const MAX_DOCUMENTS = 500;
const MAX_BYTES = 4194304;
const SEED_NAME_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/;
const SYSTEM_PROPERTIES = new Set(["_rid", "_self", "_etag", "_attachments", "_ts"]);
const BACKEND = "csharp";
const FUNCTION_TEMPLATE = "using System.Net;\nusing System.Security.Cryptography;\nusing System.Text;\nusing Azure.Identity;\nusing Microsoft.Azure.Cosmos;\nusing Microsoft.Azure.Functions.Worker;\nusing Microsoft.Azure.Functions.Worker.Http;\nusing Newtonsoft.Json.Linq;\n\nnamespace SwallowKit.Functions;\n\npublic sealed class BootstrapSeedFunction\n{\n    private const string ExpectedTokenHash = \"__TOKEN_HASH__\";\n    private const long TokenExpiresAt = __EXPIRES_AT__;\n    private const bool Force = __FORCE__;\n    private const string EncodedSeed = \"__SEED_BASE64__\";\n    private const string DatabaseName = __DATABASE_NAME__;\n    private const int ProcessingLimitSeconds = 180;\n    private static readonly HashSet<string> SystemProperties = new(StringComparer.Ordinal)\n    {\n        \"_rid\", \"_self\", \"_etag\", \"_attachments\", \"_ts\"\n    };\n\n    [Function(\"seedBootstrap\")]\n    public async Task<HttpResponseData> Run(\n        [HttpTrigger(AuthorizationLevel.Anonymous, \"get\", \"post\", Route = \"seed-bootstrap\")] HttpRequestData request)\n    {\n        var authHeader = request.Headers.TryGetValues(\"Authorization\", out var values)\n            ? values.FirstOrDefault()\n            : null;\n        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith(\"Bearer \", StringComparison.OrdinalIgnoreCase))\n        {\n            return await WriteResponseAsync(request, HttpStatusCode.Unauthorized, new { errorCategory = \"authentication\" });\n        }\n\n        var token = authHeader[\"Bearer \".Length..];\n        if (token.Length == 0 || DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= TokenExpiresAt)\n        {\n            return await WriteResponseAsync(request, HttpStatusCode.Unauthorized, new { errorCategory = token.Length == 0 ? \"authentication\" : \"expired\" });\n        }\n\n        byte[] expectedHash;\n        try\n        {\n            expectedHash = Convert.FromHexString(ExpectedTokenHash);\n        }\n        catch (FormatException)\n        {\n            return await WriteResponseAsync(request, HttpStatusCode.InternalServerError, new { errorCategory = \"configuration\" });\n        }\n        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));\n        if (expectedHash.Length != suppliedHash.Length || !CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash))\n        {\n            return await WriteResponseAsync(request, HttpStatusCode.Unauthorized, new { errorCategory = \"authentication\" });\n        }\n\n        if (string.Equals(request.Method, \"GET\", StringComparison.OrdinalIgnoreCase))\n        {\n            return await WriteResponseAsync(request, HttpStatusCode.OK, new { ready = true });\n        }\n\n        var started = DateTimeOffset.UtcNow;\n        var created = 0;\n        var upserted = 0;\n        var skipped = 0;\n        var conflicts = 0;\n        var failed = 0;\n        string? activeContainerName = null;\n        int? activeSeedDocumentIndex = null;\n        var operation = \"create-client\";\n\n        try\n        {\n            using var client = CreateCosmosClient();\n            operation = \"decode-seed\";\n            var containers = JArray.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(EncodedSeed)));\n            foreach (var containerSeed in containers.OfType<JObject>())\n            {\n                var containerName = containerSeed.Value<string>(\"containerName\")!;\n                activeContainerName = containerName;\n                activeSeedDocumentIndex = null;\n                operation = \"get-container\";\n                var partitionKeyField = containerSeed.Value<string>(\"partitionKeyField\")!;\n                var container = client.GetContainer(\n                    Environment.GetEnvironmentVariable(\"COSMOS_DB_DATABASE_NAME\") ?? DatabaseName,\n                    containerName);\n\n                foreach (var item in containerSeed[\"documents\"]!.OfType<JObject>())\n                {\n                    activeSeedDocumentIndex = ((JArray)containerSeed[\"documents\"]!).IndexOf(item);\n                    operation = \"validate-item\";\n                    if ((DateTimeOffset.UtcNow - started).TotalSeconds >= ProcessingLimitSeconds)\n                    {\n                        return await WriteResponseAsync(request, HttpStatusCode.GatewayTimeout, new\n                        {\n                            created, upserted, skipped, conflicts, failed, errorCategory = \"processing-time-limit\"\n                        });\n                    }\n\n                    var id = item.Value<string>(\"id\");\n                    var partitionValue = item[partitionKeyField];\n                    if (string.IsNullOrWhiteSpace(id) ||\n                        partitionValue is null ||\n                        partitionValue.Type is JTokenType.Null or JTokenType.Object or JTokenType.Array)\n                    {\n                        failed++;\n                        return await WriteResponseAsync(request, HttpStatusCode.BadRequest, new\n                        {\n                            created, upserted, skipped, conflicts, failed, errorCategory = \"invalid-seed\"\n                        });\n                    }\n\n                    var partitionKey = ToPartitionKey(partitionValue);\n                    if (Force)\n                    {\n                        operation = \"upsert-item\";\n                        await container.UpsertItemAsync(item, partitionKey);\n                        upserted++;\n                        continue;\n                    }\n                    operation = \"read-item\";\n                    var existing = await ReadExistingAsync(container, id, partitionKey);\n                    if (existing is not null)\n                    {\n                        if (DocumentsEqual(existing, item))\n                        {\n                            skipped++;\n                            continue;\n                        }\n                        conflicts++;\n                        return await WriteResponseAsync(request, HttpStatusCode.Conflict, new\n                        {\n                            created, upserted, skipped, conflicts, failed, errorCategory = \"data-conflict\",\n                            containerName, seedDocumentIndex = ((JArray)containerSeed[\"documents\"]!).IndexOf(item)\n                        });\n                    }\n\n                    try\n                    {\n                        operation = \"create-item\";\n                        await container.CreateItemAsync(item, partitionKey);\n                        created++;\n                    }\n                    catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)\n                    {\n                        operation = \"read-after-conflict\";\n                        var racedItem = await ReadExistingAsync(container, id, partitionKey);\n                        if (racedItem is not null && DocumentsEqual(racedItem, item))\n                        {\n                            skipped++;\n                            continue;\n                        }\n                        conflicts++;\n                        return await WriteResponseAsync(request, HttpStatusCode.Conflict, new\n                        {\n                            created, upserted, skipped, conflicts, failed, errorCategory = \"data-conflict\",\n                            containerName, seedDocumentIndex = ((JArray)containerSeed[\"documents\"]!).IndexOf(item)\n                        });\n                    }\n                }\n            }\n\n            if ((DateTimeOffset.UtcNow - started).TotalSeconds >= ProcessingLimitSeconds)\n            {\n                return await WriteResponseAsync(request, HttpStatusCode.GatewayTimeout, new\n                {\n                    created, upserted, skipped, conflicts, failed, errorCategory = \"processing-time-limit\"\n                });\n            }\n            return await WriteResponseAsync(request, HttpStatusCode.OK, new { created, upserted, skipped, conflicts, failed });\n        }\n        catch (Exception error)\n        {\n            failed++;\n            return await WriteResponseAsync(request, HttpStatusCode.ServiceUnavailable, new\n            {\n                created, upserted, skipped, conflicts, failed, errorCategory = \"write-outcome-unknown\",\n                containerName = activeContainerName, seedDocumentIndex = activeSeedDocumentIndex,\n                operation, elapsedMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,\n                diagnostics = BuildFailureDetails(error)\n            });\n        }\n    }\n\n    private static object BuildFailureDetails(Exception error)\n    {\n        var cosmosError = error as CosmosException;\n        return new\n        {\n            exceptionType = error.GetType().Name,\n            innerExceptionType = error.InnerException?.GetType().Name,\n            cosmosStatusCode = cosmosError is null ? (int?)null : (int)cosmosError.StatusCode,\n            cosmosSubStatusCode = cosmosError?.SubStatusCode,\n            activityId = cosmosError?.ActivityId,\n            retryAfterMs = cosmosError?.RetryAfter?.TotalMilliseconds\n        };\n    }\n\n    private static CosmosClient CreateCosmosClient()\n    {\n        var endpoint = Environment.GetEnvironmentVariable(\"CosmosDBConnection__accountEndpoint\");\n        if (!string.IsNullOrWhiteSpace(endpoint))\n        {\n            return new CosmosClient(endpoint, new DefaultAzureCredential(), new CosmosClientOptions\n            {\n                RequestTimeout = TimeSpan.FromSeconds(10),\n                MaxRetryAttemptsOnRateLimitedRequests = 2,\n                MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(10),\n            });\n        }\n        var connectionString = Environment.GetEnvironmentVariable(\"CosmosDBConnection\");\n        if (string.IsNullOrWhiteSpace(connectionString))\n        {\n            throw new InvalidOperationException(\"Cosmos DB connection is not configured.\");\n        }\n        return new CosmosClient(connectionString, new CosmosClientOptions\n        {\n            RequestTimeout = TimeSpan.FromSeconds(10),\n            MaxRetryAttemptsOnRateLimitedRequests = 2,\n            MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(10),\n        });\n    }\n\n    private static PartitionKey ToPartitionKey(JToken value)\n    {\n        return value.Type switch\n        {\n            JTokenType.String => new PartitionKey(value.Value<string>()!),\n            JTokenType.Integer or JTokenType.Float => new PartitionKey(value.Value<double>()),\n            JTokenType.Boolean => new PartitionKey(value.Value<bool>()),\n            _ => throw new InvalidOperationException(\"Unsupported partition key value.\"),\n        };\n    }\n\n    private static async Task<JObject?> ReadExistingAsync(Container container, string id, PartitionKey partitionKey)\n    {\n        try\n        {\n            var response = await container.ReadItemAsync<JObject>(id, partitionKey);\n            return response.Resource;\n        }\n        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)\n        {\n            return null;\n        }\n    }\n\n    private static bool DocumentsEqual(JObject left, JObject right) =>\n        JToken.DeepEquals(Normalize(left), Normalize(right));\n\n    private static JToken? Normalize(JToken? node)\n    {\n        if (node is JObject obj)\n        {\n            var normalized = new JObject();\n            foreach (var entry in obj.Properties().OrderBy(entry => entry.Name, StringComparer.Ordinal))\n            {\n                if (!SystemProperties.Contains(entry.Name))\n                {\n                    normalized[entry.Name] = Normalize(entry.Value);\n                }\n            }\n            return normalized;\n        }\n        if (node is JArray array)\n        {\n            var normalized = new JArray();\n            foreach (var item in array)\n            {\n                normalized.Add(Normalize(item) ?? JValue.CreateNull());\n            }\n            return normalized;\n        }\n        return node?.DeepClone();\n    }\n\n    private static async Task<HttpResponseData> WriteResponseAsync(\n        HttpRequestData request,\n        HttpStatusCode status,\n        object body)\n    {\n        var response = request.CreateResponse(status);\n        await response.WriteAsJsonAsync(body);\n        return response;\n    }\n}\n";
const seedName = process.argv[2] || "";
const tokenHash = process.env.BOOTSTRAP_TOKEN_HASH || "";
const expiresAt = Number(process.env.BOOTSTRAP_EXPIRES_AT || 0);
const force = process.env.BOOTSTRAP_FORCE || "false";

function fail(message) {
  console.error("Bootstrap seed preparation failed: " + message);
  process.exit(1);
}

if (!SEED_NAME_PATTERN.test(seedName)) {
  fail("seed_name must start with an alphanumeric character and contain only letters, numbers, hyphens, or underscores (max 64 characters).");
}
if (force !== "true" && force !== "false") {
  fail("BOOTSTRAP_FORCE must be true or false.");
}
if (!/^[a-f0-9]{64}$/.test(tokenHash) || !Number.isSafeInteger(expiresAt) || expiresAt <= Math.floor(Date.now() / 1000)) {
  fail("temporary authentication settings are missing or invalid.");
}

const seedsRoot = path.resolve(root, "dev-seeds");
const seedDirectory = path.resolve(seedsRoot, seedName);
const relativeSeedDirectory = path.relative(seedsRoot, seedDirectory);
if (relativeSeedDirectory.startsWith("..") || path.isAbsolute(relativeSeedDirectory)) {
  fail("seed_name resolves outside dev-seeds.");
}
if (!fs.existsSync(seedsRoot) || fs.lstatSync(seedsRoot).isSymbolicLink() || !fs.statSync(seedsRoot).isDirectory()) {
  fail("dev-seeds must be a regular directory inside the project.");
}
if (!fs.existsSync(seedDirectory) || fs.lstatSync(seedDirectory).isSymbolicLink() || !fs.statSync(seedDirectory).isDirectory()) {
  fail("the selected dev-seeds directory does not exist.");
}
const realSeedsRoot = fs.realpathSync(seedsRoot);
const realSeedDirectory = fs.realpathSync(seedDirectory);
const relativeRealSeedDirectory = path.relative(realSeedsRoot, realSeedDirectory);
if (relativeRealSeedDirectory.startsWith("..") || path.isAbsolute(relativeRealSeedDirectory)) {
  fail("the selected seed directory resolves outside dev-seeds.");
}

function walk(directory) {
  return fs.readdirSync(directory, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .flatMap((entry) => walk(path.join(directory, entry.name)))
    .concat(fs.readdirSync(directory, { withFileTypes: true })
      .filter((entry) => entry.isFile() && entry.name.endsWith(".ts") && !entry.name.endsWith(".d.ts"))
      .map((entry) => path.join(directory, entry.name)));
}

function normalize(value) {
  return value.toLowerCase().replace(/[^a-z0-9]/g, "");
}

function pascalCase(value) {
  return value.split(/[^A-Za-z0-9]+/).filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1)).join("");
}

function modelDefinition(filePath, modelsRoot) {
  const content = fs.readFileSync(filePath, "utf8");
  const source = ts.createSourceFile(filePath, content, ts.ScriptTarget.Latest, true);
  const schemas = [];
  let partitionKey = "/id";
  for (const statement of source.statements) {
    if (!ts.isVariableStatement(statement) ||
        !statement.modifiers ||
        !statement.modifiers.some((modifier) => modifier.kind === ts.SyntaxKind.ExportKeyword)) continue;
    for (const declaration of statement.declarationList.declarations) {
      if (!ts.isIdentifier(declaration.name)) continue;
      if (declaration.name.text === "partitionKey" && declaration.initializer && ts.isStringLiteral(declaration.initializer)) {
        partitionKey = declaration.initializer.text;
      }
      if (!declaration.initializer ||
          !ts.isCallExpression(declaration.initializer) ||
          !ts.isPropertyAccessExpression(declaration.initializer.expression) ||
          declaration.initializer.expression.name.text !== "object") continue;
      schemas.push(declaration.name.text);
    }
  }
  const modelName = pascalCase(path.basename(filePath, ".ts"));
  const exact = schemas.filter((name) => {
    const normalizedName = name.toLowerCase();
    return normalizedName === modelName.toLowerCase() ||
      normalizedName === (modelName + "schema").toLowerCase();
  });
  const schemaName = exact.length === 1 ? exact[0] : schemas.length === 1 ? schemas[0] : null;
  if (!schemaName) fail("could not uniquely identify the Zod object schema for " + filePath + ".");
  if (!partitionKey.startsWith("/") || partitionKey.slice(1).length === 0 || partitionKey.slice(1).includes("/")) {
    fail("unsupported Cosmos partition key path in " + filePath + ".");
  }
  const relativeModel = path.relative(modelsRoot, filePath).replace(/\.ts$/, ".js");
  const compiledModel = path.join(root, "shared", "dist", "models", relativeModel);
  if (!fs.existsSync(compiledModel)) fail("compiled model module is missing: " + relativeModel);
  const modelModule = require(compiledModel);
  const schema = modelModule[schemaName];
  if (!schema || typeof schema.safeParse !== "function") {
    fail("compiled Zod schema is missing for " + filePath + ".");
  }
  return {
    modelName,
    schemaName,
    schema,
    partitionKeyField: partitionKey.slice(1),
    aliases: [modelName, schemaName, path.basename(filePath, ".ts")],
  };
}

const modelsRoot = path.resolve(root, "shared", "models");
if (!fs.existsSync(modelsRoot)) fail("shared/models was not found.");
const models = walk(modelsRoot).map((filePath) => modelDefinition(filePath, modelsRoot));
const modelAliases = new Map();
for (const model of models) {
  for (const alias of model.aliases) {
    const key = normalize(alias);
    const existing = modelAliases.get(key);
    if (existing && existing !== model) fail("model name aliases are ambiguous: " + alias + ".");
    modelAliases.set(key, model);
  }
}

const jsonFiles = fs.readdirSync(seedDirectory)
  .filter((entry) => entry.toLowerCase().endsWith(".json"))
  .sort();
if (jsonFiles.length === 0) fail("the selected dev-seeds directory contains no JSON files.");

let totalDocuments = 0;
let totalBytes = 0;
const containers = [];
for (const fileName of jsonFiles) {
  const filePath = path.join(seedDirectory, fileName);
  const stat = fs.lstatSync(filePath);
  if (!stat.isFile() || stat.isSymbolicLink() || path.dirname(fs.realpathSync(filePath)) !== fs.realpathSync(seedDirectory)) {
    fail("seed JSON files must be regular files inside the selected directory.");
  }
  totalBytes += stat.size;
  const model = modelAliases.get(normalize(path.basename(fileName, path.extname(fileName))));
  if (!model) fail("seed file " + fileName + " does not match a model in shared/models.");
  let parsed;
  try {
    parsed = JSON.parse(fs.readFileSync(filePath, "utf8"));
  } catch {
    fail("seed file " + fileName + " is not valid JSON.");
  }
  const documents = Array.isArray(parsed) ? parsed : [parsed];
  if (documents.some((document) => !document || typeof document !== "object" || Array.isArray(document))) {
    fail("seed file " + fileName + " must contain only JSON objects.");
  }
  const seenIds = new Set();
  const validatedDocuments = documents.map((document, index) => {
    const clean = Object.fromEntries(Object.entries(document).filter(([key]) => !SYSTEM_PROPERTIES.has(key)));
    if (typeof clean.id !== "string" || clean.id.trim().length === 0) {
      fail(fileName + " item " + index + " must have a non-empty string id.");
    }
    if (seenIds.has(clean.id)) fail(fileName + " contains duplicate ids.");
    seenIds.add(clean.id);
    if (!Object.prototype.hasOwnProperty.call(clean, model.partitionKeyField) || clean[model.partitionKeyField] === null) {
      fail(fileName + " item " + index + " is missing partition key " + model.partitionKeyField + ".");
    }
    const result = model.schema.safeParse(clean);
    if (!result.success) {
      const issue = result.error.issues[0];
      fail(fileName + " item " + index + " failed Zod validation at " + (issue.path.join(".") || "<root>") + ".");
    }
    return result.data;
  });
  totalDocuments += validatedDocuments.length;
  const containerName = model.modelName.endsWith("s") ? model.modelName : model.modelName + "s";
  containers.push({ containerName, partitionKeyField: model.partitionKeyField, documents: validatedDocuments });
}

if (totalDocuments === 0) fail("the selected seed contains no documents.");
if (totalDocuments > MAX_DOCUMENTS) fail("seed has " + totalDocuments + " documents; limit is " + MAX_DOCUMENTS + ".");
if (totalBytes > MAX_BYTES) fail("seed files total " + totalBytes + " bytes; limit is " + MAX_BYTES + ".");

const encodedSeed = Buffer.from(JSON.stringify(containers), "utf8").toString("base64");
const packageName = JSON.parse(fs.readFileSync(path.join(root, "package.json"), "utf8")).name || "App";
const databaseName = packageName.charAt(0).toUpperCase() + packageName.slice(1) + "Database";
const generatedFunction = FUNCTION_TEMPLATE
  .replaceAll("__TOKEN_HASH__", tokenHash)
  .replaceAll("__EXPIRES_AT__", String(expiresAt))
  .replaceAll("__FORCE__", force)
  .replaceAll("__FORCE_PYTHON__", force === "true" ? "True" : "False")
  .replaceAll("__DATABASE_NAME__", JSON.stringify(databaseName))
  .replaceAll("__SEED_BASE64__", encodedSeed);

if (BACKEND === "csharp") {
  const target = path.join(root, "functions", "Crud", "BootstrapSeedFunction.cs");
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, generatedFunction, "utf8");
} else if (BACKEND === "python") {
  const target = path.join(root, "functions", "blueprints", "bootstrap_seed.py");
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, generatedFunction, "utf8");
  const appPath = path.join(root, "functions", "function_app.py");
  let app = fs.readFileSync(appPath, "utf8");
  const registration = "from blueprints.bootstrap_seed import bp as bootstrap_seed_bp\napp.register_blueprint(bootstrap_seed_bp)\n";
  const marker = "# SwallowKit scaffold registrations";
  if (!app.includes(marker)) fail("function_app.py is missing the SwallowKit registration marker.");
  app = app.replace(marker, registration + marker);
  fs.writeFileSync(appPath, app, "utf8");
} else {
  const target = path.join(root, "functions", "src", "bootstrap-seed.ts");
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, generatedFunction, "utf8");
}

console.log("Prepared " + totalDocuments + " validated seed document(s) across " + containers.length + " container(s).");
