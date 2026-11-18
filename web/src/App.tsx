import { Navigate, Route, Routes } from 'react-router'
import { AppShell, RequireAuth } from '@/components/layout/AppShell'
import { LoginPage, RegisterPage } from '@/features/auth/AuthPages'
import { ChatPage } from '@/features/chat/ChatPage'
import { DocumentsPage } from '@/features/documents/DocumentsPage'
import { EvaluationPage } from '@/features/evaluation/EvaluationPage'
import { SettingsPage } from '@/features/settings/SettingsPage'
import { WorkspacesPage } from '@/features/workspaces/WorkspacesPage'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<AppShell />}>
          <Route index element={<Navigate to="/workspaces" replace />} />
          <Route path="/workspaces" element={<WorkspacesPage />} />
          <Route path="/w/:workspaceId">
            <Route index element={<Navigate to="chat" replace />} />
            <Route path="chat/:conversationId?" element={<ChatPage />} />
            <Route path="documents" element={<DocumentsPage />} />
            <Route path="evaluation" element={<EvaluationPage />} />
            <Route path="settings" element={<SettingsPage />} />
          </Route>
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
