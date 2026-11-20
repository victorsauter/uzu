import {
  clearPendingInvitation,
  hasPendingInvitation,
  invitationTokenFromUrl,
  setPendingInvitation,
  takePendingInvitation,
} from '../pendingInvitation';

const token = 'Zm9vYmFyLTEyMw_-AbCdEfGhIjKlMnOpQrStUvWxYz0';

describe('invitationTokenFromUrl', () => {
  it.each([
    [`uzu://invitation/${token}`, token],
    [`uzu://invitation/${token}/`, token],
    [`/invitation/${token}`, token],
    [`  uzu://invitation/${token}?utm=x`, token],
    [`exp://192.168.1.2:8081/--/invitation/${token}`, token],
  ])('reads the token of %s', (url, expected) => {
    expect(invitationTokenFromUrl(url)).toBe(expected);
  });

  it.each([
    'uzu://walks/abc',
    'uzu://invitation',
    'uzu://invitation/',
    'uzu://invitation/a/b',
    'uzu://invitations/abc',
    'uzu://invitation/not%20a%20token',
    `uzu://invitation/${'a'.repeat(129)}`,
    '',
  ])('does not read a token from %s', (url) => {
    expect(invitationTokenFromUrl(url)).toBeNull();
  });
});

describe('the pending invitation', () => {
  beforeEach(() => clearPendingInvitation());

  it('is given out once', () => {
    expect(hasPendingInvitation()).toBe(false);
    setPendingInvitation(token);
    expect(hasPendingInvitation()).toBe(true);
    expect(takePendingInvitation()).toBe(token);
    expect(takePendingInvitation()).toBeNull();
    expect(hasPendingInvitation()).toBe(false);
  });

  it('keeps the last link received', () => {
    setPendingInvitation('first');
    setPendingInvitation('second');
    expect(takePendingInvitation()).toBe('second');
  });

  it('can be forgotten', () => {
    setPendingInvitation(token);
    clearPendingInvitation();
    expect(takePendingInvitation()).toBeNull();
  });

  it('is never written to the console', () => {
    const spies = (['log', 'info', 'warn', 'error'] as const).map((method) =>
      jest.spyOn(console, method).mockImplementation(() => {}),
    );
    setPendingInvitation(token);
    takePendingInvitation();
    clearPendingInvitation();
    for (const spy of spies) expect(spy).not.toHaveBeenCalled();
    jest.restoreAllMocks();
  });
});
