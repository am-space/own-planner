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
} from '@mui/material';
import SendIcon from '@mui/icons-material/Send';
import LogoutIcon from '@mui/icons-material/Logout';
import DeleteIcon from '@mui/icons-material/Delete';
import { useAuth } from '../contexts/AuthContext';
import { apiService } from '../services/api';

interface Message {
  id: string;
  text: string;
  sender: 'user' | 'assistant';
  timestamp: Date;
}

export default function ChatPage() {
    const { user, logout } = useAuth();
    const navigate = useNavigate();
    const [messages, setMessages] = useState<Message[]>([]);
    const [inputText, setInputText] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [sessionId, setSessionId] = useState<string | null>(null);
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const inputRef = useRef<HTMLInputElement>(null);

    // Auto-scroll to bottom when messages change
    useEffect(() => {
        messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
    }, [messages]);

    // Load session status on mount
    useEffect(() => {
        loadSessionStatus();
    }, []);

    const loadSessionStatus = async () => {
        try {
            const status = await apiService.getChatSessionStatus();
            setSessionId(status.sessionId);
        } catch (err) {
            console.error('Failed to load session status:', err);
        }
    };

    const handleLogout = async () => {
        await logout();
        navigate('/login');
    };

    const handleClearSession = async () => {
        try {
            await apiService.clearChatSession();
            setMessages([]);
            setError(null);
            loadSessionStatus();
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
            setSessionId(response.sessionId);
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

    return (
        <Box sx={{ display: 'flex', flexDirection: 'column', height: '100vh' }}>
            {/* Header */}
            <AppBar position="static">
                <Toolbar>
                    <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
                        OwnPlanner Chat
                    </Typography>
                    {sessionId && (
                        <Chip
                            label={`Session: ${sessionId.substring(0, 8)}...`}
                            size="small"
                            sx={{ mr: 2, bgcolor: 'rgba(255,255,255,0.1)', color: 'white' }}
                        />
                    )}
                    {user && (
                        <>
                            <Chip
                                avatar={<Avatar>{user.username[0].toUpperCase()}</Avatar>}
                                label={user.username}
                                sx={{ mr: 2, bgcolor: 'rgba(255,255,255,0.2)', color: 'white' }}
                            />
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
                </Toolbar>
            </AppBar>

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
                                Start by typing a message below!
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
                            <Paper
                                elevation={1}
                                sx={{
                                    p: 2,
                                    maxWidth: '70%',
                                    bgcolor: message.sender === 'user' ? 'primary.main' : 'grey.100',
                                    color: message.sender === 'user' ? 'white' : 'text.primary',
                                }}
                            >
                                <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap' }}>
                                    {message.text}
                                </Typography>
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
