import { useState } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import {
  AppBar,
  Box,
  Button,
  Divider,
  Drawer,
  IconButton,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Toolbar,
  Tooltip,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material';
import ChatBubbleOutlineIcon from '@mui/icons-material/ChatBubbleOutlined';
import ChecklistIcon from '@mui/icons-material/Checklist';
import FlagOutlinedIcon from '@mui/icons-material/FlagOutlined';
import NotesOutlinedIcon from '@mui/icons-material/NotesOutlined';
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined';
import MenuIcon from '@mui/icons-material/Menu';
import ChevronLeftIcon from '@mui/icons-material/ChevronLeft';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import KeyboardArrowDownIcon from '@mui/icons-material/KeyboardArrowDown';
import KeyboardArrowUpIcon from '@mui/icons-material/KeyboardArrowUp';
import LogoutIcon from '@mui/icons-material/Logout';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined';
import { useAuth } from '../contexts/useAuth';
import ChatPage from '../pages/ChatPage';
import logo from '../assets/logo.svg';

const expandedNavigationWidth = 232;
const collapsedNavigationWidth = 72;
const assistantPreferenceKey = 'ownplanner.assistant.collapsed';

export interface PlannerShellOutletContext {
  inspectorHost: HTMLDivElement | null;
}

const navigationItems = [
  { label: 'Chat', path: '/chat', icon: <ChatBubbleOutlineIcon /> },
  { label: 'Tasks', path: '/planner/tasks', icon: <ChecklistIcon /> },
  { label: 'Trash', path: '/planner/trash', icon: <DeleteOutlineIcon /> },
  { label: 'Goals', path: '/planner/goals', icon: <FlagOutlinedIcon /> },
  { label: 'Notes', path: '/planner/notes', icon: <NotesOutlinedIcon /> },
  { label: 'Settings', path: '/settings', icon: <SettingsOutlinedIcon /> },
];

export default function PlannerShell() {
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
  const location = useLocation();
  const navigate = useNavigate();
  const { user, logout } = useAuth();
  const [mobileNavigationOpen, setMobileNavigationOpen] = useState(false);
  const [navigationCollapsed, setNavigationCollapsed] = useState(false);
  const [inspectorHost, setInspectorHost] = useState<HTMLDivElement | null>(null);
  const [assistantCollapsed, setAssistantCollapsed] = useState(
    () => sessionStorage.getItem(assistantPreferenceKey) === 'true',
  );
  const [mobileSurface, setMobileSurface] = useState<'planner' | 'chat'>('planner');

  const isPlanner = location.pathname.startsWith('/planner/');
  const isChat = location.pathname === '/chat';
  const isSettings = location.pathname === '/settings';
  const isNavigationCollapsed = navigationCollapsed && !isMobile;
  const navigationWidth = isNavigationCollapsed ? collapsedNavigationWidth : expandedNavigationWidth;
  const currentItem = navigationItems.find(item => item.path === location.pathname) ?? navigationItems[0];

  const handleNavigate = (path: string) => {
    setMobileNavigationOpen(false);
    setMobileSurface('planner');
    navigate(path);
  };

  const handleAssistantCollapsed = (collapsed: boolean) => {
    setAssistantCollapsed(collapsed);
    sessionStorage.setItem(assistantPreferenceKey, String(collapsed));
  };

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  const navigation = (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <Toolbar sx={{ gap: 1.5, px: isNavigationCollapsed ? 1 : 2.5, justifyContent: isNavigationCollapsed ? 'center' : 'flex-start' }}>
        {isNavigationCollapsed ? (
          <Tooltip title="Expand navigation" placement="right">
            <IconButton aria-label="Expand navigation" onClick={() => setNavigationCollapsed(false)}>
              <ChevronRightIcon />
            </IconButton>
          </Tooltip>
        ) : (
          <>
            <Box component="img" src={logo} alt="OwnPlanner" sx={{ width: 34, height: 34 }} />
            <Typography variant="subtitle1" sx={{ flex: 1, fontWeight: 700, whiteSpace: 'nowrap' }}>
              OwnPlanner
            </Typography>
            {!isMobile && (
              <Tooltip title="Collapse navigation">
                <IconButton aria-label="Collapse navigation" edge="end" onClick={() => setNavigationCollapsed(true)}>
                  <ChevronLeftIcon />
                </IconButton>
              </Tooltip>
            )}
          </>
        )}
      </Toolbar>
      <Divider />
      <List component="nav" aria-label="Primary navigation" sx={{ px: 1, py: 1.5 }}>
        {navigationItems.map(item => (
          <Tooltip key={item.path} title={isNavigationCollapsed ? item.label : ''} placement="right">
            <ListItemButton
              selected={location.pathname === item.path}
              onClick={() => handleNavigate(item.path)}
              aria-current={location.pathname === item.path ? 'page' : undefined}
              aria-label={isNavigationCollapsed ? item.label : undefined}
              sx={{ borderRadius: 2, mb: 0.5, minHeight: 44, justifyContent: isNavigationCollapsed ? 'center' : 'flex-start' }}
            >
              <ListItemIcon sx={{ minWidth: isNavigationCollapsed ? 0 : 40, justifyContent: 'center' }}>
                {item.icon}
              </ListItemIcon>
              {!isNavigationCollapsed && <ListItemText primary={item.label} />}
            </ListItemButton>
          </Tooltip>
        ))}
      </List>
      <Box sx={{ mt: 'auto', p: 1 }}>
        {user && !isNavigationCollapsed && (
          <Typography variant="body2" color="text.secondary" noWrap sx={{ px: 2, py: 1 }}>
            {user.username}
          </Typography>
        )}
        <Tooltip title={isNavigationCollapsed ? 'Logout' : ''} placement="right">
          <ListItemButton
            onClick={handleLogout}
            aria-label={isNavigationCollapsed ? 'Logout' : undefined}
            sx={{ borderRadius: 2, justifyContent: isNavigationCollapsed ? 'center' : 'flex-start' }}
          >
            <ListItemIcon sx={{ minWidth: isNavigationCollapsed ? 0 : 40, justifyContent: 'center' }}>
              <LogoutIcon />
            </ListItemIcon>
            {!isNavigationCollapsed && <ListItemText primary="Logout" />}
          </ListItemButton>
        </Tooltip>
      </Box>
    </Box>
  );

  return (
    <Box sx={{ display: 'flex', width: '100%', height: '100dvh', overflow: 'hidden', bgcolor: 'background.default' }}>
      {isMobile ? (
        <Drawer
          open={mobileNavigationOpen}
          onClose={() => setMobileNavigationOpen(false)}
          ModalProps={{ keepMounted: true }}
          slotProps={{ paper: { sx: { width: expandedNavigationWidth } } }}
        >
          {navigation}
        </Drawer>
      ) : (
        <Drawer
          variant="permanent"
          open
          slotProps={{ paper: { sx: { width: navigationWidth, position: 'relative', overflowX: 'hidden' } } }}
          sx={{ width: navigationWidth, flexShrink: 0, transition: theme.transitions.create('width') }}
        >
          {navigation}
        </Drawer>
      )}

      <Box component="main" sx={{ display: 'flex', flexDirection: 'column', flex: 1, minWidth: 0, minHeight: 0 }}>
        {isMobile && (
          <AppBar position="static" color="default" elevation={0} sx={{ borderBottom: 1, borderColor: 'divider' }}>
            <Toolbar sx={{ gap: 1 }}>
              <IconButton aria-label="Open navigation" edge="start" onClick={() => setMobileNavigationOpen(true)}>
                <MenuIcon />
              </IconButton>
              <Typography variant="h6" component="div" sx={{ flex: 1 }}>
                {currentItem.label}
              </Typography>
              {isPlanner && (
                <Button
                  variant="outlined"
                  size="small"
                  startIcon={mobileSurface === 'planner' ? <ChatBubbleOutlineIcon /> : currentItem.icon}
                  onClick={() => setMobileSurface(value => value === 'planner' ? 'chat' : 'planner')}
                >
                  {mobileSurface === 'planner' ? 'Chat' : currentItem.label}
                </Button>
              )}
            </Toolbar>
          </AppBar>
        )}

        <Box sx={{ display: (isPlanner && (!isMobile || mobileSurface === 'planner')) || isSettings ? 'flex' : 'none', flex: 1, minHeight: 0, overflow: isSettings ? 'auto' : 'hidden' }}>
          <Outlet context={{ inspectorHost } satisfies PlannerShellOutletContext} />
        </Box>

        <Box
          sx={{
            display: isChat || (isPlanner && isMobile && mobileSurface === 'chat') || (isPlanner && !isMobile && !assistantCollapsed)
              ? 'flex'
              : 'none',
            flex: isChat || (isPlanner && isMobile) ? 1 : '0 0 clamp(260px, 38vh, 420px)',
            height: isChat || (isPlanner && isMobile) ? 'auto' : 'clamp(260px, 38vh, 420px)',
            minHeight: 0,
            borderTop: isPlanner && !isMobile ? 1 : 0,
            borderColor: 'divider',
            overflow: 'hidden',
            position: 'relative',
          }}
        >
          <Box sx={{ flex: 1, minWidth: 0, minHeight: 0 }}>
            <ChatPage compact={isPlanner && !isMobile} />
          </Box>
          {isPlanner && !isMobile && (
            <Tooltip title="Collapse assistant">
              <IconButton
                aria-label="Collapse assistant"
                aria-expanded="true"
                size="small"
                onClick={() => handleAssistantCollapsed(true)}
                sx={{ position: 'absolute', right: 12, mt: 0.75, zIndex: 2 }}
              >
                <KeyboardArrowDownIcon />
              </IconButton>
            </Tooltip>
          )}
        </Box>

        {isPlanner && !isMobile && assistantCollapsed && (
          <Button
            onClick={() => handleAssistantCollapsed(false)}
            startIcon={<ChatBubbleOutlineIcon />}
            endIcon={<KeyboardArrowUpIcon />}
            aria-expanded="false"
            sx={{ flex: '0 0 52px', borderRadius: 0, borderTop: 1, borderColor: 'divider', justifyContent: 'flex-start', px: 3 }}
          >
            Ask OwnPlanner…
          </Button>
        )}
      </Box>

      <Box
        ref={setInspectorHost}
        sx={{ display: { xs: 'none', lg: 'block' }, flex: '0 0 auto', minWidth: 0, height: '100%' }}
      />
    </Box>
  );
}
