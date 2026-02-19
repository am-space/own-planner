import { createContext, useContext } from 'react';
import type { Theme } from '@mui/material';

export type ColorModePreference = 'light' | 'dark' | 'system';

export interface ThemeContextValue {
    mode: ColorModePreference;
    setMode: (mode: ColorModePreference) => void;
    theme: Theme;
}

export const ThemeContext = createContext<ThemeContextValue | undefined>(undefined);

export const THEME_STORAGE_KEY = 'ownplanner-color-mode';

export function useThemeContext() {
    const ctx = useContext(ThemeContext);
    if (!ctx) throw new Error('useThemeContext must be used inside ThemeContextProvider');
    return ctx;
}
