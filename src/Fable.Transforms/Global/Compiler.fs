namespace Fable

module Literals =
    [<Literal>]
    let VERSION = "5.20.0"

#if !FABLE_CORE_ONLY
    [<Literal>]
    let JS_LIBRARY_VERSION = "2.8.0"
#endif

type CompilerOptionsHelper =
    static member Make
        (
            ?language,
            ?typedArrays,
            ?define,
            ?debugMode,
            ?optimizeFSharpAst,
            ?verbosity,
            ?fileExtension,
            ?clampByteArrays,
            ?noReflection,
            ?jsTemporal
        )
        =
#if !FABLE_CORE_ONLY
        let language = defaultArg language JavaScript
#else
        let language = defaultArg language WasmGc
#endif
        {
            CompilerOptions.Define = defaultArg define []
            DebugMode = defaultArg debugMode true
            Language = language
            FileExtension = defaultArg fileExtension ".fs.js"
            // JS typed arrays (Int32Array, ...) don't exist on WasmGc: Replacements injects them as global idents.
            TypedArrays = defaultArg typedArrays (language <> WasmGc)
            OptimizeFSharpAst = defaultArg optimizeFSharpAst false
            Verbosity = defaultArg verbosity Verbosity.Normal
            ClampByteArrays = defaultArg clampByteArrays false
            NoReflection = defaultArg noReflection false
            TriggeredByDependency = false
            JsTemporal = defaultArg jsTemporal false
        }

// [<RequireQualifiedAccess>]
// [<Struct>]
// type Severity =
//     | Warning
//     | Error
//     | Info

[<RequireQualifiedAccess>]
[<Struct>]
type OutputType =
    | Library
    | Exe

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open Fable.AST

type InlineExpr =
    {
        Args: Fable.Ident list
        Body: Fable.Expr
        FileName: string
        GenericArgs: string list
        ScopeIdents: Set<string>
        IsFileLocal: bool
    }

type SourceReader = string -> int * Lazy<string>

// ──────────────────────────────────────────────────────────────────────────────
// InlineExprLazy  (uses Compiler, so lives alongside Compiler)
// ──────────────────────────────────────────────────────────────────────────────

// Forward declaration — Compiler is defined below.
type Compiler =
    // ── Members now inherited from ICompilerCore ──────────────────────────────
    inherit ICompilerCore
    // Options, CurrentFile, LibraryDir, TryGetEntity, Plugins,
    // TryGetPluginHelper, IncrementCounter, AddLog  ← all from ICompilerCore.

    // ── FCS-specific members ──────────────────────────────────────────────────
    abstract OutputDir: string option
    abstract OutputType: OutputType
    abstract ProjectFile: string
    abstract ProjectOptions: FSharpProjectOptions
    abstract SourceFiles: string array
    abstract IsPrecompilingInlineFunction: bool
    abstract WillPrecompileInlineFunction: file: string -> Compiler

    abstract GetImplementationFile: fileName: string -> FSharpImplementationFileDeclaration list
    abstract GetRootModule: fileName: string -> string * FSharpXmlDoc option
    abstract GetInlineExpr: string -> InlineExpr
    abstract AddWatchDependency: file: string -> unit

/// Extended platform-agnostic interface for F# symbol resolution via FCS.
/// Use in compiler-frontend code that needs inline-expr resolution and entity
/// metadata but wants to remain decoupled from the full Compiler interface.
type ISharpCompilerProvider =
    inherit ICompilerCore
    abstract GetRootModule : fileName: string -> string * FSharpXmlDoc option
    abstract GetInlineExpr : memberUniqueName: string -> InlineExpr

/// Lazily-computed inline expression.  Cached after first calculation.
type InlineExprLazy(f: Compiler -> InlineExpr) =
    let mutable value: InlineExpr voption = ValueNone

    member _.Calculate(com: Compiler) =
        lock f <| fun () ->
            match value with
            | ValueSome v -> v
            | ValueNone   ->
                let v = f com
                value <- ValueSome v
                v

// ──────────────────────────────────────────────────────────────────────────────
// Extension methods
// ──────────────────────────────────────────────────────────────────────────────

[<AutoOpen>]
module CompilerExt =

    let expectedVersionMatchesActual (expected: string) (actual: string) =
        try
            let r = System.Text.RegularExpressions.Regex(@"^(\d+)\.(\d+)(?:\.(\d+))?")

            let parse v =
                let m = r.Match(v)

                int m.Groups[1].Value,
                int m.Groups[2].Value,
                (if m.Groups[3].Success then int m.Groups[3].Value else 0)
            let aM, am, ap = parse actual
            let eM, em, ep = parse expected
            aM > eM || (aM = eM && (am > em || (am = em && ap >= ep)))
        with _ -> false

    let private coreAssemblyNames = set Metadata.coreAssemblies

#if !FABLE_CORE_ONLY
    let mutable private _language   = JavaScript
#else
    // TODO: instead of having a default language, we should make it mandatory to set it at the start of the program
    let mutable private _language   = WasmGc
#endif
    let mutable private _checkNulls = false

    // ── ICompilerCore extensions ──────────────────────────────────────────────
    // These only need TryGetEntity, Plugins, and TryGetPluginHelper — all on
    // ICompilerCore — so they work for every backend, not just FCS-backed ones.

    type ICompilerCore with

        member com.GetEntity(entityRef: Fable.EntityRef) =
            match com.TryGetEntity(entityRef) with
            | Some e -> e
            | None   ->
                let category =
                    match entityRef.Path with
                    | Fable.CoreAssemblyName _ -> "core"
                    | Fable.AssemblyPath _     -> "external"
                    | Fable.PrecompiledLib _   -> "precompiled"
                    | Fable.SourcePath _       -> "user"

                failwith $"Cannot find %s{category} entity %s{entityRef.FullName}"

        member com.TryGetMember(memberRef: Fable.MemberRef) : Fable.MemberFunctionOrValue option =
            match memberRef with
            | Fable.GeneratedMemberRef gen -> Some(gen :> _)
            | Fable.MemberRef(declaringEntity, memberInfo) ->
                com.TryGetEntity(declaringEntity)
                |> Option.bind (fun ent -> ent.TryFindMember(memberInfo))

        member com.GetMember(memberRef: Fable.MemberRef) : Fable.MemberFunctionOrValue =
            match com.TryGetMember(memberRef) with
            | Some e -> e
            | None   -> failwith $"Cannot find member ref: %A{memberRef}"

        /// Execute attribute plugins over `input`.
        /// Short-circuits immediately if the plugin map is empty (WasmGC fast path).
        /// When plugins are present, defers helper construction to TryGetPluginHelper.
        member com.ApplyPlugin<'Plugin, 'Input when 'Plugin :> PluginAttribute>
                (plugins: Map<_, _>, atts: Fable.Attribute seq, input: 'Input, transform) : 'Input =
            if Map.isEmpty plugins then input
            else
                match com.TryGetPluginHelper() with
                | None -> input  // backend has no plugin support — skip silently
                | Some helper ->
                    (input, Seq.rev atts)
                    ||> Seq.fold (fun acc att ->
                        match Map.tryFind att.Entity plugins with
                        | None -> acc
                        | Some plugin ->
                            let inst =
                                System.Activator.CreateInstance(plugin, List.toArray att.ConstructorArgs)
                                :?> 'Plugin
                            if not (expectedVersionMatchesActual inst.FableMinimumVersion Literals.VERSION) then
                                failwithf
                                    "Plugin %s expects v%s but running Fable v%s"
                                    plugin.FullName
                                    inst.FableMinimumVersion
                                    Literals.VERSION
                            transform inst helper acc)

        member com.ApplyMemberDeclarationPlugin(file: Fable.File, decl: Fable.MemberDecl) =
            match com.TryGetMember(decl.MemberRef) with
            | None      -> decl
            | Some memb ->
                com.ApplyPlugin<MemberDeclarationPluginAttribute, _>(
                    com.Plugins.MemberDeclarationPlugins,
                    memb.Attributes,
                    decl,
                    fun p h i -> p.Transform(h, file, i))

        member com.ApplyMemberCallPlugin(memb: Fable.MemberFunctionOrValue, expr: Fable.Expr) =
            com.ApplyPlugin<MemberDeclarationPluginAttribute, _>(
                com.Plugins.MemberDeclarationPlugins,
                memb.Attributes,
                expr,
                fun p h e -> p.TransformCall(h, memb, e))

    // ── Compiler (FCS-backed) extensions ─────────────────────────────────────

    type Compiler with

        static member CoreAssemblyNames = coreAssemblyNames
        static member Language  = _language
        static member CheckNulls = _checkNulls
        static member SetLanguageUnsafe  language = _language   <- language
        static member SetCheckNullsUnsafe enabled = _checkNulls <- enabled

        /// Build a PluginHelper that wraps this compiler.
        /// Called only when Plugins.MemberDeclarationPlugins is non-empty.
        member com.ToPluginHelper() =
            { new PluginHelper with
                member _.LibraryDir  = com.LibraryDir
                member _.CurrentFile = com.CurrentFile
                member _.OutputDir   = com.OutputDir
                member _.ProjectFile = com.ProjectFile
                member _.SourceFiles = com.SourceFiles
                member _.Options     = com.Options
                member _.GetRootModule(fileName) = com.GetRootModule(fileName) |> fst
                member _.GetEntity(ref) = com.GetEntity(ref)
                member _.GetMember(ref) = com.GetMember(ref)

                member _.LogWarning(msg, r) =
                    com.AddLog(msg, Severity.Warning, ?range = r, fileName = com.CurrentFile)

                member _.LogError(msg, r) =
                    com.AddLog(msg, Severity.Error, ?range = r, fileName = com.CurrentFile)

                member _.GetOutputPath(file) =
                    let file = Path.ChangeExtension(file, com.Options.FileExtension)

                    match com.OutputDir with
                    | None -> file
                    | Some outDir ->
                        // TODO: This is a simplified version of the actual mechanism and will not work with deduplicated paths
                        let projDir = Path.GetDirectoryName(com.ProjectFile)

                        let relPath = Path.getRelativeFileOrDirPath true projDir false file

                        let relPath =
                            if relPath.StartsWith("./", System.StringComparison.Ordinal) then relPath[2..]
                            else relPath
                        Path.Combine(outDir, relPath)
                member this.GetOutputPath() = this.GetOutputPath(com.CurrentFile) }
