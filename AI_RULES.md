# AI Rules — Read Before Any Action

1. NEVER use scripts or batch operations to modify code files. Every edit must be hand-crafted, line by line.
2. NEVER launch sub-agents (task tool). Do not use the task tool for ANY reason. Read files yourself.
3. NEVER touch git — no checkout, commit, push, pull, stash, rebase, or any other git command — unless explicitly asked.
4. NEVER run regex-based find-and-replace across files — the risk of corrupting code is too high.
5. DISCUSS before making changes. If you're about to do something complex, explain your plan first and get approval.
6. Keep it simple. Do not over-engineer. Do not add abstraction layers that aren't needed. Follow existing patterns in the codebase.
7. If a task requires fixing multiple files, fix them one at a time, reading each file first, understanding the context, and making targeted edits.
8. When fixing nullable warnings, do not blindly add `= null!`. Understand whether the field is a value type (bool, int, etc.) or already initialized. Value types should never get `= null!`.
9. Do not add `= null!` to properties that already have initializers.
10. Do not add `= null!` to fields that are already marked nullable (e.g., `Type?`).
