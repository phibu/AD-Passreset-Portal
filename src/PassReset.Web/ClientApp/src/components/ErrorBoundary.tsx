import { Component } from 'react';
import type { ErrorInfo, ReactNode } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';

interface Props {
  children: ReactNode;
}

interface State {
  hasError: boolean;
}

export class ErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false };

  static getDerivedStateFromError(): State {
    return { hasError: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('ErrorBoundary caught:', error, info.componentStack);
  }

  render() {
    if (this.state.hasError) {
      return (
        <Box sx={{ py: 4, px: 2, textAlign: 'center' }}>
          <Alert severity="error" sx={{ mb: 2, textAlign: 'left' }}>
            <Typography fontWeight={600}>Something went wrong</Typography>
            <Typography variant="body2" sx={{ mt: 0.5 }}>
              An unexpected error occurred. Please try reloading the page.
            </Typography>
          </Alert>
          <Button
            variant="contained"
            onClick={() => window.location.reload()}
          >
            Reload page
          </Button>
        </Box>
      );
    }

    return this.props.children;
  }
}
