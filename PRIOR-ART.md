# Prior art, and where we diverge

This file is for one moment: when someone says "but Microsoft says the opposite." Some of these rules ride the platform's own direction. A few invert canonical .NET on purpose. Know which is which, because pretending the bans are uncontroversial loses the argument. The heresies are defensible, but only if you defend them.

No published artifact does "C# the way Rust, Go, and Zig do it" as a single ruleset. The pieces each have prior art on one side or the other. The curation and the thesis are the contribution.

## With the grain

These are not contrarian. They line up with shipped Microsoft analyzers and the runtime team's trajectory. Lead with them, because they buy the credibility the heresies spend.

| Rule | Already backed by |
|------|-------------------|
| Concurrency and async discipline | David Fowler's [AsyncGuidance](https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md), and we reuse Microsoft's own `VSTHRD` analyzers |
| Sealed by default | [CA1852](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1852) (seal internal types, .NET 7) and CA1515 (.NET 9); the runtime seals for devirtualization |
| readonly struct, value-type equality | CA1815, IDE0250, IDE0251, all Microsoft's |
| No reflection, no dynamic, AOT-clean | Exactly where Native AOT and source generators are taking the platform |
| Construct fully formed, no builders | `required` (C# 11) was added for this |

On async we are not just aligned, we are downstream of the canonical source. Fowler's guidance says async-all-the-way, no `async void`, `ValueTask` on hot paths, never block a pool thread. That is our concurrency section, near verbatim.

## Against the grain

### Public fields, no properties

The heresy, and the one with a shipped analyzer pointed straight at it. [CA1051 "Do not declare visible instance fields"](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1051) flags public fields and tells you to use a property. The [Framework Design Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/property) and [Properties vs Fields](https://devblogs.microsoft.com/vbteam/properties-vs-fields-why-does-it-matter-jonathan-aneja/) agree.

Their arguments, and our answer:

- "A field cannot be part of an interface contract." True. We do not put data on interfaces; data is records and structs, behaviour is interfaces. The case does not arise.
- "Field to property is a breaking ABI change." True for public library APIs. Irrelevant inside an application boundary, which is where this style lives. For a published library, carve it out.
- "Serializers and databinding default to properties." Half true. System.Text.Json binds through the constructor, which our DTOs have; set `IncludeFields` once. Databinding (WPF/MVVM `INotifyPropertyChanged`) genuinely needs properties. That is a real cost and we accept it: this style is not for XAML view models.

Nobody mainstream publishes "public fields only." This is pure Rust and Zig: a field cannot lie about whether access runs code. Own that. Do not soft-sell it as a minor preference.

### Errors are values, not exceptions

Microsoft's [best practices for exceptions](https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions) is exception-centric, so we diverge from the platform's stated guidance. But the community is on this side: LanguageExt, ErrorOr, CSharpFunctionalExtensions all sell the Result pattern, and C# 15 `union` is Microsoft itself moving toward exhaustive sum types. The platform is drifting our way; it has not arrived.

### No MediatR, no AutoMapper, no reflection dispatch

This is where we are the exact opposite of the enterprise-traditional mainstream. See SSW below.

## SSW, the foil

[SSW Rules](https://github.com/SSWConsulting/SSW.Rules.Content) is the cleanest counterpoint to this skill. Their flagship [Clean Architecture Template](https://adamcogan.com/2024/05/09/ssw-clean-architecture-template/) ships MediatR, CQRS, AutoMapper, FluentValidation, and EF Core, and they have a dedicated rule selling [the Mediator pattern with CQRS](https://www.ssw.com.au/rules/use-the-mediator-pattern-with-cqrs) as the "pit of success."

That stack reads like a list of our bans:

- MediatR is the "runtime dispatch magic" the DI ban names by hand.
- AutoMapper is the reflection-and-attribute magic ban.
- Properties everywhere, exceptions, layered inheritance: the OOP defaults this skill exists to reject.

The overlap with SSW is thin: async discipline and nullable reference types. Everything structural is opposed. SSW is traditional-Microsoft-stack distilled and well argued; honest-csharp is the anti-SSW. Read their rules to know the position you are arguing against, not to adopt it.

## Bottom line

Two-thirds of this is the modern platform direction with analyzer support behind it. One-third is deliberate heresy with the Rust and Go flavour. The strategic order follows from that: lead with the aligned rules, so the heresies land as considered rather than ignorant. And on public fields, remember you are overriding a shipped analyzer, not just a style preference. That is the one to have memorized.
