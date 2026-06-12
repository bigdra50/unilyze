import { writeFile } from "node:fs/promises";

function readNumber(flag, fallback) {
  const index = process.argv.indexOf(flag);
  return index >= 0 ? Number(process.argv[index + 1]) : fallback;
}

function readText(flag, fallback) {
  const index = process.argv.indexOf(flag);
  return index >= 0 ? process.argv[index + 1] : fallback;
}

const typeCount = readNumber("--types", 1500);
const dependencyCount = readNumber("--deps", 4500);
const namespaceCount = readNumber("--namespaces", 60);
const output = readText("-o", "synthetic-viewer.json");

if (![typeCount, dependencyCount, namespaceCount].every(Number.isInteger) ||
    typeCount < 1 || dependencyCount < 0 || namespaceCount < 1) {
  throw new Error("Counts must be non-negative integers, with at least one type and namespace.");
}

const assemblies = [{ name: "Synthetic.Assembly" }];
const types = Array.from({ length: typeCount }, (_, index) => {
  const namespace = `Synthetic.Ns${String(index % namespaceCount).padStart(2, "0")}`;
  const qualifiedName = `${namespace}.Type${index}`;
  return {
    name: `Type${index}`,
    namespace,
    kind: "class",
    modifiers: ["public"],
    baseType: null,
    interfaces: [],
    members: [],
    constructorParams: [],
    attributes: [],
    genericConstraints: [],
    enumBaseType: null,
    assembly: "Synthetic.Assembly",
    filePath: `Synthetic/Type${index}.cs`,
    isNested: false,
    lineCount: 10,
    qualifiedName,
    typeId: `Synthetic.Assembly::${qualifiedName}`
  };
});

const kinds = ["FieldType", "MethodParam", "ReturnType", "InterfaceImpl"];
const dependencies = Array.from({ length: dependencyCount }, (_, index) => {
  const from = types[index % typeCount];
  const to = types[(index * 37 + 17) % typeCount];
  return {
    fromType: from.qualifiedName,
    toType: to.qualifiedName,
    kind: kinds[index % kinds.length],
    fromTypeId: from.typeId,
    toTypeId: to.typeId
  };
});

const result = {
  projectPath: "/synthetic/LargeGraph",
  analyzedAt: new Date(0).toISOString(),
  assemblies,
  types,
  dependencies,
  typeMetrics: []
};

await writeFile(output, `${JSON.stringify(result, null, 2)}\n`);
console.log(`Wrote ${typeCount} types and ${dependencyCount} dependencies to ${output}`);
