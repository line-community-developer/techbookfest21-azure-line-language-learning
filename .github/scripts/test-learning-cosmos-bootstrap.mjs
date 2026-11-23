import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';

const root = process.cwd();
const originalProgram = fs.readFileSync(path.join(root, 'functions', 'Program.cs'), 'utf8');
const originalTokenHash = process.env.BOOTSTRAP_TOKEN_HASH;
const originalExpires = process.env.BOOTSTRAP_EXPIRES_AT;
process.env.BOOTSTRAP_TOKEN_HASH = crypto.createHash('sha256').update('test-only-token').digest('hex');
process.env.BOOTSTRAP_EXPIRES_AT = String(Math.floor(Date.now() / 1000) + 900);
const temporaryRoots = [];

function fixture() {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'learning-bootstrap-test-'));
  temporaryRoots.push(directory);
  fs.mkdirSync(path.join(directory, 'functions'));
  fs.copyFileSync(path.join(root, 'package.json'), path.join(directory, 'package.json'));
  for (const file of fs.readdirSync(path.join(root, 'functions')).filter(file => file.endsWith('.csproj') || file === 'host.json')) {
    fs.copyFileSync(path.join(root, 'functions', file), path.join(directory, 'functions', file));
  }
  fs.copyFileSync(path.join(root, 'functions', 'Program.cs'), path.join(directory, 'functions', 'Program.cs'));
  fs.symlinkSync(fs.realpathSync(path.join(root, 'node_modules')), path.join(directory, 'node_modules'),
    process.platform === 'win32' ? 'junction' : 'dir');
  for (const source of ['shared', 'dev-seeds', '.github']) {
    fs.cpSync(path.join(root, source), path.join(directory, source), {
      recursive: true, filter: entry => !entry.includes(`${path.sep}node_modules${path.sep}`) && !entry.endsWith(`${path.sep}node_modules`),
    });
  }
  return directory;
}

function prepare(seedName, directory) {
  const result = spawnSync(process.execPath, [path.join(directory, '.github', 'scripts', 'prepare-cosmos-bootstrap.mjs'), seedName], {
    cwd: directory, env: process.env, encoding: 'utf8',
  });
  assert.equal(result.error, undefined);
  return result;
}

try {
  for (const seedName of ['production', 'a1-new', 'a2-review-ready', 'a2-review-in-progress', 'a2-review-completed']) {
    const directory = fixture();
    const result = prepare(seedName, directory);
    assert.equal(result.status, 0, result.stderr);
    const generated = fs.readFileSync(path.join(directory, 'functions', 'Crud', 'BootstrapSeedFunction.cs'), 'utf8');
    const encodedSeed = generated.match(/private const string EncodedSeed = "([^"]+)";/)[1];
    const containers = JSON.parse(Buffer.from(encodedSeed, 'base64').toString('utf8'));
    assert.equal(containers.length, seedName === 'production' ? 3 : 9);
    if (seedName === 'production') {
      assert.deepEqual(containers.map(c => c.containerName).sort(), ['GrammarTopics', 'SampleQuestions', 'TutorProfiles']);
    }
    for (const container of containers) {
      const expectedKey = ['GrammarTopics', 'TutorProfiles', 'SampleQuestions'].includes(container.containerName) ? 'language' : 'userId';
      assert.equal(container.partitionKeyField, expectedKey);
    }
    assert.equal(containers.find(container => container.containerName === 'GrammarTopics').documents.length, 15);
    assert.equal(containers.find(container => container.containerName === 'TutorProfiles').documents.length, 1);
    assert.equal(containers.find(container => container.containerName === 'SampleQuestions').documents.length, 69);
    assert.equal(fs.readFileSync(path.join(directory, 'functions', 'Program.cs'), 'utf8'), originalProgram);
    assert(!fs.existsSync(path.join(directory, '.swallowkit', 'cosmos-bootstrap')));
    console.log(`PASS: ${seedName}, ${containers.length} seed collections, correct partitions, standard generator, ordinary startup unchanged`);
  }
  const traversal = fixture();
  assert.notEqual(prepare('../outside', traversal).status, 0);
  assert(!fs.existsSync(path.join(traversal, 'functions', 'Crud', 'BootstrapSeedFunction.cs')));
  const invalid = fixture();
  fs.writeFileSync(path.join(invalid, 'dev-seeds', 'a1-new', 'sample-question.json'), '[{"id":"invalid"}]');
  assert.notEqual(prepare('a1-new', invalid).status, 0);
  assert(!fs.existsSync(path.join(invalid, 'functions', 'Crud', 'BootstrapSeedFunction.cs')));
  console.log('PASS: invalid input and invalid seed rejected before deployment');
} finally {
  if (originalTokenHash === undefined) delete process.env.BOOTSTRAP_TOKEN_HASH;
  else process.env.BOOTSTRAP_TOKEN_HASH = originalTokenHash;
  if (originalExpires === undefined) delete process.env.BOOTSTRAP_EXPIRES_AT;
  else process.env.BOOTSTRAP_EXPIRES_AT = originalExpires;
  for (const directory of temporaryRoots) fs.rmSync(directory, { recursive: true, force: true });
}
