import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import crypto from "node:crypto";
import ts from "typescript";

const root = path.resolve(process.argv[2] ?? "");
if (!root || !fs.existsSync(root) || !fs.statSync(root).isDirectory()) {
    process.stderr.write("A valid module directory is required.\n");
    process.exit(2);
}

function readJson(file) {
    try { return JSON.parse(fs.readFileSync(file, "utf8")); }
    catch { return null; }
}

function hasDependency(packageJson, name) {
    return ["dependencies", "devDependencies", "peerDependencies", "optionalDependencies"]
        .some(section => typeof packageJson?.[section]?.[name] === "string");
}

function relative(file) {
    return path.relative(root, file).split(path.sep).join("/");
}

function position(node, sourceFile) {
    const value = sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile));
    return { line: value.line + 1, column: value.character + 1 };
}

function routeValue(expression) {
    if (!expression) return { value: null, status: "Dynamic", evidence: ["route argument missing"] };
    if (ts.isStringLiteral(expression) || ts.isNoSubstitutionTemplateLiteral(expression))
        return { value: expression.text, status: "Resolved", evidence: ["literal route"] };
    if (ts.isTemplateExpression(expression))
        return { value: expression.getText(), status: "PartiallyResolved", evidence: ["template route expression"] };
    return { value: expression.getText(), status: "Dynamic", evidence: ["non-literal route expression"] };
}

function combine(prefix, suffix) {
    if (!prefix) return suffix;
    if (!suffix) return prefix;
    return prefix.replace(/\/+$/, "") + "/" + suffix.replace(/^\/+/, "");
}

function createEvidence(sourceFile, sourcePath, declaration, language, framework, provider, handler, methods, route, status, composition) {
    const pos = position(declaration, sourceFile);
    const identity = sourcePath + "|" + declaration.getStart(sourceFile) + "|" + declaration.getWidth(sourceFile) + "|" + handler;
    return {
        evidenceId: crypto.createHash("sha256").update(identity).digest("hex").toUpperCase(),
        language, framework, providerId: provider, modulePath: root,
        sourceFile: path.resolve(root, sourcePath), startLine: pos.line, startColumn: pos.column,
        handlerIdentity: handler, httpMethods: [...new Set(methods)],
        routeTemplate: route, resolutionStatus: status,
        routeCompositionEvidence: composition,
        declarationEvidence: declaration.getText(sourceFile)
    };
}

function resolveStatus(...statuses) {
    if (statuses.includes("Dynamic")) return "Dynamic";
    if (statuses.includes("Unsupported")) return "Unsupported";
    if (statuses.includes("PartiallyResolved")) return "PartiallyResolved";
    return "Resolved";
}

function getDecoratorName(decorator) {
    let expression = decorator.expression;
    if (ts.isCallExpression(expression)) expression = expression.expression;
    return ts.isIdentifier(expression) ? expression.text : null;
}

function getDecoratorRoute(decorator) {
    if (!ts.isCallExpression(decorator.expression))
        return { value: "", status: "Resolved", evidence: ["framework default route"] };
    const argument = decorator.expression.arguments[0];
    return argument ? routeValue(argument)
        : { value: "", status: "Resolved", evidence: ["framework default route"] };
}

function importBindings(sourceFile) {
    const bindings = new Map();
    for (const statement of sourceFile.statements) {
        if (!ts.isImportDeclaration(statement) || !ts.isStringLiteral(statement.moduleSpecifier)) continue;
        const moduleName = statement.moduleSpecifier.text;
        const clause = statement.importClause;
        if (!clause?.namedBindings || !ts.isNamedImports(clause.namedBindings)) continue;
        for (const element of clause.namedBindings.elements)
            bindings.set(element.name.text, { imported: element.propertyName?.text ?? element.name.text, moduleName });
    }
    return bindings;
}

function discoverNest(sourceFile, sourcePath, results) {
    const imports = importBindings(sourceFile);
    const nestNames = new Map();
    for (const [local, value] of imports)
        if (value.moduleName === "@nestjs/common") nestNames.set(local, value.imported);

    const mappings = new Map([
        ["Get", ["GET"]], ["Post", ["POST"]], ["Put", ["PUT"]],
        ["Delete", ["DELETE"]], ["Patch", ["PATCH"]],
        ["Options", ["OPTIONS"]], ["Head", ["HEAD"]], ["All", ["ALL"]]
    ]);

    function decorators(node) {
        return ts.canHaveDecorators(node) ? ts.getDecorators(node) ?? [] : [];
    }

    function visit(node) {
        if (!ts.isClassDeclaration(node) || !node.name) {
            ts.forEachChild(node, visit);
            return;
        }
        const classDecorators = decorators(node);
        const controller = classDecorators.find(item => nestNames.get(getDecoratorName(item)) === "Controller");
        if (!controller) {
            ts.forEachChild(node, visit);
            return;
        }
        const prefix = getDecoratorRoute(controller);
        for (const member of node.members) {
            if (!ts.isMethodDeclaration(member) || !member.name) continue;
            for (const decorator of decorators(member)) {
                const imported = nestNames.get(getDecoratorName(decorator));
                if (!mappings.has(imported)) continue;
                const route = getDecoratorRoute(decorator);
                const combined = combine(prefix.value, route.value);
                results.push(createEvidence(sourceFile, sourcePath, decorator, "TypeScript", "NestJS",
                    "typescript-ast-nestjs", node.name.text + "." + member.name.getText(sourceFile),
                    mappings.get(imported), combined, resolveStatus(prefix.status, route.status),
                    [...prefix.evidence, ...route.evidence, "NestJS controller and route decorators"]));
            }
        }
    }
    visit(sourceFile);
}

function discoverExpress(sourceFile, sourcePath, results) {
    const packageJson = readJson(path.join(root, "package.json"));
    const importsExpress = sourceFile.statements.some(statement =>
        ts.isImportDeclaration(statement) && ts.isStringLiteral(statement.moduleSpecifier)
        && (statement.moduleSpecifier.text === "express" || statement.moduleSpecifier.text.startsWith("express/")));
    if (!hasDependency(packageJson, "express") && !importsExpress) return;

    const methods = new Map([
        ["get", "GET"], ["post", "POST"], ["put", "PUT"], ["delete", "DELETE"],
        ["patch", "PATCH"], ["head", "HEAD"], ["options", "OPTIONS"], ["all", "ALL"]
    ]);

    function receiverName(expression) {
        return ts.isPropertyAccessExpression(expression) && ts.isIdentifier(expression.expression)
            ? expression.expression.text : expression.getText(sourceFile);
    }

    function visit(node) {
        if (ts.isCallExpression(node) && ts.isPropertyAccessExpression(node.expression)) {
            const method = node.expression.name.text.toLowerCase();
            if (methods.has(method) && node.arguments.length >= 2) {
                const route = routeValue(node.arguments[0]);
                const handler = node.arguments.slice(1).map(argument => argument.getText(sourceFile)).join(", ");
                const receiver = receiverName(node.expression.expression);
                results.push(createEvidence(sourceFile, sourcePath, node, "TypeScript", "Express",
                    "typescript-ast-express", receiver + "." + method + "(" + handler + ")",
                    [methods.get(method)], route.value, route.status,
                    [...route.evidence, "Express router method declaration"]));
            }
            if (method === "route" && node.arguments.length >= 1) {
                const route = routeValue(node.arguments[0]);
                const receiver = receiverName(node.expression.expression);
                let current = node.parent;
                while (ts.isPropertyAccessExpression(current) || ts.isCallExpression(current)) {
                    if (ts.isCallExpression(current) && ts.isPropertyAccessExpression(current.expression)) {
                        const verb = current.expression.name.text.toLowerCase();
                        if (methods.has(verb)) {
                            const handler = current.arguments.map(argument => argument.getText(sourceFile)).join(", ");
                            results.push(createEvidence(sourceFile, sourcePath, current, "TypeScript", "Express",
                                "typescript-ast-express", receiver + ".route(" + route.value + ")." + verb + "(" + handler + ")",
                                [methods.get(verb)], route.value, route.status,
                                [...route.evidence, "Express chained route declaration"]));
                        }
                    }
                    current = current.parent;
                }
            }
        }
        ts.forEachChild(node, visit);
    }
    visit(sourceFile);
}

let input = "";
for await (const chunk of process.stdin) input += chunk;
let files;
try { files = JSON.parse(input); }
catch (error) {
    process.stderr.write("Invalid source-file manifest: " + error.message + "\n");
    process.exit(2);
}
if (!Array.isArray(files)) {
    process.stderr.write("Source-file manifest must be a JSON array.\n");
    process.exit(2);
}

const results = [];
const diagnostics = [];
for (const file of files) {
    const fullPath = path.resolve(file);
    if (!fullPath.startsWith(root + path.sep) || !fs.existsSync(fullPath)) {
        diagnostics.push({ code: "SourceFileOutsideModule", file: String(file) });
        continue;
    }
    const extension = path.extname(fullPath).toLowerCase();
    const scriptKind = extension === ".tsx" ? ts.ScriptKind.TSX
        : extension === ".jsx" ? ts.ScriptKind.JSX
        : [".js", ".mjs", ".cjs"].includes(extension) ? ts.ScriptKind.JS
        : ts.ScriptKind.TS;
    const source = fs.readFileSync(fullPath, "utf8").replace(/^\uFEFF/, "");
    const sourceFile = ts.createSourceFile(fullPath, source, ts.ScriptTarget.Latest, true, scriptKind);
    if (sourceFile.parseDiagnostics.length > 0) {
        for (const diagnostic of sourceFile.parseDiagnostics) {
            const pos = diagnostic.start == null ? null : sourceFile.getLineAndCharacterOfPosition(diagnostic.start).line + 1;
            diagnostics.push({
                code: "TypeScriptSyntaxDiagnostic", file: relative(fullPath), line: pos,
                message: ts.flattenDiagnosticMessageText(diagnostic.messageText, "\n")
            });
        }
    }
    discoverNest(sourceFile, relative(fullPath), results);
    discoverExpress(sourceFile, relative(fullPath), results);
}

process.stdout.write(JSON.stringify({
    status: "completed", analyzer: "typescript-compiler-api",
    analyzerVersion: ts.version, filesAnalyzed: files.length,
    endpoints: results, diagnostics
}));
