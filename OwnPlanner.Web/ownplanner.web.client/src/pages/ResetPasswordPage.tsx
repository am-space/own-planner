import { useEffect, useRef, useState } from 'react';
import { useNavigate, useSearchParams, Link as RouterLink } from 'react-router-dom';
import {
  Container,
  Box,
  TextField,
  Button,
  Typography,
  Paper,
  Link,
  Alert,
  IconButton,
  Tooltip,
} from '@mui/material';
import LightModeIcon from '@mui/icons-material/LightMode';
import DarkModeIcon from '@mui/icons-material/DarkMode';
import ContrastIcon from '@mui/icons-material/Contrast';
import { apiService } from '../services/api';
import { useThemeContext } from '../contexts/ThemeContext';
import type { ColorModePreference } from '../contexts/ThemeContext';
import AboutDialog from '../components/AboutDialog';

const MODE_CYCLE: ColorModePreference[] = ['light', 'dark', 'system'];

const MODE_ICON: Record<ColorModePreference, React.ReactElement> = {
  light: <LightModeIcon />,
  dark: <DarkModeIcon />,
  system: <ContrastIcon />,
};

const MODE_LABEL: Record<ColorModePreference, string> = {
  light: 'Light mode',
  dark: 'Dark mode',
  system: 'System mode',
};

export default function ResetPasswordPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token') ?? '';
  const { mode: colorMode, setMode: setColorMode } = useThemeContext();
  const [formData, setFormData] = useState({
    password: '',
    confirmPassword: '',
  });
  const [error, setError] = useState('');
  const [success, setSuccess] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [aboutOpen, setAboutOpen] = useState(false);
  const redirectTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    return () => {
      if (redirectTimer.current) {
        clearTimeout(redirectTimer.current);
      }
    };
  }, []);

  const handleCycleColorMode = () => {
    const next = MODE_CYCLE[(MODE_CYCLE.indexOf(colorMode) + 1) % MODE_CYCLE.length];
    setColorMode(next);
  };

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setFormData({
      ...formData,
      [e.target.name]: e.target.value,
    });
    setError('');
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');

    // Validate passwords match
    if (formData.password !== formData.confirmPassword) {
      setError('Passwords do not match');
      return;
    }

    // Validate password length
    if (formData.password.length < 8) {
      setError('Password must be at least 8 characters');
      return;
    }

    setIsLoading(true);

    try {
      const result = await apiService.resetPassword(token, formData.password);
      if (result.success) {
        setSuccess(true);
        redirectTimer.current = setTimeout(() => navigate('/login'), 2000);
      } else {
        setError(result.errorMessage || 'Failed to reset password');
      }
    } catch {
      setError('An unexpected error occurred');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <Container component="main" maxWidth="xs">
      <Box
        sx={{
          minHeight: '100vh',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <Box sx={{ position: 'absolute', top: 16, right: 16 }}>
          <Tooltip title={MODE_LABEL[colorMode]}>
            <IconButton onClick={handleCycleColorMode} color="inherit">
              {MODE_ICON[colorMode]}
            </IconButton>
          </Tooltip>
        </Box>
        <Paper elevation={3} sx={{ p: 4, width: '100%' }}>
          <Typography component="h1" variant="h5" align="center" gutterBottom>
            Set a new password
          </Typography>

          {!token ? (
            <>
              <Alert severity="error" sx={{ mb: 2 }}>
                Invalid or missing reset link. Please request a new one.
              </Alert>
              <Box sx={{ textAlign: 'center' }}>
                <Link component={RouterLink} to="/forgot-password" variant="body2">
                  Request a new reset link
                </Link>
              </Box>
            </>
          ) : success ? (
            <>
              <Alert severity="success" sx={{ mb: 2 }}>
                Your password has been reset. Redirecting to sign in...
              </Alert>
              <Box sx={{ textAlign: 'center' }}>
                <Link component={RouterLink} to="/login" variant="body2">
                  Go to sign in
                </Link>
              </Box>
            </>
          ) : (
            <>
              {error && (
                <Alert severity="error" sx={{ mb: 2 }}>
                  {error}
                </Alert>
              )}

              <Box component="form" onSubmit={handleSubmit} noValidate>
                <TextField
                  margin="normal"
                  required
                  fullWidth
                  name="password"
                  label="New Password"
                  type="password"
                  id="password"
                  autoComplete="new-password"
                  autoFocus
                  variant="outlined"
                  value={formData.password}
                  onChange={handleChange}
                  disabled={isLoading}
                />
                <TextField
                  margin="normal"
                  required
                  fullWidth
                  name="confirmPassword"
                  label="Confirm New Password"
                  type="password"
                  id="confirmPassword"
                  autoComplete="new-password"
                  variant="outlined"
                  value={formData.confirmPassword}
                  onChange={handleChange}
                  disabled={isLoading}
                />
                <Button
                  type="submit"
                  fullWidth
                  variant="contained"
                  sx={{ mt: 3, mb: 2 }}
                  disabled={isLoading}
                >
                  {isLoading ? 'Resetting...' : 'Reset password'}
                </Button>
                <Box sx={{ textAlign: 'center' }}>
                  <Link component={RouterLink} to="/login" variant="body2">
                    Back to sign in
                  </Link>
                </Box>
              </Box>
            </>
          )}
        </Paper>

        {/* About Link */}
        <Box sx={{ mt: 3, textAlign: 'center' }}>
          <Link
            component="button"
            variant="body2"
            onClick={() => setAboutOpen(true)}
            sx={{ cursor: 'pointer' }}
          >
            About OwnPlanner
          </Link>
        </Box>
      </Box>

      {/* About Dialog */}
      <AboutDialog open={aboutOpen} onClose={() => setAboutOpen(false)} />
    </Container>
  );
}
