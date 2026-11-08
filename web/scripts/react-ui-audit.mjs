import { readFile, readdir } from "node:fs/promises";
import { join, relative, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { createRequire } from "node:module";
const web = fileURLToPath(new URL("../", import.meta.url));
const require = createRequire(
  join(web, "packages/approval-react-ui/package.json"),
);
const ts = require("typescript");
const failures = [];
let checked = 0;
const nativeWidgets = new Set([
  "input",
  "select",
  "textarea",
  "button",
  "fieldset",
  "details",
  "summary",
  "table",
  "dialog",
  "progress",
  "datalist",
  "option",
]);
export function nativeWidgetFindings(text, path = "component.tsx") {
  const source = ts.createSourceFile(
    path,
    text,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX,
  );
  const findings = [];
  function visit(node) {
    if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) {
      const tag = node.tagName.getText(source);
      const dateInput =
        tag === "Input" &&
        node.attributes.properties.some((attribute) => {
          if (
            !ts.isJsxAttribute(attribute) ||
            attribute.name.getText(source) !== "type" ||
            !attribute.initializer
          )
            return false;
          const value = ts.isJsxExpression(attribute.initializer)
            ? attribute.initializer.expression
            : attribute.initializer;
          return value && ts.isStringLiteral(value) && value.text === "date";
        });
      if (nativeWidgets.has(tag) || dateInput)
        findings.push({
          tag: dateInput ? "native date Input" : tag,
          line:
            source.getLineAndCharacterOfPosition(node.getStart(source)).line +
            1,
        });
    }
    ts.forEachChild(node, visit);
  }
  visit(source);
  return findings;
}
async function inspect(directory) {
  for (const item of await readdir(directory, { withFileTypes: true })) {
    const path = join(directory, item.name);
    if (item.isDirectory()) {
      if (item.name !== "shadcn") await inspect(path);
      continue;
    }
    if (!path.endsWith(".tsx")) continue;
    checked++;
    for (const finding of nativeWidgetFindings(
      await readFile(path, "utf8"),
      path,
    ))
      failures.push(
        `${relative(web, path)}:${finding.line}: use the registered shadcn widget instead of ${finding.tag}`,
      );
  }
}
if (
  process.argv[1] &&
  import.meta.url === pathToFileURL(resolve(process.argv[1])).href
) {
  await inspect(join(web, "packages/approval-react-ui/src"));
  await inspect(join(web, "apps/react-demo/src"));
  if (failures.length) {
    console.error(failures.join("\n"));
    process.exitCode = 1;
  } else
    console.log(
      `React UI audit passed: ${checked} consumer files; no raw widgets, native date pickers or datalists outside official shadcn components.`,
    );
}
