import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { AppLayout } from '../components/Layout'
import { ToastProvider } from '../components/ui'
import { ProfilePage } from '../pages/ProfilePage'
import { directorUser, raUser } from './fixtures'

describe('Role-based navigation and forms', () => {
  it('hides Hall Director navigation from resident assistants', () => {
    render(<MemoryRouter><AppLayout user={raUser}><div>content</div></AppLayout></MemoryRouter>)
    fireEvent.click(screen.getByRole('button', { name: 'Open navigation' }))
    expect(screen.queryByRole('link', { name: 'Director desk' })).not.toBeInTheDocument()
  })

  it('shows Hall Director navigation to directors', () => {
    render(<MemoryRouter><AppLayout user={directorUser}><div>content</div></AppLayout></MemoryRouter>)
    fireEvent.click(screen.getByRole('button', { name: 'Open navigation' }))
    expect(screen.getByRole('link', { name: 'Director desk' })).toBeInTheDocument()
  })

  it('labels trusted and editable profile fields', () => {
    const client = new QueryClient()
    render(<QueryClientProvider client={client}><ToastProvider><ProfilePage user={raUser} /></ToastProvider></QueryClientProvider>)
    expect(screen.getByLabelText('First name')).toBeDisabled()
    expect(screen.getByLabelText('Room number')).toBeEnabled()
    expect(screen.getByLabelText('Phone number')).toHaveAttribute('type', 'tel')
  })
})
