import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { ThemeProvider, CssBaseline } from '@mui/material';
import { AuthProvider } from './contexts/AuthProvider';
import ThemeContextProvider from './contexts/ThemeContextProvider';
import { useThemeContext } from './contexts/ThemeContext';
import ProtectedRoute from './components/ProtectedRoute';
import LoginPage from './pages/LoginPage';
import RegisterPage from './pages/RegisterPage';
import ForgotPasswordPage from './pages/ForgotPasswordPage';
import ResetPasswordPage from './pages/ResetPasswordPage';
import SettingsPage from './pages/SettingsPage';
import PlannerPage from './pages/PlannerPage';
import PlannerShell from './components/PlannerShell';
import TrashPage from './pages/TrashPage';

function ThemedApp() {
  const { theme } = useThemeContext();
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/register" element={<RegisterPage />} />
            <Route path="/forgot-password" element={<ForgotPasswordPage />} />
            <Route path="/reset-password" element={<ResetPasswordPage />} />
            <Route
              element={
                <ProtectedRoute>
                  <PlannerShell />
                </ProtectedRoute>
              }
            >
              <Route path="/chat" element={null} />
              <Route path="/planner" element={<Navigate to="/planner/tasks" replace />} />
              <Route path="/planner/tasks" element={<PlannerPage section="tasks" />} />
              <Route path="/planner/trash" element={<TrashPage />} />
              <Route path="/planner/goals" element={<PlannerPage section="goals" />} />
              <Route path="/planner/notes" element={<PlannerPage section="notes" />} />
              <Route path="/settings" element={<SettingsPage />} />
            </Route>
            <Route path="/" element={<Navigate to="/chat" replace />} />
            <Route path="*" element={<Navigate to="/chat" replace />} />
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </ThemeProvider>
  );
}

function App() {
  return (
    <ThemeContextProvider>
      <ThemedApp />
    </ThemeContextProvider>
  );
}

export default App;
