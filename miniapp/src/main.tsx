import { StrictMode, Suspense } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import './styles/tokens.css'
import './i18n'
import App from './App'
import { ApiError } from './api/client'
import { Loading } from './components/States'
import { ToastProvider } from './hooks/useToast'
import { initTelegram } from './lib/telegram'

initTelegram((scheme) => {
  document.documentElement.dataset.theme = scheme
})

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 15_000,
      // Client errors (403, 404, validation) will not fix themselves; only network / 5xx are retried.
      retry: (failures, error) => !(error instanceof ApiError && error.status < 500) && failures < 2,
    },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Suspense fallback={<Loading full />}>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <ToastProvider>
            <App />
          </ToastProvider>
        </BrowserRouter>
      </QueryClientProvider>
    </Suspense>
  </StrictMode>
)
