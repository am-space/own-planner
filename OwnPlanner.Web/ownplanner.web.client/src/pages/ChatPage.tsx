import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
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
    Avatar,
    Chip,
    CircularProgress,
    Alert,
    Snackbar,
    useMediaQuery,
    useTheme,
} from '@mui/material';
import SendIcon from '@mui/icons-material/Send';
import LogoutIcon from '@mui/icons-material/Logout';
import DeleteIcon from '@mui/icons-material/Delete';
import InfoIcon from '@mui/icons-material/Info';
import LightbulbOutlinedIcon from '@mui/icons-material/LightbulbOutlined';
import { useAuth } from '../contexts/AuthContext';
import { apiService } from '../services/api';
import AboutDialog from '../components/AboutDialog';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';

interface Message {
    id: string;
    text: string;
    sender: 'user' | 'assistant';
    timestamp: Date;
}

// Suggested prompts for empty chat
const SUGGESTED_PROMPTS = [
    "Help me plan my day",
    "Create a to-do list for a project",
    "Suggest productivity tips",
    "Organize my weekly schedule",
    "Break down a large task",
    "Help me design a 12-Week Year plan"
];

export default function ChatPage() {
    const { user, logout } = useAuth();
    const navigate = useNavigate();
    const theme = useTheme();
    const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
    const [messages, setMessages] = useState<Message[]>([]);
    const [inputText, setInputText] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [aboutOpen, setAboutOpen] = useState(false);
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const inputRef = useRef<HTMLInputElement>(null);

    // Auto-scroll to bottom when messages change
    useEffect(() => {
        messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
    }, [messages]);

    const handleLogout = async () => {
        await logout();
        navigate('/login');
    };

    const handleClearSession = async () => {
        try {
            await apiService.clearChatSession();
            setMessages([]);
            setError(null);
            // Refocus input after clearing
            inputRef.current?.focus();
        } catch (err) {
            setError('Failed to clear session');
            console.error('Error clearing session:', err);
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

        try {
            const response = await apiService.sendChatMessage(messageToSend);

            const assistantMessage: Message = {
                id: (Date.now() + 1).toString(),
                text: response.message,
                sender: 'assistant',
                timestamp: new Date(response.timestamp),
            };

            setMessages((prev) => [...prev, assistantMessage]);
        } catch (err) {
            setError(err instanceof Error ? err.message : 'Failed to send message');
            console.error('Error sending message:', err);

            // Remove the user message if sending failed
            setMessages((prev) => prev.filter((m) => m.id !== userMessage.id));
            // Restore input text
            setInputText(messageToSend);
        } finally {
            setIsLoading(false);
        }
    };

    const handleKeyPress = (e: React.KeyboardEvent) => {
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
        <Box sx={{ display: 'flex', flexDirection: 'column', height: '100vh' }}>
            {/* Header */}
            <AppBar position="static">
                <Toolbar>
                    <Typography variant="h6" component="div" sx={{ mr: 2 }}>
                        OwnPlanner Chat
                    </Typography>

                    {/* About Button - Left Side */}
                    {isMobile ? (
                        <IconButton
                            color="inherit"
                            onClick={() => setAboutOpen(true)}
                            sx={{ mr: 'auto' }}
                        >
                            <InfoIcon />
                        </IconButton>
                    ) : (
                        <Button
                            color="inherit"
                            startIcon={<InfoIcon />}
                            onClick={() => setAboutOpen(true)}
                            sx={{ mr: 'auto' }}
                        >
                            About
                        </Button>
                    )}

                    {user && (
                        <>
                            <Chip
                                avatar={<Avatar>{user.username[0].toUpperCase()}</Avatar>}
                                label={user.username}
                                sx={{
                                    mr: isMobile ? 1 : 2,
                                    bgcolor: 'rgba(255,255,255,0.2)',
                                    color: 'white',
                                    display: isMobile ? 'none' : 'flex'
                                }}
                            />
                            {isMobile ? (
                                <>
                                    <IconButton
                                        color="inherit"
                                        onClick={handleClearSession}
                                        disabled={isLoading}
                                        sx={{ mr: 0.5 }}
                                    >
                                        <DeleteIcon />
                                    </IconButton>
                                    <IconButton
                                        color="inherit"
                                        onClick={handleLogout}
                                    >
                                        <LogoutIcon />
                                    </IconButton>
                                </>
                            ) : (
                                <>
                                    <Button
                                        color="inherit"
                                        startIcon={<DeleteIcon />}
                                        onClick={handleClearSession}
                                        sx={{ mr: 1 }}
                                        disabled={isLoading}
                                    >
                                        Clear
                                    </Button>
                                    <Button
                                        color="inherit"
                                        startIcon={<LogoutIcon />}
                                        onClick={handleLogout}
                                    >
                                        Logout
                                    </Button>
                                </>
                            )}
                        </>
                    )}
                </Toolbar>
            </AppBar>

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
            <Container maxWidth="md" sx={{ flexGrow: 1, overflow: 'auto', py: 3 }}>
                <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                    {messages.length === 0 && (
                        <>
                            <Paper
                                elevation={0}
                                sx={{
                                    p: 3,
                                    textAlign: 'center',
                                    bgcolor: 'grey.50',
                                    border: '1px dashed',
                                    borderColor: 'grey.300',
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

                            {/* Suggested Prompts */}
                            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1, alignItems: 'center' }}>
                                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                                    <LightbulbOutlinedIcon fontSize="small" color="action" />
                                    <Typography variant="body2" color="text.secondary" fontWeight="medium">
                                        Suggestions:
                                    </Typography>
                                </Box>
                                <Box
                                    sx={{
                                        display: 'flex',
                                        flexWrap: 'wrap',
                                        gap: 1,
                                        justifyContent: 'center',
                                    }}
                                >
                                    {SUGGESTED_PROMPTS.map((prompt, index) => (
                                        <Chip
                                            key={index}
                                            label={prompt}
                                            onClick={() => handlePromptClick(prompt)}
                                            sx={(theme) => ({
                                                cursor: 'pointer',
                                                bgcolor: 'background.paper',
                                                borderColor: 'primary.main',
                                                '& .MuiChip-label': {
                                                    color: 'text.primary',
                                                },
                                                transition: 'background-color 0.2s, transform 0.2s, color 0.2s',
                                                '&:hover': {
                                                    bgcolor: 'primary.main',
                                                    '& .MuiChip-label': {
                                                        color: theme.palette.primary.dark,
                                                    },
                                                    transform: 'scale(1.06)',
                                                },
                                            })}
                                            variant="outlined"
                                            color="primary"
                                        />
                                    ))}
                                </Box>
                            </Box>
                        </>
                    )}

                    {messages.map((message) => (
                        <Box
                            key={message.id}
                            sx={{
                                display: 'flex',
                                justifyContent: message.sender === 'user' ? 'flex-end' : 'flex-start',
                            }}
                        >
                            <Paper
                                elevation={1}
                                sx={{
                                    p: 2,
                                    maxWidth: '70%',
                                    bgcolor: message.sender === 'user' ? 'primary.main' : 'grey.100',
                                    color: message.sender === 'user' ? 'white' : 'text.primary',
                                }}
                            >
                                <Box sx={{
                                    '& p': { m: 0 },
                                    '& ul, & ol': { mt: 0.5, mb: 0.5, pl: 2 },
                                    '& li': { mb: 0.25 },
                                    '& code': {
                                        bgcolor: 'rgba(0,0,0,0.1)',
                                        p: 0.5,
                                        borderRadius: 1,
                                        fontFamily: 'monospace',
                                        fontSize: '0.875rem'
                                    },
                                    '& pre': {
                                        bgcolor: 'rgba(0,0,0,0.1)',
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
                                        bgcolor: 'rgba(0,0,0,0.05)',
                                        fontWeight: 'bold'
                                    }
                                }}>
                                    <ReactMarkdown
                                        remarkPlugins={[remarkGfm]}
                                        components={{
                                            table: ({ node, ...props }) => (
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
                        </Box>
                    ))}

                    {isLoading && (
                        <Box sx={{ display: 'flex', justifyContent: 'flex-start' }}>
                            <Paper
                                elevation={1}
                                sx={{
                                    p: 2,
                                    bgcolor: 'grey.100',
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

                    <div ref={messagesEndRef} />
                </Box>
            </Container>

            {/* Input Area */}
            <Paper
                elevation={3}
                sx={{
                    p: 2,
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
                            onKeyPress={handleKeyPress}
                            variant="outlined"
                            sx={{
                                '& .MuiInputBase-input': {
                                    cursor: isLoading ? 'wait' : 'text',
                                }
                            }}
                        />
                        <IconButton
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
