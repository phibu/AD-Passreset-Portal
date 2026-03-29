import { useEffect, useMemo, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Container from '@mui/material/Container';
import CssBaseline from '@mui/material/CssBaseline';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { ThemeProvider, createTheme } from '@mui/material/styles';
import LockPersonIcon from '@mui/icons-material/LockPerson';

import { ErrorBoundary } from './components/ErrorBoundary';
import { PasswordForm } from './components/PasswordForm';
import { useSettings } from './hooks/useSettings';

function buildTheme(prefersDark: boolean) {
  return createTheme({
    typography: {
      fontFamily: '"Inter", "Roboto", "Helvetica Neue", Arial, sans-serif',
    },
    palette: {
      mode: prefersDark ? 'dark' : 'light',
      primary: {
        main:  '#0b6366',  // WCAG AA compliant (~4.7:1 on white)
        dark:  '#094e51',
        light: '#0d7377',
      },
      ...(prefersDark ? {} : {
        background: {
          default: '#f5f5f7',
        },
      }),
    },
    components: {
      MuiCard: {
        styleOverrides: {
          root: {
            borderRadius: 16,
            boxShadow: prefersDark
              ? '0 8px 32px rgba(0,0,0,0.30)'
              : '0 8px 32px rgba(0,0,0,0.10)',
          },
        },
      },
      MuiTextField: {
        defaultProps: {
          variant: 'outlined',
          size: 'small',
        },
      },
      MuiButton: {
        styleOverrides: {
          root: {
            textTransform: 'none',
            fontWeight: 600,
            borderRadius: 8,
          },
        },
      },
    },
  });
}

export default function App() {
  const prefersDark = useMediaQuery('(prefers-color-scheme: dark)');
  const theme = useMemo(() => buildTheme(prefersDark), [prefersDark]);

  const { settings, loading, error } = useSettings();
  const [succeeded, setSucceeded]    = useState(false);

  // Update page title once settings are loaded
  useEffect(() => {
    if (settings?.applicationTitle) {
      document.title = settings.applicationTitle;
    }
  }, [settings?.applicationTitle]);

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <ErrorBoundary>
      <Box
        sx={{
          minHeight: '100vh',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          bgcolor: 'background.default',
          py: 4,
          gap: 3,
        }}
      >
        {/* Product name header */}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <LockPersonIcon sx={{ fontSize: 28, color: 'primary.main' }} />
          <Typography
            variant="h6"
            fontWeight={700}
            letterSpacing={0.5}
            sx={{ color: 'primary.main', lineHeight: 1 }}
          >
            PassReset
          </Typography>
        </Box>

        <Container disableGutters sx={{ maxWidth: 440, width: '100%', px: { xs: 2, sm: 0 } }}>
          <Card>
            <CardContent sx={{ p: { xs: 3, sm: 4 } }}>

              {/* Header */}
              <Typography variant="h5" fontWeight={600} gutterBottom>
                {settings?.changePasswordTitle ?? 'Change Account Password'}
              </Typography>

              {/* Loading skeleton — preserves layout to minimise CLS */}
              {loading && (
                <Box sx={{ py: 1 }}>
                  <Skeleton variant="rounded" height={40} sx={{ mb: 2 }} />
                  <Skeleton variant="rounded" height={40} sx={{ mb: 2 }} />
                  <Skeleton variant="rounded" height={40} sx={{ mb: 2 }} />
                  <Skeleton variant="rounded" height={40} sx={{ mb: 2 }} />
                  <Skeleton variant="rounded" height={42} />
                </Box>
              )}

              {/* Settings load error */}
              {!loading && error && (
                <Alert severity="error">
                  Unable to load application settings. Please refresh the page or contact IT Support.
                </Alert>
              )}

              {/* Success state */}
              {!loading && !error && succeeded && (
                <Box>
                  <Alert severity="success" sx={{ mb: 2 }}>
                    <Typography fontWeight={600}>
                      {settings?.alerts?.successAlertTitle ?? 'Password changed successfully.'}
                    </Typography>
                    {settings?.alerts?.successAlertBody && (
                      <Typography variant="body2" sx={{ mt: 0.5 }}>
                        {settings.alerts.successAlertBody}
                      </Typography>
                    )}
                  </Alert>
                  <Button
                    variant="outlined"
                    onClick={() => setSucceeded(false)}
                    fullWidth
                  >
                    Change another password
                  </Button>
                </Box>
              )}

              {/* Form */}
              {!loading && !error && !succeeded && settings && (
                <PasswordForm settings={settings} onSuccess={() => setSucceeded(true)} />
              )}

            </CardContent>
          </Card>
        </Container>

        {/* Footer */}
        <Typography variant="caption" sx={{ color: 'text.disabled' }}>
          &copy; {new Date().getFullYear()} — Internal IT Tool
        </Typography>
      </Box>
      </ErrorBoundary>
    </ThemeProvider>
  );
}
