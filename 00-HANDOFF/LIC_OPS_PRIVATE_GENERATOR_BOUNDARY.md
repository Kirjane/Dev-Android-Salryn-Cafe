# LIC-OPS — Private Generator Boundary (POS side)

**Status:** Spec only. No private key. No generator code in Salryn POS.

## Boundary

| Lives in Salryn POS | Lives outside POS (`Kirch-Nairu/Salryn.LicensingOps` or equivalent private tool) |
|---------------------|----------------------------------------------------------------------------------|
| Public verification key | Private signing key |
| Verifier + local store | License issuance CLI/UI |
| Host/Terminal validation | Customer issuance records |
| Manual activation UX | Audit trail of issued LicenseIds |

## Rules

1. Private signing material must **never** enter the Salryn POS repository.
2. POS verifies offline only; it does not call cloud activation for beta.
3. Generator must produce schema version **2** documents matching `LocalLicenseSignatureVerifier.BuildSigningPayload`.
4. Host licenses include `LicenseType=Host`, `AllowedLanTerminals`, machine binding, StoreId, ApplicationId=`Salryn.POS`.
5. Terminal licenses include `LicenseType=Terminal`, `HostLicenseId`, `TerminalId`, machine binding; **no** `AllowedLanTerminals`.
6. Beta contact for manual requests: `salryn.info@gmail.com`.

## Payload fields signed (must match POS)

LicenseId, CustomerName, Edition, IssuedAt, ExpiresAt, MachineBinding, Features, SchemaVersion, ApplicationId, StoreId, AllowedLanTerminals, SupportExpiresAt, UpdatesUntil, LicenseType, HostLicenseId, TerminalId.

Signature algorithm: RSA PKCS#1 v1.5 + SHA-256 over UTF-8 canonical JSON payload (signature field excluded).
