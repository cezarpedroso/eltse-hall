import { CalendarDays, ClipboardCheck, ClipboardList, ContactRound, FileSpreadsheet, Home, LogOut, Menu, ShieldCheck, UserRound, UsersRound, X } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { signOut as endSession } from '../api'
import type { CurrentUser } from '../types'

export function AppLayout({ user, children }: { user: CurrentUser; children: ReactNode }) {
  const [mobileOpen, setMobileOpen] = useState(false)
  const location = useLocation()
  const isDirector = user.role === 'HallDirector' || user.role === 'Admin'
  const close = () => setMobileOpen(false)
  const links = [
    { to: '/', label: 'Home', icon: Home, end: true },
    { to: '/schedule', label: 'Schedule', icon: CalendarDays },
    { to: '/dorm-checks', label: 'Dorm check', icon: ClipboardCheck },
    { to: '/residents', label: 'Residents', icon: UsersRound },
    { to: '/directory', label: 'RA directory', icon: ContactRound },
    { to: '/profile', label: 'My profile', icon: UserRound },
    ...(isDirector ? [
      { to: '/admin', label: 'Director desk', icon: ShieldCheck, end: true },
      { to: '/admin/users', label: 'People', icon: UsersRound },
      { to: '/admin/residents', label: 'Roster import', icon: FileSpreadsheet },
      { to: '/admin/audit', label: 'Activity', icon: ClipboardList },
    ] : []),
  ]
  async function signOut() {
    await endSession()
    window.location.assign('/')
  }
  return <div className={`app-shell ${location.pathname === '/schedule' ? 'app-shell--calendar' : ''}`}>
    <header className="topbar">
      <button className="mobile-menu" onClick={() => setMobileOpen(true)} aria-label="Open navigation" title="Open navigation"><Menu /></button>
      <NavLink to="/" className="brand" aria-label={`${user.residenceHallName} home`}><strong>{user.residenceHallName}</strong></NavLink>
      <NavLink to="/profile" className="account-chip" title={`${user.firstName} ${user.lastName}`}><span>{initials(user)}</span><div><strong>{user.firstName} {user.lastName}</strong><small>{user.role === 'Admin' ? 'Administrator' : isDirector ? 'Hall Director' : `RA · Room ${user.roomNumber ?? '—'}`}</small></div></NavLink>
      <NavLink to="/profile" className="mobile-account" aria-label="Open my profile">{initials(user)}</NavLink>
    </header>
    <aside className={`mobile-drawer ${mobileOpen ? 'is-open' : ''}`} aria-hidden={!mobileOpen} inert={mobileOpen ? undefined : true}>
      <div className="mobile-drawer__head"><span className="brand"><strong>{user.residenceHallName}</strong></span><button className="icon-button" onClick={close} aria-label="Close navigation"><X /></button></div>
      <nav aria-label="Mobile navigation">{links.map(({ to, label, icon: Icon, end }) => <NavLink to={to} end={end} key={to} onClick={close}><Icon size={19} />{label}</NavLink>)}</nav>
      <button className="drawer-signout" onClick={signOut}><LogOut size={18} />Sign out</button>
    </aside>
    {mobileOpen && <button className="drawer-scrim" aria-label="Close navigation" onClick={close} />}
    <main id="main-content">{children}</main>
  </div>
}

function initials(user: CurrentUser) { return `${user.firstName[0] ?? ''}${user.lastName[0] ?? ''}` }
