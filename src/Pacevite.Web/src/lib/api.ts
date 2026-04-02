import axios from 'axios'

// JWT is stored in an in-memory module-level variable, never in localStorage or
// sessionStorage, to prevent XSS attacks from reading the token.
let accessToken: string | null = null

export const tokenStore = {
  set: (token: string) => { accessToken = token },
  clear: () => { accessToken = null },
  get: () => accessToken,
}

export const apiClient = axios.create({
  baseURL: '/api',
})

apiClient.interceptors.request.use(config => {
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`
  }
  return config
})
