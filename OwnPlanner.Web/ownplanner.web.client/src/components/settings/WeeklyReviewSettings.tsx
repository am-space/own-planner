import { useEffect, useState } from 'react';
import { Alert, Autocomplete, Box, Button, FormControlLabel, MenuItem, Paper, Stack, Switch, TextField, Typography } from '@mui/material';
import { apiService } from '../../services/api';
import type { WeeklyReviewPreferences, WeeklyReviewView } from '../../types/api.types';

const zones = ['UTC', ...Intl.supportedValuesOf('timeZone')];
const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

export default function WeeklyReviewSettings() {
  const [preferences, setPreferences] = useState<WeeklyReviewPreferences | null>(null);
  const [review, setReview] = useState<WeeklyReviewView | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [deferTime, setDeferTime] = useState('');

  useEffect(() => {
    let active = true;
    apiService.getWeeklyReviewPreferences().then(value => { if (active) setPreferences(value); })
      .catch(err => { if (active) setError(err instanceof Error ? err.message : 'Could not load weekly review settings'); });
    return () => { active = false; };
  }, []);

  const run = async (action: () => Promise<void>) => {
    setBusy(true); setError(null); setNotice(null);
    try { await action(); }
    catch (err) { setError(err instanceof Error ? err.message : 'Weekly review request failed'); }
    finally { setBusy(false); }
  };

  return <Paper sx={{ p: { xs: 2, sm: 3 }, mb: 3 }}>
    <Typography variant="h6" gutterBottom>Weekly review</Typography>
    <Typography color="text.secondary" sx={{ mb: 2 }}>
      Review unfinished work and upcoming commitments. Telegram reminders are optional and require a connected account.
      The same review is available in web and Telegram chat.
    </Typography>
    {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    {notice && <Alert severity="success" sx={{ mb: 2 }}>{notice}</Alert>}
    {!preferences ? <Typography>Loading weekly review settings…</Typography> : <Stack spacing={2}>
      <FormControlLabel control={<Switch checked={preferences.enabled} disabled={busy}
        onChange={(_, enabled) => setPreferences({ ...preferences, enabled })} />} label="Enable weekly reminders" />
      <Autocomplete options={zones} value={preferences.timeZoneId} disabled={busy}
        onChange={(_, timeZoneId) => setPreferences({ ...preferences, timeZoneId })}
        renderInput={params => <TextField {...params} label="Review timezone" helperText="Select your timezone explicitly." />} />
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
        <TextField select fullWidth label="Week starts on" value={preferences.weekStart} disabled={busy}
          onChange={event => setPreferences({ ...preferences, weekStart: Number(event.target.value) })}>
          {days.map((day, index) => <MenuItem key={day} value={index}>{day}</MenuItem>)}
        </TextField>
        <TextField fullWidth label="Reminder time" type="time" value={preferences.reminderTime} disabled={busy}
          helperText={`Sent on ${days[(preferences.weekStart + 6) % 7]} in your selected timezone.`}
          onChange={event => setPreferences({ ...preferences, reminderTime: event.target.value })} />
        <TextField fullWidth label="Delivery channel" value="Telegram" slotProps={{ input: { readOnly: true } }} />
      </Stack>
      <Box><Button variant="contained" disabled={busy || (preferences.enabled && !preferences.timeZoneId)} onClick={() => void run(async () => {
        setPreferences(await apiService.saveWeeklyReviewPreferences(preferences)); setNotice('Weekly review settings saved.');
      })}>Save weekly review settings</Button></Box>
      <Box><Button disabled={busy || !preferences.timeZoneId} onClick={() => void run(async () => {
        setReview(await apiService.openWeeklyReview());
      })}>Open weekly review</Button></Box>
      {review && <Stack spacing={2}>
        <Typography>Week of {review.review.targetWeek} · {review.review.timeZoneId} · {review.review.status}</Typography>
        <Typography>{review.report.carryoverCount} carryover · {review.report.overdueCount} overdue deadlines · {review.report.dueInTargetWeekCount} due in target week</Typography>
        <Typography color="text.secondary">Continue in chat: “Open my weekly review.” Receiving or opening a review never changes tasks.</Typography>
        {review.report.totalCount === 0 ? <Typography>No tasks need review.</Typography> : <Box component="ul" sx={{ pl: 3 }}>
          {review.report.tasks.map(task => <li key={task.id}>{task.title} — {[task.carryover && 'carryover', task.overdue && 'overdue deadline', task.dueInTargetWeek && 'due in target week'].filter(Boolean).join(', ')}</li>)}
        </Box>}
        <Stack direction="row" spacing={1}>
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
      </Stack>}
    </Stack>}
  </Paper>;
}
