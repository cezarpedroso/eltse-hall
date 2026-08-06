import { PublicClientApplication } from '@azure/msal-browser'

const tenantId = import.meta.env.VITE_ENTRA_TENANT_ID || '__ENTRA_TENANT_ID__'
const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID || '__SPA_CLIENT_ID__'

export const isDevelopmentAuth = import.meta.env.VITE_USE_DEVELOPMENT_AUTH === 'true'
export const developmentUser = import.meta.env.VITE_DEVELOPMENT_USER === 'admin' ? 'admin' : import.meta.env.VITE_DEVELOPMENT_USER === 'director' ? 'director' : 'ra'
export const apiScope = import.meta.env.VITE_API_SCOPE || `api://${clientId}/access_as_user`

export const msalInstance = new PublicClientApplication({
  auth: {
    clientId,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: { cacheLocation: 'sessionStorage' },
})

export const loginRequest = { scopes: [apiScope] }
