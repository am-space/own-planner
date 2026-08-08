import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useOutletContext, useSearchParams } from 'react-router-dom';
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  CircularProgress,
  Divider,
  Drawer,
  FormControlLabel,
  IconButton,
  List,
  ListItemButton,
  ListItemText,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import RefreshIcon from '@mui/icons-material/Refresh';
import StarIcon from '@mui/icons-material/Star';
import PushPinIcon from '@mui/icons-material/PushPin';
import { ApiError, apiService } from '../services/api';
import type {
  GoalHorizon,
  PagedResult,
  PlannerFilterOptions,
  PlannerGoalDetail,
  PlannerGoalStatus,
  PlannerGoalSummary,
  PlannerNoteDetail,
  PlannerNoteSummary,
  PlannerTaskDetail,
  PlannerTaskStatus,
  PlannerTaskSummary,
} from '../types/api.types';
import type { PlannerShellOutletContext } from '../components/PlannerShell';

type PlannerSection = 'tasks' | 'goals' | 'notes';

interface PlannerPageProps {
  section: PlannerSection;
}

const pageSize = 25;
const inspectorWidth = 380;

export default function PlannerPage({ section }: PlannerPageProps) {
  const [filterOptions, setFilterOptions] = useState<PlannerFilterOptions | null>(null);
  const [filterOptionsError, setFilterOptionsError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    apiService.getPlannerFilterOptions()
      .then(options => {
        if (active) setFilterOptions(options);
      })
      .catch(error => {
        if (active) setFilterOptionsError(getErrorMessage(error));
      });
    return () => { active = false; };
  }, []);

  return (
    <Box sx={{ display: 'flex', flex: 1, minWidth: 0, minHeight: 0 }}>
      {section === 'tasks' && <TaskBrowser filterOptions={filterOptions} filterOptionsError={filterOptionsError} />}
      {section === 'goals' && <GoalBrowser />}
      {section === 'notes' && <NoteBrowser filterOptions={filterOptions} filterOptionsError={filterOptionsError} />}
    </Box>
  );
}

function TaskBrowser({
  filterOptions,
  filterOptionsError,
}: {
  filterOptions: PlannerFilterOptions | null;
  filterOptionsError: string | null;
}) {
  const [searchParams, setSearchParams] = useSearchParams();
  const search = searchParams.get('search') ?? '';
  const status = (searchParams.get('status') ?? 'Open') as PlannerTaskStatus;
  const important = searchParams.get('important') === 'true';
  const taskListId = searchParams.get('taskListId') ?? '';
  const contextId = searchParams.get('contextId') ?? '';
  const goalId = searchParams.get('goalId') ?? '';
  const selected = searchParams.get('selected');
  const offset = readOffset(searchParams);
  const [result, setResult] = useState<PagedResult<PlannerTaskSummary> | null>(null);
  const [detail, setDetail] = useState<PlannerTaskDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    let active = true;
    queueMicrotask(() => {
      if (active) {
        setLoading(true);
        setError(null);
      }
    });
    apiService.getPlannerTasks({
      search: search || undefined,
      status,
      important: important || undefined,
      taskListId: taskListId || undefined,
      contextId: contextId || undefined,
      goalId: goalId || undefined,
      offset,
      limit: pageSize,
    }).then(page => {
      if (active) setResult(page);
    }).catch(requestError => {
      if (active) setError(getErrorMessage(requestError));
    }).finally(() => {
      if (active) setLoading(false);
    });
    return () => { active = false; };
  }, [search, status, important, taskListId, contextId, goalId, offset, reloadToken]);

  useEffect(() => {
    if (!selected) {
      queueMicrotask(() => {
        setDetail(null);
        setDetailError(null);
      });
      return;
    }
    let active = true;
    queueMicrotask(() => {
      if (active) {
        setDetailLoading(true);
        setDetailError(null);
      }
    });
    apiService.getPlannerTask(selected)
      .then(item => { if (active) setDetail(item); })
      .catch(requestError => { if (active) setDetailError(getErrorMessage(requestError)); })
      .finally(() => { if (active) setDetailLoading(false); });
    return () => { active = false; };
  }, [selected]);

  const updateParams = (updates: Record<string, string | null>, resetPage = true) => {
    const next = new URLSearchParams(searchParams);
    Object.entries(updates).forEach(([key, value]) => value ? next.set(key, value) : next.delete(key));
    if (resetPage) next.delete('offset');
    if (resetPage && !Object.hasOwn(updates, 'selected')) next.delete('selected');
    setSearchParams(next);
  };
  const hasFilters = Boolean(search || status !== 'Open' || important || taskListId || contextId || goalId);

  return (
    <CollectionWithInspector
      title="Tasks"
      subtitle="Open work across all task lists"
      loading={loading}
      error={error}
      result={result}
      hasFilters={hasFilters}
      emptyLabel="No tasks yet. Ask OwnPlanner to create one from the chat below."
      filteredEmptyLabel="No tasks match these filters."
      onRetry={() => setReloadToken(value => value + 1)}
      selected={selected}
      onSelect={id => updateParams({ selected: id }, false)}
      onCloseDetail={() => updateParams({ selected: null }, false)}
      onPrevious={() => updateParams({ offset: String(Math.max(0, offset - pageSize)) }, false)}
      onNext={() => updateParams({ offset: String(offset + pageSize) }, false)}
      filters={
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} useFlexGap flexWrap="wrap">
          <TextField
            label="Search tasks"
            value={search}
            onChange={event => updateParams({ search: event.target.value })}
            size="small"
            sx={{ minWidth: 220, flex: 1 }}
          />
          <TextField select label="Status" value={status} onChange={event => updateParams({ status: event.target.value })} size="small" sx={{ minWidth: 130 }}>
            {['Open', 'Completed', 'All'].map(value => <MenuItem key={value} value={value}>{value}</MenuItem>)}
          </TextField>
          <OptionSelect label="Task list" value={taskListId} onChange={value => updateParams({ taskListId: value })} options={filterOptions?.taskLists ?? []} />
          <OptionSelect label="Context" value={contextId} onChange={value => updateParams({ contextId: value })} options={filterOptions?.contexts ?? []} />
          <OptionSelect label="Goal" value={goalId} onChange={value => updateParams({ goalId: value })} options={filterOptions?.goals ?? []} />
          <FormControlLabel
            control={<Checkbox checked={important} onChange={event => updateParams({ important: event.target.checked ? 'true' : null })} />}
            label="Important only"
          />
        </Stack>
      }
      filterOptionsError={filterOptionsError}
      renderRow={item => (
        <>
          <ListItemText
            primary={<Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>{item.title}{item.isImportant && <StarIcon color="warning" fontSize="small" />}</Box>}
            secondary={joinMetadata(item.contextName, item.taskListName)}
            secondaryTypographyProps={{ noWrap: true }}
          />
          <Stack direction="row" spacing={0.75} sx={{ ml: 2, alignItems: 'center' }}>
            {item.isCompleted && <Chip size="small" label="Completed" color="success" />}
          </Stack>
        </>
      )}
      detailTitle={detail?.title ?? 'Task details'}
      detailLoading={detailLoading}
      detailError={detailError}
      detail={detail && (
        <DetailStack>
          <DetailText label="Description" value={detail.description} multiline />
          <DetailText label="Status" value={detail.isCompleted ? 'Completed' : 'Open'} />
          <DetailText label="Important" value={detail.isImportant ? 'Yes' : 'No'} />
          <DetailText label="Task list" value={detail.taskListName} />
          <DetailText label="Context" value={detail.contextName} />
          <DetailText label="Goal" value={detail.goalName} />
          <DetailText label="Focus date" value={formatDate(detail.focusAt)} />
          <DetailText label="Due date" value={formatDate(detail.dueAt)} />
          <DetailText label="Created" value={formatDate(detail.createdAt, true)} />
          <DetailText label="Updated" value={formatDate(detail.updatedAt, true)} />
        </DetailStack>
      )}
    />
  );
}

function GoalBrowser() {
  const [searchParams, setSearchParams] = useSearchParams();
  const search = searchParams.get('search') ?? '';
  const status = (searchParams.get('status') ?? 'Active') as PlannerGoalStatus;
  const horizon = (searchParams.get('horizon') ?? '') as GoalHorizon | '';
  const selected = searchParams.get('selected');
  const offset = readOffset(searchParams);
  const [result, setResult] = useState<PagedResult<PlannerGoalSummary> | null>(null);
  const [detail, setDetail] = useState<PlannerGoalDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    let active = true;
    queueMicrotask(() => {
      if (active) {
        setLoading(true);
        setError(null);
      }
    });
    apiService.getPlannerGoals({ search: search || undefined, status, horizon: horizon || undefined, offset, limit: pageSize })
      .then(page => { if (active) setResult(page); })
      .catch(requestError => { if (active) setError(getErrorMessage(requestError)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [search, status, horizon, offset, reloadToken]);

  useEffect(() => {
    if (!selected) {
      queueMicrotask(() => {
        setDetail(null);
        setDetailError(null);
      });
      return;
    }
    let active = true;
    queueMicrotask(() => {
      if (active) {
        setDetailLoading(true);
        setDetailError(null);
      }
    });
    apiService.getPlannerGoal(selected)
      .then(item => { if (active) setDetail(item); })
      .catch(requestError => { if (active) setDetailError(getErrorMessage(requestError)); })
      .finally(() => { if (active) setDetailLoading(false); });
    return () => { active = false; };
  }, [selected]);

  const updateParams = (updates: Record<string, string | null>, resetPage = true) => {
    const next = new URLSearchParams(searchParams);
    Object.entries(updates).forEach(([key, value]) => value ? next.set(key, value) : next.delete(key));
    if (resetPage) next.delete('offset');
    if (resetPage && !Object.hasOwn(updates, 'selected')) next.delete('selected');
    setSearchParams(next);
  };
  const hasFilters = Boolean(search || status !== 'Active' || horizon);

  return (
    <CollectionWithInspector
      title="Goals"
      subtitle="Outcomes across every planning horizon"
      loading={loading}
      error={error}
      result={result}
      hasFilters={hasFilters}
      emptyLabel="No goals yet. Ask OwnPlanner to define one from the chat below."
      filteredEmptyLabel="No goals match these filters."
      onRetry={() => setReloadToken(value => value + 1)}
      selected={selected}
      onSelect={id => updateParams({ selected: id }, false)}
      onCloseDetail={() => updateParams({ selected: null }, false)}
      onPrevious={() => updateParams({ offset: String(Math.max(0, offset - pageSize)) }, false)}
      onNext={() => updateParams({ offset: String(offset + pageSize) }, false)}
      filters={
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} useFlexGap flexWrap="wrap">
          <TextField label="Search goals" value={search} onChange={event => updateParams({ search: event.target.value })} size="small" sx={{ minWidth: 220, flex: 1 }} />
          <TextField select label="Status" value={status} onChange={event => updateParams({ status: event.target.value })} size="small" sx={{ minWidth: 130 }}>
            {['Active', 'Achieved', 'Dropped', 'All'].map(value => <MenuItem key={value} value={value}>{value}</MenuItem>)}
          </TextField>
          <TextField select label="Horizon" value={horizon} onChange={event => updateParams({ horizon: event.target.value })} size="small" sx={{ minWidth: 150 }}>
            <MenuItem value="">All horizons</MenuItem>
            {['Monthly', 'Quarterly', 'Yearly', 'TargetDate'].map(value => <MenuItem key={value} value={value}>{splitLabel(value)}</MenuItem>)}
          </TextField>
        </Stack>
      }
      renderRow={item => (
        <>
          <ListItemText
            primary={item.title}
            secondary={joinMetadata(splitLabel(item.horizon), formatGoalTarget(item))}
            secondaryTypographyProps={{ noWrap: true }}
          />
          <Stack direction="row" spacing={0.75} sx={{ ml: 2 }}>
            <Chip size="small" label={item.status} color={item.status === 'Active' ? 'primary' : item.status === 'Achieved' ? 'success' : 'default'} />
          </Stack>
        </>
      )}
      detailTitle={detail?.title ?? 'Goal details'}
      detailLoading={detailLoading}
      detailError={detailError}
      detail={detail && (
        <DetailStack>
          <DetailText label="Description" value={detail.description} multiline />
          <DetailText label="Status" value={detail.status} />
          <DetailText label="Horizon" value={splitLabel(detail.horizon)} />
          <DetailText label="Target period" value={detail.targetPeriod} />
          <DetailText label="Target date" value={formatDate(detail.targetDate)} />
          <DetailText label="Metric" value={detail.metric} />
          <DetailText label="Current value" value={detail.metricCurrent} />
          <DetailText label="Created" value={formatDate(detail.createdAt, true)} />
          <DetailText label="Updated" value={formatDate(detail.updatedAt, true)} />
        </DetailStack>
      )}
    />
  );
}

function NoteBrowser({
  filterOptions,
  filterOptionsError,
}: {
  filterOptions: PlannerFilterOptions | null;
  filterOptionsError: string | null;
}) {
  const [searchParams, setSearchParams] = useSearchParams();
  const search = searchParams.get('search') ?? '';
  const pinned = searchParams.get('pinned') === 'true';
  const noteListId = searchParams.get('noteListId') ?? '';
  const contextId = searchParams.get('contextId') ?? '';
  const goalId = searchParams.get('goalId') ?? '';
  const selected = searchParams.get('selected');
  const offset = readOffset(searchParams);
  const [result, setResult] = useState<PagedResult<PlannerNoteSummary> | null>(null);
  const [detail, setDetail] = useState<PlannerNoteDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    let active = true;
    queueMicrotask(() => {
      if (active) {
        setLoading(true);
        setError(null);
      }
    });
    apiService.getPlannerNotes({
      search: search || undefined,
      pinned: pinned || undefined,
      noteListId: noteListId || undefined,
      contextId: contextId || undefined,
      goalId: goalId || undefined,
      offset,
      limit: pageSize,
    }).then(page => {
      if (active) setResult(page);
    }).catch(requestError => {
      if (active) setError(getErrorMessage(requestError));
    }).finally(() => {
      if (active) setLoading(false);
    });
    return () => { active = false; };
  }, [search, pinned, noteListId, contextId, goalId, offset, reloadToken]);

  useEffect(() => {
    if (!selected) {
      queueMicrotask(() => {
        setDetail(null);
        setDetailError(null);
      });
      return;
    }
    let active = true;
    queueMicrotask(() => {
      if (active) {
        setDetailLoading(true);
        setDetailError(null);
      }
    });
    apiService.getPlannerNote(selected)
      .then(item => { if (active) setDetail(item); })
      .catch(requestError => { if (active) setDetailError(getErrorMessage(requestError)); })
      .finally(() => { if (active) setDetailLoading(false); });
    return () => { active = false; };
  }, [selected]);

  const updateParams = (updates: Record<string, string | null>, resetPage = true) => {
    const next = new URLSearchParams(searchParams);
    Object.entries(updates).forEach(([key, value]) => value ? next.set(key, value) : next.delete(key));
    if (resetPage) next.delete('offset');
    if (resetPage && !Object.hasOwn(updates, 'selected')) next.delete('selected');
    setSearchParams(next);
  };
  const hasFilters = Boolean(search || pinned || noteListId || contextId || goalId);

  return (
    <CollectionWithInspector
      title="Notes"
      subtitle="Reference material and captured ideas"
      loading={loading}
      error={error}
      result={result}
      hasFilters={hasFilters}
      emptyLabel="No notes yet. Ask OwnPlanner to capture one from the chat below."
      filteredEmptyLabel="No notes match these filters."
      onRetry={() => setReloadToken(value => value + 1)}
      selected={selected}
      onSelect={id => updateParams({ selected: id }, false)}
      onCloseDetail={() => updateParams({ selected: null }, false)}
      onPrevious={() => updateParams({ offset: String(Math.max(0, offset - pageSize)) }, false)}
      onNext={() => updateParams({ offset: String(offset + pageSize) }, false)}
      filters={
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} useFlexGap flexWrap="wrap">
          <TextField label="Search notes" value={search} onChange={event => updateParams({ search: event.target.value })} size="small" sx={{ minWidth: 220, flex: 1 }} />
          <OptionSelect label="Note list" value={noteListId} onChange={value => updateParams({ noteListId: value })} options={filterOptions?.noteLists ?? []} />
          <OptionSelect label="Context" value={contextId} onChange={value => updateParams({ contextId: value })} options={filterOptions?.contexts ?? []} />
          <OptionSelect label="Goal" value={goalId} onChange={value => updateParams({ goalId: value })} options={filterOptions?.goals ?? []} />
          <FormControlLabel control={<Checkbox checked={pinned} onChange={event => updateParams({ pinned: event.target.checked ? 'true' : null })} />} label="Pinned only" />
        </Stack>
      }
      filterOptionsError={filterOptionsError}
      renderRow={item => (
        <>
          <ListItemText
            primary={<Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>{item.title}{item.isPinned && <PushPinIcon color="primary" fontSize="small" />}</Box>}
            secondary={joinMetadata(item.contextName, item.noteListName)}
            secondaryTypographyProps={{ noWrap: true }}
          />
        </>
      )}
      detailTitle={detail?.title ?? 'Note details'}
      detailLoading={detailLoading}
      detailError={detailError}
      detail={detail && (
        <DetailStack>
          <DetailText label="Content" value={detail.content} multiline />
          <DetailText label="Pinned" value={detail.isPinned ? 'Yes' : 'No'} />
          <DetailText label="Note list" value={detail.noteListName} />
          <DetailText label="Context" value={detail.contextName} />
          <DetailText label="Goal" value={detail.goalName} />
          <DetailText label="Created" value={formatDate(detail.createdAt, true)} />
          <DetailText label="Updated" value={formatDate(detail.updatedAt, true)} />
        </DetailStack>
      )}
    />
  );
}

interface CollectionWithInspectorProps<T extends { id: string }> {
  title: string;
  subtitle: string;
  loading: boolean;
  error: string | null;
  result: PagedResult<T> | null;
  hasFilters: boolean;
  emptyLabel: string;
  filteredEmptyLabel: string;
  onRetry: () => void;
  selected: string | null;
  onSelect: (id: string) => void;
  onCloseDetail: () => void;
  onPrevious: () => void;
  onNext: () => void;
  filters: React.ReactNode;
  filterOptionsError?: string | null;
  renderRow: (item: T) => React.ReactNode;
  detailTitle: string;
  detailLoading: boolean;
  detailError: string | null;
  detail: React.ReactNode;
}

function CollectionWithInspector<T extends { id: string }>({
  title,
  subtitle,
  loading,
  error,
  result,
  hasFilters,
  emptyLabel,
  filteredEmptyLabel,
  onRetry,
  selected,
  onSelect,
  onCloseDetail,
  onPrevious,
  onNext,
  filters,
  filterOptionsError,
  renderRow,
  detailTitle,
  detailLoading,
  detailError,
  detail,
}: CollectionWithInspectorProps<T>) {
  const theme = useTheme();
  const useOverlayInspector = useMediaQuery(theme.breakpoints.down('lg'));
  const { inspectorHost } = useOutletContext<PlannerShellOutletContext>();
  const headingRef = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    if (selected && (useOverlayInspector || inspectorHost)) headingRef.current?.focus();
  }, [selected, useOverlayInspector, inspectorHost]);

  const handleClose = () => {
    const originatingRow = selected ? document.getElementById(`planner-row-${selected}`) : null;
    onCloseDetail();
    window.setTimeout(() => originatingRow?.focus(), 0);
  };
  const firstIndex = result && result.totalCount > 0 ? result.offset + 1 : 0;
  const lastIndex = result ? result.offset + result.items.length : 0;
  const inspector = (
    <Box
      component="aside"
      aria-label={`${title} details inspector`}
      sx={{ display: 'flex', flexDirection: 'column', width: useOverlayInspector ? '100%' : inspectorWidth, maxWidth: '100%', height: '100%', overflow: 'hidden', bgcolor: 'background.paper', borderLeft: useOverlayInspector ? 0 : 1, borderColor: 'divider' }}
    >
      <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, p: 2 }}>
        <Typography ref={headingRef} tabIndex={-1} variant="h6" component="h2" sx={{ flex: 1, outline: 'none' }}>
          {detailTitle}
        </Typography>
        <IconButton aria-label="Close details" onClick={handleClose}><CloseIcon /></IconButton>
      </Box>
      <Divider />
      <Box sx={{ flex: 1, overflow: 'auto', p: 2.5 }} aria-busy={detailLoading}>
        {detailLoading ? <CircularProgress size={28} /> : detailError ? <Alert severity="error">{detailError}</Alert> : detail}
      </Box>
    </Box>
  );

  return (
    <>
      <Box sx={{ flex: 1, minWidth: 0, minHeight: 0, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
        <Box sx={{ px: { xs: 2, md: 3 }, pt: 2.5, pb: 2 }}>
          <Typography variant="h5" component="h1" sx={{ fontWeight: 650 }}>{title}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>{subtitle}</Typography>
          {filters}
          {filterOptionsError && <Alert severity="warning" sx={{ mt: 1.5 }}>Some relationship filters could not be loaded: {filterOptionsError}</Alert>}
        </Box>
        <Divider />

        <Box sx={{ flex: 1, minHeight: 0, overflow: 'auto', px: { xs: 1, md: 2 }, py: 1.5 }} aria-busy={loading}>
          {loading && !result ? (
            <Box sx={{ display: 'grid', placeItems: 'center', minHeight: 180 }}>
              <CircularProgress aria-label={`Loading ${title.toLowerCase()}`} />
            </Box>
          ) : error ? (
            <Alert severity={error.includes('session') ? 'warning' : 'error'} action={<Button color="inherit" size="small" startIcon={<RefreshIcon />} onClick={onRetry}>Retry</Button>}>
              {error}
            </Alert>
          ) : result?.items.length === 0 ? (
            <Paper variant="outlined" sx={{ p: 4, textAlign: 'center', borderStyle: 'dashed' }}>
              <Typography color="text.secondary">{hasFilters ? filteredEmptyLabel : emptyLabel}</Typography>
            </Paper>
          ) : (
            <List disablePadding aria-label={`${title} results`}>
              {result?.items.map(item => (
                <ListItemButton
                  key={item.id}
                  id={`planner-row-${item.id}`}
                  selected={selected === item.id}
                  onClick={() => onSelect(item.id)}
                  sx={{ mb: 0.75, border: 1, borderColor: selected === item.id ? 'primary.main' : 'divider', borderRadius: 2, bgcolor: 'background.paper' }}
                >
                  {renderRow(item)}
                </ListItemButton>
              ))}
            </List>
          )}
        </Box>

        <Divider />
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, px: { xs: 2, md: 3 }, py: 1 }}>
          <Typography variant="body2" color="text.secondary" sx={{ flex: 1 }} aria-live="polite">
            {result ? `${firstIndex}–${lastIndex} of ${result.totalCount}` : '—'}
          </Typography>
          <Button size="small" onClick={onPrevious} disabled={!result || result.offset === 0 || loading}>Previous</Button>
          <Button size="small" onClick={onNext} disabled={!result?.hasMore || loading}>Next</Button>
        </Box>
      </Box>

      {selected && useOverlayInspector && (
        <Drawer
          anchor="right"
          open
          onClose={handleClose}
          PaperProps={{ sx: { width: { xs: '100%', sm: inspectorWidth }, maxWidth: '100%' } }}
        >
          {inspector}
        </Drawer>
      )}
      {selected && !useOverlayInspector && inspectorHost && createPortal(inspector, inspectorHost)}
    </>
  );
}

function OptionSelect({
  label,
  value,
  onChange,
  options,
}: {
  label: string;
  value: string;
  onChange: (value: string | null) => void;
  options: Array<{ id: string; name: string; isArchived?: boolean; status?: string }>;
}) {
  return (
    <TextField select label={label} value={value} onChange={event => onChange(event.target.value || null)} size="small" sx={{ minWidth: 150 }}>
      <MenuItem value="">All</MenuItem>
      {options.map(option => (
        <MenuItem key={option.id} value={option.id}>
          {option.name}{formatOptionState(option)}
        </MenuItem>
      ))}
    </TextField>
  );
}

function formatOptionState(option: { isArchived?: boolean; status?: string }) {
  if (option.isArchived) return ' (archived)';
  return option.status && option.status !== 'Active' ? ` (${option.status.toLowerCase()})` : '';
}

function DetailStack({ children }: { children: React.ReactNode }) {
  return <Stack spacing={2.25}>{children}</Stack>;
}

function DetailText({ label, value, multiline = false }: { label: string; value: string | null | undefined; multiline?: boolean }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 0.35 }}>{label}</Typography>
      <Typography variant="body2" sx={{ whiteSpace: multiline ? 'pre-wrap' : 'normal', overflowWrap: 'anywhere' }}>{value || '—'}</Typography>
    </Box>
  );
}

function readOffset(params: URLSearchParams) {
  const raw = params.get('offset');
  if (!raw) return 0;
  const parsed = Number(raw);
  return Number.isInteger(parsed) ? parsed : -1;
}

function getErrorMessage(error: unknown) {
  if (error instanceof ApiError && error.status === 401) {
    return 'Your session has expired. Sign in again to view planner data.';
  }
  return error instanceof Error ? error.message : 'Failed to load planner data.';
}

function formatDate(value: string | null, includeTime = false) {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return includeTime ? date.toLocaleString() : date.toLocaleDateString();
}

function splitLabel(value: string) {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2');
}

function joinMetadata(...values: Array<string | null | undefined>) {
  return values.filter((value): value is string => Boolean(value)).join(' · ') || undefined;
}

function formatGoalTarget(goal: PlannerGoalSummary) {
  return goal.targetDate ? formatDate(goal.targetDate) : goal.targetPeriod;
}
