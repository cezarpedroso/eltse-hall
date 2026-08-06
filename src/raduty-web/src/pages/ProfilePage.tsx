import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import type { ReactNode } from 'react'
import { LockKeyhole, ShieldCheck } from 'lucide-react'
import { z } from 'zod'
import { api, ApiError } from '../api'
import type { CurrentUser } from '../types'
import { useToast } from '../components/toast'

const schema = z.object({
  roomNumber: z.string().trim().max(30, 'Use 30 characters or fewer.'),
  phoneNumber: z.string().trim().max(30, 'Use 30 characters or fewer.').regex(/^[0-9+()\- ]*$/, 'Enter a valid phone number.'),
})
type FormValues = z.infer<typeof schema>

export function ProfilePage({ user }: { user: CurrentUser }) {
  const queryClient = useQueryClient()
  const toast = useToast()
  const { register, handleSubmit, formState: { errors, isDirty }, reset } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { roomNumber: user.roomNumber ?? '', phoneNumber: user.phoneNumber ?? '' } })
  const mutation = useMutation({ mutationFn: (values: FormValues) => api<CurrentUser>('/api/me/profile', { method: 'PUT', body: JSON.stringify(values) }), onSuccess: (updated) => { queryClient.setQueryData(['me'], updated); reset({ roomNumber: updated.roomNumber ?? '', phoneNumber: updated.phoneNumber ?? '' }); toast('Profile updated.') } })
  return <div className="page page--form">
    <div className="page-heading"><div><span className="eyebrow">Account</span><h1>My profile</h1><p>Keep your on-duty contact information current.</p></div></div>
    <div className="profile-layout"><form className="profile-form" onSubmit={handleSubmit((values) => mutation.mutate(values))}>
      <section><div className="section-heading"><div><h2>School identity</h2><p>Verified through Microsoft Entra ID.</p></div><LockKeyhole size={18} /></div><div className="form-grid"><Field label="First name"><input value={user.firstName} disabled /></Field><Field label="Last name"><input value={user.lastName} disabled /></Field><Field label="School email" wide><input value={user.schoolEmail} disabled /></Field><Field label="Role" wide><input value={user.role === 'Admin' ? 'Administrator' : user.role === 'HallDirector' ? 'Hall Director' : 'Resident Assistant'} disabled /></Field></div></section>
      <section><div className="section-heading"><div><h2>Duty contact details</h2><p>These details are shown in the authorized RA directory.</p></div></div><div className="form-grid"><Field label="Room number" error={errors.roomNumber?.message}><input {...register('roomNumber')} autoComplete="off" placeholder="e.g. 214" /></Field><Field label="Phone number" error={errors.phoneNumber?.message}><input {...register('phoneNumber')} type="tel" autoComplete="tel" placeholder="e.g. 312-555-0142" /></Field></div></section>
      {mutation.isError && <p className="inline-error" role="alert">{mutation.error instanceof ApiError ? mutation.error.problem.title : 'Your profile could not be saved.'}</p>}
      <div className="form-actions"><button className="button button--primary" type="submit" disabled={!isDirty || mutation.isPending}>{mutation.isPending ? 'Saving…' : 'Save changes'}</button><button className="button button--quiet" type="button" disabled={!isDirty} onClick={() => reset()}>Discard</button>{isDirty && <span role="status">Unsaved changes</span>}</div>
    </form><aside className="profile-privacy"><ShieldCheck /><h2>Who can see this?</h2><p>Your room and phone number are limited to people authorized for this residence hall. They support duty coverage and emergency coordination.</p><p>Trusted name, email, and role fields are managed by Residence Life and Microsoft Entra ID.</p></aside></div>
  </div>
}

function Field({ label, error, wide, children }: { label: string; error?: string; wide?: boolean; children: ReactNode }) {
  return <label className={wide ? 'field field--wide' : 'field'}><span>{label}</span>{children}{error && <small className="field-error">{error}</small>}</label>
}
