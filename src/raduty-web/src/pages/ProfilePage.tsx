import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { useState, type FormEvent, type ReactNode } from 'react'
import { KeyRound, LockKeyhole, LogOut, ShieldCheck } from 'lucide-react'
import { z } from 'zod'
import { api, ApiError, changePassword, signOut as endSession } from '../api'
import type { CurrentUser } from '../types'
import { useToast } from '../components/toast'
import { Dialog } from '../components/ui'
import { PasswordField } from '../components/PasswordField'

const schema = z.object({
  phoneNumber: z.string().trim().max(30, 'Use 30 characters or fewer.').regex(/^[0-9+()\- ]*$/, 'Enter a valid phone number.'),
})
type FormValues = z.infer<typeof schema>

export function ProfilePage({ user }: { user: CurrentUser }) {
  const queryClient = useQueryClient()
  const toast = useToast()
  const [passwordOpen, setPasswordOpen] = useState(false)
  const [loggingOut, setLoggingOut] = useState(false)
  const { register, handleSubmit, formState: { errors, isDirty }, reset } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { phoneNumber: user.phoneNumber ?? '' } })
  const mutation = useMutation({ mutationFn: (values: FormValues) => api<CurrentUser>('/api/me/profile', { method: 'PUT', body: JSON.stringify(values) }), onSuccess: (updated) => { queryClient.setQueryData(['me'], updated); reset({ phoneNumber: updated.phoneNumber ?? '' }); toast('Profile updated.') } })
  async function logOut() {
    setLoggingOut(true)
    try { await endSession(); queryClient.clear(); window.location.assign('/') }
    catch { toast('The app could not log you out. Try again.', 'error'); setLoggingOut(false) }
  }
  return <div className="page page--form">
    <div className="page-heading"><div><span className="eyebrow">Account</span><h1>My profile</h1><p>Keep your on-duty contact information current.</p></div></div>
    <div className="profile-layout"><form className="profile-form" onSubmit={handleSubmit((values) => mutation.mutate(values))}>
      <section><div className="section-heading"><div><h2>School account</h2><p>Managed securely by Eltse Hall.</p></div><LockKeyhole size={18} /></div><div className="form-grid"><Field label="First name"><input value={user.firstName} disabled /></Field><Field label="Last name"><input value={user.lastName} disabled /></Field><Field label="School email" wide><input value={user.schoolEmail} disabled /></Field><Field label="Role" wide><input value={user.role === 'Admin' ? 'Administrator' : user.role === 'HallDirector' ? 'Hall Director' : 'Resident Assistant'} disabled /></Field></div></section>
      <section><div className="section-heading"><div><h2>Duty contact details</h2><p>Room assignments are read-only here. Room moves are managed from Residents.</p></div></div><div className="form-grid"><Field label="Room number"><input value={user.roomNumber ?? 'Not assigned'} disabled /></Field><Field label="Phone number" error={errors.phoneNumber?.message}><input {...register('phoneNumber')} type="tel" autoComplete="tel" placeholder="e.g. 312-555-0142" /></Field></div></section>
      {mutation.isError && <p className="inline-error" role="alert">{mutation.error instanceof ApiError ? mutation.error.problem.title : 'Your profile could not be saved.'}</p>}
      <div className="form-actions"><button className="button button--primary" type="submit" disabled={!isDirty || mutation.isPending}>{mutation.isPending ? 'Saving…' : 'Save changes'}</button><button className="button button--quiet" type="button" disabled={!isDirty} onClick={() => reset()}>Discard</button>{isDirty && <span role="status">Unsaved changes</span>}</div>
    </form><aside className="profile-privacy"><ShieldCheck /><h2>Account security</h2><p>Your room and phone number are limited to people authorized for this residence hall.</p><p>Reset your password whenever you think someone else may know it, or log out when you finish on a shared device.</p><div className="profile-account-actions"><button className="button button--quiet" onClick={() => setPasswordOpen(true)}><KeyRound size={16} />Reset password</button><button className="button button--danger-quiet" onClick={logOut} disabled={loggingOut}><LogOut size={16} />{loggingOut ? 'Logging out…' : 'Log out'}</button></div></aside></div>
    <ChangePasswordDialog open={passwordOpen} onClose={() => setPasswordOpen(false)} />
  </div>
}

function ChangePasswordDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const toast = useToast(); const [currentPassword, setCurrentPassword] = useState(''); const [newPassword, setNewPassword] = useState(''); const [confirmation, setConfirmation] = useState(''); const [error, setError] = useState(''); const [busy, setBusy] = useState(false)
  async function submit(event: FormEvent) {
    event.preventDefault(); setError('')
    if (newPassword !== confirmation) { setError('The new passwords do not match.'); return }
    setBusy(true)
    try { await changePassword(currentPassword, newPassword); toast('Password changed.'); setCurrentPassword(''); setNewPassword(''); setConfirmation(''); onClose() }
    catch (cause) { setError(cause instanceof ApiError ? cause.problem.title : 'The password could not be changed.') }
    finally { setBusy(false) }
  }
  return <Dialog open={open} onClose={onClose} title="Reset password"><form className="dialog-form" onSubmit={submit}><PasswordField label="Current password" autoComplete="current-password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} required /><PasswordField label="New password" autoComplete="new-password" minLength={15} maxLength={128} value={newPassword} onChange={(event) => setNewPassword(event.target.value)} required hint="Use 15–128 characters and choose a unique passphrase." /><PasswordField label="Confirm new password" autoComplete="new-password" minLength={15} maxLength={128} value={confirmation} onChange={(event) => setConfirmation(event.target.value)} required />{error && <p className="inline-error" role="alert">{error}</p>}<div className="dialog-actions"><button className="button button--primary" disabled={busy}>{busy ? 'Saving…' : 'Reset password'}</button><button className="button button--quiet" type="button" onClick={onClose}>Cancel</button></div></form></Dialog>
}

function Field({ label, error, wide, children }: { label: string; error?: string; wide?: boolean; children: ReactNode }) {
  return <label className={wide ? 'field field--wide' : 'field'}><span>{label}</span>{children}{error && <small className="field-error">{error}</small>}</label>
}
