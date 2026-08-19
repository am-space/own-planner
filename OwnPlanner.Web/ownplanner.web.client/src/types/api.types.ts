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

export interface ForgotPasswordRequest {
  email: string;
}

export interface ResetPasswordRequest {
  token: string;
  newPassword: string;
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

export interface CreatePersonalAccessTokenRequest {
  name: string;
}

export interface PersonalAccessTokenResponse {
  id: string;
  userId: string;
  name: string;
  createdAt: string;
  lastUsedAt: string | null;
  revokedAt: string | null;
}

export interface PersonalAccessTokenCreatedResponse {
  token: PersonalAccessTokenResponse;
  plaintextToken: string;
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
  maxContextLengthTokens: number;
  remainingDailyQuota: number | null;
  quotaResetAtUtc: string | null;
}

export interface SessionStatusResponse {
  sessionId: string;
  isActive: boolean;
  activeSessionsCount: number;
  currentMode: PlanningMode | null;
  contextLengthTokens: number | null;
  maxContextLengthTokens: number;
  dailyQuotaLimit: number | null;
  dailyQuotaUsed: number | null;
  remainingDailyQuota: number | null;
  quotaResetAtUtc: string | null;
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

export interface TelegramConnectionStatus {
  enabled: boolean;
  connected: boolean;
  pending: boolean;
  telegramUserId: number | null;
  connectedAtUtc: string | null;
  mode: PlanningMode | null;
}

export interface TelegramConnectionLink {
  url: string;
  expiresAtUtc: string;
}

// Read-only planner workspace types
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  offset: number;
  limit: number;
  hasMore: boolean;
}

export type PlannerTaskStatus = 'Open' | 'Completed' | 'All';
export type PlannerGoalStatus = 'Active' | 'Achieved' | 'Dropped' | 'All';
export type GoalHorizon = 'Monthly' | 'Quarterly' | 'Yearly' | 'TargetDate';
export type GoalStatus = 'Active' | 'Achieved' | 'Dropped';
export type ContextStatus = 'Active' | 'Paused' | 'Completed' | 'Archived';

export interface PlannerTaskQuery {
  search?: string;
  status?: PlannerTaskStatus;
  important?: boolean;
  taskListId?: string;
  contextId?: string;
  goalId?: string;
  offset?: number;
  limit?: number;
}

export interface PlannerGoalQuery {
  search?: string;
  status?: PlannerGoalStatus;
  horizon?: GoalHorizon;
  offset?: number;
  limit?: number;
}

export interface PlannerNoteQuery {
  search?: string;
  pinned?: boolean;
  noteListId?: string;
  contextId?: string;
  goalId?: string;
  offset?: number;
  limit?: number;
}

export interface PlannerTaskSummary {
  id: string;
  title: string;
  descriptionPreview: string | null;
  isCompleted: boolean;
  isImportant: boolean;
  dueAt: string | null;
  focusAt: string | null;
  updatedAt: string;
  taskListId: string;
  taskListName: string;
  contextId: string | null;
  contextName: string | null;
  goalId: string | null;
  goalName: string | null;
}

export interface PlannerTaskDetail extends Omit<PlannerTaskSummary, 'descriptionPreview'> {
  description: string | null;
  createdAt: string;
  completedAt: string | null;
}

export interface PlannerGoalSummary {
  id: string;
  title: string;
  descriptionPreview: string | null;
  horizon: GoalHorizon;
  targetPeriod: string | null;
  targetDate: string | null;
  status: GoalStatus;
  metric: string | null;
  metricCurrent: string | null;
  updatedAt: string;
}

export interface PlannerGoalDetail extends Omit<PlannerGoalSummary, 'descriptionPreview'> {
  description: string | null;
  createdAt: string;
}

export interface PlannerNoteSummary {
  id: string;
  title: string;
  contentPreview: string | null;
  isPinned: boolean;
  updatedAt: string;
  noteListId: string;
  noteListName: string;
  contextId: string | null;
  contextName: string | null;
  goalId: string | null;
  goalName: string | null;
}

export interface PlannerNoteDetail extends Omit<PlannerNoteSummary, 'contentPreview'> {
  content: string | null;
  createdAt: string;
}

export interface PlannerListOption {
  id: string;
  name: string;
  color: string | null;
  isArchived: boolean;
}

export interface PlannerContextOption {
  id: string;
  name: string;
  color: string | null;
  status: ContextStatus;
}

export interface PlannerGoalOption {
  id: string;
  name: string;
  status: GoalStatus;
}

export interface PlannerFilterOptions {
  taskLists: PlannerListOption[];
  noteLists: PlannerListOption[];
  contexts: PlannerContextOption[];
  goals: PlannerGoalOption[];
}
