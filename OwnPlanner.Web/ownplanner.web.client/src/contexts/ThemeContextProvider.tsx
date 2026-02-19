import { useState, useEffect, useMemo } from 'react';
import type { ReactNode } from 'react';
import { createTheme, useMediaQuery } from '@mui/material';
import { ThemeContext, THEME_STORAGE_KEY } from './ThemeContext';
import type { ColorModePreference } from './ThemeContext';

export default function ThemeContextProvider({ children }: { children: ReactNode }) {
    const prefersDark = useMediaQuery('(prefers-color-scheme: dark)');

    const [mode, setModeState] = useState<ColorModePreference>(() => {
        const stored = localStorage.getItem(THEME_STORAGE_KEY);
        if (stored === 'light' || stored === 'dark' || stored === 'system') {
            return stored;
        }
        return 'system';
    });

    const setMode = (newMode: ColorModePreference) => {
        setModeState(newMode);
        localStorage.setItem(THEME_STORAGE_KEY, newMode);
    };

    const resolvedMode: 'light' | 'dark' =
        mode === 'system' ? (prefersDark ? 'dark' : 'light') : mode;

    const theme = useMemo(
        () =>
            createTheme({
                palette: {
                    mode: resolvedMode,
                    primary: { main: '#1976d2' },
                    secondary: { main: '#dc004e' },
                },
                components: {
                    MuiTextField: {
                        defaultProps: { variant: 'outlined' },
                    },
                },
            }),
        [resolvedMode]
    );

    useEffect(() => {
        document.documentElement.style.colorScheme = resolvedMode;
    }, [resolvedMode]);

    return (
        <ThemeContext.Provider value={{ mode, setMode, theme }}>
            {children}
        </ThemeContext.Provider>
    );
}
