import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { Link, Navigate, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { BookOpenText, ShieldCheck, Sparkles } from 'lucide-react'
import { Button, FieldError, Input, Label } from '@/components/ui'
import { api } from '@/lib/api'
import type { AuthResponse } from '@/lib/types'
import { useAuthStore } from '@/stores/auth'

const loginSchema = z.object({
  email: z.email('Enter a valid email'),
  password: z.string().min(1, 'Password is required'),
})

const registerSchema = z.object({
  displayName: z.string().min(1, 'Name is required').max(100),
  email: z.email('Enter a valid email'),
  password: z.string().min(8, 'At least 8 characters'),
})

function AuthLayout({ title, subtitle, children }: { title: string; subtitle: string; children: React.ReactNode }) {
  const token = useAuthStore((s) => s.accessToken)
  if (token) return <Navigate to="/" replace />

  return (
    <div className="grid min-h-full lg:grid-cols-2">
      <div className="hidden flex-col justify-between bg-gradient-to-br from-brand-700 via-brand-600 to-indigo-500 p-12 text-white lg:flex">
        <div className="flex items-center gap-2 text-lg font-semibold">
          <BookOpenText className="h-6 w-6" /> DocuMind
        </div>
        <div className="space-y-6">
          <h2 className="text-4xl font-semibold leading-tight">Answers from your company knowledge — with sources.</h2>
          <ul className="space-y-3 text-brand-100">
            <li className="flex gap-3"><Sparkles className="h-5 w-5 shrink-0" /> Hybrid semantic + keyword search with reranking</li>
            <li className="flex gap-3"><ShieldCheck className="h-5 w-5 shrink-0" /> Workspace isolation and role-based access</li>
            <li className="flex gap-3"><BookOpenText className="h-5 w-5 shrink-0" /> Every answer cites the exact document and page</li>
          </ul>
        </div>
        <p className="text-sm text-brand-100">RAG reference architecture · .NET 10 · React · Qdrant</p>
      </div>
      <div className="flex items-center justify-center p-6">
        <div className="w-full max-w-sm">
          <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
          <p className="mb-8 mt-1 text-sm text-slate-500">{subtitle}</p>
          {children}
        </div>
      </div>
    </div>
  )
}

export function LoginPage() {
  const navigate = useNavigate()
  const setSession = useAuthStore((s) => s.setSession)
  const form = useForm({ resolver: zodResolver(loginSchema), defaultValues: { email: '', password: '' } })

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      setSession(await api.auth<AuthResponse>('/api/auth/login', values))
      navigate('/')
    } catch (error) {
      toast.error((error as Error).message)
    }
  })

  return (
    <AuthLayout title="Welcome back" subtitle="Sign in to your DocuMind workspace.">
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div>
          <Label htmlFor="email">Email</Label>
          <Input id="email" type="email" autoComplete="email" {...form.register('email')} />
          <FieldError message={form.formState.errors.email?.message} />
        </div>
        <div>
          <Label htmlFor="password">Password</Label>
          <Input id="password" type="password" autoComplete="current-password" {...form.register('password')} />
          <FieldError message={form.formState.errors.password?.message} />
        </div>
        <Button type="submit" className="w-full" loading={form.formState.isSubmitting}>Sign in</Button>
      </form>
      <div className="mt-6 rounded-lg border border-dashed border-slate-300 bg-white p-3 text-xs text-slate-500">
        Demo account (development): <code className="font-mono">demo@documind.local</code> / <code className="font-mono">Demo123!</code>
        <Button
          variant="ghost"
          size="sm"
          className="mt-1 h-7 px-2 text-xs"
          type="button"
          onClick={() => {
            form.setValue('email', 'demo@documind.local')
            form.setValue('password', 'Demo123!')
          }}
        >
          Fill demo credentials
        </Button>
      </div>
      <p className="mt-6 text-center text-sm text-slate-500">
        No account? <Link to="/register" className="font-medium text-brand-600 hover:underline">Create one</Link>
      </p>
    </AuthLayout>
  )
}

export function RegisterPage() {
  const navigate = useNavigate()
  const setSession = useAuthStore((s) => s.setSession)
  const form = useForm({ resolver: zodResolver(registerSchema), defaultValues: { displayName: '', email: '', password: '' } })

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      setSession(await api.auth<AuthResponse>('/api/auth/register', values))
      navigate('/')
    } catch (error) {
      toast.error((error as Error).message)
    }
  })

  return (
    <AuthLayout title="Create your account" subtitle="Start organizing your team's knowledge.">
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div>
          <Label htmlFor="displayName">Full name</Label>
          <Input id="displayName" autoComplete="name" {...form.register('displayName')} />
          <FieldError message={form.formState.errors.displayName?.message} />
        </div>
        <div>
          <Label htmlFor="email">Work email</Label>
          <Input id="email" type="email" autoComplete="email" {...form.register('email')} />
          <FieldError message={form.formState.errors.email?.message} />
        </div>
        <div>
          <Label htmlFor="password">Password</Label>
          <Input id="password" type="password" autoComplete="new-password" {...form.register('password')} />
          <FieldError message={form.formState.errors.password?.message} />
        </div>
        <Button type="submit" className="w-full" loading={form.formState.isSubmitting}>Create account</Button>
      </form>
      <p className="mt-6 text-center text-sm text-slate-500">
        Already registered? <Link to="/login" className="font-medium text-brand-600 hover:underline">Sign in</Link>
      </p>
    </AuthLayout>
  )
}
