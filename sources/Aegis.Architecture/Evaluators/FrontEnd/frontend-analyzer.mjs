import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import ts from "typescript";
import { parseTemplate } from "@angular/compiler";

const framework = process.argv[2]?.toLowerCase();
const projectPath = process.argv[3];

const ANALYZER_VERSION = "1.2.1";

const EXCLUDED_DIRECTORIES = new Set([
    "node_modules",
    ".git",
    ".next",
    ".nuxt",
    "dist",
    "build",
    "coverage",
    "vendor",
    "bin",
    "obj",
    ".angular"
]);

function fail(message, exitCode = 2)
{
    process.stderr.write(`${ message}\n`);
    process.exit(exitCode);
}

if (! ["angular", "react"].includes(framework))
{
    fail(
        "Usage: node frontend-analyzer.mjs <angular|react> <project-path>"
    );
}

if (!projectPath || !fs.existsSync(projectPath))
{
    fail(`Project path does not exist: ${ projectPath ?? "(not supplied)"}`);
}

const root = path.resolve(projectPath);

if (!fs.statSync(root).isDirectory())
{
    fail(`Project path is not a directory: ${ root}`);
}

function relativeFile(file)
{
    return path.relative(root, file).split(path.sep).join("/");
}

function readSource(file)
{
    return fs.readFileSync(file, "utf8").replace(/ ^\uFEFF/, "");
}

function countNonBlankLines(source)
{
    if (source.length === 0)
    {
        return 0;
    }

    return source
        .split(/\r\n |\n |\r /)
        .filter(line => line.trim().length > 0)
        .length;
}

function walk(directory, extensions, files = [])
{
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        const fullPath = path.join(directory, entry.name);

        if (entry.isDirectory())
        {
            if (!EXCLUDED_DIRECTORIES.has(entry.name))
            {
                walk(fullPath, extensions, files);
            }
            continue;
        }

        if (
            entry.isFile() &&
            extensions.includes(path.extname(entry.name).toLowerCase())
        )
        {
            files.push(fullPath);
        }
    }

    return files;
}

function readPackageJson()
{
    const packagePath = path.join(root, "package.json");

    if (!fs.existsSync(packagePath))
    {
        return null;
    }

    try
    {
        return JSON.parse(readSource(packagePath));
    }
    catch (error)
    {
        throw new Error(
            `Cannot parse package.json: ${ error.message}`,
            { cause: error }
        );
    }
}

function getPackageDeclaration(packageJson, packageName)
{
    return (
        packageJson?.dependencies?.[packageName] ??
        packageJson?.devDependencies?.[packageName] ??
        packageJson?.peerDependencies?.[packageName] ??
        packageJson?.optionalDependencies?.[packageName] ??
        null
    );
}

function getPackageVersion(packageJson, packageName)
{
    const declaration = getPackageDeclaration(packageJson, packageName);

    if (typeof declaration !== "string")
    {
        return null;
    }

    // Extract the major version from ordinary semver declarations such as:
    // 21.0.0, ^21.0.0, ~21.0.0, >=21.0.0.
    const match = declaration.match(
        / (?: ^| [^\d])(\d +)\.\d +\.\d + ($| [^\d]) /
    );

    return match ? Number(match[1]) : null;
}

function detectProjectFramework(packageJson)
{
    const angularVersion =
        getPackageVersion(packageJson, "@angular/core") ??
        getPackageVersion(packageJson, "@angular/compiler");

    const hasAngularConfiguration =
        fs.existsSync(path.join(root, "angular.json"));

    const hasAngular =
        angularVersion !== null || hasAngularConfiguration;

    const hasReact =
        packageJson !== null &&
        ["react", "react-dom"].some(name =>
            getPackageDeclaration(packageJson, name) !== null
        );

    let detectedFramework = "unknown";

    if (hasAngular && hasReact)
    {
        detectedFramework = "mixed";
    }
    else if (hasAngular)
    {
        detectedFramework = "angular";
    }
    else if (hasReact)
    {
        detectedFramework = "react";
    }

    return {
        angularVersion,
        hasAngular,
        hasReact,
        detectedFramework
    }
    ;
}

function parseTypeScript(file, source, jsxForJavaScript = false)
{
    const extension = path.extname(file).toLowerCase();

    const scriptKind =
        extension === ".tsx" ? ts.ScriptKind.TSX :
        extension === ".jsx" ? ts.ScriptKind.JSX :
        jsxForJavaScript &&
            [".js", ".mjs", ".cjs"].includes(extension)
            ? ts.ScriptKind.JSX :
        [".js", ".mjs", ".cjs"].includes(extension)
            ? ts.ScriptKind.JS :
        ts.ScriptKind.TS;

    return ts.createSourceFile(
        file,
        source,
        ts.ScriptTarget.Latest,
        true,
        scriptKind
    );
}

function getParseDiagnostics(sourceFile)
{
    return sourceFile.parseDiagnostics.map(diagnostic => ({
    code: diagnostic.code,
        message: ts.flattenDiagnosticMessageText(
            diagnostic.messageText,
            "\n"
        ),
        line: diagnostic.start == null
            ? null
            : sourceFile.getLineAndCharacterOfPosition(
                diagnostic.start
            ).line + 1
    }));
}

function decoratorName(decorator)
{
    let expression = decorator.expression;

    if (ts.isCallExpression(expression))
    {
        expression = expression.expression;
    }

    if (ts.isIdentifier(expression))
    {
        return expression.text;
    }

    if (
        ts.isPropertyAccessExpression(expression) &&
        ts.isIdentifier(expression.name)
    )
    {
        return expression.name.text;
    }

    return null;
}

function getDecorators(node)
{
    return ts.canHaveDecorators(node)
        ? ts.getDecorators(node) ?? []
        : [];
}

function getObjectProperty(object, name)
{
    if (!object || !ts.isObjectLiteralExpression(object))
    {
        return undefined;
    }

    return object.properties.find(property =>
        ts.isPropertyAssignment(property) &&
        (
            (
                ts.isIdentifier(property.name) &&
                property.name.text === name
            ) ||
            (
                ts.isStringLiteral(property.name) &&
                property.name.text === name
            )
        )
    );
}

function literalValue(property)
{
    if (!property || !ts.isPropertyAssignment(property))
    {
        return undefined;
    }

    const expression = property.initializer;

    if (
        ts.isStringLiteral(expression) ||
        ts.isNoSubstitutionTemplateLiteral(expression)
    )
    {
        return expression.text;
    }

    if (expression.kind === ts.SyntaxKind.TrueKeyword)
    {
        return true;
    }

    if (expression.kind === ts.SyntaxKind.FalseKeyword)
    {
        return false;
    }

    if (expression.kind === ts.SyntaxKind.NullKeyword)
    {
        return null;
    }

    if (ts.isArrayLiteralExpression(expression))
    {
        if (expression.elements.some(element => ts.isSpreadElement(element)))
        {
            return undefined;
        }

        return expression.elements.map(element => {
            if (ts.isIdentifier(element))
            {
                return element.text;
            }

            if (
                ts.isStringLiteral(element) ||
                ts.isNoSubstitutionTemplateLiteral(element)
            )
            {
                return element.text;
            }

            return element.getText();
        });
    }

    return undefined;
}

function getAngularStandaloneDefault(angularVersion)
{
    if (angularVersion === null)
    {
        return {
        value: null,
            source: "unknown-angular-version"
        }
        ;
    }

    return angularVersion >= 19
        ? {
    value: true,
            source: "angular-version-default"
        }
        : {
value: false,
            source: "angular-version-default"
        }
;
}

function getDecoratorMetadata(decorator)
{
    const expression = decorator.expression;

    if (!ts.isCallExpression(expression))
    {
        return undefined;
    }

    const metadata = expression.arguments[0];

    return metadata && ts.isObjectLiteralExpression(metadata)
        ? metadata
        : undefined;
}

function analyzeAngularSource(file, source, angularVersion)
{
    const sourceFile = parseTypeScript(file, source);
    const components = [];
    const modules = [];
    const classNames = [];

    const standaloneDefault =
        getAngularStandaloneDefault(angularVersion);

    function visit(node)
    {
        if (ts.isClassDeclaration(node) && node.name)
        {
            classNames.push(node.name.text);

            for (const decorator of getDecorators(node)) {
                const name = decoratorName(decorator);

                if (name !== "Component" && name !== "NgModule")
                {
                    continue;
                }

                const metadata = getDecoratorMetadata(decorator);

                const selector = literalValue(
                    getObjectProperty(metadata, "selector")
                );

                const explicitStandalone = literalValue(
                    getObjectProperty(metadata, "standalone")
                );

                const template = literalValue(
                    getObjectProperty(metadata, "template")
                );

                const templateUrl = literalValue(
                    getObjectProperty(metadata, "templateUrl")
                );

                const declarations = literalValue(
                    getObjectProperty(metadata, "declarations")
                );

                const standalone =
                    typeof explicitStandalone === "boolean"
                        ? explicitStandalone
                        : name === "Component"
                            ? standaloneDefault.value
                            : null;

                const record = {
                    className: node.name.text,

                    selector: typeof selector === "string"
                        ? selector
                        : null,

                    standalone,

                    standaloneSource:
                        typeof explicitStandalone === "boolean"
                            ? "explicit-metadata"
                            : name === "Component"
                                ? standaloneDefault.source
                                : "not-applicable",

                    sourceLineCount: countNonBlankLines(
                        node.getText(sourceFile)
                    ),

                    declarationCount: Array.isArray(declarations)
                        ? declarations.length
                        : null,

                    templateKind:
typeof template === "string" ? "inline" :
typeof templateUrl === "string" ? "external" :
"none",

                    templateDiagnostics: [],

                    templateAnalysisStatus:
name === "Component" ? "not-run" : "not-applicable"
                };

if (name === "Component")
{
    record._inlineTemplate =
        typeof template === "string"
            ? template
            : undefined;

    record._templateUrl =
        typeof templateUrl === "string"
            ? templateUrl
            : undefined;

    components.push(record);
}
else
{
    modules.push(record);
}
            }
        }

        ts.forEachChild(node, visit);
    }

    visit(sourceFile);

return {
filePath: relativeFile(file),
        sourceLineCount: countNonBlankLines(source),
        components,
        modules,
        classNames,
        syntaxDiagnostics: getParseDiagnostics(sourceFile)
    }
;
}

function analyzeAngularTemplate(component, componentFile)
{
    let template;
    let templateFile = componentFile;

    if (component._inlineTemplate !== undefined)
    {
        template = component._inlineTemplate;
    }
    else if (component._templateUrl !== undefined)
    {
        templateFile = path.resolve(
            path.dirname(componentFile),
            component._templateUrl
        );

        if (!fs.existsSync(templateFile))
        {
            return {
            status: "missing-template",
                diagnostics:
                [{
                code: "AEG-FE-TEMPLATE-MISSING",
                    message:
                        `External template not found: ${ component._templateUrl}`,
                    line: null
                }]
            };
        }

        if (!fs.statSync(templateFile).isFile())
{
    return {
    status: "invalid-template-path",
                diagnostics:
        [{
        code: "AEG-FE-TEMPLATE-NOT-FILE",
                    message:
                        `External template path is not a file: ${ component._templateUrl}`,
                    line: null
                }]
            }
    ;
}

template = readSource(templateFile);
    } else
{
    return {
    status: "no-template-declared",
            diagnostics: []
        }
    ;
}

try
{
    const result = parseTemplate(
        template,
        templateFile,
            { preserveWhitespaces: false }
        );

    return {
    status: "parsed",
            diagnostics: (result.errors ?? []).map(error => ({
        code: "AEG-FE-TEMPLATE-PARSE",
                message: error.toString(),
                line: Number.isInteger(error.span?.start?.line)
                    ? error.span.start.line + 1
                    : null
            }))
        }
    ;
}
catch (error)
{
    return {
    status: "parser-failure",
            diagnostics:
        [{
        code: "AEG-FE-TEMPLATE-PARSER-FAILURE",
                message: error.message ?? String(error),
                line: null
            }]
        }
    ;
}
}

function removeInternalFields(component)
{
    const {
        _inlineTemplate,
        _templateUrl,
        ...publicComponent
    } = component;

    return publicComponent;
}

function analyzeAngularProject(angularVersion)
{
    const files = walk(root, [".ts"])
        .filter(file => !file.endsWith(".d.ts"));

    const results = [];

    for (const file of files) {
        const source = readSource(file);

        const result = analyzeAngularSource(
            file,
            source,
            angularVersion
        );

        for (const component of result.components) {
            const analysis = analyzeAngularTemplate(component, file);

            component.templateAnalysisStatus = analysis.status;
            component.templateDiagnostics = analysis.diagnostics;
        }

        if (
            result.components.length === 0 &&
            result.modules.length === 0 &&
            result.classNames.length === 0 &&
            result.syntaxDiagnostics.length === 0
        )
        {
            continue;
        }

        result.components = result.components.map(removeInternalFields);
        result.modules = result.modules.map(removeInternalFields);

        results.push(result);
    }

    return {
    status: "completed",
        framework: "Angular",
        analyzerVersion: ANALYZER_VERSION,

        capabilities:
        {
        typescriptAst: true,
            typescriptTypeChecking: false,
            angularTemplateParsing: true,
            angularTemplateTypeChecking: false,
            angularProjectCompilation: false
        },

        angularVersion,
        filesAnalyzed: results.length,
        files: results
    }
    ;
}

function analyzeReactSource(file, source)
{
    const sourceFile = parseTypeScript(file, source, true);

    const components = [];
    const hooks = [];

    let jsxElementCount = 0;
    let conditionalCount = 0;

    function visit(node)
    {
        if (
            ts.isFunctionDeclaration(node) &&
            node.name &&
            / ^ [A - Z] /.test(node.name.text)
        )
        {
            components.push(node.name.text);
        }

        if (
            ts.isVariableDeclaration(node) &&
            ts.isIdentifier(node.name) &&
            / ^ [A - Z] /.test(node.name.text) &&
            node.initializer &&
            (
                ts.isArrowFunction(node.initializer) ||
                ts.isFunctionExpression(node.initializer)
            )
        )
        {
            components.push(node.name.text);
        }

        if (
            ts.isCallExpression(node) &&
            ts.isIdentifier(node.expression) &&
            / ^use[A - Z0 - 9] /.test(node.expression.text)
        )
        {
            hooks.push({
            name: node.expression.text,
                line: sourceFile.getLineAndCharacterOfPosition(
                    node.getStart(sourceFile)
                ).line + 1
            });
        }

        if (
            ts.isJsxElement(node) ||
            ts.isJsxSelfClosingElement(node) ||
            ts.isJsxFragment(node)
        )
        {
            jsxElementCount++;
        }

        if (
            ts.isIfStatement(node) ||
            ts.isConditionalExpression(node) ||
            ts.isSwitchStatement(node)
        )
        {
            conditionalCount++;
        }

        ts.forEachChild(node, visit);
    }

    visit(sourceFile);

    return {
    filePath: relativeFile(file),
        sourceLineCount: countNonBlankLines(source),
        components: [...new Set(components)],
        hooks,
        jsxElementCount,
        conditionalCount,
        syntaxDiagnostics: getParseDiagnostics(sourceFile)
    }
    ;
}

function analyzeReactProject()
{
    const files = walk(root, [".js", ".jsx", ".ts", ".tsx"])
        .filter(file => !file.endsWith(".d.ts"));

    const results = files.map(file =>
        analyzeReactSource(file, readSource(file))
    );

    const jsxFileCount = results.filter(
        result => result.jsxElementCount > 0
    ).length;

    return {
    status: "completed",
        framework: "React",
        analyzerVersion: ANALYZER_VERSION,

        capabilities:
        {
        typescriptAst: true,
            typescriptTypeChecking: false,
            reactCompiler: false,
            jsxStructuralAnalysis: true
        },

        filesAnalyzed: results.length,
        jsxFileCount,
        files: results
    }
    ;
}

try
{
    const packageJson = readPackageJson();
    const detection = detectProjectFramework(packageJson);

    if (
        (framework === "angular" && !detection.hasAngular) ||
        (framework === "react" && !detection.hasReact)
    )
    {
        const requested =
            framework === "angular" ? "Angular" : "React";

        process.stdout.write(JSON.stringify({
        status: "not-applicable",
            framework: requested,
            analyzerVersion: ANALYZER_VERSION,
            detectedFramework: detection.detectedFramework,
            message:
                `The project does not appear to declare ${ requested}. ` +
                "Check package.json and project configuration before analysis."
        }));
    }
    else
    {
        const result = framework === "angular"
            ? analyzeAngularProject(detection.angularVersion)
            : analyzeReactProject();

        process.stdout.write(JSON.stringify(result));
    }
}
catch (error)
{
    process.stderr.write(
        `${ error.stack ?? error.message ?? String(error)}\n`
    );

    process.exitCode = 1;
}