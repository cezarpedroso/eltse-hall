import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { AccessError, SignInPage } from '../App'
import { ApiError } from '../api'
import { AppLayout } from '../components/Layout'
import { ToastProvider } from '../components/ui'
import { ProfilePage } from '../pages/ProfilePage'
import { directorUser, raUser } from './fixtures'

describe('Role-based navigation and forms', () => {
  it('can reveal and hide a typed password', () => {
    render(<SignInPage onSignedIn={vi.fn()} />)
    const password = screen.getByLabelText('Password')
    expect(password).toHaveAttribute('type', 'password')
    fireEvent.click(screen.getByRole('button', { name: 'Show password' }))
    expect(password).toHaveAttribute('type', 'text')
    fireEvent.click(screen.getByRole('button', { name: 'Hide password' }))
    expect(password).toHaveAttribute('type', 'password')
  })

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
    expect(screen.getByLabelText('Room number')).toBeDisabled()
    expect(screen.getByLabelText('Phone number')).toHaveAttribute('type', 'tel')
    expect(screen.getByRole('button', { name: 'Reset password' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Log out' })).toBeInTheDocument()
  })

  it('helps a blocked user sign out of an inactive account', () => {
    const signOut = vi.fn()
    render(<AccessError error={new ApiError({
      status: 403,
      title: 'Your residence-life account is inactive.',
      code: 'USER_INACTIVE',
    })} onRetry={vi.fn()} onSignOut={signOut} />)

    expect(screen.getByRole('heading', { name: 'Access is not available' })).toBeInTheDocument()
    expect(screen.getByText(/account is active/i)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }))
    expect(signOut).toHaveBeenCalledOnce()
  })
})
