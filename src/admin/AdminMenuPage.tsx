import { Link } from 'react-router-dom';
import { adminFeatures } from './navigation';

export default function AdminMenuPage() {
  return (
    <main className="container mx-auto px-4 py-8">
      <h1 className="text-3xl font-bold">管理者メニュー</h1>
      <p className="mt-3 text-gray-600 dark:text-gray-300">学習コンテンツの整備と、AI による問題作成を行います。</p>
      {['問題作成', 'マスターデータ'].map(category => (
        <section key={category} className="mt-8">
          <h2 className="mb-4 text-xl font-semibold">{category}</h2>
          <div className="grid gap-6 md:grid-cols-2 lg:grid-cols-3">
            {adminFeatures.filter(item => item.category === category).map(item => (
              <Link key={item.path} to={item.path} className="block rounded-xl border border-gray-200 bg-white p-6 transition-all hover:border-blue-400 hover:shadow-lg focus-visible:outline-2 focus-visible:outline-blue-600 dark:border-gray-700 dark:bg-gray-800 dark:hover:border-blue-500">
                <h3 className="text-xl font-semibold text-blue-600 dark:text-blue-400">{item.title}<span aria-hidden="true"> →</span></h3>
                <p className="mt-3 leading-relaxed text-gray-600 dark:text-gray-300">{item.description}</p>
              </Link>
            ))}
          </div>
        </section>
      ))}
    </main>
  );
}
