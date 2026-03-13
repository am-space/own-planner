import CircularProgress from '@mui/material/CircularProgress';
import FormControl from '@mui/material/FormControl';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import type { SxProps, Theme } from '@mui/material/styles';
import type { SelectChangeEvent } from '@mui/material/Select';
import ArrowDropDownIcon from '@mui/icons-material/ArrowDropDown';
import type { PlanningMode } from '../types/api.types';

const MODES: { value: PlanningMode; label: string }[] = [
  { value: 'GlobalPlanning', label: 'Global Planning' },
  { value: 'WeekPlanning', label: 'Week Planning' },
  { value: 'DayWork', label: 'Day Work' },
  { value: 'Reflection', label: 'Reflection' },
  { value: 'SystemAnalysis', label: 'System Analysis' },
];

interface PlanningModeSelectorProps {
  currentMode: PlanningMode;
  disabled: boolean;
  loading: boolean;
  onChange: (mode: PlanningMode) => void;
  sx?: SxProps<Theme>;
  fullWidth?: boolean;
}

export default function PlanningModeSelector({
  currentMode,
  disabled,
  loading,
  onChange,
  sx,
  fullWidth,
}: PlanningModeSelectorProps) {
  const handleChange = (event: SelectChangeEvent) => {
    onChange(event.target.value as PlanningMode);
  };

  return (
    <FormControl size="small" sx={{ minWidth: fullWidth ? undefined : 150 }} fullWidth={fullWidth}>
      <Select
        value={currentMode}
        onChange={handleChange}
        disabled={disabled}
        IconComponent={
          loading
            ? () => <CircularProgress size={14} sx={{ mr: '7px', color: 'inherit' }} />
            : ArrowDropDownIcon
        }
        sx={sx}
      >
        {MODES.map(({ value, label }) => (
          <MenuItem key={value} value={value}>
            {label}
          </MenuItem>
        ))}
      </Select>
    </FormControl>
  );
}
