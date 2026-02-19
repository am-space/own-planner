import { useState } from 'react';
import { useNavigate, Link as RouterLink } from 'react-router-dom';
import {
  Container,
  Box,
  TextField,
  Button,
  Typography,
  Paper,
  Link,
  Alert,
  FormControlLabel,
  Checkbox,
  IconButton,
  Tooltip,
} from '@mui/material';
import LightModeIcon from '@mui/icons-material/LightMode';
import DarkModeIcon from '@mui/icons-material/DarkMode';
import ContrastIcon from '@mui/icons-material/Contrast';
import { useAuth } from '../contexts/AuthContext';
import { useThemeContext } from '../contexts/ThemeContext';
import type { ColorModePreference } from '../contexts/ThemeContext';
import AboutDialog from '../components/AboutDialog';
import TermsOfServiceDialog from '../components/TermsOfServiceDialog';

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

export default function RegisterPage() {
  const navigate = useNavigate();
  const { register } = useAuth();
  const { mode: colorMode, setMode: setColorMode } = useThemeContext();
  const [formData, setFormData] = useState({
    email: '',
    username: '',
    password: '',
    confirmPassword: '',
  });
  const [agreedToTerms, setAgreedToTerms] = useState(false);
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [aboutOpen, setAboutOpen] = useState(false);
  const [termsOpen, setTermsOpen] = useState(false);

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

    // Validate terms acceptance
    if (!agreedToTerms) {
      setError('You must agree to the terms to create an account');
      return;
    }

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
      const result = await register(formData.email, formData.username, formData.password);
      if (result.success) {
        navigate('/chat');
      } else {
        setError(result.error || 'Registration failed');
      }
    } catch (err) {
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
            Create Account
          </Typography>

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
              id="email"
              label="Email Address"
              name="email"
              autoComplete="email"
              autoFocus
              value={formData.email}
              onChange={handleChange}
              disabled={isLoading}
            />
            <TextField
              margin="normal"
              required
              fullWidth
              id="username"
              label="Username"
              name="username"
              autoComplete="username"
              value={formData.username}
              onChange={handleChange}
              disabled={isLoading}
            />
            <TextField
              margin="normal"
              required
              fullWidth
              name="password"
              label="Password"
              type="password"
              id="password"
              autoComplete="new-password"
              value={formData.password}
              onChange={handleChange}
              disabled={isLoading}
            />
            <TextField
              margin="normal"
              required
              fullWidth
              name="confirmPassword"
              label="Confirm Password"
              type="password"
              id="confirmPassword"
              autoComplete="new-password"
              value={formData.confirmPassword}
              onChange={handleChange}
              disabled={isLoading}
            />

            {/* AI Disclosure */}
            <Alert severity="info" sx={{ mt: 2, mb: 1 }}>
              <Typography variant="body2">
                This application uses <strong>Google Gemini AI</strong>.
              </Typography>
            </Alert>

            {/* Terms Agreement */}
            <FormControlLabel
              control={
                <Checkbox
                  checked={agreedToTerms}
                  onChange={(e) => setAgreedToTerms(e.target.checked)}
                  disabled={isLoading}
                  color="primary"
                />
              }
              label={
                <Typography variant="body2">
                  I agree to the{' '}
                  <Link
                    component="button"
                    variant="body2"
                    type="button"
                    onClick={(e) => {
                      e.preventDefault();
                      setTermsOpen(true);
                    }}
                    sx={{ cursor: 'pointer', textDecoration: 'underline' }}
                  >
                    terms of service
                  </Link>
                  {' '}and understand that this app uses AI services
                </Typography>
              }
              sx={{ mt: 1, mb: 1, alignItems: 'flex-start' }}
            />

            <Button
              type="submit"
              fullWidth
              variant="contained"
              sx={{ mt: 2, mb: 2 }}
              disabled={isLoading}
            >
              {isLoading ? 'Creating Account...' : 'Register'}
            </Button>
            <Box sx={{ textAlign: 'center' }}>
              <Link component={RouterLink} to="/login" variant="body2">
                Already have an account? Sign in
              </Link>
            </Box>
          </Box>
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

      {/* Terms of Service Dialog */}
      <TermsOfServiceDialog
        open={termsOpen}
        onClose={() => setTermsOpen(false)}
        onAccept={() => setAgreedToTerms(true)}
        showAcceptButton={!agreedToTerms}
      />
    </Container>
  );
}
