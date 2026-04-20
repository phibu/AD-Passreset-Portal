import { describe, it, expect, afterEach, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import AdPasswordPolicyPanel from '../AdPasswordPolicyPanel';
import { PasswordForm } from '../PasswordForm';
import type { ClientSettings, PolicyResponse } from '../../types/settings';

/**
 * Plan 10-04 / STAB-021 — accessibility regression tests.
 *
 * Guards:
 *  - Panel exposes role="region" with an accessible name.
 *  - Tab order traverses the panel BEFORE the Username input.
 *  - No disclosure widget (Collapse / Accordion) is reintroduced — guards D-19.
 */

const samplePolicy: PolicyResponse = {
  minLength: 12,
  requiresComplexity: true,
  historyLength: 5,
  minAgeDays: 1,
  maxAgeDays: 90,
};

function baseSettings(overrides: Partial<ClientSettings> = {}): ClientSettings {
  return {
    usePasswordGeneration: false,
    minimumDistance: 0,
    passwordEntropy: 12,
    showPasswordMeter: false,
    minimumScore: 0,
    useEmail: false,
    showAdPasswordPolicy: true,
    ...overrides,
  };
}

function mockPolicyFetch(policy: PolicyResponse | null) {
  const fetchFn = vi.fn(async (url: RequestInfo | URL) => {
    if (typeof url === 'string' && url === '/api/password/policy') {
      return {
        ok: policy !== null,
        status: policy !== null ? 200 : 503,
        json: async () => policy,
      } as unknown as Response;
    }
    return { ok: true, status: 200, json: async () => ({}) } as unknown as Response;
  });
  vi.stubGlobal('fetch', fetchFn);
  return fetchFn;
}

describe('AdPasswordPolicyPanel a11y', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('panel element has role="region" (role_region_present)', () => {
    render(<AdPasswordPolicyPanel policy={samplePolicy} loading={false} />);
    const region = screen.getByRole('region');
    expect(region.getAttribute('role')).toBe('region');
  });

  it('panel has an aria-label mentioning "password requirements" (accessible_name_set)', () => {
    render(<AdPasswordPolicyPanel policy={samplePolicy} loading={false} />);
    const region = screen.getByRole('region');
    const label = region.getAttribute('aria-label') ?? '';
    expect(label.toLowerCase()).toContain('password requirements');
  });

  it('keyboard focus order visits the panel before the Username field (keyboard_focus_order_above_fields)', async () => {
    mockPolicyFetch(samplePolicy);
    const { container } = render(
      <PasswordForm
        settings={baseSettings({ showAdPasswordPolicy: true })}
        onSuccess={vi.fn()}
      />,
    );

    const region = await screen.findByRole('region', {
      name: /password requirements/i,
    });

    // Collect focusable nodes in DOM order.
    const focusables = Array.from(
      container.querySelectorAll<HTMLElement>(
        'button, input, textarea, a[href], [tabindex]:not([tabindex="-1"])',
      ),
    );

    const usernameInput = screen.getByLabelText(/username/i) as HTMLElement;
    const usernameIndex = focusables.indexOf(usernameInput);
    expect(usernameIndex).toBeGreaterThanOrEqual(0);

    // Panel-owned focusables (if any) must all come before the username input.
    const panelFocusables = Array.from(
      region.querySelectorAll<HTMLElement>(
        'button, input, textarea, a[href], [tabindex]:not([tabindex="-1"])',
      ),
    );

    for (const node of panelFocusables) {
      const idx = focusables.indexOf(node);
      expect(idx).toBeLessThan(usernameIndex);
    }

    // The panel region itself must precede the username input in document order.
    const position = region.compareDocumentPosition(usernameInput);
    expect(position & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('panel contains no disclosure widget — no aria-expanded or aria-controls (no_collapse_widget)', () => {
    const { container } = render(
      <AdPasswordPolicyPanel policy={samplePolicy} loading={false} />,
    );
    expect(container.querySelector('[aria-expanded]')).toBeNull();
    expect(container.querySelector('[aria-controls]')).toBeNull();
    expect(container.querySelector('button[role="switch"]')).toBeNull();
  });
});
