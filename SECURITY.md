# Security Policy – The Cursed Commodore

## Reporting Security Vulnerabilities

🔒 **NEVER** open a public GitHub issue for security vulnerabilities.

Instead:

1. **Do NOT** disclose the vulnerability publicly
2. **Email** the maintainers with:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if you have one)
3. **Wait** for confirmation (usually within 48 hours)
4. **Allow** time for a patch (typically 7-14 days)
5. **Coordinate** public disclosure with the maintainers

---

## Security Features in This Codebase

This bot implements multiple security hardening measures:

### 1. **Fail-Fast Token Validation**
- Missing bot token throws immediately
- Prevents silent failures and zombie processes
- Program exits with code 1 on startup failure

### 2. **Metadata-Only Logging**
- No sensitive user data in logs
- Only metadata tracked (user IDs, timestamps, message length)
- PII redacted across all debug output

### 3. **Fail-Close Error Handling**
- Security exceptions halt message processing
- No silent failure swallowing
- Explicit error logging for all security events

### 4. **DoS Protection**
- **Global Backpressure**: 120 concurrent message slots
- **Per-User Rate Limiting**: 6 commands per 10 seconds
- **Sliding Window Algorithm**: Accurate rate limit tracking

### 5. **Atomic File Persistence**
- Thread-safe JSON writes using temp files + atomic replace
- Per-file locking prevents corruption
- All data operations are atomic

### 6. **Permission Guards**
- Sensitive commands protected with role/owner checks
- Admin-only operations require verification
- Owner commands have additional confirmation steps

### 7. **Input Validation**
- All user inputs validated before processing
- Regex patterns for expected formats
- Type-safe command parameters

### 8. **Whitelisting Architecture**
- Server whitelisting (allowed guilds only)
- User whitelisting for sensitive features
- Invite code validation with expiry

---

## Known Security Considerations

### What This Bot DOES Protect Against

✅ Token exposure (fail-fast validation)
✅ Unauthorized access (permission guards)
✅ Denial of Service attacks (rate limiting + backpressure)
✅ Data corruption (atomic writes)
✅ Silent failures (explicit error handling)
✅ Lateral movement (permission checks)
✅ Spam abuse (per-user throttling)

### What This Bot DOESN'T Protect Against

❌ Compromised Discord account (Discord responsibility)
❌ Malicious Discord API (Discord responsibility)
❌ Network-level attacks (TLS/Discord infrastructure)
❌ Host/server compromises (OS/hosting responsibility)

---

## Best Practices for Deployment

### Environment Variables
- ✅ Store all secrets in environment variables (not code)
- ✅ Use `.env` files locally (excluded from git)
- ✅ Use secure secret management in production (Docker secrets, etc.)
- ❌ Never commit `.env` to version control

### Bot Permissions
- ✅ Grant minimum required permissions
- ✅ Regularly audit bot roles in guilds
- ✅ Use permission overwrites for restricted channels
- ❌ Don't grant Administrator permission unless absolutely necessary

### Monitoring
- ✅ Monitor rate limit hits for suspicious patterns
- ✅ Log security events to a dedicated channel
- ✅ Watch for command abuse patterns
- ✅ Regular security audits

### Updates
- ✅ Keep Discord.NET updated
- ✅ Monitor security advisories
- ✅ Apply patches promptly
- ✅ Test updates in staging before production

---

## Vulnerability Disclosure Timeline

| Phase | Timeline | Action |
|-------|----------|--------|
| **Report** | Day 0 | Vulnerability reported to maintainers |
| **Confirm** | Day 1-2 | Maintainers confirm receipt and severity |
| **Fix** | Day 3-14 | Patch developed and tested |
| **Release** | Day 15 | Security patch released |
| **Disclose** | Day 15 | Public disclosure with CVE (if applicable) |
| **Credit** | Day 15 | Researcher credited in advisory |

---

## Security Contacts

For **security vulnerabilities only**:
- 📧 Report via private channels
- 🔐 Use PGP encryption if needed
- ⏰ Include timeline for disclosure

For **non-security issues**:
- 📝 Use GitHub Issues
- 💬 Use GitHub Discussions
- 📋 Check existing issues first

---

## Dependencies

### Discord.NET Security
- Uses official Discord.NET library (actively maintained)
- Regular security updates included
- No known critical vulnerabilities at release time

### .NET Runtime Security
- Built on .NET 10.0 LTS (long-term support)
- Automatic security updates
- Follows Microsoft security guidelines

---

## Compliance

This bot follows best practices from:
- 🛡️ OWASP Top 10
- 📊 CWE/SANS Top 25
- 🔐 Microsoft Secure Coding Guidelines
- 💻 Discord Bot Security Best Practices

---

## Questions?

For security concerns:
- ✉️ Contact maintainers privately
- 🔍 Review the code – transparency is key
- 💬 Ask in discussions (for non-sensitive topics)

**Remember:** Security is not a feature, it's a responsibility. 
Treat this codebase with care. ⚓🔒

*Fair winds and following seas – in a secure harbor.* 🏴‍☠️

---

**Remember:** Security is not a feature, it's a responsibility. 
Treat this codebase with care. ⚓🔒
