using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DteMcp.Mcp;
using DteMcp.Vs;

namespace DteMcp.Tools
{
    /// <summary>Read and edit documents through the editor buffers of Visual Studio (unsaved edits included, undo preserved).</summary>
    internal sealed class DocumentTools : IToolProvider
    {
        private const int MaxDocumentChars = 100000;
        private const int MaxSelectionChars = 5000;
        private const int NormalizeNewlines = 4; // vsEPReplaceTextOptions.vsEPReplaceTextNormalizeNewlines
        private const int SaveChangesYes = 1;    // vsSaveChanges
        private const int SaveChangesNo = 2;

        private readonly DteSession session;

        public DocumentTools(DteSession session)
        {
            this.session = session;
        }

        public IEnumerable<ToolDefinition> Tools()
        {
            SchemaProperty path = ToolSchema.String("path", "Full path of the file.");

            yield return ToolDefinition.ReadOnly("vs_documents", "Lists open documents with path, saved state, language and which one is active.", ToolSchema.Object(), _ => Documents());

            yield return ToolDefinition.ReadOnly(
                "vs_selection",
                "Active document, caret/selection position and selected text.",
                ToolSchema.Object(),
                _ => Selection());

            yield return ToolDefinition.ReadOnly(
                "vs_read_document",
                "Reads a file. Uses the editor buffer (including unsaved edits) when the file is open in Visual Studio, otherwise the file on disk. Optional 1-based line range; output is capped, 'nextLine' says where to continue.",
                ToolSchema.Object(
                    new[] { "path" },
                    path,
                    ToolSchema.Integer("startLine", "First line, 1-based. Default 1."),
                    ToolSchema.Integer("endLine", "Last line, inclusive. Default last line."),
                    ToolSchema.Boolean("lineNumbers", "Prefix each line with its number.")),
                Read);

            yield return ToolDefinition.Mutating(
                "vs_write_document",
                "Edits a file in the editor (opens it if needed; one undo step). mode: replace_all (default; creates the file if it does not exist), replace_lines (startLine..endLine, a trailing newline in text is ignored), insert (before startLine; a startLine past the end appends). Not saved unless save=true.",
                ToolSchema.Object(
                    new[] { "path", "text" },
                    path,
                    ToolSchema.String("text", "New text. Empty text with replace_lines deletes the lines."),
                    ToolSchema.String("mode", "Edit mode. Default replace_all.", "replace_all", "replace_lines", "insert"),
                    ToolSchema.Integer("startLine", "First line, 1-based (replace_lines, insert)."),
                    ToolSchema.Integer("endLine", "Last line, inclusive (replace_lines). Default startLine."),
                    ToolSchema.Boolean("save", "Save the document after editing. Default false.")),
                Write);

            yield return ToolDefinition.Mutating(
                "vs_open_document",
                "Opens a file in the editor, activates it, and optionally moves the caret.",
                ToolSchema.Object(
                    new[] { "path" },
                    path,
                    ToolSchema.Integer("line", "Line to move the caret to, 1-based."),
                    ToolSchema.Integer("column", "Column, 1-based. Default 1.")),
                Open);

            yield return ToolDefinition.Mutating(
                "vs_close_document",
                "Closes an open document. Saves it first unless save=false (then unsaved changes are discarded).",
                ToolSchema.Object(new[] { "path" }, path, ToolSchema.Boolean("save", "Save before closing. Default true.")),
                Close);

            yield return ToolDefinition.Mutating(
                "vs_save",
                "Saves one document (path), or everything (documents, projects, solution) when path is omitted.",
                ToolSchema.Object(path),
                Save);
        }

        // ---------------------------------------------------------------- Tools

        private object Documents()
        {
            string active = ActiveDocumentPath();
            return OpenDocuments().Select(document => DescribeDocument(document, active)).ToList();
        }

        private object Selection()
        {
            dynamic dte = session.Dte;
            object active = Com.Try(() => (object)dte.ActiveDocument);
            if (active == null)
            {
                return new Dictionary<string, object> { { "activeDocument", null } };
            }

            dynamic textDocument = TextDocumentOf(active);
            dynamic selection = Com.Run(() => textDocument.Selection);
            string text = Com.Try(() => (string)selection.Text) ?? string.Empty;

            return new Dictionary<string, object>
            {
                { "activeDocument", DocumentPath(active) },
                { "startLine", Com.Try(() => (int?)selection.TopPoint.Line) },
                { "startColumn", Com.Try(() => (int?)selection.TopPoint.LineCharOffset) },
                { "endLine", Com.Try(() => (int?)selection.BottomPoint.Line) },
                { "endColumn", Com.Try(() => (int?)selection.BottomPoint.LineCharOffset) },
                { "isEmpty", text.Length == 0 },
                { "text", text.Length > MaxSelectionChars ? text.Substring(0, MaxSelectionChars) + "...(truncated)" : text }
            };
        }

        private object Read(ToolArguments args)
        {
            string path = args.RequireString("path");
            object document = FindOpenDocument(path);

            string text = document != null ? EditorText(document) : File.ReadAllText(path);
            string[] lines = SplitLines(text);

            int startLine = Math.Max(1, args.GetInt("startLine", 1));
            int endLine = Math.Min(lines.Length, args.GetInt("endLine", lines.Length));
            bool numbered = args.GetBool("lineNumbers", false);

            List<string> included = TakeWithinBudget(
                lines.Skip(startLine - 1).Take(Math.Max(0, endLine - startLine + 1)).Select((line, offset) => numbered ? (startLine + offset) + ": " + line : line));
            int lastIncluded = startLine + included.Count - 1;

            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "path", path },
                { "source", document != null ? "editor" : "disk" },
                { "totalLines", lines.Length },
                { "startLine", startLine },
                { "endLine", lastIncluded },
                { "text", string.Join("\n", included) }
            };

            if (lastIncluded < endLine)
            {
                result["nextLine"] = lastIncluded + 1;
            }

            return result;
        }

        private object Write(ToolArguments args)
        {
            string path = args.RequireString("path");
            string text = args.Has("text") ? args.GetString("text") : throw new ArgumentException("Missing required argument: text");
            string mode = args.GetString("mode") ?? "replace_all";

            if (!File.Exists(path) && FindOpenDocument(path) == null)
            {
                return CreateFile(path, text, mode);
            }

            object document = OpenDocument(path);
            dynamic textDocument = TextDocumentOf(document);

            WithUndoGroup("Claude: edit " + Path.GetFileName(path), () => ApplyEdit(textDocument, mode, text, args));

            if (args.GetBool("save", false))
            {
                dynamic typed = document;
                Com.Do(() => typed.Save());
            }

            return DescribeDocument(document, ActiveDocumentPath());
        }

        private object Open(ToolArguments args)
        {
            string path = args.RequireString("path");
            object document = OpenDocument(path);
            dynamic typed = document;
            Com.Do(() => typed.Activate());

            if (args.Has("line"))
            {
                dynamic textDocument = TextDocumentOf(document);
                int line = args.GetInt("line", 1);
                int column = args.GetInt("column", 1);
                Com.Do(() => textDocument.Selection.MoveToLineAndOffset(line, column, false));
            }

            return DescribeDocument(document, ActiveDocumentPath());
        }

        private object Close(ToolArguments args)
        {
            string path = args.RequireString("path");
            dynamic document = FindOpenDocument(path) ?? throw new InvalidOperationException("Document is not open: " + path);
            int saveChanges = args.GetBool("save", true) ? SaveChangesYes : SaveChangesNo;
            Com.Do(() => document.Close(saveChanges));
            return new Dictionary<string, object> { { "closed", path } };
        }

        private object Save(ToolArguments args)
        {
            if (args.Has("path"))
            {
                dynamic document = FindOpenDocument(args.GetString("path")) ?? throw new InvalidOperationException("Document is not open: " + args.GetString("path"));
                Com.Do(() => document.Save());
            }
            else
            {
                dynamic dte = session.Dte;
                Com.Do(() => dte.ExecuteCommand("File.SaveAll"));
            }

            return Documents();
        }

        // ---------------------------------------------------------------- Editing

        private static void ApplyEdit(dynamic textDocument, string mode, string text, ToolArguments args)
        {
            switch (mode)
            {
                case "replace_all":
                    Replace(textDocument, textDocument.StartPoint, textDocument.EndPoint, text);
                    break;
                case "replace_lines":
                    ReplaceLines(textDocument, args.RequireInt("startLine"), args.GetInt("endLine", args.RequireInt("startLine")), text);
                    break;
                case "insert":
                    Insert(textDocument, args.RequireInt("startLine"), text);
                    break;
                default:
                    throw new ArgumentException("Unknown write mode: " + mode);
            }
        }

        private static void ReplaceLines(dynamic textDocument, int startLine, int endLine, string text)
        {
            int lineCount = Com.Run(() => (int)textDocument.EndPoint.Line);
            if (startLine < 1 || endLine < startLine || endLine > lineCount)
            {
                throw new ArgumentException("Line range " + startLine + ".." + endLine + " is outside 1.." + lineCount);
            }

            dynamic from = Com.Run(() => textDocument.StartPoint.CreateEditPoint());
            Com.Do(() => from.MoveToLineAndOffset(startLine, 1));

            dynamic to = Com.Run(() => textDocument.StartPoint.CreateEditPoint());
            Com.Do(() =>
            {
                to.MoveToLineAndOffset(endLine, 1);
                to.EndOfLine();
            });

            Replace(textDocument, from, to, WithoutOneTrailingNewline(text));
        }

        private static void Insert(dynamic textDocument, int beforeLine, string text)
        {
            int lineCount = Com.Run(() => (int)textDocument.EndPoint.Line);
            if (beforeLine < 1)
            {
                throw new ArgumentException("startLine must be 1 or greater.");
            }

            dynamic at = Com.Run(() => textDocument.StartPoint.CreateEditPoint());
            dynamic same = Com.Run(() => textDocument.StartPoint.CreateEditPoint());

            if (beforeLine > lineCount)
            {
                Com.Do(() =>
                {
                    at.MoveToPoint(textDocument.EndPoint);
                    same.MoveToPoint(textDocument.EndPoint);
                });
                bool endsMidLine = Com.Run(() => (int)at.LineCharOffset) > 1;
                Replace(textDocument, at, same, endsMidLine ? "\n" + text : text);
                return;
            }

            Com.Do(() =>
            {
                at.MoveToLineAndOffset(beforeLine, 1);
                same.MoveToLineAndOffset(beforeLine, 1);
            });
            Replace(textDocument, at, same, text.EndsWith("\n", StringComparison.Ordinal) ? text : text + "\n");
        }

        private static void Replace(dynamic textDocument, dynamic from, dynamic to, string text)
        {
            dynamic start = Com.Run(() => from.CreateEditPoint());
            Com.Do(() => start.ReplaceText(to, text, NormalizeNewlines));
        }

        private void WithUndoGroup(string name, Action edit)
        {
            dynamic dte = session.Dte;
            bool opened = Com.Try(() =>
            {
                dte.UndoContext.Open(name);
                return true;
            });

            try
            {
                edit();
            }
            finally
            {
                if (opened)
                {
                    Com.Try(() =>
                    {
                        dte.UndoContext.Close();
                        return true;
                    });
                }
            }
        }

        private static object CreateFile(string path, string text, string mode)
        {
            if (mode != "replace_all")
            {
                throw new FileNotFoundException("File does not exist (only replace_all creates files): " + path);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, text, new UTF8Encoding(true));
            return new Dictionary<string, object> { { "path", path }, { "created", true }, { "note", "Created on disk. Add it to a project with vs_project_item_add if needed." } };
        }

        // ---------------------------------------------------------------- Helpers

        private IEnumerable<object> OpenDocuments()
        {
            return Com.Run(() => ((IEnumerable)session.Dte.Documents).Cast<object>().ToList());
        }

        private object FindOpenDocument(string path)
        {
            return OpenDocuments().FirstOrDefault(document => SamePath(DocumentPath(document), path));
        }

        private object OpenDocument(string path)
        {
            object existing = FindOpenDocument(path);
            if (existing != null)
            {
                return existing;
            }

            dynamic dte = session.Dte;
            dynamic window = Com.Run(() => dte.ItemOperations.OpenFile(path));
            return Com.Run(() => (object)window.Document);
        }

        private string ActiveDocumentPath()
        {
            dynamic dte = session.Dte;
            object active = Com.Try(() => (object)dte.ActiveDocument);
            return active == null ? null : DocumentPath(active);
        }

        private static string DocumentPath(object document)
        {
            dynamic typed = document;
            return Com.Try(() => (string)typed.FullName);
        }

        private static dynamic TextDocumentOf(object document)
        {
            dynamic typed = document;
            object textDocument = Com.Run(() => (object)typed.Object("TextDocument"));
            return textDocument ?? throw new InvalidOperationException("Not a text document: " + DocumentPath(document));
        }

        private static string EditorText(object document)
        {
            dynamic textDocument = TextDocumentOf(document);
            return Com.Run(() => (string)textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint));
        }

        private static Dictionary<string, object> DescribeDocument(object document, string activePath)
        {
            dynamic typed = document;
            string path = DocumentPath(document);

            return new Dictionary<string, object>
            {
                { "path", path },
                { "saved", Com.Try(() => (bool?)typed.Saved) },
                { "language", Com.Try(() => (string)typed.Language) },
                { "readOnly", Com.Try(() => (bool?)typed.ReadOnly) },
                { "active", SamePath(path, activePath) }
            };
        }

        private static bool SamePath(string left, string right)
        {
            return left != null && right != null
                && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string[] SplitLines(string text)
        {
            return text.Replace("\r\n", "\n").Split('\n');
        }

        private static string WithoutOneTrailingNewline(string text)
        {
            return text.EndsWith("\r\n", StringComparison.Ordinal) ? text.Substring(0, text.Length - 2)
                : text.EndsWith("\n", StringComparison.Ordinal) ? text.Substring(0, text.Length - 1)
                : text;
        }

        private static List<string> TakeWithinBudget(IEnumerable<string> lines)
        {
            List<string> included = new List<string>();
            int used = 0;

            foreach (string line in lines)
            {
                if (used + line.Length > MaxDocumentChars && included.Count > 0)
                {
                    break;
                }

                included.Add(line);
                used += line.Length + 1;
            }

            return included;
        }
    }
}
