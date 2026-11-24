# Tool reference

All tools return JSON text. Failures come back as an MCP tool result with `isError: true` and a message.
"Mutating" tools change Visual Studio state and are hidden when `DTEMCP_READONLY=1`.
Lines and columns are 1-based. `frameIndex` and breakpoint `index` are 0-based.
The Visual Studio instance is chosen as described in the README (the debugging one wins, `DTEMCP_PID` forces one).

## Debugger

| Tool | Mutating | Arguments | Returns |
|---|---|---|---|
| `debugger_status` | no | none | `instances[]` (name, processId, solution, mode), `selected`, `mode` (`design`, `break`, `run`), and `currentFrame` in break mode |
| `debugger_call_stack` | no | none | up to 50 frames: index, function, module, file, line. Needs break mode |
| `debugger_locals` | no | `frameIndex` (default 0) | up to 100 locals: name, type, value as the debugger shows it, not expanded. Needs break mode |
| `debugger_exception` | no | none | `hasException`, and when true `type`, `message`, `stackTrace`. Evaluates `$exception.Message` in the debuggee. Needs break mode |
| `debugger_breakpoints` | no | none | breakpoints: index, file, line, function, enabled, condition, hitCount |
| `debugger_control` | yes | `action` (required): `start`, `continue`, `start_without_debugging`, `break`, `stop`, `restart`, `step_into`, `step_over`, `step_out`, `run_to_cursor` | the new `debugger_status`. Steps wait until they finish |
| `debugger_evaluate` | yes | `expression` (required), `frameIndex`, `timeoutMs` (default 2000) | `expression`, `isValid`, `type`, `value`. Assignments such as `x = 5` run in the debuggee. Needs break mode |
| `debugger_breakpoint_add` | yes | `file` (full path, required), `line` (required), `condition` | the breakpoint list |
| `debugger_breakpoint_remove` | yes | `index`, or `all=true` | the remaining breakpoints |
| `debugger_breakpoint_enable` | yes | `index` (required), `enabled` (required) | the breakpoint list |

## Editor

| Tool | Mutating | Arguments | Returns |
|---|---|---|---|
| `vs_documents` | no | none | open documents: path, saved, language, readOnly, active |
| `vs_selection` | no | none | active document, caret or selection start and end, `isEmpty`, selected text (capped at 5000 characters) |
| `vs_read_document` | no | `path` (required), `startLine`, `endLine`, `lineNumbers` | `text` plus `source` (`editor` when the file is open, so unsaved edits count, otherwise `disk`), `totalLines`, `startLine`, `endLine`, and `nextLine` when the output was capped at about 100000 characters |
| `vs_write_document` | yes | `path` and `text` (required), `mode`: `replace_all` (default), `replace_lines`, `insert`; `startLine`, `endLine`, `save` | the document's state. One undo step. `replace_all` creates a missing file on disk. `replace_lines` replaces `startLine..endLine` (a trailing newline in `text` is ignored; empty text deletes the lines). `insert` goes before `startLine`; a line past the end appends. Not saved unless `save=true` |
| `vs_open_document` | yes | `path` (required), `line`, `column` | the document's state; it becomes active and the caret moves |
| `vs_close_document` | yes | `path` (required), `save` (default true) | `closed`. `save=false` discards unsaved changes |
| `vs_save` | yes | `path` (optional) | open documents. Without `path` it runs File.SaveAll |

## Solution and projects

| Tool | Mutating | Arguments | Returns |
|---|---|---|---|
| `vs_solution` | no | none | `isOpen`, path, projects (name, uniqueName, fullName, kind, isSolutionFolder), startupProjects, activeConfiguration, activePlatform |
| `vs_project_items` | no | `project` (name or unique name, required) | tree paths and full paths of items, capped at 500, with `truncated` |
| `vs_project_properties` | no | `project` (required), `names[]` | property name and value; without `names`, the first 300 properties |
| `vs_solution_open` | yes | `path` (required) | `vs_solution` |
| `vs_solution_close` | yes | `save` (default true) | `vs_solution` |
| `vs_project_item_add` | yes | `project`, `path` (required) | `added`. Needed for old-style projects; SDK-style projects include files by themselves |
| `vs_project_item_remove` | yes | `project`, `path` (full path or tree path, required), `deleteFromDisk` (default false) | `removed`, `deletedFromDisk`. **`deleteFromDisk=true` deletes the file permanently** |
| `vs_project_property_set` | yes | `project`, `name`, `value` (required) | the property's new value. Call `vs_save` afterwards to write the project file |

## Build

| Tool | Mutating | Arguments | Returns |
|---|---|---|---|
| `vs_build` | yes | `action`: `build` (default), `rebuild`, `clean`, `cancel`; `configuration`, `platform`, `project` (unique name, `build` only), `wait` (default true) | `state` (`notStarted`, `inProgress`, `done`), `failedProjects`, first 50 errors. With `wait=false` it returns at once; poll `vs_build_status` |
| `vs_build_status` | no | none | the same result as `vs_build` without starting anything |
| `vs_errors` | no | `severity`: `error`, `warning`, `message`; `max` (default 50) | Error List items: severity, description, file, line, column, project |

## Commands, output and the object model

| Tool | Mutating | Arguments | Returns |
|---|---|---|---|
| `vs_execute_command` | yes | `command` (required, canonical name such as `Edit.FormatDocument`, `File.SaveAll`, `Build.BuildSolution`), `args` | `executed` |
| `vs_find_commands` | no | `filter` (required, case-insensitive substring) | up to 100 commands: name, available |
| `vs_output` | no | `pane` (for example `Build`, `Debug`), `maxChars` (default 20000) | without `pane`, the pane names; with it, the end of that pane's text |
| `vs_invoke` | yes | `path` (required), `operation`: `get` (default) or `set`, `value`, `depth` (0 to 3, default 1) | the member's value. COM objects come back as type plus Name, FullName, Kind and Count and, with depth above 0, their first 50 items |

### `vs_invoke` paths

A path starts at `DTE`. Members are separated by dots. Method calls and indexers take literals:
`"text"` (with `\"`, `\\`, `\n`, `\t` escapes), integers, decimals, `true`, `false`, `null`.

    Solution.Projects.Item(1).Name
    Documents.Item("C:\\src\\a.cs").Save()
    ActiveWindow.Caption
    Windows.Count

`operation=set` assigns `value` to the last member. Nested calls such as `Item(Other(1))` are not supported.

## Limits

Outputs are capped (frames 50, locals 100, breakpoints 100, project items 500, Error List scan 2000, document text about
100000 characters, values 500 characters) so one call cannot flood the agent's context. Capped results say so.
