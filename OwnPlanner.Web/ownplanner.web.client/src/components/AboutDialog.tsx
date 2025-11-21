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
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import GitHubIcon from '@mui/icons-material/GitHub';
import FiberManualRecordIcon from '@mui/icons-material/FiberManualRecord';

interface AboutDialogProps {
  open: boolean;
  onClose: () => void;
}

export default function AboutDialog({ open, onClose }: AboutDialogProps) {
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
              Version 1.0.0
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
              An AI-powered personal planning assistant that helps you manage tasks, 
              notes, and stay organized with intelligent conversation.
            </Typography>
          </Box>

          <Divider />

          {/* Technology Stack */}
          <Box>
            <Typography variant="subtitle1" fontWeight="bold" gutterBottom>
              Built With
            </Typography>
            <List dense disablePadding sx={{ py: 0 }}>
              <ListItem disableGutters sx={{ py: 0, minHeight: 'auto' }}>
                <ListItemIcon sx={{ minWidth: 24 }}>
                  <FiberManualRecordIcon sx={{ fontSize: 8 }} />
                </ListItemIcon>
                <ListItemText 
                  primary="Frontend: React 18 + TypeScript + Material-UI"
                  primaryTypographyProps={{ variant: 'body2', color: 'text.secondary' }}
                />
              </ListItem>
              <ListItem disableGutters sx={{ py: 0, minHeight: 'auto' }}>
                <ListItemIcon sx={{ minWidth: 24 }}>
                  <FiberManualRecordIcon sx={{ fontSize: 8 }} />
                </ListItemIcon>
                <ListItemText 
                  primary="Backend: .NET 9 + ASP.NET Core"
                  primaryTypographyProps={{ variant: 'body2', color: 'text.secondary' }}
                />
              </ListItem>
              <ListItem disableGutters sx={{ py: 0, minHeight: 'auto' }}>
                <ListItemIcon sx={{ minWidth: 24 }}>
                  <FiberManualRecordIcon sx={{ fontSize: 8 }} />
                </ListItemIcon>
                <ListItemText 
                  primary="AI: Google Gemini + Mscc.GenerativeAI SDK"
                  primaryTypographyProps={{ variant: 'body2', color: 'text.secondary' }}
                />
              </ListItem>
            </List>
          </Box>

          <Divider />

          {/* GitHub Link */}
          <Box>
            <Typography variant="subtitle1" fontWeight="bold" gutterBottom>
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
    </Dialog>
  );
}
