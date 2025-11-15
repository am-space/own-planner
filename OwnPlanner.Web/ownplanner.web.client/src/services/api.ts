// API Service for authentication and user management
import type {
  RegisterRequest,
  LoginRequest,
  UserResponse,
  AuthResult,
  AuthCheckResponse,
  ChatRequest,
  ChatResponse,
  SessionStatusResponse,
  ChatHealthResponse,
} from '../types/api.types';

class ApiService {
  private baseUrl = '/api';

  async register(request: RegisterRequest): Promise<AuthResult> {
    const response = await fetch(`${this.baseUrl}/auth/register`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
      credentials: 'include',
    });

    if (!response.ok) {
      const error = await response.json();
      return {
        success: false,
        errorMessage: error.message || 'Registration failed',
      };
    }

    const data = await response.json();
    return {
      success: true,
      user: data.user,
    };
  }

  async login(request: LoginRequest): Promise<AuthResult> {
    const response = await fetch(`${this.baseUrl}/auth/login`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
      credentials: 'include',
    });

    if (!response.ok) {
      const error = await response.json();
      return {
        success: false,
        errorMessage: error.message || 'Login failed',
      };
    }

    const data = await response.json();
    return {
      success: true,
      user: data.user,
    };
  }

  async logout(): Promise<void> {
    await fetch(`${this.baseUrl}/auth/logout`, {
      method: 'POST',
      credentials: 'include',
    });
  }

  async checkAuth(): Promise<AuthCheckResponse> {
    const response = await fetch(`${this.baseUrl}/auth/check`, {
      credentials: 'include',
    });

    if (!response.ok) {
      return { isAuthenticated: false };
    }

    return await response.json();
  }

  async getCurrentUser(): Promise<UserResponse | null> {
    const response = await fetch(`${this.baseUrl}/auth/me`, {
      credentials: 'include',
    });

    if (!response.ok) {
      return null;
    }

    return await response.json();
  }

  // Chat API methods
  async sendChatMessage(message: string): Promise<ChatResponse> {
    const response = await fetch(`${this.baseUrl}/chat/message`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ message } as ChatRequest),
      credentials: 'include',
    });

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || 'Failed to send message');
    }

    return await response.json();
  }

  async clearChatSession(): Promise<{ message: string; sessionId: string }> {
    const response = await fetch(`${this.baseUrl}/chat/clear`, {
      method: 'POST',
      credentials: 'include',
    });

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || 'Failed to clear session');
    }

    return await response.json();
  }

  async getChatSessionStatus(): Promise<SessionStatusResponse> {
    const response = await fetch(`${this.baseUrl}/chat/status`, {
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error('Failed to get session status');
    }

    return await response.json();
  }

  async getChatHealth(): Promise<ChatHealthResponse> {
    const response = await fetch(`${this.baseUrl}/chat/health`, {
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error('Failed to get chat health');
    }

    return await response.json();
  }
}

export const apiService = new ApiService();
