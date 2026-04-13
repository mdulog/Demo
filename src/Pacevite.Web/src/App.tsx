import { createBrowserRouter, RouterProvider, Navigate } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ThemeProvider } from '@/context/ThemeContext'
import { AuthProvider } from '@/context/AuthContext'
import { AuthGuard } from '@/components/AuthGuard'
import { LoginPage } from '@/pages/LoginPage'
import { RegisterPage } from '@/pages/RegisterPage'
import { DashboardPage } from '@/pages/DashboardPage'
import { UploadPage } from '@/pages/UploadPage'
import { AddEventPage } from '@/pages/AddEventPage'
import { EventDetailPage } from '@/pages/EventDetailPage'
import { PredictPage } from '@/pages/PredictPage'
import { SyncPage } from '@/pages/SyncPage'
import { ChatWidget } from '@/components/chat/ChatWidget'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 30_000,
    },
  },
})

const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '/register', element: <RegisterPage /> },
  {
    path: '/dashboard',
    element: (
      <AuthGuard>
        <DashboardPage />
      </AuthGuard>
    ),
  },
  {
    path: '/upload',
    element: (
      <AuthGuard>
        <UploadPage />
      </AuthGuard>
    ),
  },
  {
    path: '/events/new',
    element: (
      <AuthGuard>
        <AddEventPage />
      </AuthGuard>
    ),
  },
  {
    path: '/events/:id',
    element: (
      <AuthGuard>
        <EventDetailPage />
      </AuthGuard>
    ),
  },
  {
    path: '/predict',
    element: (
      <AuthGuard>
        <PredictPage />
      </AuthGuard>
    ),
  },
  {
    path: '/sync',
    element: (
      <AuthGuard>
        <SyncPage />
      </AuthGuard>
    ),
  },
  { path: '/', element: <Navigate to="/dashboard" replace /> },
  { path: '*', element: <Navigate to="/dashboard" replace /> },
])

export default function App() {
  return (
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <RouterProvider router={router} />
          <ChatWidget />
        </AuthProvider>
      </QueryClientProvider>
    </ThemeProvider>
  )
}
