import { useEffect, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  List,
  ListItem,
  ListItemText,
  Paper,
  Stack,
  Typography,
} from '@mui/material';
import RestoreFromTrashIcon from '@mui/icons-material/RestoreFromTrash';
import DeleteForeverIcon from '@mui/icons-material/DeleteForever';
import { ApiError, apiService } from '../services/api';
import type { PagedResult, TrashedTask } from '../types/api.types';

const pageSize = 25;

export default function TrashPage() {
  const [result, setResult] = useState<PagedResult<TrashedTask> | null>(null);
  const [offset, setOffset] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [pendingDelete, setPendingDelete] = useState<TrashedTask | null>(null);

  useEffect(() => {
    let active = true;
    apiService.getTaskTrash(offset, pageSize).then(page => {
      if (active) setResult(page);
    }).catch(reason => {
      if (active) setError(reason instanceof ApiError ? reason.message : 'Failed to load Trash.');
    }).finally(() => {
      if (active) setLoading(false);
    });
    return () => { active = false; };
  }, [offset]);

  const tasks = result?.items ?? [];

  const restore = async (task: TrashedTask) => {
    setBusyId(task.id);
    setError(null);
    try {
      await apiService.restoreTrashedTask(task.id);
      if (tasks.length === 1 && offset > 0) setOffset(Math.max(0, offset - pageSize));
      setResult(current => current && ({
        ...current,
        items: current.items.filter(item => item.id !== task.id),
        totalCount: Math.max(0, current.totalCount - 1),
      }));
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Failed to restore the task.');
    } finally {
      setBusyId(null);
    }
  };

  const permanentlyDelete = async () => {
    if (!pendingDelete) return;
    setBusyId(pendingDelete.id);
    setError(null);
    try {
      await apiService.permanentlyDeleteTrashedTask(pendingDelete.id);
      if (tasks.length === 1 && offset > 0) setOffset(Math.max(0, offset - pageSize));
      setResult(current => current && ({
        ...current,
        items: current.items.filter(item => item.id !== pendingDelete.id),
        totalCount: Math.max(0, current.totalCount - 1),
      }));
      setPendingDelete(null);
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Failed to permanently delete the task.');
    } finally {
      setBusyId(null);
    }
  };

  return (
    <Box sx={{ p: { xs: 2, md: 3 }, width: '100%', maxWidth: 960, mx: 'auto' }}>
      <Stack spacing={2}>
        <Box>
          <Typography variant="h4">Trash</Typography>
          <Typography color="text.secondary">Restore tasks or permanently delete them.</Typography>
        </Box>
        {error && <Alert severity="error">{error}</Alert>}
        {loading ? (
          <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}><CircularProgress /></Box>
        ) : tasks.length === 0 ? (
          <Paper variant="outlined" sx={{ p: 4, textAlign: 'center' }}>
            <Typography color="text.secondary">Trash is empty.</Typography>
          </Paper>
        ) : (
          <Paper variant="outlined">
            <List disablePadding>
              {tasks.map(task => (
                <ListItem
                  key={task.id}
                  divider
                  sx={{ alignItems: 'stretch' }}
                >
                  <Stack spacing={1.5} sx={{ width: '100%' }}>
                    <ListItemText
                      primary={task.title}
                      secondary={`Trashed ${new Date(task.trashedAt).toLocaleString()}${task.description ? ` — ${task.description}` : ''}`}
                    />
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                      <Button
                        size="small"
                        startIcon={<RestoreFromTrashIcon />}
                        disabled={busyId === task.id}
                        onClick={() => void restore(task)}
                      >
                        Restore
                      </Button>
                      <Button
                        size="small"
                        color="error"
                        startIcon={<DeleteForeverIcon />}
                        disabled={busyId === task.id}
                        onClick={() => setPendingDelete(task)}
                      >
                        Delete forever
                      </Button>
                    </Stack>
                  </Stack>
                </ListItem>
              ))}
            </List>
          </Paper>
        )}
        {result && result.totalCount > pageSize && (
          <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center' }}>
            <Button disabled={offset === 0 || loading} onClick={() => setOffset(Math.max(0, offset - pageSize))}>
              Previous
            </Button>
            <Typography variant="body2" color="text.secondary">
              {offset + 1}–{Math.min(offset + tasks.length, result.totalCount)} of {result.totalCount}
            </Typography>
            <Button disabled={!result.hasMore || loading} onClick={() => setOffset(offset + pageSize)}>
              Next
            </Button>
          </Stack>
        )}
      </Stack>

      <Dialog open={pendingDelete !== null} onClose={() => busyId === null && setPendingDelete(null)}>
        <DialogTitle>Permanently delete task?</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {pendingDelete ? `“${pendingDelete.title}” will be permanently deleted. This cannot be undone.` : ''}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setPendingDelete(null)} disabled={busyId !== null}>Cancel</Button>
          <Button color="error" variant="contained" onClick={() => void permanentlyDelete()} disabled={busyId !== null}>
            Delete forever
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}
