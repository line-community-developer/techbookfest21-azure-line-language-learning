export interface ScaffoldModel {
  name: string;
  path: string;
  label: string;
}

export const scaffoldConfig = {
  models: [
    { name: 'TutorProfile', path: '/tutor-profile', label: 'Tutor Profile' },
    { name: 'GrammarTopic', path: '/grammar-topic', label: 'Grammar Topic' },
    { name: 'SampleQuestion', path: '/sample-question', label: 'Sample Question' },
  ] as ScaffoldModel[]
};
