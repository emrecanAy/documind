import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { AuthResponse, User } from '@/lib/types'

interface AuthState {
  accessToken: string | null
  refreshToken: string | null
  user: User | null
  setSession: (session: AuthResponse) => void
  clear: () => void
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      refreshToken: null,
      user: null,
      setSession: (session) =>
        set({ accessToken: session.accessToken, refreshToken: session.refreshToken, user: session.user }),
      clear: () => set({ accessToken: null, refreshToken: null, user: null }),
    }),
    { name: 'documind-auth' },
  ),
)
