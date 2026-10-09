# MDS Studio — Shared Identity Contract (M1)

## Boundary

MDS Core, MDS Studio and the future App must trust the same MDS identity issuer. The Studio does not create user accounts, passwords or tokens.

## Token contract

The MDS Shared Identity service must issue an HTTPS JWT access token with:

- `iss`: the configured Identity issuer;
- `aud`: `mds-studio-api` (or the configured Studio audience);
- `sub`: stable MDS user identifier;
- `name`: display name;
- `permission`: one or more permission claims.

M0 permissions remain `studio.access`, `studio.diagnostics` and `agent.manage`. The token issuer owns users, credentials, sessions and permission assignment.

## Studio configuration

Production requires this configuration outside Git:

```text
Authentication__Mode=SharedJwt
Authentication__Authority=https://identity.example.mds
Authentication__Audience=mds-studio-api
Authentication__RequireHttpsMetadata=true
```

When `SharedJwt` is selected, the API refuses to start without a non-empty Authority and Audience. This prevents an accidental production fallback to the development identity.

## Homologation transition

The current M0 homologation station continues using the explicitly configured `Development` mode so the proven API → Agent route remains repeatable. Before accepting shared identity in homologation, set its mode to `SharedJwt` and provide the real issuer and audience through environment variables or a local, untracked configuration file.

## Out of scope

This contract does not implement MDS Core identity, a login screen, account administration, password recovery, social login or any product/design feature.
