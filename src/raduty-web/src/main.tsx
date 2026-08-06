import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MsalProvider } from '@azure/msal-react'
import { msalInstance } from './auth'
import App from './App'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: false }, mutations: { retry: false } },
})

await msalInstance.initialize()
const redirect = await msalInstance.handleRedirectPromise()
if (redirect?.account) msalInstance.setActiveAccount(redirect.account)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <MsalProvider instance={msalInstance}>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter><App /></BrowserRouter>
      </QueryClientProvider>
    </MsalProvider>
  </StrictMode>,
)
