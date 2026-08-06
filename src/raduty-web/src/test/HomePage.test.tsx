import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { HomePage } from '../pages/HomePage'
import { directorUser, raUser } from './fixtures'

describe('Home page', () => {
  it('presents schedule, dorm checks, and resident management without dorm sweep', () => {
    render(<MemoryRouter><HomePage user={raUser} /></MemoryRouter>)
    expect(screen.getByRole('link', { name: /Schedule/ })).toHaveAttribute('href', '/schedule')
    expect(screen.getByRole('link', { name: /Dorm check/ })).toHaveAttribute('href', '/dorm-checks')
    expect(screen.getByRole('link', { name: /Residents/ })).toHaveAttribute('href', '/residents')
    expect(screen.queryByText('Dorm sweep')).not.toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Dorm tools' }).children).toHaveLength(3)
  })

  it('gives Hall Directors the same direct resident-management entry point', () => {
    render(<MemoryRouter><HomePage user={directorUser} /></MemoryRouter>)
    expect(screen.getByRole('link', { name: /Residents/ })).toHaveAttribute('href', '/residents')
    expect(screen.getByRole('navigation', { name: 'Dorm tools' }).children).toHaveLength(3)
  })
})
