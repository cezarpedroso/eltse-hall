import { ArrowRight, CalendarDays, ClipboardCheck, UsersRound } from 'lucide-react'
import { Link } from 'react-router-dom'
import type { CurrentUser } from '../types'

export function HomePage({ user }: { user: CurrentUser }) {
  return <div className="home-page">
    <header className="home-intro">
      <span>{user.residenceHallName}</span>
      <h1>Hi, {user.firstName}.</h1>
      <p>What would you like to do?</p>
    </header>

    <nav className="home-actions" aria-label="Dorm tools">
      <Link className="home-action home-action--primary" to="/schedule">
        <span className="home-action__icon"><CalendarDays /></span>
        <span className="home-action__copy"><strong>Schedule</strong><small>Pick or review night shifts</small></span>
        <ArrowRight className="home-action__arrow" />
      </Link>

      <Link className="home-action" to="/dorm-checks">
        <span className="home-action__icon"><ClipboardCheck /></span>
        <span className="home-action__copy"><strong>Dorm check</strong><small>Check suites and rooms</small></span>
        <ArrowRight className="home-action__arrow" />
      </Link>

      <Link className="home-action" to="/residents">
        <span className="home-action__icon"><UsersRound /></span>
        <span className="home-action__copy"><strong>Residents</strong><small>Add, edit, or move residents</small></span>
        <ArrowRight className="home-action__arrow" />
      </Link>
    </nav>
  </div>
}
