// Exports JSON Schemas derived from the shared Zod contracts so the C# backend can
// (1) validate tool arguments / model outputs at runtime and (2) prove its responses match in tests.
// Usage: node shared/scripts/export-json-schemas.cjs [--check]
const fs = require('node:fs');
const path = require('node:path');
const { z } = require('zod/v4');
const shared = require('../dist/index.js');

const outDir = path.resolve(__dirname, '../../functions/Learning/Contracts/Schemas');

const groups = {
  model: [
    'LearningProfile', 'GrammarTopic', 'PracticeSession', 'LearningObservation',
    'TutorProfile', 'TutorSession', 'ReviewPlan', 'WorkflowRun', 'ProcessedEvent', 'SampleQuestion', 'TutorToolTurn',
  ],
  ai: ['GeneratedQuestionBatch', 'ConversationAnalysis', 'QuestionJudgement'],
  tool: Object.keys(shared.ToolSchemas),
  api: [
    'SystemStatus', 'DashboardView', 'HistoryView', 'PracticeSessionView', 'AnswerResult',
    'AdminQuestionGenerationRequest', 'AdminQuestionGenerationResponse', 'AdminQuestionValidationRequest', 'QuestionValidation',
    'AdminQuestionPublishRequest', 'AdminQuestionPublishResponse',
    'GrammarListView', 'GrammarSearchView', 'TutorSessionView', 'TutorSessionListView',
    'SendTutorMessageResponse', 'EndTutorSessionResponse',
    'StartPracticeRequest', 'SubmitAnswerRequest', 'StartTutorSessionRequest', 'SendTutorMessageRequest',
  ],
};

function toSchema(schema) {
  const json = z.toJSONSchema(schema, { target: 'draft-2020-12', io: 'output', unrepresentable: 'any' });
  return JSON.stringify(json, null, 2) + '\n';
}

const files = new Map();
for (const group of ['model', 'ai', 'api']) {
  for (const name of groups[group]) {
    if (!shared[name]) throw new Error(`Missing shared export: ${name}`);
    files.set(`${group}.${name}.schema.json`, toSchema(shared[name]));
  }
}
for (const name of groups.tool) {
  files.set(`tool.${name}.input.schema.json`, toSchema(shared.ToolSchemas[name].input));
  files.set(`tool.${name}.output.schema.json`, toSchema(shared.ToolSchemas[name].output));
}

const check = process.argv.includes('--check');
const existing = fs.existsSync(outDir) ? fs.readdirSync(outDir).filter((f) => f.endsWith('.schema.json')) : [];
const stale = [];
for (const [file, content] of files) {
  const target = path.join(outDir, file);
  const current = fs.existsSync(target) ? fs.readFileSync(target, 'utf8').replace(/\r\n/g, '\n') : null;
  if (current !== content) stale.push(file);
}
const orphaned = existing.filter((f) => !files.has(f));

if (check) {
  if (stale.length || orphaned.length) {
    console.error('JSON Schemas are out of date. Run: pnpm run contracts:export');
    for (const f of [...stale, ...orphaned]) console.error('  - ' + f);
    process.exit(1);
  }
  console.log(`JSON Schemas up to date (${files.size} files).`);
} else {
  fs.mkdirSync(outDir, { recursive: true });
  for (const f of orphaned) fs.rmSync(path.join(outDir, f));
  for (const [file, content] of files) fs.writeFileSync(path.join(outDir, file), content);
  console.log(`Wrote ${files.size} JSON Schemas to ${path.relative(process.cwd(), outDir)}`);
}
