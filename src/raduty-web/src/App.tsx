import { useQuery } from '@tanstack/react-query'
import { useMsal } from '@azure/msal-react'
import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { ArrowRight, LockKeyhole, MoonStar, ShieldCheck } from 'lucide-react'
import { api, ApiError } from './api'
import { isDevelopmentAuth, loginRequest } from './auth'
import type { CurrentUser } from './types'
import { AppLayout } from './components/Layout'
import { ToastProvider } from './components/ui'
import { SchedulePage } from './pages/SchedulePage'
import { HomePage } from './pages/HomePage'

const DirectoryPage = lazy(() => import('./pages/DirectoryPage').then((module) => ({ default: module.DirectoryPage })))
const ProfilePage = lazy(() => import('./pages/ProfilePage').then((module) => ({ default: module.ProfilePage })))
const DormCheckPage = lazy(() => import('./pages/DormCheckPage').then((module) => ({ default: module.DormCheckPage })))
const AdminDashboardPage = lazy(() => import('./pages/AdminPages').then((module) => ({ default: module.AdminDashboardPage })))
const AuditLogPage = lazy(() => import('./pages/AdminPages').then((module) => ({ default: module.AuditLogPage })))
const UserManagementPage = lazy(() => import('./pages/AdminPages').then((module) => ({ default: module.UserManagementPage })))
const DormRosterImportPage = lazy(() => import('./pages/DormRosterImportPage').then((module) => ({ default: module.DormRosterImportPage })))
const ResidentManagementPage = lazy(() => import('./pages/ResidentManagementPage').then((module) => ({ default: module.ResidentManagementPage })))

export default function App() {
  const { instance, accounts } = useMsal()
  const isSignedIn = isDevelopmentAuth || accounts.length > 0
  const me = useQuery({ queryKey: ['me'], queryFn: ({ signal }) => api<CurrentUser>('/api/me', {}, signal), enabled: isSignedIn })
  if (!isSignedIn) return <SignInPage onSignIn={() => instance.loginRedirect(loginRequest)} />
  if (me.isLoading) return <AppLoading />
  if (me.isError) return <AccessError error={me.error} onRetry={() => me.refetch()} />
  if (!me.data) return null
  return <ToastProvider><AppLayout user={me.data}><Suspense fallback={<RouteLoading />}><Routes>
    <Route path="/" element={<HomePage user={me.data} />} />
    <Route path="/schedule" element={<SchedulePage user={me.data} />} />
    <Route path="/dorm-checks" element={<DormCheckPage />} />
    <Route path="/residents" element={<ResidentManagementPage user={me.data} />} />
    <Route path="/directory" element={<DirectoryPage />} />
    <Route path="/profile" element={<ProfilePage user={me.data} />} />
    <Route path="/admin" element={me.data.role === 'HallDirector' || me.data.role === 'Admin' ? <AdminDashboardPage /> : <Navigate to="/" replace />} />
    <Route path="/admin/users" element={me.data.role === 'HallDirector' || me.data.role === 'Admin' ? <UserManagementPage /> : <Navigate to="/" replace />} />
    <Route path="/admin/residents" element={me.data.role === 'HallDirector' || me.data.role === 'Admin' ? <DormRosterImportPage /> : <Navigate to="/" replace />} />
    <Route path="/admin/audit" element={me.data.role === 'HallDirector' || me.data.role === 'Admin' ? <AuditLogPage /> : <Navigate to="/" replace />} />
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes></Suspense></AppLayout></ToastProvider>
}

function SignInPage({ onSignIn }: { onSignIn: () => void }) {
  return <main className="signin-page"><section className="signin-panel"><div className="signin-brand"><span className="brand__mark">ND</span><span><strong>Night Desk</strong><small>University Residence Life</small></span></div><div className="signin-copy"><span className="eyebrow">Hawthorne Hall</span><h1>Night-duty scheduling</h1><p>Choose coverage nights, coordinate with your hall team, and keep the published schedule close at hand.</p></div><button className="microsoft-button" onClick={onSignIn}><span className="microsoft-mark" aria-hidden="true"><i /><i /><i /><i /></span><span>Sign in with your school account</span><ArrowRight size={18} /></button><div className="access-statement"><ShieldCheck size={18} /><p>Access is limited to authorized Residence Life staff in the approved Microsoft Entra group.</p></div></section><aside className="signin-context"><MoonStar /><blockquote>“Duty coverage works best when everyone can see the plan and act with confidence.”</blockquote><div><strong>Night Desk</strong><span>Secure scheduling for residence-life teams</span></div></aside></main>
}

function AppLoading() { return <main className="app-loading"><div className="signin-brand"><span className="brand__mark">ND</span><strong>Night Desk</strong></div><div className="loading-line" /><p>Preparing your hall schedule…</p></main> }

function RouteLoading() { return <div className="page"><div className="skeleton skeleton--title" /><div className="skeleton skeleton--panel" aria-label="Loading page" /></div> }

function AccessError({ error, onRetry }: { error: Error; onRetry: () => void }) {
  const forbidden = error instanceof ApiError && error.problem.status === 403
  return <main className="access-error"><div className="access-error__icon">{forbidden ? <LockKeyhole /> : <MoonStar />}</div><span className="eyebrow">Night Desk</span><h1>{forbidden ? 'Access is not available' : 'We could not load your account'}</h1><p>{error instanceof ApiError ? error.problem.title : 'Check your connection and try again.'}</p><button className="button button--primary" onClick={onRetry}>Try again</button><small>{forbidden ? 'Ask your Hall Director to confirm your approved-group membership and application role.' : 'If this continues, contact Residence Life support.'}</small></main>
}
