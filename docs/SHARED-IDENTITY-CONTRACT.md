# MDS Studio — Shared Identity Contract (M1)

## Boundary

MDS Core owns users, passwords, roles, permissions and sessions. MDS Studio never stores or creates those credentials. Instead, it receives the user's existing MDS Core session in the `Authorization: Bearer` header and asks MDS Core to validate it through `GET /auth/me`.

## Identity response contract

For a valid Core session, `GET /auth/me` returns the stable user identity and its effective access:

```json
{
  "id": "stable-user-id",
  "username": "user.name",
  "email": "user@example.com",
  "roles": ["ADMIN"],
  "permissions": ["studio.access", "studio.diagnostics", "agent.manage"]
}
```

The Studio maps `id`, `username`, roles and permissions into its request identity. The M0 Studio permissions are `studio.access`, `studio.diagnostics` and `agent.manage`; MDS Core assigns them to the appropriate roles. The initial migration grants all three only to `ADMIN`.

## Studio configuration

Production configuration is outside Git:

```text
Authentication__Mode=CoreSession
Authentication__CoreBaseUrl=https://core.example.mds/api
Authentication__RequireHttps=true
```

With `CoreSession`, the Studio refuses to start without a valid Core URL. HTTPS is required unless `Authentication__RequireHttps=false` is intentionally set for local homologation.

## Homologation transition

The current M0 homologation station continues using `Development` mode, preserving the proven API → Agent route. After MDS Core applies its Studio identity migration, homologation can use:

```text
Authentication__Mode=CoreSession
Authentication__CoreBaseUrl=http://192.168.7.14:8081/api
Authentication__RequireHttps=false
```

## Out of scope

This contract does not add a second login, copy passwords, or introduce product, customer, Pattern, Graphics, Smart Size or production functionality.
