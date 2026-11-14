import { useEffect, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Box, Button, Link, Paper, Stack, TextField, Typography } from '@mui/material';
import { apiService } from '../services/api';
import type { WeeklyReviewView } from '../types/api.types';

export default function WeeklyReviewPage() {
  const [review, setReview] = useState<WeeklyReviewView | null>(null);
  const [needsSetup, setNeedsSetup] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadAttempt, setLoadAttempt] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [deferTime, setDeferTime] = useState('');

  useEffect(() => {
    let active = true;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const preferences = await apiService.getWeeklyReviewPreferences();
        if (!active) return;
        setNeedsSetup(!preferences.timeZoneId);
        if (preferences.timeZoneId) {
          const value = await apiService.openWeeklyReview();
          if (active) setReview(value);
        }
      } catch (err) {
        if (active) setError(err instanceof Error ? err.message : 'Could not open weekly review.');
      } finally {
        if (active) setLoading(false);
      }
    };
    void load();
    return () => { active = false; };
  }, [loadAttempt]);

  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try { await action(); }
    catch (err) { setError(err instanceof Error ? err.message : 'Weekly review request failed.'); }
    finally { setBusy(false); }
  };

  const goals = review?.report.goals;
  const share = goals?.workShare;
  const completedGoal = share?.completedGoal.workItemCount ?? goals?.completedGoalWorkCount ?? 0;
  const completedOther = share?.completedOther.workItemCount ?? goals?.completedOtherWorkCount ?? 0;
  const plannedGoal = share?.plannedGoal.workItemCount ?? goals?.plannedGoalWorkCount ?? 0;
  const plannedOther = share?.plannedOther.workItemCount ?? goals?.plannedOtherWorkCount ?? 0;

  return <Box sx={{ width: '100%', minHeight: 0, overflowY: 'auto' }}>
    <Box sx={{ p: { xs: 2, md: 3 }, maxWidth: 960, mx: 'auto' }}>
      <Stack spacing={2}>
        <Box>
          <Typography variant="h4" component="h1">Weekly Review</Typography>
          <Typography color="text.secondary">Review unfinished work and commitments for the target week.</Typography>
          <Link component={RouterLink} to="/settings#weekly-review" variant="body2">Review settings</Link>
        </Box>
        {error && <Alert severity="error" action={!review && !loading ? <Button color="inherit" onClick={() => setLoadAttempt(value => value + 1)}>Retry</Button> : undefined}>{error}</Alert>}
        {loading ? <Typography role="status">Loading weekly review…</Typography> : needsSetup ? <Alert severity="info">
          Choose your review timezone in <Link component={RouterLink} to="/settings#weekly-review">Weekly Review settings</Link> to get started.
          {' '}Manual review is available with reminders disabled and without a Telegram connection.
        </Alert> : review && <Paper sx={{ p: { xs: 2, sm: 3 } }}>
          <Stack spacing={2} aria-busy={busy}>
            <Typography role="status">Week of {review.review.targetWeek} · {review.review.timeZoneId} · {review.review.status}</Typography>
            {review.activeGoalWarning && <Alert severity="warning">{review.activeGoalWarning}</Alert>}
            {review.pausedGoalsMention && <Typography>{review.pausedGoalsMention}</Typography>}
            <Typography variant="h6">Goals first</Typography>
            {review.suggestCreatingGoals && <Typography color="text.secondary">No active goals yet. In chat, you can formulate 1–3 goals for this season.</Typography>}
            {review.report.goals.items.map(goal => <Box key={goal.id}>
              <Typography sx={{ fontWeight: 'bold' }}>{goal.title}</Typography>
              <Typography>Completed work items in the last 7 days: {goal.work?.completionActivity.completedCount ?? goal.completedLast7Count} · {goal.openTaskCount} open tasks/series · Planned work items for this week: {goal.plannedWork?.workItemCount ?? goal.plannedTaskCount}</Typography>
              {goal.metric && <Typography color="text.secondary">{goal.metric}: {goal.metricCurrent || 'No progress value yet'}</Typography>}
              {(goal.noNextStep || goal.stalled || goal.targetPeriodFlag) && <Typography color="warning.main">
                {[goal.noNextStep && 'No next step', goal.stalled && 'Stalled', goal.targetPeriodFlag === 'targetPeriodPassed' && 'Target period passed', goal.targetPeriodFlag === 'targetPeriodEndingSoon' && 'Target period ending soon'].filter(Boolean).join(' · ')}
              </Typography>}
              {goal.work && goal.work.completionActivity.samples.length > 0 && <Typography color="text.secondary">Completed sources: {[...new Map(goal.work.completionActivity.samples.map(task => [task.taskId, task.title])).values()].join(', ')}{goal.work.completionActivity.truncated ? '…' : ''}</Typography>}
              {!goal.work && goal.completedLast7.length > 0 && <Typography color="text.secondary">Completed: {goal.completedLast7.map(task => task.title).join(', ')}{goal.completedLast7Count > goal.completedLast7.length ? '…' : ''}</Typography>}
              {goal.openTasks.length > 0 && <Typography color="text.secondary">Open: {goal.openTasks.map(task => task.title).join(', ')}{goal.openTaskCount > goal.openTasks.length ? '…' : ''}</Typography>}
            </Box>)}
            <Typography>Goal work: {completedGoal} of {completedGoal + completedOther} work items completed in the last 7 days; {plannedGoal} of {plannedGoal + plannedOther} planned for this week.</Typography>
            {review.report.goals.unplannedGoalIds.length > 0 && <Typography color="warning.main">No plan for this week: {review.report.goals.items.filter(goal => review.report.goals.unplannedGoalIds.includes(goal.id)).map(goal => goal.title).join(', ')}.</Typography>}
            <Typography variant="h6">Remaining tasks and series</Typography>
            <Typography>{review.report.carryoverCount} carryover tasks/series · {review.report.overdueCount} overdue deadlines · {review.report.dueInTargetWeekCount} due in target week</Typography>
            {review.report.occurrences && <Typography>{review.report.occurrences.totalCount} pending occurrences across {review.report.occurrences.seriesCount} series · {review.report.occurrences.carryoverCount} earlier scheduled dates · {review.report.occurrences.plannedCount} scheduled in target week</Typography>}
            <Typography color="text.secondary">Continue in chat: “Open my weekly review.” Ask for occurrence dates or history in chat. Each series has one row here; pending occurrence counts describe separate work items. Receiving or opening a review never changes tasks.</Typography>
            {review.report.totalCount === 0 ? <Typography>No tasks need review.</Typography> : <Box component="ul" sx={{ pl: 3 }}>
              {review.report.tasks.map(task => <li key={task.id}>{task.title} — {[task.isRecurring && `${task.pendingOccurrenceCount ?? 0} pending occurrences`, task.carryover && 'carryover', task.overdue && 'overdue deadline', task.dueInTargetWeek && 'due in target week'].filter(Boolean).join(', ')}</li>)}
            </Box>}
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
              <Typography variant="body2" color="text.secondary">
                {review.report.totalCount === 0 ? '0 tasks/series' : `${review.report.offset + 1}–${Math.min(review.report.offset + review.report.tasks.length, review.report.totalCount)} of ${review.report.totalCount} tasks/series`}
              </Typography>
              <Button disabled={busy || review.report.offset === 0} onClick={() => void run(async () => setReview(await apiService.openWeeklyReview(review.review.id, Math.max(0, review.report.offset - review.report.limit))))}>Previous</Button>
              <Button disabled={busy || review.report.offset + review.report.limit >= review.report.totalCount} onClick={() => void run(async () => setReview(await apiService.openWeeklyReview(review.review.id, review.report.offset + review.report.limit)))}>Next</Button>
            </Stack>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
              {['complete', 'skip'].map(action => <Button key={action} disabled={busy || ['completed', 'skipped'].includes(review.review.status)} onClick={() => void run(async () => {
                const state = await apiService.transitionWeeklyReview(review.review.id, action); setReview({ ...review, review: state });
              })}>{action === 'complete' ? 'Finish review' : 'Skip this week'}</Button>)}
            </Stack>
            <TextField label={`Remind me later (${review.review.timeZoneId})`} type="datetime-local" value={deferTime} disabled={busy}
              slotProps={{ inputLabel: { shrink: true } }} onChange={event => setDeferTime(event.target.value)} />
            <Box><Button disabled={busy || !deferTime || ['completed', 'skipped'].includes(review.review.status)} onClick={() => void run(async () => {
              const state = await apiService.transitionWeeklyReview(review.review.id, 'defer', deferTime); setReview({ ...review, review: state });
            })}>Defer review</Button></Box>
          </Stack>
        </Paper>}
      </Stack>
    </Box>
  </Box>;
}
