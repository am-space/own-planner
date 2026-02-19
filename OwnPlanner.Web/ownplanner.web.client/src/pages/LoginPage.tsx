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

export default function LoginPage() {
  const navigate = useNavigate();
  const { login } = useAuth();
  const { mode: colorMode, setMode: setColorMode } = useThemeContext();
  const [formData, setFormData] = useState({
    email: '',
    password: '',
  });
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [aboutOpen, setAboutOpen] = useState(false);

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
    setIsLoading(true);

    try {
      const result = await login(formData.email, formData.password);
      if (result.success) {
        navigate('/chat');
      } else {
        setError(result.error || 'Login failed');
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
            Sign In
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
              variant="outlined"
              value={formData.email}
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
              autoComplete="current-password"
              variant="outlined"
              value={formData.password}
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
              {isLoading ? 'Signing in...' : 'Sign In'}
            </Button>
            <Box sx={{ textAlign: 'center' }}>
              <Link component={RouterLink} to="/register" variant="body2">
                Don't have an account? Sign up
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
    </Container>
  );
}
