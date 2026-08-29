import { useState, useEffect, useRef, useCallback } from 'react';
import {
    Box,
    Container,
    Paper,
    TextField,
    IconButton,
    Typography,
    AppBar,
    Toolbar,
    Button,
    Chip,
    CircularProgress,
    Alert,
    Snackbar,
    Tooltip,
    Divider,
    useTheme,
} from '@mui/material';
import SendIcon from '@mui/icons-material/Send';
import DeleteIcon from '@mui/icons-material/Delete';
import InfoIcon from '@mui/icons-material/Info';
import LightbulbOutlinedIcon from '@mui/icons-material/LightbulbOutlined';
import LightModeIcon from '@mui/icons-material/LightMode';
import DarkModeIcon from '@mui/icons-material/DarkMode';
import ContrastIcon from '@mui/icons-material/Contrast';
import { useThemeContext } from '../contexts/ThemeContext';
import type { ColorModePreference } from '../contexts/ThemeContext';
import { apiService, RateLimitError } from '../services/api';
import type { PlanningMode } from '../types/api.types';
import AboutDialog from '../components/AboutDialog';
import PlanningModeSelector from '../components/PlanningModeSelector';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';

interface Message {
    id: string;
    text: string;
    sender: 'user' | 'assistant' | 'system';
    timestamp: Date;
}

const MODE_LABELS: Record<string, string> = {
    GlobalPlanning: 'Global Planning',
    WeekPlanning: 'Week Planning',
    DayWork: 'Day Work',
    Reflection: 'Reflection',
    SystemAnalysis: 'System Analysis',
};

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

interface ChatPageProps {
    compact?: boolean;
}

export default function ChatPage({ compact = false }: ChatPageProps) {
    const theme = useTheme();
    const { mode: colorMode, setMode: setColorMode } = useThemeContext();
    const [messages, setMessages] = useState<Message[]>([]);
    const [inputText, setInputText] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [aboutOpen, setAboutOpen] = useState(false);
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const inputRef = useRef<HTMLInputElement>(null);
    const [planningMode, setPlanningMode] = useState<PlanningMode>('DayWork');
    const [isSwitchingMode, setIsSwitchingMode] = useState(false);
    const [starterPrompts, setStarterPrompts] = useState<string[]>([]);
    const [showStarterPrompts, setShowStarterPrompts] = useState(false);
    const [contextLengthTokens, setContextLengthTokens] = useState<number | null>(null);
    const [maxContextLengthTokens, setMaxContextLengthTokens] = useState(64 * 1024);
    const [contextResetLabel, setContextResetLabel] = useState<string | null>(null);
    const [remainingDailyQuota, setRemainingDailyQuota] = useState<number | null>(null);

    const formatTokenCount = (value: number | null) =>
        value === null ? '—' : value.toLocaleString();

    // Format an ISO timestamp to a local HH:MM for "resets at ..." messaging.
    const formatResetTime = (iso: string | null) => {
        if (!iso) return null;
        const date = new Date(iso);
        return Number.isNaN(date.getTime())
            ? null
            : date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    };
    const contextUsageValue = contextLengthTokens ?? 0;
    const contextUsagePercent = Math.min((contextUsageValue / Math.max(maxContextLengthTokens, 1)) * 100, 100);
    const contextIndicatorColor = contextUsagePercent >= 100
        ? 'error.main'
        : contextUsagePercent >= 80
            ? 'warning.main'
            : 'success.main';

    const fetchAndSetStarterPrompts = useCallback(async (mode: PlanningMode) => {
        try {
            const result = await apiService.getModeStarterPrompts(mode);
            setStarterPrompts(result.starterPrompts);
            setShowStarterPrompts(true);
        } catch {
            setStarterPrompts([]);
            setShowStarterPrompts(false);
        }
    }, []);

    useEffect(() => {
        const init = async () => {
            try {
                const status = await apiService.getChatSessionStatus();
                const initialMode = status.currentMode ?? 'DayWork';

                setPlanningMode(initialMode);
                setContextLengthTokens(status.contextLengthTokens);
                // Instead of hard-coding, preserve the current configured value
                setMaxContextLengthTokens(prev => status.maxContextLengthTokens ?? prev);
                setContextResetLabel(null);
                setRemainingDailyQuota(status.remainingDailyQuota);

                if (!status.isActive) {
                    try {
                        await apiService.switchPlanningMode('DayWork');
                    } catch {
                        // non-critical: server activates DayWork lazily on first message
                    }
                }

                await fetchAndSetStarterPrompts(initialMode);
            } catch {
                setContextLengthTokens(null);
                setMaxContextLengthTokens(64 * 1024);
                setContextResetLabel(null);
                try {
                    await apiService.switchPlanningMode('DayWork');
                } catch {
                    // non-critical: server activates DayWork lazily on first message
                }
                await fetchAndSetStarterPrompts('DayWork');
            }
        };
        init();
    }, [fetchAndSetStarterPrompts]);

    const handleCycleColorMode = () => {
        const next = MODE_CYCLE[(MODE_CYCLE.indexOf(colorMode) + 1) % MODE_CYCLE.length];
        setColorMode(next);
    };

    // Auto-scroll to bottom when messages change
    useEffect(() => {
        messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
    }, [messages, isLoading]);

    const handleClearSession = async () => {
        try {
            await apiService.clearChatSession();
            setMessages([]);
            setPlanningMode('DayWork');
            setContextLengthTokens(null);
            setContextResetLabel('Context cleared');
            setError(null);
            try {
                await apiService.switchPlanningMode('DayWork');
            } catch {
                // non-critical: server activates DayWork lazily on first message
            }
            // Fetch status to get the current configured maxContextLengthTokens
            try {
                const status = await apiService.getChatSessionStatus();
                setMaxContextLengthTokens(status.maxContextLengthTokens ?? maxContextLengthTokens);
            } catch {
                // Keep existing maxContextLengthTokens if status fetch fails
            }
            await fetchAndSetStarterPrompts('DayWork');
            // Refocus input after clearing
            inputRef.current?.focus();
        } catch (err) {
            setError('Failed to clear session');
            console.error('Error clearing session:', err);
        }
    };

    const handleSwitchMode = async (mode: PlanningMode) => {
        if (mode === planningMode || isSwitchingMode) return;
        setIsSwitchingMode(true);
        try {
            await apiService.switchPlanningMode(mode);
            setPlanningMode(mode);
            setContextLengthTokens(null);
            setContextResetLabel('Context reset');
            // Fetch status to get the current configured maxContextLengthTokens
            try {
                const status = await apiService.getChatSessionStatus();
                setMaxContextLengthTokens(prev => status.maxContextLengthTokens ?? prev);
            } catch {
                // Keep existing maxContextLengthTokens if status fetch fails
            }
            await fetchAndSetStarterPrompts(mode);
            setMessages((prev) => [
                ...prev,
                {
                    id: `mode-switch-${Date.now()}`,
                    text: `Switched to ${MODE_LABELS[mode] ?? mode}`,
                    sender: 'system',
                    timestamp: new Date(),
                },
            ]);
        } catch (err) {
            setError(err instanceof Error ? err.message : 'Failed to switch planning mode');
        } finally {
            setIsSwitchingMode(false);
        }
    };

    const handleSendMessage = async () => {
        if (!inputText.trim() || isLoading) return;

        const messageToSend = inputText; // Capture the message before clearing
        const userMessage: Message = {
            id: Date.now().toString(),
            text: messageToSend,
            sender: 'user',
            timestamp: new Date(),
        };

        setMessages((prev) => [...prev, userMessage]);
        setInputText('');
        setIsLoading(true);
        setError(null);
        setShowStarterPrompts(false);

        try {
            const response = await apiService.sendChatMessage(messageToSend);

            const assistantMessage: Message = {
                id: (Date.now() + 1).toString(),
                text: response.message,
                sender: 'assistant',
                timestamp: new Date(response.timestamp),
            };

            setMessages((prev) => [...prev, assistantMessage]);
            setContextLengthTokens(response.contextLengthTokens);
            setMaxContextLengthTokens(response.maxContextLengthTokens);
            setContextResetLabel(null);
            setRemainingDailyQuota(response.remainingDailyQuota);
        } catch (err) {
            if (err instanceof RateLimitError) {
                const resetAt = formatResetTime(err.quotaResetAtUtc);
                setError(
                    err.limitKind === 'Daily' && resetAt
                        ? `Daily limit reached — resets at ${resetAt}`
                        : err.message,
                );
                if (err.limitKind === 'Daily') {
                    setRemainingDailyQuota(0);
                }
            } else {
                setError(err instanceof Error ? err.message : 'Failed to send message');
            }
            console.error('Error sending message:', err);

            // Remove the user message if sending failed
            setMessages((prev) => prev.filter((m) => m.id !== userMessage.id));
            // Restore input text
            setInputText(messageToSend);
        } finally {
            setIsLoading(false);
        }
    };

    const handleKeyDown = (e: React.KeyboardEvent) => {
        // Prevent sending while loading
        if (isLoading) return;

        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            handleSendMessage();
        }
    };

    const handlePromptClick = (prompt: string) => {
        setInputText(prompt);
        // Focus the input field after setting the text
        inputRef.current?.focus();
    };

    return (
        <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 }}>
            {/* Header */}
            {compact ? (
                <AppBar position="static">
                    <Toolbar variant="dense" sx={{ minHeight: 48, gap: 1.5 }}>
                        <Tooltip title="About OwnPlanner">
                            <IconButton aria-label="About OwnPlanner" color="inherit" size="small" onClick={() => setAboutOpen(true)}>
                                <InfoIcon fontSize="small" />
                            </IconButton>
                        </Tooltip>
                        <Typography variant="subtitle1" component="h2" sx={{ fontWeight: 600, flexShrink: 0 }}>
                            Assistant
                        </Typography>
                        <PlanningModeSelector
                            currentMode={planningMode}
                            disabled={isLoading || isSwitchingMode}
                            loading={isSwitchingMode}
                            onChange={handleSwitchMode}
                            sx={{
                                minWidth: 180,
                                ml: 'auto',
                                color: 'white',
                                '& .MuiOutlinedInput-notchedOutline': { borderColor: 'rgba(255,255,255,0.5)' },
                                '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: 'white' },
                                '&.Mui-focused .MuiOutlinedInput-notchedOutline': { borderColor: 'white' },
                                '& .MuiSvgIcon-root': { color: 'white' },
                            }}
                        />
                        <Tooltip title="Clear chat session">
                            <span>
                                <IconButton
                                    aria-label="Clear chat session"
                                    size="small"
                                    color="inherit"
                                    onClick={handleClearSession}
                                    disabled={isLoading}
                                >
                                    <DeleteIcon fontSize="small" />
                                </IconButton>
                            </span>
                        </Tooltip>
                    </Toolbar>
                </AppBar>
            ) : (
            <AppBar position="static">
                <Toolbar sx={{ gap: 1, minHeight: 56 }}>
                    <Tooltip title="About OwnPlanner">
                        <IconButton aria-label="About OwnPlanner" color="inherit" onClick={() => setAboutOpen(true)}>
                            <InfoIcon />
                        </IconButton>
                    </Tooltip>
                    <Typography variant="h6" component="div" sx={{ flexShrink: 0 }}>
                        Chat
                    </Typography>
                    <Box sx={{ display: { xs: 'none', sm: 'flex' }, ml: 1 }}>
                        <PlanningModeSelector
                            currentMode={planningMode}
                            disabled={isLoading || isSwitchingMode}
                            loading={isSwitchingMode}
                            onChange={handleSwitchMode}
                            sx={{
                                color: 'white',
                                '& .MuiOutlinedInput-notchedOutline': { borderColor: 'rgba(255,255,255,0.5)' },
                                '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: 'white' },
                                '&.Mui-focused .MuiOutlinedInput-notchedOutline': { borderColor: 'white' },
                                '& .MuiSvgIcon-root': { color: 'white' },
                            }}
                        />
                    </Box>

                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, ml: 'auto', minWidth: 0 }}>
                        {remainingDailyQuota !== null && (
                            <Tooltip title={`${formatTokenCount(remainingDailyQuota)} chat requests remaining today`}>
                                <Chip
                                    size="small"
                                    label={`${formatTokenCount(remainingDailyQuota)} left`}
                                    color={remainingDailyQuota <= 0 ? 'error' : remainingDailyQuota <= 10 ? 'warning' : 'default'}
                                    sx={{ color: 'white', borderColor: 'rgba(255,255,255,0.5)', display: { xs: 'none', md: 'flex' } }}
                                    variant="outlined"
                                />
                            </Tooltip>
                        )}
                        <Tooltip title={contextResetLabel ?? `Context ${formatTokenCount(contextLengthTokens)} / ${formatTokenCount(maxContextLengthTokens)} tokens`}>
                            <Box
                                sx={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: 1,
                                    px: 1,
                                    py: 0.5,
                                    borderRadius: 5,
                                    bgcolor: contextResetLabel ? 'rgba(255,193,7,0.25)' : 'rgba(255,255,255,0.12)',
                                }}
                            >
                                <Typography variant="caption" sx={{ color: 'white', whiteSpace: 'nowrap' }}>
                                    {contextResetLabel ?? formatTokenCount(contextLengthTokens)}
                                </Typography>
                                <Box sx={{ position: 'relative', display: 'inline-flex', flexShrink: 0 }}>
                                    <CircularProgress
                                        variant="determinate"
                                        value={100}
                                        size={18}
                                        thickness={6}
                                        sx={{ color: 'rgba(255,255,255,0.25)' }}
                                    />
                                    <CircularProgress
                                        variant="determinate"
                                        value={contextUsagePercent}
                                        size={18}
                                        thickness={6}
                                        sx={{ color: contextIndicatorColor, position: 'absolute', left: 0 }}
                                    />
                                </Box>
                            </Box>
                        </Tooltip>

                        {/* Theme toggle */}
                        <Tooltip title={MODE_LABEL[colorMode]}>
                            <IconButton aria-label={MODE_LABEL[colorMode]} color="inherit" onClick={handleCycleColorMode}>
                                {MODE_ICON[colorMode]}
                            </IconButton>
                        </Tooltip>
                        <Button
                            color="inherit"
                            startIcon={<DeleteIcon />}
                            onClick={handleClearSession}
                            disabled={isLoading}
                            sx={{ display: { xs: 'none', sm: 'inline-flex' } }}
                        >
                            Clear
                        </Button>
                        <Tooltip title="Clear chat session">
                            <span>
                                <IconButton
                                    aria-label="Clear chat session"
                                    color="inherit"
                                    onClick={handleClearSession}
                                    disabled={isLoading}
                                    sx={{ display: { xs: 'inline-flex', sm: 'none' } }}
                                >
                                    <DeleteIcon />
                                </IconButton>
                            </span>
                        </Tooltip>
                    </Box>
                </Toolbar>
            </AppBar>
            )}

            {/* About Dialog */}
            <AboutDialog open={aboutOpen} onClose={() => setAboutOpen(false)} />

            {/* Error Snackbar */}
            <Snackbar
                open={!!error}
                autoHideDuration={6000}
                onClose={() => setError(null)}
                anchorOrigin={{ vertical: 'top', horizontal: 'center' }}
            >
                <Alert onClose={() => setError(null)} severity="error" sx={{ width: '100%' }}>
                    {error}
                </Alert>
            </Snackbar>

            {/* Chat Messages */}
            <Container maxWidth="md" sx={{ flexGrow: 1, minHeight: 0, overflow: 'auto', py: compact ? 1.5 : 3 }}>
                <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                    {messages.length === 0 && (
                        <Paper
                            elevation={0}
                            sx={{
                                p: compact ? 2 : 3,
                                textAlign: 'center',
                                bgcolor: 'background.default',
                                border: '1px dashed',
                                borderColor: 'divider',
                            }}
                        >
                            <Typography variant="h6" color="text.secondary" gutterBottom>
                                Welcome to OwnPlanner Chat!
                            </Typography>
                            <Typography variant="body2" color="text.secondary">
                                I'm your AI assistant. I can help you manage tasks, notes, and answer questions.
                                Start by typing a message below or try one of these suggestions!
                            </Typography>
                        </Paper>
                    )}

                    {messages.map((message) => (
                        <Box
                            key={message.id}
                            sx={{
                                display: 'flex',
                                justifyContent: message.sender === 'user' ? 'flex-end' : 'flex-start',
                            }}
                        >
                            {message.sender === 'system' ? (
                                <Divider sx={{ width: '100%', my: 1 }}>
                                    <Typography variant="caption" color="text.secondary">
                                        {message.text}
                                    </Typography>
                                </Divider>
                            ) : (
                                <Paper
                                    elevation={1}
                                    sx={{
                                        p: 2,
                                        maxWidth: '90%',
                                        bgcolor: message.sender === 'user'
                                            ? 'primary.main'
                                            : (theme.palette.mode === 'dark' ? 'grey.800' : 'grey.100'),
                                        color: message.sender === 'user' ? 'white' : 'text.primary',
                                    }}
                                >
                                    <Box sx={{
                                        '& p': { m: 0 },
                                        '& ul, & ol': { mt: 0.5, mb: 0.5, pl: 2 },
                                        '& li': { mb: 0.25 },
                                        '& code': {
                                            bgcolor: theme.palette.mode === 'dark' ? 'rgba(255,255,255,0.1)' : 'rgba(0,0,0,0.1)',
                                            p: 0.5,
                                            borderRadius: 1,
                                            fontFamily: 'monospace',
                                            fontSize: '0.875rem'
                                        },
                                        '& pre': {
                                            bgcolor: theme.palette.mode === 'dark' ? 'rgba(255,255,255,0.1)' : 'rgba(0,0,0,0.1)',
                                            p: 1,
                                            borderRadius: 1,
                                            overflowX: 'auto',
                                            '& code': {
                                                bgcolor: 'transparent',
                                                p: 0
                                            }
                                        },
                                        '& a': {
                                            color: 'inherit',
                                            textDecoration: 'underline'
                                        },
                                        '& table': {
                                            borderCollapse: 'collapse',
                                            width: '100%',
                                            mt: 1,
                                            mb: 1
                                        },
                                        '& th, & td': {
                                            border: '1px solid',
                                            borderColor: 'divider',
                                            p: 1
                                        },
                                        '& th': {
                                            bgcolor: theme.palette.mode === 'dark' ? 'rgba(255,255,255,0.05)' : 'rgba(0,0,0,0.05)',
                                            fontWeight: 'bold'
                                        }
                                    }}>
                                        <ReactMarkdown
                                            remarkPlugins={[remarkGfm]}
                                            components={{
                                         table: (props) => (
                                                    <Box sx={{ overflowX: 'auto', display: 'block', maxWidth: '100%' }}>
                                                        <table {...props} />
                                                    </Box>
                                                )
                                            }}
                                        >
                                            {message.text}
                                        </ReactMarkdown>
                                    </Box>
                                    <Typography
                                        variant="caption"
                                        sx={{
                                            display: 'block',
                                            mt: 0.5,
                                            opacity: 0.7,
                                        }}
                                    >
                                        {message.timestamp.toLocaleTimeString()}
                                    </Typography>
                                </Paper>
                            )}
                        </Box>
                    ))}

                    {isLoading && (
                        <Box sx={{ display: 'flex', justifyContent: 'flex-start' }}>
                            <Paper
                                elevation={1}
                                sx={{
                                    p: 2,
                                    bgcolor: theme.palette.mode === 'dark' ? 'grey.800' : 'grey.100',
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: 1,
                                }}
                            >
                                <CircularProgress size={20} />
                                <Typography variant="body2" color="text.secondary">
                                    Thinking...
                                </Typography>
                            </Paper>
                        </Box>
                    )}

                    <Box ref={messagesEndRef} />
                </Box>
            </Container>

            {/* Starter Prompts — fixed above input */}
            {!compact && showStarterPrompts && starterPrompts.length > 0 && (
                <Box
                    sx={{
                        px: 2,
                        py: 1,
                        bgcolor: 'background.paper',
                        borderTop: 1,
                        borderColor: 'divider',
                    }}
                >
                    <Container maxWidth="md">
                        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1, alignItems: 'center' }}>
                            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                                <LightbulbOutlinedIcon fontSize="small" color="action" />
                                <Typography variant="body2" color="text.secondary" sx={{ fontWeight: 'medium' }}>
                                    {planningMode === 'SystemAnalysis' ? 'Run analysis:' : 'Suggestions:'}
                                </Typography>
                            </Box>
                            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, justifyContent: 'center' }}>
                                {planningMode === 'SystemAnalysis' ? (
                                    starterPrompts.map((prompt, index) => (
                                        <Button
                                            key={index}
                                            variant="contained"
                                            color="primary"
                                            onClick={() => handlePromptClick(prompt)}
                                            sx={{ textTransform: 'none' }}
                                        >
                                            {prompt}
                                        </Button>
                                    ))
                                ) : (
                                    starterPrompts.map((prompt, index) => (
                                        <Chip
                                            key={index}
                                            label={prompt}
                                            onClick={() => handlePromptClick(prompt)}
                                            sx={(theme) => ({
                                                cursor: 'pointer',
                                                bgcolor: 'background.paper',
                                                borderColor: 'primary.main',
                                                '& .MuiChip-label': { color: 'text.primary' },
                                                transition: 'background-color 0.2s, transform 0.2s, color 0.2s',
                                                '&:hover': {
                                                    bgcolor: 'primary.main',
                                                    '& .MuiChip-label': { color: theme.palette.primary.dark },
                                                    transform: 'scale(1.06)',
                                                },
                                            })}
                                            variant="outlined"
                                            color="primary"
                                        />
                                    ))
                                )}
                            </Box>
                        </Box>
                    </Container>
                </Box>
            )}

            {/* Planning mode selector (mobile) */}
            <Box
                sx={{
                    display: compact ? 'none' : { xs: 'flex', sm: 'none' },
                    px: 2,
                    py: 1,
                    bgcolor: 'background.paper',
                    borderTop: 1,
                    borderColor: 'divider',
                    justifyContent: 'center',
                }}
            >
                <PlanningModeSelector
                    currentMode={planningMode}
                    disabled={isLoading || isSwitchingMode}
                    loading={isSwitchingMode}
                    onChange={handleSwitchMode}
                    fullWidth
                />
            </Box>

            {/* Input Area */}
            <Paper
                elevation={3}
                sx={{
                    p: compact ? 1 : 2,
                    borderRadius: 0,
                }}
            >
                <Container maxWidth="md">
                    <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-end' }}>
                        <TextField
                            inputRef={inputRef}
                            fullWidth
                            multiline
                            maxRows={4}
                            placeholder={isLoading ? "Waiting for response..." : "Type your message... (Enter to send, Shift+Enter for new line)"}
                            value={inputText}
                            onChange={(e) => setInputText(e.target.value)}
                            onKeyDown={handleKeyDown}
                            variant="outlined"
                            sx={{
                                '& .MuiInputBase-input': {
                                    cursor: isLoading ? 'wait' : 'text',
                                }
                            }}
                        />
                        <IconButton
                            aria-label="Send message"
                            color="primary"
                            onClick={handleSendMessage}
                            disabled={!inputText.trim() || isLoading}
                            sx={{
                                bgcolor: 'primary.main',
                                color: 'white',
                                flexShrink: 0,
                                '&:hover': { bgcolor: 'primary.dark' },
                                '&:disabled': { bgcolor: 'grey.300', color: 'grey.500' },
                            }}
                        >
                            {isLoading ? <CircularProgress size={24} color="inherit" /> : <SendIcon />}
                        </IconButton>
                    </Box>
                </Container>
            </Paper>
        </Box>
    );
}
