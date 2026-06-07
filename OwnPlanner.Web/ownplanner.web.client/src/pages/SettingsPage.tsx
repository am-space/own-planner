import { useEffect, useMemo, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import {
  Alert,
  Box,
  Button,
  Chip,
  Container,
  Divider,
  IconButton,
  Paper,
  Snackbar,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
import { apiService } from '../services/api';
import type { PersonalAccessTokenCreatedResponse, PersonalAccessTokenResponse } from '../types/api.types';

export default function SettingsPage() {
  const [tokens, setTokens] = useState<PersonalAccessTokenResponse[]>([]);
  const [name, setName] = useState('');
  const [generatedToken, setGeneratedToken] = useState<PersonalAccessTokenCreatedResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [copyNotice, setCopyNotice] = useState<string | null>(null);

  const activeTokens = useMemo(() => tokens.filter(token => token.revokedAt === null), [tokens]);
  const revokedTokens = useMemo(() => tokens.filter(token => token.revokedAt !== null), [tokens]);

  useEffect(() => {
    const load = async () => {
      try {
        setTokens(await apiService.getPersonalAccessTokens());
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load personal access tokens');
      } finally {
        setLoading(false);
      }
    };

    load();
  }, []);

  const handleCreate = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!name.trim() || busy) {
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const created = await apiService.createPersonalAccessToken(name.trim());
      setGeneratedToken(created);
      setName('');
      setTokens(prev => [created.token, ...prev]);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create personal access token');
    } finally {
      setBusy(false);
    }
  };

  const handleRevoke = async (tokenId: string) => {
    setBusy(true);
    setError(null);
    try {
      await apiService.revokePersonalAccessToken(tokenId);
      setTokens(prev => prev.map(token => token.id === tokenId ? { ...token, revokedAt: new Date().toISOString() } : token));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to revoke personal access token');
    } finally {
      setBusy(false);
    }
  };

  const handleCopy = async (value: string) => {
    await navigator.clipboard.writeText(value);
    setCopyNotice('Token copied to clipboard');
  };

  const formatDate = (value: string | null) => {
    if (!value) {
      return '—';
    }

    return new Date(value).toLocaleString();
  };

  return (
    <Container maxWidth="md" sx={{ py: 4 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 3 }}>
        <Button
          component={RouterLink}
          to="/chat"
          startIcon={<ArrowBackIcon />}
        >
          Back to chat
        </Button>
        <Typography variant="h4" component="h1">
          Personal access tokens
        </Typography>
      </Box>

      {error && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      {generatedToken && (
        <Alert
          severity="success"
          sx={{ mb: 2 }}
          action={
            <Tooltip title="Copy token">
              <IconButton color="inherit" size="small" onClick={() => handleCopy(generatedToken.plaintextToken)}>
                <ContentCopyIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          }
        >
          <Typography variant="subtitle2" gutterBottom>
            Token created. Copy it now; it will not be shown again.
          </Typography>
          <Typography component="code" sx={{ wordBreak: 'break-all' }}>
            {generatedToken.plaintextToken}
          </Typography>
        </Alert>
      )}

      <Paper sx={{ p: 3, mb: 3 }}>
        <Box component="form" onSubmit={handleCreate} sx={{ display: 'flex', gap: 2, alignItems: 'flex-end' }}>
          <TextField
            fullWidth
            label="Token name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            disabled={busy}
            helperText="Use a descriptive name like Claude Code or MacBook"
          />
          <Button
            type="submit"
            variant="contained"
            startIcon={<AddIcon />}
            disabled={busy || !name.trim()}
          >
            Create
          </Button>
        </Box>
      </Paper>

      <Paper sx={{ p: 3 }}>
        <Typography variant="h6" gutterBottom>
          Active tokens
        </Typography>
        <Divider sx={{ mb: 2 }} />
        {loading ? (
          <Typography color="text.secondary">Loading...</Typography>
        ) : activeTokens.length === 0 ? (
          <Typography color="text.secondary">No active tokens yet.</Typography>
        ) : (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            {activeTokens.map(token => (
              <Paper key={token.id} variant="outlined" sx={{ p: 2 }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 2, alignItems: 'flex-start' }}>
                  <Box>
                    <Typography variant="subtitle1">{token.name}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      Created {formatDate(token.createdAt)} · Last used {formatDate(token.lastUsedAt)}
                    </Typography>
                  </Box>
                  <Button
                    color="error"
                    startIcon={<DeleteIcon />}
                    onClick={() => handleRevoke(token.id)}
                    disabled={busy}
                  >
                    Revoke
                  </Button>
                </Box>
              </Paper>
            ))}
          </Box>
        )}

        {revokedTokens.length > 0 && (
          <>
            <Typography variant="h6" sx={{ mt: 4 }} gutterBottom>
              Revoked tokens
            </Typography>
            <Divider sx={{ mb: 2 }} />
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              {revokedTokens.map(token => (
                <Paper key={token.id} variant="outlined" sx={{ p: 2, opacity: 0.75 }}>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 2, alignItems: 'flex-start' }}>
                    <Box>
                      <Typography variant="subtitle1">{token.name}</Typography>
                      <Typography variant="body2" color="text.secondary">
                        Revoked {formatDate(token.revokedAt)}
                      </Typography>
                    </Box>
                    <Chip label="Revoked" />
                  </Box>
                </Paper>
              ))}
            </Box>
          </>
        )}
      </Paper>

      <Snackbar
        open={copyNotice !== null}
        autoHideDuration={3000}
        onClose={() => setCopyNotice(null)}
        message={copyNotice}
      />
    </Container>
  );
}
