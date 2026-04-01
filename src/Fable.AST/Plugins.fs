namespace Fable

open Fable.AST
open Fable.AST.Fable

// ──────────────────────────────────────────────────────────────────────────────
// Fundamental enums — must live here so ICompilerCore can reference them without
// depending on the Fable.Transforms assembly.
// ──────────────────────────────────────────────────────────────────────────────

[<RequireQualifiedAccess>]
type Verbosity =
    | Normal
    | Verbose
    | Silent

/// Diagnostic severity, moved from Compiler.fs so ICompilerCore.AddLog can use it
/// without a circular assembly dependency.
[<RequireQualifiedAccess>]
type Severity =
    | Warning
    | Error
    | Info

type Language =
#if !FABLE_CORE_ONLY
    | JavaScript
    | TypeScript
    | Python
    | Php
    | Dart
    | Rust
    | Beam
#endif
    | WasmGc

    override this.ToString() =
        match this with
#if !FABLE_CORE_ONLY
        | JavaScript -> "JavaScript"
        | TypeScript -> "TypeScript"
        | Python     -> "Python"
        | Php        -> "PHP"
        | Dart       -> "Dart"
        | Rust       -> "Rust"
        | Beam       -> "Beam"
#endif
        | WasmGc     -> "WasmGc"

type CompilerOptions =
    {
        TypedArrays: bool
        ClampByteArrays: bool
        Language: Language
        Define: string list
        DebugMode: bool
        OptimizeFSharpAst: bool
        Verbosity: Verbosity
        FileExtension: string
        TriggeredByDependency: bool
        NoReflection: bool
        JsTemporal: bool
    }

// ──────────────────────────────────────────────────────────────────────────────
// Plugin infrastructure
// ──────────────────────────────────────────────────────────────────────────────

/// Plugin dispatch table, keyed by EntityRef (attribute type).
/// Moved from Compiler.fs so ICompilerCore.Plugins can reference it from Fable.AST.
type CompilerPlugins = { MemberDeclarationPlugins: Map<EntityRef, System.Type> }

type PluginHelper =
    abstract LibraryDir  : string
    abstract CurrentFile : string
    abstract OutputDir   : string option
    abstract ProjectFile : string
    abstract SourceFiles : string seq
    abstract Options     : CompilerOptions
    abstract LogWarning  : string * ?range: SourceLocation -> unit
    abstract LogError    : string * ?range: SourceLocation -> unit
    abstract GetRootModule: fileName: string -> string
    abstract GetEntity  : EntityRef -> Entity
    abstract GetMember  : MemberRef -> MemberFunctionOrValue
    abstract GetOutputPath: unit -> string
    abstract GetOutputPath: file: string -> string

[<System.AttributeUsage(System.AttributeTargets.Assembly)>]
type ScanForPluginsAttribute() =
    inherit System.Attribute()

[<AbstractClass>]
type PluginAttribute() =
    inherit System.Attribute()
    abstract FableMinimumVersion: string

[<AbstractClass>]
type MemberDeclarationPluginAttribute() =
    inherit PluginAttribute()
    abstract Transform    : PluginHelper * File * MemberDecl -> MemberDecl
    abstract TransformCall: PluginHelper * member_: MemberFunctionOrValue * expr: Expr -> Expr

// ──────────────────────────────────────────────────────────────────────────────
// ICompilerCore — minimal, platform-agnostic compiler interface.
//
// Lives in Fable.AST (no FCS dependency) so every backend can depend on it
// without dragging in FSharp.Compiler.Service.
//
// Suitable for Fable 5 upstream PR: Dart, LLVM, WasmGC backends implement this
// without needing to satisfy the full Fable.Compiler interface.
// ──────────────────────────────────────────────────────────────────────────────

type ICompilerCore =
    /// Full compiler options (language, debug mode, typed arrays, …).
    abstract Options: CompilerOptions

    /// Absolute path of the source file currently being compiled.
    abstract CurrentFile: string

    /// Absolute path of the Fable library directory (fable-library or equivalent).
    abstract LibraryDir: string

    /// Try to resolve a Fable entity ref to its metadata.
    /// Returns None for entities not reachable from this compilation unit.
    abstract TryGetEntity: Fable.EntityRef -> Fable.Entity option

    /// Plugin dispatch table. Backends with no plugin support return
    /// { MemberDeclarationPlugins = Map.empty }.
    abstract Plugins: CompilerPlugins

    /// Returns a PluginHelper for executing member-declaration plugins, or None
    /// when plugins are not supported (e.g. WasmGC backend with empty plugin map).
    /// Only called by ApplyPlugin when Plugins.MemberDeclarationPlugins is non-empty.
    abstract TryGetPluginHelper: unit -> PluginHelper option

    /// Monotonically-increasing counter for generating unique identifier suffixes.
    abstract IncrementCounter: unit -> int

    /// Emit a diagnostic message. Backends must implement at minimum Error severity.
    abstract AddLog:
        msg      : string       *
        severity : Severity     *
        ?range   : SourceLocation *
        ?fileName: string       *
        ?tag     : string -> unit
