# WinForge domain glossary

**Tweak** — A reviewable, versioned declaration that describes desired Windows state and the typed operations used to detect, apply, verify, and roll it back.

**Operation** — One typed, validated state transition. It is never arbitrary shell text.

**Execution plan** — An ordered, compatibility-checked set of operations resolved from selected tweaks and profiles before elevation.

**Session** — One execution plan and its immutable original state, results, and rollback status.

**Journal** — The durable files for a session under ProgramData. A journal is written before the first mutation.

**Profile** — A named selection of tweak identifiers. A profile contains no duplicated operations.

**Preview** — A read-only calculation of exact proposed changes or deletions.

**Cleaner category** — An allowlisted set of roots and safety rules. It never follows reparse points.

**Worker** — The separately elevated executable that accepts only a signed execution plan created by the UI.
