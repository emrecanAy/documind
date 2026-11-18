import { BookOpenText, ChevronsUpDown, FileText, FlaskConical, LogOut, MessagesSquare, Settings } from 'lucide-react'
import { Navigate, NavLink, Outlet, useNavigate, useParams } from 'react-router'
import { useWorkspaces } from '@/features/workspaces/api'
import { api } from '@/lib/api'
import { queryClient } from '@/lib/queryClient'
import { cn } from '@/lib/utils'
import { useAuthStore } from '@/stores/auth'

export function RequireAuth() {
  const token = useAuthStore((s) => s.accessToken)
  return token ? <Outlet /> : <Navigate to="/login" replace />
}

export function AppShell() {
  const { workspaceId } = useParams()
  const navigate = useNavigate()
  const { user, refreshToken, clear } = useAuthStore()
  const { data: workspaces } = useWorkspaces()
  const current = workspaces?.find((w) => w.id === workspaceId)

  const logout = async () => {
    if (refreshToken) await api.auth('/api/auth/logout', { refreshToken }).catch(() => undefined)
    clear()
    queryClient.clear()
    navigate('/login')
  }

  const nav = workspaceId
    ? [
        { to: `/w/${workspaceId}/chat`, label: 'Ask', icon: MessagesSquare },
        { to: `/w/${workspaceId}/documents`, label: 'Documents', icon: FileText },
        { to: `/w/${workspaceId}/evaluation`, label: 'Quality & Usage', icon: FlaskConical },
        { to: `/w/${workspaceId}/settings`, label: 'Settings', icon: Settings },
      ]
    : []

  return (
    <div className="flex h-full">
      <aside className="flex w-64 shrink-0 flex-col border-r border-slate-200 bg-white">
        <NavLink to="/workspaces" className="flex items-center gap-2 px-5 py-5 text-lg font-semibold text-slate-900">
          <span className="rounded-lg bg-brand-600 p-1.5 text-white"><BookOpenText className="h-4 w-4" /></span>
          DocuMind
        </NavLink>

        <div className="px-3">
          <label className="px-2 text-xs font-medium uppercase tracking-wide text-slate-400">Workspace</label>
          <div className="relative mt-1">
            <select
              aria-label="Switch workspace"
              className="h-9 w-full appearance-none rounded-lg border border-slate-200 bg-slate-50 pl-3 pr-8 text-sm font-medium text-slate-800 outline-none focus:border-brand-500"
              value={workspaceId ?? ''}
              onChange={(e) => navigate(e.target.value ? `/w/${e.target.value}/chat` : '/workspaces')}
            >
              <option value="">All workspaces…</option>
              {workspaces?.map((w) => (
                <option key={w.id} value={w.id}>{w.name}</option>
              ))}
            </select>
            <ChevronsUpDown className="pointer-events-none absolute right-2.5 top-2.5 h-4 w-4 text-slate-400" />
          </div>
        </div>

        <nav className="mt-4 flex-1 space-y-0.5 px-3">
          {nav.map(({ to, label, icon: Icon }) => (
            <NavLink
              key={to}
              to={to}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
                  isActive ? 'bg-brand-50 text-brand-700' : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900',
                )
              }
            >
              <Icon className="h-4 w-4" />
              {label}
            </NavLink>
          ))}
          {current && <p className="px-3 pt-3 text-xs text-slate-400">Your role: {current.role}</p>}
        </nav>

        <div className="border-t border-slate-200 p-3">
          <div className="flex items-center gap-3 rounded-lg px-2 py-2">
            <div className="flex h-8 w-8 items-center justify-center rounded-full bg-brand-100 text-sm font-semibold text-brand-700">
              {user?.displayName?.[0]?.toUpperCase() ?? '?'}
            </div>
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium text-slate-800">{user?.displayName}</p>
              <p className="truncate text-xs text-slate-500">{user?.email}</p>
            </div>
            <button onClick={logout} title="Sign out" className="rounded p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700">
              <LogOut className="h-4 w-4" />
            </button>
          </div>
        </div>
      </aside>

      <main className="min-w-0 flex-1 overflow-hidden">
        <Outlet />
      </main>
    </div>
  )
}
