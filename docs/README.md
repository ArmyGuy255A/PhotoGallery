# PhotoGallery Architecture Docs

This is the canonical architecture documentation set for PhotoGallery. Read these before proposing changes that touch the processing pipeline, runtime settings, workers, storage, or the admin queue.

## Reading order

1. [Architecture/00-Overview.md](Architecture/00-Overview.md). Start here. Names the runtime processes, the high level boundaries, and the key principles every other doc references.
2. [Diagrams/01-High-Level-Architecture.md](Diagrams/01-High-Level-Architecture.md). One picture of how the pieces fit.
3. [Architecture/01-Processing-Pipeline.md](Architecture/01-Processing-Pipeline.md). The end-to-end photo lifecycle from upload to download.
4. [Architecture/02-AdminJob-Queue.md](Architecture/02-AdminJob-Queue.md). How admin clicks and routine maintenance become worker work.
5. The rest in the order that matches your change.

## Document map

### `Architecture/`

Prose documents. One topic per file. Each one names the source-of-truth file in the codebase so the doc and the code can be cross-checked.

| Doc                                                                  | Topic                                                              |
| -------------------------------------------------------------------- | ------------------------------------------------------------------ |
| [00-Overview.md](Architecture/00-Overview.md)                        | Runtime processes, top-level components, principles                |
| [01-Processing-Pipeline.md](Architecture/01-Processing-Pipeline.md)  | Photo lifecycle: upload, derive 7 variants, URL cache, reconcile   |
| [02-AdminJob-Queue.md](Architecture/02-AdminJob-Queue.md)            | AdminJob entity, dispatcher routing, scheduler, deduplication      |
| [03-Service-Patterns.md](Architecture/03-Service-Patterns.md)        | Adding a service, lifetime choice, DI registration                 |
| [04-Runtime-Settings.md](Architecture/04-Runtime-Settings.md)        | SettingsCatalogue + ISettingsResolver + hot-reload semantics       |
| [05-Worker-Heartbeats.md](Architecture/05-Worker-Heartbeats.md)      | Heartbeat write, read, prune, alive thresholds                     |
| [06-Storage-Abstraction.md](Architecture/06-Storage-Abstraction.md)  | IStorageProvider, factory, MinIO vs Azure Blob matrix              |

### `Diagrams/`

Each file is a single Mermaid diagram plus a few sentences of context. They are the canonical set and should be regenerated whenever the code that backs them changes.

| Diagram                                                                  | Type            |
| ------------------------------------------------------------------------ | --------------- |
| [01-High-Level-Architecture.md](Diagrams/01-High-Level-Architecture.md)  | Component (C4-ish) |
| [02-Database-Schema.md](Diagrams/02-Database-Schema.md)                  | ER              |
| [03-Sequence-Photo-Upload.md](Diagrams/03-Sequence-Photo-Upload.md)      | Sequence        |
| [04-Sequence-Photo-Download.md](Diagrams/04-Sequence-Photo-Download.md)  | Sequence        |
| [05-Sequence-Admin-Job-Flow.md](Diagrams/05-Sequence-Admin-Job-Flow.md)  | Sequence        |
| [06-Sequence-Worker-Heartbeat.md](Diagrams/06-Sequence-Worker-Heartbeat.md) | Sequence     |
| [07-DFD-Photo-Upload.md](Diagrams/07-DFD-Photo-Upload.md)                | Data flow       |
| [08-DFD-Photo-Download.md](Diagrams/08-DFD-Photo-Download.md)            | Data flow       |

## What lives elsewhere

* `MEMORY.md`. Append-only log of architectural and operational lessons. Read it for context on why decisions were made. Do not copy that content here, link to it instead.
* `Documentation/Architecture/`. Older doc set. Treat as historical, not authoritative. If you find a contradiction with this set, this set wins. Migrate the useful bits over time.
* `Architecture/` (top level). Older doc set, same story.
* `docs/decisions/`. Architecture Decision Records (ADR format). One per significant decision. Numbered.

## How to update

* Code change in scope of a diagram. Refresh the diagram in the same PR.
* New cross-cutting concern (new queue, new cache, new worker). Add a new doc and link it from this index.
* Doc-only change. Keep PRs small. One doc per PR is fine.
* All Mermaid renders should be validated before merge. The `mermaid-diagram-curator` skill describes how.

## Style

* No em-dashes. Use a colon, comma, or period.
* Short sentences. Plain words.
* Tables for structured comparisons, not bullet lists.
* Every claim should cite a file or commit so the next reader can verify.
