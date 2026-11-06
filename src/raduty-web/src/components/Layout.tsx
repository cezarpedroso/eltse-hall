import { CalendarDays, ClipboardCheck, ClipboardList, FileText, Home, LogOut, Menu, ShieldCheck, UsersRound, X } from 'lucide-react'
import { useEffect, useState, type ReactNode } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { api, setActiveResidenceHall, signOut as endSession } from '../api'
import type { CurrentUser, ResidenceHall } from '../types'

export function AppLayout({ user, children }: { user: CurrentUser; children: ReactNode }) {
  const [mobileOpen, setMobileOpen] = useState(false)
  const location = useLocation()
  const isCampusAdministrator = user.role === 'Admin' || user.role === 'DeanOfStudents' || user.role === 'AssistantDirectorOfResidenceLife'
  const [halls, setHalls] = useState<ResidenceHall[]>([])
  const roleLabel = user.role === 'Admin' ? 'Administrator' : user.role === 'DeanOfStudents' ? 'Dean of Students' : user.role === 'AssistantDirectorOfResidenceLife' ? 'Assistant Director of Residence Life' : user.role === 'HallDirector' ? 'Hall Director' : `RA · Room ${user.roomNumber ?? '—'}`
  const close = () => setMobileOpen(false)
  const links = [
    { to: '/', label: 'Home', icon: Home, end: true },
    { to: '/schedule', label: 'Schedule', icon: CalendarDays },
    { to: '/dorm-checks', label: 'Room check', icon: ClipboardCheck },
    { to: '/dorm-sweeps', label: 'Dorm sweeps', icon: ClipboardList },
    { to: '/incidents', label: 'Incident reports', icon: FileText },
    { to: '/residents', label: 'Residents', icon: UsersRound },
    { to: '/admin', label: 'Manage', icon: ShieldCheck },
  ]
  useEffect(() => {
    if (!isCampusAdministrator) { setHalls([]); return }
    let live = true
    void api<ResidenceHall[]>('/api/admin/halls').then((result) => { if (live) setHalls(result) }).catch(() => { if (live) setHalls([]) })
    return () => { live = false }
  }, [isCampusAdministrator])
  async function signOut() {
    await endSession()
    window.location.assign('/')
  }
  function changeHall(hallId: string) {
    if (hallId === user.residenceHallId) return
    setActiveResidenceHall(hallId)
    window.location.assign('/')
  }
  return <div className={`app-shell ${location.pathname === '/schedule' ? 'app-shell--calendar' : ''}`}>
    <header className="topbar">
      <button className="mobile-menu" onClick={() => setMobileOpen(true)} aria-label="Open navigation" title="Open navigation"><Menu /></button>
      <NavLink to="/" className="brand" aria-label={`${user.residenceHallName} home`}><strong>{user.residenceHallName}</strong></NavLink>
      <nav className="desktop-nav" aria-label="Primary navigation">
        {links.map(({ to, label, end }) => <NavLink to={to} end={end} key={to}>{label}</NavLink>)}
      </nav>
      {isCampusAdministrator && <label className="hall-switcher"><span>Working in</span><select aria-label="Active residence hall" value={user.residenceHallId} onChange={(event) => changeHall(event.target.value)}>{halls.filter((hall) => hall.isActive).map((hall) => <option value={hall.id} key={hall.id}>{hall.name}</option>)}</select></label>}
      <NavLink to="/profile" className="account-chip" title={`${user.firstName} ${user.lastName}`}><span>{initials(user)}</span><div><strong>{user.firstName} {user.lastName}</strong><small>{roleLabel}</small></div></NavLink>
      <NavLink to="/profile" className="mobile-account" aria-label="Open my profile">{initials(user)}</NavLink>
    </header>
    <aside className={`mobile-drawer ${mobileOpen ? 'is-open' : ''}`} aria-hidden={!mobileOpen} inert={mobileOpen ? undefined : true}>
      <div className="mobile-drawer__head"><span className="brand"><strong>{user.residenceHallName}</strong></span><button className="icon-button" onClick={close} aria-label="Close navigation"><X /></button></div>
      {isCampusAdministrator && <label className="hall-switcher hall-switcher--mobile"><span>Working in</span><select aria-label="Active residence hall" value={user.residenceHallId} onChange={(event) => changeHall(event.target.value)}>{halls.filter((hall) => hall.isActive).map((hall) => <option value={hall.id} key={hall.id}>{hall.name}</option>)}</select></label>}
      <nav aria-label="Mobile navigation">{links.map(({ to, label, icon: Icon, end }) => <NavLink to={to} end={end} key={to} onClick={close}><Icon size={19} />{label}</NavLink>)}</nav>
      <button className="drawer-signout" onClick={signOut}><LogOut size={18} />Sign out</button>
    </aside>
    {mobileOpen && <button className="drawer-scrim" aria-label="Close navigation" onClick={close} />}
    <main id="main-content">{children}</main>
  </div>
}

function initials(user: CurrentUser) { return `${user.firstName[0] ?? ''}${user.lastName[0] ?? ''}` }
