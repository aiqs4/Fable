module Fable.Transforms.FableTransforms

open Fable
open Fable.AST.Fable

val isIdentCaptured: identName: string -> expr: Expr -> bool
val getCapturedNames: expr: Expr -> string list
val isTailRecursive: identName: string -> expr: Expr -> bool * bool
val countReferencesUntil: limit: int -> identName: string -> body: Expr -> int
val replaceValues: replacements: Map<string, Expr> -> expr: Expr -> Expr
val uncurryType: typ: Type -> Type

val getTransformations: _com: ICompilerCore -> (#ICompilerCore -> Expr -> Expr) list

/// Backend-agnostic transform list for languages whose function values
/// need no runtime currying (closures already callable at full arity —
/// e.g. WasmGC). Omits uncurrySendingArgs, the one JS/TS/Dart/Python-
/// biased pass in the standard pipeline, and finishes with
/// eraseCurryMarkers so no Extended(Curry(...)) marker reaches the
/// backend's expression transform.
val getUncurryOnlyTransformations: com: ICompilerCore -> (ICompilerCore -> Expr -> Expr) list

val transformDeclaration:
    transformations: (ICompilerCore -> Expr -> Expr) list -> com: ICompilerCore -> file: File -> decl: Declaration -> Declaration

val transformFile: com: ICompilerCore -> file: File -> File
