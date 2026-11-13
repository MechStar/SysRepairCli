Act as an expert multi-agent orchestrator executing a rapid 60-minute live interview implementation for a native .NET 10 cross-platform CLI tool: `SysRepairCli`.

### Context & Goal
- Target Product: An independent, open-source host diagnostic & repair CLI for Vast.ai (a decentralized GPU marketplace).
- Primary Objective: Eliminate onboarding friction for GPU hosts (from local under-desk setups to off-premise server racks via SSH).
- Key Capabilities: Auto-diagnose and remediate common onboarding blockers (e.g., NVIDIA driver state, CUDA compatibility, IOMMU/VT-d settings, Docker runtime, open ports, SSH key access, network throughput).
- Target Directory: `/Users/denispavlov/Git/SysRepairCli/`

### Time Constraint & Methodology
- Strictly optimized for a 60-minute MVP delivery.
- Prioritize speed, working execution, and brevity over production complexity.
- Follow a strict Test-Driven Development (TDD) workflow: test specifications and failing stubs MUST be established before implementation.

### Execution Instructions
Immediately execute Phase 1 and generate all corresponding files directly in `/Users/denispavlov/Git/SysRepairCli/`. Do not wait for prompt confirmation between phases. When finished, use this repo https://github.com/MechStar/SysRepairCli to push all the code and make sure to include an appropriate .gitignore and README.md.

Execute the multi-agent workflow sequentially:

1. Phase 1 (Senior Technical PM Agent): Generate `PRD.md` defining lightweight MVP scope, core diagnostic/repair feature requirements, and strict MoSCoW priorities.
2. Phase 2 (Principal .NET 10 Architect Agent): Read `PRD.md` and generate `ARCHITECTURE.md` outlining Clean Architecture project layout, SSH execution abstraction layer, diagnostic interfaces, and local SQLite state schema.
3. Phase 3 (Senior QA / TDD Lead Agent): Read `PRD.md` and `ARCHITECTURE.md`. Initialize `SysRepairCli.Tests` (xUnit, Moq, FluentAssertions) and generate initial failing unit test stubs for diagnostic engines and script execution wrappers.
4. Phase 4 (Agile Delivery Lead Agent): Read all files and generate `TODO.md` with a phased, TDD-compliant checklist mapped to specific engineering roles for rapid execution.
5. Phase 5 (Senior .NET 10 CLI Developer Agent): Read `TODO.md` and `ARCHITECTURE.md`. Implement `SysRepairCli` domain logic, SSH/local process wrappers, Spectre.Console UI, and make all unit tests pass (Green phase). Update `TODO.md`.
6. Phase 6 (Staff Security & Code Auditor Agent): Review the codebase for command injection risks in SSH/process runners, sudo/privilege escalation boundaries, and input sanitization. Provide a security sign-off in `TODO.md`.
7. Phase 7 (Git & Release Delivery Agent): When finished, initialize/configure the repository for `https://github.com/MechStar/SysRepairCli`, add a standard .NET `.gitignore`, write a comprehensive yet concise `README.md` following industry best practices (including project overview, tech stack, installation instructions, quick-start usage, and configuration guidelines), and push all code.

Present a brief roadmap of the execution plan, then immediately begin Phase 1.
