import { useQuery, useQueryClient } from '@tanstack/react-query'
import { lazy, Suspense, useState, type FormEvent } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { ArrowRight, KeyRound, LockKeyhole, MoonStar, ShieldCheck } from 'lucide-react'
import { api, ApiError, changePassword, signIn, signOut } from './api'
import type { CurrentUser } from './types'
import { AppLayout } from './components/Layout'
import { PasswordField } from './components/PasswordField'
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
  const queryClient = useQueryClient()
  const [passwordRequired, setPasswordRequired] = useState(false)
  const me = useQuery({ queryKey: ['me'], queryFn: ({ signal }) => api<CurrentUser>('/api/me', {}, signal), retry: false })
  const unauthenticated = me.error instanceof ApiError && me.error.problem.status === 401
  const requiresPasswordChange = passwordRequired || me.error instanceof ApiError && me.error.problem.code === 'PASSWORD_CHANGE_REQUIRED'
  if (me.isLoading) return <AppLoading />
  if (requiresPasswordChange) return <ChangePasswordPage onChanged={async () => { setPasswordRequired(false); await queryClient.invalidateQueries({ queryKey: ['me'] }) }} />
  if (unauthenticated) return <SignInPage onSignedIn={async (mustChange) => { setPasswordRequired(mustChange); if (!mustChange) await queryClient.invalidateQueries({ queryKey: ['me'] }) }} />
  if (me.isError) return <AccessError error={me.error} onRetry={() => me.refetch()}
    onSignOut={async () => { await signOut(); queryClient.clear(); window.location.assign('/') }} />
  if (!me.data) return null
  return <ToastProvider><AppLayout user={me.data}><Suspense fallback={<RouteLoading />}><Routes>
    <Route path="/" element={<HomePage user={me.data} />} />
    <Route path="/schedule" element={<SchedulePage user={me.data} />} />
    <Route path="/dorm-checks" element={<DormCheckPage />} />
    <Route path="/residents" element={<ResidentManagementPage user={me.data} />} />
    <Route path="/directory" element={<DirectoryPage />} />
    <Route path="/profile" element={<ProfilePage user={me.data} />} />
    <Route path="/admin" element={me.data.role === 'HallDirector' || me.data.role === 'Admin' ? <AdminDashboardPage /> : <Navigate to="/" replace />} />
    <Route path="/admin/users" element={me.data.role === 'HallDirector' || me.data.role === 'Admin' ? <UserManagementPage currentUser={me.data} /> : <Navigate to="/" replace />} />
    <Route path="/admin/residents" element={me.data.role === 'HallDirector' || me.data.role === 'Admin' ? <DormRosterImportPage /> : <Navigate to="/" replace />} />
    <Route path="/admin/audit" element={me.data.role === 'HallDirector' || me.data.role === 'Admin' ? <AuditLogPage /> : <Navigate to="/" replace />} />
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes></Suspense></AppLayout></ToastProvider>
}

export function SignInPage({ onSignedIn }: { onSignedIn: (mustChangePassword: boolean) => Promise<void> | void }) {
  const [email, setEmail] = useState(''); const [password, setPassword] = useState('')
  const [rememberMe, setRememberMe] = useState(true)
  const [busy, setBusy] = useState(false); const [error, setError] = useState('')
  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError('')
    try { const result = await signIn(email, password, rememberMe); await onSignedIn(result.mustChangePassword) }
    catch (cause) { setError(cause instanceof ApiError ? cause.problem.title : 'Sign-in could not be completed.') }
    finally { setBusy(false) }
  }
  return <main className="signin-page"><section className="signin-panel"><div className="signin-brand"><span className="brand__mark">EH</span><span><strong>Eltse Hall</strong><small>William Penn Residence Life</small></span></div><div className="signin-copy"><span className="eyebrow">William Penn University</span><h1>Welcome back</h1><p>Sign in to manage night-duty coverage, residents, and room checks.</p></div><form className="signin-form" onSubmit={submit}><label className="field"><span>School email</span><input type="email" inputMode="email" autoComplete="username" value={email} onChange={(event) => setEmail(event.target.value)} placeholder="name@wmpenn.edu" required /></label><PasswordField label="Password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} required /><label className="signin-remember"><input type="checkbox" checked={rememberMe} onChange={(event) => setRememberMe(event.target.checked)} /><span>Keep me signed in on this device</span></label>{error && <p className="signin-error" role="alert">{error}</p>}<button className="signin-button" type="submit" disabled={busy}><KeyRound size={19} /><span>{busy ? 'Signing in…' : 'Sign in'}</span><ArrowRight size={18} /></button><small className="signin-help">Forgot your password? Ask your Hall Director or administrator to reset it.</small></form><div className="access-statement"><ShieldCheck size={18} /><p>Only provisioned Residence Life staff with an @wmpenn.edu email can sign in.</p></div></section><aside className="signin-context"><MoonStar /><div><strong>Eltse Hall</strong><span>Secure tools for the residence-life team</span></div></aside></main>
}

function ChangePasswordPage({ onChanged }: { onChanged: () => Promise<void> | void }) {
  const [currentPassword, setCurrentPassword] = useState(''); const [newPassword, setNewPassword] = useState('')
  const [confirmation, setConfirmation] = useState(''); const [busy, setBusy] = useState(false); const [error, setError] = useState('')
  async function submit(event: FormEvent) {
    event.preventDefault(); setError('')
    if (newPassword !== confirmation) { setError('The new passwords do not match.'); return }
    setBusy(true)
    try { await changePassword(currentPassword, newPassword); await onChanged() }
    catch (cause) { setError(cause instanceof ApiError ? cause.problem.title : 'The password could not be changed.') }
    finally { setBusy(false) }
  }
  return <main className="password-page"><section className="password-panel"><div className="access-error__icon"><KeyRound /></div><span className="eyebrow">Secure your account</span><h1>Choose your password</h1><p>The temporary password can only be used to reach this step. Create a private passphrase before continuing.</p><form className="signin-form" onSubmit={submit}><PasswordField label="Temporary password" autoComplete="current-password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} required /><PasswordField label="New password" autoComplete="new-password" minLength={15} maxLength={128} value={newPassword} onChange={(event) => setNewPassword(event.target.value)} required hint="Use 15–128 characters. A long, unique passphrase is recommended." /><PasswordField label="Confirm new password" autoComplete="new-password" minLength={15} maxLength={128} value={confirmation} onChange={(event) => setConfirmation(event.target.value)} required />{error && <p className="signin-error" role="alert">{error}</p>}<button className="button button--primary" disabled={busy}>{busy ? 'Saving…' : 'Create new password'}</button></form></section></main>
}

function AppLoading() { return <main className="app-loading"><div className="signin-brand"><span className="brand__mark">EH</span><strong>Eltse Hall</strong></div><div className="loading-line" /><p>Preparing your hall tools…</p></main> }

function RouteLoading() { return <div className="page"><div className="skeleton skeleton--title" /><div className="skeleton skeleton--panel" aria-label="Loading page" /></div> }

export function AccessError({ error, onRetry, onSignOut }: { error: Error; onRetry: () => void; onSignOut: () => void }) {
  const forbidden = error instanceof ApiError && error.problem.status === 403
  return <main className="access-error"><div className="access-error__icon">{forbidden ? <LockKeyhole /> : <MoonStar />}</div><span className="eyebrow">Eltse Hall</span><h1>{forbidden ? 'Access is not available' : 'We could not load your account'}</h1><p>{error instanceof ApiError ? error.problem.title : 'Check your connection and try again.'}</p><div className="access-actions"><button className="button button--primary" onClick={onRetry}>Try again</button><button className="button button--quiet" onClick={onSignOut}>Sign out</button></div><small>{forbidden ? 'Ask your Hall Director to confirm that your Eltse Hall account is active.' : 'If this continues, contact Residence Life support.'}</small></main>
}
