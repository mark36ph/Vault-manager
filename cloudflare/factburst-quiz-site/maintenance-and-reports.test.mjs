import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("./", import.meta.url);

async function text(path) {
  return readFile(new URL(path, root), "utf8");
}

test("site status replaces the full page for non-admin visitors during maintenance", async () => {
  const source = await text("public/site-status.js");
  assert.match(source, /if \(status\.is_admin\)/);
  assert.match(source, /showMaintenanceScreen/);
  assert.match(source, /factburst-maintenance-screen/);
  assert.match(source, /document\.body\.innerHTML/);
  assert.match(source, /Administrator access/);
});

test("tracker admin worker exposes question reports management", async () => {
  const worker = await readFile(new URL("../factburst-link-tracker/admin-worker-entry.js", root), "utf8");
  const reports = await readFile(new URL("../factburst-link-tracker/site-question-report-admin.js", root), "utf8");
  assert.match(worker, /handleSiteQuestionReportAdmin/);
  assert.match(worker, /\/api\/site\/question-reports/);
  assert.match(reports, /site_question_reports/);
  assert.match(reports, /resolved/);
  assert.match(reports, /dismissed/);
});
