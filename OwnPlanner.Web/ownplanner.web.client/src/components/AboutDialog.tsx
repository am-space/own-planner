import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Typography,
  Box,
  Divider,
  IconButton,
  List,
  ListItem,
  ListItemText,
  ListItemIcon,
  Alert,
  AlertTitle,
} from '@mui/material';
import { useEffect, useState } from 'react';
import CloseIcon from '@mui/icons-material/Close';
import GitHubIcon from '@mui/icons-material/GitHub';
import FiberManualRecordIcon from '@mui/icons-material/FiberManualRecord';
import { apiService } from '../services/api';

interface AboutDialogProps {
  open: boolean;
  onClose: () => void;
}

const appVersion = import.meta.env.VITE_APP_VERSION || 'Local-Dev';

export default function AboutDialog({ open, onClose }: AboutDialogProps) {
  const [registeredUserCount, setRegisteredUserCount] = useState<number | null>(null);
  const [isLoadingStats, setIsLoadingStats] = useState(false);

  useEffect(() => {
    if (!open || registeredUserCount !== null) {
      return;
    }

    let isCancelled = false;

    const loadStats = async () => {
      setIsLoadingStats(true);

      try {
        const stats = await apiService.getAuthStats();
        if (!isCancelled) {
          setRegisteredUserCount(stats.registeredUserCount);
        }
      } catch {
        if (!isCancelled) {
          setRegisteredUserCount(null);
        }
      } finally {
        if (!isCancelled) {
          setIsLoadingStats(false);
        }
      }
    };

    loadStats();

    return () => {
      isCancelled = true;
    };
  }, [open, registeredUserCount]);

  const registeredUsersText = isLoadingStats
    ? 'Loading…'
    : registeredUserCount?.toLocaleString() ?? '—';

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        About OwnPlanner
        <IconButton
          aria-label="close"
          onClick={onClose}
          sx={{ color: 'grey.500' }}
        >
          <CloseIcon />
        </IconButton>
      </DialogTitle>

      <DialogContent dividers>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          {/* App Info */}
          <Box>
            <Typography variant="h6" gutterBottom>
              OwnPlanner
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Version {appVersion} · Registered users: {registeredUsersText}
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
              An AI-powered personal planning assistant that helps you manage tasks,
              notes, and stay organized with intelligent conversation.
            </Typography>

          </Box>

          <Alert severity="warning">
            <AlertTitle>Alpha Tech Preview</AlertTitle>
            This is a POC demonstration, not a commercial product.
            Data may be periodically wiped. Do not store sensitive information.
          </Alert>

          <Alert severity="info">
            OwnPlanner uses a single essential authentication cookie (OwnPlanner.Auth) to keep you signed in. No tracking or analytics cookies are set.
          </Alert>

          <Divider />

          {/* Technology Stack */}
          <Box>
            <Typography variant="subtitle1" sx={{ fontWeight: 'bold' }} gutterBottom>
              Built With
            </Typography>
            <List dense disablePadding sx={{ py: 0 }}>
              <ListItem disableGutters sx={{ py: 0, minHeight: 'auto' }}>
                <ListItemIcon sx={{ minWidth: 24 }}>
                  <FiberManualRecordIcon sx={{ fontSize: 8 }} />
                </ListItemIcon>
                <ListItemText
                  primary="Frontend: React 19 + TypeScript + Material UI"
                  slotProps={{ primary: { variant: 'body2', color: 'text.secondary' } }}
                />
              </ListItem>
              <ListItem disableGutters sx={{ py: 0, minHeight: 'auto' }}>
                <ListItemIcon sx={{ minWidth: 24 }}>
                  <FiberManualRecordIcon sx={{ fontSize: 8 }} />
                </ListItemIcon>
                <ListItemText
                  primary="Backend: .NET 10 + ASP.NET Core"
                  slotProps={{ primary: { variant: 'body2', color: 'text.secondary' } }}
                />
              </ListItem>
              <ListItem disableGutters sx={{ py: 0, minHeight: 'auto' }}>
                <ListItemIcon sx={{ minWidth: 24 }}>
                  <FiberManualRecordIcon sx={{ fontSize: 8 }} />
                </ListItemIcon>
                <ListItemText
                  primary="AI: Google Gemini + Mscc.GenerativeAI SDK"
                  slotProps={{ primary: { variant: 'body2', color: 'text.secondary' } }}
                />
              </ListItem>
            </List>
          </Box>

          <Divider />

          {/* GitHub Link */}
          <Box>
            <Typography variant="subtitle1" sx={{ fontWeight: 'bold' }} gutterBottom>
              Open Source
            </Typography>
            <Button
              variant="outlined"
              startIcon={<GitHubIcon />}
              href="https://github.com/am-space/own-planner"
              target="_blank"
              rel="noopener noreferrer"
              sx={{ mt: 1 }}
            >
              View on GitHub
            </Button>
          </Box>

          <Divider />

          {/* Copyright */}
          <Box>
            <Typography variant="body2" color="text.secondary" align="center">
              (C) {new Date().getFullYear()} OwnPlanner. All rights reserved.
            </Typography>
          </Box>
        </Box>
      </DialogContent>

      <DialogActions>
        <Button onClick={onClose} color="primary" variant="contained">
          Close
        </Button>
      </DialogActions>
    </Dialog >
  );
}
