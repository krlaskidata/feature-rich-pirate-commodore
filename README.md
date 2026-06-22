# The Cursed Commodore

**Advanced Discord Bot Architecture & Security Reference**

A professional, fully-hardened Discord bot showcasing enterprise-grade security, moderation, economy systems, and community management – all built with C# and Discord.NET.

---

## Purpose

This repository serves as an **architectural reference** for building production-ready Discord bots. Study the patterns, learn from the security implementations, and understand how to structure a complex bot application.

**This is NOT a starter template** – it is a reference codebase for educational purposes.

---

## Features

### Core Systems
- **Security & Moderation** – Hardened permission guards, fail-close error handling
- **Economy System** – Progressive ranking, currency management, player progression
- **XP & Leveling** – Voice time tracking, message XP, automatic role assignment
- **Ticket System** – Guild support channel management with atomic persistence
- **Birthday Announcements** – Automated event scheduling

### Advanced Architecture
- **DoS Protection** – Rate limiting (6 commands/10s) + global backpressure (120 slots)
- **Atomic File Persistence** – Thread-safe JSON writes preventing corruption
- **Security Services** – Centralized handling of malicious users and appeals
- **Fail-Fast Validation** – Immediate exception throwing on critical failures
- **Metadata-Only Logging** – PII redaction in debug output

### Community Features
- Themed commands and responses
- Community-based games and challenges
- Event management and lore integration

---

## Project Structure

```
feature-rich-pirate-commodore/
├── Modules/
│   ├── Bot.cs                          # Core orchestration & message routing
│   ├── Program.cs                      # Entry point with fail-fast exception handling
│   ├── MessageProtectionService.cs     # DoS mitigation & rate limiting
│   ├── AtomicFileStore.cs              # Thread-safe JSON persistence
│   ├── SecurityCommands.cs             # Moderation & security
│   ├── TicketCommands.cs               # Support ticket system
│   ├── EconomyCommands.cs              # Currency & ranking
│   ├── XPSystem.cs                     # Experience & leveling
│   ├── BirthdayCommands.cs             # Birthday system
│   ├── VoiceCommands.cs                # Voice channel management
│   └── [Other specialized modules]
├── PiratBot.csproj                     # Project configuration
└── PiratBot.sln                        # Solution file
```

---

## Security Architecture

### 10 Critical Vulnerabilities Addressed

1. **Token Fail-Fast** – Missing credentials throw immediately
2. **Environment-Based Config** – All sensitive values externalized
3. **PII Redaction** – Logs contain only metadata
4. **Fail-Close Error Handling** – Security exceptions halt pipeline
5. **Rate Limiting** – Per-user (6/10s) + global (120 slots) protection
6. **Permission Guards** – Sensitive commands require verification
7. **Thread-Safe Persistence** – Atomic writes with per-file locking
8. **User Validation** – Whitelist-based access control
9. **DoS Mitigation** – Semaphore-based backpressure + rate limiting
10. **Exit Code Handling** – Program exits with code 1 on failure

### Key Implementation Patterns

- **MessageProtectionService** – Centralized DoS protection
- **AtomicFileStore** – Prevents JSON corruption under concurrent load
- **SecurityService** – Fail-close architecture for policy enforcement
- **Type-Safe Nullability** – C# 8.0+ null-safety enabled

---

## Getting Started

### Prerequisites
- .NET 10.0 SDK
- Visual Studio 2022 or VS Code
- Discord.NET 3.11.0 (via NuGet)

### Code Exploration
1. Clone this repository
2. Open `PiratBot.sln` in Visual Studio
3. Explore the module structure
4. Study security patterns in `MessageProtectionService.cs` and `AtomicFileStore.cs`

### Running (For Study Only)
```bash
# You will need valid Discord credentials to run
# This is intentionally restricted for reference use
dotnet build
dotnet run
```

**Note:** This bot requires proper Discord bot credentials and environment setup. 
Refer to [Discord.NET Documentation](https://docs.discordnet.dev/) for guidance.

---

## 🏗️ Architecture Principles

### Design Patterns
- **Service Locator Pattern** – Centralized dependency injection
- **Command Pattern** – Discord.NET command framework
- **Repository Pattern** – JSON-based data persistence
- **Observer Pattern** – Event-driven message handling

### Security Principles
- **Principle of Least Privilege** – Minimal permissions per command
- **Fail-Close Over Fail-Open** – Security exceptions halt execution
- **Defense in Depth** – Multiple layers of validation
- **Immutable Configuration** – Environment-based settings (no code changes)

---

## 📊 Key Technologies

| Component | Technology | Purpose |
|-----------|-----------|---------|
| **Framework** | Discord.NET 3.11.0 | Discord API interaction |
| **Language** | C# 10+ | Type-safe, modern syntax |
| **Runtime** | .NET 10.0 | High performance |
| **Data** | JSON (File-based) | Simple persistence |
| **Concurrency** | SemaphoreSlim | Thread-safe operations |
| **Locking** | ConcurrentDictionary | Per-resource synchronization |

---

## 📖 Learning Resources

### Discord.NET
- [Official Docs](https://docs.discordnet.dev/)
- [API Reference](https://docs.discordnet.dev/api/)

### C# Security & Patterns
- [Microsoft C# Documentation](https://docs.microsoft.com/en-us/dotnet/csharp/)
- [Secure Coding Guidelines](https://docs.microsoft.com/en-us/dotnet/standard/security/)

### Discord Bot Development
- [Discord Developer Portal](https://discord.com/developers/)
- [Best Practices Guide](https://discord.com/developers/docs/topics/oauth2)

---

## ⚠️ Important Notes

### What This Is NOT
- ❌ A copy-paste starter template
- ❌ Open source for commercial use
- ❌ Available for redistribution
- ❌ Licensed under permissive open source terms

### What This IS
- ✅ An architectural reference
- ✅ Educational code study material
- ✅ Security best practices showcase
- ✅ Professional bot design patterns

### Licensing
This code is **proprietary** and provided for educational reference only.
See [LICENSE](LICENSE) for full terms.

You may study, learn from, and discuss the code – but you cannot copy, modify, 
or redistribute it. If you reference specific patterns, proper attribution is required.

---

## 🏴‍☠️ About The Cursed Commodore

Built by a crew of experienced developers, The Cursed Commodore represents years 
of Discord bot architecture refinement, security hardening, and community feedback.

Every line of code serves a purpose. Every design decision is intentional.

**Savvy?** ⚓

---

## Support & Questions

For questions about the **architecture or design patterns**:
- Review the code comments
- Check the inline documentation
- Study the security implementations

For **licensing or usage inquiries**:
- Review the LICENSE file
- Contact the repository maintainers

### Discord Support Server

Have questions? Need help? Join our support community:

**[Join Support Discord Server](https://discord.gg/9mPqrZdSH3)**

Connect with other developers, share insights, and get help from the community.

---

## 🌊 Fair Winds and Following Seas

*This codebase represents the collective knowledge of a dedicated team. 
Respect the work, learn from it, and build something great of your own.* ⚓🏴‍☠️

**Remember: Great developers don't copy – they learn and create.** 💀

---

### Version
**The Cursed Commodore v1.0** – June 2026  
*A reference implementation of enterprise Discord bot architecture.*
