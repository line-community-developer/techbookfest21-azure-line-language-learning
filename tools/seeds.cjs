const fs = require('node:fs');
const path = require('node:path');
const models = require('../shared/dist/index.js');

const scenarios = ['production', 'a1-new', 'a2-review-ready', 'a2-review-in-progress', 'a2-review-completed'];
const collections = {
  LearningProfiles: ['learning-profile', models.LearningProfile],
  LearningObservations: ['learning-observation', models.LearningObservation],
  PracticeSessions: ['practice-session', models.PracticeSession],
  TutorSessions: ['tutor-session', models.TutorSession],
  ReviewPlans: ['review-plan', models.ReviewPlan],
  WorkflowRuns: ['workflow-run', models.WorkflowRun],
  GrammarTopics: ['grammar-topic', models.GrammarTopic],
  TutorProfiles: ['tutor-profile', models.TutorProfile],
  SampleQuestions: ['sample-question', models.SampleQuestion],
};

function load(scenario) {
  const result = {};
  const directory = path.join(__dirname, '..', 'dev-seeds', scenario);
  const production = scenario === 'production';
  const referenceCollections = ['GrammarTopics', 'TutorProfiles', 'SampleQuestions'];
  const selected = Object.entries(collections).filter(([name]) => !production || referenceCollections.includes(name));
  const expectedFiles = selected.map(([, [stem]]) => `${stem}.json`).sort();
  const actualFiles = fs.readdirSync(directory).filter(file => file.endsWith('.json')).sort();
  if (JSON.stringify(actualFiles) !== JSON.stringify(expectedFiles)) throw new Error(`${scenario}: seed filenames must match the singular kebab-case model names`);
  for (const [container, [fileStem, schema]] of Object.entries(collections)) {
    if (production && !referenceCollections.includes(container)) {
      result[container] = [];
      continue;
    }
    const file = path.join(directory, `${fileStem}.json`);
    const documents = JSON.parse(fs.readFileSync(file, 'utf8'));
    if (!Array.isArray(documents)) throw new Error(`${file}: expected an array`);
    const ids = new Set();
    for (const document of documents) {
      schema.parse(document);
      if (('userId' in document && document.userId !== 'local-demo-learner') || ids.has(document.id)) throw new Error(`${file}: invalid learner or duplicate ID`);
      ids.add(document.id);
    }
    result[container] = documents;
  }
  if (!production && result.LearningProfiles.length !== 1) throw new Error(`${scenario}: expected one learner`);
  if (!result.GrammarTopics.length || !result.TutorProfiles.length || !result.SampleQuestions.length) throw new Error(`${scenario}: missing reference data`);
  const grammar = new Map(result.GrammarTopics.map(topic => [topic.id, topic]));
  const tutorIds = new Set(result.TutorProfiles.map(tutor => tutor.id));
  for (const topic of result.GrammarTopics) {
    if (topic.relatedTopics.some(id => !grammar.has(id))) throw new Error(`${scenario}: broken related topic`);
  }
  for (const question of result.SampleQuestions) {
    const topic = grammar.get(question.grammarTopicId);
    if (!topic || topic.language !== question.language || question.ruleIds.some(id => !topic.rules.some(rule => rule.id === id)))
      throw new Error(`${scenario}: broken sample question reference`);
  }
  for (const session of result.TutorSessions) {
    if (!tutorIds.has(session.tutorProfileId)) throw new Error(`${scenario}: broken tutor profile reference`);
  }
  const practices = new Set(result.PracticeSessions.map(d => d.id));
  const tutors = new Set(result.TutorSessions.map(d => d.id));
  const observations = new Set(result.LearningObservations.map(d => d.id));
  for (const plan of result.ReviewPlans) {
    const sources = plan.sourceType === 'conversation' ? tutors : practices;
    if (!sources.has(plan.sourceId) || plan.practiceSessionIds.some(id => !practices.has(id))) throw new Error(`${scenario}: broken review link`);
    for (const item of plan.items) {
      for (const id of item.evidenceObservationIds) if (!observations.has(id)) throw new Error(`${scenario}: missing observation ${id}`);
    }
  }
  return result;
}

const [command] = process.argv.slice(2);
if (command === 'check') {
  for (const name of scenarios) {
    load(name);
    console.log(`Valid seed contracts and references: ${name}`);
  }
} else {
  throw new Error('Usage: pnpm seeds:check');
}
