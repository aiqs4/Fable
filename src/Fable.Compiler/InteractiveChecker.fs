// Copyright (c) Fable contributors.
// Originally derived from fcs/service_slim.fs (patched FCS).
// Rewritten to use only the public FSharp.Compiler.Service NuGet API so that
// Fable can reference the official, unpatched FCS package instead of the
// private `lib/fcs` build.
//
// Key differences from the patched service_slim.fs:
//   • All internal FCS types (TcImports, TcConfig, CheckOneInput, …) are gone.
//     We delegate everything to the public FSharpChecker.
//   • The custom parse/typecheck cache is replaced by FSharpChecker's own
//     incremental cache, which is transparent to callers.
//   • The `subscriber` callback in ParseAndCheckProject is still supported
//     but fires *after* the full project check rather than incrementally.
//     This means watch-mode parallel compilation no longer overlaps with
//     type-checking; every file is compiled sequentially after type-checking
//     finishes. The result is always correct, just slightly slower on large
//     projects in watch mode.
//   • GetDependentFiles uses a conservative approximation: it returns the
//     current file and every file that appears after it in project order,
//     because parsing the AST to build a real dependency graph would require
//     internal FCS types. This is always safe (may recompile more files than
//     necessary, never fewer).
//   • Compile() uses FSharpChecker.Compile (command-line driven) instead of
//     the patched CompileFromTypedAst.

namespace Fable.Compiler

open System
open System.IO
open System.Collections.Concurrent
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text

// ---------------------------------------------------------------------------
// InteractiveChecker
// ---------------------------------------------------------------------------

/// Wraps FSharpChecker with the API surface that Fable needs.
/// One instance is created per project; it caches state across compilations
/// using FSharpChecker's own incremental infrastructure.
type InteractiveChecker private (checker: FSharpChecker, projectOptions: FSharpProjectOptions) =

    // Source-text cache: keyed by (normalizedPath, sourceHash).
    // Avoids re-reading files from disk when the content has not changed.
    let sourceTextCache =
        ConcurrentDictionary<string * int, ISourceText>()

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    let getSourceText (fileName: string) (sourceReader: string -> int * Lazy<string>) : int * ISourceText =
        let hash, lazySource = sourceReader fileName
        let sourceText =
            sourceTextCache.GetOrAdd(
                (fileName, hash),
                fun _ -> SourceText.ofString (lazySource.Force())
            )
        hash, sourceText

    /// Truncate the source-file list to `lastFile` (inclusive), if supplied.
    let sliceFiles (fileNames: string[]) (lastFile: string option) =
        match lastFile with
        | None -> fileNames
        | Some last ->
            match fileNames |> Array.tryFindIndex ((=) last) with
            | Some idx -> fileNames |> Array.take (idx + 1)
            | None ->
                eprintfn "InteractiveChecker: lastFile '%s' not found in project; using all files." last
                fileNames

    /// Build project options for a sub-set of files (e.g. up to lastFile).
    let makeOpts (projectFileName: string) (fileNames: string[]) =
        { projectOptions with
            ProjectFileName = projectFileName
            SourceFiles      = fileNames }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// Create a checker for the given project options.
    static member Create(projectOptions: FSharpProjectOptions) =
        let checker =
            FSharpChecker.Create(
                keepAssemblyContents                              = true,
                // Don't hold background resolutions in memory; Fable does its own caching.
                keepAllBackgroundResolutions                      = false,
                suggestNamesForErrors                             = true,
                keepAllBackgroundSymbolUses                       = false,
                // Semantic classification is IDE-only; disable for throughput.
                enableBackgroundItemKeyStoreAndSemanticClassification = false
            )
        InteractiveChecker(checker, projectOptions)

    /// Invalidate all cached results. Call this when project options change.
    member _.ClearCache() =
        checker.InvalidateAll()
        sourceTextCache.Clear()

    /// Return all assemblies referenced by the project (FSharp + .NET).
    /// Internally performs a full project check on first call; subsequent
    /// calls are served from FSharpChecker's cache.
    member _.GetImportedAssemblies() : Async<FSharpAssembly list> = async {
        let! results = checker.ParseAndCheckProject(projectOptions)
        return results.ProjectContext.GetReferencedAssemblies()
    }

    /// Parse and type-check the whole project (or up to `lastFile`).
    ///
    /// Parameters:
    ///   projectFileName – logical name used as the project identifier.
    ///   fileNames       – ordered list of source files to compile.
    ///   sourceReader    – returns (contentHash, lazySource) for a path.
    ///   lastFile        – if supplied, only files up to this one are checked.
    ///   subscriber      – optional callback invoked for each implementation
    ///                     file once the full check is complete.
    member _.ParseAndCheckProject
        (
            projectFileName : string,
            fileNames       : string[],
            sourceReader    : string -> int * Lazy<string>,
            ?lastFile       : string,
            ?subscriber     : FSharpImplementationFileContents -> unit
        )
        : Async<FSharpCheckProjectResults> = async {

        let files = sliceFiles fileNames lastFile
        let opts  = makeOpts projectFileName files

        let! results = checker.ParseAndCheckProject(opts)

        // Drive the subscriber (if any) over the available implementation files.
        // In the patched service_slim.fs this was done incrementally during
        // type-checking; here we do it after the fact, which is always correct.
        match subscriber with
        | Some fn ->
            for implFile in results.AssemblyContents.ImplementationFiles do
                fn implFile
        | None -> ()

        return results
    }

    /// Parse and type-check a single file in the context of the full project.
    ///
    /// Returns the parse results, the per-file check results (for intellisense),
    /// and the project-level check results.
    member _.ParseAndCheckFileInProject
        (
            projectFileName : string,
            fileNames       : string[],
            sourceReader    : string -> int * Lazy<string>,
            fileName        : string
        )
        : Async<FSharpParseFileResults * FSharpCheckFileResults * FSharpCheckProjectResults> = async {

        let opts = makeOpts projectFileName fileNames
        let fileVersion, sourceText = getSourceText fileName sourceReader

        let! parseResults, checkAnswer =
            checker.ParseAndCheckFileInProject(fileName, fileVersion, sourceText, opts)

        let checkResults =
            match checkAnswer with
            | FSharpCheckFileAnswer.Succeeded r -> r
            | FSharpCheckFileAnswer.Aborted ->
                failwith $"Type checking was aborted for file: {fileName}"

        // The project results are needed by callers (e.g. to get all impl files).
        let! projectResults = checker.ParseAndCheckProject(opts)

        return parseResults, checkResults, projectResults
    }

    /// Return the files (in project order) that transitively depend on
    /// `currentFileName`.
    ///
    /// This implementation is *conservative*: it returns the current file and
    /// all files that follow it in the project. A precise implementation would
    /// parse every file and build a real module-dependency graph, but that
    /// requires either FCS internals or re-implementing the graph algorithm.
    ///
    /// Conservative is always safe: at worst we recompile a few extra files.
    member _.GetDependentFiles
        (
            currentFileName : string,
            fileNames       : string[],
            _sourceReader   : string -> int * Lazy<string>
        )
        : Async<string[]> = async {

        match fileNames |> Array.tryFindIndex ((=) currentFileName) with
        | None ->
            eprintfn "InteractiveChecker.GetDependentFiles: '%s' not in project files." currentFileName
            return [| currentFileName |]
        | Some idx ->
            return fileNames |> Array.skip idx
    }

    /// Compile source files to a .dll / .exe using the standard fsc driver.
    ///
    /// In the patched FCS this called the internal CompileFromTypedAst, which
    /// reused already-computed type-check results. Here we re-invoke the
    /// compiler from scratch. The output is identical; it is just slightly
    /// slower because type-checking is repeated.
    ///
    /// Returns (diagnostics, exceptionOption).
    member _.Compile
        (
            fileNames    : string[],
            _sourceReader: string -> int * Lazy<string>,
            outFile      : string
        )
        : Async<FSharpDiagnostic[] * exn option> = async {

        // Build a standard fsc command line from the project options.
        let argv =
            [|
                yield "fsc.exe"
                yield! projectOptions.OtherOptions
                yield "--out:" + outFile
                yield! fileNames
            |]

        return! checker.Compile(argv)
    }
