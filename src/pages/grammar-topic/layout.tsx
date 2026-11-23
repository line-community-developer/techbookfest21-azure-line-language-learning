import { AuthProvider } from '@/lib/auth/schemes/admin/auth-context';
import type { ReactNode } from 'react';

export default function GrammarTopicLayout({
  children,
}: {
  children: ReactNode;
}) {
  return <AuthProvider>{children}</AuthProvider>;
}
