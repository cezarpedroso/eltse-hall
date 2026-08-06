import { useDeferredValue, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Mail, MapPin, Phone, Search, ShieldCheck, UserRound } from 'lucide-react'
import { api, ApiError } from '../api'
import type { ResidentAssistant } from '../types'
import { EmptyState, ErrorState } from '../components/ui'

export function DirectoryPage() {
  const [search, setSearch] = useState('')
  const deferredSearch = useDeferredValue(search)
  const directory = useQuery({ queryKey: ['directory', deferredSearch], queryFn: ({ signal }) => api<ResidentAssistant[]>(`/api/resident-assistants${deferredSearch ? `?search=${encodeURIComponent(deferredSearch)}` : ''}`, {}, signal) })
  return <div className="page page--narrow">
    <div className="page-heading"><div><span className="eyebrow">Authorized directory</span><h1>Resident Assistants</h1><p>Contact information for your residence-life team.</p></div></div>
    <div className="directory-toolbar"><label className="search-field"><Search size={18} /><span className="sr-only">Search resident assistants</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search by name or room" /></label><span>{directory.data?.length ?? 0} people</span></div>
    {directory.isError && <ErrorState message={directory.error instanceof ApiError ? directory.error.problem.title : 'The directory could not be loaded.'} onRetry={() => directory.refetch()} />}
    {directory.isLoading && <div className="table-skeleton">{Array.from({ length: 6 }, (_, index) => <div className="skeleton skeleton--row" key={index} />)}</div>}
    {directory.data && !directory.data.length && <EmptyState title="No matches" message="Try a different name or room number." />}
    {!!directory.data?.length && <div className="responsive-table"><table><caption className="sr-only">Resident assistant contact directory</caption><thead><tr><th>Name</th><th>Room</th><th>Phone</th><th>Email</th><th>Role</th></tr></thead><tbody>{directory.data.map((ra) => <tr key={ra.id}><td data-label="Name"><span className="person-cell"><span className="avatar">{ra.firstName[0]}{ra.lastName[0]}</span><strong>{ra.firstName} {ra.lastName}</strong></span></td><td data-label="Room"><span className="with-icon"><MapPin size={15} />{ra.roomNumber ?? 'Not listed'}</span></td><td data-label="Phone">{ra.phoneNumber ? <a href={`tel:${ra.phoneNumber}`} className="with-icon"><Phone size={15} />{ra.phoneNumber}</a> : 'Not listed'}</td><td data-label="Email"><a href={`mailto:${ra.schoolEmail}`} className="with-icon"><Mail size={15} />{ra.schoolEmail}</a></td><td data-label="Role"><span className="role-label">{ra.role === 'ResidentAssistant' ? <UserRound size={15} /> : <ShieldCheck size={15} />}{ra.role === 'Admin' ? 'Admin' : ra.role === 'HallDirector' ? 'Hall Director' : 'RA'}</span></td></tr>)}</tbody></table></div>}
    <div className="privacy-callout"><ShieldCheck size={18} /><p><strong>Respect resident privacy.</strong> Room and phone information is restricted to authorized residence-life staff and should not be shared outside duty operations.</p></div>
  </div>
}
