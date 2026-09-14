import { redirect } from 'next/navigation';

export default function HomePage() {
  // next/navigation's redirect() automatically prepends the app's basePath
  // ('/app') when resolving the Location header — verified by testing the
  // built server; do NOT hardcode '/app' here, it would double up.
  redirect('/dashboard');
}
