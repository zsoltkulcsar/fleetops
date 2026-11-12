import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const source = await readFile(new URL("./ruleGuidance.ts", import.meta.url), "utf8");
const { outputText } = ts.transpileModule(source, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 },
});
const { calculateRuleWarnings } = await import(
  `data:text/javascript;base64,${Buffer.from(outputText).toString("base64")}`
);
const fixture = JSON.parse(
  await readFile(new URL("../../../../tests/fixtures/rule-cases.json", import.meta.url), "utf8"),
);

test("rule cases fixture matches the client", () => {
  assert.ok(fixture.cases.length > 0);
  for (const entry of fixture.cases) {
    const warnings = calculateRuleWarnings(entry.activities, entry.rules, entry.scope);
    assert.deepEqual(
      warnings.map((warning) => warning.ruleId).sort(),
      [...entry.expectedWarnings].sort(),
      entry.name,
    );
    for (const warning of warnings) {
      const rule = entry.rules.find((rule) => rule.id === warning.ruleId);
      assert.equal(warning.severity, rule.confirmed ? "warning" : "info", entry.name);
      assert.equal(
        warning.message,
        rule.confirmed
          ? "Possible breach \u2014 check against tachograph"
          : "Guide value \u00b7 rule not yet confirmed",
        entry.name,
      );
    }
  }
});
