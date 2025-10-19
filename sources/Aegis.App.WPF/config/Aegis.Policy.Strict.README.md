# 🛡️ AEGIS POLICY — BERNARDO COMPLIANCE LEVEL

**Version:** 1.0.0  
**Maintainer:** Bernardo Estacio Abreu  
**Mode:** *Sniper Audit — Enterprise-Grade Strictness*

---

## 🎯 Purpose

This configuration defines the **core governance model** of the Aegis Intelligent Evaluator System.  
It reflects Bernardo’s architectural philosophy of deterministic quality, absolute cohesion, and precision-driven enforcement.

Every parameter herein is designed to:
- Eliminate architectural drift.
- Prevent anemic and god-class anti-patterns.
- Enforce reproducible domain integrity.
- Ensure codebases remain maintainable, auditable, and scalable.

---

## ⚙️ Enforcement Philosophy

| Level | Definition | Aegis Behaviour |
|-------|-------------|-----------------|
| **Strict** | Violations trigger warnings or errors with remediation advice. | Immediate pattern-level feedback. |
| **Advisory** | Informational guidance for dynamic or scripting ecosystems. | Passive evaluation only. |
| **Sniper** | Hybrid strict mode with contextual awareness and zero false positives. | Enforced in this configuration. |

---

## 🧩 Evaluation Domains

| Category | Goal | Enforcement Mode |
|-----------|------|------------------|
| **Maintainability** | Ensure long-term clarity and documentation quality. | 🔒 *Strict* |
| **Complexity** | Prevent monolithic functions; promote atomic design. | 🔒 *Strict* |
| **Architecture** | Maintain DDD-style layer purity (`Api → App → Domain → Infra`). | 🔒 *Strict* |
| **Dependency Graph** | Detect spaghetti coupling and circular imports early. | 🔒 *Strict* |
| **Transactions** | Mandate explicit commit/rollback semantics across ecosystems. | 🔒 *Strict* |
| **Performance** | Promote async-first, non-blocking, scalable design. | 🔒 *Strict* |
| **Security** | Zero tolerance for secrets or weak crypto. | 🔒 *Strict* |
| **Design Patterns** | Enforce canonical patterns with static/dynamic intelligence. | 🔒 *Sniper* |
| **Anti-Patterns** | Detect God Classes, Anemic Domains, and circular logic. | 🔒 *Strict* |

---

## 🧠 Design Pattern Coverage

| Pattern | Enforced | Ruleset |
|----------|-----------|---------|
| **Repository** | ✅ | Requires interface, async consistency. |
| **Singleton** | ✅ | Thread-safe, lazy, single instance only. |
| **Factory** | ✅ | ≤ 3 instantiations per factory. |
| **Builder** | ✅ | Must expose fluent API + `Build()` method. |
| **Mediator** | ✅ (.NET only) | Supports both `MediatR` & `Franz.Common.Mediator`. |
| **Command** | ✅ | Requires matching handler and undo capability. |
| **Strategy** | ✅ | Interface required; no conditional switches. |
| **Observer** | ✅ | Must unsubscribe properly; no manual polling. |
| **Decorator** | ✅ | Must delegate via constructor injection. |
| **Facade** | ✅ | ≤ 4 dependencies, ≤ 8 public methods. |

---

## 💀 Anti-Pattern Detection

| Anti-Pattern | Threshold | Enforcement |
|---------------|------------|--------------|
| **God Class** | ≥ 20 methods OR ≥ 600 lines | 🔴 Error |
| **Anemic Domain** | ≥ 70% properties / ≤ 1 method | 🟠 Warning |
| **Circular Dependencies** | Depth > 8 | 🔴 Error |
| **Over-Dispatching Mediator** | > 10 calls per file | 🟠 Warning |

---

## 📦 Repository Hygiene

| Requirement | Rule |
|--------------|------|
| Governance Files | Must include `README.md`, `.editorconfig`, `LICENSE` |
| CI/CD Presence | `.github/workflows` or `azure-pipelines.yml` required |
| Large File Limit | 10 MB per file |
| TODO/FIXME Density | ≤ 3 per file |

---

## ⚡ Performance Constraints

| Rule | Description |
|-------|-------------|
| No blocking calls | (`Thread.Sleep`, `.Result`, `Task.Wait`) |
| Max nested loop depth | ≤ 3 |
| Max loop body length | ≤ 80 lines |
| Async I/O required | All file and network operations |
| Large collection init | Warn ≥ 1000 elements |

---

## 🔒 Security Constraints

- Detect hardcoded credentials and tokens.  
- Enforce secure cipher suites and SSL/TLS >= 1.2.  
- Warn on weak hashing algorithms (MD5, SHA1).  

---

## 🧰 Maintainability Metrics

| Metric | Threshold |
|---------|------------|
| Maintainability Index | ≥ 80 |
| Comment Density | ≥ 8% |
| Max File Length | ≤ 400 lines |
| Complexity Weight | 2.5 |
| Line Weight | 0.015 |

---

## 🧬 “Bernardo Style” Ethos

> *Precision is not severity — it’s respect for the craft.*  
>  
> This configuration ensures that every evaluator in Aegis operates with  
> **surgical intent**, enforcing quality without dogmatism.  
> Every rule is measurable, defensible, and aligned with enterprise-grade standards.

---

### ✅ Summary

- **Mode:** Sniper Audit  
- **Languages:** .NET (C#), Java, Python, TypeScript  
- **Purpose:** Unified architectural, performance, and design enforcement  
- **Philosophy:** Deterministic consistency → Predictable scalability  

---

> **Aegis Motto:**  
> “No architecture shall drift unobserved.”
