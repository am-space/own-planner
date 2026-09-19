import { Box, alpha } from '@mui/material';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';

/** Shared, scoped presentation for untrusted Markdown. Raw HTML remains disabled. */
export default function MarkdownContent({ children }: { children: string }) {
    return (
        <Box sx={(theme) => ({
            minWidth: 0,
            maxWidth: '80ch',
            fontSize: '1rem',
            lineHeight: 1.65,
            overflowWrap: 'anywhere',
            '& > :first-child': { mt: 0 },
            '& > :last-child': { mb: 0 },
            '& h1, & h2, & h3, & h4, & h5, & h6': {
                fontFamily: 'inherit', fontWeight: 600, lineHeight: 1.3,
                mt: 3, mb: 1.25, color: 'text.primary',
            },
            '& h1': { fontSize: '1.75rem', letterSpacing: '-0.025em' },
            '& h2': { fontSize: '1.3rem' },
            '& h3': { fontSize: '1.125rem' },
            '& h4': { fontSize: '1rem' },
            '& h5': { fontSize: '0.9375rem' },
            '& h6': { fontSize: '0.875rem' },
            '& p, & ul, & ol, & blockquote, & pre, & .markdown-table': { my: 1.5 },
            '& strong': { fontWeight: 600 },
            '& ul, & ol': { pl: 3 },
            '& li': { pl: 0.25, my: 0.5 },
            '& li > p': { my: 0.75 },
            '& li > ul, & li > ol': { my: 0.5 },
            '& .contains-task-list': { listStyle: 'none', pl: 0 },
            '& input[type="checkbox"]': { mr: 1, accentColor: theme.palette.primary.main },
            '& blockquote': {
                borderLeft: 3, borderColor: 'divider', pl: 2, color: 'text.secondary',
                '& > :first-child': { mt: 0 }, '& > :last-child': { mb: 0 },
            },
            '& hr': { border: 0, borderTop: 1, borderColor: 'divider', my: 3 },
            '& a': {
                color: theme.palette.mode === 'dark' ? 'primary.light' : 'primary.dark',
                textDecoration: 'underline', textUnderlineOffset: '0.2em',
                '&:hover': { textDecorationThickness: '2px' },
            },
            '& :focus-visible': { outline: '2px solid', outlineColor: 'primary.main', outlineOffset: 3 },
            '& code': {
                fontFamily: 'ui-monospace, SFMono-Regular, Consolas, monospace',
                fontSize: '0.875em', bgcolor: alpha(theme.palette.text.primary, 0.06),
                px: 0.5, py: 0.25, borderRadius: 0.5,
            },
            '& pre': {
                p: 2, border: 1, borderColor: 'divider', borderRadius: 2,
                bgcolor: alpha(theme.palette.text.primary, 0.035), overflowX: 'auto',
                whiteSpace: 'pre', overflowWrap: 'normal', lineHeight: 1.6,
                '& code': { p: 0, bgcolor: 'transparent', fontSize: '0.875rem' },
            },
            '& table': { borderCollapse: 'collapse', width: '100%', fontSize: '0.9375rem' },
            '& th, & td': { borderBottom: 1, borderColor: 'divider', px: 1.5, py: 1, minWidth: 120, verticalAlign: 'top' },
            '& th': { bgcolor: 'action.hover', fontWeight: 600 },
            '& img': { maxWidth: '100%', height: 'auto' },
        })}>
            <ReactMarkdown remarkPlugins={[remarkGfm]} components={{
                table: ({ children }) => (
                    <Box className="markdown-table" role="region" aria-label="Scrollable table" tabIndex={0}
                        sx={{ overflowX: 'auto', maxWidth: '100%', border: 1, borderColor: 'divider', borderRadius: 2 }}>
                        <table>{children}</table>
                    </Box>
                ),
                pre: ({ children }) => <pre tabIndex={0} aria-label="Code block">{children}</pre>,
            }}>
                {children}
            </ReactMarkdown>
        </Box>
    );
}
