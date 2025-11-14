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
}
