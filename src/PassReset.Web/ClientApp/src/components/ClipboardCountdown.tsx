/**
 * FEAT-003: Clipboard countdown chip.
 *
 * Displays remaining seconds until the generated password is auto-cleared from
 * the clipboard. Switches to a warning color at <= 5s, renders a success
 * "Clipboard cleared" chip after the clear fires, and renders nothing while
 * idle or cancelled. Wrapped in an aria-live polite region per UI-SPEC.
 */

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import ContentPasteOffOutlinedIcon from '@mui/icons-material/ContentPasteOffOutlined';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';

export interface ClipboardCountdownProps {
  /** Seconds remaining when counting. Not used for 'cleared' / 'idle' / 'cancelled' states. */
  remaining: number;
  state: 'counting' | 'cleared' | 'cancelled' | 'idle';
}

export default function ClipboardCountdown({ remaining, state }: ClipboardCountdownProps) {
  if (state === 'idle' || state === 'cancelled') {
    // Still render the live region so screen readers register subsequent updates.
    return <Box aria-live="polite" aria-atomic="true" />;
  }

  return (
    <Box aria-live="polite" aria-atomic="true">
      {state === 'counting' && (
        <Chip
          size="small"
          icon={<ContentPasteOffOutlinedIcon />}
          label={`Clipboard clears in ${Math.max(remaining, 0)}s`}
          color={remaining <= 5 ? 'warning' : 'default'}
          sx={{ mt: 1 }}
        />
      )}
      {state === 'cleared' && (
        <Chip
          size="small"
          icon={<CheckCircleOutlineIcon />}
          label="Clipboard cleared"
          color="success"
          sx={{ mt: 1 }}
        />
      )}
    </Box>
  );
}
