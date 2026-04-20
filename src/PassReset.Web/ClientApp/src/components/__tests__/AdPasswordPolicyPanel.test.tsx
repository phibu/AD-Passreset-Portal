import { describe, it, expect, afterEach, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import AdPasswordPolicyPanel from '../AdPasswordPolicyPanel';
import { PasswordForm } from '../PasswordForm';
import type { ClientSettings, PolicyResponse } from '../../types/settings';

/**
 * Plan 10-04 / STAB-021 — tests guaranteeing that:
 *  - When operators enable the panel (new default), it renders visibly from mount.
 *  - When operators opt out, it does not render.
 *  - In PasswordForm, the panel sits ABOVE the Username field.
 *  - The panel shows a skeleton placeholder while the policy is still loading.
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
    // Swallow any other request (HIBP precheck etc.) with an empty OK.
    return { ok: true, status: 200, json: async () => ({}) } as unknown as Response;
  });
  vi.stubGlobal('fetch', fetchFn);
  return fetchFn;
}

describe('AdPasswordPolicyPanel', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('renders a visible region when policy is loaded (panel_rendered_by_default)', () => {
    render(<AdPasswordPolicyPanel policy={samplePolicy} loading={false} />);
    const region = screen.getByRole('region', { name: /password requirements/i });
    expect(region).toBeInTheDocument();
  });

  it('renders nothing when operators disable the panel (panel_hidden_when_disabled)', () => {
    mockPolicyFetch(samplePolicy);
    render(
      <PasswordForm
        settings={baseSettings({ showAdPasswordPolicy: false })}
        onSuccess={vi.fn()}
      />,
    );
    expect(
      screen.queryByRole('region', { name: /password requirements/i }),
    ).toBeNull();
  });

  it('places the panel above the Username field in PasswordForm (panel_renders_above_username)', async () => {
    mockPolicyFetch(samplePolicy);
    render(
      <PasswordForm
        settings={baseSettings({ showAdPasswordPolicy: true })}
        onSuccess={vi.fn()}
      />,
    );

    const region = await screen.findByRole('region', {
      name: /password requirements/i,
    });
    const username = screen.getByLabelText(/username/i);

    // DOCUMENT_POSITION_FOLLOWING (4) => second arg comes AFTER first in document order.
    // Region must precede username => region.compareDocumentPosition(username) & 4 is truthy.
    const position = region.compareDocumentPosition(username);
    expect(position & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('renders a placeholder while the policy is still loading (panel_skeleton_while_loading)', () => {
    const { container } = render(
      <AdPasswordPolicyPanel policy={null} loading={true} />,
    );
    // Skeleton renders a non-null element; the loading state must not collapse to null.
    expect(container.firstChild).not.toBeNull();
  });

  it('hides the panel once loading resolves with no policy payload', async () => {
    mockPolicyFetch(null);
    render(
      <PasswordForm
        settings={baseSettings({ showAdPasswordPolicy: true })}
        onSuccess={vi.fn()}
      />,
    );
    // When the fetch resolves to null, AdPasswordPolicyPanel fails closed.
    await waitFor(() =>
      expect(
        screen.queryByRole('region', { name: /password requirements/i }),
      ).toBeNull(),
    );
  });
});
