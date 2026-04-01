module Fable.Transforms.Replacements.Api

#nowarn "1182"

open Fable
open Fable.AST
open Fable.AST.Fable
open Fable.Transforms

type ICompiler = FSharp2Fable.IFableCompiler

let curryExprAtRuntime (com: ICompilerCore) arity (expr: Expr) =
    match com.Options.Language with
    // | Rust -> Rust.Replacements.curryExprAtRuntime com arity expr
    // | Python -> Helper.LibCall(com, "Util", "curry", expr.Type, [makeIntConst arity; expr])
    | _ -> Util.curryExprAtRuntime com arity expr

let uncurryExprAtRuntime (com: ICompilerCore) arity (expr: Expr) =
    match com.Options.Language with
    // | Rust -> Rust.Replacements.uncurryExprAtRuntime com expr.Type arity expr
    // | Python -> Helper.LibCall(com, "Util", "uncurry", expr.Type, [makeIntConst arity; expr])
    | _ -> Util.uncurryExprAtRuntime com arity expr

let partialApplyAtRuntime (com: ICompilerCore) t arity (fn: Expr) (args: Expr list) =
    match com.Options.Language with
    // | Rust -> Rust.Replacements.partialApplyAtRuntime com t arity fn args
    // | Python ->
    //     let args = NewArray(ArrayValues args, Any, MutableArray) |> makeValue None
    //     Helper.LibCall(com, "Util", "partialApply", t, [makeIntConst arity; fn; args])
    | _ -> Util.partialApplyAtRuntime com t arity fn args

let tryField (com: ICompiler) returnTyp ownerTyp fieldName =
    match com.Options.Language with
#if !FABLE_CORE_ONLY
    | Rust -> Rust.Replacements.tryField com returnTyp ownerTyp fieldName
    | Python -> Py.Replacements.tryField com returnTyp ownerTyp fieldName
    | Dart -> Dart.Replacements.tryField com returnTyp ownerTyp fieldName
    | Beam -> Beam.Replacements.tryField com returnTyp ownerTyp fieldName
#endif
    | _ -> JS.Replacements.tryField com returnTyp ownerTyp fieldName

let tryBaseConstructor (com: ICompiler) ctx (ent: EntityRef) (argTypes: Lazy<Type list>) genArgs args =
    match com.Options.Language with
#if !FABLE_CORE_ONLY
    | Python -> Py.Replacements.tryBaseConstructor com ctx ent argTypes genArgs args
    | Dart -> Dart.Replacements.tryBaseConstructor com ctx ent argTypes genArgs args
    | Rust -> Rust.Replacements.tryBaseConstructor com ctx ent argTypes genArgs args
    | Beam -> Beam.Replacements.tryBaseConstructor com ctx ent argTypes genArgs args
#endif
    | _ -> JS.Replacements.tryBaseConstructor com ctx ent argTypes genArgs args

let makeMethodInfo (com: ICompiler) r (name: string) (parameters: (string * Type) list) (returnType: Type) =
    match com.Options.Language with
    | _ -> JS.Replacements.makeMethodInfo com r name parameters returnType

let tryType (com: ICompiler) (t: Type) =
    match com.Options.Language with
#if !FABLE_CORE_ONLY
    | Rust -> Rust.Replacements.tryType t
    | Python -> Py.Replacements.tryType t
    | Dart -> Dart.Replacements.tryType t
    | Beam -> Beam.Replacements.tryType t
#endif
    | _ -> JS.Replacements.tryType t

let tryCall (com: ICompiler) ctx r t info thisArg args =
    match com.Options.Language with
#if !FABLE_CORE_ONLY
    | Rust -> Rust.Replacements.tryCall com ctx r t info thisArg args
    | Python -> Py.Replacements.tryCall com ctx r t info thisArg args
    | Dart -> Dart.Replacements.tryCall com ctx r t info thisArg args
    | Beam -> Beam.Replacements.tryCall com ctx r t info thisArg args
#endif
    | _ -> JS.Replacements.tryCall com ctx r t info thisArg args

let error (com: ICompiler) msg =
    match com.Options.Language with
#if !FABLE_CORE_ONLY
    | Python -> Py.Replacements.error com msg
    | Rust -> Rust.Replacements.error com msg
    | Dart -> Dart.Replacements.error com msg
    | Beam -> Beam.Replacements.error com msg
#endif
    | _ -> JS.Replacements.error com msg

let defaultof (com: ICompiler) ctx r typ =
    match com.Options.Language with
#if !FABLE_CORE_ONLY
    | Rust -> Rust.Replacements.getZero com ctx typ
    | Python -> Py.Replacements.defaultof com ctx r typ
    | Dart -> Dart.Replacements.getZero com ctx typ
    | Beam -> Beam.Replacements.defaultof com ctx r typ
#endif
    | _ -> JS.Replacements.defaultof com ctx r typ

let createMutablePublicValue (com: ICompiler) value =
    match com.Options.Language with
#if !FABLE_CORE_ONLY
    | Python -> Py.Replacements.createAtom com value
    | JavaScript
    | TypeScript -> JS.Replacements.createAtom com value
    | Rust
    | Php
    | Dart
    | Beam
#endif
    | WasmGc -> value

let getRefCell (com: ICompiler) r typ (expr: Expr) =
    match com.Options.Language with
#if !FABLE_CORE_ONLY
    | Python -> Py.Replacements.getRefCell com r typ expr
    | Rust -> Rust.Replacements.getRefCell com r typ expr
    | Dart -> Dart.Replacements.getRefCell com r typ expr
    | Beam -> Beam.Replacements.getRefCell com r typ expr
#endif
    | _ -> JS.Replacements.getRefCell com r typ expr

let setRefCell (com: ICompiler) r (expr: Expr) (value: Expr) =
    match com.Options.Language with
#if !FABLE_CORE_ONLY
    | Python -> Py.Replacements.setRefCell com r expr value
    | Rust -> Rust.Replacements.setRefCell com r expr value
    | Dart -> Dart.Replacements.setRefCell com r expr value
    | Beam -> Beam.Replacements.setRefCell com r expr value
#endif
    | _ -> JS.Replacements.setRefCell com r expr value

let makeRefCellFromValue (com: ICompiler) r (value: Expr) =
    match com.Options.Language with
#if !FABLE_CORE_ONLY
    | Python -> Py.Replacements.makeRefCellFromValue com r value
    | Rust -> Rust.Replacements.makeRefCellFromValue com r value
    | Dart -> Dart.Replacements.makeRefCellFromValue com r value
    | Beam -> Beam.Replacements.makeRefCellFromValue com r value
#endif
    | _ -> JS.Replacements.makeRefCellFromValue com r value

let makeRefFromMutableFunc (com: ICompiler) ctx r t (value: Expr) =
    match com.Options.Language with
#if !FABLE_CORE_ONLY
    | Python -> Py.Replacements.makeRefFromMutableFunc com ctx r t value
    | Rust -> Rust.Replacements.makeRefFromMutableFunc com ctx r t value
    | Dart -> Dart.Replacements.makeRefFromMutableFunc com ctx r t value
    | Beam -> Beam.Replacements.makeRefFromMutableFunc com ctx r t value
#endif
    | _ -> JS.Replacements.makeRefFromMutableFunc com ctx r t value

let makeRefFromMutableValue (com: ICompiler) ctx r t (value: Expr) =
    match com.Options.Language with
#if !FABLE_CORE_ONLY
    | Python -> Py.Replacements.makeRefFromMutableValue com ctx r t value
    | Rust -> Rust.Replacements.makeRefFromMutableValue com ctx r t value
    | Dart -> Dart.Replacements.makeRefFromMutableValue com ctx r t value
    | Beam -> Beam.Replacements.makeRefFromMutableValue com ctx r t value
#endif
    | _ -> JS.Replacements.makeRefFromMutableValue com ctx r t value

let makeRefFromMutableField (com: ICompiler) ctx r t (value: Expr) =
    match com.Options.Language with
#if !FABLE_CORE_ONLY
    | Python -> Py.Replacements.makeRefFromMutableField com ctx r t value
    | Rust -> Rust.Replacements.makeRefFromMutableField com ctx r t value
    | Dart -> Dart.Replacements.makeRefFromMutableField com ctx r t value
    | Beam -> Beam.Replacements.makeRefFromMutableField com ctx r t value
#endif
    | _ -> JS.Replacements.makeRefFromMutableField com ctx r t value
