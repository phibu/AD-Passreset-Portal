import { useEffect, useRef, useState } from 'react';
import Box from '@mui/material/Box';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';

interface Props {
  password: string;
}

const LABELS = ['Very Weak', 'Weak', 'Fair', 'Strong', 'Very Strong'] as const;
const COLORS = ['error', 'error', 'warning', 'success', 'success'] as const;
const VALUES = [20, 40, 60, 80, 100] as const;

type ZxcvbnFn = (password: string) => { score: 0 | 1 | 2 | 3 | 4 };

export function PasswordStrengthMeter({ password }: Props) {
  const [score, setScore] = useState<0 | 1 | 2 | 3 | 4>(0);
  const [loaded, setLoaded] = useState(false);
  const zxcvbnRef = useRef<ZxcvbnFn | null>(null);

  useEffect(() => {
    if (!password) return;
    let cancelled = false;
    const timer = setTimeout(() => {
      const evaluate = (fn: ZxcvbnFn) => {
        if (!cancelled) setScore(fn(password).score);
      };

      if (zxcvbnRef.current) {
        evaluate(zxcvbnRef.current);
      } else {
        import('@zxcvbn-ts/core').then(async (coreModule) => {
          if (cancelled) return;
          const [common, en] = await Promise.all([
            import('@zxcvbn-ts/language-common'),
            import('@zxcvbn-ts/language-en'),
          ]);
          coreModule.zxcvbnOptions.setOptions({
            translations: en.translations,
            graphs: common.adjacencyGraphs,
            dictionary: { ...common.dictionary, ...en.dictionary },
          });
          const fn: ZxcvbnFn = (pwd) => coreModule.zxcvbn(pwd);
          zxcvbnRef.current = fn;
          setLoaded(true);
          evaluate(fn);
        }).catch(() => {/* ignore load errors */});
      }
    }, 250);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [password]);

  if (!password || !loaded) return null;

  return (
    <Box sx={{ mt: 0.5 }}>
      <LinearProgress
        variant="determinate"
        value={VALUES[score]}
        color={COLORS[score]}
        sx={{ height: 6, borderRadius: 3 }}
      />
      <Typography variant="caption" color={`${COLORS[score]}.main`} sx={{ mt: 0.25, display: 'block' }}>
        {LABELS[score]}
      </Typography>
    </Box>
  );
}
