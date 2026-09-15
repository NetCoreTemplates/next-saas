# Organizations and members

Organizations isolate customer data, subscriptions, quotas, files, API keys, settings, and lifecycle operations. One user can belong to several organizations.

[Documentation home](../README.md) · [Tenancy concept](../concepts/organizations-and-tenancy.md)

## User behavior

- Registration or first SaaS access creates a personal organization when enabled.
- Users can create another organization with independent data and billing.
- The application shell switches the active organization explicitly.
- Owners and Admins invite members and manage non-owner roles.
- Billing members manage subscription state.
- Ownership transfer is an explicit lifecycle operation.

## Models and APIs

Core models are `Workspace`, `WorkspaceMember`, and `UserWorkspacePreference`.

| Operation | API |
| --- | --- |
| List memberships | `GET /saas/workspaces` |
| Create organization | `POST /saas/organizations` |
| Select active organization | `POST /saas/workspaces/active` |
| Update profile | `POST /saas/workspace` |
| List members | `GET /saas/members` |
| Invite | `POST /saas/members` |
| Resend/revoke/remove/change role | workspace member APIs |
| Accept invitation | `POST /saas/invitations/accept` |
| Transfer, leave, delete | `/saas/lifecycle` APIs |

## Roles

Owner controls ownership and deletion. Admin manages settings, members, and exports. Billing manages billing. Member uses entitled features. Server-side `WorkspaceAuthorization` owns these decisions.

![Team Roles and Member Management](../assets/team-roles.png)

Platform Admin/BillingAdmin/Support roles are separate and do not create customer membership.

## Invitations

Invitations bind a role and normalized email to a random, one-way-hashed token and expiry. Acceptance requires the authenticated user’s verified email to match. Resend rotates delivery state; revoke or expiry makes the token unusable.

`Saas.InvitationExpiryDays` is global. Delivery uses the notification system, so production requires working SMTP.

![Invite Organization Member](../assets/invite-member.png)

## Configuration

- `Saas.EnablePersonalWorkspaces` — provision a personal organization when appropriate.
- `Saas.DefaultPlan` — initial published plan code.
- `Saas.InvitationExpiryDays` — invitation lifetime.
- `Product` — deployment-wide brand/contact information, not customer profile data.

## Isolation and authorization

Every resource lookup includes the resolved organization. Switching active organization refreshes shared customer state. API keys remain bound to the organization in which they were created and ignore browser selection changes.

## Failure modes

- Invite email already belongs to the organization: reject or return existing state.
- Wrong authenticated email accepts token: reject.
- Owner attempts to leave: require ownership transfer.
- Member changes Owner through ordinary role API: reject.

![Owner Protection and Transfer](../assets/owner-protection.png)
- Deleted/disabled membership accesses data: resolver denies it.

## Extension points

Add organization profile fields to the RDBMS, not global JSON. Decide which roles can change each field, add tenant isolation tests, and include customer-owned data in export/deletion policy.

## Verify

Create two organizations, switch between them, invite and accept each role, exercise permissions, bind API keys, attempt cross-tenant resource IDs, transfer ownership, and leave safely.

## Related documentation

- [Organizations and tenancy](../concepts/organizations-and-tenancy.md)
- [Data lifecycle](data-lifecycle.md)
- [API keys](api-keys.md)

