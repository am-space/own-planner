// Type definitions for API models

export interface RegisterRequest {
  email: string;
  username: string;
  password: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface UserResponse {
  id: string;
  email: string;
  username: string;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface AuthResult {
  success: boolean;
  errorMessage?: string;
  user?: UserResponse;
}

export interface AuthCheckResponse {
  isAuthenticated: boolean;
  userId?: string;
  username?: string;
  email?: string;
  sessionId?: string;
}

export interface AuthStatsResponse {
  registeredUserCount: number;
}

// Chat API types
export interface ChatRequest {
  message: string;
}

export interface ChatResponse {
  message: string;
  sessionId: string;
  timestamp: string;
  contextLengthTokens: number | null;
}

export interface SessionStatusResponse {
  sessionId: string;
  isActive: boolean;
  activeSessionsCount: number;
  currentMode: PlanningMode | null;
  contextLengthTokens: number | null;
}

export interface ChatHealthResponse {
  status: string;
  activeSessions: number;
  timestamp: string;
}

export type PlanningMode =
  | 'GlobalPlanning'
  | 'WeekPlanning'
  | 'DayWork'
  | 'Reflection'
  | 'SystemAnalysis';

export interface SwitchModeResponse {
  mode: PlanningMode;
  sessionId: string;
}

export interface ModeStarterPromptsResponse {
  mode: PlanningMode;
  starterPrompts: string[];
}
