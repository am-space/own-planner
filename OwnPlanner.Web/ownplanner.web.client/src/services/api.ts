// API Service for authentication and user management
import type {
  RegisterRequest,
  LoginRequest,
  ForgotPasswordRequest,
  ResetPasswordRequest,
  UserResponse,
  AuthResult,
  AuthCheckResponse,
  AuthStatsResponse,
  CreatePersonalAccessTokenRequest,
  PersonalAccessTokenCreatedResponse,
  PersonalAccessTokenResponse,
  ChatRequest,
  ChatResponse,
  SessionStatusResponse,
  ChatHealthResponse,
  PlanningMode,
  SwitchModeResponse,
  ModeStarterPromptsResponse,
  PagedResult,
  PlannerTaskQuery,
  PlannerGoalQuery,
  PlannerNoteQuery,
  PlannerTaskSummary,
  PlannerTaskDetail,
  PlannerGoalSummary,
  PlannerGoalDetail,
  PlannerNoteSummary,
  PlannerNoteDetail,
  PlannerFilterOptions,
  TelegramConnectionStatus,
  TelegramConnectionLink,
} from '../types/api.types';

/**
 * Thrown when the chat endpoint responds with HTTP 429. Carries the quota reset time so the UI can tell
 * the user when they can retry.
 */
export class RateLimitError extends Error {
  readonly quotaResetAtUtc: string | null;
  readonly limitKind: string | null;

  constructor(message: string, quotaResetAtUtc: string | null, limitKind: string | null) {
    super(message);
    this.name = 'RateLimitError';
    this.quotaResetAtUtc = quotaResetAtUtc;
    this.limitKind = limitKind;
  }
}

export class ApiError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

class ApiService {
  private baseUrl = '/api';

  private async getPlannerJson<T>(
    path: string,
    query?: object,
  ): Promise<T> {
    const searchParams = new URLSearchParams();
    Object.entries(query ?? {}).forEach(([key, value]) => {
      if (value !== undefined && value !== '') {
        searchParams.set(key, String(value));
      }
    });
    const suffix = searchParams.size > 0 ? `?${searchParams.toString()}` : '';
    const response = await fetch(`${this.baseUrl}/planner/${path}${suffix}`, {
      credentials: 'include',
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({})) as { detail?: string; message?: string; title?: string };
      throw new ApiError(
        error.message || error.detail || error.title || 'Failed to load planner data',
        response.status,
      );
    }

    return await response.json() as T;
  }

  async getPlannerTasks(query: PlannerTaskQuery): Promise<PagedResult<PlannerTaskSummary>> {
    return this.getPlannerJson('tasks', query);
  }

  async getPlannerTask(id: string): Promise<PlannerTaskDetail> {
    return this.getPlannerJson(`tasks/${encodeURIComponent(id)}`);
  }

  async getPlannerGoals(query: PlannerGoalQuery): Promise<PagedResult<PlannerGoalSummary>> {
    return this.getPlannerJson('goals', query);
  }

  async getPlannerGoal(id: string): Promise<PlannerGoalDetail> {
    return this.getPlannerJson(`goals/${encodeURIComponent(id)}`);
  }

  async getPlannerNotes(query: PlannerNoteQuery): Promise<PagedResult<PlannerNoteSummary>> {
    return this.getPlannerJson('notes', query);
  }

  async getPlannerNote(id: string): Promise<PlannerNoteDetail> {
    return this.getPlannerJson(`notes/${encodeURIComponent(id)}`);
  }

  async getPlannerFilterOptions(): Promise<PlannerFilterOptions> {
    return this.getPlannerJson('filter-options');
  }

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

  async forgotPassword(email: string): Promise<{ success: boolean; errorMessage?: string }> {
    const response = await fetch(`${this.baseUrl}/auth/forgot-password`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ email } satisfies ForgotPasswordRequest),
      credentials: 'include',
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({}));
      return {
        success: false,
        errorMessage: error.message || 'Failed to request password reset',
      };
    }

    return { success: true };
  }

  async resetPassword(token: string, newPassword: string): Promise<{ success: boolean; errorMessage?: string }> {
    const response = await fetch(`${this.baseUrl}/auth/reset-password`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ token, newPassword } satisfies ResetPasswordRequest),
      credentials: 'include',
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({}));
      return {
        success: false,
        errorMessage: error.message || 'Failed to reset password',
      };
    }

    return { success: true };
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

  async getAuthStats(): Promise<AuthStatsResponse> {
    const response = await fetch(`${this.baseUrl}/auth/stats`, {
      credentials: 'include',
    });

    if (!response.ok) {
      const text = await response.text();
      let message = 'Failed to get statistics';
      try { message = (JSON.parse(text) as { message?: string }).message || message; } catch { if (text) message = text; }
      throw new Error(message);
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

  async getPersonalAccessTokens(): Promise<PersonalAccessTokenResponse[]> {
    const response = await fetch(`${this.baseUrl}/auth/tokens`, {
      credentials: 'include',
    });

    if (!response.ok) {
      const text = await response.text();
      let message = 'Failed to get personal access tokens';
      try { message = (JSON.parse(text) as { message?: string }).message || message; } catch { if (text) message = text; }
      throw new Error(message);
    }

    return await response.json();
  }

  async createPersonalAccessToken(name: string): Promise<PersonalAccessTokenCreatedResponse> {
    const response = await fetch(`${this.baseUrl}/auth/tokens`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ name } satisfies CreatePersonalAccessTokenRequest),
      credentials: 'include',
    });

    if (!response.ok) {
      const text = await response.text();
      let message = 'Failed to create personal access token';
      try { message = (JSON.parse(text) as { message?: string }).message || message; } catch { if (text) message = text; }
      throw new Error(message);
    }

    return await response.json();
  }

  async revokePersonalAccessToken(tokenId: string): Promise<void> {
    const response = await fetch(`${this.baseUrl}/auth/tokens/${tokenId}`, {
      method: 'DELETE',
      credentials: 'include',
    });

    if (!response.ok) {
      const text = await response.text();
      let message = 'Failed to revoke personal access token';
      try { message = (JSON.parse(text) as { message?: string }).message || message; } catch { if (text) message = text; }
      throw new Error(message);
    }
  }

  async getTelegramConnection(): Promise<TelegramConnectionStatus> {
    const response = await fetch(`${this.baseUrl}/telegram/connection`, { credentials: 'include' });
    if (!response.ok) throw new Error('Failed to load Telegram connection status');
    return await response.json();
  }

  async createTelegramConnection(): Promise<TelegramConnectionLink> {
    const response = await fetch(`${this.baseUrl}/telegram/connection`, { method: 'POST', credentials: 'include' });
    if (!response.ok) {
      const error = await response.json().catch(() => ({})) as { message?: string };
      throw new Error(error.message || 'Failed to create Telegram connection link');
    }
    return await response.json();
  }

  async disconnectTelegram(): Promise<void> {
    const response = await fetch(`${this.baseUrl}/telegram/connection`, { method: 'DELETE', credentials: 'include' });
    if (!response.ok) throw new Error('Failed to disconnect Telegram');
  }

  // Account (GDPR) API methods

  /**
   * Permanently deletes the current user's account and all associated data. Requires the user's
   * current password as confirmation. This action is irreversible.
   */
  async deleteAccount(password: string): Promise<void> {
    const response = await fetch(`${this.baseUrl}/account/delete`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ password }),
      credentials: 'include',
    });

    if (!response.ok) {
      const text = await response.text();
      let message = 'Failed to delete account';
      try { message = (JSON.parse(text) as { message?: string }).message || message; } catch { if (text) message = text; }
      throw new Error(message);
    }
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
      const error = await response.json().catch(() => ({}));
      if (response.status === 429) {
        throw new RateLimitError(
          error.message || 'Usage limit reached. Please try again later.',
          error.quotaResetAtUtc ?? null,
          error.limitKind ?? null,
        );
      }
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

  async switchPlanningMode(mode: PlanningMode): Promise<SwitchModeResponse> {
    const response = await fetch(`${this.baseUrl}/chat/mode`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ mode }),
      credentials: 'include',
    });

    if (!response.ok) {
      const text = await response.text();
      let message = 'Failed to switch planning mode';
      try { message = (JSON.parse(text) as { message?: string }).message || message; } catch { if (text) message = text; }
      throw new Error(message);
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

  async getModeStarterPrompts(mode: PlanningMode): Promise<ModeStarterPromptsResponse> {
    const response = await fetch(`${this.baseUrl}/chat/mode/${mode}/prompts`, {
      credentials: 'include',
    });

    if (!response.ok) {
      const text = await response.text();
      let message = 'Failed to fetch mode prompts';
      try { message = (JSON.parse(text) as { message?: string }).message || message; } catch { if (text) message = text; }
      throw new Error(message);
    }

    return await response.json();
  }

  // Account (GDPR) API methods

  /**
   * Downloads a ZIP export of the current user's planning data. Streams the response to a Blob and
   * triggers a browser download, using the filename from the Content-Disposition header when present.
   */
  async exportAccountData(): Promise<void> {
    const response = await fetch(`${this.baseUrl}/account/export`, {
      credentials: 'include',
    });

    if (!response.ok) {
      const text = await response.text();
      let message = 'Failed to export your data';
      try { message = (JSON.parse(text) as { message?: string }).message || message; } catch { if (text) message = text; }
      throw new Error(message);
    }

    const blob = await response.blob();
    const fileName = parseContentDispositionFilename(response.headers.get('Content-Disposition'))
      ?? `ownplanner-export-${new Date().toISOString().slice(0, 10).replace(/-/g, '')}.zip`;

    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    // Defer revoke: revoking synchronously after click() can cancel an in-progress download in
    // some browsers (notably Firefox/Safari) before the blob has been fully read.
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
  }
}

/** Extracts the filename from a Content-Disposition header value, if one is present. */
function parseContentDispositionFilename(header: string | null): string | null {
  if (!header) {
    return null;
  }

  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (utf8Match) {
    return decodeURIComponent(utf8Match[1]);
  }

  const asciiMatch = /filename="?([^";]+)"?/i.exec(header);
  return asciiMatch ? asciiMatch[1] : null;
}

export const apiService = new ApiService();
