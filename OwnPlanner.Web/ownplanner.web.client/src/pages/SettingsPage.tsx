import { useEffect, useMemo, useState } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import {
  Alert,
  Box,
  Button,
  Chip,
  Container,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
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
import DeleteForeverIcon from '@mui/icons-material/DeleteForever';
import DownloadIcon from '@mui/icons-material/Download';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import LinkOffIcon from '@mui/icons-material/LinkOff';
import { apiService } from '../services/api';
import type { PersonalAccessTokenCreatedResponse, PersonalAccessTokenResponse, TelegramConnectionLink, TelegramConnectionStatus } from '../types/api.types';

export default function SettingsPage() {
  const navigate = useNavigate();
  const [tokens, setTokens] = useState<PersonalAccessTokenResponse[]>([]);
  const [name, setName] = useState('');
  const [generatedToken, setGeneratedToken] = useState<PersonalAccessTokenCreatedResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [copyNotice, setCopyNotice] = useState<string | null>(null);
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [deletePassword, setDeletePassword] = useState('');
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [telegram, setTelegram] = useState<TelegramConnectionStatus | null>(null);
  const [telegramLink, setTelegramLink] = useState<TelegramConnectionLink | null>(null);
  const [telegramBusy, setTelegramBusy] = useState(false);
  const [telegramLoading, setTelegramLoading] = useState(true);
  const [telegramError, setTelegramError] = useState<string | null>(null);

  const activeTokens = useMemo(() => tokens.filter(token => token.revokedAt === null), [tokens]);
  const revokedTokens = useMemo(() => tokens.filter(token => token.revokedAt !== null), [tokens]);

  useEffect(() => {
    const loadTokens = async () => {
      try {
        setTokens(await apiService.getPersonalAccessTokens());
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load personal access tokens');
      } finally {
        setLoading(false);
      }
    };

    const loadTelegram = async () => {
      try {
        setTelegram(await apiService.getTelegramConnection());
      } catch (err) {
        setTelegramError(err instanceof Error ? err.message : 'Failed to load Telegram connection status');
      } finally {
        setTelegramLoading(false);
      }
    };

    void loadTokens();
    void loadTelegram();
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

  const handleConnectTelegram = async () => {
    setTelegramBusy(true);
    setError(null);
    setTelegramError(null);
    try {
      const link = await apiService.createTelegramConnection();
      setTelegramLink(link);
      setTelegram(current => current ? { ...current, pending: true } : current);
    } catch (err) {
      setTelegramError(err instanceof Error ? err.message : 'Failed to connect Telegram');
    } finally {
      setTelegramBusy(false);
    }
  };

  const handleDisconnectTelegram = async () => {
    setTelegramBusy(true);
    setError(null);
    try {
      await apiService.disconnectTelegram();
      setTelegram(current => current ? { ...current, connected: false, pending: false, telegramUserId: null, connectedAtUtc: null, mode: null } : current);
      setTelegramLink(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to disconnect Telegram');
    } finally {
      setTelegramBusy(false);
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

  const handleExport = async () => {
    setExporting(true);
    setError(null);
    try {
      await apiService.exportAccountData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to export your data');
    } finally {
      setExporting(false);
    }
  };

  const handleCloseDeleteDialog = () => {
    if (deleting) {
      return;
    }
    setDeleteDialogOpen(false);
    setDeletePassword('');
    setDeleteError(null);
  };

  const handleDeleteAccount = async () => {
    if (!deletePassword || deleting) {
      return;
    }

    setDeleting(true);
    setDeleteError(null);
    try {
      await apiService.deleteAccount(deletePassword);
      // The server has signed us out; send the user to login.
      navigate('/login', { replace: true });
    } catch (err) {
      setDeleteError(err instanceof Error ? err.message : 'Failed to delete account');
    } finally {
      setDeleting(false);
    }
  };

  const handleCopy = async (value: string) => {
    try {
      await navigator.clipboard.writeText(value);
      setCopyNotice('Token copied to clipboard');
    } catch {
      setError('Failed to copy token to clipboard');
    }
  };

  const formatDate = (value: string | null) => {
    if (!value) {
      return '—';
    }

    return new Date(value).toLocaleString();
  };

  return (
    <Container maxWidth="md" sx={{ py: 3 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 3, flexWrap: 'wrap' }}>
        <Button
          component={RouterLink}
          to="/chat"
          startIcon={<ArrowBackIcon />}
          size="small"
        >
          Back to chat
        </Button>
        <Typography variant="h5" component="h1" sx={{ fontWeight: 500 }}>
          Settings
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
        <Typography variant="h6" gutterBottom>Telegram</Typography>
        <Divider sx={{ mb: 2 }} />
        {telegramLoading ? (
          <Typography color="text.secondary">Loading...</Typography>
        ) : telegramError ? (
          <Alert severity="error">{telegramError}</Alert>
        ) : telegram === null ? (
          <Alert severity="error">Telegram connection status is unavailable.</Alert>
        ) : !telegram.enabled ? (
          <Alert severity="info">Telegram integration is not enabled on this OwnPlanner server.</Alert>
        ) : telegram.connected ? (
          <Box sx={{ display: 'flex', gap: 2, alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap' }}>
            <Box>
              <Typography>Connected</Typography>
              <Typography variant="body2" color="text.secondary">
                Telegram user {telegram.telegramUserId} · {telegram.mode ?? 'DayWork'} mode · connected {formatDate(telegram.connectedAtUtc)}
              </Typography>
            </Box>
            <Button color="error" variant="outlined" startIcon={<LinkOffIcon />} disabled={telegramBusy} onClick={handleDisconnectTelegram}>
              Disconnect
            </Button>
          </Box>
        ) : (
          <Box>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
              Link one private Telegram account to use the same planner data and assistant from the bot.
            </Typography>
            {telegram.pending && (
              <Alert severity="warning" sx={{ mb: 2 }}>
                A connection link is pending. Generate a new one if it expired.
              </Alert>
            )}
            <Button variant="contained" startIcon={<OpenInNewIcon />} disabled={telegramBusy} onClick={handleConnectTelegram}>
              {telegram.pending ? 'Generate new link' : 'Connect Telegram'}
            </Button>
            {telegramLink && (
              <Box sx={{ mt: 2 }}>
                <Button component="a" href={telegramLink.url} target="_blank" rel="noopener noreferrer" variant="outlined" startIcon={<OpenInNewIcon />}>
                  Open Telegram
                </Button>
                <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
                  Link expires {formatDate(telegramLink.expiresAtUtc)}.
                </Typography>
              </Box>
            )}
          </Box>
        )}
      </Paper>

      <Paper sx={{ p: 3, mb: 3 }}>
		<Typography variant="h6" gutterBottom>Personal access tokens</Typography>
		<Divider sx={{ mb: 2 }} />
        <Box
          component="form"
          onSubmit={handleCreate}
          sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}
        >
          <Box sx={{ display: 'flex', gap: 2, alignItems: 'center', flexWrap: { xs: 'wrap', sm: 'nowrap' } }}>
            <Box sx={{ flex: 1, minWidth: 0, width: '100%' }}>
              <TextField
                fullWidth
                label="Token name"
                value={name}
                onChange={(event) => setName(event.target.value)}
                disabled={busy}
              />
            </Box>
            <Button
              type="submit"
              variant="contained"
              startIcon={<AddIcon />}
              disabled={busy || !name.trim()}
              sx={{ alignSelf: { xs: 'stretch', sm: 'center' } }}
            >
              Create
            </Button>
          </Box>
          <Typography variant="body2" color="text.secondary" sx={{ px: 1.75 }}>
            Use a descriptive name like Claude Code or MacBook
          </Typography>
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

      <Paper sx={{ p: 3, mt: 3 }}>
        <Typography variant="h6" gutterBottom>
          Your data
        </Typography>
        <Divider sx={{ mb: 2 }} />
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Download a complete copy of your planning data — contexts, goals, task lists and tasks,
          and note lists and notes — as a ZIP containing a standard SQLite database file.
        </Typography>
        <Button
          variant="outlined"
          startIcon={<DownloadIcon />}
          onClick={handleExport}
          disabled={exporting}
        >
          {exporting ? 'Preparing…' : 'Export your data'}
        </Button>
      </Paper>

      <Paper variant="outlined" sx={{ p: 3, mt: 3, borderColor: 'error.main' }}>
        <Typography variant="h6" color="error" gutterBottom>
          Danger zone
        </Typography>
        <Divider sx={{ mb: 2 }} />
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Permanently delete your account and all of your data — contexts, goals, tasks, notes,
          access tokens, and AI usage history. This action cannot be undone.
        </Typography>
        <Button
          color="error"
          variant="outlined"
          startIcon={<DeleteForeverIcon />}
          onClick={() => setDeleteDialogOpen(true)}
        >
          Delete account
        </Button>
      </Paper>

      <Dialog open={deleteDialogOpen} onClose={handleCloseDeleteDialog} maxWidth="xs" fullWidth>
        <DialogTitle>Delete your account?</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            This permanently deletes your account and all associated data. This action is{' '}
            <strong>irreversible</strong>. Enter your current password to confirm.
          </DialogContentText>
          {deleteError && (
            <Alert severity="error" sx={{ mb: 2 }}>
              {deleteError}
            </Alert>
          )}
          <TextField
            autoFocus
            fullWidth
            type="password"
            label="Current password"
            value={deletePassword}
            onChange={(event) => setDeletePassword(event.target.value)}
            disabled={deleting}
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={handleCloseDeleteDialog} disabled={deleting}>
            Cancel
          </Button>
          <Button
            color="error"
            variant="contained"
            onClick={handleDeleteAccount}
            disabled={deleting || !deletePassword}
          >
            {deleting ? 'Deleting…' : 'Delete account'}
          </Button>
        </DialogActions>
      </Dialog>

      <Snackbar
        open={copyNotice !== null}
        autoHideDuration={3000}
        onClose={() => setCopyNotice(null)}
        message={copyNotice}
      />
    </Container>
  );
}
