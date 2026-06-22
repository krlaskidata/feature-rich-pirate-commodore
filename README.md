# The Cursed Commodore

Advanced Discord bot for community management, security, and economy systems — fully free and ready to use.
Built with C# and Discord.NET for production-grade reliability.

---

## Use the Bot (Free & Online 24/7)

Add The Cursed Commodore to your Discord server:

https://discord.com/oauth2/authorize?client_id=1448582334315237387&permissions=8&integration_type=0&scope=bot

Simply click the link and invite the bot to your server. No setup required to start using features.

**Before using the code:** Please read the [LICENSE](LICENSE) — code reproduction is restricted.

---

## Commands by Category

### Security & Moderation

```
?setsecuritymod              Set up security system (creates log channel)
?status                      Show security system status
?disable                     Disable security system
?cleanup-now                 Delete all messages in current channel
?cleanup-intervall <ch> 2h   Auto-delete messages every 2 hours
?delcleanup-intervall <ch>   Remove auto-cleanup from channel
```

Features: Log-channel event tracking, auto-cleanup, message management, server protection tools.

---

### Ticket System

```
?ticket-setup                Interactive setup wizard
?set-support @role           Set support role for tickets
?ticket-close                Close current ticket
?ticket-transcript           Generate ticket transcript
?ticket-status               Show ticket system status
?del-ticket-system           Remove ticket configuration
```

Features: Interactive ticket creation, support roles, transcript generation, categorization.

---

### Voice Channels

```
?voicesetup                  Full voice system setup
?create <category-id>        Create Join-to-Create channel
?voicename CoolRoom          Rename your voice channel
?voicelimit 5                Set user limit (0 = unlimited)
?voiceprivate                Make channel private
?voicepublic                 Make channel public
?voicestats                  View voice activity statistics
?voice-cleanup               (Admin) Remove empty voice channels
```

Features: Dynamic voice creation, private/public rooms, user limits, activity tracking.

---

### XP & Leveling

```
?run-xp-setup                Setup XP system (creates ranks)
?xp                          View your XP and rank
?xp @user                    View another user's XP
?xp-give @user 500           (Admin) Grant XP
?set-rank-channel #ch        Set level-up notification channel
?rank-channel-off            Disable level-up notifications
?rank-channel-status         Show current rank channel
?remove-xp-setup             Disable XP system
```

Features: 202-tier military ranking, chat and voice XP, automatic role assignment, notifications.

---

### Economy & Currency

```
?daily                       Collect daily credits
?balance                     View wallet and bank balance
?deposit 500                 Deposit money into bank
?withdraw all                Withdraw from bank
?pay @user 200               Send money to another user
?rob @user                   Attempt to steal from wallet (40% success)
?coinflip 100 heads          Bet on coin flip
?dice 200 15 20              Roll dice and guess number
?slots 500                   Spin slot machine with jackpot
```

Features: Wallet and bank system, daily rewards, transfers, gambling games, progression tracking.

---

### Birthday System

```
?birthdayset 25.12.1995      Save your birthday
?mybirthdayinfo              View your saved birthday
?birthdaychannel #channel    (Admin) Set announcement channel
?birthdaylist                (Admin) View all birthdays
?birthdayremove              Remove your birthday
```

Features: Birthday tracking, automatic announcements, celebration events, member lists.

---

### Verification

```
?verify-setup #ch @role           Set up verification
?verify-setup #ch @role captcha   Enable CAPTCHA verification
```

Features: Gate-based access, optional CAPTCHA, automatic role assignment.

---

### Giveaway System

```
?gcreate 1h 1 Discord Nitro   Create 1-hour giveaway
?gend <message-id>            End giveaway early
?greroll <message-id>         Re-roll winners
?glist                        List active giveaways
```

Features: Reaction-based entry, automatic selection, re-roll capability, multiple winners.

---

### Bump Reminders

```
?bumpreminder on              Enable bump reminders
?bumpreminder off             Disable bump reminders
?bumpstatus                   Check reminder status
```

Features: Automated Disboard reminders, 2-hour intervals, ready-to-bump notifications.

---

### Utilities

```
?ping                         Check bot latency
?info                         Show bot information
?sendit <msg-id> to <ch-id>   Forward message to channel
```

---

## Security Architecture

This bot implements enterprise-grade security hardening:

- Token validation with fail-fast exception handling
- Environment-based configuration (no hardcoded secrets)
- PII redaction in logging (metadata only)
- Fail-close error handling for security operations
- Rate limiting: 6 commands per 10 seconds per user
- Global backpressure: 120 concurrent message slots
- Permission guards on sensitive commands
- Thread-safe atomic file persistence
- Whitelist-based access control
- Exit code handling on startup failure

---

## Getting Started (For Learning)

### Prerequisites
- .NET 10.0 SDK
- Visual Studio 2022 or VS Code
- Discord.NET 3.11.0 (via NuGet)

### Code Exploration
1. Clone this repository
2. Open `PiratBot.sln` in Visual Studio
3. Review Modules for architecture patterns
4. Study MessageProtectionService.cs and AtomicFileStore.cs

### Running
```bash
dotnet build
dotnet run
```

Note: Valid Discord credentials required.

---

## Key Technologies

- Framework: Discord.NET 3.11.0
- Language: C# 10+
- Runtime: .NET 10.0
- Data: JSON (file-based persistence)
- Concurrency: SemaphoreSlim, ConcurrentDictionary

---

## Important Notes

### What This Is NOT
- A copy-paste starter template
- Open source for commercial use
- Available for redistribution
- Licensed under permissive terms

### What This IS
- An architectural reference
- Educational study material
- Security best practices showcase
- Professional bot design patterns

### Licensing

This code is proprietary and provided for educational reference only. See [LICENSE](LICENSE).

You may study and learn from the code – but cannot copy, modify, or redistribute it.

---

## Support & Questions

For questions about architecture or design patterns:
- Review code comments
- Check inline documentation
- Study security implementations

For licensing inquiries:
- Review the LICENSE file
- Contact maintainers

### Community

Join our Discord support server:

https://discord.gg/9mPqrZdSH3

Connect with developers, share insights, and get help.

---

## Version

The Cursed Commodore v1.0 – June 2026

A reference implementation of enterprise Discord bot architecture.

---

This codebase represents collective knowledge and years of refinement.
Respect the work, learn from it, build something original of your own.

Great developers learn and create – they don't copy.
