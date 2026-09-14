# Authentication and accounts

ASP.NET Core Identity owns users, password credentials, confirmation tokens, external-login records, two-factor state, recovery codes, and application cookies.

[Security](README.md) · [Organizations and members](../features/organizations-and-members.md)

## Included behavior

- New accounts require a confirmed email before sign-in.
- Registration rejects duplicate usernames/emails and validates password confirmation.
- Confirmation and reset tokens use ASP.NET Core Identity token providers.
- Registration preserves only local relative return URLs, preventing external redirect targets.
- Authenticator-app 2FA, recovery codes, password changes, email changes, external-login management, and personal-data pages are included in Razor Identity UI.
- Development seed accounts are created only when the environment is Development and the Identity store is empty.

`Notifications.Provider=Smtp` is required by the default production policy so confirmation, recovery, and invitations can be delivered. Development's no-op sender exposes confirmation links locally and is not appropriate for real users.

## Invitations

Organization invitations use a random 24-byte token represented as 48 hexadecimal characters. Only a one-way hash is stored. Acceptance requires:

- pending, non-revoked membership state;
- an unexpired invitation;
- an authenticated user whose verified email matches the normalized invited email.

Registration does not automatically attach an invited organization. The user explicitly accepts the invitation after authenticating as the invited address.

## Production decisions

Review and explicitly configure Identity password, lockout, cookie lifetime, session renewal, account enumeration, and external-provider policies. Enable and enforce MFA for platform operators; consider requiring it for organization Owners and Billing members.

The template applies a fixed-window per-IP limit to sensitive authentication POST routes, including login, registration, password reset, confirmation resend, 2FA/recovery login, and invitation acceptance. Configure `Security.AuthenticationRequestsPerMinute`, add monitoring, and replace or supplement it with a distributed/edge limiter when scaling. The API-key limiter is separate.

Protect recovery codes like passwords. Regeneration invalidates the prior set. Recovery and email-change events should be visible in security audit/notification flows in a derived product.

## Account and organization lifecycle

An Identity user and an organization are different objects:

- organization deletion removes customer organization data but intentionally keeps user accounts;
- the included Identity **Delete personal data** page deletes only `ApplicationUser` through `UserManager`.

The template coordinates personal-account deletion with SaaS state. An Owner must transfer or delete every owned organization first. For an eligible account, the server removes memberships and invitations, API keys, notification preferences/history, and active-organization preference, and revokes active support grants before deleting Identity. Shared organization data and historical audit attribution remain.

## Verify

Test confirmation required, invalid/expired tokens, local and malicious return URLs, password reset, 2FA setup/recovery/reset, lockout policy, invitation email mismatch, revoked/expired invitations, seeded-user absence in Production, and coordinated account deletion.

## Related documentation

- [Authorization and roles](authorization-and-roles.md)
- [Data protection and privacy](data-protection-and-privacy.md)
- [Notifications](../features/notifications.md)
