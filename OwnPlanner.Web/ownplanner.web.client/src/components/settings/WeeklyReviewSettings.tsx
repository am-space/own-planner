import { useEffect, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Autocomplete, Box, Button, FormControlLabel, MenuItem, Paper, Stack, Switch, TextField, Typography } from '@mui/material';
import { apiService } from '../../services/api';
import type { WeeklyReviewPreferences } from '../../types/api.types';

const zones = ['UTC', ...Intl.supportedValuesOf('timeZone')];
const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

export default function WeeklyReviewSettings() {
  const [preferences, setPreferences] = useState<WeeklyReviewPreferences | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    apiService.getWeeklyReviewPreferences().then(value => { if (active) setPreferences(value); })
      .catch(err => { if (active) setError(err instanceof Error ? err.message : 'Could not load weekly review settings'); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);

  const run = async (action: () => Promise<void>) => {
    setBusy(true); setError(null); setNotice(null);
    try { await action(); }
    catch (err) { setError(err instanceof Error ? err.message : 'Weekly review request failed'); }
    finally { setBusy(false); }
  };

  return <Paper id="weekly-review" sx={{ p: { xs: 2, sm: 3 }, mb: 3 }}>
    <Typography variant="h6" gutterBottom>Weekly review</Typography>
    <Typography color="text.secondary" sx={{ mb: 2 }}>
      Review unfinished work and upcoming commitments. Telegram reminders are optional and require a connected account.
      The same review is available in web and Telegram chat.
    </Typography>
    {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    {notice && <Alert severity="success" sx={{ mb: 2 }}>{notice}</Alert>}
    {loading ? <Typography role="status">Loading weekly review settings…</Typography> : preferences && <Stack spacing={2}>
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
      <Box><Button component={RouterLink} to="/planner/review">Open weekly review</Button></Box>
    </Stack>}
  </Paper>;
}
