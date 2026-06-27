import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
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

export default function ForgotPasswordPage() {
  const { mode: colorMode, setMode: setColorMode } = useThemeContext();
  const [email, setEmail] = useState('');
  const [submitted, setSubmitted] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [aboutOpen, setAboutOpen] = useState(false);

  const handleCycleColorMode = () => {
    const next = MODE_CYCLE[(MODE_CYCLE.indexOf(colorMode) + 1) % MODE_CYCLE.length];
    setColorMode(next);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsLoading(true);

    try {
      // Anti-enumeration: the response is identical regardless of whether the
      // account exists, so we always show the same confirmation.
      await apiService.forgotPassword(email);
    } catch {
      // Swallow request errors too, so failures don't leak account existence
      // and don't surface as unhandled errors. The confirmation is shown either way.
    } finally {
      setIsLoading(false);
      setSubmitted(true);
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
            Reset your password
          </Typography>

          {submitted ? (
            <>
              <Alert severity="success" sx={{ mb: 2 }}>
                If an account exists for that email, a password reset link has been sent.
              </Alert>
              <Box sx={{ textAlign: 'center' }}>
                <Link component={RouterLink} to="/login" variant="body2">
                  Back to sign in
                </Link>
              </Box>
            </>
          ) : (
            <>
              <Typography variant="body2" color="text.secondary" align="center" sx={{ mb: 2 }}>
                Enter the email address for your account and we'll send you a link to reset your
                password.
              </Typography>

              <Box component="form" onSubmit={handleSubmit} noValidate>
                <TextField
                  margin="normal"
                  required
                  fullWidth
                  id="email"
                  label="Email Address"
                  name="email"
                  autoComplete="email"
                  autoFocus
                  variant="outlined"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  disabled={isLoading}
                />
                <Button
                  type="submit"
                  fullWidth
                  variant="contained"
                  sx={{ mt: 3, mb: 2 }}
                  disabled={isLoading}
                >
                  {isLoading ? 'Sending...' : 'Send reset link'}
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
